using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Замер этапов запуска: пишет в системный лог, сколько занял каждый шаг.
    ///
    /// Нужен, чтобы на удалённой базе (VPN) видеть, что именно тормозит:
    /// сеть, достройка схемы, посадка метаданных или построение дерева.
    /// </summary>
    public static class StartupProfiler
    {
        private static readonly Stopwatch Total = Stopwatch.StartNew();

        public static async Task MeasureAsync(string stage, Func<Task> action)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await action();
            }
            finally
            {
                Log(stage, watch.ElapsedMilliseconds);
            }
        }

        public static async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                return await action();
            }
            finally
            {
                Log(stage, watch.ElapsedMilliseconds);
            }
        }

        private static void Log(string stage, long elapsedMs)
        {
            var message = $"⏱ {stage}: {elapsedMs} мс (с запуска {Total.ElapsedMilliseconds} мс)";

            // Медленные этапы пишем предупреждением, чтобы их было видно сразу.
            if (elapsedMs >= 1000)
                SystemLogService.Warning(message, "Startup");
            else
                SystemLogService.Info(message, "Startup");
        }
    }
}
