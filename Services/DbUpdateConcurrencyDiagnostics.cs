using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Диагностика <see cref="DbUpdateConcurrencyException"/>.
    /// В обычный системный лог попадает только текст исключения, из-за чего невозможно
    /// понять, КАКАЯ именно сущность не была сохранена. Класс дополнительно фиксирует:
    /// какие записи находились в трекере, их состояние, значения ключей и свойств,
    /// а также есть ли строка с таким ключом в БД прямо сейчас.
    /// Отчёт пишется в отдельный многострочный файл logs/concurrency_yyyyMMdd.log,
    /// чтобы не ломать табличный формат основного system_yyyyMMdd.log.
    /// </summary>
    public static class DbUpdateConcurrencyDiagnostics
    {
        public static string DiagnosticsLogFilePath =>
            Path.Combine(SystemLogService.LogDirectory, $"concurrency_{DateTime.Now:yyyyMMdd}.log");

        public static async Task LogAsync(
            DbContext? context,
            Exception exception,
            string? source,
            string? stage)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("================================================================");
                report.AppendLine($"Время       : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                report.AppendLine($"Источник    : {source ?? "-"}");
                report.AppendLine($"Этап        : {stage ?? "-"}");
                report.AppendLine($"Исключение : {exception.GetType().FullName}");
                report.AppendLine(exception.ToString());
                report.AppendLine();

                if (context == null)
                {
                    report.AppendLine("Контекст недоступен — состояние сущностей не получено.");
                }
                else
                {
                    WriteFailedEntries(report, exception);
                    WriteTrackerSnapshot(report, context);
                    await WriteRowExistenceReportAsync(report, context);
                }

                report.AppendLine("================================================================");
                report.AppendLine();

                Directory.CreateDirectory(SystemLogService.LogDirectory);
                File.AppendAllText(DiagnosticsLogFilePath, report.ToString(), Encoding.UTF8);

                SystemLogService.Error(
                    $"DbUpdateConcurrencyException. Этап: {stage ?? "-"}. " +
                    $"Подробности: logs/{Path.GetFileName(DiagnosticsLogFilePath)}",
                    source);
            }
            catch (Exception loggingException)
            {
                SystemLogService.Warning(
                    $"Диагностика DbUpdateConcurrencyException не выполнена: {loggingException.Message}",
                    source);
            }
        }

        private static void WriteFailedEntries(StringBuilder report, Exception exception)
        {
            report.AppendLine("--- Записи из исключения (виновники падения) ---");
            if (exception is not DbUpdateException updateException || updateException.Entries.Count == 0)
            {
                report.AppendLine("  EF не передал записи в исключении — ниже полный снимок трекера.");
            }
            else
            {
                foreach (var entry in updateException.Entries)
                {
                    report.AppendLine($"  {Describe(entry)}");
                }
            }

            report.AppendLine();
        }

        private static void WriteTrackerSnapshot(StringBuilder report, DbContext context)
        {
            var pending = context.ChangeTracker.Entries().ToList();
            report.AppendLine($"--- Снимок ChangeTracker: {pending.Count} записей ---");
            foreach (var entry in pending)
            {
                report.AppendLine(Describe(entry));
                foreach (var property in entry.Properties)
                {
                    report.AppendLine(
                        $"      {property.Metadata.Name}: current={Format(property.CurrentValue)}" +
                        $" original={Format(property.OriginalValue)}");
                }
            }

            report.AppendLine();
        }

        private static async Task WriteRowExistenceReportAsync(StringBuilder report, DbContext context)
        {
            report.AppendLine("--- Проверка наличия строк в БД (перечитывание Reload) ---");
            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (entry.State == EntityState.Detached || entry.State == EntityState.Added)
                {
                    continue;
                }

                var stateBefore = entry.State;
                var keyBefore = DescribeKey(entry);
                try
                {
                    await entry.ReloadAsync();
                    if (entry.State == EntityState.Detached)
                    {
                        report.AppendLine(
                            $"  !!! СТРОКА ОТСУТСТВУЕТ В БД: {entry.Metadata.ClrType.Name} " +
                            $"{keyBefore} (состояние до проверки: {stateBefore})");
                    }
                    else
                    {
                        report.AppendLine(
                            $"  строка есть: {entry.Metadata.ClrType.Name} {keyBefore} " +
                            $"(состояние после перечитывания: {entry.State})");
                    }
                }
                catch (Exception checkException)
                {
                    report.AppendLine(
                        $"  проверка не удалась для {entry.Metadata.ClrType.Name} {keyBefore}: " +
                        checkException.Message);
                }
            }
        }

        private static string Describe(EntityEntry entry)
        {
            var tableName = entry.Metadata.GetTableName();
            if (string.IsNullOrEmpty(tableName))
            {
                return $"{entry.Metadata.ClrType.Name} State={entry.State} Key=[{DescribeKey(entry)}]";
            }

            var schema = entry.Metadata.GetSchema();
            var fullTable = string.IsNullOrEmpty(schema)
                ? $"\"{tableName}\""
                : $"\"{schema}\".\"{tableName}\"";

            return $"{entry.Metadata.ClrType.Name} State={entry.State} Table={fullTable} " +
                   $"Key=[{DescribeKey(entry)}]";
        }

        private static string DescribeKey(EntityEntry entry)
        {
            var primaryKey = entry.Metadata.FindPrimaryKey();
            if (primaryKey == null)
            {
                return "нет PK";
            }

            return string.Join(", ", primaryKey.Properties.Select(property =>
                $"{property.Name}={Format(entry.Property(property.Name).CurrentValue)}"));
        }

        private static string Format(object? value) => value switch
        {
            null => "null",
            string text => text.Length > 120 ? text[..120] + "…" : text,
            _ => value.ToString() ?? "null"
        };
    }
}
