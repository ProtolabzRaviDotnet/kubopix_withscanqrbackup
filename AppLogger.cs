using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kuboPix
{
    public static class AppLogger
    {
        private static readonly string logFilePath = Path.Combine(Path.GetTempPath(), "kuboPix_Logs.txt");
        public static void Log(string message)
        {
            try
            {
                CleanupOldLogs();

                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch { /* ignore logging error */ }
        }
        private static void CleanupOldLogs()
        {
            try
            {
                if (!File.Exists(logFilePath))
                    return;

                var lines = File.ReadAllLines(logFilePath);
                var today = DateTime.Now.Date;

                var filteredLines = lines.Where(line =>
                {
                    if (line.Length < 19) return false;

                    string datePart = line.Substring(0, 19);

                    if (DateTime.TryParseExact(datePart,"yyyy-MM-dd HH:mm:ss",null,System.Globalization.DateTimeStyles.None,out DateTime logDate))
                    {
                        int diffDays = (today - logDate.Date).Days;

                        // keep only today and yesterday
                        return diffDays <= 1;
                    }

                    return false;
                });

                File.WriteAllLines(logFilePath, filteredLines);
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}
