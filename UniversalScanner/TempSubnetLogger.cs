using System;
using System.IO;

namespace UniversalScanner
{
    public static class TempSubnetLogger
    {
        private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "UniversalScanner", "TemporarySubnet.log");

        private static bool IsEnabled => Config.getInstance().EnableTemporarySubnetDebugLogging;

        static TempSubnetLogger()
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        public static void Log(string message)
        {
            if (!IsEnabled) return;

            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                File.AppendAllText(LogFile, $"[{timestamp}] {message}\r\n");
            }
            catch { }
        }

        public static void LogHeader(string title)
        {
            if (!IsEnabled) return;

            Log("");
            Log(new string('-', 50));
            Log(title);
            Log(new string('-', 50));
        }

        public static void LogSubHeader(string title)
        {
            if (!IsEnabled) return;
            Log($"\r\n===== {title} =====");
        }
    }
}
