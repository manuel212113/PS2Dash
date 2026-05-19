using System;
using System.IO;

namespace PS2Desktop.Services
{
    public enum LogLevel { Info, Warning, Error }

    public class LoggingService
    {
        private static readonly Lazy<LoggingService> _instance = new(() => new());
        public static LoggingService Instance => _instance.Value;

        private readonly string _logDir;
        private readonly object _lock = new();

        private LoggingService()
        {
            _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(_logDir);
            CleanOldLogs();
        }

        public void Info(string message) => Write(LogLevel.Info, message);
        public void Warning(string message) => Write(LogLevel.Warning, message);
        public void Error(string message, Exception? ex = null)
        {
            var msg = ex != null ? $"{message} | {ex.GetType().Name}: {ex.Message}" : message;
            Write(LogLevel.Error, msg);
        }

        private void Write(LogLevel level, string message)
        {
            try
            {
                var date = DateTime.Now.ToString("yyyy-MM-dd");
                var time = DateTime.Now.ToString("HH:mm:ss");
                var logFile = Path.Combine(_logDir, $"log-{date}.txt");
                var line = $"[{time}] [{level}] {message}";

                lock (_lock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }

                System.Diagnostics.Debug.WriteLine(line);
            }
            catch { /* Silently ignore logging failures */ }
        }

        private void CleanOldLogs()
        {
            try
            {
                foreach (var f in Directory.GetFiles(_logDir, "log-*.txt"))
                {
                    var fi = new FileInfo(f);
                    if (fi.CreationTime < DateTime.Now.AddDays(-30))
                        fi.Delete();
                }
            }
            catch { /* Silently ignore log cleanup failures */ }
        }
    }
}
