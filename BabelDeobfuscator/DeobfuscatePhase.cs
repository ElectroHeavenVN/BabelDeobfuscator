using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace BabelDeobfuscator
{
    internal interface IDeobfuscatePhase
    {
        void Run(ModuleDefMD module, Assembly assembly);
    }
}
