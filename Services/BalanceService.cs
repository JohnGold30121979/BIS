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

            var accountCodes = accounts.Keys
                .Union(postings.SelectMany(posting => new[] { posting.DebitAccount, posting.CreditAccount }))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToList();

            return accountCodes.Select(code =>
            {
                var openingDebit = postings
                    .Where(posting => posting.Date < periodStart && posting.DebitAccount == code)
                    .Sum(posting => posting.Amount);
                var openingCredit = postings
                    .Where(posting => posting.Date < periodStart && posting.CreditAccount == code)
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
            var accountCodes = postings
                .SelectMany(posting => new[] { posting.DebitAccount, posting.CreditAccount })
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

                return new GeneralLedger
                {
                    AccountCode = code,
                    AccountName = accounts.TryGetValue(code, out var account) ? account.Name : code,
                    MonthlyTurnoverDebit = debitByMonth,
                    MonthlyTurnoverCredit = creditByMonth,
                    YearTurnoverDebit = debitByMonth.Values.Sum(),
                    YearTurnoverCredit = creditByMonth.Values.Sum()
                };
            }).ToList();
        }

        public async Task<EnterpriseBalance> GetEnterpriseBalanceAsync(DateTime date)
        {
            var postings = await new PostingService(_context).GetAllPostingsAsync(null, date.Date.AddDays(1).AddTicks(-1));
            var accounts = await LoadAccountsAsync();
            var result = new EnterpriseBalance();

            foreach (var (code, account) in accounts.OrderBy(pair => pair.Key))
            {
                var debit = postings.Where(posting => posting.DebitAccount == code).Sum(posting => posting.Amount);
                var credit = postings.Where(posting => posting.CreditAccount == code).Sum(posting => posting.Amount);
                var netDebit = debit - credit;
                if (netDebit == 0 || string.IsNullOrWhiteSpace(code))
                    continue;

                var item = new BalanceItem
                {
                    AccountCode = code,
                    AccountName = account.Name,
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

        private static decimal CalculateCurrentFinancialResult(
            IEnumerable<PostingViewModel> postings,
            IReadOnlyDictionary<string, AccountInfo> accounts)
        {
            decimal result = 0;
            foreach (var (code, account) in accounts.Where(pair => pair.Key.Length > 0 && pair.Key[0] >= '6'))
            {
                var debit = postings.Where(posting => posting.DebitAccount == code).Sum(posting => posting.Amount);
                var credit = postings.Where(posting => posting.CreditAccount == code).Sum(posting => posting.Amount);
                result += account.Type.Equals("Passive", StringComparison.OrdinalIgnoreCase)
                    ? credit - debit
                    : -(debit - credit);
            }

            return result;
        }

        private async Task<Dictionary<string, AccountInfo>> LoadAccountsAsync()
        {
            var metadata = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");
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

        private sealed class AccountInfo
        {
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Type { get; init; } = string.Empty;
        }
    }
}
