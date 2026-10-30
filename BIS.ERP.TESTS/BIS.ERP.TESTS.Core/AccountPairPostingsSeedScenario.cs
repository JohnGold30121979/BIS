using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Testing;

/// <summary>Явное создание сохраняемых тестовых проводок только в ОСОО2. Очистки нет.</summary>
public sealed class AccountPairPostingsSeedScenario : SmokeTestScenarioBase
{
    private const string Marker = "ТЕСТ ПАРЫ 14200000-32200000 ОСОО2";
    private const string TestCode = "999830";
    private static readonly DateTime PostingDate = new(2026, 10, 30);
    public override string Code => "account-pair-postings-seed";
    public override string Name => "ОСОО2: создать и оставить проводки по паре счетов";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Verify проверяет готовность ОСОО2. Run создаёт четыре помеченные проводки по паре 14200000–32200000 на 30.10.2026 и оставляет их в базе. Повторный запуск не дублирует записи.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
            return SmokeTestResult.Failure("Удаление отключено: тестовые проводки должны остаться в ОСОО2.");
        var details = new List<string>();
        try
        {
            var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
            var targets = candidates.Where(item => item.DatabaseName.StartsWith("bis_OsOO2_", StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.ConnectionString).Select(group => group.First()).ToList();
            if (targets.Count != 1)
                return SmokeTestResult.Failure($"Ожидалась ровно одна база ОСОО2, найдено {targets.Count}. Записей не создано.");
            var target = targets[0];
            await using var context = new AppDbContext(target.ConnectionString);
            var metadata = new MetadataService(context);
            var objects = await context.MetadataObjects.Include(item => item.Fields).ToListAsync(cancellationToken);
            MetadataObject Object(string table) => objects.Single(item => item.TableName == table);
            var postingObject = Object("doc_postings");
            var organizations = Object("catalog_organizations");
            var currencies = Object("catalog_currencies");
            var pairsObject = Object("catalog_advance_payments");
            var accounts = await AccountAnalyticsRegistry.LoadAsync(metadata);
            foreach (var actualPair in await AccountPairTurnoverBuilder.LoadPairsAsync(metadata))
                details.Add($"Доступная пара {actualPair.Code}: {actualPair.FirstAccount} - {actualPair.SecondAccount}, {actualPair.Name}.");
            details.Add("Счета 14*/32*: " + string.Join(", ", accounts.Accounts.Where(item => item.Code.StartsWith("14") || item.Code.StartsWith("32")).Select(item => item.Code)));
            var first = accounts.FindAccount("14200000") ?? throw new Exception("В плане счетов ОСОО2 нет 14200000.");
            var second = accounts.FindAccount("32200000") ?? throw new Exception("В плане счетов ОСОО2 нет 32200000.");
            var externalDebit = accounts.FindAccount("121000") ?? accounts.FindAccount("12100000")
                ?? accounts.Accounts.FirstOrDefault(item => item.Code != first.Code && item.Code != second.Code)
                ?? throw new Exception("Не найден третий счёт для внешней корреспонденции.");
            var externalCredit = accounts.FindAccount("611000") ?? accounts.FindAccount("61100000") ?? externalDebit;
            var currencyRows = await metadata.GetCatalogDataAsync(currencies.Id);
            var usd = currencyRows.FirstOrDefault(row => Text(row, "code", "Код").Equals("USD", StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("В справочнике валют ОСОО2 нет USD. Записей не создано.");
            var usdId = Guid.Parse(Text(usd, "Id"));
            var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(metadata);
            var pair = pairs.FirstOrDefault(item => item.FirstAccount == first.Code && item.SecondAccount == second.Code);
            details.Add($"База: {target.DatabaseName}; пара: {first.Code} - {second.Code}; {(pair == null ? "пару необходимо добавить пользователю" : $"код пары {pair.Code}, {pair.Name}")}.");
            details.Add($"Внешние счета: {externalDebit.Code}, {externalCredit.Code}; валюта USD; дата {PostingDate:dd.MM.yyyy}.");
            details.Add("Тестовый эквивалент: 90 сом за 1 USD, только для контрольного примера.");
            foreach (var field in postingObject.Fields.OrderBy(field => field.Order))
                details.Add($"Поле проводки: {field.Name} = {field.DbColumnName} ({field.FieldType}), обязательное: {field.IsRequired}.");
            if (options.Command == SmokeTestCommand.Verify)
            {
                var storedRows = await metadata.GetCatalogDataAsync(postingObject.Id);
                foreach (var row in storedRows)
                {
                    string Value(string column) => Text(row,
                        postingObject.Fields.FirstOrDefault(field => field.DbColumnName == column)?.Name ?? column, column);
                    if (!Value("doc_number").StartsWith("99983010300", StringComparison.Ordinal)) continue;
                    details.Add($"ЗАПИСЬ {Value("doc_number")}: тип «{Value("document_type") }», Дт {Value("debit_account")}, Кт {Value("credit_account")}, " +
                        $"сумма {Value("amount_kgs")} сом, валюта {Value("amount_currency")}; " +
                        $"document_table=«{Value("document_table")}», document_id=«{Value("document_id")}»; {Value("description")}.");
                }
                return SmokeTestResult.Success("ОСОО2: готовность и существующие тестовые проводки проверены. Verify ничего не записывает.", details.ToArray());
            }

            // Используем тот же путь, что диалог ручной проводки: проверки даты,
            // метаданных, номеров и штатный журнал событий остаются действующими.
            if (pair == null)
                throw new Exception("Сначала добавьте пару 14200000 - 32200000 кнопкой «Добавить» в бухгалтерском отчёте. Пара автоматически не создаётся.");
            var organizationRows = await metadata.GetCatalogDataAsync(organizations.Id);
            var organization = organizationRows.FirstOrDefault(row => Text(row, "name", "Наименование") == Marker);
            var organizationData = new Dictionary<string, object>
            {
                ["code"] = TestCode, ["name"] = Marker, ["full_name"] = Marker,
                ["is_active"] = true, ["is_primary"] = false
            };
            if (organization == null)
            {
                var countryField = organizations.Fields.FirstOrDefault(field => field.IsRequired &&
                    (field.Name == "Государство" || field.DbColumnName.Contains("country", StringComparison.OrdinalIgnoreCase)));
                if (countryField != null)
                {
                    var countryCatalog = objects.FirstOrDefault(item => item.Name == countryField.ReferenceCatalog)
                        ?? throw new Exception("Не найден справочник государств для тестовой организации.");
                    var countries = await metadata.GetCatalogDataAsync(countryCatalog.Id);
                    var country = countries.FirstOrDefault(row => row.Values.Any(value =>
                        value?.ToString()?.Contains("Кыргыз", StringComparison.OrdinalIgnoreCase) == true ||
                        value?.ToString()?.Contains("Киргиз", StringComparison.OrdinalIgnoreCase) == true));
                    if (country == null) throw new Exception("В справочнике государств не найден Кыргызстан.");
                    organizationData[countryField.DbColumnName] = Guid.Parse(Text(country, "Id"));
                }
            }
            var organizationId = organization == null
                ? await metadata.AddCatalogItemAsync(organizations.Id, Map(organizations, organizationData))
                : Guid.Parse(Text(organization, "Id"));
            var cases = new[]
            {
                (Number: "999830103001", Debit: first.Code, Credit: externalCredit.Code, Amount: 9000m, Currency: 100m, Text: "Начисление, USD"),
                (Number: "999830103002", Debit: externalDebit.Code, Credit: second.Code, Amount: 3600m, Currency: 40m, Text: "Оплата, USD"),
                (Number: "999830103003", Debit: first.Code, Credit: second.Code, Amount: 900m, Currency: 10m, Text: "Прямая корреспонденция пары, USD"),
                (Number: "999830103004", Debit: first.Code, Credit: externalCredit.Code, Amount: 1500m, Currency: 0m, Text: "Начисление, сом")
            };
            var existing = await metadata.GetCatalogDataAsync(postingObject.Id);
            foreach (var item in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = existing.FirstOrDefault(row => Text(row, "doc_number", "Номер документа", "Номер") == item.Number);
                if (previous != null)
                {
                    if (!Text(previous, "description", "Примечание", "Содержание", "Описание").StartsWith(Marker, StringComparison.Ordinal))
                        throw new Exception($"Номер {item.Number} уже занят другой записью. Она не изменена.");
                    details.Add($"{item.Number}: ранее созданная тестовая запись оставлена без изменений.");
                    continue;
                }
                var data = new Dictionary<string, object>
                {
                    ["posting_date"] = PostingDate, ["doc_number"] = item.Number,
                    ["document_type"] = "Ручная проводка", ["module_code"] = "Finance",
                    ["debit_account"] = item.Debit, ["credit_account"] = item.Credit,
                    ["amount_kgs"] = item.Amount, ["amount_currency"] = item.Currency,
                    ["organization_id"] = organizationId, ["description"] = Marker + ": " + item.Text,
                    ["is_active"] = true
                };
                if (item.Currency != 0) data["currency_id"] = usdId;
                var id = await metadata.CreateDynamicRecordAsync(postingObject.Id, Map(postingObject, data));
                details.Add($"Создана {item.Number}: Дт {item.Debit}, Кт {item.Credit}, {item.Amount:N2} сом, {item.Currency:N2} USD, Id {id}.");
            }
            // Проверяем именно два источника, которыми пользуются окна программы.
            var savedRows = await metadata.GetCatalogDataAsync(postingObject.Id);
            var journal = await new PostingService(context).GetAllPostingsAsync(PostingDate, PostingDate);
            foreach (var item in cases)
            {
                if (!savedRows.Any(row => Text(row, "doc_number", "Номер документа", "Номер") == item.Number))
                    throw new Exception($"Проводки: не видна запись {item.Number}.");
                var saved = journal.SingleOrDefault(row => row.DocumentNumber == item.Number)
                    ?? throw new Exception($"Журнал проводок: не видна запись {item.Number}.");
                if (saved.Amount != item.Amount || saved.AmountCurrency != item.Currency ||
                    saved.DebitAccount != item.Debit || saved.CreditAccount != item.Credit || !saved.IsActive)
                    throw new Exception($"Журнал проводок: поля записи {item.Number} отличаются от контрольных.");
            }
            details.Add($"Организация: код {TestCode}, {Marker}; Id {organizationId}.");
            details.Add("Все 4 записи видны источникам окон «Проводки» и «Журнал проводок». Записи оставлены в базе; удаления нет.");
            return SmokeTestResult.Success("ОСОО2: тестовые проводки созданы и сохранены.", details.ToArray());
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure("Создание тестовых проводок ОСОО2: ошибка. Уже созданные тестовые записи не удалялись.", details.Append(DescribeError(ex)));
        }
    }

    private static string Text(Dictionary<string, object> row, params string[] keys) =>
        keys.Select(key => row.GetValueOrDefault(key)?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static Dictionary<string, object> Map(MetadataObject metadata, Dictionary<string, object> values) =>
        values.Where(item => metadata.Fields.Any(field => field.DbColumnName == item.Key))
            .ToDictionary(item => metadata.Fields.First(field => field.DbColumnName == item.Key).Name, item => item.Value);
}
