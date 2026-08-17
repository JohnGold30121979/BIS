using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BIS.ERP.Services
{
    public enum SystemLogLevel
    {
        Info,
        Warning,
        Error
    }

    public static class SystemLogService
    {
        private static readonly object SyncRoot = new();

        public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

        public static string MainLogFilePath => Path.Combine(LogDirectory, $"system_{DateTime.Now:yyyyMMdd}.log");

        public static void Info(string message, string? source = null)
        {
            Write(SystemLogLevel.Info, message, source, null);
        }

        public static void Warning(string message, string? source = null, Exception? exception = null)
        {
            Write(SystemLogLevel.Warning, message, source, exception);
        }

        public static void Error(string message, string? source = null, Exception? exception = null)
        {
            Write(SystemLogLevel.Error, message, source, exception);
        }

        private static void Write(SystemLogLevel level, string message, string? source, Exception? exception)
        {
            if (string.IsNullOrWhiteSpace(message) && exception == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(LogDirectory);

                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append('\t')
                    .Append(level)
                    .Append('\t')
                    .Append(Environment.UserName)
                    .Append('\t')
                    .Append(source ?? string.Empty)
                    .Append('\t')
                    .Append(message?.ReplaceLineEndings(" ") ?? string.Empty)
                    .Append('\t')
                    .Append(exception?.ToString().ReplaceLineEndings(" ") ?? string.Empty)
                    .AppendLine()
                    .ToString();

                lock (SyncRoot)
                {
                    File.AppendAllText(MainLogFilePath, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка записи основного системного лога: {ex.Message}");
            }
        }
    }
}
