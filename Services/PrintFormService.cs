using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;

namespace BIS.ERP.Services
{
    public class PrintFormService
    {
        private readonly AppDbContext _context;

        public PrintFormService(AppDbContext context)
        {
            _context = context;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task EnsureSchemaAsync()
        {
            const string sql = @"
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""Code"" varchar(100) NOT NULL DEFAULT '';
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsPrintForm"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsDefault"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""SourceFormat"" varchar(30) NOT NULL DEFAULT 'Native';
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""TemplateVersion"" integer NOT NULL DEFAULT 1;
                CREATE INDEX IF NOT EXISTS ""IX_Reports_PrintForms""
                    ON ""Reports"" (""DataSourceId"", ""IsPrintForm"", ""IsActive"");";
            await _context.Database.ExecuteSqlRawAsync(sql);
        }

        public async Task SeedCashOrderFormsAsync()
        {
            await EnsureSchemaAsync();
            var documents = await _context.MetadataObjects.AsNoTracking()
                .Where(item => item.ObjectType == "Document" &&
                    (item.Name == "Приходный кассовый ордер" || item.Name == "Расходный кассовый ордер"))
                .ToListAsync();
            var receipt = documents.FirstOrDefault(item => item.Name == "Приходный кассовый ордер");
            var payment = documents.FirstOrDefault(item => item.Name == "Расходный кассовый ордер");

            if (receipt != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.receipt.standard"))
                await _context.Reports.AddAsync(CreateCashForm(
                    "cash.receipt.standard", "Приходный кассовый ордер с квитанцией",
                    "CashReceiptOrder", receipt.Id, "ПКО и отрывная квитанция на одном листе"));
            if (payment != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.payment.standard"))
                await _context.Reports.AddAsync(CreateCashForm(
                    "cash.payment.standard", "Расходный кассовый ордер",
                    "CashPaymentOrder", payment.Id, "Унифицированная печатная форма РКО"));
            await _context.SaveChangesAsync();
        }

        public async Task<List<Report>> GetPrintFormsAsync(Guid metadataId, bool includeInactive = true)
        {
            await EnsureSchemaAsync();
            var query = _context.Reports.AsNoTracking()
                .Where(item => item.IsPrintForm && item.DataSourceId == metadataId);
            if (!includeInactive)
                query = query.Where(item => item.IsActive);
            return await query.OrderByDescending(item => item.IsDefault).ThenBy(item => item.Order).ThenBy(item => item.Name).ToListAsync();
        }

        public async Task SetAvailabilityAsync(Guid reportId, bool isActive)
        {
            await EnsureSchemaAsync();
            var report = await _context.Reports.FindAsync(reportId)
                ?? throw new InvalidOperationException("Печатная форма не найдена.");
            report.IsActive = isActive;
            report.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<byte[]> ExportDocumentAsync(Report report, Guid recordId)
        {
            if (!report.IsActive)
                throw new InvalidOperationException("Выбранная печатная форма отключена.");
            if (!report.DataSourceId.HasValue)
                throw new InvalidOperationException("У печатной формы не указан документ-источник.");

            var metadata = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.Id == report.DataSourceId.Value)
                ?? throw new InvalidOperationException("Документ-источник печатной формы не найден.");
            var metadataService = new MetadataService(_context);
            var rows = await metadataService.GetCatalogDataAsync(metadata.Id);
            var rawRow = rows.FirstOrDefault(row => Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var id) && id == recordId)
                ?? throw new InvalidOperationException("Документ для печати не найден.");
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(metadata, metadataService);
            var row = ReferenceDisplayHelper.ResolveRows(new[] { rawRow }, maps).Single();
            var data = await BuildCashOrderDataAsync(row, metadata.Name);

            if (report.SourceFormat == "FoxProFRX" && !string.IsNullOrWhiteSpace(report.Template))
                return BuildFoxProLayoutPdf(report, data);
            return metadata.Name == "Расходный кассовый ордер" || report.ReportType == "CashPaymentOrder"
                ? BuildCashPaymentPdf(report, data)
                : BuildCashReceiptPdf(report, data);
        }

        public byte[] ExportTemplatePreview(Report report)
        {
            var sample = new CashOrderPrintData
            {
                Number = "42", Date = DateTime.Today, Organization = "ОсОО Пример",
                Inn = "12345678901234", Okpo = "12345678", Person = "Иванов Иван Иванович",
                CashDesk = "Касса KGS", CorrespondentAccount = "11100000 - Денежные средства",
                Amount = 12345.67m,
                AmountInWords = "двенадцать тысяч триста сорок пять сом 67 тыйын",
                Basis = "Оплата согласно заявлению", Note = "Предпросмотр импортированного макета"
            };
            return report.SourceFormat == "FoxProFRX" && !string.IsNullOrWhiteSpace(report.Template)
                ? BuildFoxProLayoutPdf(report, sample)
                : report.ReportType == "CashPaymentOrder"
                    ? BuildCashPaymentPdf(report, sample)
                    : BuildCashReceiptPdf(report, sample);
        }

        private async Task<CashOrderPrintData> BuildCashOrderDataAsync(
            Dictionary<string, object> row,
            string documentName)
        {
            var amount = GetDecimal(row, "Сумма", "amount");
            var correspondentAccount = GetString(row, "Корр. счет", "correspondent_account");
            correspondentAccount = await ResolveAccountAsync(correspondentAccount);
            var data = new CashOrderPrintData
            {
                DocumentName = documentName,
                Number = GetString(row, "Номер", "Номер документа", "doc_number"),
                Date = GetDate(row, "Дата", "doc_date"),
                Organization = GetString(row, "Организация", "organization_id"),
                Person = GetString(row, "Сотрудник", "employee_id", "МОЛ", "responsible_person_id"),
                CashDesk = GetString(row, "Касса", "cash_desk_id"),
                CorrespondentAccount = correspondentAccount,
                Amount = amount,
                AmountInWords = RussianMoneyInWords(amount),
                Basis = GetString(row, "Основание", "basis"),
                Note = GetString(row, "Примечание", "description"),

                 // НОВЫЕ ПОЛЯ
             //   DebitAccount = debitAccount,
            //    CreditAccount = creditAccount,
           //    AmountInCurrency = amountInCurrency
            };
            var organizationValue = GetString(row, "organization_id");
            if (Guid.TryParse(organizationValue, out var organizationId))
            {
                var catalog = await _context.MetadataObjects.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
                if (catalog != null)
                {
                    var organizations = await new MetadataService(_context).GetCatalogDataAsync(catalog.Id);
                    var organization = organizations.FirstOrDefault(item =>
                        Guid.TryParse(item.GetValueOrDefault("Id")?.ToString(), out var id) && id == organizationId);
                    if (organization != null)
                    {
                        data.Organization = GetString(organization, "Полное наименование", "Наименование");
                        data.Inn = GetString(organization, "ИНН");
                        data.Okpo = GetString(organization, "ОКПО");
                    }
                }
            }
            return data;
        }

        private async Task<string> ResolveAccountAsync(string value)
        {
            if (!Guid.TryParse(value, out var id))
                return value;
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");
            if (catalog == null)
                return value;
            var rows = await new MetadataService(_context).GetCatalogDataAsync(catalog.Id);
            var account = rows.FirstOrDefault(row => Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var rowId) && rowId == id);
            return account == null ? value : $"{GetString(account, "Код")} - {GetString(account, "Наименование")}".Trim(' ', '-');
        }

        private static Report CreateCashForm(string code, string name, string type, Guid sourceId, string description) => new()
        {
            Code = code, Name = name, TitleText = name, Description = description,
            DataSourceType = "Document", DataSourceId = sourceId, ReportType = type,
            IsPrintForm = true, IsActive = true, IsDefault = true,
            SourceFormat = "Native", TemplateVersion = 1, Icon = "🖨", Order = 10,
            PageOrientation = type == "CashReceiptOrder" ? "Landscape" : "Portrait",
            ShowGridLines = true, ShowHeader = false, ShowFooter = false, ShowPageNumbers = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        private static byte[] BuildCashReceiptPdf(Report report, CashOrderPrintData data) =>
            QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(10, Unit.Millimetre);
                page.DefaultTextStyle(text => text.FontFamily("Arial").FontSize(9));
                page.Content().Row(row =>
                {
                    row.RelativeItem(1.6f).PaddingRight(8).Element(container => ComposeReceiptOrder(container, report, data, false));
                    row.ConstantItem(1).Background(Colors.Grey.Darken2);
                    row.RelativeItem(1).PaddingLeft(8).Element(container => ComposeReceiptOrder(container, report, data, true));
                });
            })).GeneratePdf();

