using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>Вариант отчёта «Журнал проводок».</summary>
    public enum PostingsJournalVariant
    {
        Full,
        Brief,
        ByArticle,
        BySubaccount
    }

    /// <summary>
    /// Печать журнала проводок через визуальный макет ClosedXML.
    ///
    /// Раньше отчёт был захардкожен: код писал ячейки в XLWorkbook и сохранял файл.
    /// Теперь каждый вариант — обычная печатная форма (Report с ClosedXmlLayout),
    /// поэтому её можно открыть в конструкторе отчётов и поправить мышью,
    /// а вывод в Excel и PDF идёт одним и тем же макетом.
    /// </summary>
    public sealed class PostingsJournalPrintService
    {
        public const string CodePrefix = "standard.finance.postings-journal.";

        private readonly AppDbContext _context;

        public PostingsJournalPrintService(AppDbContext context)
        {
            _context = context;
        }

        public static string GetCode(PostingsJournalVariant variant) => CodePrefix + variant switch
        {
            PostingsJournalVariant.Brief => "brief",
            PostingsJournalVariant.ByArticle => "by-article",
            PostingsJournalVariant.BySubaccount => "by-subaccount",
            _ => "full"
        };

        public static string GetName(PostingsJournalVariant variant) => "Журнал проводок: " + GetKindTitle(variant);

        public static string GetKindTitle(PostingsJournalVariant variant) => variant switch
        {
            PostingsJournalVariant.Brief => "Краткая",
            PostingsJournalVariant.ByArticle => "Итоги по статьям",
            PostingsJournalVariant.BySubaccount => "Итоги по субсчетам",
            _ => "Полная"
        };

        public static PostingsJournalVariant ParseVariant(string? code) => code switch
        {
            var value when string.Equals(value, GetCode(PostingsJournalVariant.Brief), StringComparison.OrdinalIgnoreCase) => PostingsJournalVariant.Brief,
            var value when string.Equals(value, GetCode(PostingsJournalVariant.ByArticle), StringComparison.OrdinalIgnoreCase) => PostingsJournalVariant.ByArticle,
            var value when string.Equals(value, GetCode(PostingsJournalVariant.BySubaccount), StringComparison.OrdinalIgnoreCase) => PostingsJournalVariant.BySubaccount,
            _ => PostingsJournalVariant.Full
        };

        /// <summary>Создаёт/обновляет системные печатные формы журнала (макет не перетирается).</summary>
        public async Task EnsureAsync()
        {
            foreach (var variant in Enum.GetValues<PostingsJournalVariant>())
            {
                var code = GetCode(variant);
                var report = await _context.Reports.FirstOrDefaultAsync(item => item.Code == code);

                if (report == null)
                {
                    report = new Report
                    {
                        Id = Guid.NewGuid(),
                        Code = code,
                        Name = GetName(variant),
                        Description = "Системная печатная форма журнала проводок (ClosedXML).",
                        ReportType = "PostingsJournal",
                        SourceFormat = "ClosedXmlExcel",
                        IsSystem = true,
                        PageOrientation = "Landscape",
                        ShowGridLines = false
                    };
                    await _context.Reports.AddAsync(report);
                }

                report.IsPrintForm = true;
                report.IsActive = true;

                if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout))
                    report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(BuildLayout(variant));
            }

            await _context.SaveChangesAsync();
        }

        public async Task<List<Report>> GetPrintFormsAsync()
        {
            await EnsureAsync();

            var codes = Enum.GetValues<PostingsJournalVariant>().Select(GetCode).ToList();
            return await _context.Reports
                .AsNoTracking()
                .Where(report => codes.Contains(report.Code))
                .OrderBy(report => report.Name)
                .ToListAsync();
        }

        // ---------- данные ----------

        public static DataTable BuildTable(
            PostingsJournalVariant variant,
            IReadOnlyList<PostingViewModel> postings,
            DateTime startDate,
            DateTime endDate)
        {
            var table = new DataTable();
            table.Columns.Add("period_text", typeof(string));
            table.Columns.Add("kind_text", typeof(string));

            switch (variant)
            {
                case PostingsJournalVariant.Brief:
                    table.Columns.Add("date", typeof(DateTime));
                    table.Columns.Add("document", typeof(string));
                    table.Columns.Add("doc_type", typeof(string));
                    table.Columns.Add("module", typeof(string));
                    table.Columns.Add("debit", typeof(string));
                    table.Columns.Add("credit", typeof(string));
                    table.Columns.Add("count", typeof(int));
                    table.Columns.Add("amount", typeof(decimal));
                    table.Columns.Add("amount_currency", typeof(decimal));
                    table.Columns.Add("note", typeof(string));
                    break;

                case PostingsJournalVariant.ByArticle:
                    table.Columns.Add("article", typeof(string));
                    table.Columns.Add("count", typeof(int));
                    table.Columns.Add("amount", typeof(decimal));
                    table.Columns.Add("amount_currency", typeof(decimal));
                    break;

                case PostingsJournalVariant.BySubaccount:
                    table.Columns.Add("code", typeof(string));
                    table.Columns.Add("name", typeof(string));
                    table.Columns.Add("count", typeof(int));
                    table.Columns.Add("debit_amount", typeof(decimal));
                    table.Columns.Add("credit_amount", typeof(decimal));
                    break;

                default:
                    table.Columns.Add("date", typeof(DateTime));
                    table.Columns.Add("document", typeof(string));
                    table.Columns.Add("doc_type", typeof(string));
                    table.Columns.Add("module", typeof(string));
                    table.Columns.Add("debit", typeof(string));
                    table.Columns.Add("credit", typeof(string));
                    table.Columns.Add("amount", typeof(decimal));
                    table.Columns.Add("amount_currency", typeof(decimal));
                    table.Columns.Add("currency", typeof(string));
                    table.Columns.Add("organization", typeof(string));
                    table.Columns.Add("employee", typeof(string));
                    table.Columns.Add("note", typeof(string));
                    break;
            }

            var periodText = $"Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";
            var kindText = $"Вид отчёта: {GetKindTitle(variant)}";

            DataRow NewRow()
            {
                var row = table.NewRow();
                row["period_text"] = periodText;
                row["kind_text"] = kindText;
                return row;
            }

            switch (variant)
            {
                case PostingsJournalVariant.Brief:
                    foreach (var group in postings
                                 .GroupBy(posting => new
                                 {
                                     Date = posting.Date.Date,
                                     posting.DocumentNumber,
                                     posting.DocumentType,
                                     posting.ModuleName,
                                     posting.DebitAccount,
                                     posting.CreditAccount,
                                     Note = posting.Note ?? string.Empty
                                 })
                                 .OrderBy(group => group.Key.Date)
                                 .ThenBy(group => group.Key.DocumentNumber))
                    {
                        var row = NewRow();
                        row["date"] = group.Key.Date;
                        row["document"] = group.Key.DocumentNumber ?? string.Empty;
                        row["doc_type"] = group.Key.DocumentType ?? string.Empty;
                        row["module"] = group.Key.ModuleName ?? string.Empty;
                        row["debit"] = group.Key.DebitAccount ?? string.Empty;
                        row["credit"] = group.Key.CreditAccount ?? string.Empty;
                        row["count"] = group.Count();
                        row["amount"] = group.Sum(item => item.Amount);
                        row["amount_currency"] = group.Sum(item => item.AmountCurrency);
                        row["note"] = group.Key.Note;
                        table.Rows.Add(row);
                    }
                    break;

                case PostingsJournalVariant.ByArticle:
                    foreach (var group in postings
                                 .GroupBy(posting => string.IsNullOrWhiteSpace(posting.Note) ? "(без статьи)" : posting.Note.Trim())
                                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var row = NewRow();
                        row["article"] = group.Key;
                        row["count"] = group.Count();
                        row["amount"] = group.Sum(item => item.Amount);
                        row["amount_currency"] = group.Sum(item => item.AmountCurrency);
                        table.Rows.Add(row);
                    }
                    break;

                case PostingsJournalVariant.BySubaccount:
                    var debitRows = postings.Select(posting => new
                    {
                        Code = posting.DebitAccount,
                        Name = posting.DebitAccountName,
                        DebitAmount = posting.Amount,
                        CreditAmount = 0m
                    });
                    var creditRows = postings.Select(posting => new
                    {
                        Code = posting.CreditAccount,
                        Name = posting.CreditAccountName,
                        DebitAmount = 0m,
                        CreditAmount = posting.Amount
                    });

                    foreach (var group in debitRows
                                 .Concat(creditRows)
                                 .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                                 .GroupBy(item => item.Code!, StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        var row = NewRow();
                        row["code"] = group.Key;
                        row["name"] = group.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                        row["count"] = group.Count();
                        row["debit_amount"] = group.Sum(item => item.DebitAmount);
                        row["credit_amount"] = group.Sum(item => item.CreditAmount);
                        table.Rows.Add(row);
                    }
                    break;

                default:
                    foreach (var posting in postings.OrderBy(item => item.Date).ThenBy(item => item.DocumentNumber))
                    {
                        var row = NewRow();
                        row["date"] = posting.Date;
                        row["document"] = posting.DocumentNumber ?? string.Empty;
                        row["doc_type"] = posting.DocumentType ?? string.Empty;
                        row["module"] = posting.ModuleName ?? string.Empty;
                        row["debit"] = posting.DebitAccount ?? string.Empty;
                        row["credit"] = posting.CreditAccount ?? string.Empty;
                        row["amount"] = posting.Amount;
                        row["amount_currency"] = posting.AmountCurrency;
                        row["currency"] = posting.Currency ?? string.Empty;
                        row["organization"] = posting.Organization ?? string.Empty;
                        row["employee"] = posting.Employee ?? string.Empty;
                        row["note"] = posting.Note ?? string.Empty;
                        table.Rows.Add(row);
                    }
                    break;
            }

            return table;
        }

        // ---------- вывод ----------

        public static byte[] RenderExcel(Report report, DataTable table, DateTime startDate, DateTime endDate)
        {
            var layout = ParseLayout(report);
            var prepared = ClosedXmlReportRenderer.PrepareData(table, report);
            return ClosedXmlReportRenderer.RenderToBytes(layout, prepared, BuildParameters(startDate, endDate));
        }

        public static byte[] RenderPdf(Report report, DataTable table, DateTime startDate, DateTime endDate)
        {
            var layout = ParseLayout(report);
            var prepared = ClosedXmlReportRenderer.PrepareData(table, report);
            return ClosedXmlReportRenderer.RenderToPdfBytes(layout, prepared, BuildParameters(startDate, endDate));
        }

        private static ClosedXmlReportLayout ParseLayout(Report report)
        {
            var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out var error);
            if (layout == null)
                throw new InvalidOperationException(error);

            return layout;
        }

        private static IReadOnlyDictionary<string, object?> BuildParameters(DateTime startDate, DateTime endDate) =>
            new Dictionary<string, object?>
            {
                ["PeriodStart"] = startDate,
                ["PeriodEnd"] = endDate
            };

        // ---------- макет ----------

        private sealed record ColumnSpec(
            string Field,
            string Header,
            double Width,
            string NumberFormat = "",
            string Align = "Left");

        private static List<ColumnSpec> GetColumns(PostingsJournalVariant variant) => variant switch
        {
            PostingsJournalVariant.Brief => new List<ColumnSpec>
            {
                new("date", "Дата", 12, "dd.MM.yyyy", "Center"),
                new("document", "Документ", 18),
                new("doc_type", "Тип", 16),
                new("module", "Модуль", 14),
                new("debit", "Дебет", 10, "", "Center"),
                new("credit", "Кредит", 10, "", "Center"),
                new("count", "Кол-во", 8, "0", "Right"),
                new("amount", "Сумма", 15, "#,##0.00", "Right"),
                new("amount_currency", "Сумма вал.", 15, "#,##0.00", "Right"),
                new("note", "Содержание", 34)
            },

            PostingsJournalVariant.ByArticle => new List<ColumnSpec>
            {
                new("article", "Статья / содержание", 48),
                new("count", "Кол-во", 10, "0", "Right"),
                new("amount", "Оборот сом", 16, "#,##0.00", "Right"),
                new("amount_currency", "Оборот вал.", 16, "#,##0.00", "Right")
            },

            PostingsJournalVariant.BySubaccount => new List<ColumnSpec>
            {
                new("code", "Субсчет", 14, "", "Center"),
                new("name", "Наименование", 44),
                new("count", "Кол-во", 10, "0", "Right"),
                new("debit_amount", "Дебет оборот", 16, "#,##0.00", "Right"),
                new("credit_amount", "Кредит оборот", 16, "#,##0.00", "Right")
            },

            _ => new List<ColumnSpec>
            {
                new("date", "Дата", 12, "dd.MM.yyyy", "Center"),
                new("document", "Документ", 18),
                new("doc_type", "Тип", 16),
                new("module", "Модуль", 14),
                new("debit", "Дебет", 10, "", "Center"),
                new("credit", "Кредит", 10, "", "Center"),
                new("amount", "Сумма", 14, "#,##0.00", "Right"),
                new("amount_currency", "Сумма вал.", 14, "#,##0.00", "Right"),
                new("currency", "Валюта", 9, "", "Center"),
                new("organization", "Организация", 22),
                new("employee", "Сотрудник", 18),
                new("note", "Содержание", 30)
            }
        };

        private static List<string> GetSumFields(PostingsJournalVariant variant) => variant switch
        {
            PostingsJournalVariant.Brief => new List<string> { "count", "amount", "amount_currency" },
            PostingsJournalVariant.ByArticle => new List<string> { "count", "amount", "amount_currency" },
            PostingsJournalVariant.BySubaccount => new List<string> { "count", "debit_amount", "credit_amount" },
            _ => new List<string> { "amount", "amount_currency" }
        };

        private static ClosedXmlReportLayout BuildLayout(PostingsJournalVariant variant)
        {
            var columns = GetColumns(variant);
            var lastColumn = columns.Count;

            var sheet = new ClosedXmlSheet
            {
                Name = GetKindTitle(variant),
                LayoutKind = ClosedXmlLayoutKinds.Simple,
                PageSetup = new ClosedXmlPageSetup
                {
                    Orientation = "Landscape",
                    FitToPagesWide = 1,
                    ShowGridLines = false,
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
                                Cells =
                                {
                                    new ClosedXmlTextCell
                                    {
                                        Column = 1,
                                        MergeToColumn = lastColumn,
                                        Text = "Журнал проводок",
                                        Bold = true,
                                        FontSize = 14,
                                        Align = "Center"
                                    }
                                }
                            },
                            new ClosedXmlTextRow
                            {
                                Cells =
                                {
                                    new ClosedXmlTextCell
                                    {
                                        Column = 1,
                                        MergeToColumn = lastColumn,
                                        TakeFromRow = 1,
                                        TakeFromField = "period_text",
                                        Align = "Left"
                                    }
                                }
                            },
                            new ClosedXmlTextRow
                            {
                                Cells =
                                {
                                    new ClosedXmlTextCell
                                    {
                                        Column = 1,
                                        MergeToColumn = lastColumn,
                                        TakeFromRow = 1,
                                        TakeFromField = "kind_text",
                                        Align = "Left"
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
                            HeaderFill = "#DDEAF6",
                            RowBorder = "All",
                            Columns = columns
                                .Select(column => new ClosedXmlColumn
                                {
                                    Field = column.Field,
                                    Header = column.Header,
                                    Width = column.Width,
                                    NumberFormat = column.NumberFormat,
                                    Align = column.Align
                                })
                                .ToList(),
                            GrandTotal = new ClosedXmlTotal
                            {
                                Label = "ИТОГО",
                                SumFields = GetSumFields(variant),
                                Bold = true
                            }
                        }
                    }
                }
            };

            return new ClosedXmlReportLayout { Sheets = { sheet } };
        }
    }
}
