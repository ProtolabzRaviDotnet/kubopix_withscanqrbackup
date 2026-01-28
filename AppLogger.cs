using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniApp
{
    public static class AppLogger
    {
        private static readonly string logFilePath =
            Path.Combine(Path.GetTempPath(), "MiniApp_Logs.txt");

        public static void Log(string message)
        {
            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch { /* ignore logging error */ }
        }
    }
}
