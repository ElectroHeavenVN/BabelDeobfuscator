using dnlib.DotNet;
using System;

namespace BabelDeobfuscator
{
    internal class Logger : ILogger
    {
        internal static bool isVerbose;

        internal static ConsoleColor oldColor;

        internal static void LogVerbose(string content)
        {
            if (isVerbose)
            {
                oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("[V] " + content);
                Console.ForegroundColor = oldColor;
            }
        }

        internal static void LogInfo(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("[I] " + content);
            Console.ForegroundColor = oldColor;
        }

        internal static void LogWarning(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("![W] " + content);
            Console.ForegroundColor = oldColor;
        }

        internal static void LogError(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("![E] " + content);
            Console.ForegroundColor = oldColor;
        }

        internal static void LogException(Exception content)
        {

            oldColor = Console.ForegroundColor;
            LogException(content, false);
            Console.ForegroundColor = oldColor;
        }

        static void LogException(Exception content, bool hasInnerException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{(hasInnerException ? " ---> " : "![Ex] ")}{content.GetType()}: {content.Message}{Environment.NewLine}");
            if (content.InnerException != null)
                LogException(content.InnerException, true);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(content.StackTrace);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write((content.InnerException == null ? "" : Environment.NewLine + "   --- End of inner exception stack trace ---") + Environment.NewLine);
        }

        public void Log(object sender, LoggerEvent loggerEvent, string format, params object[] args)
        {
            switch (loggerEvent)
            {
                case LoggerEvent.Error:
                    LogError(string.Format(format, args));
                    break;
                case LoggerEvent.Warning:
                    LogWarning(string.Format(format, args));
                    break;
                case LoggerEvent.Info:
                    LogInfo(string.Format(format, args));
                    break;
                case LoggerEvent.Verbose:
                case LoggerEvent.VeryVerbose:
                    LogVerbose(string.Format(format, args));
                    break;
            }
        }

        public bool IgnoresEvent(LoggerEvent loggerEvent)
        {
            return false;
        }
    }
}
