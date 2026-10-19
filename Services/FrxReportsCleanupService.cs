using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Разовая чистка FRX-отчётов из базы (этап 2 из Docs/FRX_удаление_план_и_скрипты.md).
    ///
    /// FRX отключён (ReportingFeatures.FrxEnabled = false), отчёты FoxPro остались в данных
    /// прошлых баз. Метод выполняется один раз на базу: делает резервные копии
    /// (<c>FrxBackup_*</c>), удаляет отчёты и связанные строки, пишет метку в
    /// <c>DataFixJournal</c>. Повторный запуск ничего не делает — даже если FRX-отчёты
    /// снова появятся, они не будут удалены молча.
    /// </summary>
    public sealed class FrxReportsCleanupService
    {
        /// <summary>Ключ метки в DataFixJournal.</summary>
        public const string JournalKey = "FRX_REPORTS_CLEANUP";

        /// <summary>Что считаем FRX-отчётом.</summary>
        private const string FrxCondition =
            "(\"Code\" LIKE 'standard.frx.%' OR \"ReportType\" = 'FoxProLayout' OR \"SourceFormat\" = 'FoxProFRX')";

        private readonly AppDbContext _context;

        public FrxReportsCleanupService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<FrxCleanupOutcome> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            await EnsureJournalAsync(cancellationToken);

            if (await IsAppliedAsync(cancellationToken))
                return new FrxCleanupOutcome(true, 0, "чистка уже выполнялась ранее");

            var frxReports = await ScalarAsync(
                $"SELECT count(*)::int AS \"Value\" FROM \"Reports\" WHERE {FrxCondition}", cancellationToken);

            if (frxReports == 0)
            {
                await MarkAppliedAsync("FRX-отчётов не найдено", cancellationToken);
                return new FrxCleanupOutcome(false, 0, "FRX-отчётов не найдено");
            }

            var details = new List<string>();

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await BackupAsync("Reports", null, cancellationToken);
                details.Add($"Reports={frxReports}");

                // Дочерние таблицы отчётов — копируем и удаляем только строки FRX-отчётов.
                foreach (var (table, ownerColumn) in new[]
                         {
                             ("ReportFields", "ReportId"),
                             ("ReportFilters", "ReportId"),
                             ("ReportGroups", "ReportId"),
                             ("ReportParameters", "ReportId"),
                             ("ReportElementMappings", "ReportId"),
                             ("ReportHeaderFooter", "ReportId")
                         })
                {
                    if (!await TableExistsAsync(table, cancellationToken))
                        continue;

                    var backupSql =
                        $"CREATE TABLE IF NOT EXISTS \"FrxBackup_{table}\" AS " +
                        $"SELECT child.* FROM \"{table}\" child WHERE child.\"{ownerColumn}\" IN " +
                        $"(SELECT \"Id\" FROM \"Reports\" WHERE {FrxCondition})";
                    await _context.Database.ExecuteSqlRawAsync(backupSql, cancellationToken);

                    var deleted = await _context.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM \"{table}\" WHERE \"{ownerColumn}\" IN (SELECT \"Id\" FROM \"Reports\" WHERE {FrxCondition})",
                        cancellationToken);
                    if (deleted > 0)
                        details.Add($"{table}={deleted}");
                }

                // Пункты меню, привязанные к FRX-отчётам (ObjectType = 'Report').
                if (await TableExistsAsync("MetadataModuleItems", cancellationToken))
                {
                    var menuDeleted = await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"MetadataModuleItems\" " +
                        "WHERE \"ObjectType\" = 'Report' AND \"ObjectId\" IN " +
                        $"(SELECT \"Id\" FROM \"Reports\" WHERE {FrxCondition})",
                        cancellationToken);
                    if (menuDeleted > 0)
                        details.Add($"MetadataModuleItems={menuDeleted}");
                }

                // Правила полей FoxPro — отдельная таблица, живёт только для FRX.
                if (await TableExistsAsync("FoxProReportFieldRules", cancellationToken))
                {
                    var rulesBackup =
                        "CREATE TABLE IF NOT EXISTS \"FrxBackup_FoxProReportFieldRules\" AS SELECT * FROM \"FoxProReportFieldRules\"";
                    await _context.Database.ExecuteSqlRawAsync(rulesBackup, cancellationToken);

                    var rulesDeleted = await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"FoxProReportFieldRules\"", cancellationToken);
                    if (rulesDeleted > 0)
                        details.Add($"FoxProReportFieldRules={rulesDeleted}");
                }

                var reportsDeleted = await _context.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM \"Reports\" WHERE {FrxCondition}", cancellationToken);
                details.Add($"ReportsDeleted={reportsDeleted}");

                await transaction.CommitAsync(cancellationToken);

                var summary = string.Join(", ", details);
                await MarkAppliedAsync(summary, cancellationToken);

                SystemLogService.Warning(
                    $"Удалены FRX-отчёты (разовая чистка): {summary}. Резервные копии — таблицы FrxBackup_*.",
                    "FRX");

                return new FrxCleanupOutcome(false, frxReports, summary);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                SystemLogService.Warning($"Чистка FRX не выполнена: {ex.Message}", "FRX");
                throw;
            }
        }

        private async Task BackupAsync(string table, string? whereSql, CancellationToken cancellationToken)
        {
            var sql = $"CREATE TABLE IF NOT EXISTS \"FrxBackup_{table}\" AS SELECT * FROM \"{table}\"" +
                      (string.IsNullOrWhiteSpace(whereSql) ? string.Empty : $" WHERE {whereSql}");
            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task EnsureJournalAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS ""DataFixJournal"" (
                    ""Key"" text NOT NULL PRIMARY KEY,
                    ""AppliedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                    ""Details"" text NULL);";
            await _context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task<bool> IsAppliedAsync(CancellationToken cancellationToken)
        {
            var count = await ScalarAsync(
                $"SELECT count(*)::int AS \"Value\" FROM \"DataFixJournal\" WHERE \"Key\" = '{JournalKey}'",
                cancellationToken);
            return count > 0;
        }

        private async Task MarkAppliedAsync(string details, CancellationToken cancellationToken)
        {
            var safeDetails = details.Replace("'", "''");
            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"DataFixJournal\"(\"Key\", \"AppliedAt\", \"Details\") " +
                $"VALUES ('{JournalKey}', now(), '{safeDetails}') ON CONFLICT (\"Key\") DO NOTHING",
                cancellationToken);
        }

        private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
        {
            var safeTable = tableName.Replace("'", "''");
            var count = await ScalarAsync(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables " +
                $"WHERE table_schema = 'public' AND table_name = '{safeTable}'",
                cancellationToken);
            return count > 0;
        }

        private async Task<int> ScalarAsync(string sql, CancellationToken cancellationToken)
        {
            var result = await _context.Database
                .SqlQueryRaw<int>(sql)
                .ToListAsync(cancellationToken);
            return result.Count == 0 ? 0 : result[0];
        }
    }

    /// <summary>Результат разовой чистки FRX.</summary>
    public sealed record FrxCleanupOutcome(bool AlreadyAppliedEarlier, int FrxReports, string Details);
}
