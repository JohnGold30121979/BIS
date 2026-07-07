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
                CREATE TABLE IF NOT EXISTS ""AccountOpeningBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""BalanceDate"" timestamp with time zone NOT NULL,
                    ""AccountCode"" varchar(50) NOT NULL, ""Debit"" numeric(18,2) NOT NULL,
                    ""Credit"" numeric(18,2) NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                ALTER TABLE ""AccountOpeningBalances""
                    ADD COLUMN IF NOT EXISTS ""SourcePeriodId"" uuid NULL;
                ALTER TABLE ""AccountOpeningBalances""
                    ADD COLUMN IF NOT EXISTS ""IsSystemGenerated"" boolean NOT NULL DEFAULT false;
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AccountOpeningBalances_Date_Account""
                    ON ""AccountOpeningBalances"" (""BalanceDate"", ""AccountCode"");
                CREATE INDEX IF NOT EXISTS ""IX_AccountOpeningBalances_SourcePeriodId""
                    ON ""AccountOpeningBalances"" (""SourcePeriodId"");
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
                    ""IsActive"" boolean NOT NULL);
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
                    ""VatAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""SalesTaxAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""TaxAmount"" numeric(18,2) NOT NULL, ""TotalAmount"" numeric(18,2) NOT NULL,
                    ""SourceRecordId"" uuid NULL, ""CreatedAt"" timestamp with time zone NOT NULL);";
            const string taxJournalAlterSql = @"
                ALTER TABLE ""TaxJournalRecords""
                    ADD COLUMN IF NOT EXISTS ""VatAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""TaxJournalRecords""
                    ADD COLUMN IF NOT EXISTS ""SalesTaxAmount"" numeric(18,2) NOT NULL DEFAULT 0;";
            await _context.Database.ExecuteSqlRawAsync(sql);
            await _context.Database.ExecuteSqlRawAsync(taxJournalAlterSql);
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
            period.Status = "Collected";
            period.CollectedAt = DateTime.UtcNow;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return period;
        }

        public async Task CloseAsync(Guid periodId)
        {
            await EnsureSchemaAsync();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден");
            if (period.Status != "Collected")
                throw new InvalidOperationException("Перед закрытием выполните сбор информации за период");
            var snapshots = await _context.AccountTurnoverSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.PeriodId == periodId).ToListAsync();
            var openingDifference = snapshots.Sum(item => item.OpeningDebit - item.OpeningCredit);
            var turnoverDifference = snapshots.Sum(item => item.TurnoverDebit - item.TurnoverCredit);
            var closingDifference = snapshots.Sum(item => item.ClosingDebit - item.ClosingCredit);
            if (Math.Abs(openingDifference) >= 0.01m || Math.Abs(turnoverDifference) >= 0.01m ||
                Math.Abs(closingDifference) >= 0.01m)
                throw new InvalidOperationException("Период не закрыт: контроль дебета и кредита не пройден.");
            await CarryForwardClosingBalancesAsync(period, snapshots);
            period.Status = "Closed";
            period.IsLocked = true;
            period.ClosedAt = DateTime.UtcNow;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task ReopenAsync(Guid periodId)
        {
            await EnsureSchemaAsync();
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var period = await _context.AccountingPeriods.FindAsync(periodId)
                ?? throw new InvalidOperationException("Учетный период не найден");
            await RemoveGeneratedOpeningBalancesAsync(period.Id);
            period.Status = "Open";
            period.IsLocked = false;
            period.ClosedAt = null;
            period.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public async Task<AccountingPeriod?> FindAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureSchemaAsync();
            var start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);
            return await _context.AccountingPeriods.AsNoTracking()
                .FirstOrDefaultAsync(item => item.StartDate == start && item.EndDate == end);
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

        public async Task EnsureOpeningBalanceCanBeModifiedAsync(DateTime balanceDate)
        {
            await EnsureSchemaAsync();
            var utcDate = DateTime.SpecifyKind(balanceDate.Date, DateTimeKind.Utc);
            var blockingPeriod = await _context.AccountingPeriods.AsNoTracking()
                .Where(period => period.IsLocked && period.EndDate >= utcDate)
                .OrderBy(period => period.StartDate)
                .FirstOrDefaultAsync();
            if (blockingPeriod != null)
            {
                throw new InvalidOperationException(
                    $"Остаток на дату {balanceDate:dd.MM.yyyy} влияет на закрытый период " +
                    $"{blockingPeriod.StartDate:dd.MM.yyyy} - {blockingPeriod.EndDate:dd.MM.yyyy}. " +
                    "Сначала откройте этот период.");
            }
        }

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
                VatAmount = entry.VatAmount,
                SalesTaxAmount = entry.SalesTaxAmount,
                TaxAmount = entry.TaxAmount,
                TotalAmount = entry.Amount
            }));
        }

        private async Task SeedFinancialReportLinesAsync()
        {
            var defaults = new[]
            {
                new FinancialReportLine { ReportCode = "Balance", LineCode = "A", SectionCode = "Assets", Name = "Активы", SortOrder = 10, IsTotal = true },
                new FinancialReportLine { ReportCode = "Balance", LineCode = "L", SectionCode = "Liabilities", Name = "Обязательства", SortOrder = 20, IsTotal = true },
                new FinancialReportLine { ReportCode = "Balance", LineCode = "E", SectionCode = "Equity", Name = "Капитал", SortOrder = 30, IsTotal = true },
                new FinancialReportLine { ReportCode = "ProfitLoss", LineCode = "I", SectionCode = "Income", Name = "Доходы", SortOrder = 10, IsTotal = true },
                new FinancialReportLine { ReportCode = "ProfitLoss", LineCode = "X", SectionCode = "Expenses", Name = "Расходы", SortOrder = 20, IsTotal = true }
            };
            foreach (var line in defaults)
            {
                if (!await _context.FinancialReportLines.AnyAsync(item => item.ReportCode == line.ReportCode && item.LineCode == line.LineCode))
                    await _context.FinancialReportLines.AddAsync(line);
            }
            await _context.SaveChangesAsync();
        }

        private async Task CarryForwardClosingBalancesAsync(
            AccountingPeriod period,
            IReadOnlyCollection<AccountTurnoverSnapshot> snapshots)
        {
            var nextPeriodStart = DateTime.SpecifyKind(period.EndDate.Date.AddDays(1), DateTimeKind.Utc);
            var updatedAt = DateTime.UtcNow;
            var carriedSnapshots = snapshots
                .Where(snapshot => snapshot.ClosingDebit != 0m || snapshot.ClosingCredit != 0m)
                .ToList();

            var existingNextPeriodBalances = await _context.AccountOpeningBalances
                .Where(balance => balance.BalanceDate == nextPeriodStart)
                .ToListAsync();
            var previousGenerated = existingNextPeriodBalances
                .Where(balance => balance.IsSystemGenerated && balance.SourcePeriodId == period.Id)
                .ToList();

            var carriedCodes = carriedSnapshots
                .Select(snapshot => snapshot.AccountCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflicts = existingNextPeriodBalances
                .Where(balance => carriedCodes.Contains(balance.AccountCode) &&
                    !(balance.IsSystemGenerated && balance.SourcePeriodId == period.Id))
                .Select(balance => balance.AccountCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (conflicts.Count > 0)
            {
                throw new InvalidOperationException(
                    $"На дату {nextPeriodStart:dd.MM.yyyy} уже существуют входящие остатки по счетам: {string.Join(", ", conflicts)}. " +
                    "Автоперенос закрытия не перезаписывает их автоматически.");
            }

            var previousGeneratedByAccount = previousGenerated
                .GroupBy(balance => balance.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var newBalances = new List<AccountOpeningBalance>();

            foreach (var snapshot in carriedSnapshots)
            {
                if (previousGeneratedByAccount.TryGetValue(snapshot.AccountCode, out var existingBalance))
                {
                    existingBalance.BalanceDate = nextPeriodStart;
                    existingBalance.Debit = snapshot.ClosingDebit;
                    existingBalance.Credit = snapshot.ClosingCredit;
                    existingBalance.SourcePeriodId = period.Id;
                    existingBalance.IsSystemGenerated = true;
                    existingBalance.UpdatedAt = updatedAt;
                    continue;
                }

                newBalances.Add(new AccountOpeningBalance
                {
                    BalanceDate = nextPeriodStart,
                    AccountCode = snapshot.AccountCode,
                    Debit = snapshot.ClosingDebit,
                    Credit = snapshot.ClosingCredit,
                    SourcePeriodId = period.Id,
                    IsSystemGenerated = true,
                    UpdatedAt = updatedAt
                });
            }

            var obsoleteBalances = previousGenerated
                .Where(balance => !carriedCodes.Contains(balance.AccountCode))
                .ToList();
            if (obsoleteBalances.Count > 0)
                _context.AccountOpeningBalances.RemoveRange(obsoleteBalances);

            if (newBalances.Count > 0)
                await _context.AccountOpeningBalances.AddRangeAsync(newBalances);
        }

        private async Task RemoveGeneratedOpeningBalancesAsync(Guid sourcePeriodId)
        {
            var generatedBalances = await _context.AccountOpeningBalances
                .Where(balance => balance.IsSystemGenerated && balance.SourcePeriodId == sourcePeriodId)
                .ToListAsync();
            if (generatedBalances.Count > 0)
                _context.AccountOpeningBalances.RemoveRange(generatedBalances);
        }
    }
}
