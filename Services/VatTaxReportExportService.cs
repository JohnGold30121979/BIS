using BIS.ERP.Data;
using BIS.ERP.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed class VatTaxReportExportService
    {
        public const string OfficialTemplateCode = "STI-062_7";
        private readonly AppDbContext _context;

        public VatTaxReportExportService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<VatReportExportResult> ExportMonthlyVatReportAsync(
            DateTime startDate,
            DateTime endDate,
            string outputPath)
        {
            var entries = await new BalanceService(_context).GetPurchaseSalesJournalAsync(startDate, endDate);
            var organization = await LoadPrimaryOrganizationAsync();
            var data = BuildWorkbookData(startDate, endDate, organization, entries);
            var templateService = new RegulatedReportTemplateService(_context);
            var template = await templateService.GetActiveTemplateAsync(OfficialTemplateCode);

            if (template is { TemplateData.Length: > 0 })
            {
                var tempDirectory = CreateTemplateTempDirectory();
                try
                {
                    var tempTemplatePath = await WriteTemplateToTempFileAsync(template, tempDirectory);
                    await Task.Run(() => ExportUsingExcelTemplate(data, tempTemplatePath, outputPath));
                    return new VatReportExportResult(true, template.Code, template.Name, template.OriginalFileName);
                }
                finally
                {
                    DeleteDirectorySafe(tempDirectory);
                }
            }

            ExportFallbackWorkbook(data, outputPath);
            return new VatReportExportResult(false, OfficialTemplateCode, string.Empty, string.Empty);
        }

        private VatWorkbookData BuildWorkbookData(
            DateTime startDate,
            DateTime endDate,
            VatOrganizationInfo organization,
            IReadOnlyCollection<PurchaseSaleJournalEntry> entries)
        {
            var sales = entries
                .Where(entry => entry.IsPosted && entry.Section.Equals("Продажи", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Date)
                .ThenBy(entry => entry.DocumentNumber, StringComparer.OrdinalIgnoreCase)
                .Select(entry => ToRow(entry))
                .ToList();

            var purchases = entries
                .Where(entry => entry.IsPosted && entry.Section.Equals("Закупки", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Date)
                .ThenBy(entry => entry.DocumentNumber, StringComparer.OrdinalIgnoreCase)
                .Select(entry => ToRow(entry))
                .ToList();

            return new VatWorkbookData(
                startDate.Date,
                endDate.Date,
                organization,
                sales,
                purchases);
        }

        private static VatJournalRow ToRow(PurchaseSaleJournalEntry entry)
        {
            var description = string.IsNullOrWhiteSpace(entry.Note)
                ? entry.DocumentType
                : $"{entry.DocumentType}: {entry.Note}";
            var vatAmount = entry.VatAmount != 0 ? entry.VatAmount : entry.TaxAmount;
            var totalTax = vatAmount + entry.SalesTaxAmount;
            var amountWithoutTax = entry.AmountWithoutTax != 0
                ? entry.AmountWithoutTax
                : entry.Amount - totalTax;
            var totalAmount = entry.Amount != 0
                ? entry.Amount
                : amountWithoutTax + totalTax;

            return new VatJournalRow(
                entry.Date,
                entry.Organization,
                description,
                entry.DocumentNumber,
                amountWithoutTax,
                entry.SalesTaxAmount,
                vatAmount,
                totalAmount,
                entry.TaxType,
                entry.Note);
        }

        private async Task<VatOrganizationInfo> LoadPrimaryOrganizationAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            if (catalog == null)
                return VatOrganizationInfo.Empty;

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $@"
                SELECT *
                FROM ""{catalog.TableName}""
                ORDER BY COALESCE(""is_primary"", false) DESC, ""code"", ""CreatedAt""
                LIMIT 1";

            var opened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    opened = true;
                }

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return VatOrganizationInfo.Empty;

                string GetString(string column)
                {
                    try
                    {
                        var ordinal = reader.GetOrdinal(column);
                        return reader.IsDBNull(ordinal)
                            ? string.Empty
                            : reader.GetValue(ordinal)?.ToString() ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }

                return new VatOrganizationInfo(
                    GetString("name"),
                    GetString("full_name"),
                    GetString("inn"),
                    GetString("okpo"),
                    !string.IsNullOrWhiteSpace(GetString("legal_address"))
                        ? GetString("legal_address")
                        : GetString("actual_address"),
                    GetString("director"),
                    GetString("chief_accountant"));
            }
            finally
            {
                if (opened)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private static void ExportUsingExcelTemplate(
            VatWorkbookData data,
            string templatePath,
            string outputPath)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Шаблон налогового отчета не найден.", templatePath);

            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                throw new InvalidOperationException(
                    "Для выгрузки в официальный шаблон Excel требуется установленный Microsoft Excel.");
            }

            object? excel = null;
            object? workbooks = null;
            object? workbook = null;

            try
            {
                excel = Activator.CreateInstance(excelType);
                dynamic excelApp = excel!;
                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;
                workbooks = excelApp.Workbooks;
                workbook = ((dynamic)workbooks).Open(templatePath);

                FillSummarySheet(workbook, data);
                FillRegisterSheet(workbook, data.SalesRows, SheetKind.Sales);
                FillRegisterSheet(workbook, data.PurchaseRows, SheetKind.Purchases);

                excelApp.CalculateFull();
                ((dynamic)workbook).SaveAs(outputPath);
                ((dynamic)workbook).Close(false);
                excelApp.Quit();
            }
            finally
            {
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
                ReleaseComObject(excel);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private static string CreateTemplateTempDirectory()
        {
            var baseDirectory = Path.Combine(Path.GetTempPath(), "BIS.ERP", "RegulatedReportTemplates");
            Directory.CreateDirectory(baseDirectory);
            CleanupOldTemplateTempDirectories(baseDirectory);

            var tempDirectory = Path.Combine(baseDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private static void CleanupOldTemplateTempDirectories(string baseDirectory)
        {
            try
            {
                foreach (var directory in Directory.GetDirectories(baseDirectory))
                {
                    try
                    {
                        var info = new DirectoryInfo(directory);
                        if (info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-2))
                            info.Delete(true);
                    }
                    catch
                    {
                        // Оставляем старый каталог, если ОС еще держит файлы.
                    }
                }
            }
            catch
            {
                // Не мешаем основному сценарию экспорта из-за проблем очистки temp-каталога.
            }
        }

        private static async Task<string> WriteTemplateToTempFileAsync(
            RegulatedReportTemplate template,
            string tempDirectory)
        {
            var extension = string.IsNullOrWhiteSpace(template.FileExtension)
                ? Path.GetExtension(template.OriginalFileName)
                : template.FileExtension;
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".xls";

            var fileName = string.IsNullOrWhiteSpace(template.OriginalFileName)
                ? $"template{extension}"
                : Path.GetFileNameWithoutExtension(template.OriginalFileName) + extension;
            var tempPath = Path.Combine(tempDirectory, fileName);
            await File.WriteAllBytesAsync(tempPath, template.TemplateData);
            return tempPath;
        }

        private static void DeleteDirectorySafe(string? directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(directoryPath, true);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }
        }

        private static void FillSummarySheet(dynamic workbook, VatWorkbookData data)
        {
            var summarySheet = FindWorksheet(workbook, new[]
            {
                "sti-062", "062", "титул", "общ", "свод", "main", "лист1"
            });

            if (summarySheet == null)
                return;

            WriteValueNearLabel(summarySheet, new[] { "наименование", "налогоплательщик", "организация" },
                data.Organization.DisplayName);
            WriteValueNearLabel(summarySheet, new[] { "инн" }, data.Organization.Inn);
            WriteValueNearLabel(summarySheet, new[] { "окпо" }, data.Organization.Okpo);
            WriteValueNearLabel(summarySheet, new[] { "адрес" }, data.Organization.Address);
            WriteValueNearLabel(summarySheet, new[] { "руководитель" }, data.Organization.Director);
            WriteValueNearLabel(summarySheet, new[] { "главныйбухгалтер", "бухгалтер" }, data.Organization.ChiefAccountant);
            WriteValueNearLabel(summarySheet, new[] { "период", "отчетныйпериод" }, data.PeriodDisplay);
            WriteValueNearLabel(summarySheet, new[] { "месяц" }, data.StartDate.ToString("MMMM", new CultureInfo("ru-RU")));
            WriteValueNearLabel(summarySheet, new[] { "год" }, data.StartDate.Year.ToString(CultureInfo.InvariantCulture));
        }

        private static void FillRegisterSheet(dynamic workbook, IReadOnlyList<VatJournalRow> rows, SheetKind kind)
        {
            var sheet = FindWorksheet(workbook, kind == SheetKind.Sales
                ? new[] { "продаж", "sales", "реестрпродаж", "приложение7", "лист2" }
                : new[] { "закуп", "purchase", "реестрзакупок", "приложение8", "лист3" });

            var createdSheet = false;
            if (sheet == null)
            {
                sheet = ((dynamic)workbook).Worksheets.Add();
                sheet.Name = kind == SheetKind.Sales ? "Продажи" : "Закупки";
                CreateFallbackRegisterLayout(sheet, kind);
                createdSheet = true;
            }

            var header = DetectRegisterHeader(sheet);
            if (header == null)
            {
                CreateFallbackRegisterLayout(sheet, kind);
                header = DetectRegisterHeader(sheet)
                    ?? throw new InvalidOperationException("Не удалось подготовить структуру листа для реестра НДС.");
                createdSheet = true;
            }

            var startRow = header.HeaderRow + header.HeaderHeight;
            var lastUsedRow = GetLastUsedRow(sheet);
            var templateColumnCount = header.Columns.Count == 0 ? 1 : Enumerable.Max(header.Columns.Values);
            var lastUsedColumn = Math.Max(GetLastUsedColumn(sheet), templateColumnCount);
            int? footerRow = createdSheet ? null : FindFooterRow(sheet, startRow, lastUsedRow, lastUsedColumn);
            var clearToRow = footerRow.HasValue
                ? footerRow.Value - 1
                : Math.Max(lastUsedRow, startRow + Math.Max(rows.Count, 50));

            if (clearToRow >= startRow)
                ClearRange(sheet, startRow, clearToRow, 1, lastUsedColumn);

            for (var index = 0; index < rows.Count; index++)
            {
                var rowIndex = startRow + index;
                var row = rows[index];
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.Date, row.Date);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.Organization, row.Organization);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.Description, row.Description);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.DocumentNumber, row.DocumentNumber);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.AmountWithoutTax, row.AmountWithoutTax);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.ExemptAmount, 0m);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.FixedAmount, 0m);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.SalesTaxAmount, row.SalesTaxAmount);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.VatAmount, row.VatAmount);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.TotalAmount, row.TotalAmount);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.TaxType, row.TaxType);
                WriteRowValue(sheet, rowIndex, header.Columns, ColumnRole.Note, row.Note);
            }

            if (footerRow.HasValue)
                WriteFooterFormulas(sheet, footerRow.Value, header.Columns, startRow, startRow + rows.Count - 1);
        }

        private static RegisterHeaderInfo? DetectRegisterHeader(dynamic sheet)
        {
            var lastRow = Math.Max(GetLastUsedRow(sheet), 1);
            var lastColumn = Math.Max(GetLastUsedColumn(sheet), 1);
            var headerKeywords = new[]
            {
                "дата", "орган", "номер", "ндс", "всего", "сумма", "налог"
            };

            for (var row = 1; row <= Math.Min(lastRow, 60); row++)
            {
                var columns = new Dictionary<ColumnRole, int>();
                var headerHeight = 1;

                for (var column = 1; column <= Math.Min(lastColumn, 25); column++)
                {
                    var current = NormalizeHeader(GetCellText(sheet, row, column));
                    var next = row < lastRow ? NormalizeHeader(GetCellText(sheet, row + 1, column)) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(next) && headerKeywords.Any(keyword => next.Contains(keyword, StringComparison.Ordinal)))
                        headerHeight = 2;

                    var combined = $"{current} {next}".Trim();
                    if (TryDetectColumnRole(combined, out var role) && !columns.ContainsKey(role))
                        columns[role] = column;
                }

                var score = 0;
                if (columns.ContainsKey(ColumnRole.Date)) score++;
                if (columns.ContainsKey(ColumnRole.Organization) || columns.ContainsKey(ColumnRole.Description)) score++;
                if (columns.ContainsKey(ColumnRole.VatAmount) || columns.ContainsKey(ColumnRole.SalesTaxAmount)) score++;
                if (columns.ContainsKey(ColumnRole.TotalAmount) || columns.ContainsKey(ColumnRole.AmountWithoutTax)) score++;

                if (score >= 3)
                    return new RegisterHeaderInfo(row, headerHeight, columns);
            }

            return null;
        }

        private static bool TryDetectColumnRole(string normalizedHeader, out ColumnRole role)
        {
            role = ColumnRole.Note;
            if (string.IsNullOrWhiteSpace(normalizedHeader))
                return false;

            if (normalizedHeader.Contains("дата", StringComparison.Ordinal))
            {
                role = ColumnRole.Date;
                return true;
            }

            if (normalizedHeader.Contains("инн", StringComparison.Ordinal) ||
                normalizedHeader.Contains("орган", StringComparison.Ordinal) ||
                normalizedHeader.Contains("поставщик", StringComparison.Ordinal) ||
                normalizedHeader.Contains("покупатель", StringComparison.Ordinal) ||
                normalizedHeader.Contains("контрагент", StringComparison.Ordinal))
            {
                role = ColumnRole.Organization;
                return true;
            }

            if (normalizedHeader.Contains("опис", StringComparison.Ordinal) ||
                normalizedHeader.Contains("товар", StringComparison.Ordinal) ||
                normalizedHeader.Contains("мат", StringComparison.Ordinal) ||
                normalizedHeader.Contains("документ", StringComparison.Ordinal) ||
                normalizedHeader.Contains("содерж", StringComparison.Ordinal))
            {
                role = ColumnRole.Description;
                return true;
            }

            if (normalizedHeader.Contains("номер", StringComparison.Ordinal) ||
                normalizedHeader.Contains("сф", StringComparison.Ordinal) ||
                normalizedHeader.Contains("бланк", StringComparison.Ordinal))
            {
                role = ColumnRole.DocumentNumber;
                return true;
            }

            if (normalizedHeader.Contains("фикс", StringComparison.Ordinal))
            {
                role = ColumnRole.FixedAmount;
                return true;
            }

            if (normalizedHeader.Contains("освоб", StringComparison.Ordinal) ||
                normalizedHeader.Contains("необлага", StringComparison.Ordinal) ||
                normalizedHeader.Contains("безналог", StringComparison.Ordinal))
            {
                role = ColumnRole.ExemptAmount;
                return true;
            }

            if (normalizedHeader.Contains("безндс", StringComparison.Ordinal) ||
                normalizedHeader.Contains("безналог", StringComparison.Ordinal) ||
                normalizedHeader.Contains("облага", StringComparison.Ordinal))
            {
                role = ColumnRole.AmountWithoutTax;
                return true;
            }

            if (normalizedHeader.Contains("налогспродаж", StringComparison.Ordinal) ||
                normalizedHeader.Contains("налогспрод", StringComparison.Ordinal))
            {
                role = ColumnRole.SalesTaxAmount;
                return true;
            }

            if (normalizedHeader.Contains("ндс", StringComparison.Ordinal) ||
                normalizedHeader.Contains("vat", StringComparison.Ordinal))
            {
                role = ColumnRole.VatAmount;
                return true;
            }

            if (normalizedHeader.Contains("всего", StringComparison.Ordinal) ||
                normalizedHeader.Contains("итого", StringComparison.Ordinal) ||
                normalizedHeader.Equals("сумма", StringComparison.Ordinal))
            {
                role = ColumnRole.TotalAmount;
                return true;
            }

            if (normalizedHeader.Contains("ставканалога", StringComparison.Ordinal) ||
                normalizedHeader.Contains("видналога", StringComparison.Ordinal))
            {
                role = ColumnRole.TaxType;
                return true;
            }

            if (normalizedHeader.Contains("примеч", StringComparison.Ordinal) ||
                normalizedHeader.Contains("основан", StringComparison.Ordinal))
            {
                role = ColumnRole.Note;
                return true;
            }

            return false;
        }

        private static void CreateFallbackRegisterLayout(dynamic sheet, SheetKind kind)
        {
            sheet.Cells.Clear();
            sheet.Cells[1, 1].Value = kind == SheetKind.Sales
                ? "Реестр продаж по НДС"
                : "Реестр закупок по НДС";

            var headers = new[]
            {
                "Дата",
                "Организация",
                "Документ",
                "Номер",
                "Сумма без налога",
                "Налог с продаж",
                "НДС",
                "Всего",
                "Ставка налога",
                "Примечание"
            };

            for (var index = 0; index < headers.Length; index++)
                sheet.Cells[3, index + 1].Value = headers[index];
        }

        private static void ExportFallbackWorkbook(VatWorkbookData data, string outputPath)
        {
            using var workbook = new XLWorkbook();
            var summary = workbook.Worksheets.Add("Свод НДС");
            var sales = workbook.Worksheets.Add("Продажи");
            var purchases = workbook.Worksheets.Add("Закупки");

            FillFallbackRegister(sales, "Реестр продаж по НДС", data.SalesRows);
            FillFallbackRegister(purchases, "Реестр закупок по НДС", data.PurchaseRows);

            summary.Cell("A1").Value = "Экспорт данных в налоговый отчет по НДС";
            summary.Cell("A2").Value = "Организация";
            summary.Cell("B2").Value = data.Organization.DisplayName;
            summary.Cell("A3").Value = "ИНН";
            summary.Cell("B3").Value = data.Organization.Inn;
            summary.Cell("A4").Value = "ОКПО";
            summary.Cell("B4").Value = data.Organization.Okpo;
            summary.Cell("A5").Value = "Период";
            summary.Cell("B5").Value = data.PeriodDisplay;

            summary.Cell("A7").Value = "Продажи, документов";
            summary.Cell("B7").FormulaA1 = "COUNTA(Продажи!A:A)-1";
            summary.Cell("A8").Value = "Продажи, база";
            summary.Cell("B8").FormulaA1 = "SUM(Продажи!E:E)";
            summary.Cell("A9").Value = "Продажи, налог с продаж";
            summary.Cell("B9").FormulaA1 = "SUM(Продажи!F:F)";
            summary.Cell("A10").Value = "Продажи, НДС";
            summary.Cell("B10").FormulaA1 = "SUM(Продажи!G:G)";
            summary.Cell("A11").Value = "Продажи, всего";
            summary.Cell("B11").FormulaA1 = "SUM(Продажи!H:H)";

            summary.Cell("A13").Value = "Закупки, документов";
            summary.Cell("B13").FormulaA1 = "COUNTA(Закупки!A:A)-1";
            summary.Cell("A14").Value = "Закупки, база";
            summary.Cell("B14").FormulaA1 = "SUM(Закупки!E:E)";
            summary.Cell("A15").Value = "Закупки, налог с продаж";
            summary.Cell("B15").FormulaA1 = "SUM(Закупки!F:F)";
            summary.Cell("A16").Value = "Закупки, НДС";
            summary.Cell("B16").FormulaA1 = "SUM(Закупки!G:G)";
            summary.Cell("A17").Value = "Закупки, всего";
            summary.Cell("B17").FormulaA1 = "SUM(Закупки!H:H)";

            summary.Cell("A19").Value = "НДС к уплате";
            summary.Cell("B19").FormulaA1 = "B10-B16";

            summary.Columns().AdjustToContents();
            workbook.SaveAs(outputPath);
        }

        private static void FillFallbackRegister(IXLWorksheet worksheet, string title, IReadOnlyList<VatJournalRow> rows)
        {
            worksheet.Cell("A1").Value = title;
            var headers = new[]
            {
                "Дата",
                "Организация",
                "Документ",
                "Номер",
                "Сумма без налога",
                "Налог с продаж",
                "НДС",
                "Всего",
                "Ставка налога",
                "Примечание"
            };

            for (var index = 0; index < headers.Length; index++)
                worksheet.Cell(3, index + 1).Value = headers[index];

            for (var index = 0; index < rows.Count; index++)
            {
                var rowNumber = index + 4;
                var row = rows[index];
                worksheet.Cell(rowNumber, 1).Value = row.Date;
                worksheet.Cell(rowNumber, 2).Value = row.Organization;
                worksheet.Cell(rowNumber, 3).Value = row.Description;
                worksheet.Cell(rowNumber, 4).Value = row.DocumentNumber;
                worksheet.Cell(rowNumber, 5).Value = row.AmountWithoutTax;
                worksheet.Cell(rowNumber, 6).Value = row.SalesTaxAmount;
                worksheet.Cell(rowNumber, 7).Value = row.VatAmount;
                worksheet.Cell(rowNumber, 8).Value = row.TotalAmount;
                worksheet.Cell(rowNumber, 9).Value = row.TaxType;
                worksheet.Cell(rowNumber, 10).Value = row.Note;
            }

            worksheet.Columns().AdjustToContents();
        }

        private static dynamic? FindWorksheet(dynamic workbook, IEnumerable<string> keywords)
        {
            var normalizedKeywords = keywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => NormalizeHeader(keyword))
                .Where(keyword => keyword.Length > 0)
                .ToList();

            var sheetCount = Convert.ToInt32(workbook.Worksheets.Count, CultureInfo.InvariantCulture);
            for (var index = 1; index <= sheetCount; index++)
            {
                dynamic sheet = workbook.Worksheets.Item(index);
                var sheetName = NormalizeHeader(Convert.ToString(sheet.Name, CultureInfo.InvariantCulture) ?? string.Empty);
                if (normalizedKeywords.Any(keyword => sheetName.Contains(keyword, StringComparison.Ordinal)))
                    return sheet;

                var firstCells = ReadFirstCellsPreview(sheet);
                var normalizedCells = NormalizeHeader(firstCells);
                if (normalizedKeywords.Any(keyword => normalizedCells.Contains(keyword, StringComparison.Ordinal)))
                    return sheet;
            }

            return null;
        }

        private static string ReadFirstCellsPreview(dynamic sheet)
        {
            var values = new List<string>();
            var maxRow = Math.Min(GetLastUsedRow(sheet), 8);
            var maxColumn = Math.Min(GetLastUsedColumn(sheet), 6);
            for (var row = 1; row <= maxRow; row++)
            {
                for (var column = 1; column <= maxColumn; column++)
                {
                    var text = GetCellText(sheet, row, column);
                    if (!string.IsNullOrWhiteSpace(text))
                        values.Add(text);
                }
            }

            return string.Join(" ", values);
        }

        private static void WriteValueNearLabel(dynamic sheet, IEnumerable<string> labelKeywords, object? value)
        {
            var normalizedKeywords = labelKeywords
                .Select(NormalizeHeader)
                .Where(keyword => keyword.Length > 0)
                .ToList();
            if (normalizedKeywords.Count == 0)
                return;

            var maxRow = Math.Min(GetLastUsedRow(sheet), 60);
            var maxColumn = Math.Min(GetLastUsedColumn(sheet), 20);
            for (var row = 1; row <= maxRow; row++)
            {
                for (var column = 1; column <= maxColumn; column++)
                {
                    var text = NormalizeHeader(GetCellText(sheet, row, column));
                    if (!normalizedKeywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)))
                        continue;

                    sheet.Cells[row, Math.Min(column + 1, maxColumn + 1)].Value = value ?? string.Empty;
                    return;
                }
            }
        }

        private static void WriteRowValue(
            dynamic sheet,
            int rowIndex,
            IReadOnlyDictionary<ColumnRole, int> columns,
            ColumnRole role,
            object? value)
        {
            if (!columns.TryGetValue(role, out var columnIndex))
                return;

            sheet.Cells[rowIndex, columnIndex].Value = value switch
            {
                DateTime date => date,
                decimal number => number,
                _ => value ?? string.Empty
            };
        }

        private static int? FindFooterRow(dynamic sheet, int startRow, int lastRow, int lastColumn)
        {
            for (var row = startRow; row <= Math.Min(lastRow, startRow + 500); row++)
            {
                var rowText = string.Join(" ", Enumerable.Range(1, Math.Min(lastColumn, 10))
                    .Select(column => NormalizeHeader(GetCellText(sheet, row, column))));
                if (rowText.Contains("итого", StringComparison.Ordinal) ||
                    rowText.Contains("всего", StringComparison.Ordinal))
                {
                    return row;
                }
            }

            return null;
        }

        private static void WriteFooterFormulas(
            dynamic sheet,
            int footerRow,
            IReadOnlyDictionary<ColumnRole, int> columns,
            int startRow,
            int endRow)
        {
            if (endRow < startRow)
                return;

            foreach (var role in new[]
                     {
                         ColumnRole.AmountWithoutTax,
                         ColumnRole.FixedAmount,
                         ColumnRole.ExemptAmount,
                         ColumnRole.SalesTaxAmount,
                         ColumnRole.VatAmount,
                         ColumnRole.TotalAmount
                     })
            {
                if (!columns.TryGetValue(role, out var columnIndex))
                    continue;

                var columnName = ToExcelColumnName(columnIndex);
                sheet.Cells[footerRow, columnIndex].Formula = $"=SUM({columnName}{startRow}:{columnName}{endRow})";
            }
        }

        private static int GetLastUsedRow(dynamic sheet)
        {
            try
            {
                return Math.Max(1, Convert.ToInt32(sheet.UsedRange.Rows.Count, CultureInfo.InvariantCulture));
            }
            catch
            {
                return 1;
            }
        }

        private static int GetLastUsedColumn(dynamic sheet)
        {
            try
            {
                return Math.Max(1, Convert.ToInt32(sheet.UsedRange.Columns.Count, CultureInfo.InvariantCulture));
            }
            catch
            {
                return 1;
            }
        }

        private static string GetCellText(dynamic sheet, int row, int column)
        {
            try
            {
                var value = sheet.Cells[row, column].Value2;
                return value?.ToString()?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ClearRange(dynamic sheet, int startRow, int endRow, int startColumn, int endColumn)
        {
            if (endRow < startRow || endColumn < startColumn)
                return;

            var startCell = $"{ToExcelColumnName(startColumn)}{startRow}";
            var endCell = $"{ToExcelColumnName(endColumn)}{endRow}";
            sheet.Range($"{startCell}:{endCell}").ClearContents();
        }

        private static string NormalizeHeader(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), "[^a-zа-яё0-9]+", string.Empty);
        }

        private static string ToExcelColumnName(int columnNumber)
        {
            var column = columnNumber;
            var name = string.Empty;
            while (column > 0)
            {
                column--;
                name = (char)('A' + column % 26) + name;
                column /= 26;
            }

            return name;
        }

        private static void ReleaseComObject(object? value)
        {
            try
            {
                if (value != null && Marshal.IsComObject(value))
                    Marshal.FinalReleaseComObject(value);
            }
            catch
            {
                // Игнорируем ошибки освобождения COM-объектов.
            }
        }

        private sealed record VatWorkbookData(
            DateTime StartDate,
            DateTime EndDate,
            VatOrganizationInfo Organization,
            IReadOnlyList<VatJournalRow> SalesRows,
            IReadOnlyList<VatJournalRow> PurchaseRows)
        {
            public string PeriodDisplay => $"{StartDate:dd.MM.yyyy} - {EndDate:dd.MM.yyyy}";
        }

        private sealed record VatOrganizationInfo(
            string DisplayName,
            string FullName,
            string Inn,
            string Okpo,
            string Address,
            string Director,
            string ChiefAccountant)
        {
            public static VatOrganizationInfo Empty { get; } = new(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private sealed record VatJournalRow(
            DateTime Date,
            string Organization,
            string Description,
            string DocumentNumber,
            decimal AmountWithoutTax,
            decimal SalesTaxAmount,
            decimal VatAmount,
            decimal TotalAmount,
            string TaxType,
            string Note);

        private sealed record RegisterHeaderInfo(
            int HeaderRow,
            int HeaderHeight,
            Dictionary<ColumnRole, int> Columns);

        public sealed record VatReportExportResult(
            bool UsedOfficialTemplate,
            string TemplateCode,
            string TemplateName,
            string TemplateFileName);

        private enum SheetKind
        {
            Sales,
            Purchases
        }

        private enum ColumnRole
        {
            Date,
            Organization,
            Description,
            DocumentNumber,
            FixedAmount,
            ExemptAmount,
            AmountWithoutTax,
            SalesTaxAmount,
            VatAmount,
            TotalAmount,
            TaxType,
            Note
        }
    }
}
