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

        public void Run(ModuleDefMD module, Assembly assembly)
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
