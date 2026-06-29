using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class BalanceService
    {
        private readonly AppDbContext _context;

        public BalanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<TurnoverBalance>> GetTurnoverBalanceAsync(DateTime startDate, DateTime endDate)
        {
            var periodStart = startDate.Date;
            var periodEndExclusive = endDate.Date.AddDays(1);
            var postings = await new PostingService(_context)
                .GetAllPostingsAsync(null, periodEndExclusive.AddTicks(-1));
            var accounts = await LoadAccountsAsync();
            var utcStart = DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
            var openingBalances = (await _context.AccountOpeningBalances.AsNoTracking()
                    .Where(balance => balance.BalanceDate <= utcStart)
                    .ToListAsync())
                .GroupBy(balance => balance.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.BalanceDate).First(),
                    StringComparer.OrdinalIgnoreCase);

            var accountCodes = accounts.Keys
                .Union(postings.SelectMany(posting => new[] { posting.DebitAccount, posting.CreditAccount }))
                .Union(openingBalances.Keys)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToList();

            return accountCodes.Select(code =>
            {
                openingBalances.TryGetValue(code, out var initialBalance);
                var openingFrom = initialBalance?.BalanceDate.Date ?? DateTime.MinValue;
                var openingDebit = (initialBalance?.Debit ?? 0) + postings
                    .Where(posting => posting.Date >= openingFrom && posting.Date < periodStart && posting.DebitAccount == code)
                    .Sum(posting => posting.Amount);
                var openingCredit = (initialBalance?.Credit ?? 0) + postings
                    .Where(posting => posting.Date >= openingFrom && posting.Date < periodStart && posting.CreditAccount == code)
                    .Sum(posting => posting.Amount);
                var turnoverDebit = postings
                    .Where(posting => posting.Date >= periodStart && posting.Date < periodEndExclusive && posting.DebitAccount == code)
                    .Sum(posting => posting.Amount);
                var turnoverCredit = postings
                    .Where(posting => posting.Date >= periodStart && posting.Date < periodEndExclusive && posting.CreditAccount == code)
                    .Sum(posting => posting.Amount);

                var openingNet = openingDebit - openingCredit;
                var closingNet = openingNet + turnoverDebit - turnoverCredit;
                return new TurnoverBalance
                {
                    AccountCode = code,
                    AccountName = accounts.TryGetValue(code, out var account) ? account.Name : code,
                    OpeningDebit = Math.Max(openingNet, 0),
                    OpeningCredit = Math.Max(-openingNet, 0),
                    TurnoverDebit = turnoverDebit,
                    TurnoverCredit = turnoverCredit,
                    ClosingDebit = Math.Max(closingNet, 0),
                    ClosingCredit = Math.Max(-closingNet, 0)
                };
            }).ToList();
        }

        public async Task<List<GeneralLedger>> GetGeneralLedgerAsync(int year)
        {
            var start = new DateTime(year, 1, 1);
            var end = start.AddYears(1);
            var postings = await new PostingService(_context).GetAllPostingsAsync(start, end.AddTicks(-1));
            var accounts = await LoadAccountsAsync();
            var annualBalances = (await GetTurnoverBalanceAsync(start, end.AddDays(-1)))
                .ToDictionary(balance => balance.AccountCode, StringComparer.OrdinalIgnoreCase);
            var accountCodes = postings
                .SelectMany(posting => new[] { posting.DebitAccount, posting.CreditAccount })
                .Union(annualBalances.Keys)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code);

            return accountCodes.Select(code =>
            {
                var debitByMonth = Enumerable.Range(1, 12).ToDictionary(
                    month => month,
                    month => postings.Where(posting =>
                            posting.Date.Month == month && posting.DebitAccount == code)
                        .Sum(posting => posting.Amount));
                var creditByMonth = Enumerable.Range(1, 12).ToDictionary(
                    month => month,
                    month => postings.Where(posting =>
                            posting.Date.Month == month && posting.CreditAccount == code)
                        .Sum(posting => posting.Amount));

                annualBalances.TryGetValue(code, out var balance);
                return new GeneralLedger
                {
                    AccountCode = code,
                    AccountName = accounts.TryGetValue(code, out var account) ? account.Name : code,
                    OpeningDebit = balance?.OpeningDebit ?? 0,
                    OpeningCredit = balance?.OpeningCredit ?? 0,
                    MonthlyTurnoverDebit = debitByMonth,
                    MonthlyTurnoverCredit = creditByMonth,
                    YearTurnoverDebit = debitByMonth.Values.Sum(),
                    YearTurnoverCredit = creditByMonth.Values.Sum(),
                    ClosingDebit = balance?.ClosingDebit ?? 0,
                    ClosingCredit = balance?.ClosingCredit ?? 0
                };
            }).ToList();
        }

        public async Task<EnterpriseBalance> GetEnterpriseBalanceAsync(DateTime date)
        {
            var postings = await new PostingService(_context).GetAllPostingsAsync(null, date.Date.AddDays(1).AddTicks(-1));
            var accounts = await LoadAccountsAsync();
            var result = new EnterpriseBalance();
            var configuredLines = await LoadConfiguredLinesAsync("Balance");
            var closingBalances = await GetTurnoverBalanceAsync(date.Date, date.Date);

            if (configuredLines.Any(line => line.AccountCodes.Count > 0))
            {
                foreach (var line in configuredLines.Where(line => !line.IsTotal && line.AccountCodes.Count > 0))
                {
                    var amount = closingBalances.Where(balance => line.AccountCodes.Any(code => balance.AccountCode.StartsWith(code)))
                        .Sum(balance => balance.ClosingDebit - balance.ClosingCredit) * line.Sign;
                    var item = new BalanceItem { AccountCode = line.LineCode, AccountName = line.Name, Amount = amount };
                    if (line.SectionCode.Equals("Assets", StringComparison.OrdinalIgnoreCase) || line.SectionCode.Equals("Активы", StringComparison.OrdinalIgnoreCase))
                        result.Assets.Add(item);
                    else if (line.SectionCode.Equals("Equity", StringComparison.OrdinalIgnoreCase) || line.SectionCode.Equals("Капитал", StringComparison.OrdinalIgnoreCase))
                        result.Equity.Add(item);
                    else
                        result.Liabilities.Add(item);
                }
                result.TotalAssets = result.Assets.Sum(item => item.Amount);
                result.TotalLiabilities = result.Liabilities.Sum(item => item.Amount);
                result.TotalEquity = result.Equity.Sum(item => item.Amount);
                return result;
            }

            foreach (var balance in closingBalances.OrderBy(item => item.AccountCode))
            {
                var code = balance.AccountCode;
                var netDebit = balance.ClosingDebit - balance.ClosingCredit;
                if (netDebit == 0 || string.IsNullOrWhiteSpace(code))
                    continue;

                var item = new BalanceItem
                {
                    AccountCode = code,
                    AccountName = balance.AccountName,
                    Amount = netDebit
                };

                switch (code[0])
                {
                    case '1':
                    case '2':
                        result.Assets.Add(item);
                        break;
                    case '3':
                    case '4':
                        item.Amount = -netDebit;
                        result.Liabilities.Add(item);
                        break;
                    case '5':
                        item.Amount = -netDebit;
                        result.Equity.Add(item);
                        break;
                }
            }

            var currentResult = CalculateCurrentFinancialResult(postings, accounts);
            if (currentResult != 0)
            {
                result.Equity.Add(new BalanceItem
                {
                    AccountCode = "RESULT",
                    AccountName = "Нераспределенный финансовый результат",
                    Amount = currentResult
                });
            }

            result.TotalAssets = result.Assets.Sum(item => item.Amount);
            result.TotalLiabilities = result.Liabilities.Sum(item => item.Amount);
            result.TotalEquity = result.Equity.Sum(item => item.Amount);
            return result;
        }

        public async Task<FinancialResults> GetFinancialResultsAsync(DateTime startDate, DateTime endDate)
        {
            var endExclusive = endDate.Date.AddDays(1);
            var postings = await new PostingService(_context)
                .GetAllPostingsAsync(startDate.Date, endExclusive.AddTicks(-1));
            var accounts = await LoadAccountsAsync();
            var result = new FinancialResults();
            var configuredLines = await LoadConfiguredLinesAsync("ProfitLoss");

            if (configuredLines.Any(line => line.AccountCodes.Count > 0))
            {
                foreach (var line in configuredLines.Where(line => !line.IsTotal && line.AccountCodes.Count > 0))
                {
                    var matchingAccounts = accounts.Where(pair => line.AccountCodes.Any(code => pair.Key.StartsWith(code)));
                    var amount = matchingAccounts.Sum(pair =>
                    {
                        var debit = postings.Where(posting => posting.DebitAccount == pair.Key).Sum(posting => posting.Amount);
                        var credit = postings.Where(posting => posting.CreditAccount == pair.Key).Sum(posting => posting.Amount);
                        return IsPassiveType(pair.Value.Type) ? credit - debit : debit - credit;
                    }) * line.Sign;
                    var item = new BalanceItem { AccountCode = line.LineCode, AccountName = line.Name, Amount = amount };
                    if (line.SectionCode.Equals("Income", StringComparison.OrdinalIgnoreCase) || line.SectionCode.Equals("Доходы", StringComparison.OrdinalIgnoreCase))
                        result.Income.Add(item);
                    else
                        result.Expenses.Add(item);
                }
                return result;
            }

            foreach (var (code, account) in accounts
                         .Where(pair => pair.Key.Length > 0 && pair.Key[0] >= '6')
                         .OrderBy(pair => pair.Key))
            {
                var debit = postings.Where(posting => posting.DebitAccount == code).Sum(posting => posting.Amount);
                var credit = postings.Where(posting => posting.CreditAccount == code).Sum(posting => posting.Amount);
                if (debit == 0 && credit == 0)
                    continue;

                var isIncome = IsPassiveType(account.Type);
                var amount = isIncome ? credit - debit : debit - credit;
                var item = new BalanceItem
                {
                    AccountCode = code,
                    AccountName = account.Name,
                    Amount = amount
                };

                if (isIncome)
                    result.Income.Add(item);
                else
                    result.Expenses.Add(item);
            }

            return result;
        }

        public async Task<List<PurchaseSaleJournalEntry>> GetPurchaseSalesJournalAsync(
            DateTime startDate,
            DateTime endDate)
        {
            var metadataService = new MetadataService(_context);
            var documents = await metadataService.GetDocumentsAsync();
            var result = new List<PurchaseSaleJournalEntry>();

            foreach (var document in documents.Where(document =>
                         document.Name.Equals("Приход товаров", StringComparison.OrdinalIgnoreCase) ||
                         document.Name.Equals("Расход товаров", StringComparison.OrdinalIgnoreCase) ||
                         document.Name.Contains("Счет-фактура", StringComparison.OrdinalIgnoreCase) ||
                         document.Name.Equals(InvoiceDocumentTypes.SalesIssue, StringComparison.OrdinalIgnoreCase) ||
                         document.Name.Equals(InvoiceDocumentTypes.PurchaseRegistration, StringComparison.OrdinalIgnoreCase)))
            {
                var rows = await metadataService.GetCatalogDataAsync(document.Id);
                var maps = await ReferenceDisplayHelper.LoadMapsAsync(document, metadataService);
                var displayRows = ReferenceDisplayHelper.ResolveRows(rows, maps);
                foreach (var row in displayRows)
                {
                    if (!TryGetDate(row, out var date) || date.Date < startDate.Date || date.Date > endDate.Date)
                        continue;

                    var isPurchase = document.Name.Contains("Приход", StringComparison.OrdinalIgnoreCase) ||
                                     document.Name.Contains("получ", StringComparison.OrdinalIgnoreCase) ||
                                     document.Name.Equals(InvoiceDocumentTypes.PurchaseRegistration, StringComparison.OrdinalIgnoreCase);
                    var isSales = document.Name.Equals(InvoiceDocumentTypes.SalesIssue, StringComparison.OrdinalIgnoreCase) ||
                                  document.Name.Contains("Расход", StringComparison.OrdinalIgnoreCase) ||
                                  document.Name.Contains("реализа", StringComparison.OrdinalIgnoreCase);
                    var amount = GetDecimal(row, "Сумма", "Сумма в сом");
                    var taxAmount = GetDecimal(row, "Сумма НДС", "vat_amount");
                    result.Add(new PurchaseSaleJournalEntry
                    {
                        Section = isPurchase ? "Закупки" : isSales ? "Продажи" : "Продажи",
                        Date = date,
                        DocumentNumber = GetString(row, "Номер", "Номер документа", "doc_number"),
                        DocumentType = document.Name,
                        Organization = GetString(row, "Организация"),
                        Amount = amount,
                        AmountWithoutTax = amount - taxAmount,
                        TaxAmount = taxAmount,
                        TaxType = GetString(row, "Ставка НДС", "vat_rate"),
                        IsPosted = GetBoolean(row, "Проведён", "Проведен", "is_posted"),
                        Note = GetString(row, "Примечание", "Описание")
                    });
                }
            }

            return result.OrderBy(entry => entry.Date).ThenBy(entry => entry.DocumentNumber).ToList();
        }

        public async Task<PeriodCollectionResult> CollectPeriodInformationAsync(
            DateTime startDate,
            DateTime endDate)
        {
            var result = new PeriodCollectionResult();
            var metadataService = new MetadataService(_context);
            var documents = await metadataService.GetDocumentsAsync();

            foreach (var document in documents.Where(document =>
                         !document.Name.Equals("Проводки", StringComparison.OrdinalIgnoreCase)))
            {
                var rows = await metadataService.GetCatalogDataAsync(document.Id);
                var periodRows = rows.Where(row =>
                    TryGetDate(row, out var date) &&
                    date.Date >= startDate.Date &&
                    date.Date <= endDate.Date).ToList();
                if (periodRows.Count == 0)
                    continue;

                result.Documents.Add(new PeriodDocumentSummary
                {
                    DocumentType = document.Name,
                    DocumentCount = periodRows.Count,
                    PostedCount = periodRows.Count(row =>
                        GetBoolean(row, "Проведён", "Проведен", "is_posted")),
                    Amount = periodRows.Sum(row => GetDecimal(row, "Сумма", "Сумма в сом"))
                });
            }

            var postings = await new PostingService(_context).GetAllPostingsAsync(startDate.Date, endDate.Date.AddDays(1).AddTicks(-1));
            result.PostingCount = postings.Count;
            result.DebitTurnover = postings.Sum(posting => posting.Amount);
            result.CreditTurnover = postings.Sum(posting => posting.Amount);
            return result;
        }

        private static decimal CalculateCurrentFinancialResult(
            IEnumerable<PostingViewModel> postings,
            IReadOnlyDictionary<string, AccountInfo> accounts)
        {
            decimal result = 0;
            foreach (var (code, account) in accounts.Where(pair => pair.Key.Length > 0 && pair.Key[0] >= '6'))
            {
                var debit = postings.Where(posting => posting.DebitAccount == code).Sum(posting => posting.Amount);
                var credit = postings.Where(posting => posting.CreditAccount == code).Sum(posting => posting.Amount);
                result += IsPassiveType(account.Type)
                    ? credit - debit
                    : -(debit - credit);
            }

            return result;
        }

        private async Task<Dictionary<string, AccountInfo>> LoadAccountsAsync()
        {
            var metadata = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));
            if (metadata == null)
                return new Dictionary<string, AccountInfo>(StringComparer.OrdinalIgnoreCase);

            var rows = await new MetadataService(_context).GetCatalogDataAsync(metadata.Id);
            return rows
                .Select(row => new AccountInfo
                {
                    Code = row.GetValueOrDefault("Код")?.ToString() ?? string.Empty,
                    Name = row.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty,
                    Type = row.GetValueOrDefault("Тип счета")?.ToString() ?? string.Empty
                })
                .Where(account => !string.IsNullOrWhiteSpace(account.Code))
                .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<ConfiguredLine>> LoadConfiguredLinesAsync(string reportCode)
        {
            var lines = await _context.FinancialReportLines.AsNoTracking()
                .Where(line => line.ReportCode == reportCode && line.IsActive)
                .OrderBy(line => line.SortOrder).ToListAsync();
            var lineIds = lines.Select(line => line.Id).ToList();
            var links = await _context.FinancialReportLineAccounts.AsNoTracking()
                .Where(link => lineIds.Contains(link.LineId)).ToListAsync();
            return lines.Select(line => new ConfiguredLine
            {
                LineCode = line.LineCode,
                SectionCode = line.SectionCode,
                Name = line.Name,
                Sign = line.Sign,
                IsTotal = line.IsTotal,
                AccountCodes = links.Where(link => link.LineId == line.Id).Select(link => link.AccountCode).ToList()
            }).ToList();
        }

        private static bool TryGetDate(Dictionary<string, object> row, out DateTime date)
        {
            foreach (var name in new[] { "Дата", "posting_date", "date" })
            {
                if (!row.TryGetValue(name, out var value) || value == null || value == DBNull.Value)
                    continue;
                if (value is DateTime existingDate)
                {
                    date = existingDate;
                    return true;
                }
                if (DateTime.TryParse(value.ToString(), out date))
                    return true;
            }

            date = default;
            return false;
        }

        private static string GetString(Dictionary<string, object> row, params string[] names)
        {
            return names.Select(name => row.GetValueOrDefault(name)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static decimal GetDecimal(Dictionary<string, object> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetValue(name, out var value) && decimal.TryParse(value?.ToString(), out var amount))
                    return amount;
            }
            return 0;
        }

        private static bool GetBoolean(Dictionary<string, object> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetValue(name, out var value))
                    continue;
                if (value is bool boolean)
                    return boolean;
                if (bool.TryParse(value?.ToString(), out boolean))
                    return boolean;
            }
            return false;
        }

        private sealed class AccountInfo
        {
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Type { get; init; } = string.Empty;
        }

        private static bool IsPassiveType(string value) =>
            value.Equals("Passive", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Пассивный", StringComparison.OrdinalIgnoreCase);

        private sealed class ConfiguredLine
        {
            public string LineCode { get; init; } = string.Empty;
            public string SectionCode { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public int Sign { get; init; }
            public bool IsTotal { get; init; }
            public List<string> AccountCodes { get; init; } = new();
        }
    }
}
