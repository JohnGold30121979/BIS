using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class OrganizationBalanceService
    {
        private readonly AppDbContext _context;

        public OrganizationBalanceService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            await new AccountingPeriodService(_context).EnsureSchemaAsync();
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""OrganizationBalanceSnapshots"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""PeriodStart"" timestamp with time zone NOT NULL,
                    ""PeriodEnd"" timestamp with time zone NOT NULL,
                    ""OrganizationId"" uuid NULL,
                    ""OrganizationName"" varchar(300) NOT NULL,
                    ""AccountCode"" varchar(50) NOT NULL,
                    ""AccountName"" varchar(300) NOT NULL,
                    ""CounterAccountCode"" varchar(50) NOT NULL,
                    ""CounterAccountName"" varchar(300) NOT NULL,
                    ""AccountPairName"" varchar(300) NOT NULL,
                    ""ModuleCode"" varchar(80) NOT NULL,
                    ""UsesCurrency"" boolean NOT NULL DEFAULT false,
                    ""IsOrganizationTotal"" boolean NOT NULL DEFAULT false,
                    ""OpeningDebit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""OpeningCredit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""TurnoverDebit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""TurnoverCredit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""ClosingDebit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""ClosingCredit"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""OpeningDebitCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""OpeningCreditCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""TurnoverDebitCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""TurnoverCreditCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""ClosingDebitCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""ClosingCreditCurrency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS ""IX_OrganizationBalanceSnapshots_Period""
                    ON ""OrganizationBalanceSnapshots"" (""PeriodStart"", ""PeriodEnd"");
                CREATE INDEX IF NOT EXISTS ""IX_OrganizationBalanceSnapshots_Organization""
                    ON ""OrganizationBalanceSnapshots"" (""OrganizationId"", ""OrganizationName"");
                ALTER TABLE ""OrganizationBalanceSnapshots"" ADD COLUMN IF NOT EXISTS ""ModuleCode"" varchar(80) NOT NULL DEFAULT '';
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'OrganizationBalanceSnapshots'
                          AND column_name = 'ArmCode') THEN
                        UPDATE ""OrganizationBalanceSnapshots""
                        SET ""ModuleCode"" = COALESCE(NULLIF(""ModuleCode"", ''), ""ArmCode"")
                        WHERE COALESCE(NULLIF(""ArmCode"", ''), '') <> '';
                    END IF;
                END $$;");
        }

        public async Task<OrganizationBalanceCalculationResult> CalculateAsync(DateTime startDate, DateTime endDate)
        {
            var periodStart = startDate.Date;
            var periodEnd = endDate.Date;
            if (periodEnd < periodStart)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала.");

            await EnsureSchemaAsync();

            var accounts = await LoadAccountsAsync();
            var result = new OrganizationBalanceCalculationResult
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd
            };
            var pairs = await LoadAdvancePaymentPairsAsync(accounts, result.Warnings);
            if (pairs.Count == 0)
                return result;

            var duplicateAccounts = pairs
                .SelectMany(pair => new[] { pair.DebitAccount, pair.CreditAccount }.Distinct(StringComparer.OrdinalIgnoreCase))
                .GroupBy(account => account, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateAccounts.Count > 0)
            {
                result.Warnings.Add(
                    "Один счет участвует в нескольких активных парах авансовых платежей: " +
                    string.Join(", ", duplicateAccounts));
            }

            var pairsByAccount = BuildPairsByAccount(pairs);
            var openingBalances = await LoadOpeningBalancesAsync(periodStart);
            var postings = await LoadPostingsAsync(periodEnd.AddDays(1));
            var accumulators = new Dictionary<OrganizationBalanceKey, BalanceAccumulator>();

            ApplyAccountOpeningBalances(openingBalances, pairsByAccount, accumulators);
            ApplyPostings(postings, pairsByAccount, openingBalances, periodStart, accumulators);

            var rows = accumulators.Values
                .Select(accumulator => accumulator.ToRow(periodStart, periodEnd, accounts))
                .Where(HasMovementOrBalance)
                .OrderBy(row => row.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CounterAccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var totalRows = rows
                .GroupBy(row => new { row.OrganizationId, row.OrganizationName })
                .Select(group => BuildOrganizationTotalRow(group, periodStart, periodEnd))
                .Where(HasMovementOrBalance)
                .ToList();

            result.Rows = rows
                .Concat(totalRows)
                .OrderBy(row => row.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.IsOrganizationTotal)
                .ThenBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CounterAccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await SaveSnapshotsAsync(result.Rows, periodStart, periodEnd);
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
                .Select(row =>
                {
                    var id = TryGetGuid(row, out var accountId, "Id") ? accountId : (Guid?)null;
                    var code = GetString(row, "Код", "code");
                    return new AccountInfo
                    {
                        Id = id,
                        Code = code,
                        Name = GetString(row, "Наименование", "name"),
                        LinkCurrencies = GetBool(row, false, "Связь с валютами", "link_currencies")
                    };
                })
                .Where(account => !string.IsNullOrWhiteSpace(account.Code))
                .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<AdvancePaymentPair>> LoadAdvancePaymentPairsAsync(
            IReadOnlyDictionary<string, AccountInfo> accounts,
            ICollection<string> warnings)
        {
            var accountCodesById = accounts.Values
                .Where(account => account.Id.HasValue)
                .ToDictionary(account => account.Id!.Value, account => account.Code);
            var rows = await new MetadataService(_context).GetAdvancePaymentPairsAsync();
            var pairs = new List<AdvancePaymentPair>();

            foreach (var row in rows)
            {
                var isActive = GetBool(row, true, "is_active", "Активен");
                var useSettlements = GetBool(row, true, "use_settlements", "Участвует во взаиморасчетах");
                if (!isActive || !useSettlements)
                    continue;

                var code = GetString(row, "code", "Код");
                var name = GetString(row, "name", "Вид расчета");
                var debit = ResolveAccountCode(GetString(row, "debit_account", "Дебет"), accountCodesById, accounts);
                var credit = ResolveAccountCode(GetString(row, "credit_account", "Кредит"), accountCodesById, accounts);
                if (string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit))
                    continue;

                var missingAccounts = new List<string>();
                if (!accounts.ContainsKey(debit))
                    missingAccounts.Add(debit);
                if (!accounts.ContainsKey(credit))
                    missingAccounts.Add(credit);
                if (missingAccounts.Count > 0)
                {
                    warnings.Add(
                        $"Пара счетов \"{(string.IsNullOrWhiteSpace(name) ? code : name)}\" пропущена: " +
                        "в плане счетов нет " + string.Join(", ", missingAccounts.Distinct(StringComparer.OrdinalIgnoreCase)) + ".");
                    continue;
                }

                pairs.Add(new AdvancePaymentPair
                {
                    Id = TryGetGuid(row, out var id, "Id") ? id : Guid.NewGuid(),
                    Code = code,
                    Name = name,
                    DebitAccount = debit,
                    CreditAccount = credit,
                    ModuleCode = NormalizeModuleName(GetString(row, "module_code", "arm_code")),
                    UseOrganizations = GetBool(row, true, "use_organizations", "Орг"),
                    UseCurrency = GetBool(row, false, "use_currency", "Валюта")
                });
            }

            return pairs;
        }

        private async Task<Dictionary<string, AccountOpeningBalance>> LoadOpeningBalancesAsync(DateTime periodStart)
        {
            var utcStart = DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
            var balances = await _context.AccountOpeningBalances.AsNoTracking()
                .Where(balance => balance.BalanceDate <= utcStart)
                .ToListAsync();

            return balances
                .GroupBy(balance => balance.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.BalanceDate).First(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<PostingRow>> LoadPostingsAsync(DateTime periodEndExclusive)
        {
            var rows = new List<PostingRow>();
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT p.posting_date, p.debit_account, p.credit_account,
                       COALESCE(p.amount_kgs, 0) AS amount_kgs,
                       COALESCE(p.amount_currency, 0) AS amount_currency,
                       CASE
                           WHEN COALESCE(p.organization_id::text, '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                           THEN p.organization_id::text
                           ELSE NULL
                       END AS organization_id_text,
                       COALESCE(NULLIF(o.""name"", ''), 'Без организации') AS organization_name
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON p.organization_id::text = o.""Id""::text
                WHERE p.is_active = true
                  AND p.posting_date < @periodEndExclusive
                ORDER BY p.posting_date, p.doc_number";
            command.Parameters.Add(new NpgsqlParameter("@periodEndExclusive", periodEndExclusive));

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add(new PostingRow
                    {
                        Date = reader.GetDateTime(reader.GetOrdinal("posting_date")),
                        DebitAccount = reader["debit_account"]?.ToString() ?? string.Empty,
                        CreditAccount = reader["credit_account"]?.ToString() ?? string.Empty,
                        Amount = GetDecimal(reader["amount_kgs"]),
                        AmountCurrency = GetDecimal(reader["amount_currency"]),
                        OrganizationId = Guid.TryParse(reader["organization_id_text"]?.ToString(), out var organizationId)
                            ? organizationId
                            : null,
                        OrganizationName = reader["organization_name"]?.ToString() ?? "Без организации"
                    });
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return rows;
        }

        private static Dictionary<string, List<AdvancePaymentPair>> BuildPairsByAccount(IEnumerable<AdvancePaymentPair> pairs)
        {
            var result = new Dictionary<string, List<AdvancePaymentPair>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in pairs)
            {
                Add(pair.DebitAccount, pair);
                if (!pair.CreditAccount.Equals(pair.DebitAccount, StringComparison.OrdinalIgnoreCase))
                    Add(pair.CreditAccount, pair);
            }

            return result;

            void Add(string accountCode, AdvancePaymentPair pair)
            {
                if (!result.TryGetValue(accountCode, out var accountPairs))
                {
                    accountPairs = new List<AdvancePaymentPair>();
                    result[accountCode] = accountPairs;
                }

                if (!accountPairs.Any(item => item.Id == pair.Id))
                    accountPairs.Add(pair);
            }
        }

        private static void ApplyAccountOpeningBalances(
            IReadOnlyDictionary<string, AccountOpeningBalance> openingBalances,
            IReadOnlyDictionary<string, List<AdvancePaymentPair>> pairsByAccount,
            IDictionary<OrganizationBalanceKey, BalanceAccumulator> accumulators)
        {
            foreach (var (accountCode, openingBalance) in openingBalances)
            {
                if (!pairsByAccount.TryGetValue(accountCode, out var pairs))
                    continue;

                foreach (var pair in pairs)
                {
                    var accumulator = GetAccumulator(accumulators, pair, null, "Без организации");
                    accumulator.OpeningDebit += openingBalance.Debit;
                    accumulator.OpeningCredit += openingBalance.Credit;
                }
            }
        }

        private static void ApplyPostings(
            IEnumerable<PostingRow> postings,
            IReadOnlyDictionary<string, List<AdvancePaymentPair>> pairsByAccount,
            IReadOnlyDictionary<string, AccountOpeningBalance> openingBalances,
            DateTime periodStart,
            IDictionary<OrganizationBalanceKey, BalanceAccumulator> accumulators)
        {
            foreach (var posting in postings)
            {
                if (pairsByAccount.TryGetValue(posting.DebitAccount, out var debitPairs) &&
                    IsOpeningPostingIncluded(posting, posting.DebitAccount, openingBalances, periodStart))
                {
                    foreach (var pair in debitPairs)
                    {
                        var accumulator = GetAccumulator(accumulators, pair, posting.OrganizationId, posting.OrganizationName);
                        if (posting.Date < periodStart)
                        {
                            accumulator.OpeningDebit += posting.Amount;
                            if (pair.UseCurrency)
                                accumulator.OpeningDebitCurrency += posting.AmountCurrency;
                        }
                        else
                        {
                            accumulator.TurnoverDebit += posting.Amount;
                            if (pair.UseCurrency)
                                accumulator.TurnoverDebitCurrency += posting.AmountCurrency;
                        }
                    }
                }

                if (pairsByAccount.TryGetValue(posting.CreditAccount, out var creditPairs) &&
                    IsOpeningPostingIncluded(posting, posting.CreditAccount, openingBalances, periodStart))
                {
                    foreach (var pair in creditPairs)
                    {
                        var accumulator = GetAccumulator(accumulators, pair, posting.OrganizationId, posting.OrganizationName);
                        if (posting.Date < periodStart)
                        {
                            accumulator.OpeningCredit += posting.Amount;
                            if (pair.UseCurrency)
                                accumulator.OpeningCreditCurrency += posting.AmountCurrency;
                        }
                        else
                        {
                            accumulator.TurnoverCredit += posting.Amount;
                            if (pair.UseCurrency)
                                accumulator.TurnoverCreditCurrency += posting.AmountCurrency;
                        }
                    }
                }
            }
        }

        private static bool IsOpeningPostingIncluded(
            PostingRow posting,
            string accountCode,
            IReadOnlyDictionary<string, AccountOpeningBalance> openingBalances,
            DateTime periodStart)
        {
            if (posting.Date >= periodStart)
                return true;

            if (!openingBalances.TryGetValue(accountCode, out var openingBalance))
                return true;

            return posting.Date >= openingBalance.BalanceDate.Date;
        }

        private static BalanceAccumulator GetAccumulator(
            IDictionary<OrganizationBalanceKey, BalanceAccumulator> accumulators,
            AdvancePaymentPair pair,
            Guid? organizationId,
            string organizationName)
        {
            var normalizedOrganization = string.IsNullOrWhiteSpace(organizationName)
                ? "Без организации"
                : organizationName;
            var key = new OrganizationBalanceKey(
                pair.DebitAccount,
                pair.CreditAccount,
                organizationId,
                normalizedOrganization);

            if (!accumulators.TryGetValue(key, out var accumulator))
            {
                accumulator = new BalanceAccumulator
                {
                    Pair = pair,
                    OrganizationId = organizationId,
                    OrganizationName = normalizedOrganization
                };
                accumulators[key] = accumulator;
            }

            return accumulator;
        }

        private static OrganizationBalanceRow BuildOrganizationTotalRow(
            IEnumerable<OrganizationBalanceRow> rows,
            DateTime periodStart,
            DateTime periodEnd)
        {
            var list = rows.ToList();
            var first = list.First();
            var row = new OrganizationBalanceRow
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                OrganizationId = first.OrganizationId,
                OrganizationName = first.OrganizationName,
                AccountPairName = "Итого по организации",
                IsOrganizationTotal = true,
                UsesCurrency = list.Any(item => item.UsesCurrency),
                OpeningDebit = list.Sum(item => item.OpeningDebit),
                OpeningCredit = list.Sum(item => item.OpeningCredit),
                TurnoverDebit = list.Sum(item => item.TurnoverDebit),
                TurnoverCredit = list.Sum(item => item.TurnoverCredit),
                OpeningDebitCurrency = list.Sum(item => item.OpeningDebitCurrency),
                OpeningCreditCurrency = list.Sum(item => item.OpeningCreditCurrency),
                TurnoverDebitCurrency = list.Sum(item => item.TurnoverDebitCurrency),
                TurnoverCreditCurrency = list.Sum(item => item.TurnoverCreditCurrency)
            };
            ApplyClosing(row);
            return row;
        }

        private static bool HasMovementOrBalance(OrganizationBalanceRow row)
        {
            return row.ClosingDebit != 0 ||
                   row.ClosingCredit != 0 ||
                   row.TurnoverDebit != 0 ||
                   row.TurnoverCredit != 0 ||
                   row.ClosingDebitCurrency != 0 ||
                   row.ClosingCreditCurrency != 0 ||
                   row.TurnoverDebitCurrency != 0 ||
                   row.TurnoverCreditCurrency != 0;
        }

        private async Task SaveSnapshotsAsync(IEnumerable<OrganizationBalanceRow> rows, DateTime periodStart, DateTime periodEnd)
        {
            var utcStart = DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
            var utcEnd = DateTime.SpecifyKind(periodEnd, DateTimeKind.Utc);
            await _context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""OrganizationBalanceSnapshots""
                WHERE ""PeriodStart"" = @start AND ""PeriodEnd"" = @end",
                new NpgsqlParameter("@start", utcStart),
                new NpgsqlParameter("@end", utcEnd));

            const string insertSql = @"
                INSERT INTO ""OrganizationBalanceSnapshots""
                (""Id"", ""PeriodStart"", ""PeriodEnd"", ""OrganizationId"", ""OrganizationName"",
                 ""AccountCode"", ""AccountName"", ""CounterAccountCode"", ""CounterAccountName"",
                 ""AccountPairName"", ""ModuleCode"", ""UsesCurrency"", ""IsOrganizationTotal"",
                 ""OpeningDebit"", ""OpeningCredit"", ""TurnoverDebit"", ""TurnoverCredit"",
                 ""ClosingDebit"", ""ClosingCredit"", ""OpeningDebitCurrency"", ""OpeningCreditCurrency"",
                 ""TurnoverDebitCurrency"", ""TurnoverCreditCurrency"", ""ClosingDebitCurrency"",
                 ""ClosingCreditCurrency"", ""CreatedAt"")
                VALUES
                (@id, @start, @end, @organizationId, @organizationName,
                 @accountCode, @accountName, @counterAccountCode, @counterAccountName,
                 @accountPairName, @moduleCode, @usesCurrency, @isOrganizationTotal,
                 @openingDebit, @openingCredit, @turnoverDebit, @turnoverCredit,
                 @closingDebit, @closingCredit, @openingDebitCurrency, @openingCreditCurrency,
                 @turnoverDebitCurrency, @turnoverCreditCurrency, @closingDebitCurrency,
                 @closingCreditCurrency, NOW())";

            foreach (var row in rows)
            {
                await _context.Database.ExecuteSqlRawAsync(insertSql,
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@start", utcStart),
                    new NpgsqlParameter("@end", utcEnd),
                    new NpgsqlParameter("@organizationId", (object?)row.OrganizationId ?? DBNull.Value),
                    new NpgsqlParameter("@organizationName", row.OrganizationName),
                    new NpgsqlParameter("@accountCode", row.AccountCode),
                    new NpgsqlParameter("@accountName", row.AccountName),
                    new NpgsqlParameter("@counterAccountCode", row.CounterAccountCode),
                    new NpgsqlParameter("@counterAccountName", row.CounterAccountName),
                    new NpgsqlParameter("@accountPairName", row.AccountPairName),
                    new NpgsqlParameter("@moduleCode", row.ModuleCode),
                    new NpgsqlParameter("@usesCurrency", row.UsesCurrency),
                    new NpgsqlParameter("@isOrganizationTotal", row.IsOrganizationTotal),
                    new NpgsqlParameter("@openingDebit", row.OpeningDebit),
                    new NpgsqlParameter("@openingCredit", row.OpeningCredit),
                    new NpgsqlParameter("@turnoverDebit", row.TurnoverDebit),
                    new NpgsqlParameter("@turnoverCredit", row.TurnoverCredit),
                    new NpgsqlParameter("@closingDebit", row.ClosingDebit),
                    new NpgsqlParameter("@closingCredit", row.ClosingCredit),
                    new NpgsqlParameter("@openingDebitCurrency", row.OpeningDebitCurrency),
                    new NpgsqlParameter("@openingCreditCurrency", row.OpeningCreditCurrency),
                    new NpgsqlParameter("@turnoverDebitCurrency", row.TurnoverDebitCurrency),
                    new NpgsqlParameter("@turnoverCreditCurrency", row.TurnoverCreditCurrency),
                    new NpgsqlParameter("@closingDebitCurrency", row.ClosingDebitCurrency),
                    new NpgsqlParameter("@closingCreditCurrency", row.ClosingCreditCurrency));
            }
        }

        private static void ApplyClosing(OrganizationBalanceRow row)
        {
            var closing = row.OpeningDebit - row.OpeningCredit + row.TurnoverDebit - row.TurnoverCredit;
            row.ClosingDebit = Math.Max(closing, 0);
            row.ClosingCredit = Math.Max(-closing, 0);

            var closingCurrency = row.OpeningDebitCurrency - row.OpeningCreditCurrency +
                                  row.TurnoverDebitCurrency - row.TurnoverCreditCurrency;
            row.ClosingDebitCurrency = Math.Max(closingCurrency, 0);
            row.ClosingCreditCurrency = Math.Max(-closingCurrency, 0);
        }

        private static string ResolveAccountCode(
            string rawValue,
            IReadOnlyDictionary<Guid, string> accountCodesById,
            IReadOnlyDictionary<string, AccountInfo> accounts)
        {
            var value = ExtractAccountCode(rawValue, accountCodesById);
            if (string.IsNullOrWhiteSpace(value) || accounts.ContainsKey(value))
                return value;

            var expanded = ResolveCompactAccountCode(value, accounts.Keys);
            return string.IsNullOrWhiteSpace(expanded) ? value : expanded;
        }

        private static string ExtractAccountCode(string rawValue, IReadOnlyDictionary<Guid, string> accountCodesById)
        {
            var value = rawValue.Trim();
            if (Guid.TryParse(value, out var id) && accountCodesById.TryGetValue(id, out var accountCode))
                return accountCode;

            var separatorIndex = value.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? value[..separatorIndex].Trim() : value;
        }

        private static string ResolveCompactAccountCode(string value, IEnumerable<string> accountCodes)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.All(char.IsDigit))
                return string.Empty;

            var paddedCode = value.PadRight(8, '0');
            var accounts = accountCodes.ToList();
            if (accounts.Contains(paddedCode, StringComparer.OrdinalIgnoreCase))
                return paddedCode;

            var matches = accounts
                .Where(code =>
                    code.StartsWith(value, StringComparison.OrdinalIgnoreCase) &&
                    code.Length > value.Length &&
                    code[value.Length..].All(character => character == '0'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return matches.Count == 1 ? matches[0] : string.Empty;
        }

        private static string NormalizeModuleName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Финансы";

            return value.Trim().ToUpperInvariant() switch
            {
                "ФИН" or "ФИНАНСЫ" or "FIN" or "FINANCE" => "Финансы",
                "ОС" or "FIXEDASSETS" => "Основные средства",
                "ТМЦ" or "INVENTORY" => "Учет материальных ценностей",
                _ => value.Trim()
            };
        }

        private static bool TryGetGuid(IReadOnlyDictionary<string, object> row, out Guid id, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetValue(name, out var value) || value == null || value == DBNull.Value)
                    continue;
                if (value is Guid guid)
                {
                    id = guid;
                    return true;
                }
                if (Guid.TryParse(value.ToString(), out id))
                    return true;
            }

            id = Guid.Empty;
            return false;
        }

        private static string GetString(IReadOnlyDictionary<string, object> row, params string[] names)
        {
            return names
                .Select(name => row.TryGetValue(name, out var value) ? value?.ToString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private static bool GetBool(IReadOnlyDictionary<string, object> row, bool defaultValue, params string[] names)
        {
            foreach (var name in names)
            {
                if (!row.TryGetValue(name, out var value) || value == null || value == DBNull.Value)
                    continue;
                if (value is bool boolean)
                    return boolean;
                if (bool.TryParse(value.ToString(), out boolean))
                    return boolean;
                if (int.TryParse(value.ToString(), out var integer))
                    return integer != 0;
            }

            return defaultValue;
        }

        private static decimal GetDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;
            if (value is decimal decimalValue)
                return decimalValue;
            if (value is double doubleValue)
                return (decimal)doubleValue;
            if (value is float floatValue)
                return (decimal)floatValue;
            if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out parsed)
                ? parsed
                : 0m;
        }

        private sealed class AdvancePaymentPair
        {
            public Guid Id { get; init; }
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string DebitAccount { get; init; } = string.Empty;
            public string CreditAccount { get; init; } = string.Empty;
            public string ModuleCode { get; init; } = string.Empty;
            public bool UseOrganizations { get; init; }
            public bool UseCurrency { get; init; }
        }

        private sealed class AccountInfo
        {
            public Guid? Id { get; init; }
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public bool LinkCurrencies { get; init; }
        }

        private sealed class PostingRow
        {
            public DateTime Date { get; init; }
            public Guid? OrganizationId { get; init; }
            public string OrganizationName { get; init; } = string.Empty;
            public string DebitAccount { get; init; } = string.Empty;
            public string CreditAccount { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public decimal AmountCurrency { get; init; }
        }

        private readonly record struct OrganizationBalanceKey(
            string AccountCode,
            string CounterAccountCode,
            Guid? OrganizationId,
            string OrganizationName);

        private sealed class BalanceAccumulator
        {
            public AdvancePaymentPair Pair { get; init; } = new();
            public Guid? OrganizationId { get; init; }
            public string OrganizationName { get; init; } = string.Empty;
            public decimal OpeningDebit { get; set; }
            public decimal OpeningCredit { get; set; }
            public decimal TurnoverDebit { get; set; }
            public decimal TurnoverCredit { get; set; }
            public decimal OpeningDebitCurrency { get; set; }
            public decimal OpeningCreditCurrency { get; set; }
            public decimal TurnoverDebitCurrency { get; set; }
            public decimal TurnoverCreditCurrency { get; set; }

            public OrganizationBalanceRow ToRow(
                DateTime periodStart,
                DateTime periodEnd,
                IReadOnlyDictionary<string, AccountInfo> accounts)
            {
                var row = new OrganizationBalanceRow
                {
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OrganizationId = OrganizationId,
                    OrganizationName = OrganizationName,
                    AccountCode = Pair.DebitAccount,
                    AccountName = accounts.TryGetValue(Pair.DebitAccount, out var account)
                        ? account.Name
                        : Pair.DebitAccount,
                    CounterAccountCode = Pair.CreditAccount,
                    CounterAccountName = accounts.TryGetValue(Pair.CreditAccount, out var counterAccount)
                        ? counterAccount.Name
                        : Pair.CreditAccount,
                    AccountPairName = string.IsNullOrWhiteSpace(Pair.Name) ? Pair.Code : Pair.Name,
                    ModuleCode = Pair.ModuleCode,
                    UsesCurrency = Pair.UseCurrency,
                    OpeningDebit = OpeningDebit,
                    OpeningCredit = OpeningCredit,
                    TurnoverDebit = TurnoverDebit,
                    TurnoverCredit = TurnoverCredit,
                    OpeningDebitCurrency = OpeningDebitCurrency,
                    OpeningCreditCurrency = OpeningCreditCurrency,
                    TurnoverDebitCurrency = TurnoverDebitCurrency,
                    TurnoverCreditCurrency = TurnoverCreditCurrency
                };
                ApplyClosing(row);
                return row;
            }
        }
    }
}
