// Пробник: проверяет разбор «строки с конца» в PDF-выводе (метод BuildTextCellValue).
// Именно он отвечает за итоговую фразу акта: в макете ячейка берёт значение из
// последней строки набора (TakeFromRow = -1), и до правки PDF печатал её пустой.
using System.Data;
using System.Reflection;
using System.Text;
using BIS.ERP.Models;
using BIS.ERP.Services;

Console.OutputEncoding = Encoding.UTF8;

var method = typeof(ClosedXmlReportRenderer).GetMethod(
    "BuildTextCellValue", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("BuildTextCellValue не найден");

var table = ReconciliationActBuilder.CreateTable();
var label = ClosedXmlLayoutTemplates.ActLabelField;

void Add(string operation)
{
    var row = table.NewRow();
    row[label] = operation;
    table.Rows.Add(row);
}

Add("АКТ СВЕРКИ между Основное предприятие и \"63676\"");
Add("Период: 01/01/2026 - 15/10/2026");
Add("САЛЬДО НА 15/10/2026");
Add("ИТОГО: 222 222,99");
Add("Задолженность ОАО ИСФ \"Бишкек Курулуш\" перед нами составляет 222 222,99 (двести двадцать две тысячи двести двадцать два сом 99 тыйын)");

string Resolve(int row, string field, string text = "")
{
    var cell = new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, TakeFromRow = row, TakeFromField = field, Text = text };
    return (string)method.Invoke(null, new object[] { cell, table, null })!;
}

var checks = new (string Name, string Value, string Expected)[]
{
    ("последняя строка (TakeFromRow = -1)", Resolve(-1, label), "Задолженность"),
    ("предпоследняя (-2)", Resolve(-2, label), "ИТОГО"),
    ("первая строка (1)", Resolve(1, label), "АКТ СВЕРКИ"),
    ("вне диапазона (99)", Resolve(99, label), string.Empty)
};

var failed = false;
foreach (var check in checks)
{
    var ok = check.Expected.Length == 0
        ? string.IsNullOrWhiteSpace(check.Value)
        : check.Value.StartsWith(check.Expected, StringComparison.Ordinal);
    if (!ok) failed = true;
    Console.WriteLine($"[{(ok ? "ок" : "ОШИБКА")}] {check.Name}: {check.Value}");
}

Console.WriteLine();
Console.WriteLine("Итоговая фраза из последней строки:");
Console.WriteLine("   " + Resolve(-1, label));

return failed ? 1 : 0;
