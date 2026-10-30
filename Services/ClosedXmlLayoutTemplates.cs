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
            ActLabelField, "Дата", "N докум", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Модуль"
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
        /// <summary>Признак счёта-фактуры по форме СТ-1.</summary>
        public static bool IsVatInvoiceSt1(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "VatInvoiceSt1", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("СТ-1", StringComparison.OrdinalIgnoreCase));
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

            if (IsVatInvoiceSt1(report))
                return CreateVatInvoiceSt1(report!.Name);

            if (IsCashReceiptOrder(report))
                return CreateCashReceiptOrder(report!.Name);

            if (IsCashPaymentOrder(report))
                return CreateCashPaymentOrder(report!.Name);

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
        /// <summary>Признак бланка расходного кассового ордера (КО-2).</summary>
        private static bool IsCashPaymentOrder(Report? report) =>
            report != null &&
            (report.Code ?? string.Empty).StartsWith("cash.payment.", StringComparison.OrdinalIgnoreCase);

        /// <summary>Признак бланка приходного кассового ордера (КО-1).</summary>
        private static bool IsCashReceiptOrder(Report? report) =>
            report != null &&
            (report.Code ?? string.Empty).StartsWith("cash.receipt.", StringComparison.OrdinalIgnoreCase);

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
                                    new ClosedXmlColumn { Field = ActHeaders[3], Header = ActHeaders[3], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[4], Header = ActHeaders[4], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[5], Header = ActHeaders[5], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[6], Header = ActHeaders[6], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
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
                        },
                        // Подписи сторон: слева — наша первичная организация, справа — контрагент.
                        // Наименования и расшифровки подписей приходят из карточек организаций,
                        // поэтому в макете стоят привязки, а не текст.
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Подписи сторон",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Руководитель", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 3, MergeToColumn = 4, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "our_organization"
                                        },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Руководитель", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 7, MergeToColumn = 8, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "counterparty"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 8, Text = "___________________________ подпись" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 4, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "our_director"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 5, MergeToColumn = 8, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "counterparty_director"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Гл. бухгалтер", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 3, MergeToColumn = 4, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "our_organization"
                                        },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Гл. бухгалтер", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 7, MergeToColumn = 8, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "counterparty"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 8, Text = "___________________________ подпись" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 4, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "our_accountant"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 5, MergeToColumn = 8, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "counterparty_accountant"
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

        /// <summary>Признак оборотной ведомости по счёту с группировкой.</summary>
        public static bool IsTurnoverByAccount(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "TurnoverByAccount", StringComparison.OrdinalIgnoreCase);
        }

        // =============================================================
        // Оборотная ведомость синтетического и аналитического учёта по счёту.
        //
        // Перенос печатной формы: проводки по счёту, сгруппированные по организации.
        //
        // Раскладка листа:
        //   строка 1  — «Оборотная ведомость синтетического и аналитического учета»
        //   строка 2  — «по счету … с … по …»
        //   строка 3  — пустая
        //   строка 4  — шапка: ОБОРОТЫ-сом над Дебет и Кредит (два уровня)
        //   тело      — заголовок организации, проводки, «ИТОГО по организации»
        //   конец     — общий итог по суммам
        //
        // Источник данных — системный расчёт system.movements с параметром счёта.
        // =============================================================
        public static ClosedXmlReportLayout CreateTurnoverByAccount(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Оборотная ведомость по счету" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
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
                                            Text = "Оборотная ведомость синтетического и аналитического учета",
                                            Bold = true, Align = "Center", FontSize = 12
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
                                            Text = "по счету {AccountCode} с {PeriodStart:dd/MM/yyyy} по {PeriodEnd:dd/MM/yyyy}",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Проводки по счету",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderFill = "#FFFFFF",
                                RowBorder = "All",
                                HeaderRowHeight = 34,

                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "posting_date", Header = "Дата", Width = 11, Align = "Center", NumberFormat = "dd/MM/yy" },
                                    new ClosedXmlColumn { Field = "doc_number", Header = "Документ", Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = "counter_account_code", Header = "Корр. счет", Width = 11, Align = "Center" },
                                    new ClosedXmlColumn { Field = "debit_amount", Header = "Дебет", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "credit_amount", Header = "Кредит", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "description", Header = "Содержание", Width = 34, Align = "Left" },
                                    new ClosedXmlColumn { Field = "currency_code", Header = "Валюта", Width = 10, Align = "Center" },
                                    new ClosedXmlColumn { Field = "amount_currency", Header = "Сумма в валюте", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "exchange_rate", Header = "Курс", Width = 10, Align = "Right", NumberFormat = "#,##0.0000", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "module_code", Header = "Код АРМ", Width = 9, Align = "Center" }
                                },

                                // Шапка: «ОБОРОТЫ-сом» перекрывает «Дебет» и «Кредит».
                                HeaderLevels =
                                {
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "Дата" },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Документ" },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Корр. счет" },
                                            new ClosedXmlHeaderCell { Column = 4, MergeToColumn = 5, Text = "(ОБОРОТЫ-сом)" },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "Содержание" },
                                            new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Валюта" },
                                            new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Сумма в валюте" },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Курс" },
                                            new ClosedXmlHeaderCell { Column = 10, RowSpan = 2, Text = "Код АРМ" }
                                        }
                                    },
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 4, Text = "Дебет" },
                                            new ClosedXmlHeaderCell { Column = 5, Text = "Кредит" }
                                        }
                                    }
                                },

                                // Группировка по организации: заголовок с номером и
                                // наименованием, затем проводки, затем итог группы.
                                Groups =
                                {
                                    new ClosedXmlGroup
                                    {
                                        // Заголовок группы: «1 ОсОО "Comtec-Soft"».
                                        // Подытог показывает номер: «ИТОГО по 1».
                                        ByFields = { "organization_code", "organization_id" },
                                        SubtotalValueField = "organization_code",
                                        ShowHeader = true,
                                        HeaderFormat = "{organization_code} {organization_id}",
                                        HeaderBold = true,
                                        Subtotal = new ClosedXmlTotal
                                        {
                                            Label = "ИТОГО по",
                                            SumFields = { "debit_amount", "credit_amount" },
                                            Bold = true
                                        }
                                    }
                                },

                                // Общий итог по суммам в конце ведомости.
                                GrandTotal = new ClosedXmlTotal
                                {
                                    Label = "ИТОГО",
                                    SumFields = { "debit_amount", "credit_amount" },
                                    Bold = true
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

        // =============================================================
        // Счёт-фактура по форме СТ-1 — образец форматного макета.
        //
        // Раскладка (число колонок = 13):
        //   строка 1   — «СЧЕТ-ФАКТУРА», A:M, жирный, по центру
        //   строка 2   — «(рег. N … от …)», A:M, по центру
        //   строки 3–7 — реквизиты: слева продавец, справа покупатель
        //   строки 8–9 — шапка 13 графов: два верхних уровня
        //   строка 10  — нижний уровень: номера и подписи графов
        //   строки 11+ — позиции счёта-фактуры
        //
        // Поворот подписей 90°: подписи графов стоят вертикально, как в бланке.
        // =============================================================
        public static ClosedXmlReportLayout CreateVatInvoiceSt1(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Счет-фактура СТ-1" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 10,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        // --- Заголовок формы ---
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Заголовок формы",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 13, Text = "СЧЕТ-ФАКТУРА",
                                            Bold = true, Align = "Center", FontSize = 12
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
                                            Column = 1, MergeToColumn = 13,
                                            Text = "по форме СТ-1 (рег. N 283 от 12.08.2026)",
                                            Align = "Center"
                                        }
                                    }
                                }
                            }
                        },

                        // --- Реквизиты: слева продавец, справа покупатель ---
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Реквизиты сторон",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            Text = "Продавец:", Bold = true, Align = "Left"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            Text = "Покупатель:", Bold = true, Align = "Left"
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
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_name"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_name"
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
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_inn_text"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_inn_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_address_text"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_address_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            Text = "Номер и дата регистрационного удостоверения:"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            Text = "Расчетный счет, банк:"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_reg_number"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "seller_bank_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка-разделитель перед таблицей.
                                    Row = 7,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 13, Text = string.Empty } }
                                }
                            }
                        },

                        // --- Таблица формы: 13 графов ---
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица СТ-1",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderRowHeight = 110,
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "line_number", Header = "N п/п", Width = 6, Align = "Center" },
                                    new ClosedXmlColumn { Field = "item_name", Header = "Наименование товаров, работ, услуг", Width = 30, Align = "Left" },
                                    new ClosedXmlColumn { Field = "unit_name", Header = "Ед. изм.", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "quantity", Header = "Кол-во", Width = 10, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "price", Header = "Цена", Width = 12, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = "Стоимость без налога", Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_rate", Header = "Ставка", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "vat_amount", Header = "Сумма", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "sales_tax_rate", Header = "Ставка", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "sales_tax_amount", Header = "Сумма", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "line_total", Header = "Всего", Width = 14, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "tax_code", Header = "Код", Width = 8, Align = "Center" },
                                    new ClosedXmlColumn { Field = "vat_tax_code", Header = "Код", Width = 8, Align = "Center" }
                                },
                                HeaderLevels =
                                {
                                    // Верхний уровень: названия групп графов.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "N п/п", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Наименование товаров, работ, услуг", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Ед. изм.", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "Кол-во", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 5, RowSpan = 2, Text = "Цена", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "Стоимость без налога", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 7, MergeToColumn = 8, RowSpan = 2, Text = "НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 9, MergeToColumn = 10, RowSpan = 2, Text = "Налог с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 11, RowSpan = 2, Text = "Всего", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 12, MergeToColumn = 13, RowSpan = 2, Text = "Код", TextRotation = 90 }
                                        }
                                    },
                                    // Средний уровень: подписи внутри групп занимают две
                                    // нижние строки — под ними стоят номера графов.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Ставка НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Сумма НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Ставка налога с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 10, RowSpan = 2, Text = "Сумма налога с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 12, RowSpan = 2, Text = "Код НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 13, RowSpan = 2, Text = "Код налога с продаж", TextRotation = 90 }
                                        }
                                    },
                                    // Нижний уровень: номера графов 1..13.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Номера граф",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, Text = "1", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 2, Text = "2", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 3, Text = "3", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 4, Text = "4", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 5, Text = "5", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 6, Text = "6", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 7, Text = "7", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 8, Text = "8", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 9, Text = "9", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 10, Text = "10", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 11, Text = "11", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 12, Text = "12", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 13, Text = "13", TextRotation = 0 }
                                        }
                                    }
                                },
                                HeaderFill = "#EFEFEF",
                                HeaderBold = true,
                                RowBorder = "All"
                            }
                        },

                        // --- Подписи ---
                        new ClosedXmlBlock
                        {
                            Type = "SignaturesRow",
                            Name = "Подписи",
                            Signatures =
                            {
                                new ClosedXmlSignature { Text = "Руководитель ______________", Column = 1, MergeToColumn = 4 },
                                new ClosedXmlSignature { Text = "Главный бухгалтер ______________", Column = 8, MergeToColumn = 13 }
                            }
                        }
                    }
                }
            }
        };

        // =============================================================
        // Счёт-фактура НДС (форма с РНИ/БИК, сомы) — образец бланка на одной странице.
        //
        // Раскладка (число колонок = 9, как графы бланка):
        //   шапка   — заголовок, 102 НОМЕР, 103 ДАТА, пары пунктов 201/301 … 206/306,
        //             401/403 и 404/405: шапка идёт до строк 404/405, ниже — таблица
        //             позиций этого документа;
        //   таблица — позиции счёта-фактуры, двухуровневая шапка НДС («НДС» → «ставка», «сумма»);
        //   итоги   — «ИТОГО:» по колонкам и «ВСЕГО:»;
        //   строки  — «Налог с продаж по ставке … %»: и метку, и сумму отдаёт набор данных;
        //   подписи — М.П., РУКОВОДИТЕЛЬ, ГЛАВНЫЙ БУХГАЛТЕР.
        //
        // Реквизитов покупателя и РНИ/БИК в документе нет: они подставляются
        // параметрами отчёта ({BuyerName}, {BuyerInn}, {SellerRni}, …) — администратор
        // задаёт их на вкладке «Параметры». Ячейки под цифры рисуются рамками,
        // а значение печатается целиком: разложить символы по клеткам макет не умеет.
        // =============================================================
        public static ClosedXmlReportLayout CreateVatInvoiceVatKg(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Счет-фактура НДС" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        // --- Шапка: от заголовка до строк 404/405 ---
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка бланка",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9, Text = "СЧЕТ-ФАКТУРА НДС",
                                            Bold = true, Align = "Center", FontSize = 12
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "102 НОМЕР:", Bold = true, Border = "All" },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 6, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 7, MergeToColumn = 9, Text = "103 ДАТА:", Bold = true, Border = "All" }
                                    }
                                },
                                // Дата печатается по клеткам: восемь клеток — день (2), месяц (2), год (4).
                                // Значения режет запрос (SUBSTRING), макет только раскладывает их по клеткам.
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, Text = "дата", Bold = true, Border = "All" },
                                        new ClosedXmlTextCell { Column = 2, TakeFromRow = 1, TakeFromField = "doc_day_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, TakeFromRow = 1, TakeFromField = "doc_day_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 4, TakeFromRow = 1, TakeFromField = "doc_month_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 5, TakeFromRow = 1, TakeFromField = "doc_month_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, TakeFromRow = 1, TakeFromField = "doc_year_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 7, TakeFromRow = 1, TakeFromField = "doc_year_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, TakeFromRow = 1, TakeFromField = "doc_year_3", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 9, TakeFromRow = 1, TakeFromField = "doc_year_4", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "ПОСТАВЩИК", Bold = true, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, Text = "ПОКУПАТЕЛЬ", Bold = true, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "201 ИНН", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_inn", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "301 ИНН", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_inn", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "202 Ф.И.О/наименование предприятия", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_name", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "302 Ф.И.О/наименование предприятия", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_name", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "203 адрес", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_address", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "303 адрес", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_address", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "204 код и наименование РНИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, Text = "{SellerRni}", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "304 код и наименование РНИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_reg_number", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "205 код (БИК) и наименование банка", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_bank_text", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "305 код (БИК) и наименование банка", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_bank", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "206 расчётный счёт", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_account", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "306 расчётный счёт", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_account", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "401 ДАТА И ТИП ПОСТАВКИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "supply_kind_text", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "403 СПОСОБ ПЛАТЕЖА", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "payment_kind_text", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "404 КОРРЕКТИРОВКА", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, Text = "к счет-фактуре серии ______ № ______ от ________", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "405 Причины корректировки", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, Text = "{CorrectionReason}", Border = "All", WrapText = true }
                                    }
                                }
                            }
                        },

                        // --- Таблица позиций: 9 графов, двухуровневая шапка НДС ---
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Позиции счёт-фактуры",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderRowHeight = 54,
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "group_code", Header = "код группы товаров", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "item_name", Header = "наименование товаров, работ(услуг)", Width = 30, Align = "Left" },
                                    new ClosedXmlColumn { Field = "unit_name", Header = "ед. изм.", Width = 7, Align = "Center" },
                                    new ClosedXmlColumn { Field = "quantity", Header = "количество (объем)", Width = 10, Align = "Right", NumberFormat = "#,##0.###" },
                                    new ClosedXmlColumn { Field = "price", Header = "цена (сом)", Width = 11, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = "стоимость товаров, работ, услуг без НДС", Width = 16, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_rate", Header = "ставка", Width = 8, Align = "Center", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "vat_amount", Header = "сумма(сом)", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "line_total", Header = "всего стоим. реализации (сом)", Width = 15, Align = "Right", NumberFormat = "#,##0.00" }
                                },
                                HeaderLevels =
                                {
                                    // Верхний уровень: графы, под которыми второго уровня нет,
                                    // занимают обе строки шапки — для этого у них RowSpan = 2.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "код группы товаров" },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "наименование товаров, работ(услуг)" },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "ед. изм." },
                                            new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "количество (объем)" },
                                            new ClosedXmlHeaderCell { Column = 5, RowSpan = 2, Text = "цена (сом)" },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "стоимость товаров, работ, услуг без НДС" },
                                            new ClosedXmlHeaderCell { Column = 7, MergeToColumn = 8, Text = "НДС" },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "всего стоим. реализации (сом)" }
                                        }
                                    },
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 7, Text = "ставка" },
                                            new ClosedXmlHeaderCell { Column = 8, Text = "сумма(сом)" }
                                        }
                                    }
                                },
                                // ИТОГО по колонкам бланка и отдельная строка ВСЕГО.
                                GrandTotal = new ClosedXmlTotal
                                {
                                    Label = "ИТОГО:",
                                    SumFields = { "amount_without_tax", "vat_amount", "line_total" }
                                },
                                ExtraTotals =
                                {
                                    new ClosedXmlTotal
                                    {
                                        Label = "ВСЕГО:",
                                        SumFields = { "line_total" }
                                    }
                                }
                            }
                        },

                        // --- «Налог с продаж по ставке … %»: метку и сумму отдаёт набор ---
                        new ClosedXmlBlock
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
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 6, TakeFromRow = 1, TakeFromField = "sales_tax_label" },
                                        new ClosedXmlTextCell { Column = 7, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "total_sales_tax", Align = "Right" }
                                    }
                                }
                            }
                        },

                        // --- Подписи ---
                        new ClosedXmlBlock
                        {
                            Type = "SignaturesRow",
                            Name = "Подписи",
                            Signatures =
                            {
                                new ClosedXmlSignature { Text = "М.П.", Column = 1, MergeToColumn = 2 },
                                new ClosedXmlSignature { Text = "РУКОВОДИТЕЛЬ ______________", Column = 3, MergeToColumn = 5 },
                                new ClosedXmlSignature { Text = "ГЛАВНЫЙ БУХГАЛТЕР ______________", Column = 6, MergeToColumn = 9 }
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

        /// <summary>
        /// Бланк «ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР» (КО-1) с отрывной квитанцией.
        ///
        /// Левая половина — сам ордер, правая — квитанция. Шапка бланка (наименование, ОКПО, ИНН)
        /// берётся из карточки первичной организации документа, остальные клетки — из полей набора
        /// данных ПКО: номер, дата, дебет/кредит, сумма, сумма прописью, принято от, основание;
        /// для квитанции дополнительно дата словами. Разметку удобно доводить в конструкторе.
        /// </summary>
        public static ClosedXmlReportLayout CreateCashReceiptOrder(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Приходный кассовый ордер" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Бланк КО-1",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "Унифицированная форма №КО-1", FontSize = 7, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Код", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "0310001", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, FontSize = 9, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "Утверждена постановлением Госкомстата КР", FontSize = 6 },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Форма по ОКУД", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "0310001", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 25, Text = "ОКПО", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 26, MergeToColumn = 32, TakeFromRow = 1, TakeFromField = "company_okpo" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "по ОКПО", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "company_okpo", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 25, Text = "ИНН", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 26, MergeToColumn = 32, TakeFromRow = 1, TakeFromField = "company_inn" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "ИНН", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "company_inn", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР", Bold = true, FontSize = 11, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "КВИТАНЦИЯ", Bold = true, FontSize = 12, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Номер документа", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "Дата составления", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 37, Text = "к приходному кассовому ордеру №", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "doc_number", Bold = true, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "ДЕБЕТ", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 10, Text = "КРЕДИТ", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 19, Text = "СУММА", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Принято от", Bold = true, FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "debit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 10, TakeFromRow = 1, TakeFromField = "credit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_text", Align = "Right", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "payer_name", Bold = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Принято от", Bold = true, FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "payer_name", Bold = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Основание", Bold = true, FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Основание", Bold = true, FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 13,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_text", Italic = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "amount_text", Italic = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 14,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Приложение ____________________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 15,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Главный бухгалтер _______________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 30, MergeToColumn = 40, Text = "М. П.", FontSize = 7, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 16,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Получил кассир __________________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "date_text", Bold = true, Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 17,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Главный бухгалтер ______________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 18,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Получил кассир __________________________", FontSize = 7 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };


        /// <summary>
        /// Бланк «РАСХОДНЫЙ КАССОВЫЙ ОРДЕР» (КО-2).
        ///
        /// Шапка бланка (наименование, ОКПО, подписи руководителя и главного бухгалтера) берётся
        /// из карточки первичной организации документа, остальные клетки — из полей набора данных
        /// кассовых ордеров: номер, дата, дебет/кредит, сумма, сумма прописью, получатель и основание.
        /// Разметку удобно доводить в конструкторе.
        /// </summary>
        public static ClosedXmlReportLayout CreateCashPaymentOrder(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Расходный кассовый ордер" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Бланк КО-2",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 7, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, FontSize = 9, WrapText = true },
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "Код", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, Text = "", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "формы по ОКУД", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, Text = "0310002", FontSize = 6, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "по ОКПО", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, TakeFromRow = 1, TakeFromField = "company_okpo", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 17, Text = "РАСХОДНЫЙ КАССОВЫЙ ОРДЕР", Bold = true, FontSize = 11, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 12, MergeToColumn = 14, Text = "Номер документа", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 17, Text = "Дата составления", FontSize = 6, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 12, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 17, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "Дебет: Код структурного подразделения", FontSize = 5, Align = "Center", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, Text = "Корреспондирующий счёт", FontSize = 5, Align = "Center", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, Text = "Кредит", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 15, Text = "Сумма", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 16, MergeToColumn = 18, Text = "Код целевого назначения", FontSize = 5, Align = "Center", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "debit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, TakeFromRow = 1, TakeFromField = "credit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 15, TakeFromRow = 1, TakeFromField = "amount_text", Align = "Right", Border = "All" },
                                        new ClosedXmlTextCell { Column = 16, MergeToColumn = 18, Text = "", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Выдать", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 4, TakeFromRow = 1, TakeFromField = "recipient_number", Align = "Center" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "recipient_name", Bold = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Основание", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Сумма", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "№ довер. ______________________ от", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 20, Text = "на имя ______________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 13,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "Руководитель", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "director_name", WrapText = true },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 20, Text = "расшифровка подписи", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 14,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "Гл. бухгалтер", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "accountant_name", WrapText = true },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 20, Text = "расшифровка подписи", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 15,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Получил", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 16,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, Text = "(сумма прописью)", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 17,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 10, Text = "«____» ____________________ 20___ г.", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 20, Text = "Получил ______________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 18,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "По", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 20, Text = "______________________________________________________________________________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 19,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 20, Text = "(наименование, номер, дата и место выдачи документа, удостоверяющего личность получателя)", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 20,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Выдал кассир", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 20, Text = "__________________________ расшифровка подписи", FontSize = 5 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

    }
}
