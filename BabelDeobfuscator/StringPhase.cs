using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BabelDeobfuscator
{
    internal class StringPhase : IDeobfuscatePhase
    {
        int decryptedStringCount;

        List<IMemberDef> obfuscatorGeneratedMembers = new List<IMemberDef>();

        public void Run(ModuleDefMD module, Assembly assembly)
        {
            DecryptString(module, assembly);
            RemoveObfuscatorGeneratedMembers(module);
        }

        private void RemoveObfuscatorGeneratedMembers(ModuleDefMD module)
        {
            foreach (MethodDef method in obfuscatorGeneratedMembers.OfType<MethodDef>())
            {
                bool isDelete = true;
                foreach (TypeDef type in module.GetTypes())
                {
                    foreach (MethodDef methodDef in type.Methods)
                    {
                        if (methodDef == method)
                            continue;
                        if (!method.HasBody || !method.Body.HasInstructions)
                            continue;
                        foreach (Instruction instruction in method.Body.Instructions.Where(i => i.OpCode.OperandType == OperandType.InlineMethod || i.OpCode.OperandType == OperandType.InlineTok))
                        {
                            if (instruction.Operand == method)
                            {
                                isDelete = false;
                                break;
                            }
                        }
                        if (!isDelete)
                            break;
                    }
                    if (!isDelete)
                        break;
                }
                if (isDelete)
                {
                    Logger.LogVerbose($"Deleting obfuscator generated method: {method.FullName} [0x{method.MDToken}]...");
                    method.DeclaringType.Methods.Remove(method);
                }
            }
            foreach (TypeDef type in obfuscatorGeneratedMembers.OfType<TypeDef>())
            {
                bool isDelete = true;
                foreach (MethodDef method in type.Methods)
                {
                    foreach (TypeDef typeDef in module.GetTypes())
                    {
                        if (typeDef == type)
                            continue;
                        foreach (MethodDef methodDef in type.Methods)
                        {
                            if (!method.HasBody || !method.Body.HasInstructions)
                                continue;
                            foreach (Instruction instruction in method.Body.Instructions.Where(i => i.OpCode.OperandType == OperandType.InlineMethod || i.OpCode.OperandType == OperandType.InlineTok))
                            {
                                if (instruction.Operand == method)
                                {
                                    isDelete = false;
                                    break;
                                }
                            }
                            if (!isDelete)
                                break;
                        }
                        if (!isDelete)
                            break;
                    }
                    if (!isDelete)
                        break;
                }
                if (isDelete)
                {
                    Logger.LogVerbose($"Deleting obfuscator generated type: {type.FullName} [0x{type.MDToken}]...");
                    if (type.IsNested)
                        type.DeclaringType.NestedTypes.Remove(type);
                    else
                        module.Types.Remove(type);
                }
            }
        }

        private void DecryptString(ModuleDefMD module, Assembly assembly)
        {
            do
            {
                decryptedStringCount = 0;
                foreach (TypeDef type in module.GetTypes())
                {
                    foreach (MethodDef method in type.Methods)
                    {
                        if (!method.HasBody || !method.Body.HasInstructions)
                            continue;
                        method.Body.SimplifyBranches();
                        method.Body.SimplifyMacros(method.Parameters);
                        for (int i = method.Body.Instructions.Count - 1; i >= 1; i--)
                        {
                            if (method.Body.Instructions[i].OpCode == OpCodes.Call && method.Body.Instructions[i].Operand is MethodDef && method.Body.Instructions[i - 1].OpCode == OpCodes.Ldc_I4)
                            {
                                MethodDef stringDecryptorMethod = method.Body.Instructions[i].Operand as MethodDef;
                                if (stringDecryptorMethod.ReturnType == module.CorLibTypes.String && stringDecryptorMethod.Parameters.Count == 1 && stringDecryptorMethod.Parameters[0].Type == module.CorLibTypes.Int32 && method.Body.Instructions[i - 1].IsLdcI4() && stringDecryptorMethod.Body.Instructions[0].OpCode == OpCodes.Call && stringDecryptorMethod.Body.Instructions[0].Operand.ToString().Contains("System.AppDomain::get_CurrentDomain"))
                                {
                                    int value = method.Body.Instructions[i - 1].GetLdcI4Value();
                                    method.Body.Instructions[i - 1].OpCode = OpCodes.Ldstr;
                                    method.Body.Instructions[i - 1].Operand = assembly.ManifestModule.ResolveMethod(stringDecryptorMethod.MDToken.ToInt32()).Invoke(null, new object[]
                                    {
                                        value
                                    });
                                    method.Body.Instructions.RemoveAt(i);
                                    if (!obfuscatorGeneratedMembers.Contains(stringDecryptorMethod))
                                        obfuscatorGeneratedMembers.Add(stringDecryptorMethod);
                                    if (!obfuscatorGeneratedMembers.Contains(stringDecryptorMethod.DeclaringType))
                                        obfuscatorGeneratedMembers.Add(stringDecryptorMethod.DeclaringType);
                                    decryptedStringCount++;
                                }
                            }
                        }
                        method.Body.OptimizeBranches();
                        method.Body.OptimizeMacros();
                    }
                }
            }
            while (decryptedStringCount != 0);
        }
    }
}
