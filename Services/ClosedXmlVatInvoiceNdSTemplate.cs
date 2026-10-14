using BIS.ERP.Models;
using System.Collections.Generic;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Бланк «СЧЕТ-ФАКТУРА НДС» на узкой сетке листа.
    ///
    /// Сетка: 40 узких колонок по 2,5 единицы ≈ 190 мм — A4 книжная без
    /// масштабирования, в стандарте листа. Графы таблицы занимают по нескольку
    /// узких колонок, поэтому в шапке помещаются клетки по цифрам.
    ///
    /// Клетки рисуются по максимальной длине реквизита: ИНН — 14, расчётный счёт —
    /// 17, дата — 8. Пустые цифры остаются пустыми клетками с рамкой.
    ///
    /// Значения ИНН, расчётного счёта и даты приходят из набора данных уже
    /// разрезанными по знакам (SUBSTRING в SQL). Реквизиты, которых в документе
    /// нет (БИК и РНИ, вся правая сторона покупателя), печатаются одной ячейкой
    /// во всю клеточную линейку: параметр отчёта разрезать на знаки нельзя.
    /// </summary>
    public static class ClosedXmlVatInvoiceNdSTemplate
    {
        /// <summary>Сколько узких колонок делит лист.</summary>
        private const int GridColumns = 40;

        /// <summary>Ширина узкой колонки в единицах Excel: 40 × 2,5 = 100 ≈ 190 мм.</summary>
        private const double GridColumnWidth = 2.5;

        /// <summary>Левая половина бланка — поставщик, правая — покупатель.</summary>
        private const int BuyerColumn = 21;

        /// <summary>Значения левой половины начинаются с этой колонки.</summary>
        private const int SellerValue = 4;

        /// <summary>Значения правой половины начинаются с этой колонки.</summary>
        private const int BuyerValue = 24;

        /// <summary>Клетки даты: день, месяц, год — как в бланке 103 ДАТА.</summary>
        private static readonly string[] DateFields =
        {
            "doc_day_1", "doc_day_2", "doc_month_1", "doc_month_2",
            "doc_year_1", "doc_year_2", "doc_year_3", "doc_year_4"
        };

        public static ClosedXmlReportLayout Create(string? sheetName)
        {
            var sheet = new ClosedXmlSheet
            {
                Name = string.IsNullOrWhiteSpace(sheetName) ? "Счет-фактура НДС" : sheetName,
                LayoutKind = ClosedXmlLayoutKinds.Formatted,
                GridColumns = GridColumns,
                GridColumnWidth = GridColumnWidth,
                PageSetup = new ClosedXmlPageSetup
                {
                    Orientation = "Portrait",
                    FitToPagesWide = 1,
                    ShowGridLines = false,
                    FontName = "Times New Roman",
                    FontSize = 8
                }
            };

            sheet.Blocks.Add(BuildHeader());
            sheet.Blocks.Add(BuildTable());
            sheet.Blocks.Add(BuildSalesTax());
            sheet.Blocks.Add(BuildSignatures());

            return new ClosedXmlReportLayout { Version = 1, Sheets = { sheet } };
        }

        /// <summary>
        /// Шапка бланка: заголовок, 102 НОМЕР, 103 ДАТА по клеткам, стороны и
        /// пары пунктов 201/301 … 206/306, 401/403, 404/405. Ниже — таблица позиций.
        /// </summary>
        private static ClosedXmlBlock BuildHeader()
        {
            var block = new ClosedXmlBlock { Type = "TitleRows", Name = "Шапка бланка" };
            var row = 1;

            var title = new ClosedXmlTextRow { Row = row++ };
            title.Cells.Add(Cell(1, GridColumns, "СЧЕТ-ФАКТУРА НДС", bold: true, align: "Center", fontSize: 12));
            block.Rows.Add(title);

            var requisites = new ClosedXmlTextRow { Row = row++ };
            requisites.Cells.Add(Cell(1, 4, "102 НОМЕР:", bold: true, border: "All"));
            requisites.Cells.Add(Field(5, 10, "doc_number", "Center"));
            requisites.Cells.Add(Cell(11, 14, "103 ДАТА:", bold: true, border: "All"));

            // Дата — по клеткам бланка: день, месяц, год, цифра на клетку.
            requisites.Cells.Add(Cell(15, 16, "день", border: "All", wrap: true));
            for (var index = 0; index < 2; index++)
                requisites.Cells.Add(Field(17 + index, 17 + index, DateFields[index], "Center"));

            requisites.Cells.Add(Cell(19, 20, "месяц", border: "All", wrap: true));
            for (var index = 2; index < 4; index++)
                requisites.Cells.Add(Field(19 + index, 19 + index, DateFields[index], "Center"));

            requisites.Cells.Add(Cell(23, 24, "год", border: "All", wrap: true));
            for (var index = 4; index < 8; index++)
                requisites.Cells.Add(Field(21 + index, 21 + index, DateFields[index], "Center"));

            requisites.Cells.Add(Cell(29, GridColumns, string.Empty, border: "All"));
            block.Rows.Add(requisites);

            // Пары пунктов: слева наша компания, справа — покупатель (организация документа).
            block.Rows.Add(DigitsRow(row++, "201 ИНН", "seller_inn_", 14, "buyer_inn_", 14));
            block.Rows.Add(TextRow(row++, "202 Ф.И.О/наименование предприятия", "seller_name", "buyer_name"));
            block.Rows.Add(TextRow(row++, "203 адрес", "seller_address", "buyer_address"));
            block.Rows.Add(BoxRow(row++, "204 код и наименование РНИ", 3, "seller_reg_number", 3, "buyer_reg_number"));
            block.Rows.Add(BankRow(row++));
            block.Rows.Add(DigitsRow(row++, "206 расчётный счёт", "seller_account_", 17, "buyer_account_", 17));
            block.Rows.Add(TextRow(row++, "401 ДАТА И ТИП ПОСТАВКИ", "supply_kind_text", "{PaymentKind}", "403 СПОСОБ ПЛАТЕЖА", "payment_kind_text"));
            block.Rows.Add(TextRow(row, "404 КОРРЕКТИРОВКА", "к счет-фактуре серии ______ № ______ от ________",
                "{CorrectionReason}", "405 Причины корректировки"));

            return block;
        }

        /// <summary>
        /// Подпись пункта бланка: «ПОСТАВЩИК 201 ИНН». Шрифт мелкий:
        /// в бланке подпись узкая, а рядом — клетки по знакам.
        /// </summary>
        private static ClosedXmlTextCell Label(int from, int to, string text) => new()
        {
            Column = from,
            MergeToColumn = to > from ? to : 0,
            Text = text,
            FontSize = 6,
            Border = "All",
            WrapText = true
        };

        /// <summary>Строка пары пунктов, значения — одной ячейкой во всю половину.</summary>
        private static ClosedXmlTextRow TextRow(int row, string label, string sellerField, string buyerText,
            string buyerLabel = "", string? buyerField = null)
        {
            var textRow = new ClosedXmlTextRow { Row = row };
            textRow.Cells.Add(Label(1, 6, "ПОСТАВЩИК " + label));
            textRow.Cells.Add(Field(7, 20, sellerField));
            textRow.Cells.Add(Label(BuyerColumn, BuyerColumn + 5, "ПОКУПАТЕЛЬ " +
                (string.IsNullOrWhiteSpace(buyerLabel) ? label : buyerLabel)));
            textRow.Cells.Add(buyerField == null
                ? Cell(27, GridColumns, buyerText, wrap: true)
                : Field(27, GridColumns, buyerField, border: "All"));

            return textRow;
        }

        /// <summary>
        /// Строка пары пунктов с клетками по цифрам слева (ИНН, расчётный счёт).
        /// Правой стороне клетки не рисуются: её значение — параметр отчёта.
        /// </summary>
        private static ClosedXmlTextRow DigitsRow(int row, string label, string sellerFieldPrefix, int sellerCells,
            string buyerFieldPrefix, int buyerCells)
        {
            var digitsRow = new ClosedXmlTextRow { Row = row };

            // Подпись и клетки делят половину листа: клеток столько,
            // сколько знаков в реквизите, остальное — подпись.
            var sellerLabelTo = Math.Max(3, 20 - sellerCells);
            digitsRow.Cells.Add(Label(1, sellerLabelTo, "ПОСТАВЩИК " + label));

            for (var index = 0; index < sellerCells; index++)
            {
                var column = sellerLabelTo + 1 + index;
                digitsRow.Cells.Add(Field(column, column, $"{sellerFieldPrefix}{index + 1}", "Center"));
            }

            var sellerFillerFrom = sellerLabelTo + 1 + sellerCells;
            if (sellerFillerFrom <= 20)
                digitsRow.Cells.Add(Cell(sellerFillerFrom, 20, string.Empty, border: "All"));

            var buyerLabelTo = Math.Max(BuyerColumn, GridColumns - buyerCells);
            digitsRow.Cells.Add(Label(BuyerColumn, buyerLabelTo, "ПОКУПАТЕЛЬ " + label));

            // Клетки правой половины — из данных покупателя, по знаку на клетку.
            for (var index = 0; index < buyerCells; index++)
            {
                var column = buyerLabelTo + 1 + index;
                if (column > GridColumns)
                    break;

                digitsRow.Cells.Add(Field(column, column, $"{buyerFieldPrefix}{index + 1}", "Center"));
            }

            return digitsRow;
        }

        /// <summary>Строка 205/305: код (БИК) и наименование банка.</summary>
        private static ClosedXmlTextRow BankRow(int row)
        {
            var bankRow = new ClosedXmlTextRow { Row = row };
            bankRow.Cells.Add(Label(1, 5, "ПОСТАВЩИК 205 код (БИК) и наименование банка"));
            for (var index = 0; index < 6; index++)
                bankRow.Cells.Add(Field(6 + index, 6 + index, $"seller_bic_{index + 1}", "Center"));

            bankRow.Cells.Add(Field(12, 20, "seller_bank"));
            bankRow.Cells.Add(Label(BuyerColumn, BuyerColumn + 4, "ПОКУПАТЕЛЬ 305 код (БИК) и наименование банка"));
            for (var index = 0; index < 6; index++)
                bankRow.Cells.Add(Field(26 + index, 26 + index, $"buyer_bic_{index + 1}", "Center"));

            bankRow.Cells.Add(Field(32, GridColumns, "buyer_bank"));

            return bankRow;
        }

        /// <summary>Строка 204/304: клетки под код РНИ — три знака, как в бланке.</summary>
        private static ClosedXmlTextRow BoxRow(int row, string label, int sellerCells, string sellerField,
            int buyerCells, string buyerField)
        {
            var boxRow = new ClosedXmlTextRow { Row = row };
            boxRow.Cells.Add(Label(1, 5, "ПОСТАВЩИК " + label));
            for (var index = 0; index < sellerCells; index++)
                boxRow.Cells.Add(Cell(6 + index, 6 + index, string.Empty, border: "All"));

            boxRow.Cells.Add(Field(6 + sellerCells, 20, sellerField));
            boxRow.Cells.Add(Label(BuyerColumn, BuyerColumn + 4, "ПОКУПАТЕЛЬ " + label));
            for (var index = 0; index < buyerCells; index++)
                boxRow.Cells.Add(Cell(26 + index, 26 + index, string.Empty, border: "All"));

            boxRow.Cells.Add(Field(26 + buyerCells, GridColumns, buyerField));

            return boxRow;
        }

        /// <summary>Таблица позиций: девять графов, шапка из двух уровней, итоги.</summary>
        private static ClosedXmlBlock BuildTable()
        {
            var table = new ClosedXmlTableSection { HeaderRowHeight = 54 };
            table.Columns.AddRange(new[]
            {
                Column("group_code", "код группы товаров", 2, "Center"),
                Column("item_name", "наименование товаров, работ(услуг)", 12, "Left"),
                Column("unit_name", "ед. изм.", 2, "Center"),
                Column("quantity", "количество (объем)", 3, "Right", "#,##0.###"),
                Column("price", "цена (сом)", 3, "Right"),
                Column("amount_without_tax", "стоимость товаров, работ, услуг без НДС", 6, "Right"),
                Column("vat_rate", "ставка", 3, "Center", "#,##0.00"),
                Column("vat_amount", "сумма(сом)", 3, "Right"),
                Column("line_total", "всего стоим. реализации (сом)", 6, "Right")
            });

            // Графы, под которыми второго уровня нет, занимают обе строки шапки.
            table.HeaderLevels.Add(new ClosedXmlHeaderLevel
            {
                Name = "Уровень 1",
                Cells =
                {
                    Span(1, 2, "код группы товаров", rowSpan: 2),
                    Span(3, 14, "наименование товаров, работ(услуг)", rowSpan: 2),
                    Span(15, 16, "ед. изм.", rowSpan: 2),
                    Span(17, 19, "количество (объем)", rowSpan: 2),
                    Span(20, 22, "цена (сом)", rowSpan: 2),
                    Span(23, 28, "стоимость товаров, работ, услуг без НДС", rowSpan: 2),
                    Span(29, 34, "НДС"),
                    Span(35, 40, "всего стоим. реализации (сом)", rowSpan: 2)
                }
            });
            table.HeaderLevels.Add(new ClosedXmlHeaderLevel
            {
                Name = "Уровень 2",
                Cells =
                {
                    Span(29, 31, "ставка"),
                    Span(32, 34, "сумма(сом)")
                }
            });

            table.GrandTotal = new ClosedXmlTotal
            {
                Label = "ИТОГО:",
                SumFields = { "amount_without_tax", "vat_amount", "line_total" }
            };
            table.ExtraTotals.Add(new ClosedXmlTotal
            {
                Label = "ВСЕГО:",
                SumFields = { "line_total" }
            });

            return new ClosedXmlBlock { Type = "TabularSection", Name = "Позиции счёт-фактуры", Table = table };
        }

        /// <summary>Строка «Налог с продаж по ставке … %»: и метку, и сумму отдаёт набор.</summary>
        private static ClosedXmlBlock BuildSalesTax() => new()
        {
            Type = "FreeRows",
            Name = "Налог с продаж",
            Rows =
            {
                new ClosedXmlTextRow
                {
                    Row = 1,
                    Cells =
                    {
                        Field(1, 28, "sales_tax_label"),
                        Field(29, GridColumns, "total_sales_tax", "Right")
                    }
                }
            }
        };

        private static ClosedXmlBlock BuildSignatures() => new()
        {
            Type = "SignaturesRow",
            Name = "Подписи",
            Signatures =
            {
                new ClosedXmlSignature { Text = "М.П.", Column = 1, MergeToColumn = 8 },
                new ClosedXmlSignature { Text = "РУКОВОДИТЕЛЬ ______________", Column = 9, MergeToColumn = 20 },
                new ClosedXmlSignature { Text = "ГЛАВНЫЙ БУХГАЛТЕР ______________", Column = 21, MergeToColumn = GridColumns }
            }
        };

        private static ClosedXmlColumn Column(string field, string header, int span, string align,
            string numberFormat = "", bool suppressZero = true) => new()
        {
            Field = field,
            Header = header,
            Span = span,
            Align = align,
            NumberFormat = numberFormat,
            SuppressZero = suppressZero
        };

        private static ClosedXmlHeaderCell Span(int from, int to, string text, int rowSpan = 1) => new()
        {
            Column = from,
            MergeToColumn = to > from ? to : 0,
            RowSpan = rowSpan,
            Text = text
        };

        private static ClosedXmlTextCell Cell(int from, int to, string text, bool bold = false,
            string align = "Left", string border = "None", int fontSize = 0, bool wrap = false) => new()
        {
            Column = from,
            MergeToColumn = to > from ? to : 0,
            Text = text,
            Bold = bold,
            Align = align,
            Border = border,
            FontSize = fontSize,
            WrapText = wrap
        };

        /// <summary>Ячейка со значением из первой строки набора.</summary>
        private static ClosedXmlTextCell Field(int from, int to, string field, string align = "Left",
            string border = "All") => new()
        {
            Column = from,
            MergeToColumn = to > from ? to : 0,
            TakeFromRow = 1,
            TakeFromField = field,
            Align = align,
            Border = border
        };
    }
}
