using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class AccountingPeriodService
    {
        private readonly AppDbContext _context;

        private sealed record FinancialReportLinePreset(
            string ReportCode,
            string LineCode,
            string SectionCode,
            string Name,
            int SortOrder,
            int Sign,
            bool IsTotal,
            string Formula,
            decimal FixedAmount,
            string[] AccountCodePrefixes);

        public AccountingPeriodService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS ""AccountingPeriods"" (
                    ""Id"" uuid PRIMARY KEY, ""StartDate"" timestamp with time zone NOT NULL,
                    ""EndDate"" timestamp with time zone NOT NULL, ""Status"" varchar(20) NOT NULL,
                    ""CollectedAt"" timestamp with time zone NULL, ""ClosedAt"" timestamp with time zone NULL,
                    ""IsLocked"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountingPeriods_StartDate_EndDate""
                    ON ""AccountingPeriods"" (""StartDate"", ""EndDate"");
                CREATE TABLE IF NOT EXISTS ""AccountingPeriodModuleStates"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""ModuleId"" uuid NOT NULL,
                    ""IsClosed"" boolean NOT NULL DEFAULT false, ""ClosedAt"" timestamp with time zone NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountingPeriodModuleStates_Period_Module""
                    ON ""AccountingPeriodModuleStates"" (""PeriodId"", ""ModuleId"");
                CREATE TABLE IF NOT EXISTS ""AccountOpeningBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""BalanceDate"" timestamp with time zone NOT NULL,
                    ""AccountCode"" varchar(50) NOT NULL, ""Debit"" numeric(18,2) NOT NULL,
                    ""Credit"" numeric(18,2) NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountOpeningBalances_Date_Account""
                    ON ""AccountOpeningBalances"" (""BalanceDate"", ""AccountCode"");
                CREATE TABLE IF NOT EXISTS ""AccountTurnoverSnapshots"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL,
                    ""AccountName"" varchar(300) NOT NULL, ""OpeningDebit"" numeric(18,2) NOT NULL,
                    ""OpeningCredit"" numeric(18,2) NOT NULL, ""TurnoverDebit"" numeric(18,2) NOT NULL,
                    ""TurnoverCredit"" numeric(18,2) NOT NULL, ""ClosingDebit"" numeric(18,2) NOT NULL,
                    ""ClosingCredit"" numeric(18,2) NOT NULL, ""CreatedAt"" timestamp with time zone NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountTurnoverSnapshots_Period_Account""
                    ON ""AccountTurnoverSnapshots"" (""PeriodId"", ""AccountCode"");
                CREATE TABLE IF NOT EXISTS ""FinancialReportLines"" (
                    ""Id"" uuid PRIMARY KEY, ""ReportCode"" varchar(30) NOT NULL, ""LineCode"" varchar(30) NOT NULL,
                    ""SectionCode"" varchar(30) NOT NULL, ""Name"" varchar(300) NOT NULL,
                    ""SortOrder"" integer NOT NULL, ""Sign"" integer NOT NULL, ""IsTotal"" boolean NOT NULL,
                    ""Formula"" varchar(500) NOT NULL DEFAULT '', ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""IsActive"" boolean NOT NULL);
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""Formula"" varchar(500) NOT NULL DEFAULT '';
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FinancialReportLines_Report_Line""
                    ON ""FinancialReportLines"" (""ReportCode"", ""LineCode"");
                CREATE TABLE IF NOT EXISTS ""FinancialReportLineAccounts"" (
                    ""Id"" uuid PRIMARY KEY, ""LineId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FinancialReportLineAccounts_Line_Account""
                    ON ""FinancialReportLineAccounts"" (""LineId"", ""AccountCode"");
                CREATE TABLE IF NOT EXISTS ""TaxJournalRecords"" (
                    ""Id"" uuid PRIMARY KEY, ""JournalType"" varchar(20) NOT NULL,
                    ""Date"" timestamp with time zone NOT NULL, ""DocumentNumber"" varchar(50) NOT NULL,
                    ""DocumentType"" varchar(100) NOT NULL, ""Organization"" varchar(300) NOT NULL,
                    ""TaxType"" varchar(50) NOT NULL, ""AmountWithoutTax"" numeric(18,2) NOT NULL,
                    ""TaxAmount"" numeric(18,2) NOT NULL, ""TotalAmount"" numeric(18,2) NOT NULL,
                    ""SourceRecordId"" uuid NULL, ""CreatedAt"" timestamp with time zone NOT NULL);";
            await _context.Database.ExecuteSqlRawAsync(sql);
            await SeedFinancialReportLinesAsync();
        }

        public async Task<AccountingPeriod> CollectAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureSchemaAsync();
            var start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);
            if (end < start)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала.");
            var overlappingPeriod = await _context.AccountingPeriods.AsNoTracking().AnyAsync(item =>
                !(end < item.StartDate || start > item.EndDate) &&
                !(item.StartDate == start && item.EndDate == end));
            if (overlappingPeriod)
                throw new InvalidOperationException("Выбранный диапазон пересекается с другим учетным периодом.");
            var period = await _context.AccountingPeriods
                .FirstOrDefaultAsync(item => item.StartDate == start && item.EndDate == end);
            period ??= new AccountingPeriod { StartDate = start, EndDate = end };
            if (period.IsLocked)
                throw new InvalidOperationException("Закрытый период нельзя пересобирать. Сначала откройте период.");

            if (_context.Entry(period).State == EntityState.Detached)
                await _context.AccountingPeriods.AddAsync(period);
            await _context.SaveChangesAsync();

            await EnsureFixedAssetPeriodClosingAsync(startDate, endDate);
            var balances = await new BalanceService(_context).GetTurnoverBalanceAsync(startDate, endDate);
            var previous = await _context.AccountTurnoverSnapshots.Where(item => item.PeriodId == period.Id).ToListAsync();
            _context.AccountTurnoverSnapshots.RemoveRange(previous);
            await _context.AccountTurnoverSnapshots.AddRangeAsync(balances.Select(balance => new AccountTurnoverSnapshot
            {
                PeriodId = period.Id, AccountCode = balance.AccountCode, AccountName = balance.AccountName,
                OpeningDebit = balance.OpeningDebit, OpeningCredit = balance.OpeningCredit,
                TurnoverDebit = balance.TurnoverDebit, TurnoverCredit = balance.TurnoverCredit,
                ClosingDebit = balance.ClosingDebit, ClosingCredit = balance.ClosingCredit
            }));
            await new OrganizationBalanceService(_context).CalculateAsync(startDate, endDate);
            await SynchronizeTaxJournalAsync(startDate, endDate);
            await ResetPeriodModuleStatesAsync(period.Id);
            period.Status = "Collected";
            period.CollectedAt = DateTime.UtcNow;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return period;
        }

        public async Task CloseAsync(Guid periodId)
        {
            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден");
            if (period.Status != "Collected")
                throw new InvalidOperationException("Перед закрытием выполните сбор информации за период");
            await EnsureAllModulesClosedAsync(periodId);
            var snapshots = await _context.AccountTurnoverSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.PeriodId == periodId).ToListAsync();
            var openingDifference = snapshots.Sum(item => item.OpeningDebit - item.OpeningCredit);
            var turnoverDifference = snapshots.Sum(item => item.TurnoverDebit - item.TurnoverCredit);
            var closingDifference = snapshots.Sum(item => item.ClosingDebit - item.ClosingCredit);
            if (Math.Abs(openingDifference) >= 0.01m || Math.Abs(turnoverDifference) >= 0.01m ||
                Math.Abs(closingDifference) >= 0.01m)
                throw new InvalidOperationException("Период не закрыт: контроль дебета и кредита не пройден.");
            period.Status = "Closed";
            period.IsLocked = true;
            period.ClosedAt = DateTime.UtcNow;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task ReopenAsync(Guid periodId)
        {
            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден");
            period.Status = "Open";
            period.IsLocked = false;
            period.ClosedAt = null;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await ResetPeriodModuleStatesAsync(periodId);
        }

        public async Task<AccountingPeriod?> FindAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureSchemaAsync();
            var start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);
            return await _context.AccountingPeriods.AsNoTracking()
                .FirstOrDefaultAsync(item => item.StartDate == start && item.EndDate == end);
        }

        public async Task<List<AccountingPeriodModuleStatus>> GetModuleStatusesAsync(Guid periodId)
        {
            await EnsureSchemaAsync();
            await EnsurePeriodModuleStatesAsync(periodId);

            var modules = await _context.MetadataModules.AsNoTracking()
                .Where(module => module.IsActive)
                .OrderBy(module => module.CloseOrder)
                .ThenBy(module => module.Order)
                .ThenBy(module => module.Name)
                .ToListAsync();

            var states = await _context.AccountingPeriodModuleStates.AsNoTracking()
                .Where(state => state.PeriodId == periodId)
                .ToDictionaryAsync(state => state.ModuleId);

            return modules.Select(module =>
            {
                states.TryGetValue(module.Id, out var state);
                return new AccountingPeriodModuleStatus
                {
                    PeriodId = periodId,
                    ModuleId = module.Id,
                    ModuleCode = module.Code,
                    ModuleName = module.Name,
                    CloseOrder = module.CloseOrder,
                    ParticipatesInPeriodClose = module.ParticipatesInPeriodClose,
                    RequirePreviousModulesClosed = module.RequirePreviousModulesClosed,
                    IsClosed = state?.IsClosed == true,
                    ClosedAt = state?.ClosedAt
                };
            }).ToList();
        }

        public async Task CloseModuleAsync(Guid periodId, Guid moduleId)
        {
            await EnsureSchemaAsync();
            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден.");
            if (period.IsLocked)
                throw new InvalidOperationException("Период уже закрыт итоговым балансом. Сначала откройте период.");
            if (period.Status != "Collected")
                throw new InvalidOperationException("Сначала выполните сбор информации за период.");

            await EnsurePeriodModuleStatesAsync(periodId);

            var module = await _context.MetadataModules.FindAsync(moduleId)
                ?? throw new InvalidOperationException("Модуль не найден.");
            if (!module.IsActive)
                throw new InvalidOperationException("Модуль отключен и не может участвовать в закрытии периода.");
            if (!module.ParticipatesInPeriodClose)
                throw new InvalidOperationException("Для модуля отключено участие в закрытии периода.");

            if (IsFinanceModule(module))
                await EnsureFinanceModuleCanBeClosedAsync(periodId, module.Id);

            if (module.RequirePreviousModulesClosed)
            {
                var previousModules = await _context.MetadataModules.AsNoTracking()
                    .Where(item =>
                        item.IsActive &&
                        item.ParticipatesInPeriodClose &&
                        item.CloseOrder < module.CloseOrder)
                    .OrderBy(item => item.CloseOrder)
                    .ThenBy(item => item.Name)
                    .ToListAsync();

                if (previousModules.Count > 0)
                {
                    var previousIds = previousModules.Select(item => item.Id).ToHashSet();
                    var previousStates = await _context.AccountingPeriodModuleStates.AsNoTracking()
                        .Where(item => item.PeriodId == periodId && previousIds.Contains(item.ModuleId))
                        .ToListAsync();

                    var openPrevious = previousModules
                        .Where(previous => previousStates.All(state => state.ModuleId != previous.Id || !state.IsClosed))
                        .Select(previous => previous.Name)
                        .ToList();

                    if (openPrevious.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Модуль «{module.Name}» можно закрыть только после модулей: {string.Join(", ", openPrevious)}.");
                    }
                }
            }

            var state = await _context.AccountingPeriodModuleStates
                .FirstOrDefaultAsync(item => item.PeriodId == periodId && item.ModuleId == moduleId)
                ?? throw new InvalidOperationException("Не найдено состояние модуля для выбранного периода.");

            state.IsClosed = true;
            state.ClosedAt = DateTime.UtcNow;
            state.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task ReopenModuleAsync(Guid periodId, Guid moduleId)
        {
            await EnsureSchemaAsync();
            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден.");
            if (period.IsLocked)
                throw new InvalidOperationException("Сначала откройте итогово закрытый период.");

            await EnsurePeriodModuleStatesAsync(periodId);

            var state = await _context.AccountingPeriodModuleStates
                .FirstOrDefaultAsync(item => item.PeriodId == periodId && item.ModuleId == moduleId)
                ?? throw new InvalidOperationException("Не найдено состояние модуля для выбранного периода.");

            state.IsClosed = false;
            state.ClosedAt = null;
            state.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task EnsureDateCanBeModifiedAsync(DateTime date)
        {
            await EnsureSchemaAsync();
            var utcDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var locked = await _context.AccountingPeriods.AsNoTracking().AnyAsync(period =>
                period.IsLocked && utcDate >= period.StartDate && utcDate <= period.EndDate);
            if (locked)
                throw new InvalidOperationException($"Период, содержащий дату {date:dd.MM.yyyy}, закрыт. Изменение документов запрещено.");
        }

        private async Task EnsurePeriodModuleStatesAsync(Guid periodId)
        {
            var modules = await _context.MetadataModules
                .Where(module => module.IsActive && module.ParticipatesInPeriodClose)
                .OrderBy(module => module.CloseOrder)
                .ThenBy(module => module.Name)
                .ToListAsync();

            var states = await _context.AccountingPeriodModuleStates
                .Where(state => state.PeriodId == periodId)
                .ToListAsync();

            var moduleIds = modules.Select(module => module.Id).ToHashSet();
            var orphaned = states.Where(state => !moduleIds.Contains(state.ModuleId)).ToList();
            if (orphaned.Count > 0)
                _context.AccountingPeriodModuleStates.RemoveRange(orphaned);

            foreach (var module in modules)
            {
                if (states.Any(state => state.ModuleId == module.Id))
                    continue;

                await _context.AccountingPeriodModuleStates.AddAsync(new AccountingPeriodModuleState
                {
                    PeriodId = periodId,
                    ModuleId = module.Id,
                    IsClosed = false
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task ResetPeriodModuleStatesAsync(Guid periodId)
        {
            await EnsurePeriodModuleStatesAsync(periodId);
            var states = await _context.AccountingPeriodModuleStates
                .Where(state => state.PeriodId == periodId)
                .ToListAsync();

            foreach (var state in states)
            {
                state.IsClosed = false;
                state.ClosedAt = null;
                state.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsureAllModulesClosedAsync(Guid periodId)
        {
            await EnsurePeriodModuleStatesAsync(periodId);
            var status = await GetModuleStatusesAsync(periodId);
            var openModules = status
                .Where(item => item.ParticipatesInPeriodClose && !item.IsClosed)
                .Select(item => item.ModuleName)
                .ToList();

            if (openModules.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Нельзя закрыть период итоговым балансом. Сначала закройте модули: {string.Join(", ", openModules)}.");
            }
        }

        private async Task EnsureFinanceModuleCanBeClosedAsync(Guid periodId, Guid financeModuleId)
        {
            var modules = await _context.MetadataModules.AsNoTracking()
                .Where(item =>
                    item.IsActive &&
                    item.ParticipatesInPeriodClose &&
                    item.Id != financeModuleId)
                .OrderBy(item => item.CloseOrder)
                .ThenBy(item => item.Name)
                .ToListAsync();

            if (modules.Count == 0)
                return;

            var moduleIds = modules.Select(item => item.Id).ToHashSet();
            var states = await _context.AccountingPeriodModuleStates.AsNoTracking()
                .Where(item => item.PeriodId == periodId && moduleIds.Contains(item.ModuleId))
                .ToListAsync();

            var openModules = modules
                .Where(module => states.All(state => state.ModuleId != module.Id || !state.IsClosed))
                .Select(module => module.Name)
                .ToList();

            if (openModules.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Модуль «Финансы» можно закрыть только после модулей: {string.Join(", ", openModules)}.");
            }
        }

        private static bool IsFinanceModule(MetadataModule module) =>
            module.Code.Equals(ModuleMetadataService.FinanceCode, StringComparison.OrdinalIgnoreCase) ||
            module.Name.Equals("Финансы", StringComparison.OrdinalIgnoreCase);

        private async Task SynchronizeTaxJournalAsync(DateTime startDate, DateTime endDate)
        {
            var entries = await new BalanceService(_context).GetPurchaseSalesJournalAsync(startDate, endDate);
            var utcStart = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var utcEnd = DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Utc);
            var previous = await _context.TaxJournalRecords
                .Where(record => record.Date >= utcStart && record.Date < utcEnd).ToListAsync();
            _context.TaxJournalRecords.RemoveRange(previous);
            await _context.TaxJournalRecords.AddRangeAsync(entries.Where(entry => entry.IsPosted).Select(entry => new TaxJournalRecord
            {
                JournalType = entry.Section == "Закупки" ? "Purchase" : "Sale",
                Date = DateTime.SpecifyKind(entry.Date, DateTimeKind.Utc),
                DocumentNumber = entry.DocumentNumber,
                DocumentType = entry.DocumentType,
                Organization = entry.Organization,
                TaxType = entry.TaxType,
                AmountWithoutTax = entry.AmountWithoutTax,
                TaxAmount = entry.VatAmount + entry.SalesTaxAmount,
                TotalAmount = entry.Amount
            }));
        }

        private async Task SeedFinancialReportLinesAsync()
        {
            foreach (var preset in BuildFinancialReportLinePresets())
            {
                var line = await _context.FinancialReportLines
                    .FirstOrDefaultAsync(item => item.ReportCode == preset.ReportCode && item.LineCode == preset.LineCode);
                var isNewLine = false;

                if (line == null)
                {
                    line = new FinancialReportLine
                    {
                        ReportCode = preset.ReportCode,
                        LineCode = preset.LineCode,
                        SectionCode = preset.SectionCode,
                        Name = preset.Name,
                        SortOrder = preset.SortOrder,
                        Sign = preset.Sign,
                        IsTotal = preset.IsTotal,
                        Formula = preset.Formula,
                        FixedAmount = preset.FixedAmount,
                        IsActive = true
                    };
                    await _context.FinancialReportLines.AddAsync(line);
                    isNewLine = true;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(line.SectionCode))
                        line.SectionCode = preset.SectionCode;
                    if (string.IsNullOrWhiteSpace(line.Name))
                        line.Name = preset.Name;
                    if (line.SortOrder == 0)
                        line.SortOrder = preset.SortOrder;
                    if (line.Sign == 0)
                        line.Sign = preset.Sign;
                    if (string.IsNullOrWhiteSpace(line.Formula) && !string.IsNullOrWhiteSpace(preset.Formula))
                        line.Formula = preset.Formula;
                    if (line.FixedAmount == 0 && preset.FixedAmount != 0)
                        line.FixedAmount = preset.FixedAmount;
                }

                await _context.SaveChangesAsync();

                if (preset.AccountCodePrefixes.Length == 0)
                    continue;

                var hasConfiguredAccounts = await _context.FinancialReportLineAccounts
                    .AnyAsync(item => item.LineId == line.Id);
                if (!isNewLine && hasConfiguredAccounts)
                    continue;

                foreach (var accountCodePrefix in preset.AccountCodePrefixes
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var exists = await _context.FinancialReportLineAccounts
                        .AnyAsync(item => item.LineId == line.Id && item.AccountCode == accountCodePrefix);
                    if (exists)
                        continue;

                    await _context.FinancialReportLineAccounts.AddAsync(new FinancialReportLineAccount
                    {
                        LineId = line.Id,
                        AccountCode = accountCodePrefix
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        private static FinancialReportLinePreset[] BuildFinancialReportLinePresets()
        {
            return new[]
            {
                new FinancialReportLinePreset(
                    "Balance", "100", "Assets", "Активы", 100, 1, true, "110+120", 0m, Array.Empty<string>()),
                new FinancialReportLinePreset(
                    "Balance", "110", "Assets", "Внеоборотные активы", 110, 1, false, string.Empty, 0m,
                    new[] { "11", "12", "13" }),
                new FinancialReportLinePreset(
                    "Balance", "120", "Assets", "Оборотные активы", 120, 1, false, string.Empty, 0m,
                    new[] { "14", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24", "25", "26", "27", "28", "29" }),

                new FinancialReportLinePreset(
                    "Balance", "200", "Liabilities", "Обязательства", 200, 1, true, "210+220", 0m, Array.Empty<string>()),
                new FinancialReportLinePreset(
                    "Balance", "210", "Liabilities", "Краткосрочные обязательства", 210, 1, false, string.Empty, 0m,
                    new[] { "31", "32", "33", "34", "35", "36", "37", "38", "39" }),
                new FinancialReportLinePreset(
                    "Balance", "220", "Liabilities", "Долгосрочные обязательства", 220, 1, false, string.Empty, 0m,
                    new[] { "41", "42", "43", "44", "45", "46", "47", "48", "49" }),

                new FinancialReportLinePreset(
                    "Balance", "300", "Equity", "Капитал", 300, 1, true, string.Empty, 0m,
                    new[] { "51", "52", "53", "54", "55", "56", "57", "58", "59" }),

                new FinancialReportLinePreset(
                    "ProfitLoss", "100", "Income", "Выручка от операционной деятельности", 100, 1, false, string.Empty, 0m,
                    new[] { "61", "62", "63" }),
                new FinancialReportLinePreset(
                    "ProfitLoss", "110", "Income", "Прочие доходы", 110, 1, false, string.Empty, 0m,
                    new[] { "91" }),
                new FinancialReportLinePreset(
                    "ProfitLoss", "190", "Income", "Итого доходы", 190, 1, true, "100+110", 0m, Array.Empty<string>()),

                new FinancialReportLinePreset(
                    "ProfitLoss", "200", "Expenses", "Себестоимость и прямые расходы", 200, 1, false, string.Empty, 0m,
                    new[] { "71", "72", "73", "74" }),
                new FinancialReportLinePreset(
                    "ProfitLoss", "210", "Expenses", "Коммерческие и административные расходы", 210, 1, false, string.Empty, 0m,
                    new[] { "75", "76", "77", "78", "79", "80", "81", "82", "83", "84", "85", "86", "87", "88", "89" }),
                new FinancialReportLinePreset(
                    "ProfitLoss", "220", "Expenses", "Прочие расходы и налоги", 220, 1, false, string.Empty, 0m,
                    new[] { "95", "99" }),
                new FinancialReportLinePreset(
                    "ProfitLoss", "290", "Expenses", "Итого расходы", 290, 1, true, "200+210+220", 0m, Array.Empty<string>())
            };
        }

        private async Task EnsureFixedAssetPeriodClosingAsync(DateTime startDate, DateTime endDate)
        {
            var monthEnds = GetCoveredMonthEnds(startDate, endDate);
            if (monthEnds.Count == 0)
                return;

            var metadataService = new MetadataService(_context);
            var assetCatalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            var depreciationDocument = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Начисление амортизации");

            if (assetCatalog == null || depreciationDocument == null)
                return;

            var assets = await metadataService.GetCatalogDataAsync(assetCatalog.Id);
            var existingDocuments = await metadataService.GetCatalogDataAsync(depreciationDocument.Id);
            var existingByNumber = existingDocuments
                .Select(row => new
                {
                    Id = GetGuid(row, "Id"),
                    Number = MetadataService.NormalizeLegacyDocumentNumber(GetString(row, "Номер", "doc_number")),
                    IsPosted = GetBoolean(row, "Проведен", "Проведён", "is_posted")
                })
                .Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Number))
                .GroupBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets)
            {
                var assetId = GetGuid(asset, "Id");
                if (assetId == Guid.Empty || !GetBoolean(asset, "Активен", "is_active"))
                    continue;

                var monthlyDepreciation = GetDecimal(asset, "Месячная амортизация", "monthly_depreciation");
                if (monthlyDepreciation <= 0)
                    continue;

                var initialCost = GetDecimal(asset, "Первоначальная стоимость", "initial_cost");
                var accumulatedDepreciation = GetDecimal(asset, "Накопленная амортизация", "accumulated_depreciation");
                var carryingAmount = GetDecimal(asset, "Остаточная стоимость", "carrying_amount");
                if ((initialCost > 0 && accumulatedDepreciation >= initialCost) ||
                    (initialCost <= 0 && carryingAmount <= 0))
                {
                    continue;
                }

                var commissioningDate = GetDate(asset, "Дата ввода в эксплуатацию", "commissioning_date");
                var acquisitionDate = GetDate(asset, "Дата приобретения", "acquisition_date");
                var depreciationStart = GetDepreciationStartDate(commissioningDate ?? acquisitionDate);
                if (!depreciationStart.HasValue)
                    continue;

                var conservationDate = GetDate(asset, "Дата консервации", "conservation_date");
                var reopeningDate = GetDate(asset, "Дата расконсервации", "reopening_date");

                foreach (var monthEnd in monthEnds)
                {
                    if (monthEnd.Date < depreciationStart.Value.Date)
                        continue;

                    if (conservationDate.HasValue &&
                        conservationDate.Value.Date <= monthEnd.Date &&
                        (!reopeningDate.HasValue || reopeningDate.Value.Date > monthEnd.Date))
                    {
                        continue;
                    }

                    var documentNumber = BuildFixedAssetDepreciationDocumentNumber(asset, monthEnd);
                    if (existingByNumber.TryGetValue(documentNumber, out var existingDocument))
                    {
                        if (!existingDocument.IsPosted)
                            await metadataService.PostDocumentAsync(depreciationDocument.Id, existingDocument.Id);
                        continue;
                    }

                    var documentData = new Dictionary<string, object>
                    {
                        ["Номер"] = documentNumber,
                        ["Дата"] = monthEnd,
                        ["Основное средство"] = assetId,
                        ["Сумма"] = monthlyDepreciation,
                        ["Сумма амортизации"] = monthlyDepreciation,
                        ["Счет дебета"] = GetFirstNonEmptyValue(asset, "Затратный счет", "expense_account"),
                        ["Счет кредита"] = GetFirstNonEmptyValue(asset, "Счет амортизации", "depreciation_account"),
                        ["Затратный счет"] = GetFirstNonEmptyValue(asset, "Затратный счет", "expense_account"),
                        ["Счет амортизации"] = GetFirstNonEmptyValue(asset, "Счет амортизации", "depreciation_account"),
                        ["Организация"] = GetFirstNonEmptyValue(asset, "Организация", "organization_id"),
                        ["МОЛ"] = GetFirstNonEmptyValue(asset, "МОЛ", "responsible_person_id"),
                        ["Участок"] = GetFirstNonEmptyValue(asset, "Участок", "site_id"),
                        ["Основание"] = $"Закрытие месяца {monthEnd:MM.yyyy}",
                        ["Примечание"] = $"Автоматическое начисление амортизации ОС за {monthEnd:MM.yyyy}"
                    };

                    var recordId = await metadataService.CreateDynamicRecordAsync(depreciationDocument.Id, documentData);
                    await metadataService.PostDocumentAsync(depreciationDocument.Id, recordId);
                    existingByNumber[documentNumber] = new
                    {
                        Id = recordId,
                        Number = documentNumber,
                        IsPosted = true
                    };
                }
            }
        }

        private static List<DateTime> GetCoveredMonthEnds(DateTime startDate, DateTime endDate)
        {
            var start = startDate.Date;
            var end = endDate.Date;
            if (start.Day != 1)
                return new List<DateTime>();

            var expectedEndDay = DateTime.DaysInMonth(end.Year, end.Month);
            if (end.Day != expectedEndDay)
                return new List<DateTime>();

            var result = new List<DateTime>();
            var monthStart = new DateTime(start.Year, start.Month, 1);
            while (monthStart <= end)
            {
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                if (monthEnd >= start && monthEnd <= end)
                    result.Add(monthEnd);
                monthStart = monthStart.AddMonths(1);
            }

            return result;
        }

        private static DateTime? GetDepreciationStartDate(DateTime? lifecycleDate)
        {
            if (!lifecycleDate.HasValue)
                return null;

            var monthStart = new DateTime(lifecycleDate.Value.Year, lifecycleDate.Value.Month, 1);
            return monthStart.AddMonths(1);
        }

        private static string BuildFixedAssetDepreciationDocumentNumber(Dictionary<string, object> asset, DateTime monthEnd)
        {
            var source = GetString(asset, "Инвентарный номер", "Код", "Наименование");
            if (string.IsNullOrWhiteSpace(source))
                source = GetGuid(asset, "Id").ToString("N")[..8];

            var normalized = new string(source
                .Where(ch => char.IsLetterOrDigit(ch))
                .Take(12)
                .ToArray());
            if (string.IsNullOrWhiteSpace(normalized))
                normalized = GetGuid(asset, "Id").ToString("N")[..8];

            return $"DEP-{monthEnd:yyyyMM}-{normalized.ToUpperInvariant()}";
        }

        private static Guid GetGuid(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var raw) && raw != null && raw != DBNull.Value &&
                    Guid.TryParse(raw.ToString(), out var value))
                {
                    return value;
                }
            }

            return Guid.Empty;
        }

        private static string GetString(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var raw) && raw != null && raw != DBNull.Value)
                    return raw.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static object? GetFirstNonEmptyValue(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var raw) &&
                    raw != null &&
                    raw != DBNull.Value &&
                    !string.IsNullOrWhiteSpace(raw.ToString()))
                {
                    return raw;
                }
            }

            return null;
        }

        private static decimal GetDecimal(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!data.TryGetValue(key, out var raw) || raw == null || raw == DBNull.Value)
                    continue;

                if (raw is decimal value)
                    return value;

                if (decimal.TryParse(raw.ToString(), out value))
                    return value;
            }

            return 0m;
        }

        private static DateTime? GetDate(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!data.TryGetValue(key, out var raw) || raw == null || raw == DBNull.Value)
                    continue;

                if (raw is DateTime value)
                    return value;

                if (DateTime.TryParse(raw.ToString(), out value))
                    return value;
            }

            return null;
        }

        private static bool GetBoolean(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!data.TryGetValue(key, out var raw) || raw == null || raw == DBNull.Value)
                    continue;

                if (raw is bool value)
                    return value;

                if (bool.TryParse(raw.ToString(), out value))
                    return value;

                var text = raw.ToString();
                if (string.Equals(text, "Да", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(text, "Нет", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return false;
        }
    }
}
