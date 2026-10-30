using System.Data;
using System.Globalization;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services;

public sealed record TurnoverAccountPair(Guid Id, string Code, string Name, string FirstAccount, string SecondAccount);

/// <summary>Движения каждого счёта пары, с остатками и отдельными блоками организаций.</summary>
public static class AccountPairTurnoverBuilder
{
    public const string ReportCode = "standard.finance.account-pair-turnover-closedxml";
    public const string ReportType = "AccountPairTurnover";
    public const string ReportName = "Оборотная ведомость по паре счетов (ClosedXML)";

    public static async Task<List<TurnoverAccountPair>> LoadPairsAsync(MetadataService metadata)
    {
        var registry = await AccountAnalyticsRegistry.LoadAsync(metadata);
        var result = new List<TurnoverAccountPair>();
        foreach (var row in await metadata.GetAdvancePaymentPairsAsync())
        {
            string Text(params string[] keys) => keys.Select(key => row.GetValueOrDefault(key)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
            var active = Text("is_active", "Активен");
            if (active.Equals("false", StringComparison.OrdinalIgnoreCase) || active == "0" || active == "Нет")
                continue;
            var first = registry.FindAccount(Text("debit_account", "Дебет"))?.Code;
            var second = registry.FindAccount(Text("credit_account", "Кредит"))?.Code;
            if (!Guid.TryParse(Text("Id"), out var id) || first == null || second == null)
                continue;
            result.Add(new(id, Text("code", "Код"), Text("name", "Вид расчета"), first, second));
        }
        return result.OrderBy(pair => pair.Code, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<DataTable> LoadAsync(AppDbContext context, TurnoverAccountPair pair,
        Guid? organizationId, DateTime start, DateTime end)
    {
        var movements = await LoadMovementsAsync(context, pair, organizationId, end);
        var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
        var openings = await context.AccountOpeningBalances.AsNoTracking()
            .Where(row => row.BalanceDate <= utcStart).ToListAsync();
        return Build(pair, movements, openings, organizationId, start, end);
    }

    private static async Task<List<ReconciliationMovementRow>> LoadMovementsAsync(AppDbContext context,
        TurnoverAccountPair pair, Guid? organizationId, DateTime end)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.posting_date, p.doc_number, p.debit_account, p.credit_account,
                       COALESCE(p.amount_kgs, 0) AS amount,
                       COALESCE(p.amount_currency, 0) AS amount_currency,
                       COALESCE(c.code, '') AS currency_code,
                       COALESCE(p.description, '') AS description,
                       COALESCE(p.document_type, '') AS document_type,
                       COALESCE(p.module_code, '') AS module,
                       p.organization_id::text AS organization_id,
                       COALESCE(NULLIF(o.name, ''), 'Без организации') AS organization_name,
                       COALESCE(o.code, '') AS organization_code
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON o."Id"::text = p.organization_id::text
                LEFT JOIN catalog_currencies c ON c."Id"::text = p.currency_id::text
                WHERE COALESCE(p.is_active, true) = true AND p.posting_date < @end
                  AND (@organization = '' OR p.organization_id::text = @organization)
                  AND (left(p.debit_account, length(@first)) = @first
                    OR left(p.credit_account, length(@first)) = @first
                    OR left(p.debit_account, length(@second)) = @second
                    OR left(p.credit_account, length(@second)) = @second)
                """;
            void Param(string name, object value)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
            }
            Param("end", DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc));
            Param("organization", organizationId?.ToString() ?? "");
            Param("first", pair.FirstAccount.Trim()); Param("second", pair.SecondAccount.Trim());
            var result = new List<ReconciliationMovementRow>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new ReconciliationMovementRow
                {
                    Date = reader.GetDateTime(reader.GetOrdinal("posting_date")),
                    DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(reader["doc_number"]?.ToString()),
                    DebitAccount = reader["debit_account"]?.ToString() ?? "",
                    CreditAccount = reader["credit_account"]?.ToString() ?? "",
                    Amount = Convert.ToDecimal(reader["amount"], CultureInfo.InvariantCulture),
                    AmountCurrency = Convert.ToDecimal(reader["amount_currency"], CultureInfo.InvariantCulture),
                    CurrencyCode = reader["currency_code"].ToString() ?? "",
                    Description = reader["description"].ToString() ?? "",
                    DocumentType = reader["document_type"].ToString() ?? "",
                    ModuleCode = reader["module"].ToString() ?? "",
                    OrganizationId = Guid.TryParse(reader["organization_id"]?.ToString(), out var id) ? id : null,
                    OrganizationName = reader["organization_name"].ToString() ?? "",
                    OrganizationCode = reader["organization_code"].ToString() ?? ""
                });
            return result;
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    public static DataTable Build(TurnoverAccountPair pair, IReadOnlyList<ReconciliationMovementRow> movements,
        IReadOnlyList<AccountOpeningBalance> openings, Guid? organizationId, DateTime start, DateTime end)
    {
        if (end.Date < start.Date)
            throw new ArgumentException("Дата окончания периода не может быть раньше даты начала.");
        if (string.IsNullOrWhiteSpace(pair.FirstAccount) || string.IsNullOrWhiteSpace(pair.SecondAccount))
            throw new ArgumentException("В паре должны быть заполнены оба счёта.");
        var table = new DataTable(ReportName);
        foreach (var name in new[] { "Дата", "Документ", "Дебет", "Кредит" })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("Сумма Дт", typeof(decimal));
        table.Columns.Add("Сумма Кт", typeof(decimal));
        table.Columns.Add("Сумма вал.", typeof(decimal));
        table.Columns.Add("debit_amount", typeof(decimal));
        table.Columns.Add("credit_amount", typeof(decimal));
        table.Columns.Add("Содержание", typeof(string));
        table.Columns.Add("Модуль", typeof(string));
        table.Columns.Add("report_title", typeof(string));
        table.Columns.Add("period_text", typeof(string));

        bool Matches(string code) => code.Trim().StartsWith(pair.FirstAccount.Trim(), StringComparison.OrdinalIgnoreCase)
            || code.Trim().StartsWith(pair.SecondAccount.Trim(), StringComparison.OrdinalIgnoreCase);
        var baselines = openings.Where(row => Matches(row.AccountCode) && row.BalanceDate.Date <= start.Date)
            .GroupBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.BalanceDate).First(),
                StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        Bucket Get(Guid? id, string name, string code, string currency = "")
        {
            var key = (id?.ToString() ?? name) + "|" + currency;
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new Bucket(name, code, currency);
            return bucket;
        }
        // Введённый остаток имеет только счёт, без организации: не приписываем его контрагенту.
        if (!organizationId.HasValue)
            foreach (var opening in baselines.Values.Where(row => row.Debit != 0 || row.Credit != 0))
                Get(null, "Без организации", "").Opening += opening.Debit - opening.Credit;

        foreach (var movement in movements.Where(row => row.Date.Date <= end.Date &&
                     (!organizationId.HasValue || row.OrganizationId == organizationId)))
        {
            AddSide(movement.DebitAccount, movement.CreditAccount, true);
            AddSide(movement.CreditAccount, movement.DebitAccount, false);
            void AddSide(string account, string counter, bool debit)
            {
                if (!Matches(account)) return;
                if (movement.Date.Date < start.Date && baselines.TryGetValue(account.Trim(), out var baseline)
                    && movement.Date.Date < baseline.BalanceDate.Date) return;
                var bucket = Get(movement.OrganizationId, movement.OrganizationName, movement.OrganizationCode, movement.CurrencyCode);
                if (movement.Date.Date < start.Date)
                {
                    bucket.Opening += debit ? movement.Amount : -movement.Amount;
                    bucket.OpeningCurrency += debit ? movement.AmountCurrency : -movement.AmountCurrency;
                }
                else
                    bucket.Lines.Add((movement, account, counter, debit));
            }
        }
        foreach (var bucket in buckets.Values.Where(value => value.Opening != 0 || value.OpeningCurrency != 0 || value.Lines.Count > 0)
                     .OrderBy(value => long.TryParse(value.Code, out var number) ? number : long.MaxValue)
                     .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Add($"Организация: {bucket.Code} {bucket.Name} {bucket.Currency}".Trim());
            foreach (var line in bucket.Lines.OrderBy(line => line.Movement.Date)
                         .ThenBy(line => line.Movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase))
                Add(line.Movement.Date.ToString("dd.MM.yy"), line.Movement.DocumentNumber, line.Movement.DebitAccount, line.Movement.CreditAccount,
                    line.Debit ? line.Movement.Amount : null, line.Debit ? null : line.Movement.Amount,
                    string.IsNullOrWhiteSpace(line.Movement.Description) ? line.Movement.DocumentType : line.Movement.Description,
                    line.Movement.ModuleCode, line.Movement.AmountCurrency);
            var debit = bucket.Lines.Where(line => line.Debit).Sum(line => line.Movement.Amount);
            var credit = bucket.Lines.Where(line => !line.Debit).Sum(line => line.Movement.Amount);
            var currencyDebit = bucket.Lines.Where(line => line.Debit).Sum(line => line.Movement.AmountCurrency);
            var currencyCredit = bucket.Lines.Where(line => !line.Debit).Sum(line => line.Movement.AmountCurrency);
            Add($"ИТОГО ПО ОРГАНИЗАЦИИ {bucket.Code}", debit: debit, credit: credit, currency: currencyDebit - currencyCredit);
            Add("САЛЬДО НА НАЧАЛО", debit: Math.Max(bucket.Opening, 0), credit: Math.Max(-bucket.Opening, 0), currency: bucket.OpeningCurrency);
            Add("ОБОРОТЫ", debit: debit, credit: credit, currency: currencyDebit - currencyCredit);
            var closing = bucket.Opening + debit - credit;
            Add("САЛЬДО НА КОНЕЦ", debit: Math.Max(closing, 0), credit: Math.Max(-closing, 0), currency: bucket.OpeningCurrency + currencyDebit - currencyCredit);
            Add("");
        }
        if (table.Rows.Count == 0) Add("Данных по выбранной паре счетов и организации за период нет.");
        return table;

        void Add(string label, string document = "", string account = "", string counter = "",
            decimal? debit = null, decimal? credit = null, string description = "", string module = "", decimal? currency = null)
        {
            table.Rows.Add(label, document, account, counter,
                (object?)debit ?? DBNull.Value, (object?)credit ?? DBNull.Value,
                (object?)currency ?? DBNull.Value, (object?)debit ?? DBNull.Value, (object?)credit ?? DBNull.Value, description, module,
                $"Оборотная ведомость синтетического и аналитического учета\nпо паре счетов {pair.FirstAccount} - {pair.SecondAccount} ({pair.Name}) — сом / валюта",
                $"с {start:dd.MM.yyyy} по {end:dd.MM.yyyy}");
        }
    }

    private sealed class Bucket(string name, string code, string currency)
    {
        public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "Без организации" : name;
        public string Code { get; } = code;
        public string Currency { get; } = currency;
        public decimal OpeningCurrency { get; set; }
        public decimal Opening { get; set; }
        public List<(ReconciliationMovementRow Movement, string Account, string Counter, bool Debit)> Lines { get; } = new();
    }
}
