using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    public sealed record ExchangeRateDifferenceCalculationResult(
        DateTime PeriodEnd,
        int ProcessedBalances,
        int CreatedPostings,
        decimal GainAmount,
        decimal LossAmount,
        IReadOnlyList<string> Warnings);

    public sealed class ExchangeRateDifferenceService
    {
        private const string CalculationDocumentType = "Расчет курсовой разницы";
        private const string FinanceModuleName = "Финансы";
        private const string ExchangeGainAccountCode = "91400000";
        private const string ExchangeLossAccountCode = "95200000";

        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;

        public ExchangeRateDifferenceService(AppDbContext context)
        {
            _context = context;
            _metadataService = new MetadataService(context);
        }

        public async Task<ExchangeRateDifferenceCalculationResult> CalculateForDateAsync(
            DateTime periodEnd,
            bool replaceExistingCalculation = true)
        {
            var calculationDate = DateTime.SpecifyKind(periodEnd.Date, DateTimeKind.Utc);
            var documentNumber = BuildDocumentNumber(calculationDate);
            var warnings = new List<string>();

            await EnsurePostingCurrencyColumnsAsync();

            if (replaceExistingCalculation)
                await DeleteExistingCalculationAsync(calculationDate, documentNumber);

            var currencyReferences = await LoadCurrencyReferencesAsync();
            var configuredRules = await LoadConfiguredRulesAsync(currencyReferences);
            var currencyAccountCodes = await LoadCurrencyAccountCodesAsync();
            var balances = await LoadCurrencyBalancesAsync(calculationDate);

            if (configuredRules.Count > 0)
            {
                balances = balances
                    .Where(balance => configuredRules.Any(rule =>
                        rule.AccountCode.Equals(balance.AccountCode, StringComparison.OrdinalIgnoreCase) &&
                        (!rule.CurrencyId.HasValue ||
                         rule.CurrencyId.Value.ToString().Equals(balance.CurrencyId, StringComparison.OrdinalIgnoreCase))))
                    .ToList();
            }
            else if (currencyAccountCodes.Count == 0)
            {
                warnings.Add("В плане счетов не найдены счета с признаком связи с валютами. Расчет выполнен по всем проводкам, где заполнена валюта.");
            }
            else
            {
                balances = balances
                    .Where(balance => currencyAccountCodes.Contains(balance.AccountCode))
                    .ToList();
            }

            var processedBalances = 0;
            var createdPostings = 0;
            var gainAmount = 0m;
            var lossAmount = 0m;

            foreach (var balance in balances)
            {
                if (!Guid.TryParse(balance.CurrencyId, out var currencyId))
                {
                    warnings.Add($"По счету {balance.AccountCode} пропущена валюта с некорректным идентификатором.");
                    continue;
                }

                if (currencyReferences.TryGetValue(currencyId, out var currencyReference) && currencyReference.IsBaseCurrency)
                    continue;

                var currencyRate = await _metadataService.GetCurrencyRateForDateAsync(currencyId, calculationDate);
                if (currencyRate == null)
                {
                    var currencyName = ResolveCurrencyDisplay(currencyReferences, currencyId);
                    warnings.Add($"Не найден курс валюты {currencyName} на {calculationDate:dd.MM.yyyy} или более раннюю дату.");
                    continue;
                }

                var revaluedAmount = RoundMoney(balance.AmountInCurrency * currencyRate.Rate);
                var difference = RoundMoney(revaluedAmount - balance.AmountInNationalCurrency);
                if (Math.Abs(difference) < 0.01m)
                    continue;

                processedBalances++;

                if (difference > 0)
                {
                    await InsertCalculationPostingAsync(
                        calculationDate,
                        documentNumber,
                        balance.AccountCode,
                        ExchangeGainAccountCode,
                        difference,
                        currencyId,
                        balance.AmountInCurrency,
                        currencyRate.Rate);
                    gainAmount += difference;
                }
                else
                {
                    var loss = Math.Abs(difference);
                    await InsertCalculationPostingAsync(
                        calculationDate,
                        documentNumber,
                        ExchangeLossAccountCode,
                        balance.AccountCode,
                        loss,
                        currencyId,
                        balance.AmountInCurrency,
                        currencyRate.Rate);
                    lossAmount += loss;
                }

                createdPostings++;
            }

            return new ExchangeRateDifferenceCalculationResult(
                calculationDate,
                processedBalances,
                createdPostings,
                gainAmount,
                lossAmount,
                warnings);
        }

        private async Task EnsurePostingCurrencyColumnsAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF to_regclass('public.doc_postings') IS NOT NULL THEN
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2);
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
                    END IF;
                END $$;");
        }

        private async Task DeleteExistingCalculationAsync(DateTime calculationDate, string documentNumber)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM doc_postings
                WHERE document_type = @documentType
                  AND doc_number = @documentNumber
                  AND posting_date::date = @calculationDate;",
                new NpgsqlParameter("@documentType", CalculationDocumentType),
                new NpgsqlParameter("@documentNumber", documentNumber),
                new NpgsqlParameter("@calculationDate", calculationDate.Date));
        }

        private async Task<List<CurrencyBalance>> LoadCurrencyBalancesAsync(DateTime calculationDate)
        {
            var balances = new List<CurrencyBalance>();
            const string sql = @"
                SELECT account_code,
                       currency_id,
                       SUM(amount_in_national_currency) AS amount_in_national_currency,
                       SUM(amount_in_currency) AS amount_in_currency
                FROM (
                    SELECT debit_account AS account_code,
                           currency_id,
                           COALESCE(amount_kgs, 0) AS amount_in_national_currency,
                           COALESCE(amount_currency, 0) AS amount_in_currency
                    FROM doc_postings
                    WHERE is_active = true
                      AND posting_date::date <= @calculationDate
                      AND COALESCE(NULLIF(currency_id, ''), '') <> ''

                    UNION ALL

                    SELECT credit_account AS account_code,
                           currency_id,
                           -COALESCE(amount_kgs, 0) AS amount_in_national_currency,
                           -COALESCE(amount_currency, 0) AS amount_in_currency
                    FROM doc_postings
                    WHERE is_active = true
                      AND posting_date::date <= @calculationDate
                      AND COALESCE(NULLIF(currency_id, ''), '') <> ''
                ) movements
                WHERE COALESCE(NULLIF(account_code, ''), '') <> ''
                GROUP BY account_code, currency_id
                HAVING ABS(SUM(amount_in_currency)) >= 0.005
                ORDER BY account_code, currency_id;";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@calculationDate", calculationDate.Date));

            var closeConnection = false;
            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    closeConnection = true;
                }

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    balances.Add(new CurrencyBalance(
                        reader["account_code"]?.ToString() ?? string.Empty,
                        reader["currency_id"]?.ToString() ?? string.Empty,
                        ReadDecimal(reader["amount_in_national_currency"]),
                        ReadDecimal(reader["amount_in_currency"])));
                }
            }
            finally
            {
                if (closeConnection)
                    await _context.Database.CloseConnectionAsync();
            }

            return balances;
        }

        private async Task<List<ExchangeRateDifferenceRule>> LoadConfiguredRulesAsync(
            IReadOnlyDictionary<Guid, CurrencyReference> currencyReferences)
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    item.Name == CalculationDocumentType);
            if (catalog == null)
                return new List<ExchangeRateDifferenceRule>();

            var accountCodeById = await LoadAccountCodeByIdAsync();
            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var rules = new List<ExchangeRateDifferenceRule>();

            foreach (var row in rows)
            {
                if (!ReadBool(row.GetValueOrDefault("Активен")) &&
                    !ReadBool(row.GetValueOrDefault("is_active")))
                {
                    continue;
                }

                var accountCode = ExtractAccountCode(
                    row.GetValueOrDefault("account_id")?.ToString() ??
                    row.GetValueOrDefault("Счет")?.ToString(),
                    accountCodeById);
                if (string.IsNullOrWhiteSpace(accountCode))
                    continue;

                var currencyId = ResolveCurrencyId(
                    row.GetValueOrDefault("currency_id")?.ToString() ??
                    row.GetValueOrDefault("Валюта")?.ToString(),
                    currencyReferences);

                rules.Add(new ExchangeRateDifferenceRule(accountCode, currencyId));
            }

            return rules;
        }

        private async Task<HashSet<string>> LoadCurrencyAccountCodesAsync()
        {
            var rows = await _metadataService.GetChartOfAccountsSelectionDataAsync(FinanceModuleName);
            return rows
                .Where(row => ReadBool(row.GetValueOrDefault("Связь с валютами")) ||
                              ReadBool(row.GetValueOrDefault("link_currencies")))
                .Select(row => row.GetValueOrDefault("Код")?.ToString() ??
                               row.GetValueOrDefault("code")?.ToString() ??
                               string.Empty)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private async Task<Dictionary<Guid, string>> LoadAccountCodeByIdAsync()
        {
            var rows = await _metadataService.GetChartOfAccountsSelectionDataAsync();
            var result = new Dictionary<Guid, string>();
            foreach (var row in rows)
            {
                var idText = row.GetValueOrDefault("Id")?.ToString();
                var code = row.GetValueOrDefault("Код")?.ToString() ??
                           row.GetValueOrDefault("code")?.ToString() ??
                           string.Empty;
                if (Guid.TryParse(idText, out var id) && !string.IsNullOrWhiteSpace(code))
                    result[id] = code.Trim();
            }

            return result;
        }

        private async Task<Dictionary<Guid, CurrencyReference>> LoadCurrencyReferencesAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Справочник валют");
            if (catalog == null)
                return new Dictionary<Guid, CurrencyReference>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var references = new Dictionary<Guid, CurrencyReference>();
            foreach (var row in rows)
            {
                var idText = row.GetValueOrDefault("Id")?.ToString();
                if (!Guid.TryParse(idText, out var id))
                    continue;

                var code = row.GetValueOrDefault("Код")?.ToString() ??
                           row.GetValueOrDefault("code")?.ToString() ??
                           string.Empty;
                var name = row.GetValueOrDefault("Наименование")?.ToString() ??
                           row.GetValueOrDefault("name")?.ToString() ??
                           string.Empty;
                var isBaseCurrency = ReadBool(row.GetValueOrDefault("Базовая")) ||
                                     ReadBool(row.GetValueOrDefault("is_base"));

                references[id] = new CurrencyReference(code, name, isBaseCurrency);
            }

            return references;
        }

        private async Task InsertCalculationPostingAsync(
            DateTime calculationDate,
            string documentNumber,
            string debitAccount,
            string creditAccount,
            decimal amount,
            Guid currencyId,
            decimal amountInCurrency,
            decimal exchangeRate)
        {
            const string sql = @"
                INSERT INTO doc_postings
                    (""Id"", posting_date, doc_number, document_type, module_code,
                     debit_account, credit_account, amount_kgs, amount_currency, currency_id,
                     description, is_active, ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @postingDate, @documentNumber, @documentType, @moduleName,
                     @debitAccount, @creditAccount, @amount, 0, @currencyId,
                     @description, true, NOW(), NOW());";

            var description =
                $"Автоматический расчет курсовой разницы на {calculationDate:dd.MM.yyyy}. " +
                $"Валютный остаток: {amountInCurrency.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))}, " +
                $"курс: {exchangeRate.ToString("N6", CultureInfo.GetCultureInfo("ru-RU"))}.";

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@postingDate", calculationDate),
                new NpgsqlParameter("@documentNumber", documentNumber),
                new NpgsqlParameter("@documentType", CalculationDocumentType),
                new NpgsqlParameter("@moduleName", FinanceModuleName),
                new NpgsqlParameter("@debitAccount", debitAccount),
                new NpgsqlParameter("@creditAccount", creditAccount),
                new NpgsqlParameter("@amount", amount),
                new NpgsqlParameter("@currencyId", currencyId.ToString()),
                new NpgsqlParameter("@description", description));
        }

        private static string BuildDocumentNumber(DateTime calculationDate) =>
            $"Курсовая разница {calculationDate:yyyyMMdd}";

        private static string ExtractAccountCode(
            string? value,
            IReadOnlyDictionary<Guid, string> accountCodeById)
        {
            var text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (Guid.TryParse(text, out var id) && accountCodeById.TryGetValue(id, out var code))
                return code;

            var digitPrefix = new string(text.TakeWhile(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(digitPrefix) ? text : digitPrefix;
        }

        private static Guid? ResolveCurrencyId(
            string? value,
            IReadOnlyDictionary<Guid, CurrencyReference> references)
        {
            var text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (Guid.TryParse(text, out var id))
                return id;

            foreach (var item in references)
            {
                if (item.Value.Code.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    item.Value.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    $"{item.Value.Code} - {item.Value.Name}".Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Key;
                }
            }

            return null;
        }

        private static string ResolveCurrencyDisplay(
            IReadOnlyDictionary<Guid, CurrencyReference> references,
            Guid currencyId)
        {
            if (!references.TryGetValue(currencyId, out var reference))
                return currencyId.ToString();

            if (!string.IsNullOrWhiteSpace(reference.Code) && !string.IsNullOrWhiteSpace(reference.Name))
                return $"{reference.Code} - {reference.Name}";

            return string.IsNullOrWhiteSpace(reference.Code) ? currencyId.ToString() : reference.Code;
        }

        private static decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static decimal ReadDecimal(object? value)
        {
            if (value == null || value is DBNull)
                return 0m;

            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(object? value)
        {
            if (value == null || value is DBNull)
                return false;

            return value switch
            {
                bool boolValue => boolValue,
                int intValue => intValue != 0,
                long longValue => longValue != 0,
                decimal decimalValue => decimalValue != 0,
                string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               text.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                               text.Equals("1", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private sealed record CurrencyBalance(
            string AccountCode,
            string CurrencyId,
            decimal AmountInNationalCurrency,
            decimal AmountInCurrency);

        private sealed record CurrencyReference(
            string Code,
            string Name,
            bool IsBaseCurrency);

        private sealed record ExchangeRateDifferenceRule(
            string AccountCode,
            Guid? CurrencyId);
    }
}
