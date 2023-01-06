using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BabelDeobfuscator
{
    internal class ConstantPhase : IDeobfuscatePhase
    {
        ModuleDefMD currentModule;

        static Random random = new Random();

        int decryptedConstantsCount;

        List<IMemberDef> obfuscatorGeneratedMembers = new List<IMemberDef>();

        public void Run(ModuleDefMD module, Assembly assembly)
        {
            Logger.LogInfo("Decrypting constants...");
            currentModule = module;
            FindPrivateImplementationDetails(module);
            DecryptConstant(assembly);
            DecryptArray(module, assembly);
            DeleteObfuscatorGeneratedMembers(module);
        }

        private void FindPrivateImplementationDetails(ModuleDefMD module)
        {
            if (module.Find("<PrivateImplementationDetails>", false) != null)
                return;
            IEnumerable<TypeDef> types = module.Types.Where(t => t != module.GlobalType && t.CustomAttributes.Count == 1 && t.CustomAttributes[0].TypeFullName.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute") && t.NestedTypes.Count > 0);
            if (types.Count() > 0)
            {
                types.ElementAt(0).Namespace = "";
                types.ElementAt(0).Name = "<PrivateImplementationDetails>";
                if (types.ElementAt(0).HasMethods && types.ElementAt(0).Methods[0].ReturnType.FullName.Contains("System.UInt32") && types.ElementAt(0).Methods[0].Parameters[0].Type.FullName.Contains("System.String"))
                {
                    types.ElementAt(0).Methods[0].Name = "ComputeStringHash";
                    types.ElementAt(0).Methods[0].Parameters[0].Name = "s";
                }
                   
            }
        }

        private void DeleteObfuscatorGeneratedMembers(ModuleDefMD module)
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

        bool isProxyIntSwitch(MethodDef currentMethod)
        {
            if (currentMethod.Parameters.Count != 1)
                return false;
            if (!currentMethod.Parameters[0].Type.FullName.Contains("System.Int32"))
                return false;
            int switchCount = 0;
            foreach (Instruction instruction in currentMethod.Body.Instructions)
            {
                if (instruction.OpCode.OperandType != OperandType.InlineI && instruction.OpCode.OperandType != OperandType.InlineI8 && instruction.OpCode.OperandType != OperandType.ShortInlineI && instruction.OpCode.OperandType != OperandType.ShortInlineR && instruction.OpCode.OperandType != OperandType.InlineR && instruction.OpCode.OperandType != OperandType.ShortInlineBrTarget && instruction.OpCode.OperandType != OperandType.InlineBrTarget && instruction.OpCode.OperandType != OperandType.ShortInlineVar && instruction.OpCode.OperandType != OperandType.InlineVar && instruction.OpCode.OperandType != OperandType.InlineNone && instruction.OpCode.OperandType != OperandType.InlineSwitch)
                    return false;
                if (instruction.OpCode == OpCodes.Switch)
                    switchCount++;
            }
            if (switchCount < 1)
                return false;
            if (!obfuscatorGeneratedMembers.Contains(currentMethod))
                obfuscatorGeneratedMembers.Add(currentMethod);
            return true;
        }

        void DecryptArray(ModuleDef module, Assembly assembly)
        {
            IEnumerable<TypeDef> types = currentModule.GetTypes();
            for (int i = 0; i < types.Count(); i++)
            {
                TypeDef type = types.ElementAt(i);
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;
                    for (int j = 5; j < method.Body.Instructions.Count - 1; j++)
                    {
                        if (method.Body.Instructions[j].OpCode != OpCodes.Call || !(method.Body.Instructions[j].Operand is MethodDef))
                            continue;
                        Instruction initializeArrrayInstruction = method.Body.Instructions[j - 1];
                        Instruction encryptedArrayDataInstruction = method.Body.Instructions[j - 2];
                        MethodDef decryptorMethod = method.Body.Instructions[j].Operand as MethodDef;
                        if (DecryptArray(assembly, initializeArrrayInstruction, encryptedArrayDataInstruction, decryptorMethod, out Array decryptedArray))
                        {
                            TypeDef privateImplementationDetails = module.Find("<PrivateImplementationDetails>", false);
                            if (privateImplementationDetails == null)
                            {
                                privateImplementationDetails = new TypeDefUser("<PrivateImplementationDetails>")
                                {
                                    IsSealed = true,
                                    BaseType = new TypeRefUser(module, "System", "Object", module.CorLibTypes.AssemblyRef)
                                };
                                privateImplementationDetails.Attributes |= dnlib.DotNet.TypeAttributes.NotPublic;
                                TypeRef compilerGeneratedAttributeType = new TypeRefUser(module, "System.Runtime.CompilerServices", "CompilerGeneratedAttribute", module.CorLibTypes.AssemblyRef);
                                MethodSig compilerGeneratedAttributeSig = MethodSig.CreateInstance(module.CorLibTypes.Void);
                                privateImplementationDetails.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(module, ".ctor", compilerGeneratedAttributeSig, compilerGeneratedAttributeType)));
                                module.AddAsNonNestedType(privateImplementationDetails);
                                module.UpdateRowId(privateImplementationDetails);
                            }
                            Type t = decryptedArray.GetType().GetElementType();
                            uint structSize = (uint)(decryptedArray.Length * SizeOf(t));
                            TypeDef decryptedStaticArrayInitType = null;
                            foreach (TypeDef staticArrayInitType in privateImplementationDetails.NestedTypes)
                            {
                                if (!staticArrayInitType.IsValueType)
                                    continue;
                                if (!staticArrayInitType.IsSealed)
                                    continue;
                                if (staticArrayInitType.ClassSize == structSize)
                                {
                                    decryptedStaticArrayInitType = staticArrayInitType;
                                    break;
                                }
                            }
                            if (decryptedStaticArrayInitType == null)
                            {
                                decryptedStaticArrayInitType = new TypeDefUser($"__StaticArrayInitTypeSize={structSize}", new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef))
                                {
                                    PackingSize = 1,
                                    Layout = dnlib.DotNet.TypeAttributes.ExplicitLayout,
                                    ClassSize = structSize,
                                    IsSealed = true
                                };
                                privateImplementationDetails.NestedTypes.Add(decryptedStaticArrayInitType);
                                module.UpdateRowId(decryptedStaticArrayInitType);
                            }
                            FieldDef fieldContainsDecryptedArray = new FieldDefUser(randomName(), new FieldSig(decryptedStaticArrayInitType.ToTypeSig()))
                            {
                                IsStatic = true,
                                IsInitOnly = true,
                                HasFieldRVA = true,
                                InitialValue = CastToByteArray(decryptedArray)
                            };
                            fieldContainsDecryptedArray.Attributes |= dnlib.DotNet.FieldAttributes.Assembly;
                            privateImplementationDetails.Fields.Add(fieldContainsDecryptedArray);
                            module.UpdateRowId(fieldContainsDecryptedArray);
                            encryptedArrayDataInstruction.Operand = fieldContainsDecryptedArray;
                            method.Body.Instructions[j - 5].OpCode = OpCodes.Ldc_I4;
                            method.Body.Instructions[j - 5].Operand = decryptedArray.Length;
                            method.Body.Instructions[j - 4].Operand = ((TypeSpec)method.Body.Instructions[j + 1].Operand).ScopeType;
                            method.Body.Instructions.RemoveAt(j + 1);
                            method.Body.Instructions.RemoveAt(j);
                        }
                    }
                }
            }
        }

        void DecryptConstant(Assembly assembly)
        {
            do
            {
                decryptedConstantsCount = 0;
                IEnumerable<TypeDef> types = currentModule.GetTypes();
                for (int i = 0; i < types.Count(); i++) 
                {
                    try
                    {
                        TypeDef type = types.ElementAt(i);
                        foreach (MethodDef method in type.Methods)
                        {
                            if (!method.HasBody || !method.Body.HasInstructions)
                                continue;
                            if (isProxyIntSwitch(method))
                                continue;
                            method.Body.SimplifyBranches();
                            method.Body.SimplifyMacros(method.Parameters);
                            for (int j = method.Body.Instructions.Count - 1; j >= 1; j--)
                            {
                                if (method.Body.Instructions[j].OpCode == OpCodes.Call && method.Body.Instructions[j].Operand is MethodDef)
                                {
                                    MethodDef decryptorMethod = method.Body.Instructions[j].Operand as MethodDef;
                                    Instruction parameterInstruction = method.Body.Instructions[j - 1];
                                    if (GetProxiedConstant(assembly, decryptorMethod, parameterInstruction, out object decryptedConstant) || DecryptConstant(assembly, decryptorMethod, parameterInstruction, out decryptedConstant))
                                    {
                                        parameterInstruction.OpCode = GetOpcode(decryptedConstant);
                                        parameterInstruction.Operand = decryptedConstant;
                                        method.Body.Instructions.RemoveAt(j);
                                        decryptedConstantsCount++;
                                    }
                                }
                            }
                            method.Body.OptimizeBranches();
                            method.Body.OptimizeMacros();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex);
                    }
                }
            }
            while (decryptedConstantsCount != 0);
        }

        private bool GetProxiedConstant(Assembly assembly, MethodDef decryptorMethod, Instruction parameterInstruction, out object decryptedConstant)
        {
            decryptedConstant = default;
            if (!decryptorMethod.HasBody || !decryptorMethod.Body.HasInstructions)
                return false;
            if (!isProxyIntSwitch(decryptorMethod))
                return false;
            decryptedConstant = assembly.ManifestModule.ResolveMethod(decryptorMethod.MDToken.ToInt32()).Invoke(null, new object[]
                {
                parameterInstruction.GetOperand()
                });
            return true;
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
                if (!obfuscatorGeneratedMembers.Contains(decryptorMethod))
                    obfuscatorGeneratedMembers.Add(decryptorMethod);
                if (!obfuscatorGeneratedMembers.Contains(decryptorMethod.DeclaringType))
                    obfuscatorGeneratedMembers.Add(decryptorMethod.DeclaringType);
                return true;
            }
            //else if (decryptorMethod.ReturnType.ToString() == "System.Array" && decryptorMethod.Parameters.Count == 1 && decryptorMethod.Parameters[0].Type.ToString() == "System.Byte[]")
            //{
            //    throw new NotSupportedException($"Array encryption is not supported!");
            //}
            return false;
        }

        bool DecryptArray(Assembly assembly, Instruction initializeArrrayInstruction, Instruction encryptedArrayDataInstruction, MethodDef decryptorMethod, out Array decryptedArray)
        {
            decryptedArray = default;
            if (initializeArrrayInstruction.OpCode != OpCodes.Call)
                return false;
            if (!initializeArrrayInstruction.Operand.ToString().Contains("System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray"))
                return false;
            if (encryptedArrayDataInstruction.OpCode != OpCodes.Ldtoken)
                return false;
            if (decryptorMethod.Body.Instructions.Count != 3)
                return false;
            if (encryptedArrayDataInstruction.Operand is FieldDef encryptedArrayDataField)
            {
                decryptedArray = (Array)assembly.ManifestModule.ResolveMethod(decryptorMethod.MDToken.ToInt32()).Invoke(null, new object[]
                {
                    encryptedArrayDataField.InitialValue
                });
                if (!obfuscatorGeneratedMembers.Contains(decryptorMethod))
                    obfuscatorGeneratedMembers.Add(decryptorMethod);
                if (!obfuscatorGeneratedMembers.Contains(decryptorMethod.DeclaringType))
                    obfuscatorGeneratedMembers.Add(decryptorMethod.DeclaringType);
                return true;
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

        static string randomName()
        {
            string result = "";
            string randomNameCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            for (int i = 0; i < 64; i++)
            {
                result += randomNameCharset[random.Next(0, randomNameCharset.Length)];
            }
            return result;
        }

        static unsafe byte[] CastToByteArray(Array array)
        {
            Type t = array.GetType().GetElementType();
            int sizeOfElementType = SizeOf(t);
            byte[] result = new byte[array.Length * sizeOfElementType];
            for (int i = 0; i < array.Length; i++)
            {
                IntPtr p = new IntPtr(&array);
                IntPtr ptr = Marshal.ReadIntPtr(p) + 8;
                Marshal.Copy(ptr, result, 0, result.Length);
            }
            return result;
        }

        static int SizeOf(Type t)
        {
            System.Reflection.Emit.DynamicMethod dynamicMethod = new System.Reflection.Emit.DynamicMethod("sizeof", typeof(int),
                                       Type.EmptyTypes);
            System.Reflection.Emit.ILGenerator ilGen = dynamicMethod.GetILGenerator();
            ilGen.Emit(System.Reflection.Emit.OpCodes.Sizeof, t);
            ilGen.Emit(System.Reflection.Emit.OpCodes.Ret);
            return (int)dynamicMethod.Invoke(null, null);
        }
    }
}
