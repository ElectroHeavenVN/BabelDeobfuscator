using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BabelDeobfuscator
{
    internal class ConstantPhase : IDeobfuscatePhase
    {
        ModuleDefMD currentModule;

        TypeSig arrayClassSig;

        TypeSig byteArraySig;

        int decryptedConstantsCount;
        public void Run(ModuleDefMD module, Assembly assembly)
        {
            currentModule = module;
            arrayClassSig = currentModule.ImportAsTypeSig(typeof(Array));
            byteArraySig = currentModule.ImportAsTypeSig(typeof(byte[]));
            do
            {
                decryptedConstantsCount = 0;
                foreach (TypeDef type in currentModule.GetTypes())
                {
                    foreach (MethodDef method in type.Methods)
                    {
                        if (!method.HasBody || !method.Body.HasInstructions)
                            continue;
                        for (int i = method.Body.Instructions.Count - 1; i >= 0; i--)
                        {
                            if (method.Body.Instructions[i].OpCode != OpCodes.Call || !(method.Body.Instructions[i].Operand is MethodDef))
                                continue;
                            MethodDef intDecryptorMethod = method.Body.Instructions[i].Operand as MethodDef;
                            Instruction parameterInstruction = method.Body.Instructions[i - 1];
                            try
                            {
                                if (!DecryptConstant(assembly, intDecryptorMethod, parameterInstruction, out object decryptedConstant))
                                    continue;
                                method.Body.Instructions[i - 1].OpCode = GetOpcode(decryptedConstant);
                                method.Body.Instructions[i - 1].Operand = decryptedConstant;
                                method.Body.Instructions.RemoveAt(i);
                                decryptedConstantsCount++;
                            }
                            catch (NotSupportedException ex)
                            {
                                if (Debugger.IsAttached)
                                    Debugger.Break();
                            }
                        }
                    }
                }
            }
            while (decryptedConstantsCount != 0);
        }

        bool DecryptConstant(Assembly assembly, MethodDef decryptorMethod, Instruction parameterInstruction, out object decryptedConstant)
        {
            decryptedConstant = default;
            if (isEncryptedType(decryptorMethod.ReturnType) && decryptorMethod.Parameters.Count == 1 && decryptorMethod.Parameters[0].Type == currentModule.CorLibTypes.Int32 && decryptorMethod.Body.Instructions.Count == 5 && isLdelem(decryptorMethod.Body.Instructions[3]))
            {
                decryptedConstant = assembly.ManifestModule.ResolveMethod(decryptorMethod.MDToken.ToInt32()).Invoke(null, new object[]
                {
                    parameterInstruction.GetOperand()
                });
                return true;
            }
            else if (decryptorMethod.ReturnType == arrayClassSig && decryptorMethod.Parameters.Count == 1 && decryptorMethod.Parameters[0].Type == byteArraySig)
            {
                throw new NotSupportedException($"Array encryption is not supported!");
            }
            return false;
        }

        private bool isLdelem(Instruction instruction) => instruction.OpCode == OpCodes.Ldelem_I4 || instruction.OpCode == OpCodes.Ldelem_I8 || instruction.OpCode == OpCodes.Ldelem_R4 || instruction.OpCode == OpCodes.Ldelem_R8;

        private OpCode GetOpcode(object obj)
        {
            switch (obj)
            {
                case int _:
                case uint _:
                case bool _:
                    return OpCodes.Ldc_I4;
                case long _:
                case ulong _:
                    return OpCodes.Ldc_I8;
                case float _:
                    return OpCodes.Ldc_R4;
                case double _:
                    return OpCodes.Ldc_R8;
                default:
                    throw new NotSupportedException($"Type {obj.GetType()} is not supported!");
            }
        }

        bool isEncryptedType(TypeSig sig)
        {
            if (sig == currentModule.CorLibTypes.Int32)         //int
                return true;
            if (sig == currentModule.CorLibTypes.Int64)         //long
                return true;
            if (sig == currentModule.CorLibTypes.Single)        //float
                return true;
            if (sig == currentModule.CorLibTypes.Double)        //double
                return true;
            return false;
        }
    }
}
