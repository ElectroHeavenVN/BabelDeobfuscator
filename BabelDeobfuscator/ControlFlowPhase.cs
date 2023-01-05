using de4dot.blocks;
using de4dot.blocks.cflow;
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
    internal class ControlFlowPhase : IDeobfuscatePhase
    {
        static readonly BlocksCflowDeobfuscator blocksCflowDeobfuscator = new BlocksCflowDeobfuscator();

        public void Run(ModuleDefMD module, Assembly assembly)
        {
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (MethodDef method in type.Methods)
                {
                    if (!method.HasBody || !method.Body.HasInstructions)
                        continue;
                    try
                    {
                        Blocks blocks = new Blocks(method);
                        blocksCflowDeobfuscator.Initialize(blocks);
                        blocksCflowDeobfuscator.Deobfuscate();
                        blocks.RepartitionBlocks();
                        blocks.GetCode(out IList<Instruction> instructions, out IList<ExceptionHandler> exceptionHandlers);
                        DotNetUtils.RestoreBody(method, instructions, exceptionHandlers);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex);
                    }
                }
            }
        }
    }
}
