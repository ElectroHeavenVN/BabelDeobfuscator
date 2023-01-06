using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BabelDeobfuscator
{
    internal class ProxyDelegatePhase : IDeobfuscatePhase
    {
        int removedProxyDelegateCount;
        List<TypeDef> obfuscatorGeneratedDelegates = new List<TypeDef>();

        public void Run(ModuleDefMD module, Assembly assembly)
        {
            Logger.LogInfo("Removing proxied methods...");
            RemoveProxyDelegate(module, assembly);
            DeleteObfuscatorGeneratedDelegate(module);
        }

        private void DeleteObfuscatorGeneratedDelegate(ModuleDefMD module)
        {
            foreach (TypeDef obfuscatorGeneratedDelegate in obfuscatorGeneratedDelegates)
            {
                bool isDelete = true;
                foreach (TypeDef type in module.GetTypes())
                {
                    foreach (MethodDef method in type.Methods)
                    {
                        if (method.ReturnType == obfuscatorGeneratedDelegate.ToTypeSig())
                        {
                            isDelete = false;
                            break;
                        }
                        if (!method.HasBody || !method.Body.HasInstructions)
                            continue;
                        foreach (Instruction instruction in method.Body.Instructions.Where(i => i.OpCode.OperandType == OperandType.InlineMethod || i.OpCode.OperandType == OperandType.InlineTok|| i.OpCode.OperandType == OperandType.InlineType))
                        {
                            if (instruction.Operand is MethodDef methodDef && methodDef.DeclaringType == obfuscatorGeneratedDelegate)
                            {
                                isDelete = false;
                                break;
                            }
                            else if (instruction.Operand is TypeDef typeDef && typeDef == obfuscatorGeneratedDelegate)
                            {
                                isDelete = false;
                                break;
                            }
                        }
                    }
                    if (!isDelete)
                        break;
                    foreach (FieldDef field in type.Fields)
                    {
                        if (field.FieldType == obfuscatorGeneratedDelegate.ToTypeSig())
                        {
                            isDelete = false;
                            break;
                        }
                    }
                    if (!isDelete)
                        break;
                }
                if (isDelete)
                {
                    Logger.LogVerbose($"Deleting obfuscator generated delegate: {obfuscatorGeneratedDelegate.FullName} [0x{obfuscatorGeneratedDelegate.MDToken}]...");
                    if (obfuscatorGeneratedDelegate.IsNested)
                        obfuscatorGeneratedDelegate.DeclaringType.NestedTypes.Remove(obfuscatorGeneratedDelegate);
                    else
                        module.Types.Remove(obfuscatorGeneratedDelegate);
                }
            }
        }

        private void RemoveProxyDelegate(ModuleDefMD module, Assembly assembly)
        {
            do
            {
                removedProxyDelegateCount = 0;
                foreach (TypeDef type in module.GetTypes())
                {
                    foreach (MethodDef method in type.Methods)
                    {
                        if (!method.HasBody || !method.Body.HasInstructions)
                            continue;
                        for (int i = 0; i < method.Body.Instructions.Count; i++)
                        {
                            if (method.Body.Instructions[i].OpCode != OpCodes.Call || !(method.Body.Instructions[i].Operand is MethodDef))
                                continue;
                            MethodDef proxyDelegateMethod = method.Body.Instructions[i].Operand as MethodDef;
                            if (!proxyDelegateMethod.DeclaringType.IsDelegate || proxyDelegateMethod.FullName.Contains("::Invoke"))
                                continue;
                            MethodDef proxyDelegateStaticConstructor = proxyDelegateMethod.DeclaringType.FindStaticConstructor();
                            MethodDef proxyDelegateResolverMethod;
                            Delegate proxyDelegateInstance;
                            if (proxyDelegateStaticConstructor.Body.Instructions.Count == 5)
                            {
                                proxyDelegateResolverMethod = proxyDelegateStaticConstructor.Body.Instructions[3].Operand as MethodDef;
                                assembly.ManifestModule.ResolveMethod(proxyDelegateResolverMethod.MDToken.ToInt32()).Invoke(null, new object[]
                                {
                                proxyDelegateStaticConstructor.Body.Instructions[0].GetLdcI4Value(),
                                proxyDelegateStaticConstructor.Body.Instructions[1].GetLdcI4Value(),
                                proxyDelegateStaticConstructor.Body.Instructions[2].GetLdcI4Value()
                                });
                            }
                            else if (proxyDelegateStaticConstructor.Body.Instructions.Count == 6)
                            {
                                proxyDelegateResolverMethod = proxyDelegateStaticConstructor.Body.Instructions[4].Operand as MethodDef;
                                assembly.ManifestModule.ResolveMethod(proxyDelegateResolverMethod.MDToken.ToInt32()).Invoke(null, new object[]
                                {
                                proxyDelegateStaticConstructor.Body.Instructions[0].GetLdcI4Value(),
                                proxyDelegateStaticConstructor.Body.Instructions[1].GetLdcI4Value(),
                                proxyDelegateStaticConstructor.Body.Instructions[2].GetLdcI4Value(),
                                proxyDelegateStaticConstructor.Body.Instructions[3].GetLdcI4Value()
                                });
                            }
                            proxyDelegateInstance = (Delegate)assembly.ManifestModule.ResolveField(proxyDelegateMethod.DeclaringType.Fields.First().MDToken.ToInt32()).GetValue(null);
                            if (proxyDelegateInstance.Method.ReturnTypeCustomAttributes.ToString().Contains("DynamicMethod"))
                            {
                                DynamicMethodBodyReader dynamicMethodBodyReader = new DynamicMethodBodyReader(module, proxyDelegateInstance);
                                dynamicMethodBodyReader.Read();
                                int count = dynamicMethodBodyReader.Instructions.Count;
                                method.Body.Instructions[i].OpCode = dynamicMethodBodyReader.Instructions[count - 2].OpCode;
                                method.Body.Instructions[i].Operand = dynamicMethodBodyReader.Instructions[count - 2].Operand;
                            }
                            else
                            {
                                method.Body.Instructions[i].OpCode = OpCodes.Call;
                                method.Body.Instructions[i].Operand = module.Import(proxyDelegateInstance.Method);
                            }
                            if (!obfuscatorGeneratedDelegates.Contains(proxyDelegateMethod.DeclaringType))
                                obfuscatorGeneratedDelegates.Add(proxyDelegateMethod.DeclaringType);
                            removedProxyDelegateCount++;
                        }
                    }
                }
            }
            while (removedProxyDelegateCount != 0);
        }
    }
}
