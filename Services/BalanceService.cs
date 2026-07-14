using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
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

            if (configuredLines.Any(HasReportCalculationRule))
            {
                var lineAmounts = CalculateBalanceLineAmounts(configuredLines, closingBalances);

                foreach (var line in configuredLines.Where(line => !line.IsTotal))
                {
                    if (!lineAmounts.TryGetValue(line.LineCode, out var amount))
                        continue;
                    if (amount == 0 && !HasReportCalculationRule(line))
                        continue;

                    var item = new BalanceItem { AccountCode = line.LineCode, AccountName = line.Name, Amount = amount };
                    if (IsSection(line, "Assets", "Активы"))
                        result.Assets.Add(item);
                    else if (IsSection(line, "Equity", "Капитал"))
                        result.Equity.Add(item);
                    else
                        result.Liabilities.Add(item);
                }

                result.TotalAssets = ResolveConfiguredSectionTotal(configuredLines, lineAmounts, "Assets", "Активы", result.Assets.Sum(item => item.Amount));
                result.TotalLiabilities = ResolveConfiguredSectionTotal(configuredLines, lineAmounts, "Liabilities", "Обязательства", result.Liabilities.Sum(item => item.Amount));
                result.TotalEquity = ResolveConfiguredSectionTotal(configuredLines, lineAmounts, "Equity", "Капитал", result.Equity.Sum(item => item.Amount));
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

            if (configuredLines.Any(HasReportCalculationRule))
            {
                var lineAmounts = CalculateProfitLossLineAmounts(configuredLines, accounts, postings);

                foreach (var line in configuredLines.Where(line => !line.IsTotal))
                {
                    if (!lineAmounts.TryGetValue(line.LineCode, out var amount))
                        continue;
                    if (amount == 0 && !HasReportCalculationRule(line))
                        continue;

                    var item = new BalanceItem { AccountCode = line.LineCode, AccountName = line.Name, Amount = amount };
                    if (IsSection(line, "Income", "Доходы"))
                        result.Income.Add(item);
                    else
                        result.Expenses.Add(item);
                }

                result.TotalIncomeOverride = ResolveConfiguredSectionTotal(configuredLines, lineAmounts, "Income", "Доходы", result.Income.Sum(item => item.Amount));
                result.TotalExpensesOverride = ResolveConfiguredSectionTotal(configuredLines, lineAmounts, "Expenses", "Расходы", result.Expenses.Sum(item => item.Amount));
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
            await new RuntimeSchemaFixService(_context).EnsureAsync();
            var metadataService = new MetadataService(_context);
            var documents = await metadataService.GetDocumentsAsync();
            var result = new List<PurchaseSaleJournalEntry>();

            var salesInvoiceDocument = documents.FirstOrDefault(document =>
                document.Name.Equals(InvoiceDocumentTypes.SalesIssue, StringComparison.OrdinalIgnoreCase));
            if (salesInvoiceDocument != null)
                await AppendInvoiceJournalEntriesAsync(result, salesInvoiceDocument, startDate, endDate, "Продажи");

            var purchaseInvoiceDocument = documents.FirstOrDefault(document =>
                document.Name.Equals(InvoiceDocumentTypes.PurchaseRegistration, StringComparison.OrdinalIgnoreCase));
            if (purchaseInvoiceDocument != null)
                await AppendInvoiceJournalEntriesAsync(result, purchaseInvoiceDocument, startDate, endDate, "Закупки");

            foreach (var document in documents.Where(document =>
                         document.Name.Equals("Приход товаров", StringComparison.OrdinalIgnoreCase) ||
                         document.Name.Equals("Расход товаров", StringComparison.OrdinalIgnoreCase) ||
                         (document.Name.Contains("Счет-фактура", StringComparison.OrdinalIgnoreCase) &&
                          !document.Name.Equals(InvoiceDocumentTypes.SalesIssue, StringComparison.OrdinalIgnoreCase) &&
                          !document.Name.Equals(InvoiceDocumentTypes.PurchaseRegistration, StringComparison.OrdinalIgnoreCase))))
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
                    var vatAmount = GetDecimal(row, "Сумма НДС", "vat_amount");
                    var salesTaxAmount = GetDecimal(row, "Сумма налога с продаж", "sales_tax_amount");
                    result.Add(new PurchaseSaleJournalEntry
                    {
                        Section = isPurchase ? "Закупки" : isSales ? "Продажи" : "Продажи",
                        Date = date,
                        DocumentNumber = GetString(row, "Номер", "Номер документа", "doc_number"),
                        DocumentType = document.Name,
                        Organization = GetString(row, "Организация"),
                        Amount = amount,
                        AmountWithoutTax = amount - vatAmount - salesTaxAmount,
                        TaxAmount = vatAmount,
                        VatAmount = vatAmount,
                        SalesTaxAmount = salesTaxAmount,
                        TaxType = GetString(row, "Ставка НДС", "vat_rate"),
                        IsPosted = GetBoolean(row, "Проведён", "Проведен", "is_posted"),
                        Note = GetString(row, "Примечание", "Описание")
                    });
                }
            }

            return result.OrderBy(entry => entry.Date).ThenBy(entry => entry.DocumentNumber).ToList();
        }

        private async Task AppendInvoiceJournalEntriesAsync(
            ICollection<PurchaseSaleJournalEntry> result,
            MetadataObject document,
            DateTime startDate,
            DateTime endDate,
            string section)
        {
            var invoiceService = new InvoiceService(_context);
            invoiceService.Configure(document);
            await invoiceService.EnsureSchemaAsync();

            var invoices = await invoiceService.GetInvoicesAsync();
            foreach (var invoiceRow in invoices.Where(item =>
                         item.DocDate.Date >= startDate.Date &&
                         item.DocDate.Date <= endDate.Date))
            {
                var invoice = await invoiceService.GetInvoiceAsync(invoiceRow.Id);
                if (invoice == null)
                    continue;

                if (invoice.Lines.Count == 0)
                {
                    result.Add(new PurchaseSaleJournalEntry
                    {
                        Section = section,
                        Date = invoice.DocDate,
                        DocumentNumber = invoice.DocNumber,
                        DocumentType = document.Name,
                        Organization = invoice.OrganizationName,
                        ModuleCode = invoice.ModuleCode,
                        TaxBlankNumber = invoice.TaxBlankNumber,
                        EsfNumber = invoice.EsfNumber,
                        Amount = invoice.TotalAmount,
                        AmountWithoutTax = invoice.AmountWithoutTax,
                        TaxAmount = invoice.VatTotal,
                        VatAmount = invoice.VatTotal,
                        SalesTaxAmount = invoice.SalesTaxTotal,
                        TaxType = ResolveInvoiceTaxType(invoice),
                        IsPosted = invoice.IsPosted,
                        Note = invoice.Basis
                    });
                    continue;
                }

                foreach (var line in invoice.Lines.OrderBy(item => item.LineNumber))
                {
                    result.Add(new PurchaseSaleJournalEntry
                    {
                        Section = section,
                        Date = invoice.DocDate,
                        DocumentNumber = invoice.DocNumber,
                        DocumentType = document.Name,
                        Organization = invoice.OrganizationName,
                        ModuleCode = invoice.ModuleCode,
                        TaxBlankNumber = invoice.TaxBlankNumber,
                        EsfNumber = invoice.EsfNumber,
                        Amount = line.LineTotal,
                        AmountWithoutTax = line.AmountWithoutTax,
                        TaxAmount = line.VatAmount,
                        VatAmount = line.VatAmount,
                        SalesTaxAmount = line.SalesTaxAmount,
                        TaxType = ResolveLineTaxType(line),
                        VatTaxCode = line.VatTaxCode,
                        SalesTaxCode = line.SalesTaxCode,
                        IsPosted = invoice.IsPosted,
                        Note = BuildInvoiceJournalNote(invoice, line)
                    });
                }
            }
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
            result.Warnings.AddRange(await GetFixedAssetIntegrityWarningsAsync());
            return result;
        }

        private async Task<List<string>> GetFixedAssetIntegrityWarningsAsync()
        {
            var assetCatalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            if (assetCatalog == null)
                return new List<string>();

            var metadataService = new MetadataService(_context);
            var rows = await metadataService.GetCatalogDataAsync(assetCatalog.Id);
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(assetCatalog, metadataService);
            rows = ReferenceDisplayHelper.ResolveRows(rows, maps);
            var warnings = new List<string>();

            foreach (var row in rows)
            {
                var inventoryNumber = GetString(row, "Инвентарный номер", "Код");
                var name = GetString(row, "Наименование", "name");
                var assetLabel = string.IsNullOrWhiteSpace(inventoryNumber)
                    ? name
                    : $"{inventoryNumber} {name}".Trim();

                var isActive = GetBoolean(row, "Активен", "is_active");
                var initialCost = GetDecimal(row, "Первоначальная стоимость", "initial_cost");
                var accumulatedDepreciation = GetDecimal(row, "Накопленная амортизация", "accumulated_depreciation");
                var carryingAmount = GetDecimal(row, "Остаточная стоимость", "carrying_amount");
                var expectedCarryingAmount = Math.Max(0m, initialCost - accumulatedDepreciation);
                var status = GetString(row, "Статус", "status");

                if (isActive)
                {
                    if (string.IsNullOrWhiteSpace(GetString(row, "Счет учета", "asset_account")) ||
                        string.IsNullOrWhiteSpace(GetString(row, "Счет амортизации", "depreciation_account")) ||
                        string.IsNullOrWhiteSpace(GetString(row, "Затратный счет", "expense_account")))
                    {
                        warnings.Add($"ОС {assetLabel}: не заполнены все учетные счета (учета, амортизации, затрат).");
                    }

                    if (Math.Abs(carryingAmount - expectedCarryingAmount) >= 0.01m)
                    {
                        warnings.Add(
                            $"ОС {assetLabel}: остаточная стоимость {carryingAmount:N2} не равна расчетной {expectedCarryingAmount:N2}.");
                    }
                }

                if (status.Contains("Выбыло", StringComparison.OrdinalIgnoreCase) && isActive)
                    warnings.Add($"ОС {assetLabel}: статус 'Выбыло', но карточка все еще активна.");

                var conservationDate = GetDate(row, "Дата консервации", "conservation_date");
                if (conservationDate.HasValue &&
                    !status.Contains("консервац", StringComparison.OrdinalIgnoreCase) &&
                    !status.Contains("CONSERVATION", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"ОС {assetLabel}: дата консервации заполнена, но статус не переведен на консервацию.");
                }
            }

            return warnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
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
                SortOrder = line.SortOrder,
                Sign = line.Sign,
                Formula = line.Formula,
                FixedAmount = line.FixedAmount,
                IsTotal = line.IsTotal,
                AccountCodes = links.Where(link => link.LineId == line.Id).Select(link => link.AccountCode).ToList()
            }).ToList();
        }

        private static Dictionary<string, decimal> CalculateBalanceLineAmounts(
            IReadOnlyCollection<ConfiguredLine> lines,
            IReadOnlyCollection<TurnoverBalance> closingBalances)
        {
            var lineAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var accountAmount = line.AccountCodes.Count == 0
                    ? 0m
                    : closingBalances
                        .Where(balance => line.AccountCodes.Any(code =>
                            balance.AccountCode.StartsWith(code, StringComparison.OrdinalIgnoreCase)))
                        .Sum(balance => balance.ClosingDebit - balance.ClosingCredit);

                lineAmounts[line.LineCode] = (accountAmount + line.FixedAmount) * NormalizeSign(line.Sign);
            }

            ApplyConfiguredFormulas(lines, lineAmounts);
            return lineAmounts;
        }

        private static Dictionary<string, decimal> CalculateProfitLossLineAmounts(
            IReadOnlyCollection<ConfiguredLine> lines,
            IReadOnlyDictionary<string, AccountInfo> accounts,
            IReadOnlyCollection<PostingViewModel> postings)
        {
            var lineAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var accountAmount = line.AccountCodes.Count == 0
                    ? 0m
                    : accounts
                        .Where(pair => line.AccountCodes.Any(code =>
                            pair.Key.StartsWith(code, StringComparison.OrdinalIgnoreCase)))
                        .Sum(pair =>
                        {
                            var debit = postings
                                .Where(posting => posting.DebitAccount.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                                .Sum(posting => posting.Amount);
                            var credit = postings
                                .Where(posting => posting.CreditAccount.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                                .Sum(posting => posting.Amount);
                            return IsPassiveType(pair.Value.Type) ? credit - debit : debit - credit;
                        });

                lineAmounts[line.LineCode] = (accountAmount + line.FixedAmount) * NormalizeSign(line.Sign);
            }

            ApplyConfiguredFormulas(lines, lineAmounts);
            return lineAmounts;
        }

        private static void ApplyConfiguredFormulas(
            IReadOnlyCollection<ConfiguredLine> lines,
            Dictionary<string, decimal> lineAmounts)
        {
            var formulaLines = lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Formula))
                .OrderBy(line => line.SortOrder)
                .ToList();
            if (formulaLines.Count == 0)
                return;

            for (var pass = 0; pass < Math.Max(3, formulaLines.Count); pass++)
            {
                foreach (var line in formulaLines)
                {
                    lineAmounts[line.LineCode] =
                        (EvaluateReportFormula(line.Formula, lineAmounts) + line.FixedAmount) *
                        NormalizeSign(line.Sign);
                }
            }
        }

        private static decimal EvaluateReportFormula(
            string formula,
            IReadOnlyDictionary<string, decimal> lineAmounts)
        {
            var expression = formula
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace(";", "+", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(expression))
                return 0m;

            var total = 0m;
            var sign = 1;
            var index = 0;
            while (index < expression.Length)
            {
                var current = expression[index];
                if (current == '+' || current == '-')
                {
                    sign = current == '-' ? -1 : 1;
                    index++;
                    continue;
                }

                var start = index;
                while (index < expression.Length && expression[index] != '+' && expression[index] != '-')
                    index++;

                var term = expression[start..index];
                if (!string.IsNullOrWhiteSpace(term))
                    total += sign * EvaluateFormulaTerm(term, lineAmounts);
                sign = 1;
            }

            return total;
        }

        private static decimal EvaluateFormulaTerm(
            string term,
            IReadOnlyDictionary<string, decimal> lineAmounts)
        {
            var rangeParts = term.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 2 &&
                TryGetLineNumber(rangeParts[0], out var fromLine) &&
                TryGetLineNumber(rangeParts[1], out var toLine))
            {
                var min = Math.Min(fromLine, toLine);
                var max = Math.Max(fromLine, toLine);
                return lineAmounts
                    .Where(pair => TryGetLineNumber(pair.Key, out var lineNumber) &&
                                   lineNumber >= min && lineNumber <= max)
                    .Sum(pair => pair.Value);
            }

            var lineCode = NormalizeLineReference(term);
            if (lineAmounts.TryGetValue(lineCode, out var lineAmount))
                return lineAmount;

            return decimal.TryParse(term, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue) ||
                   decimal.TryParse(term, NumberStyles.Any, CultureInfo.CurrentCulture, out invariantValue)
                ? invariantValue
                : 0m;
        }

        private static decimal ResolveConfiguredSectionTotal(
            IReadOnlyCollection<ConfiguredLine> lines,
            IReadOnlyDictionary<string, decimal> lineAmounts,
            string englishSectionCode,
            string russianSectionCode,
            decimal fallback)
        {
            var totalLine = lines
                .Where(line => line.IsTotal && IsSection(line, englishSectionCode, russianSectionCode))
                .OrderBy(line => line.SortOrder)
                .LastOrDefault(line => lineAmounts.ContainsKey(line.LineCode) && HasReportCalculationRule(line));

            return totalLine != null && lineAmounts.TryGetValue(totalLine.LineCode, out var total)
                ? total
                : fallback;
        }

        private static bool HasReportCalculationRule(ConfiguredLine line) =>
            line.AccountCodes.Count > 0 ||
            !string.IsNullOrWhiteSpace(line.Formula) ||
            line.FixedAmount != 0;

        private static bool IsSection(ConfiguredLine line, string englishSectionCode, string russianSectionCode) =>
            line.SectionCode.Equals(englishSectionCode, StringComparison.OrdinalIgnoreCase) ||
            line.SectionCode.Equals(russianSectionCode, StringComparison.OrdinalIgnoreCase);

        private static int NormalizeSign(int sign) => sign == -1 ? -1 : 1;

        private static bool TryGetLineNumber(string value, out int lineNumber)
        {
            var normalized = NormalizeLineReference(value);
            return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out lineNumber);
        }

        private static string NormalizeLineReference(string value)
        {
            var normalized = value.Trim().Trim('[', ']', '(', ')');
            if (normalized.StartsWith("n", StringComparison.OrdinalIgnoreCase) && normalized.Length > 1)
                normalized = normalized[1..];
            return normalized;
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

        private static DateTime? GetDate(Dictionary<string, object> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetValue(name, out var value) || value == null || value == DBNull.Value)
                    continue;
                if (value is DateTime date)
                    return date;
                if (DateTime.TryParse(value.ToString(), out date))
                    return date;
            }

            return null;
        }

        private static string GetString(Dictionary<string, object> row, params string[] names)
        {
            return names.Select(name => row.GetValueOrDefault(name)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static string ResolveInvoiceTaxType(InvoiceDocument invoice)
        {
            var firstLine = invoice.Lines.FirstOrDefault();
            return firstLine == null ? string.Empty : ResolveLineTaxType(firstLine);
        }

        private static string ResolveLineTaxType(InvoiceLineRow line)
        {
            if (line.VatRate > 0)
                return $"{line.VatRate:0.##}%";
            if (!string.IsNullOrWhiteSpace(line.VatTaxCode))
                return line.VatTaxCode;
            if (line.SalesTaxRate > 0)
                return $"НП {line.SalesTaxRate:0.##}%";
            if (!string.IsNullOrWhiteSpace(line.SalesTaxCode))
                return line.SalesTaxCode;
            return string.Empty;
        }

        private static string BuildInvoiceJournalNote(InvoiceDocument invoice, InvoiceLineRow line)
        {
            if (string.IsNullOrWhiteSpace(invoice.Basis))
                return line.Name;

            return string.IsNullOrWhiteSpace(line.Name)
                ? invoice.Basis
                : $"{invoice.Basis}; {line.Name}";
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
            public int SortOrder { get; init; }
            public int Sign { get; init; }
            public string Formula { get; init; } = string.Empty;
            public decimal FixedAmount { get; init; }
            public bool IsTotal { get; init; }
            public List<string> AccountCodes { get; init; } = new();
        }
    }
}
