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

        public void Run(ModuleDefMD module, Assembly assembly)
        {
            currentModule = module;
            DecryptConstant(module, assembly);
            DecryptArray(module, assembly);
        }

        bool isProxyIntSwitch(MethodDef currentMethod)
        {
            int switchCount = 0;
            foreach (Instruction instruction in currentMethod.Body.Instructions)
            {
                if (instruction.OpCode.OperandType != OperandType.InlineI && instruction.OpCode.OperandType != OperandType.InlineI8 && instruction.OpCode.OperandType != OperandType.ShortInlineI && instruction.OpCode.OperandType != OperandType.ShortInlineR && instruction.OpCode.OperandType != OperandType.InlineR && instruction.OpCode.OperandType != OperandType.ShortInlineBrTarget && instruction.OpCode.OperandType != OperandType.InlineBrTarget && instruction.OpCode.OperandType != OperandType.ShortInlineVar && instruction.OpCode.OperandType != OperandType.InlineVar && instruction.OpCode.OperandType != OperandType.InlineNone && instruction.OpCode.OperandType != OperandType.InlineSwitch)
                    return false;
                if (instruction.OpCode == OpCodes.Switch)
                    switchCount++;
            }
            if (switchCount != 1)
                return false;
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
                                privateImplementationDetails = new TypeDefUser("<PrivateImplementationDetails>");
                                privateImplementationDetails.IsSealed = true;
                                privateImplementationDetails.Attributes |= dnlib.DotNet.TypeAttributes.NotPublic;
                                privateImplementationDetails.BaseType = new TypeRefUser(module, "System", "Object", module.CorLibTypes.AssemblyRef);
                                TypeRef compilerGeneratedAttributeType = new TypeRefUser(module, "System.Runtime.CompilerServices", "CompilerGeneratedAttribute", module.CorLibTypes.AssemblyRef);
                                MethodSig compilerGeneratedAttributeSig = MethodSig.CreateInstance(module.CorLibTypes.Void);
                                privateImplementationDetails.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(module, ".ctor", compilerGeneratedAttributeSig, compilerGeneratedAttributeType)));
                                module.AddAsNonNestedType(privateImplementationDetails);
                                module.UpdateRowId(privateImplementationDetails);
                            }
                            Type t = decryptedArray.GetType().GetElementType();
                            uint structSize = (uint)(decryptedArray.Length * Marshal.SizeOf(t));
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
                                decryptedStaticArrayInitType = new TypeDefUser($"__StaticArrayInitTypeSize={structSize}", new TypeRefUser(module, "System", "ValueType", module.CorLibTypes.AssemblyRef));
                                decryptedStaticArrayInitType.PackingSize = 1;
                                decryptedStaticArrayInitType.Layout = dnlib.DotNet.TypeAttributes.ExplicitLayout;
                                decryptedStaticArrayInitType.ClassSize = structSize;
                                decryptedStaticArrayInitType.IsSealed = true;
                                decryptedStaticArrayInitType.Attributes |= dnlib.DotNet.TypeAttributes.NotPublic;
                                privateImplementationDetails.NestedTypes.Add(decryptedStaticArrayInitType);
                                module.UpdateRowId(decryptedStaticArrayInitType);
                            }
                            FieldDef fieldContainsDecryptedArray = new FieldDefUser(randomName(), new FieldSig(decryptedStaticArrayInitType.ToTypeSig()));
                            fieldContainsDecryptedArray.IsStatic = true;
                            fieldContainsDecryptedArray.Attributes |= dnlib.DotNet.FieldAttributes.Assembly;
                            fieldContainsDecryptedArray.IsInitOnly = true;
                            fieldContainsDecryptedArray.HasFieldRVA = true;
                            fieldContainsDecryptedArray.InitialValue = CastToByteArray(decryptedArray);
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

        void DecryptConstant(ModuleDefMD module, Assembly assembly)
        {
            do
            {
                decryptedConstantsCount = 0;
                IEnumerable<TypeDef> types = currentModule.GetTypes();
                for (int i = 0; i < types.Count(); i++) 
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
            }
            while (decryptedConstantsCount != 0);
        }

        private bool GetProxiedConstant(Assembly assembly, MethodDef decryptorMethod, Instruction parameterInstruction, out object decryptedConstant)
        {
            decryptedConstant = default;
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
                return true;
            }
            else if (decryptorMethod.ReturnType.ToString() == "System.Array" && decryptorMethod.Parameters.Count == 1 && decryptorMethod.Parameters[0].Type.ToString() == "System.Byte[]")
            {
                //throw new NotSupportedException($"Array encryption is not supported!");
            }
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

        static byte[] CastToByteArray(Array array)
        {
            Type t = array.GetType().GetElementType();
            int sizeOfElementType = Marshal.SizeOf(t);
            List<byte> result = new List<byte>(array.Length * sizeOfElementType);
            for (int i = 0; i < array.Length; i++)
            {
                if (sizeOfElementType == 1)
                {
                    try
                    {
                        result.Add(BitConverter.GetBytes((sbyte)array.GetValue(i))[0]);
                    }
                    catch (InvalidCastException)
                    {
                        result.Add(BitConverter.GetBytes((byte)array.GetValue(i))[0]);
                    }
                }
                else if (sizeOfElementType == 2)
                {
                    try
                    {
                        result.AddRange(BitConverter.GetBytes((short)array.GetValue(i)));
                    }
                    catch (InvalidCastException)
                    {
                        result.AddRange(BitConverter.GetBytes((ushort)array.GetValue(i)));
                    }
                }
                else if (sizeOfElementType == 4)
                {
                    try
                    {
                        result.AddRange(BitConverter.GetBytes((int)array.GetValue(i)));
                    }
                    catch (InvalidCastException)
                    {
                        result.AddRange(BitConverter.GetBytes((uint)array.GetValue(i)));
                    }
                }
                else if (sizeOfElementType == 8)
                {
                    try
                    {
                        result.AddRange(BitConverter.GetBytes((long)array.GetValue(i)));
                    }
                    catch (InvalidCastException)
                    {
                        result.AddRange(BitConverter.GetBytes((ulong)array.GetValue(i)));
                    }
                }
                else throw new NotSupportedException($"Array of type {t} is not supported!");
            }
            return result.ToArray();
        }
    }
}
