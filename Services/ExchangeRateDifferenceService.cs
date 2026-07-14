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
            return await CalculateForDateAsync(periodEnd, null, replaceExistingCalculation);
        }

        public async Task<ExchangeRateDifferenceCalculationResult> CalculateForDateAsync(
            DateTime periodEnd,
            DateTime? periodStart,
            bool replaceExistingCalculation = true)
        {
            var calculationDate = DateTime.SpecifyKind(periodEnd.Date, DateTimeKind.Utc);
            var calculationStart = DateTime.SpecifyKind(
                (periodStart?.Date ?? new DateTime(calculationDate.Year, calculationDate.Month, 1)).Date,
                DateTimeKind.Utc);
            if (calculationStart > calculationDate)
                calculationStart = new DateTime(calculationDate.Year, calculationDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var documentNumber = BuildDocumentNumber(calculationDate);
            var warnings = new List<string>();

            await EnsurePostingCurrencyColumnsAsync();

            if (replaceExistingCalculation)
                await DeleteExistingCalculationAsync(documentNumber);

            var currencyReferences = await LoadCurrencyReferencesAsync();
            var configuredRules = await LoadConfiguredRulesAsync(currencyReferences);
            var currencyAccountCodes = await LoadCurrencyAccountCodesAsync();
            var accountKinds = await LoadAccountKindsAsync();
            var movements = await LoadCurrencyMovementsAsync(calculationDate);
            var balances = BuildCurrencyBalances(movements, configuredRules, currencyAccountCodes, warnings);

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

                var calculatedPostings = balance.Rule?.CalculationAlgorithm == 3
                    ? await BuildMovementAwarePostingsAsync(balance, currencyId, calculationStart, calculationDate, warnings, accountKinds)
                    : await BuildBalancePostingsAsync(balance, currencyId, calculationDate, warnings, accountKinds);

                if (calculatedPostings.Count == 0)
                    continue;

                processedBalances++;
                foreach (var posting in calculatedPostings)
                {
                    await InsertCalculationPostingAsync(documentNumber, posting);
                    createdPostings++;
                    if (posting.IsGain)
                        gainAmount += posting.Amount;
                    else
                        lossAmount += posting.Amount;
                }
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
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS exchange_rate numeric(18,4);
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS debit_report_line integer;
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS credit_report_line integer;
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS organization_id uuid;
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS employee_id uuid;
                    END IF;
                END $$;");
        }

        private async Task DeleteExistingCalculationAsync(string documentNumber)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM doc_postings
                WHERE document_type = @documentType
                  AND doc_number = @documentNumber;",
                new NpgsqlParameter("@documentType", CalculationDocumentType),
                new NpgsqlParameter("@documentNumber", documentNumber));
        }

        private async Task<List<CurrencyMovement>> LoadCurrencyMovementsAsync(DateTime calculationDate)
        {
            var movements = new List<CurrencyMovement>();
            const string sql = @"
                SELECT posting_date,
                       account_code,
                       currency_id,
                       module_code,
                       organization_id,
                       employee_id,
                       amount_in_national_currency,
                       amount_in_currency
                FROM (
                    SELECT posting_date::date AS posting_date,
                           debit_account AS account_code,
                           currency_id,
                           COALESCE(module_code, '') AS module_code,
                           COALESCE(organization_id::text, '') AS organization_id,
                           COALESCE(employee_id::text, '') AS employee_id,
                           COALESCE(amount_kgs, 0) AS amount_in_national_currency,
                           COALESCE(amount_currency, 0) AS amount_in_currency
                    FROM doc_postings
                    WHERE is_active = true
                      AND posting_date::date <= @calculationDate
                      AND COALESCE(NULLIF(currency_id, ''), '') <> ''

                    UNION ALL

                    SELECT posting_date::date AS posting_date,
                           credit_account AS account_code,
                           currency_id,
                           COALESCE(module_code, '') AS module_code,
                           COALESCE(organization_id::text, '') AS organization_id,
                           COALESCE(employee_id::text, '') AS employee_id,
                           -COALESCE(amount_kgs, 0) AS amount_in_national_currency,
                           -COALESCE(amount_currency, 0) AS amount_in_currency
                    FROM doc_postings
                    WHERE is_active = true
                      AND posting_date::date <= @calculationDate
                      AND COALESCE(NULLIF(currency_id, ''), '') <> ''
                ) movements
                WHERE COALESCE(NULLIF(account_code, ''), '') <> ''
                ORDER BY account_code, currency_id, posting_date;";

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
                    movements.Add(new CurrencyMovement(
                        DateTime.SpecifyKind(Convert.ToDateTime(reader["posting_date"], CultureInfo.InvariantCulture).Date, DateTimeKind.Utc),
                        reader["account_code"]?.ToString() ?? string.Empty,
                        reader["currency_id"]?.ToString() ?? string.Empty,
                        NormalizeModuleName(reader["module_code"]?.ToString()),
                        reader["organization_id"]?.ToString() ?? string.Empty,
                        reader["employee_id"]?.ToString() ?? string.Empty,
                        ReadDecimal(reader["amount_in_national_currency"]),
                        ReadDecimal(reader["amount_in_currency"])));
                }
            }
            finally
            {
                if (closeConnection)
                    await _context.Database.CloseConnectionAsync();
            }

            return movements;
        }

        private static List<CurrencyBalance> BuildCurrencyBalances(
            IReadOnlyList<CurrencyMovement> movements,
            IReadOnlyList<ExchangeRateDifferenceRule> configuredRules,
            IReadOnlySet<string> currencyAccountCodes,
            List<string> warnings)
        {
            if (configuredRules.Count > 0)
                return BuildRuleBalances(movements, configuredRules);

            if (currencyAccountCodes.Count == 0)
            {
                warnings.Add("В плане счетов не найдены счета с признаком связи с валютами. Расчет выполнен по всем проводкам, где заполнена валюта.");
            }

            return movements
                .Where(movement => currencyAccountCodes.Count == 0 || currencyAccountCodes.Contains(movement.AccountCode))
                .GroupBy(movement => new { movement.AccountCode, movement.CurrencyId })
                .Select(group => new CurrencyBalance(
                    group.Key.AccountCode,
                    null,
                    group.Key.CurrencyId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    group.Sum(item => item.AmountInNationalCurrency),
                    group.Sum(item => item.AmountInCurrency),
                    null,
                    group.ToList()))
                .Where(balance => Math.Abs(balance.AmountInCurrency) >= 0.005m)
                .OrderBy(balance => balance.AccountCode)
                .ThenBy(balance => balance.CurrencyId)
                .ToList();
        }

        private static List<CurrencyBalance> BuildRuleBalances(
            IReadOnlyList<CurrencyMovement> movements,
            IReadOnlyList<ExchangeRateDifferenceRule> configuredRules)
        {
            var balances = new List<CurrencyBalance>();

            foreach (var rule in configuredRules)
            {
                var matchingMovements = movements
                    .Where(movement => RuleMatchesMovement(rule, movement))
                    .ToList();

                var groupedMovements = matchingMovements.GroupBy(movement => new
                {
                    rule.AccountCode,
                    rule.PairedAccountCode,
                    movement.CurrencyId,
                    ModuleName = string.IsNullOrWhiteSpace(rule.ModuleName) ? string.Empty : movement.ModuleName,
                    OrganizationId = rule.DetailMode == CalculationDetailMode.Organization ? movement.OrganizationId : string.Empty,
                    EmployeeId = rule.DetailMode == CalculationDetailMode.Employee ? movement.EmployeeId : string.Empty
                });

                foreach (var group in groupedMovements)
                {
                    var amountInCurrency = group.Sum(item => item.AmountInCurrency);
                    if (Math.Abs(amountInCurrency) < 0.005m)
                        continue;

                    balances.Add(new CurrencyBalance(
                        group.Key.AccountCode,
                        group.Key.PairedAccountCode,
                        group.Key.CurrencyId,
                        group.Key.ModuleName,
                        group.Key.OrganizationId,
                        group.Key.EmployeeId,
                        group.Sum(item => item.AmountInNationalCurrency),
                        amountInCurrency,
                        rule,
                        group.ToList()));
                }
            }

            return balances
                .OrderBy(balance => balance.AccountCode)
                .ThenBy(balance => balance.PairedAccountCode)
                .ThenBy(balance => balance.CurrencyId)
                .ToList();
        }

        private async Task<List<CalculatedPosting>> BuildBalancePostingsAsync(
            CurrencyBalance balance,
            Guid currencyId,
            DateTime calculationDate,
            List<string> warnings,
            IReadOnlyDictionary<string, AccountBalanceKind> accountKinds)
        {
            var currencyRate = await _metadataService.GetCurrencyRateForDateAsync(currencyId, calculationDate);
            if (currencyRate == null)
            {
                warnings.Add($"Не найден курс валюты {currencyId} на {calculationDate:dd.MM.yyyy} или более раннюю дату.");
                return new List<CalculatedPosting>();
            }

            var revaluedAmount = RoundMoney(balance.AmountInCurrency * currencyRate.Rate);
            var difference = RoundMoney(revaluedAmount - balance.AmountInNationalCurrency);
            if (Math.Abs(difference) < 0.01m)
                return new List<CalculatedPosting>();

            return
            [
                CreateCalculatedPosting(
                    balance,
                    calculationDate,
                    difference,
                    currencyId,
                    balance.AmountInCurrency,
                    currencyRate.Rate,
                    null,
                    warnings,
                    accountKinds)
            ];
        }

        private async Task<List<CalculatedPosting>> BuildMovementAwarePostingsAsync(
            CurrencyBalance balance,
            Guid currencyId,
            DateTime periodStart,
            DateTime periodEnd,
            List<string> warnings,
            IReadOnlyDictionary<string, AccountBalanceKind> accountKinds)
        {
            var result = new List<CalculatedPosting>();
            var accumulatedDifference = 0m;
            var periodMovements = balance.Movements
                .Where(movement => movement.PostingDate.Date >= periodStart.Date && movement.PostingDate.Date <= periodEnd.Date)
                .Where(movement => Math.Abs(movement.AmountInCurrency) >= 0.005m)
                .GroupBy(movement => movement.PostingDate.Date)
                .OrderBy(group => group.Key)
                .ToList();

            foreach (var movementGroup in periodMovements)
            {
                var currencyRate = await _metadataService.GetCurrencyRateForDateAsync(currencyId, movementGroup.Key);
                if (currencyRate == null)
                {
                    warnings.Add($"Не найден курс валюты {currencyId} на {movementGroup.Key:dd.MM.yyyy} или более раннюю дату.");
                    continue;
                }

                var amountInCurrency = movementGroup.Sum(item => item.AmountInCurrency);
                var amountInNationalCurrency = movementGroup.Sum(item => item.AmountInNationalCurrency);
                var movementDifference = RoundMoney(amountInCurrency * currencyRate.Rate - amountInNationalCurrency);
                accumulatedDifference += movementDifference;
                if (Math.Abs(movementDifference) < 0.01m)
                    continue;

                result.Add(CreateCalculatedPosting(
                    balance,
                    movementGroup.Key,
                    movementDifference,
                    currencyId,
                    amountInCurrency,
                    currencyRate.Rate,
                    $"оборот {movementGroup.Key:dd.MM.yyyy}",
                    warnings,
                    accountKinds));
            }

            var periodEndRate = await _metadataService.GetCurrencyRateForDateAsync(currencyId, periodEnd);
            if (periodEndRate == null)
            {
                warnings.Add($"Не найден курс валюты {currencyId} на {periodEnd:dd.MM.yyyy} или более раннюю дату.");
                return result;
            }

            var finalDifference = RoundMoney(balance.AmountInCurrency * periodEndRate.Rate - (balance.AmountInNationalCurrency + accumulatedDifference));
            if (Math.Abs(finalDifference) >= 0.01m)
            {
                result.Add(CreateCalculatedPosting(
                    balance,
                    periodEnd,
                    finalDifference,
                    currencyId,
                    balance.AmountInCurrency,
                    periodEndRate.Rate,
                    "остаток периода",
                    warnings,
                    accountKinds));
            }

            return result;
        }

        private static CalculatedPosting CreateCalculatedPosting(
            CurrencyBalance balance,
            DateTime postingDate,
            decimal difference,
            Guid currencyId,
            decimal amountInCurrency,
            decimal exchangeRate,
            string? sourceDescription,
            List<string> warnings,
            IReadOnlyDictionary<string, AccountBalanceKind> accountKinds)
        {
            var rule = balance.Rule;
            var amount = Math.Abs(difference);
            var isGain = difference > 0;

            string debitAccount;
            string creditAccount;
            int? debitReportLine = null;
            int? creditReportLine = null;

            if (isGain)
            {
                debitAccount = ResolvePositiveDifferenceAccount(balance, accountKinds);
                creditAccount = ResolveConfiguredAccount(
                    rule?.GainAccountCode,
                    ExchangeGainAccountCode,
                    balance.AccountCode,
                    "дохода",
                    warnings);
                debitReportLine = rule?.DebitReportLine;
            }
            else
            {
                debitAccount = ResolveConfiguredAccount(
                    rule?.LossAccountCode,
                    ExchangeLossAccountCode,
                    balance.AccountCode,
                    "расхода",
                    warnings);
                creditAccount = ResolveBalanceAccount(balance);
                creditReportLine = rule?.CreditReportLine;
            }

            return new CalculatedPosting(
                postingDate,
                debitAccount,
                creditAccount,
                amount,
                currencyId,
                amountInCurrency,
                exchangeRate,
                isGain,
                debitReportLine,
                creditReportLine,
                balance.ModuleName,
                balance.OrganizationId,
                balance.EmployeeId,
                sourceDescription,
                balance.AccountCode,
                balance.PairedAccountCode);
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
                if (!IsActiveRule(row))
                    continue;

                var accountCode = ExtractAccountCode(
                    GetRowString(row, "account_id", "Основной счет", "Счет"),
                    accountCodeById);
                if (string.IsNullOrWhiteSpace(accountCode))
                    continue;

                var currencyId = ResolveCurrencyId(
                    GetRowString(row, "currency_id", "Валюта"),
                    currencyReferences);

                var pairedAccountCode = ExtractAccountCode(
                    GetRowString(row, "paired_account_id", "Парный счет"),
                    accountCodeById);

                var gainAccountCode = ExtractAccountCode(
                    GetRowString(row, "gain_account_id", "Счет дохода"),
                    accountCodeById);

                var lossAccountCode = ExtractAccountCode(
                    GetRowString(row, "loss_account_id", "Счет расхода"),
                    accountCodeById);

                var detailMode = ResolveCalculationDetailMode(ReadInt(row, "calculation_detail_mode", "expense_direction", "Разрез расчета", "Признак расхода"));
                var moduleName = NormalizeModuleName(GetRowString(row, "module_code", "Модуль"));
                var calculationAlgorithm = ReadInt(row, "calculation_algorithm", "Алгоритм расчета");
                var debitReportLine = ReadNullableInt(row, "debit_report_line", "Строка отчета по дебету");
                var creditReportLine = ReadNullableInt(row, "credit_report_line", "Строка отчета по кредиту");
                var calculationMethod = GetRowString(row, "calculation_method", "calc_method", "Способ расчета");

                rules.Add(new ExchangeRateDifferenceRule(
                    accountCode,
                    string.IsNullOrWhiteSpace(pairedAccountCode) ? null : pairedAccountCode,
                    currencyId,
                    string.IsNullOrWhiteSpace(gainAccountCode) ? null : gainAccountCode,
                    string.IsNullOrWhiteSpace(lossAccountCode) ? null : lossAccountCode,
                    detailMode,
                    moduleName,
                    calculationAlgorithm,
                    debitReportLine,
                    creditReportLine,
                    calculationMethod));
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

        private async Task<Dictionary<string, AccountBalanceKind>> LoadAccountKindsAsync()
        {
            var rows = await _metadataService.GetChartOfAccountsSelectionDataAsync();
            var result = new Dictionary<string, AccountBalanceKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var code = row.GetValueOrDefault("Код")?.ToString() ??
                           row.GetValueOrDefault("code")?.ToString() ??
                           string.Empty;
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var accountType = row.GetValueOrDefault("Тип счета")?.ToString() ??
                                  row.GetValueOrDefault("account_type")?.ToString();
                result[code.Trim()] = ResolveAccountKind(accountType);
            }

            return result;
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
            string documentNumber,
            CalculatedPosting posting)
        {
            const string sql = @"
                INSERT INTO doc_postings
                    (""Id"", posting_date, doc_number, document_type, module_code,
                     debit_account, credit_account, amount_kgs, amount_currency, currency_id, exchange_rate,
                     debit_report_line, credit_report_line, description, is_active, ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @postingDate, @documentNumber, @documentType, @moduleName,
                     @debitAccount, @creditAccount, @amount, 0, @currencyId, @exchangeRate,
                     @debitReportLine, @creditReportLine, @description, true, NOW(), NOW());";

            var description = BuildPostingDescription(posting);

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@postingDate", posting.PostingDate),
                new NpgsqlParameter("@documentNumber", documentNumber),
                new NpgsqlParameter("@documentType", CalculationDocumentType),
                new NpgsqlParameter("@moduleName", string.IsNullOrWhiteSpace(posting.ModuleName) ? FinanceModuleName : posting.ModuleName),
                new NpgsqlParameter("@debitAccount", posting.DebitAccount),
                new NpgsqlParameter("@creditAccount", posting.CreditAccount),
                new NpgsqlParameter("@amount", posting.Amount),
                new NpgsqlParameter("@currencyId", posting.CurrencyId.ToString()),
                new NpgsqlParameter("@exchangeRate", posting.ExchangeRate),
                new NpgsqlParameter("@debitReportLine", (object?)posting.DebitReportLine ?? DBNull.Value),
                new NpgsqlParameter("@creditReportLine", (object?)posting.CreditReportLine ?? DBNull.Value),
                new NpgsqlParameter("@description", description));
        }

        private static string BuildPostingDescription(CalculatedPosting posting)
        {
            var parts = new List<string>
            {
                $"Автоматический расчет курсовой разницы на {posting.PostingDate:dd.MM.yyyy}",
                $"счет {posting.SourceAccountCode}",
                $"валютный остаток {posting.AmountInCurrency.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"))}",
                $"курс {posting.ExchangeRate.ToString("N4", CultureInfo.GetCultureInfo("ru-RU"))}"
            };

            if (!string.IsNullOrWhiteSpace(posting.PairedAccountCode))
                parts.Add($"парный счет {posting.PairedAccountCode}");
            if (!string.IsNullOrWhiteSpace(posting.SourceDescription))
                parts.Add(posting.SourceDescription);

            return string.Join("; ", parts) + ".";
        }

        private static string BuildDocumentNumber(DateTime calculationDate) =>
            $"Курсовая разница {calculationDate:yyyyMMdd}";

        private static bool RuleMatchesMovement(ExchangeRateDifferenceRule rule, CurrencyMovement movement)
        {
            var accountMatches =
                rule.AccountCode.Equals(movement.AccountCode, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(rule.PairedAccountCode) &&
                 rule.PairedAccountCode.Equals(movement.AccountCode, StringComparison.OrdinalIgnoreCase));
            if (!accountMatches)
                return false;

            if (rule.CurrencyId.HasValue &&
                !rule.CurrencyId.Value.ToString().Equals(movement.CurrencyId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ModuleMatches(rule.ModuleName, movement.ModuleName);
        }

        private static bool ModuleMatches(string? ruleModuleName, string? movementModuleName)
        {
            var normalizedRuleModule = NormalizeModuleName(ruleModuleName);
            if (string.IsNullOrWhiteSpace(normalizedRuleModule))
                return true;

            var normalizedMovementModule = NormalizeModuleName(movementModuleName);
            return string.IsNullOrWhiteSpace(normalizedMovementModule) ||
                   normalizedRuleModule.Equals(normalizedMovementModule, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePositiveDifferenceAccount(
            CurrencyBalance balance,
            IReadOnlyDictionary<string, AccountBalanceKind> accountKinds)
        {
            var accountKind = accountKinds.TryGetValue(balance.AccountCode, out var kind)
                ? kind
                : AccountBalanceKind.ActivePassive;

            if (accountKind == AccountBalanceKind.Active)
                return balance.AccountCode;

            return string.IsNullOrWhiteSpace(balance.PairedAccountCode)
                ? balance.AccountCode
                : balance.PairedAccountCode;
        }

        private static string ResolveBalanceAccount(CurrencyBalance balance) =>
            string.IsNullOrWhiteSpace(balance.PairedAccountCode)
                ? balance.AccountCode
                : balance.PairedAccountCode;

        private static string ResolveConfiguredAccount(
            string? configuredAccountCode,
            string fallbackAccountCode,
            string balanceAccountCode,
            string accountPurpose,
            List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(configuredAccountCode))
                return configuredAccountCode;

            warnings.Add(
                $"Для валютного счета {balanceAccountCode} не настроен счет {accountPurpose}; используется счет {fallbackAccountCode}.");
            return fallbackAccountCode;
        }

        private static AccountBalanceKind ResolveAccountKind(string? rawValue)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return AccountBalanceKind.ActivePassive;

            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Актив", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("Пассив", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Active", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("Passive", StringComparison.OrdinalIgnoreCase))
            {
                return AccountBalanceKind.Active;
            }

            if (value.Equals("2", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Пассив", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("Актив", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Passive", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("Active", StringComparison.OrdinalIgnoreCase))
            {
                return AccountBalanceKind.Passive;
            }

            return AccountBalanceKind.ActivePassive;
        }

        private static CalculationDetailMode ResolveCalculationDetailMode(int rawValue)
        {
            return rawValue switch
            {
                2 => CalculationDetailMode.Organization,
                3 => CalculationDetailMode.Employee,
                _ => CalculationDetailMode.None
            };
        }

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

        private static string NormalizeModuleName(string? value) =>
            ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName(value);

        private static decimal RoundMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static bool IsActiveRule(IReadOnlyDictionary<string, object> row)
        {
            var value = row.GetValueOrDefault("is_active") ?? row.GetValueOrDefault("Активен");
            return value == null || value is DBNull || ReadBool(value);
        }

        private static string GetRowString(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static int ReadInt(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value is DBNull)
                    continue;

                if (value is int intValue)
                    return intValue;
                if (value is long longValue)
                    return Convert.ToInt32(longValue, CultureInfo.InvariantCulture);
                if (value is decimal decimalValue)
                    return Convert.ToInt32(decimalValue, CultureInfo.InvariantCulture);
                if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return 0;
        }

        private static int? ReadNullableInt(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            var value = ReadInt(row, keys);
            return value == 0 ? null : value;
        }

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

        private enum AccountBalanceKind
        {
            Active,
            Passive,
            ActivePassive
        }

        private enum CalculationDetailMode
        {
            None,
            Organization,
            Employee
        }

        private sealed record CurrencyMovement(
            DateTime PostingDate,
            string AccountCode,
            string CurrencyId,
            string ModuleName,
            string OrganizationId,
            string EmployeeId,
            decimal AmountInNationalCurrency,
            decimal AmountInCurrency);

        private sealed record CurrencyBalance(
            string AccountCode,
            string? PairedAccountCode,
            string CurrencyId,
            string ModuleName,
            string OrganizationId,
            string EmployeeId,
            decimal AmountInNationalCurrency,
            decimal AmountInCurrency,
            ExchangeRateDifferenceRule? Rule,
            IReadOnlyList<CurrencyMovement> Movements);

        private sealed record CurrencyReference(
            string Code,
            string Name,
            bool IsBaseCurrency);

        private sealed record ExchangeRateDifferenceRule(
            string AccountCode,
            string? PairedAccountCode,
            Guid? CurrencyId,
            string? GainAccountCode,
            string? LossAccountCode,
            CalculationDetailMode DetailMode,
            string ModuleName,
            int CalculationAlgorithm,
            int? DebitReportLine,
            int? CreditReportLine,
            string CalculationMethod);

        private sealed record CalculatedPosting(
            DateTime PostingDate,
            string DebitAccount,
            string CreditAccount,
            decimal Amount,
            Guid CurrencyId,
            decimal AmountInCurrency,
            decimal ExchangeRate,
            bool IsGain,
            int? DebitReportLine,
            int? CreditReportLine,
            string ModuleName,
            string OrganizationId,
            string EmployeeId,
            string? SourceDescription,
            string SourceAccountCode,
            string? PairedAccountCode);
    }
}
