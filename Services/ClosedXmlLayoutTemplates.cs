using BIS.ERP.Models;
using System;
using System.Collections.Generic;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Готовые разметки ClosedXML-макета, построенные по РЕАЛЬНЫМ программным отчётам
    /// системы, а не «на глазок»:
    /// <list type="bullet">
    ///   <item>«Акт сверки» — по <c>ReconciliationActBuilder</c> и
    ///     <c>PrintFormService.BuildReconciliationActExcel</c>: 8 настоящих колонок,
    ///     плоский поток строк, выделение важных строк по содержимому;</item>
    ///   <item>«Оборотная ведомость по авансам» — колонки из
    ///     <c>AdvancePaymentsTurnoverReportService</c>.</item>
    /// </list>
    /// Класс намеренно не зависит от WPF: его используют и конструктор, и вывод отчёта.
    /// </summary>
    public static class ClosedXmlLayoutTemplates
    {
        /// <summary>Колонка набора акта, в которой лежат заголовок акта и период.</summary>
        public const string ActLabelField = "Наименование материала, вид операции";

        /// <summary>Заголовки акта — те же, что печатает программный экспорт.</summary>
        private static readonly string[] ActHeaders =
        {
            ActLabelField, "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "N докум", "Дата", "Модуль"
        };

        public static ClosedXmlReportLayout CreateBlank() => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = "Лист1",
                    PageSetup = new ClosedXmlPageSetup(),
                    Blocks = { CreateBlock("TitleRows", 1) }
                }
            }
        };

        /// <summary>Программный акт сверки — единственный отчёт этого вида.</summary>
        public static bool IsReconciliationAct(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("Акт сверки", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Разметка по умолчанию для отчёта: у программного акта сверки визуальный макет
        /// подставляется сразу, поэтому «программный» отчёт можно открыть и править мышью.
        /// </summary>
        public static ClosedXmlReportLayout CreateForReport(Report? report) =>
            IsReconciliationAct(report) ? CreateReconciliationAct(report!.Name) : CreateBlank();

        public static ClosedXmlReportLayout Create(string tag) => tag switch
        {
            "AdvanceTurnover" => CreateAdvanceTurnover(),
            _ => CreateReconciliationAct(null)
        };

        // =============================================================
        // Акт сверки — ТОЧНЫЙ перенос программного Excel
        // (PrintFormService.BuildReconciliationActExcel).
        //
        // Раскладка листа:
        //   строка 1  — «АКТ СВЕРКИ», объединение A:H, жирный, по центру
        //   строка 2  — заголовок акта из 1-й строки набора, объединение A:H, жирный, по центру
        //   строка 3  — период из 2-й строки набора, объединение A:H, по центру
        //   строка 4  — пустая
        //   строка 5  — шапка 8 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 6+ — тело: строки набора без «АКТ…», «Период…» и без итоговой фразы
        //   2 пустые строки и итоговая фраза задолженности (последняя строка набора), A:H, жирный
        // =============================================================
        public static ClosedXmlReportLayout CreateReconciliationAct(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Акт сверки" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 4,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка акта",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8, Text = "АКТ СВЕРКИ",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            TakeFromRow = 1, TakeFromField = ActLabelField,
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            TakeFromRow = 2, TakeFromField = ActLabelField,
                                            Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка 4: в программном Excel между периодом и шапкой пусто.
                                    // Объединение задано специально — иначе пустая ячейка не переживёт
                                    // сохранение в конструкторе (там строка собирается из непустых ячеек).
                                    Row = 4,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица акта",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = ActHeaders[0], Header = ActHeaders[0], Width = 44, Align = "Left" },
                                    new ClosedXmlColumn { Field = ActHeaders[1], Header = ActHeaders[1], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[2], Header = ActHeaders[2], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[3], Header = ActHeaders[3], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[4], Header = ActHeaders[4], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[5], Header = ActHeaders[5], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[6], Header = ActHeaders[6], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[7], Header = ActHeaders[7], Width = 12, Align = "Center" }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                // «АКТ…» и «Период…» уже в шапке, итог задолженности выводится отдельно,
                                // пустые строки набора программный экспорт тоже не печатает.
                                SkipRowsStartingWith = { "АКТ", "Период", "Задолженность" },
                                SkipEmptyRows = true,
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "Contains", Text = "Пара счетов", Bold = true },
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "САЛЬДО", Bold = true },
                                    new ClosedXmlEmphasisRule { Match = "Contains", Text = "ИТОГО", Bold = true }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Итог задолженности",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells = { new ClosedXmlTextCell { Column = 1, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells = { new ClosedXmlTextCell { Column = 1, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Итоговая фраза — последняя строка набора (TakeFromRow = -1).
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            TakeFromRow = -1, TakeFromField = ActLabelField,
                                            Bold = true
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // =============================================================
        // Оборотная ведомость по авансам: колонки программного отчёта
        // (Services/AdvancePaymentsTurnoverReportService.cs).
        // Многорядная шапка «ОБОРОТЫ - сом» и сальдо с арифметикой вне SUM
        // моделью пока не выражаются — см. Docs/ExcelReportsProgrammatic.md §13.
        // =============================================================
        public static ClosedXmlReportLayout CreateAdvanceTurnover() => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = "Оборотная ведомость",
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        ShowGridLines = true,
                        FontName = "Times New Roman",
                        FontSize = 10
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Заголовок",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            Text = "Оборотная ведомость синтетического и аналитического учета",
                                            Bold = true, FontSize = 14, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            Text = "по паре счетов {param.AccountPair} с {param.DateFrom} по {param.DateTo}",
                                            Align = "Center"
                                        }
                                    }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "Дата", Header = "Дата", Width = 11, Align = "Center" },
                                    new ClosedXmlColumn { Field = "Документ", Header = "Документ", Width = 11 },
                                    new ClosedXmlColumn { Field = "Счет", Header = "Счет", Width = 12 },
                                    new ClosedXmlColumn { Field = "Корр. счет", Header = "Корр. счет", Width = 12 },
                                    new ClosedXmlColumn { Field = "Дебет", Header = "Дебет", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "Кредит", Header = "Кредит", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "Содержание", Header = "Содержание", Width = 38 },
                                    new ClosedXmlColumn { Field = "Модуль", Header = "Модуль", Width = 10 }
                                },
                                HeaderFill = string.Empty,
                                HeaderBold = true,
                                HeaderWrapText = true,
                                RowBorder = "All",
                                Groups =
                                {
                                    new ClosedXmlGroup
                                    {
                                        ByFields = { "Аналитика" },
                                        ShowHeader = true,
                                        HeaderFormat = "{value}",
                                        HeaderBold = true,
                                        Subtotal = new ClosedXmlTotal { Label = "ИТОГО ПО", SumFields = { "Дебет", "Кредит" } }
                                    }
                                },
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "САЛЬДО", Bold = true, Italic = true },
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ОБОРОТЫ", Bold = true, Italic = true },
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ИТОГ", Bold = true }
                                }
                            }
                        }
                    }
                }
            }
        };

        public static ClosedXmlBlock CreateBlock(string type, int ordinal) => type switch
        {
            "TabularSection" => new ClosedXmlBlock
            {
                Type = "TabularSection",
                Name = $"Таблица {ordinal}",
                Table = new ClosedXmlTableSection
                {
                    Columns =
                    {
                        new ClosedXmlColumn { Field = "Поле1", Header = "Колонка 1", Width = 20 },
                        new ClosedXmlColumn { Field = "Поле2", Header = "Колонка 2", Width = 14, NumberFormat = "#,##0.00", Align = "Right" }
                    }
                }
            },
            "FreeRows" => new ClosedXmlBlock
            {
                Type = "FreeRows",
                Name = $"Строки {ordinal}",
                Rows =
                {
                    new ClosedXmlTextRow
                    {
                        Row = 1,
                        Cells = { new ClosedXmlTextCell { Column = 1, Text = "Текст" } }
                    }
                }
            },
            "SignaturesRow" => new ClosedXmlBlock
            {
                Type = "SignaturesRow",
                Name = $"Подписи {ordinal}",
                Signatures =
                {
                    new ClosedXmlSignature { Text = "Руководитель __________", Column = 1 },
                    new ClosedXmlSignature { Text = "Бухгалтер __________", Column = 5 }
                }
            },
            "PageBreak" => new ClosedXmlBlock
            {
                Type = "PageBreak",
                Name = $"Разрыв {ordinal}"
            },
            _ => new ClosedXmlBlock
            {
                Type = "TitleRows",
                Name = $"Заголовок {ordinal}",
                Rows =
                {
                    new ClosedXmlTextRow
                    {
                        Row = 1,
                        Cells =
                        {
                            new ClosedXmlTextCell
                            {
                                Column = 1,
                                MergeToColumn = 6,
                                Text = "Заголовок",
                                Bold = true,
                                FontSize = 14,
                                Align = "Center"
                            }
                        }
                    }
                }
            }
        };
    }
}
