using BIS.ERP.Models;

namespace BIS.ERP.Services;

public static class AccountPairTurnoverLayout
{
    public static ClosedXmlReportLayout Create(string? name) => new()
    {
        Sheets =
        {
            new ClosedXmlSheet
            {
                Name = name ?? "Ведомость по паре",
                PageSetup = new ClosedXmlPageSetup
                {
                    Orientation = "Portrait", FitToPagesWide = 1, ShowGridLines = false,
                    FontName = "Times New Roman", FontSize = 9, FreezeRows = 5
                },
                Blocks =
                {
                    new ClosedXmlBlock
                    {
                        Type = "TitleRows", Name = "Шапка ведомости",
                        Rows =
                        {
                            new ClosedXmlTextRow { Row = 1, Cells = { new ClosedXmlTextCell
                            {
                                Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "report_title",
                                Bold = true, Align = "Center", WrapText = true
                            }}},
                            new ClosedXmlTextRow { Row = 2, Cells = { new ClosedXmlTextCell
                            {
                                Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "period_text", Align = "Center"
                            }}},
                            new ClosedXmlTextRow { Row = 3, Cells = { new ClosedXmlTextCell { Column = 1, Text = "" }}}
                        }
                    },
                    new ClosedXmlBlock
                    {
                        Type = "TabularSection", Name = "Движения и остатки по организациям",
                        Table = new ClosedXmlTableSection
                        {
                            Columns =
                            {
                                new ClosedXmlColumn { Field = "Дата", Header = "Дата", Width = 10 },
                                new ClosedXmlColumn { Field = "Документ", Header = "Документ", Width = 12 },
                                new ClosedXmlColumn { Field = "Дебет", Header = "Дебет", Width = 12 },
                                new ClosedXmlColumn { Field = "Кредит", Header = "Кредит", Width = 12 },
                                new ClosedXmlColumn { Field = "Сумма Дт", Header = "Сумма Дт", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Сумма Кт", Header = "Сумма Кт", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Сумма вал.", Header = "Сумма вал.", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Содержание", Header = "Содержание", Width = 40 },
                                new ClosedXmlColumn { Field = "Модуль", Header = "Модуль", Width = 10 }
                            },
                            HeaderFill = "", RowBorder = "All", SkipEmptyRows = false,
                            HeaderLevels =
                            {
                                new ClosedXmlHeaderLevel { Cells =
                                {
                                    new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "Дата" },
                                    new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Документ" },
                                    new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Дебет" },
                                    new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "Кредит" },
                                    new ClosedXmlHeaderCell { Column = 5, MergeToColumn = 6, Text = "ОБОРОТЫ — сом" },
                                    new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Сумма вал." },
                                    new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Содержание" },
                                    new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Модуль" }
                                }},
                                new ClosedXmlHeaderLevel { Cells =
                                {
                                    new ClosedXmlHeaderCell { Column = 5, Text = "Сумма Дт" },
                                    new ClosedXmlHeaderCell { Column = 6, Text = "Сумма Кт" }
                                }}
                            },
                            EmphasisRules =
                            {
                                new ClosedXmlEmphasisRule { Text = "Организация:", MergeLabelToColumn = 9, Bold = true, Align = "Center" },
                                new ClosedXmlEmphasisRule { Text = "ИТОГО", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "САЛЬДО", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "ОБОРОТЫ", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "Данных", MergeLabelToColumn = 9 }
                            }
                        }
                    }
                }
            }
        }
    };
}
