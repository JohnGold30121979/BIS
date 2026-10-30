using System.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using ClosedXML.Excel;


namespace BIS.ERP.Testing;

public static class AccountPairTurnoverChecks
{
    public static int Run(string outputDirectory, Action<string>? log = null)
    {
        Directory.CreateDirectory(outputDirectory);
        void Log(string message) => log?.Invoke(message);
        var start = new DateTime(2026, 7, 1);
        var end = new DateTime(2026, 7, 31);
        var org = Guid.NewGuid();
        var other = Guid.NewGuid();
        var pair = new TurnoverAccountPair(Guid.NewGuid(), "1", "Расчёты с заказчиками", "14200000", "32200000");
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
            Log("PASS " + message);
        }
        ReconciliationMovementRow Posting(DateTime date, string debit, string credit, decimal amount,
            string number = "1", Guid? organization = null) => new()
        {
            Date = date, DebitAccount = debit, CreditAccount = credit, Amount = amount, DocumentNumber = number,
            OrganizationId = organization ?? org, OrganizationCode = "2", OrganizationName = "Организация 2",
            Description = "Абонентское обслуживание", ModuleCode = "Finance"
        };
        DataRow Label(DataTable table, string text) => table.Rows.Cast<DataRow>().Single(row => (string)row["Дата"] == text);
        decimal Amount(DataRow row, string column)
        {
            var field = column == "Дебет" ? "debit_amount" : column == "Кредит" ? "credit_amount" : column;
            return row.IsNull(field) ? 0 : (decimal)row[field];
        }
        var movements = new[]
        {
            Posting(start.AddDays(-1), "14200000", "611000", 26700),
            Posting(start, "14200000", "611000", 15000, "151"),
            Posting(end.AddHours(23), "14200000", "611000", 12690, "159"),
            Posting(start.AddDays(1), "121000", "32200000", 26700, "220494"),
            Posting(start, "14200000", "32200000", 1000, "DIRECT"),
            Posting(start, "999", "888", 900, "IGNORE"),
            Posting(end.AddDays(1), "14200000", "611000", 999, "FUTURE")
        };
        var table = AccountPairTurnoverBuilder.Build(pair, movements, [], null, start, end);
        var rows = table.Rows.Cast<DataRow>().ToList();
        Check(rows.Count(row => (string)row["Документ"] == "DIRECT") == 2, "Прямая проводка — две стороны, две строки");
        Check(!rows.Any(row => (string)row["Документ"] is "IGNORE" or "FUTURE"), "Посторонние счета и следующий день исключены");
        Check(Amount(Label(table, "ОБОРОТЫ"), "Дебет") == 28690 && Amount(Label(table, "ОБОРОТЫ"), "Кредит") == 27700,
            "Обороты включают начало периода, весь последний день и внешние корреспонденции");
        Check(Amount(Label(table, "САЛЬДО НА НАЧАЛО"), "Дебет") == 26700 &&
              Amount(Label(table, "САЛЬДО НА КОНЕЦ"), "Дебет") == 27690, "Начальное и конечное сальдо");
        Check(rows.Where(row => !string.IsNullOrEmpty((string)row["Документ"])) .Sum(row => Amount(row, "Дебет")) ==
              Amount(Label(table, "ОБОРОТЫ"), "Дебет"), "Детализация равна итогам оборотов");
        var onlyOpening = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start.AddDays(-1), "611000", "32200000", 504)], [], null, start, end);
        Check(Amount(Label(onlyOpening, "САЛЬДО НА КОНЕЦ"), "Кредит") == 504, "Организация с кредитовым остатком без движений");
        var scoped = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start, "14200000", "611000", 100), Posting(start, "14200000", "611000", 900, organization: other)],
            [], org, start, end);
        Check(Amount(Label(scoped, "ОБОРОТЫ"), "Дебет") == 100, "Отбор конкретной организации");
        var same = pair with { SecondAccount = pair.FirstAccount };
        var sameTable = AccountPairTurnoverBuilder.Build(same, [Posting(start, "14200000", "611000", 100)], [], null, start, end);
        Check(sameTable.Rows.Cast<DataRow>().Count(row => (string)row["Документ"] == "1") == 1, "Одинаковые счета не дублируют сторону");
        var prefixTable = AccountPairTurnoverBuilder.Build(pair with { FirstAccount = "142" },
            [Posting(start, "14200000", "611000", 100)], [], null, start, end);
        Check(Amount(Label(prefixTable, "ОБОРОТЫ"), "Дебет") == 100, "Префикс включает субсчета");
        var opening = new AccountOpeningBalance { AccountCode = "14200000", BalanceDate = start.AddDays(-5), Debit = 100 };
        var baselineTable = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start.AddDays(-6), "14200000", "611000", 999), Posting(start.AddDays(-4), "14200000", "611000", 20)],
            [opening], null, start, end);
        Check(baselineTable.Rows.Cast<DataRow>().Where(row => (string)row["Дата"] == "САЛЬДО НА НАЧАЛО")
            .Sum(row => Amount(row, "Дебет")) == 120, "Снимок остатка исключает повторный учёт старой истории");
        var empty = AccountPairTurnoverBuilder.Build(pair, [], [], null, start, end);
        Check(empty.Rows.Count == 1 && ((string)empty.Rows[0]["Дата"]).StartsWith("Данных"), "Пустой отчёт содержит пояснение");
        var layout = AccountPairTurnoverLayout.Create("По паре счетов");
        using var stream = new MemoryStream(ClosedXmlReportRenderer.RenderToBytes(layout, table));
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheet(1);
        Check(sheet.Cell(4, 9).GetString() == "Модуль", "В Excel заголовок Модуль");
        Check(sheet.Cell(4, 5).GetString() == "ОБОРОТЫ — сом" && sheet.Cell(5, 5).GetString() == "Сумма Дт",
            "Двухуровневая шапка оборотов");
        Check(sheet.MergedRanges.Any(range => range.RangeAddress.FirstAddress.RowNumber == 6 && range.ColumnCount() == 9),
            "Заголовок организации объединён на ширину таблицы");
        Check(sheet.Cell(6, 1).GetString().StartsWith("Организация:"), "В теле нет служебных строк шапки");
        var actLayout = ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт сверки");
        Check(actLayout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
            .SelectMany(block => block.Table!.Columns).Any(column => column.Header == "Модуль"), "Предыдущий акт использует заголовок Модуль");
        var output = Path.Combine(outputDirectory, "account-pair-checks.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        book.SaveAs(output);
        var pdf = ClosedXmlReportRenderer.RenderToPdfBytes(layout, table);
        Check(pdf.Length > 1000 && System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF", "Печатный PDF сформирован");
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-checks.pdf"), pdf);
        Log($"PASS: {checks} проверок. Excel: {output}");
        
        var currencyRows = new[]
        {
            new ReconciliationMovementRow { Date = start.AddDays(-1), OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "14200000", CreditAccount = "611000", Amount = 9000, AmountCurrency = 100, CurrencyCode = "USD" },
            new ReconciliationMovementRow { Date = start, OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "611000", CreditAccount = "32200000", Amount = 1800, AmountCurrency = 20, CurrencyCode = "USD", DocumentNumber = "USD-1" },
            new ReconciliationMovementRow { Date = start, OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "14200000", CreditAccount = "611000", Amount = 1000, AmountCurrency = 10, CurrencyCode = "EUR", DocumentNumber = "EUR-1" }
        };
        var currencyTable = AccountPairTurnoverBuilder.Build(pair, currencyRows, [], null, start, end);
        var currencyClosing = currencyTable.Rows.Cast<DataRow>().Where(row => (string)row["Дата"] == "САЛЬДО НА КОНЕЦ").ToList();
        Check(currencyClosing.Count == 2 && currencyClosing.Any(row => Amount(row, "Сумма вал.") == 80) && currencyClosing.Any(row => Amount(row, "Сумма вал.") == 10), "USD и EUR не смешиваются; валютное начальное сальдо и кредит учтены");
        Check(currencyTable.Rows.Cast<DataRow>().Single(row => (string)row["Документ"] == "USD-1").Field<decimal>("Сумма вал.") == 20, "В движении отображается исходная сумма в валюте");
        Check(currencyTable.Columns["Дебет"]!.DataType == typeof(string) && currencyTable.Columns["Кредит"]!.DataType == typeof(string), "Дебет и Кредит — номера счетов, а не суммы");
        Check(layout.Sheets[0].Blocks.Single(block => block.Table != null).Table!.Columns.Count(column => column.Field == "Сумма Дт") == 1 && layout.Sheets[0].Blocks.Single(block => block.Table != null).Table!.Columns.Count(column => column.Field == "Сумма вал.") == 1, "В макете сохранена Сумма Дт и одна Сумма вал.");
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-currency-checks.xlsx"), ClosedXmlReportRenderer.RenderToBytes(layout, currencyTable));
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-currency-checks.pdf"), ClosedXmlReportRenderer.RenderToPdfBytes(layout, currencyTable));
        return checks;
    }
}
