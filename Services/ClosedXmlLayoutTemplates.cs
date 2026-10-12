using BIS.ERP.Models;
using System;
using System.Collections.Generic;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Готовые разметки ClosedXML-макета, построенные по РЕАЛЬНОМУ программному отчёту
    /// системы, а не «на глазок»:
    /// <list type="bullet">
    ///   <item>«Акт сверки» — по <c>ReconciliationActBuilder</c> и
    ///     <c>PrintFormService.BuildReconciliationActExcel</c>: 8 настоящих колонок,
    ///     плоский поток строк, выделение важных строк по содержимому.</item>
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

        /// <summary>Колонка набора ведомости, в которой лежат заголовок и период.</summary>
        public const string TrialBalanceTitleField = "report_title";

        /// <summary>Заголовки ведомости — те же, что печатает программный вывод.</summary>
        private static readonly string[] TrialBalanceHeaders =
        {
            "Счет", "Наименование",
            "Сальдо на начало Дт", "Сальдо на начало Кт",
            "Оборот Дт", "Оборот Кт",
            "Сальдо на конец Дт", "Сальдо на конец Кт"
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
        /// Разметка по умолчанию для отчёта: у акта сверки и оборотной ведомости
        /// визуальный макет подставляется сразу, поэтому «программный» отчёт
        /// можно открыть и править мышью.
        /// </summary>
        public static ClosedXmlReportLayout CreateForReport(Report? report)
        {
            if (IsReconciliationAct(report))
                return CreateReconciliationAct(report!.Name);

            if (IsTrialBalance(report))
                return CreateTrialBalance(report!.Name);

            if (IsInvoiceJournal(report))
                return CreateInvoiceJournal(report!.Name);

            return CreateBlank();
        }

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

        /// <summary>Признак оборотно-сальдовой ведомости.</summary>
        public static bool IsTrialBalance(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "TrialBalance", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("Оборот", StringComparison.OrdinalIgnoreCase));
        }

        // =============================================================
        // Оборотно-сальдовая ведомость — перенос программного вывода.
        //
        // Раскладка листа:
        //   строка 1  — наименование ведомости, объединение A:H, жирный, по центру
        //   строка 2  — период, объединение A:H, по центру
        //   строка 3  — шапка 8 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 4+ — тело: счета ведомости, последняя строка набора — «ИТОГО»
        // =============================================================
        public static ClosedXmlReportLayout CreateTrialBalance(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Оборотная ведомость" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 3,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка ведомости",
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
                                            TakeFromRow = 1, TakeFromField = TrialBalanceTitleField,
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
                                            TakeFromRow = 2, TakeFromField = TrialBalanceTitleField,
                                            Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка между периодом и шапкой таблицы.
                                    Row = 3,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица ведомости",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[0], Header = TrialBalanceHeaders[0], Width = 14, Align = "Left" },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[1], Header = TrialBalanceHeaders[1], Width = 40, Align = "Left" },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[2], Header = TrialBalanceHeaders[2], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[3], Header = TrialBalanceHeaders[3], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[4], Header = TrialBalanceHeaders[4], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[5], Header = TrialBalanceHeaders[5], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[6], Header = TrialBalanceHeaders[6], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[7], Header = TrialBalanceHeaders[7], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                SkipEmptyRows = true,
                                // Наименование ведомости и период уже выведены в шапке,
                                // и в теле таблицы они не печатаются.
                                SkipRowsStartingWith = { "Оборотно-сальдовая", "Оборотная", "Период" },
                                // Строка итогов выделяется жирным. Отдельной строки-заголовка
                                // у ведомости нет: заголовок и период выведены в шапке.
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ИТОГО", Bold = true }
                                }
                            }
                        }
                    }
                }
            }
        };

        /// <summary>Заголовки журнала счет-фактур.</summary>
        private static readonly string[] InvoiceJournalHeaders =
        {
            "Дата", "N докум", "Организация", "ЭСФ", "Вид документа",
            "Без налога", "НДС", "Налог с продаж", "Всего", "Состояние"
        };

        /// <summary>Признак журнала счет-фактур.</summary>
        public static bool IsInvoiceJournal(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "InvoiceJournal", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("счет-фактур", StringComparison.OrdinalIgnoreCase));
        }

        // =============================================================
        // Журнал счет-фактур — образец отчёта, собранного администратором.
        //
        // Раскладка листа:
        //   строка 1  — заголовок из набора, объединение A:J, жирный, по центру
        //   строка 2  — пустая
        //   строка 3  — шапка 10 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 4+ — записи журнала
        // =============================================================
        public static ClosedXmlReportLayout CreateInvoiceJournal(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Журнал счет-фактур" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 3,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Заголовок журнала",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 10,
                                            TakeFromRow = 1, TakeFromField = "report_title",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 10, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Журнал",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "doc_date", Header = InvoiceJournalHeaders[0], Width = 13, Align = "Center", NumberFormat = "dd.MM.yyyy" },
                                    new ClosedXmlColumn { Field = "doc_number", Header = InvoiceJournalHeaders[1], Width = 11, Align = "Center" },
                                    new ClosedXmlColumn { Field = "org_name", Header = InvoiceJournalHeaders[2], Width = 22, Align = "Left" },
                                    new ClosedXmlColumn { Field = "esf_number", Header = InvoiceJournalHeaders[3], Width = 14, Align = "Center" },
                                    new ClosedXmlColumn { Field = "document_kind", Header = InvoiceJournalHeaders[4], Width = 13, Align = "Center" },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = InvoiceJournalHeaders[5], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_total", Header = InvoiceJournalHeaders[6], Width = 13, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "sales_tax_total", Header = InvoiceJournalHeaders[7], Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "total_amount", Header = InvoiceJournalHeaders[8], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "posted_state", Header = InvoiceJournalHeaders[9], Width = 13, Align = "Center" }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                SkipEmptyRows = true,
                                // Строка «ИТОГО» строки набора не имеет: сумму считает запрос при необходимости.
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ИТОГО", Bold = true }
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
