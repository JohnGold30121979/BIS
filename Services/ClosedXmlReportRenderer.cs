using BIS.ERP.Models;
using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Интерпретатор JSON-разметки <see cref="ClosedXmlReportLayout"/> в книгу ClosedXML.
    /// Чистый рендерер: не ходит в БД и не зависит от UI.
    /// Поддерживает блоки: TitleRows, FreeRows, TabularSection (группы, подытоги SUM,
    /// общий итог), SignaturesRow, PageBreak.
    /// </summary>
    public static class ClosedXmlReportRenderer
    {
        private static readonly Regex ParameterRegex = new(@"\{param\.([^}]+)\}", RegexOptions.Compiled);

        public static string SerializeLayout(ClosedXmlReportLayout layout) =>
            JsonSerializer.Serialize(layout, new JsonSerializerOptions
            {
                WriteIndented = true,
                // Без этого китайские/русские названия блоков уходят в JSON как \u041f\u0443...
                // и разметку невозможно читать глазами.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

        /// <summary>Разбирает JSON разметки. При ошибке возвращает null и текст ошибки.</summary>
        public static ClosedXmlReportLayout? ParseLayout(string? json, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Разметка ClosedXML-макета пуста.";
                return null;
            }

            ClosedXmlReportLayout? layout;
            try
            {
                layout = JsonSerializer.Deserialize<ClosedXmlReportLayout>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                error = $"Некорректный JSON разметки: {ex.Message}";
                return null;
            }

            if (layout == null || layout.Sheets.Count == 0)
            {
                error = "Разметка не содержит ни одного листа.";
                return null;
            }

            foreach (var sheet in layout.Sheets.Where(s => s != null))
            {
                foreach (var block in sheet.Blocks.Where(b => b != null))
                {
                    if (block.Type == "TabularSection" &&
                        (block.Table == null || block.Table.Columns.Count == 0 ||
                         block.Table.Columns.All(c => string.IsNullOrWhiteSpace(c.Field))))
                    {
                        error = $"Блок «{block.Name}»: таблица не содержит колонок с полями.";
                        return null;
                    }
                }
            }

            return layout;
        }

        /// <summary>
        /// Готовит данные для рендера. После <c>ReportService.SelectFields</c> колонки DataTable
        /// переименованы в DisplayName, а в разметке поле обычно указано именем источника.
        /// Добавляем колонки-двойники, чтобы разметка работала и по имени поля, и по его подписи.
        /// </summary>
        public static DataTable PrepareData(DataTable source, Report report)
        {
            var result = source.Copy();
            if (report?.Fields == null)
                return result;

            foreach (var field in report.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.FieldName) || result.Columns.Contains(field.FieldName))
                    continue;

                var displayName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldName : field.DisplayName;
                if (!result.Columns.Contains(displayName))
                    continue;

                var clone = result.Columns.Add(field.FieldName, result.Columns[displayName].DataType);
                foreach (DataRow row in result.Rows)
                    row[clone] = row[displayName];
            }

            return result;
        }

        public static byte[] RenderToBytes(
            ClosedXmlReportLayout layout,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            if (layout.Sheets.Count == 0)
                throw new InvalidOperationException("ClosedXML-макет не содержит листов.");

            using var workbook = new XLWorkbook();
            foreach (var sheetLayout in layout.Sheets)
            {
                var worksheet = workbook.Worksheets.Add(BuildSafeSheetName(sheetLayout.Name));
                RenderSheet(worksheet, sheetLayout, dataTable, parameters);
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public static void RenderToFile(
            string filePath,
            ClosedXmlReportLayout layout,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            var bytes = RenderToBytes(layout, dataTable, parameters);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(filePath, bytes);
        }

        private static void RenderSheet(
            IXLWorksheet worksheet,
            ClosedXmlSheet sheetLayout,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var setup = sheetLayout.PageSetup ?? new ClosedXmlPageSetup();
            worksheet.ShowGridLines = setup.ShowGridLines;
            worksheet.Style.Font.FontName = string.IsNullOrWhiteSpace(setup.FontName) ? "Times New Roman" : setup.FontName;
            worksheet.Style.Font.FontSize = setup.FontSize > 0 ? setup.FontSize : 10;
            worksheet.PageSetup.PageOrientation = string.Equals(setup.Orientation, "Portrait", StringComparison.OrdinalIgnoreCase)
                ? XLPageOrientation.Portrait
                : XLPageOrientation.Landscape;
            if (setup.FitToPagesWide > 0)
                worksheet.PageSetup.FitToPages(setup.FitToPagesWide, 0);
            if (setup.FreezeRows > 0)
                worksheet.SheetView.FreezeRows(setup.FreezeRows);

            var rowCursor = 1;
            foreach (var block in sheetLayout.Blocks)
            {
                rowCursor = block.Type switch
                {
                    "TitleRows" or "FreeRows" => WriteTextRows(worksheet, rowCursor, block, dataTable, parameters),
                    "TabularSection" => WriteTableSection(worksheet, rowCursor, block, dataTable, parameters),
                    "SignaturesRow" => WriteSignatures(worksheet, rowCursor, block),
                    "PageBreak" => WritePageBreak(worksheet, rowCursor),
                    _ => rowCursor
                };
            }
        }

        private static int WriteTextRows(
            IXLWorksheet worksheet,
            int startRow,
            ClosedXmlBlock block,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var rowCursor = startRow;
            foreach (var row in block.Rows)
            {
                var rowIndex = row.Row > 0 ? startRow + row.Row - 1 : rowCursor;
                foreach (var cellLayout in row.Cells)
                {
                    var column = Math.Max(1, cellLayout.Column);
                    var cell = worksheet.Cell(rowIndex, column);

                    // Значение можно взять прямо из набора (например заголовок и период акта).
                    var picked = PickFieldValue(dataTable, cellLayout.TakeFromRow, cellLayout.TakeFromField);
                    if (picked == null)
                    {
                        cell.Value = ResolveText(cellLayout.Text, parameters);
                    }
                    else if (picked is DateTime pickedDate)
                    {
                        cell.Value = pickedDate;
                    }
                    else if (picked is decimal or double or float or int or long or short or byte)
                    {
                        cell.Value = Convert.ToDecimal(picked, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        cell.Value = picked.ToString() ?? string.Empty;
                    }

                    ApplyCellStyle(cell, cellLayout.Bold, cellLayout.Italic, cellLayout.FontSize,
                        cellLayout.Align, cellLayout.Fill, cellLayout.Border);
                    if (cellLayout.MergeToColumn > column)
                        worksheet.Range(rowIndex, column, rowIndex, cellLayout.MergeToColumn).Merge();
                }

                rowCursor = Math.Max(rowCursor, rowIndex + 1);
            }

            return rowCursor;
        }

        /// <summary>
        /// Значение ячейки набора. <paramref name="rowNumber"/> — номер строки 1-based,
        /// отрицательное значение считается с конца (-1 — последняя строка).
        /// </summary>
        private static object? PickFieldValue(DataTable? dataTable, int rowNumber, string? field)
        {
            if (dataTable == null || rowNumber == 0 || string.IsNullOrWhiteSpace(field))
                return null;
            if (!dataTable.Columns.Contains(field))
                return null;

            var index = rowNumber > 0 ? rowNumber - 1 : dataTable.Rows.Count + rowNumber;
            if (index < 0 || index >= dataTable.Rows.Count)
                return null;

            var value = dataTable.Rows[index][field];
            return value == DBNull.Value ? null : value;
        }

        private static int WriteSignatures(IXLWorksheet worksheet, int startRow, ClosedXmlBlock block)
        {
            foreach (var signature in block.Signatures)
            {
                var column = Math.Max(1, signature.Column);
                var cell = worksheet.Cell(startRow, column);
                cell.Value = signature.Text;
                cell.Style.Font.Bold = signature.Bold;
                if (signature.MergeToColumn > column)
                    worksheet.Range(startRow, column, startRow, signature.MergeToColumn).Merge();
            }

            return startRow + 1;
        }

        private static int WritePageBreak(IXLWorksheet worksheet, int startRow)
        {
            // ClosedXML 0.105: разрыв страницы ставится на строке, а не свойством PageBreak.
            worksheet.Row(startRow).AddHorizontalPageBreak();
            return startRow + 1;
        }


        private static int WriteTableSection(
            IXLWorksheet worksheet,
            int startRow,
            ClosedXmlBlock block,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var section = block.Table!;
            var columns = section.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Field)).ToList();
            if (columns.Count == 0)
                return startRow;

            // Индексы колонок, которые реально есть в DataTable.
            var visible = columns
                .Where(c => dataTable.Columns.Contains(c.Field))
                .ToList();
            if (visible.Count == 0)
                return startRow;

            var columnByField = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < visible.Count; i++)
            {
                var columnLayout = visible[i];
                var excelColumn = i + 1;
                columnByField[columnLayout.Field] = excelColumn;
                worksheet.Column(excelColumn).Width = columnLayout.Width > 0 ? columnLayout.Width : 14;
            }

            // Шапка таблицы.
            var headerRow = startRow;
            for (var i = 0; i < visible.Count; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = visible[i].Header;
                ApplyCellStyle(cell, section.HeaderBold, false, 0, "Center",
                    section.HeaderFill, section.RowBorder);
                cell.Style.Alignment.WrapText = section.HeaderWrapText;
            }

            var detailRows = new List<int>();
            var currentRow = headerRow + 1;

            var rows = dataTable.Rows.Cast<DataRow>()
                .Where(row => !ShouldSkipTableRow(section, row, visible))
                .ToList();
            if (section.Groups.Count > 0)
            {
                currentRow = WriteGroupsRecursive(worksheet, currentRow, rows, section.Groups, 0,
                    visible, columnByField, section, detailRows, parameters);
            }
            else
            {
                foreach (var dataRow in rows)
                {
                    WriteDetailRow(worksheet, currentRow, dataRow, visible, columnByField,
                        section, detailRows.Count);
                    detailRows.Add(currentRow);
                    currentRow++;
                }
            }

            // Общий итог суммирует только строки данных (не заголовки групп и не подытоги).
            if (section.GrandTotal != null && detailRows.Count > 0)
                currentRow = WriteTotalRow(worksheet, currentRow, section.GrandTotal, visible,
                    columnByField, detailRows, section.RowBorder);

            return currentRow;
        }

        private static int WriteGroupsRecursive(
            IXLWorksheet worksheet,
            int startRow,
            List<DataRow> rows,
            List<ClosedXmlGroup> groups,
            int level,
            List<ClosedXmlColumn> visible,
            Dictionary<string, int> columnByField,
            ClosedXmlTableSection section,
            List<int> detailRows,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var group = groups[level];
            var byFields = group.ByFields
                .Where(field => rows.Count > 0 && rows[0].Table.Columns.Contains(field))
                .ToList();
            if (byFields.Count == 0)
                return startRow;

            var rowCursor = startRow;
            var ordered = rows
                .OrderBy(r => GetFieldValueText(r, byFields[0]), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var chunk in ordered.GroupBy(r => string.Join("|", byFields.Select(f => GetFieldValueText(r, f)))))
            {
                var groupRows = chunk.ToList();
                if (group.ShowHeader)
                {
                    var headerText = BuildGroupHeaderText(group, groupRows[0], byFields, parameters);
                    var cell = worksheet.Cell(rowCursor, 1);
                    cell.Value = headerText;
                    cell.Style.Font.Bold = group.HeaderBold;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F6");
                    if (visible.Count > 1)
                        worksheet.Range(rowCursor, 1, rowCursor, visible.Count).Merge();
                    rowCursor++;
                }

                var groupDetailStart = rowCursor;
                if (level + 1 < groups.Count)
                {
                    rowCursor = WriteGroupsRecursive(worksheet, rowCursor, groupRows, groups, level + 1,
                        visible, columnByField, section, detailRows, parameters);
                }
                else
                {
                    foreach (var dataRow in groupRows)
                    {
                        WriteDetailRow(worksheet, rowCursor, dataRow, visible, columnByField, section,
                            detailRows.Count);
                        detailRows.Add(rowCursor);
                        rowCursor++;
                    }
                }

                // Подытог группы — SUM только по строкам данных этого уровня.
                if (group.Subtotal != null)
                {
                    var groupDetailRows = new List<int>();
                    for (var r = groupDetailStart; r < rowCursor; r++)
                    {
                        if (detailRows.Contains(r))
                            groupDetailRows.Add(r);
                    }

                    if (groupDetailRows.Count > 0)
                        rowCursor = WriteTotalRow(worksheet, rowCursor, group.Subtotal, visible,
                            columnByField, groupDetailRows, "All");
                }
            }

            return rowCursor;
        }

        /// <summary>
        /// Текст строки-заголовка группы. Поддерживаются:
        /// <c>{value}</c> — значения полей группировки через « / »,
        /// <c>{ИмяПоля}</c> — значение конкретного поля первой строки группы,
        /// <c>{param.Имя}</c> — параметр отчёта.
        /// Пример: «Пара счетов: {Дебет} - {Кредит} ({Наименование})».
        /// </summary>
        private static string BuildGroupHeaderText(
            ClosedXmlGroup group,
            DataRow firstRow,
            List<string> byFields,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var format = string.IsNullOrWhiteSpace(group.HeaderFormat) ? "{value}" : group.HeaderFormat;

            var text = format.Replace("{value}",
                string.Join(" / ", byFields.Select(field => GetFieldValueText(firstRow, field))));

            foreach (var field in byFields)
                text = text.Replace("{" + field + "}", GetFieldValueText(firstRow, field));

            return ResolveText(text, parameters);
        }

        private static void WriteDetailRow(
            IXLWorksheet worksheet,
            int rowIndex,
            DataRow dataRow,
            List<ClosedXmlColumn> visible,
            Dictionary<string, int> columnByField,
            ClosedXmlTableSection section,
            int detailIndex)
        {
            var alternate = !string.IsNullOrWhiteSpace(section.AlternateFill) && detailIndex % 2 == 1;
            var labelText = string.Empty;

            for (var i = 0; i < visible.Count; i++)
            {
                var columnLayout = visible[i];
                var cell = worksheet.Cell(rowIndex, i + 1);
                var value = dataRow.Table.Columns.Contains(columnLayout.Field)
                    ? dataRow[columnLayout.Field]
                    : DBNull.Value;

                if (value == DBNull.Value || value == null)
                    cell.Value = string.Empty;
                else if (value is DateTime date)
                    cell.Value = date;
                else if (value is decimal or double or float or int or long or short or byte)
                {
                    // «0 не печатать»: в акте сверки нулевые суммы остаются пустыми.
                    var number = Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
                    cell.Value = columnLayout.SuppressZero && number == 0m ? string.Empty : number;
                }
                else
                {
                    var text = value.ToString() ?? string.Empty;
                    cell.Value = text;
                    if (string.IsNullOrWhiteSpace(labelText) && !string.IsNullOrWhiteSpace(text))
                        labelText = text;
                }

                if (!string.IsNullOrWhiteSpace(columnLayout.NumberFormat))
                    cell.Style.NumberFormat.Format = columnLayout.NumberFormat;
                else if (value is DateTime)
                    cell.Style.NumberFormat.Format = "dd.MM.yyyy";
                else if (value is decimal or double or float or int or long or short or byte)
                    cell.Style.NumberFormat.Format = "#,##0.00";

                ApplyCellStyle(cell, columnLayout.Bold, false, 0, columnLayout.Align,
                    alternate ? section.AlternateFill : string.Empty, section.RowBorder);
            }

            ApplyEmphasisRule(worksheet, rowIndex, visible.Count, section, labelText);
        }

        /// <summary>
        /// Применяет к строке правило выделения: жирный/курсив/выравнивание/заливка
        /// и (при необходимости) объединение метки с колонки 1 по указанную колонку.
        /// </summary>
        private static void ApplyEmphasisRule(
            IXLWorksheet worksheet,
            int rowIndex,
            int columnCount,
            ClosedXmlTableSection section,
            string labelText)
        {
            var rule = MatchEmphasisRule(section, labelText);
            if (rule == null || columnCount <= 0)
                return;

            var mergeTo = Math.Min(rule.MergeLabelToColumn, columnCount);

            if (mergeTo > 1)
            {
                // Объединяем только если справа пусто — иначе потеряли бы данные.
                // Стиль применяем в любом случае.
                var candidate = worksheet.Range(rowIndex, 1, rowIndex, mergeTo);
                if (candidate.CellsUsed().Count() <= 1)
                    candidate.Merge();
            }

            var range = mergeTo > 1
                ? worksheet.Range(rowIndex, 1, rowIndex, mergeTo)
                : worksheet.Range(rowIndex, 1, rowIndex, columnCount);

            if (rule.Bold)
                range.Style.Font.Bold = true;
            if (rule.Italic)
                range.Style.Font.Italic = true;
            if (!string.IsNullOrWhiteSpace(rule.Align))
            {
                range.Style.Alignment.Horizontal = rule.Align.ToUpperInvariant() switch
                {
                    "CENTER" => XLAlignmentHorizontalValues.Center,
                    "RIGHT" => XLAlignmentHorizontalValues.Right,
                    _ => XLAlignmentHorizontalValues.Left
                };
            }

            if (!string.IsNullOrWhiteSpace(rule.Fill))
                range.Style.Fill.BackgroundColor = XLColor.FromHtml(rule.Fill);
        }

        /// <summary>
        /// Пропускает строку тела: пустую (при <c>SkipEmptyRows</c>) или начинающуюся
        /// с одного из заданных текстов.
        /// </summary>
        private static bool ShouldSkipTableRow(
            ClosedXmlTableSection section,
            DataRow row,
            List<ClosedXmlColumn> visible)
        {
            var label = FirstNonEmptyValueText(row, visible);

            if (string.IsNullOrWhiteSpace(label))
                return section.SkipEmptyRows;

            foreach (var prefix in section.SkipRowsStartingWith)
            {
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string FirstNonEmptyValueText(DataRow row, List<ClosedXmlColumn> visible)
        {
            foreach (var column in visible)
            {
                if (!row.Table.Columns.Contains(column.Field))
                    continue;

                var value = row[column.Field];
                if (value == null || value == DBNull.Value)
                    continue;

                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        private static ClosedXmlEmphasisRule? MatchEmphasisRule(ClosedXmlTableSection section, string labelText)
        {
            if (string.IsNullOrWhiteSpace(labelText) || section.EmphasisRules.Count == 0)
                return null;

            foreach (var rule in section.EmphasisRules)
            {
                if (string.IsNullOrWhiteSpace(rule.Text))
                    continue;

                var comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var matched = (rule.Match ?? "StartsWith").ToUpperInvariant() switch
                {
                    "CONTAINS" => labelText.Contains(rule.Text, comparison),
                    "EQUALS" => labelText.Equals(rule.Text, comparison),
                    _ => labelText.StartsWith(rule.Text, comparison)
                };

                if (matched)
                    return rule;
            }

            return null;
        }

        private static int WriteTotalRow(
            IXLWorksheet worksheet,
            int startRow,
            ClosedXmlTotal total,
            List<ClosedXmlColumn> visible,
            Dictionary<string, int> columnByField,
            List<int> detailRows,
            string border)
        {
            // Метка итога — в первую колонку.
            var labelCell = worksheet.Cell(startRow, 1);
            labelCell.Value = total.Label;
            labelCell.Style.Font.Bold = total.Bold;

            var sumFields = total.SumFields.Count > 0
                ? total.SumFields.Where(columnByField.ContainsKey).ToList()
                : visible
                    .Where(c => columnByField.ContainsKey(c.Field))
                    .Select(c => c.Field)
                    .ToList();

            foreach (var field in sumFields)
            {
                var excelColumn = columnByField[field];
                var columnName = GetColumnName(excelColumn);

                // SUM только по строкам данных этого блока (не захватывает
                // заголовки групп и строки подытогов).
                var ranges = BuildContiguousRanges(detailRows);
                var formula = $"SUM({string.Join(",", ranges.Select(r => $"{columnName}{r.First}:{columnName}{r.Last}"))})";
                var cell = worksheet.Cell(startRow, excelColumn);
                cell.FormulaA1 = $"={formula}";
                cell.Style.NumberFormat.Format = "#,##0.00";
                cell.Style.Font.Bold = total.Bold;
            }

            if (!string.IsNullOrWhiteSpace(border))
            {
                var lastColumn = Math.Max(1, visible.Count);
                var range = worksheet.Range(startRow, 1, startRow, lastColumn);
                range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                range.Style.Border.TopBorderColor = XLColor.Black;
            }

            return startRow + 1;
        }

        /// <summary>
        /// Сжимает номера строк в непрерывные диапазоны: 5,6,9,10 -> (5,6) и (9,10).
        /// Без сжатия формула вида SUM(C5:C5,C6:C6,...) на длинном отчёте превышает
        /// предельную длину формулы Excel (8192 символа), и файл перестаёт открываться.
        /// </summary>
        private static IEnumerable<(int First, int Last)> BuildContiguousRanges(IEnumerable<int> rows)
        {
            var ordered = rows.Distinct().OrderBy(r => r).ToList();
            if (ordered.Count == 0)
                yield break;

            var first = ordered[0];
            var last = first;

            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i] == last + 1)
                {
                    last = ordered[i];
                    continue;
                }

                yield return (first, last);
                first = last = ordered[i];
            }

            yield return (first, last);
        }


        private static void ApplyCellStyle(
            IXLCell cell,
            bool bold,
            bool italic,
            double fontSize,
            string? align,
            string fill,
            string? border)
        {
            cell.Style.Font.Bold = bold;
            cell.Style.Font.Italic = italic;
            if (fontSize > 0)
                cell.Style.Font.FontSize = fontSize;
            cell.Style.Alignment.Horizontal = align?.ToUpperInvariant() switch
            {
                "CENTER" => XLAlignmentHorizontalValues.Center,
                "RIGHT" => XLAlignmentHorizontalValues.Right,
                _ => XLAlignmentHorizontalValues.Left
            };

            if (!string.IsNullOrWhiteSpace(fill))
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(fill);

            if (string.IsNullOrWhiteSpace(border) || border.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            if (border.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.Black;
                cell.Style.Border.InsideBorderColor = XLColor.Black;
                return;
            }

            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.Black;
        }

        private static string GetColumnName(int columnNumber)
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

        private static string GetFieldValueText(DataRow row, string field)
        {
            if (!row.Table.Columns.Contains(field))
                return string.Empty;
            var value = row[field];
            return value == DBNull.Value || value == null ? string.Empty : value.ToString() ?? string.Empty;
        }

        private static string ResolveText(string? text, IReadOnlyDictionary<string, object?>? parameters)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (parameters == null || parameters.Count == 0)
                return text;

            return ParameterRegex.Replace(text, match =>
            {
                var name = match.Groups[1].Value;
                foreach (var pair in parameters)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        return pair.Value?.ToString() ?? string.Empty;
                }

                return string.Empty;
            });
        }

        private static string BuildSafeSheetName(string? name)
        {
            var invalidChars = new HashSet<char>(new[] { '[', ']', ':', '*', '?', '/', '\\' });
            var safeName = new string((name ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) || char.IsControl(ch) ? ' ' : ch)
                .ToArray())
                .Trim();

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Лист1";

            return safeName.Length <= 31 ? safeName : safeName[..31].Trim();
        }
    }
}


