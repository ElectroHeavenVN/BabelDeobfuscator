using dnlib.DotNet;
using dnlib.DotNet.Writer;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BabelDeobfuscator
{
    internal class Program
    {
        [DllImport("msvcrt.dll", CharSet = CharSet.Ansi)]
        static extern int system(string command = "pause");

        static void Main(string[] args)
        {
            string filePath;
            Logger.Log("Babel Deobfuscator v1.0.0 by ElectroHeavenVN", ConsoleColor.Green);
            if (args.Length == 0)
            {
                Logger.LogInfo("Use -v / --v / /v to turn on verbose mode.");
                Console.Write("File path: ");
                filePath = Console.ReadLine().Replace("\"", "");
                if (!File.Exists(filePath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Logger.LogError($"File {filePath} not found!");
                }
                else
                    Deobfuscate(filePath);
            }
            else
                foreach (string arg in args)
                {
                    if ((arg.StartsWith("-") || arg.StartsWith("--") || arg.StartsWith("/")) && (arg[arg.Length - 1] == 'V' || arg[arg.Length - 1] == 'v'))
                    {
                        Logger.LogInfo("Verbose mode: enabled");
                        Logger.isVerbose = true;
                    }
                }
            foreach (string arg in args)
            {
                if ((arg.StartsWith("-") || arg.StartsWith("--") || arg.StartsWith("/")) && (arg[arg.Length - 1] == 'V' || arg[arg.Length - 1] == 'v'))
                    continue;
                Deobfuscate(arg);
            }
            system();
        }

        private static void Deobfuscate(string filePath)
        {
            Logger.LogInfo("Deobfuscating module: " + Path.GetFileName(filePath) + "...");
            ModuleDefMD module = ModuleDefMD.Load(filePath);
            ModuleContext context = ModuleDef.CreateModuleContext();
            AssemblyResolver resolver = (AssemblyResolver)context.AssemblyResolver;
            resolver.EnableTypeDefCache = true;
            module.Context = context;
            ((AssemblyResolver)module.Context.AssemblyResolver).AddToCache(module);
            foreach (AssemblyRef assemblyRef in module.GetAssemblyRefs())
            {
                try
                {
                    resolver.ResolveThrow(assemblyRef, module);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error while resolving dependencies: ");
                    Logger.LogException(ex);
                }
            }
            try
            {
                Assembly assembly = Assembly.LoadFrom(filePath);
                new ControlFlowPhase().Run(module, assembly);
                new ProxyDelegatePhase().Run(module, assembly);
                new ConstantPhase().Run(module, assembly);
                new StringPhase().Run(module, assembly);
                new ControlFlowPhase().Run(module, assembly);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
            ModuleWriterOptions moduleWriterOptions = new ModuleWriterOptions(module);
            string path = Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath) + "-Deobfuscated" + Path.GetExtension(filePath);
            moduleWriterOptions.MetadataLogger = new Logger();
            try
            {
                module.Write(path, moduleWriterOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogException(ex);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                moduleWriterOptions.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
                module.Write(path, moduleWriterOptions);
            }
            Logger.LogInfo("Output file: " + path);
        }
    }
}
