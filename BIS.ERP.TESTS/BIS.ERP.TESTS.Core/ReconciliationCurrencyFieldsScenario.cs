using System.Data;
using BIS.ERP.Services;
using BIS.ERP.Models;
using ClosedXML.Excel;

namespace BIS.ERP.Testing;

/// <summary>Проверка сквозной доступности валютных полей: набор → макет → вывод.</summary>
public sealed class ReconciliationCurrencyFieldsScenario : SmokeTestScenarioBase
{
    public override string Code => "reconciliation-currency-fields";
    public override string Name => "Акт и ведомость по паре: валютные поля";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Проверяет наличие номеров счетов, суммы и суммы в валюте в таблицах и макетах обоих отчётов. Отсутствующие поля считаются ошибкой, а не успешной пустой проверкой.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();
        var pair = new TurnoverAccountPair(Guid.NewGuid(), "1", "Контроль", "142", "322");
        var start = new DateTime(2026, 7, 1);
        var pairTable = AccountPairTurnoverBuilder.Build(pair, [], [], null, start, start);
        Audit("Ведомость по паре счетов", pairTable, AccountPairTurnoverLayout.Create("Пара"));
        Audit("Акт сверки", ReconciliationActBuilder.CreateTable(), ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт"));
        var balances = new OrganizationBalanceRow
        {
            AccountCode = "142", CounterAccountCode = "322", OrganizationName = "Контрагент", UsesCurrency = true,
            OpeningDebit = 9000, OpeningDebitCurrency = 100, TurnoverCredit = 1800, TurnoverCreditCurrency = 20,
            ClosingDebit = 7200, ClosingDebitCurrency = 80
        };
        var movement = new ReconciliationMovementRow
        {
            Date = start.AddDays(1), DebitAccount = "611", CreditAccount = "322", Amount = 1800,
            AmountCurrency = 20, CurrencyCode = "USD", DocumentNumber = "USD-TEST", ModuleCode = "Finance"
        };
        var act = new ReconciliationActBuilder(null!).BuildTable([balances], [], [movement], start, start.AddDays(30), true, "Контрагент").Table;
        var labelField = ClosedXmlLayoutTemplates.ActLabelField;
        void Expect(bool condition, string message) { if (!condition) errors.Add(message); else details.Add("PASS " + message); }
        Expect(act.Rows.Cast<DataRow>().Single(row => (string)row["N докум"] == "USD-TEST").Field<decimal>("Сумма вал.") == 20,
            "Акт: валютная сумма движения передаётся в таблицу");
        Expect(act.Rows.Cast<DataRow>().Single(row => (string)row[labelField] == $"САЛЬДО НА {start.AddDays(30):dd/MM/yyyy}").Field<decimal>("Сумма вал.") == 80,
            "Акт: конечное валютное сальдо передаётся в таблицу");
        var output = Path.Combine(TestEnvironment.GetLogsDirectory(), "account-pair-turnover");
        Directory.CreateDirectory(output);
        var actLayout = ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт валютный");
        var bytes = ClosedXmlReportRenderer.RenderToBytes(actLayout, act);
        using (var stream = new MemoryStream(bytes))
        using (var book = new XLWorkbook(stream))
        {
            var sheet = book.Worksheet(1);
            var detail = sheet.RowsUsed().Single(row => row.Cell(3).GetString() == "USD-TEST");
            Expect(detail.Cell(7).GetValue<decimal>() == 1800 && detail.Cell(8).GetValue<decimal>() == 20,
                "Акт Excel: национальная сумма Кт и сумма в валюте находятся в разных графах");
        }
        File.WriteAllBytes(Path.Combine(output, "reconciliation-currency-checks.xlsx"), bytes);
        File.WriteAllBytes(Path.Combine(output, "reconciliation-currency-checks.pdf"), ClosedXmlReportRenderer.RenderToPdfBytes(actLayout, act));
        return Task.FromResult(errors.Count == 0
            ? SmokeTestResult.Success("Валютные поля обоих отчётов доступны в наборе и макете.", details.ToArray())
            : SmokeTestResult.Failure("Валютные поля: обнаружены пропуски отображения.", errors.Concat(details)));

        void Audit(string name, DataTable data, BIS.ERP.Models.ClosedXmlReportLayout layout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
                .SelectMany(block => block.Table!.Columns).Select(column => column.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            details.Add($"{name}: поля набора — {string.Join(", ", data.Columns.Cast<DataColumn>().Select(column => column.ColumnName))}.");
            foreach (var field in new[] { "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал." })
            {
                if (!data.Columns.Contains(field)) errors.Add($"{name}: в наборе нет поля «{field}».");
                if (!fields.Contains(field)) errors.Add($"{name}: в макете нет поля «{field}».");
            }
            foreach (var field in fields.Where(field => !data.Columns.Contains(field)))
                errors.Add($"{name}: макет ссылается на отсутствующее поле «{field}».");
            var columns = layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
                .SelectMany(block => block.Table!.Columns).ToList();
            foreach (var field in new[] { "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал." })
                if (columns.Count(column => column.Field == field) != 1)
                    errors.Add($"{name}: поле «{field}» должно присутствовать в макете один раз.");
            Report(progress, $"{name}: проверены валютные поля набора и макета.");
        }
    }
}
