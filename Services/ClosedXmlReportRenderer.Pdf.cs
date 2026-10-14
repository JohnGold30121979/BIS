using System.Data;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Вывод отчёта с визуальным макетом ClosedXML в PDF.
    ///
    /// Обходит ту же модель (<see cref="ClosedXmlReportLayout"/>), что и вывод в Excel,
    /// поэтому PDF и Excel совпадают по составу и оформлению.
    ///
    /// Поддерживается: заголовочные и свободные строки с объединением ячеек,
    /// табличные разделы с шапкой, рамками, заливкой и чередованием строк,
    /// правила выделения строк, пропуск строк, группировки с подытогами,
    /// общий итог и подписи.
    /// </summary>
    public static partial class ClosedXmlReportRenderer
    {
        /// <summary>
        /// Шрифт и кегль текущего листа. Нужны для картинок с повёрнутым текстом:
        /// подпись должна быть набрана тем же шрифтом, что и остальной документ.
        /// </summary>
        [ThreadStatic]
        private static string? _pageFontName;

        [ThreadStatic]
        private static double _pageFontSize;

        /// <summary>Выводит макет ClosedXML в PDF.</summary>
        public static byte[] RenderToPdfBytes(
            ClosedXmlReportLayout layout,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            if (layout.Sheets.Count == 0)
                throw new InvalidOperationException("ClosedXML-макет не содержит листов.");

            // Настройка лицензии QuestPDF: та же, что в ReportService и PrintFormService.
            // Рендерер самодостаточен — его можно вызывать без подготовки сервисов.
            QuestPDF.Settings.License = LicenseType.Community;

            var sheetLayout = layout.Sheets[0];
            var setup = sheetLayout.PageSetup ?? new ClosedXmlPageSetup();
            var landscape = !string.Equals(setup.Orientation, "Portrait", StringComparison.OrdinalIgnoreCase);
            var fontName = string.IsNullOrWhiteSpace(setup.FontName) ? "Times New Roman" : setup.FontName;
            var fontSize = setup.FontSize > 0 ? (float)setup.FontSize : 10f;

            // Запоминаем шрифт листа: им рисуются картинки с повёрнутым текстом.
            _pageFontName = fontName;
            _pageFontSize = fontSize;

            // Верхний колонтитул: организация и счётчик листов справа вверху.
            var headerText = ResolvePdfHeaderText(setup, dataTable, parameters);

            return QuestPDF.Fluent.Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(10, Unit.Millimetre);
                    page.DefaultTextStyle(style => style.FontFamily(fontName).FontSize(fontSize));

                    if (!string.IsNullOrWhiteSpace(headerText))
                    {
                        // Организация и счётчик листов — справа вверху, как в бланке.
                        page.Header().AlignRight().Text(text =>
                        {
                            text.Span(headerText + "    ").FontSize(fontSize - 1);
                            text.Span("Лист ").FontSize(fontSize - 1);
                            text.CurrentPageNumber().FontSize(fontSize - 1);
                            text.Span(" из ").FontSize(fontSize - 1);
                            text.TotalPages().FontSize(fontSize - 1);
                        });
                    }
                    else
                    {
                        page.Header().AlignRight().Text(text =>
                        {
                            text.Span("Лист ").FontSize(fontSize - 1);
                            text.CurrentPageNumber().FontSize(fontSize - 1);
                            text.Span(" из ").FontSize(fontSize - 1);
                            text.TotalPages().FontSize(fontSize - 1);
                        });
                    }

                    page.Content().Column(column =>
                    {
                        column.Spacing(2);

                        // Узкая сетка листа: её же используют текстовые блоки и шапка.
                        _sheetGridColumns = sheetLayout.GridColumns > 0 ? sheetLayout.GridColumns : 0;

                        foreach (var block in sheetLayout.Blocks)
                        {
                            switch (block.Type)
                            {
                                case "TitleRows":
                                case "FreeRows":
                                    RenderTextRows(column, block, dataTable, parameters);
                                    break;

                                case "TabularSection" when block.Table != null:
                                    RenderTableSection(column, block.Table, dataTable, parameters);
                                    break;

                                case "SignaturesRow":
                                    RenderSignatures(column, block);
                                    break;
                            }
                        }
                    });
                });
            }).GeneratePdf();
        }

        /// <summary>
        /// Текст верхнего колонтитула PDF.
        ///
        /// По умолчанию — наименование первичной организации. Номер листа
        /// подставляет сам PDF-документ, поэтому в текст он не входит.
        /// </summary>
        private static string ResolvePdfHeaderText(
            ClosedXmlPageSetup setup,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            if (!string.IsNullOrWhiteSpace(setup.PageHeaderText))
            {
                var text = setup.PageHeaderText
                    .Replace("{Дата}", DateTime.Now.ToString("dd/MM/yy"), StringComparison.OrdinalIgnoreCase);

                return text.Replace("{Organization}", ResolveOrganizationFromData(dataTable, parameters),
                    StringComparison.OrdinalIgnoreCase);
            }

            return setup.ShowPageHeader ? ResolveOrganizationFromData(dataTable, parameters) : string.Empty;
        }

        /// <summary>Выводит макет ClosedXML в PDF и сохраняет в файл.</summary>
        public static void RenderToPdfFile(
            string filePath,
            ClosedXmlReportLayout layout,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            var bytes = RenderToPdfBytes(layout, dataTable, parameters);
            File.WriteAllBytes(filePath, bytes);
        }

        /// <summary>Выводит текстовые строки блока: заголовок, реквизиты, свободные строки.</summary>
        private static void RenderTextRows(
            ColumnDescriptor column,
            ClosedXmlBlock block,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            var columnCount = _sheetGridColumns > 0
                ? _sheetGridColumns
                : Math.Max(1, ResolveColumnCount(block));

            foreach (var row in block.Rows)
            {
                var cells = row.Cells
                    .Where(cell => cell.Column > 0)
                    .OrderBy(cell => cell.Column)
                    .ToList();
                if (cells.Count == 0)
                    continue;

                column.Item().Row(rowDescriptor =>
                {
                    var cursor = 1;
                    foreach (var cellLayout in cells)
                    {
                        if (cellLayout.Column > cursor)
                        {
                            rowDescriptor.RelativeItem(cellLayout.Column - cursor);
                            cursor = cellLayout.Column;
                        }

                        var span = cellLayout.MergeToColumn >= cellLayout.Column
                            ? cellLayout.MergeToColumn - cellLayout.Column + 1
                            : 1;
                        span = Math.Max(1, span);

                        var text = BuildTextCellValue(cellLayout, dataTable, parameters);
                        var align = ParseAlign(cellLayout.Align);
                        var bold = cellLayout.Bold;
                        var italic = cellLayout.Italic;
                        var fontSize = cellLayout.FontSize;
                        var fill = cellLayout.Fill;
                        var border = cellLayout.Border;
                        var rotation = cellLayout.TextRotation;

                        var cellContainer = rowDescriptor.RelativeItem(span)
                            .Element(container => ApplyCellContainer(container, fill, border))
                            .PaddingVertical(1)
                            .PaddingHorizontal(3)
                            .Element(container => ApplyAlign(container, align));

                        if (rotation != 0)
                        {
                            // Поворот в PDF недоступен: рисуем повёрнутый текст картинкой.
                            WriteRotatedText(cellContainer, text, rotation, bold, italic, fontSize);
                        }
                        else
                        {
                            cellContainer.Text(textBlock =>
                            {
                                if (string.IsNullOrWhiteSpace(text))
                                    return;

                                var textSpan = textBlock.Span(text);
                                if (bold)
                                    textSpan.Bold();
                                if (italic)
                                    textSpan.Italic();
                                if (fontSize > 0)
                                    textSpan.FontSize((float)fontSize);
                            });
                        }

                        cursor += span;
                    }

                    if (cursor <= columnCount)
                        rowDescriptor.RelativeItem(columnCount - cursor + 1);
                });
            }
        }

        /// <summary>
        /// Рисует многоуровневую шапку.
        ///
        /// Размещение ячеек считает общий расчёт <c>BuildHeaderGrid</c>: он же
        /// используется выводом в Excel, поэтому шапки совпадают.
        /// </summary>
        private static void RenderMultiLevelHeader(
            TableDescriptor table,
            ClosedXmlTableSection section,
            List<ClosedXmlColumn> visible)
        {
            var header = BuildHeaderGrid(section, visible.GetRange(0, visible.Count));

            // Высота строки шапки ограничивает картинки с вертикальными подписями.
            var previousHeightLimit = ContainerHeightLimit;
            ContainerHeightLimit = ResolveHeaderHeightLimit(section, header.RowCount);

            try
            {
                // Позиция каждой ячейки задаётся явно. Без этого QuestPDF ставит
                // ячейки подряд, и подписи нижних уровней уезжали в первую строку:
                // «Дебет» и «Кредит» попадали под «Дату» и «Документ».
                foreach (var placement in header.Placements)
                {
                    var cell = table.Cell()
                        .Column((uint)Math.Max(1, placement.FromColumn))
                        .Row((uint)(placement.Row + 1));

                    if (placement.ToColumn > placement.FromColumn)
                        cell = cell.ColumnSpan((uint)(placement.ToColumn - placement.FromColumn + 1));
                    if (placement.RowSpan > 1)
                        cell = cell.RowSpan((uint)placement.RowSpan);

                    var fill = string.IsNullOrWhiteSpace(placement.Fill)
                        ? section.HeaderFill
                        : placement.Fill;
                    var align = ParseAlign(placement.Align);
                    var rotation = placement.TextRotation;
                    var bold = placement.Bold;

                    var cellContainer = cell
                        .Element(container => ApplyCellContainer(container, fill, section.RowBorder))
                        .PaddingVertical(2)
                        .PaddingHorizontal(3)
                        .Element(container => ApplyAlign(container, align));

                    if (rotation != 0)
                    {
                        WriteRotatedText(cellContainer, placement.Text, rotation, bold, false, 0);
                    }
                    else
                    {
                        cellContainer.Text(textBlock =>
                        {
                            if (string.IsNullOrWhiteSpace(placement.Text))
                                return;

                            var textSpan = textBlock.Span(placement.Text);
                            if (bold)
                                textSpan.Bold();
                        });
                    }
                }
            }
            finally
            {
                ContainerHeightLimit = previousHeightLimit;
            }
        }

        /// <summary>
        /// Высота, в которую должна уложиться подпись графы.
        ///
        /// Значение ограничено намеренно: в PDF высота строки определяется
        /// содержимым, поэтому высокая картинка растягивает шапку и подпись
        /// вылезает за границы ячейки. Ограничение задаётся не настройкой
        /// макета, а удобным размером подписи.
        /// </summary>
        private static double ResolveHeaderHeightLimit(ClosedXmlTableSection section, int levelsCount)
        {
            // Около 16 пунктов на уровень — примерно высота строки бланка.
            const double perLevel = 16d;
            return Math.Max(30d, levelsCount * perLevel);
        }

        /// <summary>
        /// Выводит повёрнутый текст.
        ///
        /// В PDF поворот текста недоступен, поэтому подпись рисуется средствами WPF
        /// в картинку и вставляется в документ. Картинки кэшируются: одинаковые
        /// подписи рисуются один раз.
        /// </summary>
        private static void WriteRotatedText(
            IContainer container,
            string? text,
            int rotation,
            bool bold,
            bool italic,
            double fontSize)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            // Угол приводим к допустимому: 0 заменяем на 90 — вертикальная подпись.
            var requested = Math.Clamp(rotation, -90, 90);
            var angle = requested == 0 ? 90 : requested;

            // Кегль: заданный у ячейки, иначе кегль листа.
            var emSize = fontSize > 0 ? fontSize : (_pageFontSize > 0 ? _pageFontSize : 9d);

            try
            {
                var (size, bytes) = GetRotatedTextImage(text.Trim(), angle, bold, italic, emSize);

                // Высота ограничена строкой шапки: без ограничения длинная подпись
                // вылезает за границы ячейки. Ширину масштабируем по пропорции.
                var heightLimit = ContainerHeightLimit > 0 ? ContainerHeightLimit : 260d;
                var scale = size.Height > heightLimit ? heightLimit / size.Height : 1d;

                container
                    .Width((float)Math.Max(4, size.Width * scale))
                    .Height((float)Math.Max(4, size.Height * scale))
                    .Image(bytes);
            }
            catch
            {
                // Если картинку собрать не удалось, показываем текст как есть:
                // отчёт важнее оформления.
                container.Text(textBlock => textBlock.Span(text));
            }
        }

        /// <summary>
        /// Ограничение высоты для картинки с повёрнутым текстом.
        /// Задаётся при выводе шапки по её высоте.
        /// </summary>
        [ThreadStatic]
        private static double ContainerHeightLimit;

        // Узкая сетка листа объявлена в ClosedXmlReportRenderer.cs: это один класс,
        // и сетка у обоих выводов общая.

        /// <summary>
        /// Готовит картинку с повёрнутым текстом. Результат кэшируется по подписи,
        /// углу и начертанию.
        /// </summary>
        private static (RotatedTextSize Size, byte[] Bytes) GetRotatedTextImage(
            string text,
            int angle,
            bool bold,
            bool italic,
            double fontSize)
        {
            var key = $"{text}|{angle}|{bold}|{italic}|{fontSize:0.##}";
            if (RotatedTextCache.TryGetValue(key, out var cached))
                return cached;

            var size = RenderRotatedTextImage(text, angle, bold, italic, fontSize, out var bytes);
            RotatedTextCache[key] = (size, bytes);
            return (size, bytes);
        }

        /// <summary>Размер картинки с повёрнутым текстом.</summary>
        private readonly record struct RotatedTextSize(double Width, double Height);

        /// <summary>Кэш картинок с повёрнутым текстом.</summary>
        private static readonly Dictionary<string, (RotatedTextSize Size, byte[] Bytes)> RotatedTextCache =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Рисует текст, повёрнутый на заданный угол, и возвращает картинку в PNG.
        /// Размер картинки считается так, чтобы текст поместился целиком.
        /// </summary>
        private static RotatedTextSize RenderRotatedTextImage(
            string text,
            int angle,
            bool bold,
            bool italic,
            double fontSize,
            out byte[] pngBytes)
        {
            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(_pageFontName) ? "Times New Roman" : _pageFontName),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            var emSize = fontSize > 0 ? fontSize : 9d;
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                emSize,
                System.Windows.Media.Brushes.Black,
                VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

            // Отступы: картинка не должна обрезать буквы.
            const double padding = 3d;
            var textWidth = formatted.Width + padding * 2;
            var textHeight = formatted.Height + padding * 2;

            var isVertical = Math.Abs(angle) >= 45;
            var imageWidth = isVertical ? textHeight : textWidth;
            var imageHeight = isVertical ? textWidth : textHeight;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new RotateTransform(angle, imageWidth / 2d, imageHeight / 2d));
                context.DrawText(formatted, new System.Windows.Point(
                    (imageWidth - formatted.Width) / 2d,
                    (imageHeight - formatted.Height) / 2d));
                context.Pop();
            }

            var target = new RenderTargetBitmap(
                (int)Math.Ceiling(imageWidth),
                (int)Math.Ceiling(imageHeight),
                96, 96,
                PixelFormats.Pbgra32);
            target.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            pngBytes = stream.ToArray();

            return new RotatedTextSize(target.PixelWidth, target.PixelHeight);
        }

        /// <summary>Выводит табличный раздел: шапку, строки с группировкой и общий итог.</summary>
        private static void RenderTableSection(
            ColumnDescriptor column,
            ClosedXmlTableSection section,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters)
        {
            var columns = section.Columns.Where(item => !string.IsNullOrWhiteSpace(item.Field)).ToList();
            if (columns.Count == 0)
                return;

            var visible = columns
                .Where(item => !string.IsNullOrWhiteSpace(ResolveColumnName(dataTable, item.Field)))
                .ToList();
            if (visible.Count == 0)
                return;

            var weights = visible
                .Select(item => _sheetGridColumns > 0
                    ? Math.Max(1d, item.Span <= 0 ? 1d : item.Span)
                    : item.Width > 0 ? item.Width : 14d)
                .ToList();

            var rows = dataTable.Rows.Cast<DataRow>()
                .Where(row => !ShouldSkipTableRow(section, row, visible))
                .ToList();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    foreach (var weight in weights)
                        definition.RelativeColumn((float)weight);
                });

                if (section.HeaderLevels.Count > 0)
                {
                    // Форматная шапка: уровни с объединениями и поворотом подписей.
                    RenderMultiLevelHeader(table, section, visible);
                }
                else
                {
                    var previousHeightLimit = ContainerHeightLimit;
                    ContainerHeightLimit = ResolveHeaderHeightLimit(section, 1);

                    try
                    {
                        foreach (var item in visible)
                        {
                            var fill = section.HeaderFill;
                            var bold = section.HeaderBold;
                            var align = ParseAlign(item.Align);
                            var border = section.RowBorder;
                            var header = item.Header ?? string.Empty;
                            var rotation = item.TextRotation;

                            var headerContainer = table.Cell()
                                .Element(container => ApplyCellContainer(container, fill, border))
                                .PaddingVertical(2)
                                .PaddingHorizontal(3)
                                .Element(container => ApplyAlign(container, align));

                            if (rotation != 0)
                                WriteRotatedText(headerContainer, header, rotation, bold, false, 0);
                            else
                                headerContainer.Text(textBlock =>
                                {
                                    var textSpan = textBlock.Span(header);
                                    if (bold)
                                        textSpan.Bold();
                                });
                        }
                    }
                    finally
                    {
                        ContainerHeightLimit = previousHeightLimit;
                    }
                }

                if (section.Groups.Count > 0)
                {
                    var groupDetailIndex = 0;
                    RenderGroups(table, rows, section.Groups, 0, visible, section, parameters, ref groupDetailIndex);
                }
                else
                {
                    var detailIndex = 0;
                    foreach (var row in rows)
                    {
                        RenderDetailRow(table, row, visible, section, detailIndex);
                        detailIndex++;
                    }
                }

                if (section.GrandTotal != null && rows.Count > 0)
                    RenderTotalRow(table, rows, visible, section, section.GrandTotal);

                // Дополнительные итоговые строки бланка: «Налог с продаж по ставке … %», «ВСЕГО».
                if (rows.Count > 0)
                {
                    foreach (var extraTotal in section.ExtraTotals.Where(total => total != null))
                        RenderTotalRow(table, rows, visible, section, extraTotal);
                }
            });
        }

        /// <summary>Выводит строку данных.</summary>
        private static void RenderDetailRow(
            TableDescriptor table,
            DataRow row,
            List<ClosedXmlColumn> visible,
            ClosedXmlTableSection section,
            int detailIndex)
        {
            var alternate = !string.IsNullOrWhiteSpace(section.AlternateFill) && detailIndex % 2 == 1;
            var fill = alternate ? section.AlternateFill : string.Empty;
            var labelText = FirstNonEmptyValueText(row, visible);
            var rule = MatchEmphasisRule(section, labelText);
            var mergeTo = rule is { MergeLabelToColumn: > 1 }
                ? Math.Min(rule.MergeLabelToColumn, visible.Count)
                : 0;

            for (var index = 0; index < visible.Count; index++)
            {
                var columnLayout = visible[index];
                var columnIndex = index + 1;

                if (mergeTo > 1 && columnIndex > 1 && columnIndex <= mergeTo)
                    continue;

                var span = mergeTo > 1 && columnIndex == 1 ? mergeTo : 1;
                var text = FormatCellValue(row, columnLayout);
                var align = rule != null && !string.IsNullOrWhiteSpace(rule.Align) ? rule.Align : columnLayout.Align;
                var bold = columnLayout.Bold || (rule?.Bold ?? false);
                var italic = rule?.Italic ?? false;
                var cellFill = !string.IsNullOrWhiteSpace(rule?.Fill) ? rule!.Fill : fill;
                var border = section.RowBorder;

                var cell = table.Cell();
                if (span > 1)
                    cell = cell.ColumnSpan((uint)span);

                cell.Element(container => ApplyCellContainer(container, cellFill, border))
                    .PaddingVertical(1)
                    .PaddingHorizontal(3)
                    .Element(container => ApplyAlign(container, ParseAlign(align)))
                    .Text(textBlock =>
                    {
                        if (string.IsNullOrWhiteSpace(text))
                            return;

                        var textSpan = textBlock.Span(text);
                        if (bold)
                            textSpan.Bold();
                        if (italic)
                            textSpan.Italic();
                    });
            }
        }

        /// <summary>Выводит строки группировок с подытогами.</summary>
        private static void RenderGroups(
            TableDescriptor table,
            List<DataRow> rows,
            List<ClosedXmlGroup> groups,
            int level,
            List<ClosedXmlColumn> visible,
            ClosedXmlTableSection section,
            IReadOnlyDictionary<string, object?>? parameters,
            ref int detailIndex)
        {
            if (level >= groups.Count || rows.Count == 0)
                return;

            var group = groups[level];
            var byFields = group.ByFields
                .Where(field => rows[0].Table.Columns.Contains(field))
                .ToList();
            if (byFields.Count == 0)
                return;

            var ordered = rows
                .OrderBy(row => GetFieldValueText(row, byFields[0]), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var chunk in ordered.GroupBy(row =>
                         string.Join("|", byFields.Select(field => GetFieldValueText(row, field)))))
            {
                var groupRows = chunk.ToList();

                if (group.ShowHeader)
                {
                    var headerText = BuildGroupHeaderText(group, groupRows[0], byFields, parameters);
                    var bold = group.HeaderBold;

                    table.Cell().ColumnSpan((uint)visible.Count)
                        .PaddingVertical(2)
                        .Text(textBlock =>
                        {
                            var textSpan = textBlock.Span(headerText);
                            if (bold)
                                textSpan.Bold();
                        });
                }

                if (level + 1 < groups.Count)
                {
                    RenderGroups(table, groupRows, groups, level + 1, visible, section, parameters, ref detailIndex);
                }
                else
                {
                    foreach (var row in groupRows)
                    {
                        RenderDetailRow(table, row, visible, section, detailIndex);
                        detailIndex++;
                    }
                }

                if (group.Subtotal != null)
                {
                    // Значение группы попадает в подпись подытога: «ИТОГО по 3».
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

                        // Группа без номера подписывается наименованием, чтобы
                        // подытог не остался без значения.
                        if (string.IsNullOrWhiteSpace(groupValue))
                            groupValue = GetFieldValueText(groupRows[0], "organization_name");

                        RenderTotalRow(table, groupRows, visible, section, group.Subtotal, groupValue);
                }
            }
        }

        /// <summary>Выводит строку итога.</summary>
        private static void RenderTotalRow(
            TableDescriptor table,
            List<DataRow> rows,
            List<ClosedXmlColumn> visible,
            ClosedXmlTableSection section,
            ClosedXmlTotal total,
            string groupValue = "")
        {
            var dataTable = rows.Count > 0 ? rows[0].Table : null;
            if (dataTable == null)
                return;

            var sumFields = total.SumFields.Count > 0
                ? total.SumFields
                : visible.Where(column => IsNumericColumn(dataTable, column.Field))
                    .Select(column => column.Field)
                    .ToList();

            for (var index = 0; index < visible.Count; index++)
            {
                var columnLayout = visible[index];
                var text = string.Empty;

                if (index == 0)
                {
                    var label = ResolveTotalLabel(total, rows);

                    // К подытогу группы добавляется её значение: «ИТОГО по 3».
                    text = string.IsNullOrWhiteSpace(groupValue)
                        ? label
                        : $"{label} {groupValue}";
                }
                else if (IsSumField(sumFields, columnLayout, dataTable))
                {
                    text = SumColumn(rows, columnLayout, dataTable);
                }

                var bold = total.Bold;
                var align = ParseAlign(columnLayout.Align);
                var border = section.RowBorder;

                table.Cell()
                    .Element(container => ApplyCellContainer(container, string.Empty, border))
                    .PaddingVertical(1)
                    .PaddingHorizontal(3)
                    .Element(container => ApplyAlign(container, align))
                    .Text(textBlock =>
                    {
                        if (string.IsNullOrWhiteSpace(text))
                            return;

                        var textSpan = textBlock.Span(text);
                        if (bold)
                            textSpan.Bold();
                    });
            }
        }

        /// <summary>
        /// Метка итоговой строки: из данных, если задано поле метки, иначе из настройки.
        ///
        /// Так бланк печатает «Налог с продаж по ставке 0.00 %»: текст строки
        /// собирает набор данных, а не администратор.
        /// </summary>
        private static string ResolveTotalLabel(ClosedXmlTotal total, List<DataRow> rows)
        {
            if (string.IsNullOrWhiteSpace(total.LabelField) || rows.Count == 0)
                return total.Label;

            var value = GetFieldValueText(rows[0], total.LabelField);
            return string.IsNullOrWhiteSpace(value) ? total.Label : value;
        }

        /// <summary>Выводит блок подписей.</summary>
        private static void RenderSignatures(ColumnDescriptor column, ClosedXmlBlock block)
        {
            var signatures = block.Signatures
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .OrderBy(item => item.Column)
                .ToList();
            if (signatures.Count == 0)
                return;

            var maxColumn = signatures.Max(item => Math.Max(item.Column, item.MergeToColumn));

            column.Item().PaddingTop(12).Row(row =>
            {
                var cursor = 1;
                foreach (var signature in signatures)
                {
                    if (signature.Column > cursor)
                    {
                        row.RelativeItem(signature.Column - cursor);
                        cursor = signature.Column;
                    }

                    var span = signature.MergeToColumn >= signature.Column
                        ? signature.MergeToColumn - signature.Column + 1
                        : 1;
                    span = Math.Max(1, span);

                    var bold = signature.Bold;
                    var text = signature.Text;

                    row.RelativeItem(span).Text(textBlock =>
                    {
                        var textSpan = textBlock.Span(text);
                        if (bold)
                            textSpan.Bold();
                    });

                    cursor += span;
                }

                if (cursor <= maxColumn)
                    row.RelativeItem(maxColumn - cursor + 1);
            });
        }

        private static bool IsSumField(
            IReadOnlyList<string> sumFields,
            ClosedXmlColumn column,
            DataTable dataTable)
        {
            var target = ResolveColumnName(dataTable, column.Field);
            if (target == null)
                return false;

            foreach (var field in sumFields)
            {
                var resolved = ResolveColumnName(dataTable, field);
                if (resolved != null && string.Equals(resolved, target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Значение текстовой ячейки: из набора данных или собственный текст.</summary>
        /// <summary>
        /// Текст ячейки заголовка или свободной строки.
        ///
        /// Если задано поле <see cref="ClosedXmlTextCell.TakeFromField"/>, берётся
        /// значение из данных; иначе — сам текст с подстановкой параметров отчёта.
        /// </summary>
        private static string BuildTextCellValue(
            ClosedXmlTextCell cell,
            DataTable dataTable,
            IReadOnlyDictionary<string, object?>? parameters = null)
        {
            var field = cell.TakeFromField;
            if (!string.IsNullOrWhiteSpace(field))
            {
                var rowNumber = cell.TakeFromRow;
                if (rowNumber > 0 && rowNumber <= dataTable.Rows.Count)
                {
                    var row = dataTable.Rows[rowNumber - 1];
                    var resolved = ResolveColumnName(dataTable, field);
                    if (resolved != null)
                        return FormatValueText(row[resolved]);
                }
            }

            // Подстановки {Счёт}, {Период} и подобные: без них в документе
            // остались бы фигурные скобки.
            return ResolveText(cell.Text ?? string.Empty, parameters);
        }

        /// <summary>Значение ячейки таблицы с учётом формата колонки.</summary>
        private static string FormatCellValue(DataRow row, ClosedXmlColumn column)
        {
            var resolved = ResolveColumnName(row.Table, column.Field);
            if (resolved == null)
                return string.Empty;

            var value = row[resolved];
            if (value == null || value == DBNull.Value)
                return string.Empty;

            if (value is DateTime date)
                return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            if (value is decimal or double or float or int or long or short or byte)
            {
                var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (column.SuppressZero && number == 0m)
                    return string.Empty;

                return number.ToString("N2", CultureInfo.CurrentCulture);
            }

            return value.ToString() ?? string.Empty;
        }

        private static string SumColumn(List<DataRow> rows, ClosedXmlColumn column, DataTable dataTable)
        {
            var resolved = ResolveColumnName(dataTable, column.Field);
            if (resolved == null)
                return string.Empty;

            decimal sum = 0m;
            var hasValue = false;

            foreach (var row in rows)
            {
                var value = row[resolved];
                if (value == null || value == DBNull.Value)
                    continue;

                if (value is decimal or double or float or int or long or short or byte)
                {
                    sum += Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    hasValue = true;
                }
            }

            return hasValue ? sum.ToString("N2", CultureInfo.CurrentCulture) : string.Empty;
        }

        private static bool IsNumericColumn(DataTable dataTable, string? field)
        {
            var resolved = ResolveColumnName(dataTable, field);
            if (resolved == null)
                return false;

            var type = dataTable.Columns[resolved]!.DataType;
            return type == typeof(decimal) || type == typeof(double) || type == typeof(float) ||
                   type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
        }

        private static string FormatValueText(object? value) => value switch
        {
            null or DBNull => string.Empty,
            DateTime date => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            decimal number => number.ToString("N2", CultureInfo.CurrentCulture),
            double number => number.ToString("N2", CultureInfo.CurrentCulture),
            float number => number.ToString("N2", CultureInfo.CurrentCulture),
            _ => value.ToString() ?? string.Empty
        };

        /// <summary>Оформление ячейки: заливка и рамка.</summary>
        private static IContainer ApplyCellContainer(IContainer container, string? fill, string? border)
        {
            if (!string.IsNullOrWhiteSpace(fill))
                container = container.Background(ParseColor(fill));

            if (string.IsNullOrWhiteSpace(border) || border.Equals("None", StringComparison.OrdinalIgnoreCase))
                return container;

            return container.Border(0.5f).BorderColor(QuestPDF.Helpers.Colors.Black);
        }

        /// <summary>Выравнивание содержимого ячейки.</summary>
        private static IContainer ApplyAlign(IContainer container, string align) => align switch
        {
            "Center" => container.AlignCenter(),
            "Right" => container.AlignRight(),
            _ => container.AlignLeft()
        };

        private static string ParseColor(string? html)
        {
            var value = (html ?? string.Empty).Trim().TrimStart('#');
            return value.Length == 6 ? "#" + value.ToUpperInvariant() : QuestPDF.Helpers.Colors.White;
        }

        private static string ParseAlign(string? align) =>
            (align ?? "Left").ToUpperInvariant() switch
            {
                "CENTER" => "Center",
                "RIGHT" => "Right",
                _ => "Left"
            };

        /// <summary>Число колонок листа по макету.</summary>
        private static int ResolveColumnCount(ClosedXmlBlock block)
        {
            var fromCells = block.Rows
                .SelectMany(row => row.Cells)
                .Select(cell => Math.Max(cell.Column, cell.MergeToColumn))
                .DefaultIfEmpty(0)
                .Max();

            var fromTable = block.Table?.Columns.Count ?? 0;

            return Math.Max(Math.Max(fromCells, fromTable), 1);
        }
    }
}
