using System.Data;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

namespace BIS.ERP.Testing;

public sealed class OperationImportService
{
    public async Task<IReadOnlyCollection<SmokeTestOperation>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Не указан файл операций.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл операций не найден.", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".json" => await LoadJsonAsync(path, cancellationToken),
            ".xlsx" or ".xlsm" => LoadExcel(path),
            _ => throw new InvalidOperationException("Поддерживаются только JSON и Excel (*.xlsx, *.xlsm).")
        };
    }

    private static async Task<IReadOnlyCollection<SmokeTestOperation>> LoadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var operations = await JsonSerializer.DeserializeAsync<List<SmokeTestOperation>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        return Normalize(operations ?? new List<SmokeTestOperation>());
    }

    private static IReadOnlyCollection<SmokeTestOperation> LoadExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("В Excel-файле нет листов.");

        var usedRange = worksheet.RangeUsed()
            ?? throw new InvalidOperationException("Excel-файл операций пустой.");

        var headers = usedRange.FirstRow()
            .Cells()
            .Select((cell, index) => new { Index = index + 1, Name = cell.GetString().Trim() })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        var result = new List<SmokeTestOperation>();
        foreach (var row in usedRange.RowsUsed().Skip(1))
        {
            var operation = new SmokeTestOperation
            {
                DocumentKind = GetString(row, headers, "DocumentKind", "ВидДокумента", "ТипДокумента", "DocumentType", "Kind", "Вид"),
                Name = GetString(row, headers, "Name", "Наименование", "Товар", "Операция"),
                Quantity = GetDecimal(row, headers, 1m, "Quantity", "Количество"),
                AmountWithoutTax = GetDecimal(row, headers, 1000m, "AmountWithoutTax", "СуммаБезНалогов", "Amount", "Сумма"),
                VatRate = GetDecimal(row, headers, 12m, "VatRate", "СтавкаНДС"),
                SalesTaxRate = GetDecimal(row, headers, 1.5m, "SalesTaxRate", "СтавкаНалогаСПродаж"),
                CounterpartyAccountCode = GetString(row, headers, "CounterpartyAccountCode", "СчетКонтрагента", "РасчетныйСчет"),
                LineAccountCode = GetString(row, headers, "LineAccountCode", "СчетСтроки", "СчетУчета"),
                PaymentKind = GetString(row, headers, "PaymentKind", "ВидОплаты"),
                DeliveryKind = GetString(row, headers, "DeliveryKind", "ВидПоставки"),
                SupplyKind = GetString(row, headers, "SupplyKind", "ТипПоставки"),
                Basis = GetString(row, headers, "Basis", "Основание"),
                TaxBlankNumber = GetString(row, headers, "TaxBlankNumber", "НомерБланка"),
                ModuleCode = GetString(row, headers, "ModuleCode", "Модуль")
            };

            if (string.IsNullOrWhiteSpace(operation.DocumentKind) &&
                string.IsNullOrWhiteSpace(operation.Name) &&
                operation.AmountWithoutTax == 1000m)
            {
                continue;
            }

            result.Add(operation);
        }

        return Normalize(result);
    }

    private static IReadOnlyCollection<SmokeTestOperation> Normalize(IReadOnlyCollection<SmokeTestOperation> operations)
    {
        return operations
            .Select((item, index) => new SmokeTestOperation
            {
                DocumentKind = NormalizeDocumentKind(item.DocumentKind),
                Name = string.IsNullOrWhiteSpace(item.Name) ? $"Операция {index + 1}" : item.Name.Trim(),
                Quantity = item.Quantity <= 0 ? 1m : item.Quantity,
                AmountWithoutTax = item.AmountWithoutTax <= 0 ? 1000m : item.AmountWithoutTax,
                VatRate = item.VatRate < 0 ? 0m : item.VatRate,
                SalesTaxRate = item.SalesTaxRate < 0 ? 0m : item.SalesTaxRate,
                CounterpartyAccountCode = item.CounterpartyAccountCode?.Trim() ?? string.Empty,
                LineAccountCode = item.LineAccountCode?.Trim() ?? string.Empty,
                PaymentKind = string.IsNullOrWhiteSpace(item.PaymentKind) ? "TRANSFER" : item.PaymentKind.Trim(),
                DeliveryKind = string.IsNullOrWhiteSpace(item.DeliveryKind) ? "GOODS" : item.DeliveryKind.Trim(),
                SupplyKind = string.IsNullOrWhiteSpace(item.SupplyKind) ? "TAXABLE" : item.SupplyKind.Trim(),
                Basis = item.Basis?.Trim() ?? string.Empty,
                TaxBlankNumber = item.TaxBlankNumber?.Trim() ?? string.Empty,
                ModuleCode = string.IsNullOrWhiteSpace(item.ModuleCode) ? "FIN" : item.ModuleCode.Trim()
            })
            .ToArray();
    }

    private static string NormalizeDocumentKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "purchase" or "регистрация" or "registration" => "Purchase",
            _ => "Sales"
        };
    }

    private static string GetString(IXLRangeRow row, IReadOnlyDictionary<string, int> headers, params string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var column))
            {
                var value = row.Cell(column).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return string.Empty;
    }

    private static decimal GetDecimal(IXLRangeRow row, IReadOnlyDictionary<string, int> headers, decimal fallback, params string[] names)
    {
        foreach (var name in names)
        {
            if (!headers.TryGetValue(name, out var column))
                continue;

            var cell = row.Cell(column);
            if (cell.TryGetValue<double>(out var dbl))
                return Convert.ToDecimal(dbl, CultureInfo.InvariantCulture);

            var text = cell.GetString().Trim();
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue))
                return decimalValue;
            if (decimal.TryParse(text, NumberStyles.Any, new CultureInfo("ru-RU"), out decimalValue))
                return decimalValue;
        }

        return fallback;
    }
}
