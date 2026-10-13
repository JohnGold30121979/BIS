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
    public static partial class ClosedXmlReportRenderer
    {
        private static readonly Regex ParameterRegex = new(
            @"\{(?:param\.)?([A-Za-z_][A-Za-z0-9_]*)(?::([^}]+))?\}",
            RegexOptions.Compiled);

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
        /// <summary>
        /// Готовит набор данных для вывода по ClosedXML-макету.
        ///
        /// Макет обращается к колонкам по своим именам, а набор данных отдаёт их
        /// под именами базы: «posting_date», «debit_account». Без сопоставления
        /// такие колонки молча пропадают из отчёта.
        ///
        /// Поэтому для каждого поля отчёта добавляются дополнительные колонки:
        /// под отображаемым именем («Дата», «Сумма Дт») и под именем макета.
        /// </summary>
        public static DataTable PrepareData(DataTable source, Report report)
        {
            var result = source.Copy();
            if (report?.Fields == null || report.Fields.Count == 0)
                return result;

            foreach (var field in report.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.FieldName))
                    continue;

                var candidates = new[]
                {
                    field.FieldName,
                    field.DisplayName,
                    NormalizeFieldKey(field.FieldName),
                    NormalizeFieldKey(field.DisplayName)
                };

                // Ищем колонку-источник среди имён поля и его отображаемого имени.
                DataColumn? sourceColumn = null;
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    if (result.Columns.Contains(candidate))
                    {
                        sourceColumn = result.Columns[candidate];
                        break;
                    }
                }

                if (sourceColumn == null)
                    continue;

                // Публикуем значение под всеми именами, чтобы макет нашёл его
                // независимо от того, как назвал колонку.
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    if (result.Columns.Contains(candidate))
                        continue;

                    var clone = result.Columns.Add(candidate, sourceColumn.DataType);
                    foreach (DataRow row in result.Rows)
                        row[clone] = row[sourceColumn];
                }
            }

            return result;
        }

        /// <summary>
        /// Находит имя колонки набора данных, соответствующее полю макета.
        /// Сначала точное совпадение, затем — по нормализованному имени,
        /// чтобы «Сумма Дт» и «сумма_дт» считались одним полем.
        /// </summary>
        private static string? ResolveColumnName(DataTable dataTable, string? field)
        {
            if (string.IsNullOrWhiteSpace(field))
                return null;

            if (dataTable.Columns.Contains(field))
                return field;

            var key = NormalizeFieldKey(field);
            if (key.Length == 0)
                return null;

            foreach (DataColumn column in dataTable.Columns)
            {
                if (NormalizeFieldKey(column.ColumnName) == key)
                    return column.ColumnName;
            }

            return null;
        }

        /// <summary>
        /// Приводит имя поля к сравнимому виду: убирает регистр, пробелы,
        /// точки и подчёркивания, а также префикс набора данных (ved.).
        /// </summary>
        private static string NormalizeFieldKey(string? name) =>
            System.Text.RegularExpressions.Regex.Replace(
                (name ?? string.Empty).ToLowerInvariant(),
                @"^(?:ved\d*|db_crs?|dbcrs?)[._]|[^a-zа-я0-9]+",
                string.Empty);

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

        /// <summary>
        /// Верхний и нижний колонтитулы листа.
        ///
        /// По умолчанию вверху справа печатается наименование первичной организации
        /// и счётчик листов: так оформлен печатный бланк ведомости.
        /// </summary>
        private static void ApplyPageHeader(
            IXLWorksheet worksheet,
            ClosedXmlPageSetup setup,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var organization = ResolveOrganizationFromData(dataTable, parameters);

            // Заданный текст колонтитула имеет приоритет над оформлением по умолчанию.
            if (!string.IsNullOrWhiteSpace(setup.PageHeaderText))
            {
                var text = BuildPageText(setup.PageHeaderText, organization);
                var header = worksheet.PageSetup.Header;

                // Счётчик листов ставится только тогда, когда он запрошен в тексте.
                if (text.Contains("&P", StringComparison.OrdinalIgnoreCase))
                    text = text.Replace("Лист &P", "&\"Times New Roman\"Лист &P из &N", StringComparison.OrdinalIgnoreCase);

                header.Right.Clear();
                header.Right.AddText(text);
            }
            else if (setup.ShowPageHeader)
            {
                // Организация и счётчик листов — одной строкой справа вверху.
                var header = worksheet.PageSetup.Header;
                header.Right.Clear();

                if (!string.IsNullOrWhiteSpace(organization))
                    header.Right.AddText(organization + "   ", XLHFOccurrence.AllPages);

                header.Right.AddText("Лист &P из &N", XLHFOccurrence.AllPages);
            }

            if (!string.IsNullOrWhiteSpace(setup.PageFooterText))
            {
                var footer = worksheet.PageSetup.Footer;
                footer.Center.Clear();
                footer.Center.AddText(BuildPageText(setup.PageFooterText, organization));
            }
        }

        /// <summary>Подставляет значения в текст колонтитула.</summary>
        private static string BuildPageText(string text, string organization) =>
            text
                .Replace("{Organization}", organization, StringComparison.OrdinalIgnoreCase)
                .Replace("{Дата}", DateTime.Now.ToString("dd/MM/yy"), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Ищет наименование организации в данных отчёта.
        ///
        /// Порядок: колонка report_org, затем organization_name, затем «Организация».
        /// </summary>
        private static string ResolveOrganizationFromData(
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            foreach (var name in new[] { "report_org", "organization_name", "Организация", "organization_id" })
            {
                if (!dataTable.Columns.Contains(name) || dataTable.Rows.Count == 0)
                    continue;

                var value = dataTable.Rows[0][name]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            foreach (var pair in parameters ?? new Dictionary<string, object?>())
            {
                if (pair.Key.Contains("Organization", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(pair.Value?.ToString()))
                {
                    return pair.Value!.ToString()!;
                }
            }

            return string.Empty;
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

            ApplyPageHeader(worksheet, setup, dataTable, parameters);

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
                    ApplyTextRotation(cell, cellLayout.TextRotation, cellLayout.WrapText);
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


        /// <summary>
        /// Рисует многоуровневую шапку таблицы и возвращает число занятых строк.
        ///
        /// Уровни идут сверху вниз. Ячейка объединяется по своим колонкам.
        /// Пустая подпись в ячейке нижнего уровня заменяется заголовком колонки —
        /// так подписи графов не приходится вводить дважды.
        /// </summary>
        private static int WriteMultiLevelHeader(
            IXLWorksheet worksheet,
            int startRow,
            ClosedXmlTableSection section,
            List<ClosedXmlColumn> visible)
        {
            var header = BuildHeaderGrid(section, visible);

            foreach (var placement in header.Placements)
            {
                var rowIndex = startRow + placement.Row;
                var excelCell = worksheet.Cell(rowIndex, placement.FromColumn);

                if (worksheet.Cell(rowIndex, placement.FromColumn).IsMerged())
                    continue;

                excelCell.Value = ResolveText(placement.Text, null);
                ApplyCellStyle(excelCell, placement.Bold, false, 0, placement.Align,
                    string.IsNullOrWhiteSpace(placement.Fill) ? section.HeaderFill : placement.Fill,
                    section.RowBorder);
                ApplyTextRotation(excelCell, placement.TextRotation, wrapText: true);

                if (placement.ToColumn > placement.FromColumn || placement.RowSpan > 1)
                {
                    worksheet.Range(
                        rowIndex, placement.FromColumn,
                        rowIndex + placement.RowSpan - 1, placement.ToColumn).Merge();
                }
            }

            for (var offset = 0; offset < header.RowCount; offset++)
            {
                if (section.HeaderRowHeight > 0)
                    worksheet.Row(startRow + offset).Height = section.HeaderRowHeight;
            }

            return header.RowCount;
        }

        /// <summary>
        /// Собирает подпись для объединения колонок: заголовки непустых колонок
        /// через разделитель. Нужно для верхнего уровня шапки.
        /// </summary>
        private static string BuildHeaderTextForRange(
            IReadOnlyList<ClosedXmlColumn> visible,
            int from,
            int to)
        {
            var parts = new List<string>();
            for (var index = from; index <= to && index <= visible.Count; index++)
            {
                var header = visible[index - 1].Header;
                if (!string.IsNullOrWhiteSpace(header))
                    parts.Add(header.Trim());
            }

            return parts.Count switch
            {
                0 => string.Empty,
                1 => parts[0],
                _ => string.Join(" / ", parts)
            };
        }

        /// <summary>
        /// Поворот текста в ячейке. Значения приводятся к тому, что понимает Excel:
        /// 255 — вертикальный текст, 90 и 270 — поворот, остальное — как задано.
        /// </summary>
        private static void ApplyTextRotation(IXLCell cell, int rotation, bool wrapText)
        {
            if (rotation != 0)
                cell.Style.Alignment.TextRotation = (byte)Math.Clamp(rotation, 0, 180);

            if (wrapText)
                cell.Style.Alignment.WrapText = true;
        }

        /// <summary>
        /// Размещённая ячейка шапки: где стоит и что подписывает.
        ///
        /// Начертание хранится копией, а не ссылкой на модель: при растягивании
        /// ячейки в пустую строку объединение по вертикали увеличивается.
        /// </summary>
        private sealed record HeaderPlacement(
            int Row,
            int RowSpan,
            int FromColumn,
            int ToColumn,
            string Text,
            bool Bold,
            string Align,
            int TextRotation,
            string Fill);

        /// <summary>Размещение шапки: ячейки и число занятых строк.</summary>
        private sealed record HeaderGrid(
            IReadOnlyList<HeaderPlacement> Placements,
            int RowCount);

        /// <summary>
        /// Раскладывает ячейки уровней шапки по строкам сетки.
        ///
        /// Для каждой колонки хранится первая свободная строка. Ячейка занимает
        /// самую верхнюю свободную строку своих колонок, но не выше начала своего
        /// уровня: поэтому подпись графы поднимается вверх, когда верхний уровень
        /// её не перекрывает, и не налезает на уже поставленные подписи.
        ///
        /// Ячейка с <c>RowSpan</c> больше единицы занимает столько строк вниз:
        /// так «НДС» перекрывает строку со «Ставка» и «Сумма».
        ///
        /// Пустая подпись ячейки последнего уровня заменяется заголовком колонки.
        /// </summary>
        private static HeaderGrid BuildHeaderGrid(
            ClosedXmlTableSection section,
            IReadOnlyList<ClosedXmlColumn> visible)
        {
            var levels = section.HeaderLevels;
            var lastLevelIndex = levels.Count - 1;
            var columnCount = visible.Count;

            var placements = new List<HeaderPlacement>();

            // Занятые ячейки: строки, которые уже перекрыты подписями.
            var occupied = new HashSet<(int Row, int Column)>();

            var levelBaseRow = 0;
            var rowCount = 0;

            for (var levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                var isLastLevel = levelIndex == lastLevelIndex;

                foreach (var headerCell in levels[levelIndex].Cells)
                {
                    var from = headerCell.Column;
                    var to = headerCell.MergeToColumn > from ? headerCell.MergeToColumn : from;

                    if (from < 1 || from > columnCount)
                        continue;

                    if (to > columnCount)
                        to = columnCount;

                    var rowSpan = Math.Max(1, headerCell.RowSpan);

                    // Ищем самую верхнюю свободную строку для этих колонок.
                    // Подпись поднимается вверх, если верхние уровни её не перекрывают.
                    var row = levelBaseRow;
                    while (IsRangeOccupied(occupied, row, rowSpan, from, to))
                        row++;

                    for (var probe = row; probe < row + rowSpan; probe++)
                    {
                        for (var column = from; column <= to; column++)
                            occupied.Add((probe, column));
                    }

                    if (row + rowSpan > rowCount)
                        rowCount = row + rowSpan;

                    var text = string.IsNullOrWhiteSpace(headerCell.Text) && isLastLevel
                        ? BuildHeaderTextForRange(visible, from, to)
                        : headerCell.Text;

                    placements.Add(new HeaderPlacement(
                        row, rowSpan, from, to, text,
                        headerCell.Bold, headerCell.Align, headerCell.TextRotation, headerCell.Fill));
                }

                // Следующий уровень ищет место не ниже самого верхнего края
                // уже поставленных подписей: так он может занять строку, которую
                // верхний уровень не перекрыл, и в шапке не остаётся пустой полосы.
                var highestOccupiedRow = int.MaxValue;
                foreach (var cell in occupied)
                {
                    if (cell.Row < highestOccupiedRow)
                        highestOccupiedRow = cell.Row;
                }

                levelBaseRow = highestOccupiedRow == int.MaxValue ? levelBaseRow : highestOccupiedRow;
            }

            if (rowCount == 0)
                rowCount = Math.Max(1, levels.Count);

            return new HeaderGrid(placements, rowCount);
        }

        /// <summary>Проверяет, перекрыт ли участок сетки уже поставленными подписями.</summary>
        private static bool IsRangeOccupied(
            HashSet<(int Row, int Column)> occupied,
            int row,
            int rowSpan,
            int fromColumn,
            int toColumn)
        {
            for (var probe = row; probe < row + rowSpan; probe++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (occupied.Contains((probe, column)))
                        return true;
                }
            }

            return false;
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
            // Если колонки нет под именем макета, ищем её по нормализованному имени:
            // макет может обращаться к «Сумма Дт», а данные отдавать «amount_kgs».
            var visible = columns
                .Where(c => !string.IsNullOrWhiteSpace(ResolveColumnName(dataTable, c.Field)))
                .ToList();

            if (visible.Count == 0)
                return startRow;

            var columnByField = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < visible.Count; i++)
            {
                var columnLayout = visible[i];
                var excelColumn = i + 1;
                columnByField[ResolveColumnName(dataTable, columnLayout.Field) ?? columnLayout.Field] = excelColumn;
                worksheet.Column(excelColumn).Width = columnLayout.Width > 0 ? columnLayout.Width : 14;
            }

            // Шапка таблицы.
            var headerRow = startRow;
            var headerRowCount = 1;

            if (section.HeaderLevels.Count > 0)
            {
                // Форматная шапка: несколько уровней с объединениями и поворотом.
                headerRowCount = WriteMultiLevelHeader(worksheet, headerRow, section, visible);
            }
            else
            {
                for (var i = 0; i < visible.Count; i++)
                {
                    var cell = worksheet.Cell(headerRow, i + 1);
                    cell.Value = visible[i].Header;
                    ApplyCellStyle(cell, section.HeaderBold, false, 0, "Center",
                        section.HeaderFill, section.RowBorder);
                    cell.Style.Alignment.WrapText = section.HeaderWrapText;
                    ApplyTextRotation(cell, visible[i].TextRotation, section.HeaderWrapText);
                }
            }

            var detailRows = new List<int>();
            var currentRow = headerRow + headerRowCount;

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

            // Группы без значения поля (например, «Без организации») идут последними:
            // так заполненные группы стоят в начале, как в печатной форме.
            var ordered = rows
                .OrderBy(r =>
                {
                    var text = GetFieldValueText(r, byFields[0]);
                    return string.IsNullOrWhiteSpace(text) ? 1 : 0;
                })
                .ThenBy(r => GetFieldValueText(r, byFields[0]), StringComparer.CurrentCultureIgnoreCase)
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
                    {
                        // Значение группы попадает в подпись подытога: «ИТОГО по 2».
                        // Если заданное поле пусто, берётся первое непустое из группировки.
                        var groupValue = string.Empty;
                        var valueFields = !string.IsNullOrWhiteSpace(group.SubtotalValueField)
                            ? new List<string> { group.SubtotalValueField! }
                            : byFields.ToList();

                        foreach (var field in valueFields)
                        {
                            groupValue = GetFieldValueText(groupRows[0], field);
                            if (!string.IsNullOrWhiteSpace(groupValue))
                                break;
                        }

                        rowCursor = WriteTotalRow(worksheet, rowCursor, group.Subtotal, visible,
                            columnByField, groupDetailRows, "All", groupValue);
                    }
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
            string border,
            string groupValue = "")
        {
            // Метка итога — в первую колонку. К подытогу группы добавляется
            // её значение: «ИТОГО по 2».
            var labelCell = worksheet.Cell(startRow, 1);
            labelCell.Value = string.IsNullOrWhiteSpace(groupValue)
                ? total.Label
                : $"{total.Label} {groupValue}";
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

        /// <summary>
        /// Подставляет параметры отчёта в текст.
        ///
        /// Поддерживаются записи <c>{Имя}</c> и <c>{param.Имя}</c>, а также формат
        /// значения через двоеточие: <c>{PeriodStart:dd/MM/yyyy}</c>.
        /// Неизвестные подстановки удаляются, чтобы в документе не осталось
        /// фигурных скобок.
        /// </summary>
        private static string ResolveText(string? text, IReadOnlyDictionary<string, object?>? parameters)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return ParameterRegex.Replace(text, match =>
            {
                var name = match.Groups[1].Value;
                var format = match.Groups[2].Success ? match.Groups[2].Value : null;

                foreach (var pair in parameters ?? new Dictionary<string, object?>())
                {
                    if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return FormatParameterValue(pair.Value, format);
                }

                return parameters == null ? match.Value : string.Empty;
            });
        }

        /// <summary>Приводит значение параметра к тексту с учётом формата.</summary>
        private static string FormatParameterValue(object? value, string? format)
        {
            if (value == null)
                return string.Empty;

            if (value is DateTime date)
                return date.ToString(string.IsNullOrWhiteSpace(format) ? "dd.MM.yyyy" : format,
                    System.Globalization.CultureInfo.CurrentCulture);

            if (value is DateOnly dateOnly)
                return dateOnly.ToString(string.IsNullOrWhiteSpace(format) ? "dd.MM.yyyy" : format,
                    System.Globalization.CultureInfo.CurrentCulture);

            if (value is decimal or double or float && !string.IsNullOrWhiteSpace(format))
                return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture).ToString(format);

            var text = value.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(format) && DateTime.TryParse(text, out var parsed))
                return parsed.ToString(format, System.Globalization.CultureInfo.CurrentCulture);

            return text;
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


