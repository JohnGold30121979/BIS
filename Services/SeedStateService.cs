using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Отметки о выполненных разовых посадках данных (документация, счета-фактуры,
    /// стандартные отчёты, предустановленные справочники).
    ///
    /// Зачем: эти посадки идемпотентны, но выполняются при КАЖДОМ открытии базы —
    /// на удалённой базе это 6–10 секунд впустую, хотя всё уже создано.
    /// Отметка хранится в schema_migrations: есть запись — посадку пропускаем.
    ///
    /// Когда состав посадки меняется, версию в константе нужно поднять —
    /// тогда она один раз выполнится заново на всех базах.
    /// </summary>
    public static class SeedStateService
    {
        /// <summary>Версия посадки документации. Поднимать при изменении состава.</summary>
        public const string DocumentationVersion = "2026.10.22.1";

        /// <summary>Версия посадки счетов-фактур.</summary>
        public const string InvoiceVersion = "2026.10.22.1";

        /// <summary>Версия стандартных отчётов.</summary>
        public const string StandardReportsVersion = "2026.10.22.1";

        public static string DocumentationKey => $"seed.documentation.{DocumentationVersion}";
        public static string InvoiceKey => $"seed.invoices.{InvoiceVersion}";
        public static string StandardReportsKey => $"seed.standard-reports.{StandardReportsVersion}";

        public static async Task<bool> IsAppliedAsync(AppDbContext context, string key)
        {
            try
            {
                await EnsureJournalAsync(context);

                var count = await context.Database
                    .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM schema_migrations WHERE version = {0}", key)
                    .FirstOrDefaultAsync();

                return count > 0;
            }
            catch (Exception ex)
            {
                // Не смогли прочитать отметку — работаем как раньше, выполняем посадку.
                System.Diagnostics.Debug.WriteLine($"[seed] отметка {key} не прочитана: {ex.Message}");
                return false;
            }
        }

        public static async Task MarkAsync(AppDbContext context, string key, string description)
        {
            try
            {
                await EnsureJournalAsync(context);

                var safeDescription = description.Replace("'", "''");
                await context.Database.ExecuteSqlRawAsync($@"
                    INSERT INTO schema_migrations (version, description, applied_at, runs)
                    VALUES ('{key}', '{safeDescription}', now(), 1)
                    ON CONFLICT (version) DO UPDATE
                    SET applied_at = now(), runs = schema_migrations.runs + 1;");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[seed] отметка {key} не записана: {ex.Message}");
            }
        }

        private static async Task EnsureJournalAsync(AppDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version     text PRIMARY KEY,
                    description text NOT NULL DEFAULT '',
                    applied_at  timestamp with time zone NOT NULL DEFAULT now(),
                    runs        integer NOT NULL DEFAULT 1
                );");
        }
    }
}
