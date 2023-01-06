using dnlib.DotNet;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

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
                Console.WriteLine("[V] " + EscapeString(content));
                Console.ForegroundColor = oldColor;
            }
        }

        internal static void LogInfo(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("[I] " + EscapeString(content));
            Console.ForegroundColor = oldColor;
        }

        internal static void LogWarning(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("![W] " + EscapeString(content));
            Console.ForegroundColor = oldColor;
        }

        internal static void LogError(string content)
        {
            oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("![E] " + EscapeString(content));
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
            Console.Write($"{(hasInnerException ? " ---> " : "![Ex] ")}{EscapeString(content.GetType().ToString())}: {EscapeString(content.Message)}{Environment.NewLine}");
            if (content.InnerException != null)
                LogException(content.InnerException, true);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(EscapeString(content.StackTrace));
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write((content.InnerException == null ? "" : Environment.NewLine + "   --- End of inner exception stack trace ---") + Environment.NewLine);
        }

        internal static string EscapeString(string str)
        {
            string result = "";
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (i < str.Length - 1 && c == '\r' && str[i + 1] == '\n')
                {
                    result += Environment.NewLine;
                    i++;
                }
                else if (c < 32 || c > 126)
                    result += "\\u" + Convert.ToString(c, 16).PadLeft(4, '0').ToUpper();
                else result += c;
            }
            return result;
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