        private static void ComposeReceiptOrder(IContainer container, Report report, CashOrderPrintData data, bool receiptCopy)
        {
            container.Column(column =>
            {
                column.Spacing(5);
                column.Item().AlignCenter().Text(receiptCopy ? "КВИТАНЦИЯ" : "ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР").Bold().FontSize(13);
                if (receiptCopy)
                    column.Item().AlignCenter().Text($"к приходному кассовому ордеру № {data.Number}");
                column.Item().Text(data.Organization).Bold();
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text($"Номер документа: {data.Number}");
                    row.RelativeItem().AlignRight().Text($"Дата: {data.Date:dd.MM.yyyy}");
                });
                if (!receiptCopy)
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn(1);
                        });
                        foreach (var title in new[] { "Дебет", "Кредит", "Сумма" })
                            table.Cell().Border(1).Padding(4).AlignCenter().Text(title).Bold();
                        table.Cell().Border(1).Padding(4).Text(data.CashDesk);
                        table.Cell().Border(1).Padding(4).Text(data.CorrespondentAccount);
                        table.Cell().Border(1).Padding(4).AlignRight().Text($"{data.Amount:N2}");
                    });
                column.Item().Text($"Принято от: {data.Person}");
                column.Item().Text($"Основание: {data.Basis}");
                column.Item().Text($"Сумма прописью: {data.AmountInWords}").Bold();
                if (!string.IsNullOrWhiteSpace(data.Note))
                    column.Item().Text($"Примечание: {data.Note}");
                if (!string.IsNullOrWhiteSpace(report.FooterText))
                    column.Item().Text(report.FooterText);
                column.Item().PaddingTop(12).Row(row =>
                {
                    row.RelativeItem().Text("Главный бухгалтер __________________");
                    row.RelativeItem().Text("Кассир __________________");
                });
            });
        }

        private static byte[] BuildCashPaymentPdf(Report report, CashOrderPrintData data) =>
            QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(15, Unit.Millimetre);
                page.DefaultTextStyle(text => text.FontFamily("Arial").FontSize(10));
                page.Content().Column(column =>
                {
                    column.Spacing(7);
                    column.Item().AlignCenter().Text("РАСХОДНЫЙ КАССОВЫЙ ОРДЕР").Bold().FontSize(15);
                    column.Item().Text(data.Organization).Bold();
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Номер документа: {data.Number}");
                        row.RelativeItem().AlignRight().Text($"Дата: {data.Date:dd.MM.yyyy}");
                    });
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn(1);
                        });
                        foreach (var title in new[] { "Дебет / корр. счет", "Кредит", "Сумма" })
                            table.Cell().Border(1).Padding(5).AlignCenter().Text(title).Bold();
                        table.Cell().Border(1).Padding(5).Text(data.CorrespondentAccount);
                        table.Cell().Border(1).Padding(5).Text(data.CashDesk);
                        table.Cell().Border(1).Padding(5).AlignRight().Text($"{data.Amount:N2}");
                    });
                    column.Item().Text($"Выдать: {data.Person}");
                    column.Item().Text($"Основание: {data.Basis}");
                    column.Item().Text($"Сумма прописью: {data.AmountInWords}").Bold();
                    if (!string.IsNullOrWhiteSpace(data.Note))
                        column.Item().Text($"Примечание: {data.Note}");
                    if (!string.IsNullOrWhiteSpace(report.FooterText))
                        column.Item().Text(report.FooterText);
                    column.Item().PaddingTop(18).Text("Руководитель ______________________________");
                    column.Item().Text("Главный бухгалтер _________________________");
                    column.Item().Text("Получил __________________  Подпись __________________");
                    column.Item().Text("Кассир _________________________________");
                });
            })).GeneratePdf();

        private static byte[] BuildFoxProLayoutPdf(Report report, CashOrderPrintData data)
        {
            var template = JsonSerializer.Deserialize<PrintFormTemplate>(report.Template)
                ?? throw new InvalidOperationException("Макет FoxPro поврежден.");
            var svg = BuildFoxProSvg(template, data);
            return QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(report.PageOrientation == "Landscape" ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(8, Unit.Millimetre);
                page.Content().Svg(svg).FitArea();
            })).GeneratePdf();
        }

        private static string BuildFoxProSvg(PrintFormTemplate template, CashOrderPrintData data)
        {
            var width = Math.Max(1000, template.PageWidth);
            var height = Math.Max(1000, template.PageHeight);
            var svg = new StringBuilder();
            svg.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {width.ToString(CultureInfo.InvariantCulture)} {height.ToString(CultureInfo.InvariantCulture)}'>");
            svg.Append("<rect x='0' y='0' width='100%' height='100%' fill='white'/>");
            svg.Append("<defs>");
            foreach (var element in template.Elements.Where(item => item.Type is "Text" or "Expression"))
            {
                var clipHeight = Math.Max(element.Height, Math.Max(700, element.FontSize * 105) * 1.4);
                svg.Append($"<clipPath id='clip{element.Order}'><rect x='{element.Left.ToString(CultureInfo.InvariantCulture)}' y='{element.Top.ToString(CultureInfo.InvariantCulture)}' width='{element.Width.ToString(CultureInfo.InvariantCulture)}' height='{clipHeight.ToString(CultureInfo.InvariantCulture)}'/></clipPath>");
            }
            svg.Append("</defs>");
            foreach (var element in template.Elements.OrderBy(item => item.Order))
            {
                var x = element.Left.ToString(CultureInfo.InvariantCulture);
                var y = element.Top.ToString(CultureInfo.InvariantCulture);
                var w = element.Width.ToString(CultureInfo.InvariantCulture);
                var h = element.Height.ToString(CultureInfo.InvariantCulture);
                if (element.Type == "Line")
                {
                    svg.Append($"<line x1='{x}' y1='{y}' x2='{(element.Left + element.Width).ToString(CultureInfo.InvariantCulture)}' y2='{(element.Top + element.Height).ToString(CultureInfo.InvariantCulture)}' stroke='black' stroke-width='80'/>");
                    continue;
                }
                if (element.Type == "Box")
                {
                    svg.Append($"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='none' stroke='black' stroke-width='80'/>");
                    continue;
                }
                if (element.Type == "Picture")
                    continue;

                var value = element.Type == "Expression"
                    ? EvaluateFoxExpression(element.Expression, data)
                    : UnquoteFoxText(element.Text);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var anchor = element.Alignment == "Right" ? "end" : element.Alignment == "Center" ? "middle" : "start";
                var textX = element.Alignment == "Right" ? element.Left + element.Width :
                    element.Alignment == "Center" ? element.Left + element.Width / 2 : element.Left;
                var fontSize = Math.Max(700, element.FontSize * 105);
                var fontWeight = element.Bold ? "bold" : "normal";
                var fontStyle = element.Italic ? "italic" : "normal";
                var lines = value.Replace("\r", string.Empty).Split('\n');
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var textY = element.Top + Math.Max(fontSize, element.Height * 0.75) + lineIndex * fontSize * 1.1;
                    svg.Append($"<text x='{textX.ToString(CultureInfo.InvariantCulture)}' y='{textY.ToString(CultureInfo.InvariantCulture)}' clip-path='url(#clip{element.Order})' text-anchor='{anchor}' font-family='{EscapeXml(element.FontName)}' font-size='{fontSize.ToString(CultureInfo.InvariantCulture)}' font-weight='{fontWeight}' font-style='{fontStyle}'>{EscapeXml(lines[lineIndex])}</text>");
                }
            }
            svg.Append("</svg>");
            return svg.ToString();
        }

        private static string EvaluateFoxExpression(string expression, CashOrderPrintData data)
        {
            if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
            var normalized = expression.Trim();
            var lower = normalized.ToLowerInvariant();
            if (lower.Contains("day(date") && lower.Contains("year(date"))
                return data.Date.ToString("dd MMMM yyyy 'г.'", new CultureInfo("ru-RU"));
            if (lower.Contains("msum1"))
            {
                const int defaultLineLength = 70;
                if (Regex.IsMatch(lower, @"\bleft\s*\(\s*msum1"))
                    return data.AmountInWords[..Math.Min(defaultLineLength, data.AmountInWords.Length)];
                var startMatch = Regex.Match(lower, @"subs?\(msum1\s*,\s*(\d+)");
                if (startMatch.Success)
                {
                    var start = Math.Max(0, int.Parse(startMatch.Groups[1].Value) - 1);
                    return start < data.AmountInWords.Length ? data.AmountInWords[start..] : string.Empty;
                }
                if (Regex.IsMatch(lower, @"subs?\s*\(\s*msum1\s*,\s*ll\d*\s*\+\s*1"))
                    return data.AmountInWords.Length > defaultLineLength
                        ? data.AmountInWords[defaultLineLength..]
                        : string.Empty;
                return data.AmountInWords;
            }
            if (lower.Contains("str(sum1") && lower.Contains("nval1"))
                return $"{data.Amount:N2} KGS";
            if (lower.Contains("tex1") && lower.Contains("fio"))
                return $"{data.Basis}   {data.Person}".Trim();
            if (lower.Contains("kodp1") && lower.Contains("namep1"))
                return data.Person;
            if (lower.Contains("iif(") && lower.Contains("sum"))
                return $"{data.Amount:N2}";

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nfil1"] = data.Organization, ["inn1"] = data.Inn, ["okpo1"] = data.Okpo,
                ["dok1"] = data.Number, ["date1"] = data.Date.ToString("dd.MM.yyyy"),
                ["deb1"] = GetAccountCode(data.DebitAccount ?? data.CashDesk),
                ["cred1"] = GetAccountCode(data.CreditAccount ?? data.CorrespondentAccount),
                ["deb"] = data.CashDesk, ["deb1"] = GetAccountCode(data.CorrespondentAccount),
                ["cred"] = GetAccountCode(data.CorrespondentAccount), ["cred1"] = data.CashDesk,
                ["sum"] = $"{data.Amount:N2}", ["sum1"] = $"{data.Amount:N2}", ["sum_v"] = $"{data.Amount:N2}",
                ["tex1"] = data.Basis, ["fio"] = data.Person, ["fiop1"] = data.Person,
                ["namep1"] = data.Person, ["kodp1"] = string.Empty, ["kurs_v"] = "1,00",
                ["nval1"] = "KGS", ["nakl1"] = string.Empty, ["dovn1"] = string.Empty,
                ["dovd1"] = string.Empty, ["dovf1"] = string.Empty
            };
            if (variables.TryGetValue(normalized, out var direct)) return direct;
            foreach (var (name, value) in variables.OrderByDescending(item => item.Key.Length))
                normalized = Regex.Replace(normalized, $@"\b{Regex.Escape(name)}\b", value.Replace("$", "$$"), RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"(?i)alltrim\(([^()]*)\)", "$1");
            normalized = Regex.Replace(normalized, @"(?i)str\(([^,()]+)(?:,[^()]*)?\)", "$1");
            normalized = normalized.Replace("'+'", string.Empty).Replace("+", string.Empty).Replace("'", string.Empty).Replace("\"", string.Empty);
            return normalized.Trim();
        }

        private static string UnquoteFoxText(string value)
        {
            var text = value?.Trim() ?? string.Empty;
            if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
                text = text[1..^1];
            return text.Replace("\r", string.Empty);
        }

        private static string GetAccountCode(string value)
        {
            var match = Regex.Match(value ?? string.Empty, @"\b\d{3,}\b");
            return match.Success ? match.Value : value;
        }

        private static string EscapeXml(string value) => (value ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

        private static string GetString(Dictionary<string, object> row, params string[] names)
        {
            foreach (var name in names)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static decimal GetDecimal(Dictionary<string, object> row, params string[] names) =>
            decimal.TryParse(GetString(row, names), out var value) ? value : 0;

        private static DateTime GetDate(Dictionary<string, object> row, params string[] names) =>
            DateTime.TryParse(GetString(row, names), out var value) ? value : DateTime.Today;

        private static string RussianMoneyInWords(decimal amount)
        {
            var whole = (long)Math.Floor(Math.Abs(amount));
            var coins = (int)Math.Round((Math.Abs(amount) - whole) * 100);
            return $"{NumberToWords(whole)} сом {coins:00} тыйын";
        }

        private static string NumberToWords(long value)
        {
            if (value == 0) return "ноль";
            var units = new[] { "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять", "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать" };
            var tens = new[] { "", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто" };
            var hundreds = new[] { "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот" };
            var parts = new List<string>();
            void AddTriplet(int number, string singular, string few, string many, bool feminine)
            {
                if (number == 0) return;
                parts.Add(hundreds[number / 100]);
                var rest = number % 100;
                if (rest < 20)
                {
                    var word = units[rest];
                    if (feminine && rest == 1) word = "одна";
                    if (feminine && rest == 2) word = "две";
                    parts.Add(word);
                }
                else
                {
                    parts.Add(tens[rest / 10]);
                    var unit = rest % 10;
                    var word = units[unit];
                    if (feminine && unit == 1) word = "одна";
                    if (feminine && unit == 2) word = "две";
                    parts.Add(word);
                }
                if (!string.IsNullOrEmpty(singular))
                {
                    var lastTwo = number % 100;
                    var last = number % 10;
                    parts.Add(lastTwo is >= 11 and <= 19 ? many : last == 1 ? singular : last is >= 2 and <= 4 ? few : many);
                }
            }
            AddTriplet((int)(value / 1_000_000), "миллион", "миллиона", "миллионов", false);
            AddTriplet((int)(value / 1000 % 1000), "тысяча", "тысячи", "тысяч", true);
            AddTriplet((int)(value % 1000), "", "", "", false);
            return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

       
        // Парсинг FRX-файла (макет FoxPro) с использованием FrxParser
        public async Task<string> ParseFrxFileAsync(byte[] fileData, string fileName)
        {
            try
            {
                // Создаем временный файл
                var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.frx");
                await File.WriteAllBytesAsync(tempFile, fileData);

                // Используем FrxParser
                var parser = new FrxParser();
                var frxReport = parser.ParseFrxFile(tempFile);

                // Удаляем временный файл
                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                // Получаем PrintFormTemplate из FrxReport
                var template = parser.GetPrintTemplate(frxReport);

                // Сериализуем в JSON
                var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                return json;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка парсинга FRX: {ex.Message}");
                return string.Empty;
            }
        }     

        private static string CleanFoxText(string input)
        {
            var cleaned = input;
            cleaned = cleaned.Replace("TEXT", "");
            cleaned = cleaned.Replace("LABEL", "");
            cleaned = cleaned.Replace("=", "");
            cleaned = cleaned.Replace("\"", "");
            cleaned = cleaned.Replace("'", "");
            cleaned = cleaned.Trim();

            while (cleaned.Contains("  "))
                cleaned = cleaned.Replace("  ", " ");

            return cleaned;
        }

        private static string ExtractFoxExpression(string input)
        {
            // Ищем выражение в скобках
            var startIdx = input.IndexOf('(');
            var endIdx = input.LastIndexOf(')');

            if (startIdx >= 0 && endIdx > startIdx)
            {
                var expr = input.Substring(startIdx + 1, endIdx - startIdx - 1);
                return CleanFoxText(expr);
            }

            // Ищем после "="
            var eqIdx = input.IndexOf('=');
            if (eqIdx >= 0 && eqIdx < input.Length - 1)
            {
                var expr = input.Substring(eqIdx + 1);
                return CleanFoxText(expr);
            }

            var result = CleanFoxText(input);
            if (result.Length > 0 && !result.Contains("EXPRESSION") && !result.Contains("FIELD"))
                return result;

            return string.Empty;
        }

        private sealed class CashOrderPrintData
        {
            public string DocumentName { get; init; } = string.Empty;
            public string Number { get; init; } = string.Empty;
            public DateTime Date { get; init; }
            public string Organization { get; set; } = string.Empty;
            public string Inn { get; set; } = string.Empty;
            public string Okpo { get; set; } = string.Empty;
            public string Person { get; init; } = string.Empty;
            public string CashDesk { get; init; } = string.Empty;
            public string CorrespondentAccount { get; init; } = string.Empty;

            // НОВЫЕ ПОЛЯ
            public string DebitAccount { get; set; } = string.Empty;
            public string CreditAccount { get; set; } = string.Empty;
            public decimal AmountInCurrency { get; set; }

            public decimal Amount { get; init; }
            public string AmountInWords { get; init; } = string.Empty;
            public string Basis { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
        }
    }
}
