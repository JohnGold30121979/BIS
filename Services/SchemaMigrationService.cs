using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Единый механизм миграций схемы при открытии информационной базы.
    ///
    /// Заменяет разрозненные «починим при старте»: все шаги собраны здесь,
    /// пронумерованы и записываются в журнал schema_migrations. Повторяемые шаги
    /// (достройка колонок) выполняются при каждом открытии базы — это самовосстановление;
    /// разовые (чистки, разметка) — только пока не отмечены в журнале.
    ///
    /// Шаги аддитивные и идемпотентные: существующие данные и колонки не меняются.
    /// Ошибка одного шага не блокирует открытие базы — она логируется и идёт дальше.
    /// </summary>
    public sealed class SchemaMigrationService
    {
        private sealed record Step(
            string Version,
            string Description,
            bool Repeatable,
            Func<AppDbContext, Task> Apply);

        private readonly AppDbContext _context;
        private readonly List<Step> _steps;

        public SchemaMigrationService(AppDbContext context)
        {
            _context = context;

            _steps = new List<Step>
            {
                new("2026.10.19.01",
                    "Колонки EF-модели в существующих таблицах (Reports.ClosedXmlLayout и подобные)",
                    true,
                    async ctx => { await new SchemaRepairService(ctx).RepairAsync(); }),

                new("2026.10.19.02",
                    "Колонки справочников и документов по метаданным (bank_name и подобные)",
                    true,
                    async ctx =>
                    {
                        var (tables, columns) = await new MetadataSchemaRepairService(ctx).RepairAsync();
                        if (columns > 0)
                        {
                            SystemLogService.Warning(
                                $"Схема: достроены колонки справочников/документов — {columns} (проверено таблиц: {tables}).",
                                "Schema");
                        }
                    }),

                new("2026.10.19.03",
                    "Схема кассовых дней и закрытия периода",
                    true,
                    ctx => new CashDayClosureService(ctx).EnsureSchemaAsync()),

                new("2026.10.19.04",
                    "Схема наборов данных отчётов",
                    true,
                    ctx => new ReportDataSetService(ctx).EnsureSchemaAsync()),

                new("2026.10.19.05",
                    "Разметка отчётов для окна «Бухгалтерская отчётность»",
                    false,
                    MarkAccountingReportsAsync),

                new("2026.10.19.06",
                    "Разовая чистка FRX-отчётов (этап 2)",
                    false,
                    ctx => new FrxReportsCleanupService(ctx).RunOnceAsync()),

                new("2026.10.19.07",
                    "Схема периодов, остатков и финансовых отчётов (перенесено из AccountingPeriodService)",
                    true,
                    EnsureAccountingPeriodSchemaAsync),

                new("2026.10.20.01",
                    "Канонические имена таблиц документов ОС/ТМЦ (doc_asset_*, doc_inventory_*)",
                    false,
                    NormalizeDocumentTableNamesAsync),

                new("2026.10.21.01",
                    "Модель документов ТМЗ: 3 документа + строки + справочник видов операций",
                    false,
                    EnsureInventoryDocumentModelAsync),

                new("2026.10.22.01",
                    "Справочник видов операций ТМЗ: колонка operation_kind",
                    false,
                    EnsureInventoryEntryOperationKindsAsync),

                new("2026.10.22.02",
                    "doc_postings: colonnes document_table/document_id (восстановление схемы при отсутствии)",
                    true,
                    EnsurePostingDocumentTableColumnsAsync),

                new("2026.10.26.01",
                    "Пары счетов: досид предустановленных записей (счёта — по коду из плана счетов)",
                    true,
                    EnsureAccountPairsSeedAsync),

                new("2026.10.27.01",
                    "Справочник «Ввод нового документа ТМЗ» → «Вид операции ТМЗ»",
                    false,
                    RenameInventoryEntryCatalogAsync)
            };
        }

        /// <summary>
        /// Предустановленные пары счетов («Виды расчета»).
        ///
        /// Шаг повторяемый: досид идёт по коду записи и ничего не перезаписывает,
        /// поэтому он же лечит базы, созданные до исправления сида. Раньше счёт в сиде
        /// задавался кодом («18200000»), а debit_account/credit_account — uuid-ссылки
        /// на план счетов: вставка падала с 22P02, ошибка уходила в Debug, и справочник
        /// оставался пустым.
        /// </summary>
        private static async Task EnsureAccountPairsSeedAsync(AppDbContext ctx)
        {
            var added = await new MetadataService(ctx).EnsureAccountPairsSeedDataAsync();
            if (added > 0)
            {
                SystemLogService.Info(
                    $"Пары счетов: добавлены предустановленные записи — {added}.",
                    "Schema");
            }
        }

        private static async Task EnsurePostingDocumentTableColumnsAsync(AppDbContext ctx)
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF to_regclass('public.doc_postings') IS NOT NULL THEN
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS document_table text;
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS document_id uuid;
                    END IF;
                END $$;");
        }

        public async Task<SchemaMigrationReport> EnsureAsync()
        {
            await EnsureJournalAsync();

            var alreadyApplied = await LoadAppliedAsync();
            var applied = 0;
            var skipped = 0;
            var failures = new List<string>();

            foreach (var step in _steps)
            {
                if (!step.Repeatable && alreadyApplied.Contains(step.Version))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    await step.Apply(_context);
                    await RecordAsync(step);
                    applied++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{step.Version} {step.Description}: {ex.Message}");
                    SystemLogService.Warning(
                        $"Миграция {step.Version} ({step.Description}) не выполнена: {ex.Message}", "Migration");
                }
            }

            if (applied > 0 || failures.Count > 0)
            {
                SystemLogService.Info(
                    $"Миграции схемы: выполнено {applied}, пропущено {skipped}, ошибок {failures.Count}.",
                    "Migration");
            }

            return new SchemaMigrationReport(applied, skipped, failures);
        }

        private async Task EnsureJournalAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version     text PRIMARY KEY,
                    description text NOT NULL DEFAULT '',
                    applied_at  timestamp with time zone NOT NULL DEFAULT now(),
                    runs        integer NOT NULL DEFAULT 1
                );
                ALTER TABLE schema_migrations
                    ADD COLUMN IF NOT EXISTS last_run_at timestamp with time zone;
                UPDATE schema_migrations
                   SET last_run_at = applied_at
                 WHERE last_run_at IS NULL;");
        }

        private async Task<HashSet<string>> LoadAppliedAsync()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT version FROM schema_migrations";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(reader.GetString(0));
            }
            finally
            {
                if (openedHere)
                    await connection.CloseAsync();
            }

            return result;
        }

        private async Task RecordAsync(Step step)
        {
            // applied_at — момент ПЕРВОГО применения шага и больше не меняется.
            // last_run_at — последний успешный прогон (повторяемые шаги идут при каждом открытии).
            // runs — сколько раз шаг фактически выполнялся.
            var sql = step.Repeatable
                ? @"
                INSERT INTO schema_migrations (version, description, applied_at, last_run_at, runs)
                VALUES (@version, @description, now(), clock_timestamp(), 1)
                ON CONFLICT (version) DO UPDATE
                SET last_run_at = clock_timestamp(),
                    runs = schema_migrations.runs + 1;"
                : @"
                INSERT INTO schema_migrations (version, description, applied_at, last_run_at, runs)
                VALUES (@version, @description, now(), clock_timestamp(), 1)
                ON CONFLICT (version) DO UPDATE
                SET applied_at = now(),
                    last_run_at = clock_timestamp(),
                    runs = schema_migrations.runs + 1;";

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("version", step.Version),
                new NpgsqlParameter("description", step.Description));
        }

        /// <summary>
        /// Схема периодов, остатков и финансовых отчётов: CREATE TABLE IF NOT EXISTS,
        /// ALTER ... ADD COLUMN IF NOT EXISTS и индексы. Раньше этот DDL жил внутри
        /// AccountingPeriodService; теперь единственный источник — здесь,
        /// а сервис вызывает этот метод.
        /// </summary>
        /// <summary>
        /// Миграция справочника видов операций ТМЗ: добавляет operation_kind
        /// и заполняет его из code для существующих строк.
        /// </summary>
        private static async Task EnsureInventoryEntryOperationKindsAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            await new InventoryDocumentSchemaService(isolated).EnsureEntryCatalogOperationKindAsync();
        }

        /// <summary>
        /// Справочник видов операций ТМЗ переименован: «Ввод нового документа ТМЗ» → «Вид операции ТМЗ».
        /// Шаг разовый; идемпотентен: на базе с новым именем ничего не делает.
        /// </summary>
        private static async Task RenameInventoryEntryCatalogAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var report = await new InventoryDocumentSchemaService(isolated).EnsureEntryCatalogRenamedAsync();

            foreach (var action in report.Actions)
                SystemLogService.Info($"ТМЗ: {action}", "Migration");

            foreach (var warning in report.Warnings)
                SystemLogService.Warning($"ТМЗ: {warning}", "Migration");
        }

        /// <summary>
        /// Модель документов ТМЗ: Поступление / Выбытие / Внутреннее перемещение,
        /// у каждого — таблица строк и вид операции реквизитом.
        /// Шаг разовый; идемпотентен (см. InventoryDocumentSchemaService).
        /// </summary>
        private static async Task EnsureInventoryDocumentModelAsync(AppDbContext context)
        {
            // Отдельный контекст: если шаг упадёт, «грязные» отслеживаемые записи
            // не должны ронять остальные шаги и загрузку главного окна
            // (инцидент 20.10.2026: DbUpdateConcurrencyException в MetadataFields).
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var report = await new InventoryDocumentSchemaService(isolated).EnsureAsync();

            foreach (var action in report.Actions)
                SystemLogService.Info($"ТМЗ: {action}", "Migration");

            foreach (var warning in report.Warnings)
                SystemLogService.Warning($"ТМЗ: {warning}", "Migration");
        }

        /// <summary>
        /// Приводит имена таблиц документов ОС/ТМЦ к канону <see cref="DocumentTableNames"/>.
        /// Шаг разовый: после успешного выполнения фиксируется в schema_migrations и больше
        /// не выполняется. Если прогон упал — запись не создаётся, и шаг повторится при
        /// следующем открытии базы. Идемпотентен (см. DocumentTableNameNormalizer).
        /// </summary>
        private static async Task NormalizeDocumentTableNamesAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var results = await new DocumentTableNameNormalizer(isolated).NormalizeAsync();
            var renamed = results.Where(item => item.Renamed).Count();

            foreach (var result in results.Where(item => !string.IsNullOrWhiteSpace(item.SkippedReason)))
            {
                SystemLogService.Warning(
                    $"doc-names: {result.DocumentName} — {result.SkippedReason} ({result.OldTableName} -> {result.NewTableName})",
                    "Migration");
            }

            if (renamed > 0)
            {
                SystemLogService.Info($"doc-names: переименовано таблиц — {renamed}.", "Migration");
            }
        }

        public static Task EnsureAccountingPeriodSchemaAsync(AppDbContext context)
            => context.Database.ExecuteSqlRawAsync(AccountingPeriodSchemaSql);

        private const string AccountingPeriodSchemaSql = @"
                CREATE TABLE IF NOT EXISTS ""AccountingPeriods"" (
                    ""Id"" uuid PRIMARY KEY, ""StartDate"" timestamp with time zone NOT NULL,
                    ""EndDate"" timestamp with time zone NOT NULL, ""Status"" varchar(20) NOT NULL,
                    ""CollectedAt"" timestamp with time zone NULL, ""ClosedAt"" timestamp with time zone NULL,
                    ""IsLocked"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountingPeriods_StartDate_EndDate: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountingPeriodModuleStates"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""ModuleId"" uuid NOT NULL,
                    ""IsClosed"" boolean NOT NULL DEFAULT false, ""ClosedAt"" timestamp with time zone NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountingPeriodModuleStates_Period_Module: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountOpeningBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""BalanceDate"" timestamp with time zone NOT NULL,
                    ""AccountCode"" varchar(50) NOT NULL, ""Debit"" numeric(18,2) NOT NULL,
                    ""Credit"" numeric(18,2) NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountOpeningBalances_Date_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountTurnoverSnapshots"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL,
                    ""AccountName"" varchar(300) NOT NULL, ""OpeningDebit"" numeric(18,2) NOT NULL,
                    ""OpeningCredit"" numeric(18,2) NOT NULL, ""TurnoverDebit"" numeric(18,2) NOT NULL,
                    ""TurnoverCredit"" numeric(18,2) NOT NULL, ""ClosingDebit"" numeric(18,2) NOT NULL,
                    ""ClosingCredit"" numeric(18,2) NOT NULL, ""CreatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountTurnoverSnapshots_Period_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FixedAssetPeriodBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL,
                    ""PeriodStart"" timestamp with time zone NOT NULL, ""PeriodEnd"" timestamp with time zone NOT NULL,
                    ""AssetId"" uuid NOT NULL, ""InventoryNumber"" varchar(50) NOT NULL,
                    ""AssetName"" varchar(300) NOT NULL, ""OrganizationId"" uuid NULL,
                    ""OrganizationName"" varchar(300) NOT NULL DEFAULT '', ""ResponsiblePersonId"" uuid NULL,
                    ""ResponsiblePersonName"" varchar(300) NOT NULL DEFAULT '', ""SiteId"" uuid NULL,
                    ""SiteName"" varchar(300) NOT NULL DEFAULT '', ""AssetAccount"" varchar(100) NOT NULL DEFAULT '',
                    ""DepreciationAccount"" varchar(100) NOT NULL DEFAULT '', ""ExpenseAccount"" varchar(100) NOT NULL DEFAULT '',
                    ""AcquisitionDate"" timestamp with time zone NULL, ""CommissioningDate"" timestamp with time zone NULL,
                    ""DepreciationStartDate"" timestamp with time zone NULL, ""InitialCost"" numeric(18,2) NOT NULL,
                    ""SalvageValue"" numeric(18,2) NOT NULL, ""AccumulatedDepreciation"" numeric(18,2) NOT NULL,
                    ""CarryingAmount"" numeric(18,2) NOT NULL, ""MonthlyDepreciation"" numeric(18,2) NOT NULL,
                    ""IsActive"" boolean NOT NULL, ""LifecycleStatus"" varchar(100) NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp with time zone NOT NULL);
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodStart"" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodEnd"" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OrganizationName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ResponsiblePersonName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""SiteName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""SalvageValue"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""MonthlyDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningCarryingAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""AcquisitionCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DisposalCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferInCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferOutCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""RevaluationCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""AutomaticDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ManualDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DepreciationAdjustment"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DepreciationWriteOff"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DisposalDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferInDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferOutDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingCarryingAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                -- IX_FixedAssetPeriodBalances_Period_Asset: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FinancialReportLines"" (
                    ""Id"" uuid PRIMARY KEY, ""ReportCode"" varchar(30) NOT NULL, ""LineCode"" varchar(30) NOT NULL,
                    ""SectionCode"" varchar(30) NOT NULL, ""Name"" varchar(300) NOT NULL,
                    ""SortOrder"" integer NOT NULL, ""Sign"" integer NOT NULL, ""IsTotal"" boolean NOT NULL,
                    ""Formula"" varchar(500) NOT NULL DEFAULT '', ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""IsActive"" boolean NOT NULL);
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""Formula"" varchar(500) NOT NULL DEFAULT '';
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                -- IX_FinancialReportLines_Report_Line: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FinancialReportLineAccounts"" (
                    ""Id"" uuid PRIMARY KEY, ""LineId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL);
                -- IX_FinancialReportLineAccounts_Line_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""TaxJournalRecords"" (
                    ""Id"" uuid PRIMARY KEY, ""JournalType"" varchar(20) NOT NULL,
                    ""Date"" timestamp with time zone NOT NULL, ""DocumentNumber"" varchar(50) NOT NULL,
                    ""DocumentType"" varchar(100) NOT NULL, ""Organization"" varchar(300) NOT NULL,
                    ""TaxType"" varchar(50) NOT NULL, ""AmountWithoutTax"" numeric(18,2) NOT NULL,
                    ""TaxAmount"" numeric(18,2) NOT NULL, ""TotalAmount"" numeric(18,2) NOT NULL,
                    ""SourceRecordId"" uuid NULL, ""CreatedAt"" timestamp with time zone NOT NULL);";

        private static readonly string[] DefaultAccountingReportCodes =
        {
            "standard.finance.trial-balance-closedxml",
            "standard.finance.reconciliation-act-closedxml",
            "standard.finance.invoice-journal",
            "standard.finance.vat-invoice-st1"
        };

        /// <summary>
        /// Разово отмечает рабочие отчёты для окна «Бухгалтерская отчётность».
        /// Если администратор уже что-то отметил — выбор не перетирается.
        /// </summary>
        private static async Task MarkAccountingReportsAsync(AppDbContext context)
        {
            var anyMarked = await context.Reports
                .AsNoTracking()
                .AnyAsync(report => report.ShowInAccountingReports);

            if (anyMarked)
                return;

            var codes = DefaultAccountingReportCodes;

            await context.Reports
                .Where(report => codes.Contains(report.Code))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(report => report.ShowInAccountingReports, true)
                    .SetProperty(report => report.IsActive, true));
        }
    }

    public sealed record SchemaMigrationReport(int Applied, int Skipped, IReadOnlyList<string> Failures);
}
