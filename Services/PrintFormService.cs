using BIS.ERP.Data;
using BIS.ERP.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
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
        private static readonly object SchemaSyncLock = new();
        private static readonly HashSet<string> EnsuredSchemaKeys = new(StringComparer.OrdinalIgnoreCase);

        public PrintFormService(AppDbContext context)
        {
            _context = context;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task EnsureSchemaAsync()
        {
            var schemaKey = _context.Database.GetConnectionString() ?? "default";
            lock (SchemaSyncLock)
            {
                if (EnsuredSchemaKeys.Contains(schemaKey))
                    return;
            }

            const string sql = @"
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""Code"" varchar(100) NOT NULL DEFAULT '';
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsPrintForm"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsDefault"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""SourceFormat"" varchar(30) NOT NULL DEFAULT 'Native';
                ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""TemplateVersion"" integer NOT NULL DEFAULT 1;
                ALTER TABLE ""Reports"" ALTER COLUMN ""DataSourceType"" TYPE varchar(40);
                ALTER TABLE ""Reports"" ALTER COLUMN ""ReportType"" TYPE varchar(80);
                ALTER TABLE ""Reports"" ALTER COLUMN ""SourceFormat"" TYPE varchar(40);
                ALTER TABLE ""Reports"" ALTER COLUMN ""PageOrientation"" TYPE varchar(20);
                ALTER TABLE ""Reports"" ALTER COLUMN ""Icon"" TYPE varchar(20);
                ALTER TABLE ""Reports"" ALTER COLUMN ""Code"" TYPE varchar(120);
                CREATE TABLE IF NOT EXISTS ""ReportElementMappings"" (
                    ""Id"" uuid NOT NULL,
                    ""ReportId"" uuid NOT NULL,
                    ""ElementOrder"" integer NOT NULL DEFAULT 0,
                    ""ElementType"" text NOT NULL DEFAULT 'Text',
                    ""ElementText"" text NOT NULL DEFAULT '',
                    ""ElementExpression"" text NOT NULL DEFAULT '',
                    ""BandType"" text NOT NULL DEFAULT 'Detail',
                    ""Left"" double precision NOT NULL DEFAULT 0,
                    ""Top"" double precision NOT NULL DEFAULT 0,
                    ""Width"" double precision NOT NULL DEFAULT 0,
                    ""Height"" double precision NOT NULL DEFAULT 0,
                    ""FontName"" text NOT NULL DEFAULT 'Arial',
                    ""FontSize"" double precision NOT NULL DEFAULT 9,
                    ""Bold"" boolean NOT NULL DEFAULT false,
                    ""Italic"" boolean NOT NULL DEFAULT false,
                    ""Alignment"" text NOT NULL DEFAULT 'Left',
                    ""Order"" integer NOT NULL DEFAULT 0,
                    ""MappedFieldName"" text NULL,
                    ""MappedDisplayName"" text NULL,
                    ""DataSource"" text NOT NULL DEFAULT '',
                    ""FormatString"" text NOT NULL DEFAULT '',
                    ""IsVisible"" boolean NOT NULL DEFAULT true,
                    ""CustomText"" text NOT NULL DEFAULT '',
                    CONSTRAINT ""PK_ReportElementMappings"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_ReportElementMappings_Reports_ReportId"" FOREIGN KEY (""ReportId"")
                        REFERENCES ""Reports"" (""Id"") ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS ""FoxProReportFieldRules"" (
                    ""Id"" uuid NOT NULL,
                    ""ProfileCode"" varchar(120) NOT NULL DEFAULT '',
                    ""SourcePattern"" varchar(300) NOT NULL DEFAULT '',
                    ""CanonicalField"" varchar(120) NOT NULL DEFAULT '',
                    ""TargetFieldName"" varchar(300) NOT NULL DEFAULT '',
                    ""TargetDisplayName"" varchar(300) NOT NULL DEFAULT '',
                    ""IsRegex"" boolean NOT NULL DEFAULT false,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""Priority"" integer NOT NULL DEFAULT 100,
                    ""Description"" text NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""PK_FoxProReportFieldRules"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_Reports_PrintForms""
                    ON ""Reports"" (""DataSourceId"", ""IsPrintForm"", ""IsActive"");
                CREATE INDEX IF NOT EXISTS ""IX_ReportElementMappings_ReportId_ElementOrder""
                    ON ""ReportElementMappings"" (""ReportId"", ""ElementOrder"");
                CREATE INDEX IF NOT EXISTS ""IX_FoxProReportFieldRules_SourcePattern""
                    ON ""FoxProReportFieldRules"" (""SourcePattern"");
                CREATE INDEX IF NOT EXISTS ""IX_FoxProReportFieldRules_ProfileCode_IsActive""
                    ON ""FoxProReportFieldRules"" (""ProfileCode"", ""IsActive"");";
            await _context.Database.ExecuteSqlRawAsync(sql);
            lock (SchemaSyncLock)
            {
                EnsuredSchemaKeys.Add(schemaKey);
            }
        }

        public async Task SeedCashOrderFormsAsync()
        {
            await EnsureSchemaAsync();
            var cashOrder = await _context.MetadataObjects.AsNoTracking()
                .Include(metadata => metadata.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" &&
                    (item.Name == "Расходный/Приходный КО" || item.TableName == "doc_cash_orders"));

            if (cashOrder == null)
                return;

            var existingReports = await _context.Reports
                .Where(item => item.Code.StartsWith("cash.receipt.") || item.Code.StartsWith("cash.payment."))
                .ToListAsync();

            void UpsertCashReport(
                string code,
                string name,
                string titleText,
                string description,
                string reportType,
                string sourceFormat,
                string pageOrientation,
                int order,
                bool isDefault,
                string template)
            {
                var report = existingReports.FirstOrDefault(item => item.Code == code);
                if (report == null)
                {
                    report = new Report
                    {
                        Id = Guid.NewGuid(),
                        Code = code,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Reports.Add(report);
                }

                report.Name = name;
                report.TitleText = titleText;
                report.Description = description;
                report.DataSourceType = "Document";
                report.DataSourceId = cashOrder.Id;
                report.ReportType = reportType;
                report.IsPrintForm = true;
                report.IsActive = true;
                report.IsDefault = isDefault;
                report.SourceFormat = sourceFormat;
                report.TemplateVersion = 1;
                report.Icon = "🖨";
                report.Order = order;
                report.PageOrientation = pageOrientation;
                report.ShowGridLines = false;
                report.ShowHeader = false;
                report.ShowFooter = false;
                report.ShowPageNumbers = false;
                report.Template = template;
                report.UpdatedAt = DateTime.UtcNow;
            }

            UpsertCashReport(
                "cash.receipt.foxpro",
                "Приходный кассовый ордер с квитанцией",
                "Приходный кассовый ордер",
                "ПКО и отрывная квитанция на одном листе",
                "FoxProLayout",
                "FoxProFRX",
                "Landscape",
                10,
                true,
                JsonSerializer.Serialize(GenerateCashReceiptFrxTemplate(cashOrder), new JsonSerializerOptions { WriteIndented = true }));

            UpsertCashReport(
                "cash.payment.foxpro",
                "Расходный кассовый ордер",
                "Расходный кассовый ордер",
                "Унифицированная печатная форма РКО",
                "FoxProLayout",
                "FoxProFRX",
                "Portrait",
                10,
                true,
                JsonSerializer.Serialize(GenerateCashPaymentFrxTemplate(cashOrder), new JsonSerializerOptions { WriteIndented = true }));

            UpsertCashReport(
                "cash.receipt.native",
                "Приходный кассовый ордер (нативный)",
                "Приходный кассовый ордер",
                "Настраиваемая нативная форма ПКО с квитанцией",
                "CashReceiptOrder",
                "Native",
                "Landscape",
                20,
                false,
                JsonSerializer.Serialize(CreateDefaultNativeTemplate("CashReceiptOrder"), new JsonSerializerOptions { WriteIndented = true }));

            UpsertCashReport(
                "cash.payment.native",
                "Расходный кассовый ордер (нативный)",
                "Расходный кассовый ордер",
                "Настраиваемая нативная форма РКО",
                "CashPaymentOrder",
                "Native",
                "Portrait",
                20,
                false,
                JsonSerializer.Serialize(CreateDefaultNativeTemplate("CashPaymentOrder"), new JsonSerializerOptions { WriteIndented = true }));

            await _context.SaveChangesAsync();
        }

        public async Task SeedInvoiceFormsAsync()
        {
            await EnsureSchemaAsync();
            var documents = await _context.MetadataObjects.AsNoTracking()
                .Include(m => m.Fields)
                .Where(item => item.ObjectType == "Document" &&
                    (item.Name == InvoiceDocumentTypes.SalesIssue || item.Name == InvoiceDocumentTypes.PurchaseRegistration))
                .ToListAsync();

            foreach (var document in documents.OrderBy(item => item.Name))
            {
                var isSales = InvoiceDocumentTypes.IsSales(document.Name);
                var code = isSales ? "invoice.sales.foxpro" : "invoice.purchase.foxpro";
                if (await _context.Reports.AnyAsync(item => item.Code == code))
                    continue;

                var template = GenerateInvoiceFrxTemplate(document, isSales);
                await _context.Reports.AddAsync(new Report
                {
                    Code = code,
                    Name = isSales ? "Счет-фактура на реализацию" : "Счет-фактура полученная",
                    TitleText = "Счет-фактура",
                    Description = isSales
                        ? "Печатная форма выписки счет-фактуры"
                        : "Печатная форма регистрации полученной счет-фактуры",
                    DataSourceType = "Document",
                    DataSourceId = document.Id,
                    ReportType = "FoxProLayout",
                    IsPrintForm = true,
                    IsActive = true,
                    IsDefault = true,
                    SourceFormat = "FoxProFRX",
                    TemplateVersion = 1,
                    Icon = "🧾",
                    Order = 30,
                    PageOrientation = "Portrait",
                    ShowGridLines = false,
                    ShowHeader = false,
                    ShowFooter = false,
                    ShowPageNumbers = false,
                    Template = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        private static PrintFormTemplate GenerateCashReceiptFrxTemplate(MetadataObject document)
        {
            var fields = document.Fields.ToList();
            var template = new PrintFormTemplate
            {
                SourceFormat = "FoxProFRX",
                PageWidth = 2970,
                PageHeight = 2100,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "PageHeader", Top = 0, Height = 120, Order = 0 },
                    new() { Type = "Detail", Top = 120, Height = 300, Order = 1 },
                    new() { Type = "Summary", Top = 420, Height = 100, Order = 2 }
                },
                Elements = new List<PrintFormElement>
                {
                    new() { Type = "Text", Text = "ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР", BandType = "PageHeader", Left = 200, Top = 20, Width = 2500, Height = 60, FontName = "Arial", FontSize = 18, Bold = true, Alignment = "Center", Order = 0 },
                    new() { Type = "Text", Text = "Номер документа: {Номер}", BandType = "Detail", Left = 200, Top = 100, Width = 1200, Height = 40, FontName = "Arial", FontSize = 11, Order = 1 },
                    new() { Type = "Text", Text = "Дата: {Дата}", BandType = "Detail", Left = 1500, Top = 100, Width = 1200, Height = 40, FontName = "Arial", FontSize = 11, Alignment = "Right", Order = 2 },
                    new() { Type = "Text", Text = "Принято от: {Сотрудник}", BandType = "Detail", Left = 200, Top = 160, Width = 2500, Height = 40, FontName = "Arial", FontSize = 11, Order = 3 },
                    new() { Type = "Text", Text = "Основание: {Основание}", BandType = "Detail", Left = 200, Top = 220, Width = 2500, Height = 40, FontName = "Arial", FontSize = 11, Order = 4 },
                    new() { Type = "Text", Text = "Сумма: {Сумма} KGS", BandType = "Detail", Left = 200, Top = 280, Width = 2500, Height = 40, FontName = "Arial", FontSize = 12, Bold = true, Order = 5 },
                    new() { Type = "Text", Text = "Главный бухгалтер __________________", BandType = "Summary", Left = 200, Top = 10, Width = 1200, Height = 30, FontName = "Arial", FontSize = 10, Order = 6 },
                    new() { Type = "Text", Text = "Кассир __________________", BandType = "Summary", Left = 1500, Top = 10, Width = 1200, Height = 30, FontName = "Arial", FontSize = 10, Order = 7 }
                }
            };
            return template;
        }

        private static PrintFormTemplate GenerateCashPaymentFrxTemplate(MetadataObject document)
        {
            var fields = document.Fields.ToList();
            var template = new PrintFormTemplate
            {
                SourceFormat = "FoxProFRX",
                PageWidth = 2100,
                PageHeight = 2970,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "PageHeader", Top = 0, Height = 120, Order = 0 },
                    new() { Type = "Detail", Top = 120, Height = 350, Order = 1 },
                    new() { Type = "Summary", Top = 470, Height = 120, Order = 2 }
                },
                Elements = new List<PrintFormElement>
                {
                    new() { Type = "Text", Text = "РАСХОДНЫЙ КАССОВЫЙ ОРДЕР", BandType = "PageHeader", Left = 200, Top = 20, Width = 1700, Height = 60, FontName = "Arial", FontSize = 18, Bold = true, Alignment = "Center", Order = 0 },
                    new() { Type = "Text", Text = "Номер документа: {Номер}", BandType = "Detail", Left = 200, Top = 100, Width = 800, Height = 40, FontName = "Arial", FontSize = 11, Order = 1 },
                    new() { Type = "Text", Text = "Дата: {Дата}", BandType = "Detail", Left = 1100, Top = 100, Width = 800, Height = 40, FontName = "Arial", FontSize = 11, Alignment = "Right", Order = 2 },
                    new() { Type = "Text", Text = "Выдать: {Сотрудник}", BandType = "Detail", Left = 200, Top = 170, Width = 1700, Height = 40, FontName = "Arial", FontSize = 11, Order = 3 },
                    new() { Type = "Text", Text = "Основание: {Основание}", BandType = "Detail", Left = 200, Top = 230, Width = 1700, Height = 40, FontName = "Arial", FontSize = 11, Order = 4 },
                    new() { Type = "Text", Text = "Сумма: {Сумма} KGS", BandType = "Detail", Left = 200, Top = 290, Width = 1700, Height = 40, FontName = "Arial", FontSize = 12, Bold = true, Order = 5 },
                    new() { Type = "Text", Text = "Руководитель __________________", BandType = "Summary", Left = 200, Top = 20, Width = 800, Height = 30, FontName = "Arial", FontSize = 10, Order = 6 },
                    new() { Type = "Text", Text = "Главный бухгалтер __________________", BandType = "Summary", Left = 200, Top = 60, Width = 800, Height = 30, FontName = "Arial", FontSize = 10, Order = 7 },
                    new() { Type = "Text", Text = "Получил __________________", BandType = "Summary", Left = 1000, Top = 20, Width = 800, Height = 30, FontName = "Arial", FontSize = 10, Order = 8 }
                }
            };
            return template;
        }

        private static PrintFormTemplate GenerateInvoiceFrxTemplate(MetadataObject document, bool isSales)
        {
            var elements = new List<PrintFormElement>();
            void Add(string type, string text, string expression, double left, double top, double width, double height,
                double fontSize = 9, bool bold = false, string align = "Left")
            {
                elements.Add(new PrintFormElement
                {
                    Type = type,
                    Text = text,
                    Expression = expression,
                    BandType = top < 520 ? "PageHeader" : top < 2050 ? "Detail" : "Summary",
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    FontName = "Arial",
                    FontSize = fontSize,
                    Bold = bold,
                    Alignment = align,
                    BorderStyle = type == "Box" ? "Solid" : "None",
                    Order = elements.Count
                });
            }

            Add("Box", "", "", 70, 70, 1960, 2550);
            Add("Text", "СЧЕТ-ФАКТУРА", "", 120, 110, 1860, 70, 16, true, "Center");
            Add("Text", isSales ? "Выписка счет-фактуры" : "Регистрация счет-фактуры", "", 120, 185, 1860, 50, 10, false, "Center");
            Add("Text", "Номер:", "", 120, 280, 170, 45, 10, true);
            Add("Expression", "", "number", 295, 280, 300, 45, 10, true);
            Add("Text", "Дата:", "", 1180, 280, 150, 45, 10, true);
            Add("Expression", "", "date", 1340, 280, 300, 45, 10);
            Add("Text", "Номер ЭСФ:", "", 120, 345, 240, 45, 10, true);
            Add("Expression", "", "esf_number", 370, 345, 450, 45, 10);
            Add("Text", "Организация:", "", 120, 410, 260, 45, 10, true);
            Add("Expression", "", "organization", 390, 410, 1420, 45, 10);
            Add("Text", "Счет:", "", 120, 475, 160, 45, 10, true);
            Add("Expression", "", "counterparty_account", 290, 475, 500, 45, 10);
            Add("Text", "Основание:", "", 860, 475, 220, 45, 10, true);
            Add("Expression", "", "basis", 1090, 475, 800, 45, 10);

            const double tableLeft = 120;
            const double tableTop = 610;
            const double rowHeight = 105;
            var widths = new[] { 80d, 620d, 240d, 250d, 210d, 210d, 250d };
            var headers = new[] { "№", "Наименование", "Счет", "Без налогов", "НДС", "Налог", "Итого" };
            Add("Box", "", "", tableLeft, tableTop, widths.Sum(), rowHeight * 10);
            var x = tableLeft;
            for (var column = 0; column < widths.Length; column++)
            {
                Add("Text", headers[column], "", x + 8, tableTop + 22, widths[column] - 16, 45, 8.5, true, column == 1 ? "Left" : "Center");
                if (column > 0)
                    Add("Line", "", "", x, tableTop, 0, rowHeight * 10);
                x += widths[column];
            }
            for (var row = 1; row <= 9; row++)
                Add("Line", "", "", tableLeft, tableTop + rowHeight * row, widths.Sum(), 0);

            for (var row = 1; row <= 8; row++)
            {
                var y = tableTop + rowHeight * row + 22;
                Add("Text", row.ToString(CultureInfo.InvariantCulture), "", tableLeft + 10, y, widths[0] - 20, 40, 8.5, false, "Center");
                Add("Expression", "", $"line{row}_name", tableLeft + widths[0] + 10, y, widths[1] - 20, 40, 8.5);
                Add("Expression", "", $"line{row}_account_name", tableLeft + widths[0] + widths[1] + 10, y, widths[2] - 20, 40, 8.5);
                Add("Expression", "", $"line{row}_amount_without_tax", tableLeft + widths[0] + widths[1] + widths[2] + 10, y, widths[3] - 20, 40, 8.5, false, "Right");
                Add("Expression", "", $"line{row}_vat", tableLeft + widths[0] + widths[1] + widths[2] + widths[3] + 10, y, widths[4] - 20, 40, 8.5, false, "Right");
                Add("Expression", "", $"line{row}_sales_tax", tableLeft + widths[0] + widths[1] + widths[2] + widths[3] + widths[4] + 10, y, widths[5] - 20, 40, 8.5, false, "Right");
                Add("Expression", "", $"line{row}_total", tableLeft + widths[0] + widths[1] + widths[2] + widths[3] + widths[4] + widths[5] + 10, y, widths[6] - 20, 40, 8.5, false, "Right");
            }

            Add("Text", "Итого без налогов:", "", 1100, 1690, 420, 45, 10, true);
            Add("Expression", "", "amount_without_tax", 1530, 1690, 330, 45, 10, true, "Right");
            Add("Text", "Сумма НДС:", "", 1100, 1760, 420, 45, 10, true);
            Add("Expression", "", "vat_total", 1530, 1760, 330, 45, 10, true, "Right");
            Add("Text", "Налог с продаж:", "", 1100, 1830, 420, 45, 10, true);
            Add("Expression", "", "sales_tax_total", 1530, 1830, 330, 45, 10, true, "Right");
            Add("Text", "Всего к оплате:", "", 1100, 1900, 420, 50, 11, true);
            Add("Expression", "", "total_amount", 1530, 1900, 330, 50, 11, true, "Right");
            Add("Text", "Сумма прописью:", "", 120, 2070, 380, 45, 10, true);
            Add("Expression", "", "amount_in_words", 120, 2135, 1760, 110, 10);
            Add("Text", "Руководитель __________________________", "", 120, 2360, 760, 45, 10, true);
            Add("Text", "Главный бухгалтер _____________________", "", 1030, 2360, 780, 45, 10, true);

            return new PrintFormTemplate
            {
                SourceFormat = "FoxProFRX",
                OriginalFileName = $"{document.TableName}.frx",
                PageWidth = 2100,
                PageHeight = 2970,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "PageHeader", Top = 0, Height = 540, Order = 0 },
                    new() { Type = "Detail", Top = 540, Height = 1510, Order = 1 },
                    new() { Type = "Summary", Top = 2050, Height = 620, Order = 2 }
                },
                Elements = elements
            };
        }

        public static PrintFormTemplate CreateDefaultNativeTemplate(string reportType)
        {
            return reportType == "CashPaymentOrder"
                ? CreateDefaultNativePaymentTemplate()
                : CreateDefaultNativeReceiptTemplate();
        }

        public static PrintFormTemplate CreateBlankNativeTemplate()
        {
            return new PrintFormTemplate
            {
                SourceFormat = "Native",
                OriginalFileName = "NativeDesigner",
                PageWidth = 2100,
                PageHeight = 2970,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "Header", Top = 0, Height = 420, Order = 0 },
                    new() { Type = "Body", Top = 420, Height = 1900, Order = 1 },
                    new() { Type = "Footer", Top = 2320, Height = 520, Order = 2 }
                },
                Elements = new List<PrintFormElement>()
            };
        }

        private static PrintFormTemplate CreateDefaultNativeReceiptTemplate()
        {
            var elements = new List<PrintFormElement>();
            void Add(string type, string text, string expression, double left, double top, double width, double height,
                double fontSize = 9, bool bold = false, string align = "Left", string band = "Body")
            {
                elements.Add(new PrintFormElement
                {
                    Type = type,
                    Text = text,
                    Expression = expression,
                    BandType = band,
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    FontName = "Arial",
                    FontSize = fontSize,
                    Bold = bold,
                    Alignment = align,
                    BorderStyle = type == "Box" ? "Solid" : "None",
                    Order = elements.Count
                });
            }

            Add("Box", "", "", 40, 40, 1420, 1720, 9, false);
            Add("Box", "", "", 1510, 40, 1420, 1720, 9, false);
            Add("Line", "", "", 1485, 40, 0, 1720, 9, false);
            Add("Expression", "", "organization", 90, 80, 1200, 70, 10, true);
            Add("Text", "ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР N", "", 90, 180, 760, 70, 13, true);
            Add("Expression", "", "number", 860, 180, 260, 70, 13, true, "Center");
            Add("Text", "от", "", 1150, 180, 70, 70, 10);
            Add("Expression", "", "date", 1230, 180, 210, 70, 10);
            Add("Box", "", "", 90, 300, 1320, 180, 9, false);
            Add("Text", "Дебет", "", 120, 325, 250, 50, 9, true, "Center");
            Add("Text", "Кредит", "", 480, 325, 250, 50, 9, true, "Center");
            Add("Text", "Сумма", "", 920, 325, 250, 50, 9, true, "Center");
            Add("Expression", "", "debit_account", 120, 395, 330, 60, 9);
            Add("Expression", "", "credit_account", 480, 395, 390, 60, 9);
            Add("Expression", "", "amount", 920, 395, 240, 60, 9, false, "Right");
            Add("Text", "Принято от:", "", 90, 560, 260, 55, 10, true);
            Add("Expression", "", "person", 360, 560, 1020, 55, 10);
            Add("Text", "Основание:", "", 90, 650, 260, 55, 10, true);
            Add("Expression", "", "basis", 360, 650, 1020, 90, 10);
            Add("Text", "Сумма прописью:", "", 90, 790, 360, 55, 10, true);
            Add("Expression", "", "amount_in_words", 90, 860, 1290, 140, 10);
            Add("Text", "Главный бухгалтер", "", 90, 1180, 420, 55, 10, true);
            Add("Line", "", "", 520, 1230, 380, 0, 9, false);
            Add("Text", "Кассир", "", 90, 1300, 420, 55, 10, true);
            Add("Line", "", "", 520, 1350, 380, 0, 9, false);
            Add("Text", "КВИТАНЦИЯ", "", 1510, 180, 1420, 70, 13, true, "Center");
            Add("Text", "к приходному кассовому ордеру N", "", 1580, 280, 670, 60, 10);
            Add("Expression", "", "number", 2260, 280, 230, 60, 10, true, "Center");
            Add("Expression", "", "organization", 1580, 390, 1200, 70, 10, true);
            Add("Text", "Принято от:", "", 1580, 520, 260, 55, 10, true);
            Add("Expression", "", "person", 1850, 520, 960, 55, 10);
            Add("Text", "Основание:", "", 1580, 620, 260, 55, 10, true);
            Add("Expression", "", "basis", 1850, 620, 960, 90, 10);
            Add("Text", "Сумма:", "", 1580, 760, 180, 55, 10, true);
            Add("Expression", "", "amount", 1770, 760, 280, 55, 10, true, "Right");
            Add("Text", "Сумма прописью:", "", 1580, 850, 360, 55, 10, true);
            Add("Expression", "", "amount_in_words", 1580, 920, 1230, 140, 10);
            Add("Text", "Главный бухгалтер", "", 1580, 1230, 420, 55, 10, true);
            Add("Line", "", "", 2040, 1280, 420, 0, 9, false);
            Add("Text", "Кассир", "", 1580, 1360, 420, 55, 10, true);
            Add("Line", "", "", 2040, 1410, 420, 0, 9, false);

            return new PrintFormTemplate
            {
                SourceFormat = "Native",
                PageWidth = 2970,
                PageHeight = 2100,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "Header", Top = 0, Height = 260, Order = 0 },
                    new() { Type = "Body", Top = 260, Height = 1160, Order = 1 },
                    new() { Type = "Footer", Top = 1420, Height = 420, Order = 2 }
                },
                Elements = elements
            };
        }

        private static PrintFormTemplate CreateDefaultNativePaymentTemplate()
        {
            var elements = new List<PrintFormElement>();
            void Add(string type, string text, string expression, double left, double top, double width, double height,
                double fontSize = 9, bool bold = false, string align = "Left", string band = "Body")
            {
                elements.Add(new PrintFormElement
                {
                    Type = type,
                    Text = text,
                    Expression = expression,
                    BandType = band,
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    FontName = "Arial",
                    FontSize = fontSize,
                    Bold = bold,
                    Alignment = align,
                    BorderStyle = type == "Box" ? "Solid" : "None",
                    Order = elements.Count
                });
            }

            Add("Box", "", "", 70, 70, 1960, 2600, 9, false);
            Add("Expression", "", "organization", 120, 110, 1280, 70, 10, true);
            Add("Text", "РАСХОДНЫЙ КАССОВЫЙ ОРДЕР", "", 120, 230, 1680, 80, 15, true, "Center");
            Add("Text", "Номер документа", "", 120, 360, 360, 60, 9, true);
            Add("Expression", "", "number", 500, 360, 270, 60, 10, true, "Center");
            Add("Text", "Дата составления", "", 1120, 360, 360, 60, 9, true);
            Add("Expression", "", "date", 1500, 360, 260, 60, 10, false, "Center");
            Add("Box", "", "", 120, 500, 1780, 220, 9, false);
            Add("Text", "Дебет", "", 150, 530, 330, 60, 9, true, "Center");
            Add("Text", "Кредит", "", 540, 530, 330, 60, 9, true, "Center");
            Add("Text", "Сумма", "", 980, 530, 330, 60, 9, true, "Center");
            Add("Expression", "", "debit_account", 150, 620, 360, 60, 9);
            Add("Expression", "", "credit_account", 540, 620, 360, 60, 9);
            Add("Expression", "", "amount", 980, 620, 300, 60, 9, false, "Right");
            Add("Text", "Выдать:", "", 120, 820, 210, 60, 10, true);
            Add("Expression", "", "person", 350, 820, 1460, 60, 10);
            Add("Text", "Основание:", "", 120, 930, 260, 60, 10, true);
            Add("Expression", "", "basis", 390, 930, 1420, 110, 10);
            Add("Text", "Сумма:", "", 120, 1090, 210, 60, 10, true);
            Add("Expression", "", "amount", 350, 1090, 320, 60, 10, true, "Right");
            Add("Text", "Сумма прописью:", "", 120, 1200, 420, 60, 10, true);
            Add("Expression", "", "amount_in_words", 120, 1270, 1690, 160, 10);
            Add("Text", "Руководитель", "", 120, 1580, 360, 60, 10, true);
            Add("Line", "", "", 520, 1630, 560, 0, 9, false);
            Add("Text", "Главный бухгалтер", "", 120, 1710, 430, 60, 10, true);
            Add("Line", "", "", 560, 1760, 520, 0, 9, false);
            Add("Text", "Получил", "", 120, 1900, 260, 60, 10, true);
            Add("Line", "", "", 390, 1950, 900, 0, 9, false);
            Add("Text", "Кассир", "", 120, 2130, 260, 60, 10, true);
            Add("Line", "", "", 390, 2180, 900, 0, 9, false);

            return new PrintFormTemplate
            {
                SourceFormat = "Native",
                PageWidth = 2100,
                PageHeight = 2970,
                Bands = new List<PrintFormBand>
                {
                    new() { Type = "Header", Top = 0, Height = 480, Order = 0 },
                    new() { Type = "Body", Top = 480, Height = 1050, Order = 1 },
                    new() { Type = "Footer", Top = 1530, Height = 940, Order = 2 }
                },
                Elements = elements
            };
        }

        public async Task<List<Report>> GetPrintFormsAsync(Guid metadataId, bool includeInactive = true)
        {
            await EnsureSchemaAsync();
            var query = _context.Reports.AsNoTracking()
                .Include(item => item.ElementMappings)
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
            if (InvoiceDocumentTypes.IsSales(metadata.Name) || InvoiceDocumentTypes.IsPurchase(metadata.Name))
                return await ExportInvoiceDocumentAsync(report, recordId, metadata);

            var metadataService = new MetadataService(_context);
            var rows = await metadataService.GetCatalogDataAsync(metadata.Id);
            var rawRow = rows.FirstOrDefault(row => Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var id) && id == recordId)
                ?? throw new InvalidOperationException("Документ для печати не найден.");
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(metadata, metadataService);
            var row = ReferenceDisplayHelper.ResolveRows(new[] { rawRow }, maps).Single();
            var data = await BuildCashOrderDataAsync(row, metadata.Name);
            var mappings = await LoadElementMappingsAsync(report);

            if (!string.IsNullOrWhiteSpace(report.Template))
                return BuildTemplateLayoutPdf(report, data, mappings);

            var fallbackTemplate = CreateBlankNativeTemplate();
            report.Template = JsonSerializer.Serialize(fallbackTemplate);
            report.SourceFormat = "Native";
            return BuildTemplateLayoutPdf(report, data, mappings);
        }

        public async Task<byte[]> ExportInvoiceDocumentAsync(Report report, Guid invoiceId)
        {
            if (!report.IsActive)
                throw new InvalidOperationException("Выбранная печатная форма отключена.");
            if (!report.DataSourceId.HasValue)
                throw new InvalidOperationException("У печатной формы не указан документ-источник.");

            var metadata = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.Id == report.DataSourceId.Value)
                ?? throw new InvalidOperationException("Документ-источник печатной формы не найден.");
            return await ExportInvoiceDocumentAsync(report, invoiceId, metadata);
        }

        private async Task<byte[]> ExportInvoiceDocumentAsync(Report report, Guid invoiceId, MetadataObject metadata)
        {
            var invoiceService = new InvoiceService(_context);
            invoiceService.Configure(metadata);
            await invoiceService.EnsureSchemaAsync();
            var invoice = await invoiceService.GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Счет-фактура для печати не найден.");
            var (issuer, recipient) = await LoadInvoicePartiesAsync(invoice, metadata.Name);
            var data = BuildInvoicePrintData(invoice, metadata.Name, issuer, recipient);
            var mappings = await LoadElementMappingsAsync(report);

            if (!string.IsNullOrWhiteSpace(report.Template))
                return BuildTemplateLayoutPdf(report, data, mappings);

            report.Template = JsonSerializer.Serialize(GenerateInvoiceFrxTemplate(metadata, InvoiceDocumentTypes.IsSales(metadata.Name)));
            report.SourceFormat = "FoxProFRX";
            return BuildTemplateLayoutPdf(report, data, mappings);
        }

        public byte[] ExportTemplatePreview(Report report)
        {
            var sample = new CashOrderPrintData
            {
                Number = "42", Date = DateTime.Today, Organization = "ОсОО Пример",
                Inn = "12345678901234", Okpo = "12345678", Person = "Иванов Иван Иванович",
                CashDesk = "Касса KGS", CorrespondentAccount = "11100000 - Денежные средства",
                DebitAccount = report.ReportType == "CashPaymentOrder" ? "11100000 - Денежные средства" : "Касса KGS",
                CreditAccount = report.ReportType == "CashPaymentOrder" ? "Касса KGS" : "11100000 - Денежные средства",
                Amount = 12345.67m,
                AmountInWords = "двенадцать тысяч триста сорок пять сом 67 тыйын",
                Basis = "Оплата согласно заявлению", Note = "Предпросмотр импортированного макета"
            };

            if (string.IsNullOrWhiteSpace(report.Template))
                report.Template = JsonSerializer.Serialize(CreateBlankNativeTemplate());

            return BuildTemplateLayoutPdf(report, sample, report.ElementMappings.ToList());
        }

        public byte[] ExportReportTemplatePreview(DataTable dataTable, Report report)
        {
            var rules = new FoxProReportFieldRuleService(_context).GetActiveRulesSafe();
            return ExportReportTemplatePreview(dataTable, report, rules);
        }

        public byte[] ExportReportTemplatePreview(
            DataTable dataTable,
            Report report,
            IReadOnlyCollection<FoxProReportFieldRule>? rules)
        {
            if (string.IsNullOrWhiteSpace(report.Template))
                report.Template = JsonSerializer.Serialize(CreateBlankNativeTemplate());

            var data = BuildReportPreviewData(dataTable, report, rules ?? Array.Empty<FoxProReportFieldRule>());
            return BuildTemplateLayoutPdf(report, data, report.ElementMappings.ToList(), dataTable, rules ?? Array.Empty<FoxProReportFieldRule>());
        }

        public byte[] ExportReportTemplateExcel(DataTable dataTable, Report report)
        {
            var rules = new FoxProReportFieldRuleService(_context).GetActiveRulesSafe();
            return ExportReportTemplateExcel(dataTable, report, rules);
        }

        public byte[] ExportReportTemplateExcel(
            DataTable dataTable,
            Report report,
            IReadOnlyCollection<FoxProReportFieldRule>? rules)
        {
            if (string.IsNullOrWhiteSpace(report.Template))
                report.Template = JsonSerializer.Serialize(CreateBlankNativeTemplate());

            var data = BuildReportPreviewData(dataTable, report, rules ?? Array.Empty<FoxProReportFieldRule>());
            return BuildTemplateLayoutExcel(report, data, report.ElementMappings.ToList(), dataTable, rules ?? Array.Empty<FoxProReportFieldRule>());
        }

        public byte[] ExportProgrammaticReconciliationActPreview(DataTable dataTable, Report report)
        {
            if (dataTable == null)
                throw new ArgumentNullException(nameof(dataTable));
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            return BuildReconciliationActPdf(dataTable, report);
        }

        public byte[] ExportProgrammaticReconciliationActExcel(DataTable dataTable, Report report)
        {
            if (dataTable == null)
                throw new ArgumentNullException(nameof(dataTable));
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            return BuildReconciliationActExcel(dataTable, report);
        }

        private static byte[] BuildReconciliationActPdf(DataTable dataTable, Report report)
        {
            var rows = dataTable.Rows.Cast<DataRow>().ToList();
            var title = rows.Select(ReadReconciliationOperation).FirstOrDefault(value => value.StartsWith("АКТ", StringComparison.OrdinalIgnoreCase)) ?? report.Name;
            var period = rows.Select(ReadReconciliationOperation).FirstOrDefault(value => value.StartsWith("Период", StringComparison.OrdinalIgnoreCase)) ?? report.SubtitleText;
            var summary = rows.Select(ReadReconciliationOperation).LastOrDefault(value => value.Contains("задолж", StringComparison.OrdinalIgnoreCase)) ?? report.SummaryText;
            var bodyRows = rows
                .Where(row => !ShouldSkipReconciliationBodyRow(row, summary))
                .ToList();

            return QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(10, Unit.Millimetre);
                page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(8));
                page.Content().Column(column =>
                {
                    column.Item().AlignCenter().Text("АКТ СВЕРКИ").Bold().FontSize(12);
                    column.Item().AlignCenter().PaddingTop(4).Text(title).SemiBold();
                    if (!string.IsNullOrWhiteSpace(period))
                        column.Item().AlignCenter().PaddingTop(3).Text(period);
                    column.Item().AlignRight().PaddingTop(6).Text("Лист 1").Italic();
                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4.2f);
                            columns.ConstantColumn(62);
                            columns.ConstantColumn(62);
                            columns.ConstantColumn(72);
                            columns.ConstantColumn(72);
                            columns.ConstantColumn(62);
                            columns.ConstantColumn(62);
                            columns.ConstantColumn(38);
                        });

                        AddReconciliationPdfHeader(table);
                        foreach (var row in bodyRows)
                            AddReconciliationPdfRow(table, row);
                    });

                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        column.Item().PaddingTop(10).Text(summary).SemiBold();
                        var amount = ExtractLastReconciliationAmount(rows);
                        if (amount != 0m)
                            column.Item().PaddingTop(3).Text($"составляет {RussianMoneyInWords(Math.Abs(amount))}");
                    }

                    column.Item().PaddingTop(24).Row(row =>
                    {
                        row.RelativeItem().Text("Руководитель нашей организации __________________________");
                        row.RelativeItem().Text("Руководитель контрагента __________________________");
                    });
                    column.Item().PaddingTop(18).Row(row =>
                    {
                        row.RelativeItem().Text("Главный бухгалтер нашей организации _____________________");
                        row.RelativeItem().Text("Главный бухгалтер контрагента _____________________");
                    });
                });
            })).GeneratePdf();
        }

        private static byte[] BuildReconciliationActExcel(DataTable dataTable, Report report)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(BuildSafeExcelWorksheetName(report.Name));
            worksheet.Style.Font.FontName = "Times New Roman";
            worksheet.Style.Font.FontSize = 9;
            worksheet.ShowGridLines = false;
            worksheet.Columns(1, 8).AdjustToContents();
            worksheet.Column(1).Width = 44;
            worksheet.Columns(2, 8).Width = 12;

            var rows = dataTable.Rows.Cast<DataRow>().ToList();
            var currentRow = 1;
            worksheet.Range(currentRow, 1, currentRow, 8).Merge().Value = "АКТ СВЕРКИ";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            currentRow++;

            var title = rows.Select(ReadReconciliationOperation).FirstOrDefault(value => value.StartsWith("АКТ", StringComparison.OrdinalIgnoreCase)) ?? report.Name;
            worksheet.Range(currentRow, 1, currentRow, 8).Merge().Value = title;
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            currentRow++;

            var period = rows.Select(ReadReconciliationOperation).FirstOrDefault(value => value.StartsWith("Период", StringComparison.OrdinalIgnoreCase)) ?? report.SubtitleText;
            if (!string.IsNullOrWhiteSpace(period))
            {
                worksheet.Range(currentRow, 1, currentRow, 8).Merge().Value = period;
                worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                currentRow += 2;
            }

            var headers = new[] { "Наименование материала, вид операции", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "N докум", "Дата", "Модуль" };
            for (var column = 0; column < headers.Length; column++)
            {
                var cell = worksheet.Cell(currentRow, column + 1);
                cell.Value = headers[column];
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E6F2");
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            currentRow++;

            var summary = rows.Select(ReadReconciliationOperation).LastOrDefault(value => value.Contains("задолж", StringComparison.OrdinalIgnoreCase)) ?? report.SummaryText;
            foreach (var row in rows.Where(row => !ShouldSkipReconciliationBodyRow(row, summary)))
            {
                WriteReconciliationExcelRow(worksheet, currentRow, row);
                currentRow++;
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                currentRow += 2;
                worksheet.Range(currentRow, 1, currentRow, 8).Merge().Value = summary;
                worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            }

            worksheet.SheetView.FreezeRows(4);
            worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            worksheet.PageSetup.FitToPages(1, 0);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void AddReconciliationPdfHeader(TableDescriptor table)
        {
            foreach (var title in new[] { "Наименование материала, вид операции", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "N докум", "Дата", "Модуль" })
                table.Cell().Border(1).Background("#D9E6F2").Padding(3).AlignCenter().Text(title).Bold();
        }

        private static void AddReconciliationPdfRow(TableDescriptor table, DataRow row)
        {
            var operation = ReadReconciliationOperation(row);
            var isImportant = operation.StartsWith("САЛЬДО", StringComparison.OrdinalIgnoreCase) ||
                              operation.Contains("ИТОГО", StringComparison.OrdinalIgnoreCase) ||
                              operation.Contains("Пара счетов", StringComparison.OrdinalIgnoreCase);
            AddPdfCell(table, operation, isImportant, false);
            AddPdfCell(table, ReadPreviewText(row, "Дебет"), isImportant, true);
            AddPdfCell(table, ReadPreviewText(row, "Кредит"), isImportant, true);
            AddPdfCell(table, FormatReconciliationAmount(ReadPreviewObject(row, "Сумма Дт")), isImportant, true);
            AddPdfCell(table, FormatReconciliationAmount(ReadPreviewObject(row, "Сумма Кт")), isImportant, true);
            AddPdfCell(table, ReadPreviewText(row, "N докум", "Документ", "Номер"), isImportant, true);
            AddPdfCell(table, ReadPreviewText(row, "Дата"), isImportant, true);
            AddPdfCell(table, ReadPreviewText(row, "Модуль"), isImportant, true);
        }

        private static void AddPdfCell(TableDescriptor table, string value, bool bold, bool center)
        {
            var cell = table.Cell().Border(1).Padding(2);
            if (center)
                cell = cell.AlignCenter();
            var text = cell.Text(value ?? string.Empty);
            if (bold)
                text.Bold();
        }

        private static void WriteReconciliationExcelRow(IXLWorksheet worksheet, int rowIndex, DataRow row)
        {
            var values = new[]
            {
                ReadReconciliationOperation(row),
                ReadPreviewText(row, "Дебет"),
                ReadPreviewText(row, "Кредит"),
                FormatReconciliationAmount(ReadPreviewObject(row, "Сумма Дт")),
                FormatReconciliationAmount(ReadPreviewObject(row, "Сумма Кт")),
                ReadPreviewText(row, "N докум", "Документ", "Номер"),
                ReadPreviewText(row, "Дата"),
                ReadPreviewText(row, "Модуль")
            };
            var important = values[0].StartsWith("САЛЬДО", StringComparison.OrdinalIgnoreCase) ||
                            values[0].Contains("ИТОГО", StringComparison.OrdinalIgnoreCase) ||
                            values[0].Contains("Пара счетов", StringComparison.OrdinalIgnoreCase);
            for (var index = 0; index < values.Length; index++)
            {
                var cell = worksheet.Cell(rowIndex, index + 1);
                cell.Value = values[index];
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (important)
                    cell.Style.Font.Bold = true;
                if (index > 0)
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        private static string ReadReconciliationOperation(DataRow row)
        {
            var fallback = string.Empty;
            foreach (var candidate in new[]
                     {
                         "Наименование материала, вид операции", "Наименование", "Операция",
                         "Описание", "Вид операции", "Материал", "Номенклатура",
                         "operation_name", "name_kod", "naim", "tex", "text"
                     })
            {
                var value = ReadPreviewText(row, candidate);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!LooksLikeGeneratedRowLabel(value))
                    return value;

                var payload = ExtractGeneratedRowLabelPayload(value);
                fallback = string.IsNullOrWhiteSpace(payload) ? value : payload;
            }

            return fallback;
        }

        private static bool LooksLikeGeneratedRowLabel(string value) =>
            Regex.IsMatch(value ?? string.Empty, @"(?i)^\s*строка\s+\d+\s*:");

        private static string ExtractGeneratedRowLabelPayload(string value)
        {
            var match = Regex.Match(value ?? string.Empty, @"(?i)^\s*строка\s+\d+\s*:\s*(.+)$");
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static bool ShouldSkipReconciliationBodyRow(DataRow row, string summary)
        {
            var operation = ReadReconciliationOperation(row);
            return string.IsNullOrWhiteSpace(operation) ||
                   operation.StartsWith("АКТ", StringComparison.OrdinalIgnoreCase) ||
                   operation.StartsWith("Период", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(summary) && operation.Equals(summary, StringComparison.OrdinalIgnoreCase));
        }

        private static string FormatReconciliationAmount(object? value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;
            if (value is decimal decimalValue)
                return decimalValue == 0m ? string.Empty : decimalValue.ToString("N2");
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                   decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed)
                ? parsed == 0m ? string.Empty : parsed.ToString("N2")
                : value.ToString() ?? string.Empty;
        }

        private static decimal ExtractLastReconciliationAmount(IEnumerable<DataRow> rows)
        {
            foreach (var row in rows.Reverse())
            {
                var debit = ReadPreviewDecimal(row, "Сумма Дт");
                var credit = ReadPreviewDecimal(row, "Сумма Кт");
                if (debit != 0m)
                    return debit;
                if (credit != 0m)
                    return -credit;
            }

            return 0m;
        }

        private static CashOrderPrintData BuildReportPreviewData(
            DataTable dataTable,
            Report report,
            IReadOnlyCollection<FoxProReportFieldRule>? rules = null)
        {
            var extra = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            void Add(string key, object? value)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;
                extra[key] = value ?? string.Empty;
                extra[NormalizeFieldName(key)] = value ?? string.Empty;
            }

            Add("report_name", report.Name);
            Add("title", string.IsNullOrWhiteSpace(report.TitleText) ? report.Name : report.TitleText);
            Add("subtitle", report.SubtitleText);
            Add("summary", report.SummaryText);
            AddReconciliationPeriodAliases(report, Add);

            var rows = dataTable.Rows.Cast<DataRow>().Take(20).ToList();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
                var number = rowIndex + 1;
                foreach (DataColumn column in dataTable.Columns)
                {
                    var value = row[column];
                    Add(column.ColumnName, value);
                    Add($"line{number}_{column.ColumnName}", value);
                    Add($"line{number}.{column.ColumnName}", value);
                }

                AddReconciliationAliases(number, row, Add);
                AddConfiguredFoxProRuleAliases(number, row, rules ?? Array.Empty<FoxProReportFieldRule>(), Add);
            }

            AddReconciliationSummaryAliases(rows, report, Add);
            var first = rows.FirstOrDefault(IsReconciliationDataRow) ?? rows.FirstOrDefault();
            if (first != null)
                AddReconciliationAliases(0, first, Add);

            var amount = first == null ? 0m : ReadPreviewDecimal(first, "Сумма Дт", "Сумма Кт", "Сумма", "Итого");
            return new CashOrderPrintData
            {
                DocumentName = report.Name,
                Number = string.Empty,
                Date = DateTime.Today,
                Organization = first == null ? string.Empty : ReadPreviewText(first, "Организация", "Контрагент", "Кому"),
                DebitAccount = first == null ? string.Empty : ReadPreviewText(first, "Дебет"),
                CreditAccount = first == null ? string.Empty : ReadPreviewText(first, "Кредит"),
                Amount = amount,
                AmountInCurrency = amount,
                AmountInWords = RussianMoneyInWords(amount),
                Basis = report.SubtitleText,
                Note = "Предпросмотр отчета по импортированному FRX-макету",
                ExtraFields = extra
            };
        }

        private static void AddReconciliationPeriodAliases(Report report, Action<string, object?> add)
        {
            var match = Regex.Match(report.SubtitleText ?? string.Empty, @"(\d{2}\.\d{2}\.\d{4}).*?(\d{2}\.\d{2}\.\d{4})");
            if (match.Success)
            {
                add("period_start", match.Groups[1].Value);
                add("period_end", match.Groups[2].Value);
                add("dtb", match.Groups[1].Value);
                add("dtbeg", match.Groups[1].Value);
                add("datobr", match.Groups[1].Value);
                add("dtend", match.Groups[2].Value);
                add("date_end", match.Groups[2].Value);
            }

            add("report_title", string.IsNullOrWhiteSpace(report.TitleText) ? report.Name : report.TitleText);
            add("report_summary", report.SummaryText);
            add("signature", report.FooterSignature);
        }

        private static string? ExtractReconciliationPeriodEnd(Report report)
        {
            var match = Regex.Match(report.SubtitleText ?? string.Empty, @"(\d{2}\.\d{2}\.\d{4}).*?(\d{2}\.\d{2}\.\d{4})");
            return match.Success ? match.Groups[2].Value : null;
        }
        private static void AddReconciliationSummaryAliases(
            IReadOnlyList<DataRow> rows,
            Report report,
            Action<string, object?> add)
        {
            if (rows.Count == 0)
                return;

            var title = rows
                .Select(row => ReadPreviewText(row, "Наименование материала, вид операции", "Наименование", "Операция"))
                .FirstOrDefault(value => value.StartsWith("АКТ", StringComparison.OrdinalIgnoreCase))
                ?? report.Name;
            var summary = rows
                .Select(row => ReadPreviewText(row, "Наименование материала, вид операции", "Наименование", "Операция"))
                .LastOrDefault(value => value.Contains("задолж", StringComparison.OrdinalIgnoreCase))
                ?? report.SummaryText;
            var organization = ExtractQuotedOrganizationName(title);
            if (string.IsNullOrWhiteSpace(organization))
                organization = ExtractQuotedOrganizationName(report.Name);

            add("sha", "АКТ СВЕРКИ");
            add("sha1", title);
            add("sha2", summary);
            add("txt_zak1", organization);
            add("name_kod", organization);
            add("ksprorg.naim_orgp", organization);
            add("ksprorg_naim_orgp", organization);
            add("_PAGENO", 1);
            add("_pageno", 1);
            add("pageno", 1);
            add("date()", ExtractReconciliationPeriodEnd(report) ?? DateTime.Today.ToString("dd.MM.yyyy"));

            var pairRow = rows.FirstOrDefault(row =>
                ReadPreviewText(row, "Наименование материала, вид операции").Contains("Пара счетов", StringComparison.OrdinalIgnoreCase));
            if (pairRow != null)
            {
                var pairText = ReadPreviewText(pairRow, "Наименование материала, вид операции");
                var match = Regex.Match(pairText, @"(\d{6,})\s*-\s*(\d{6,})");
                if (match.Success)
                {
                    AddDatasetAliases(add, ReconciliationPairPrefixes, "korsch", match.Groups[1].Value);
                    AddDatasetAliases(add, ReconciliationPairPrefixes, "kor_sch", match.Groups[2].Value);
                    AddDatasetAliases(add, ReconciliationPairPrefixes, "name_sch", pairText);
                }
            }

            var opening = rows.FirstOrDefault(row =>
                ReadPreviewText(row, "Наименование материала, вид операции").StartsWith("САЛЬДО НА", StringComparison.OrdinalIgnoreCase));
            if (opening != null)
            {
                AddDatasetAliases(add, ReconciliationPairPrefixes, "deb_beg", ReadPreviewObject(opening, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "cred_beg", ReadPreviewObject(opening, "Сумма Кт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "deb_beg_v", ReadPreviewObject(opening, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "cred_beg_v", ReadPreviewObject(opening, "Сумма Кт"));
            }

            var turnover = rows.FirstOrDefault(row =>
                ReadPreviewText(row, "Наименование материала, вид операции").Contains("ИТОГО ОБОРОТОВ", StringComparison.OrdinalIgnoreCase));
            if (turnover != null)
            {
                AddDatasetAliases(add, ReconciliationPairPrefixes, "debsum", ReadPreviewObject(turnover, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "credsum", ReadPreviewObject(turnover, "Сумма Кт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "debsum_v", ReadPreviewObject(turnover, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "credsum_v", ReadPreviewObject(turnover, "Сумма Кт"));
            }

            var closing = rows.LastOrDefault(row =>
                ReadPreviewText(row, "Наименование материала, вид операции").StartsWith("САЛЬДО НА", StringComparison.OrdinalIgnoreCase));
            if (closing != null)
            {
                AddDatasetAliases(add, ReconciliationPairPrefixes, "deb", ReadPreviewObject(closing, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "cred", ReadPreviewObject(closing, "Сумма Кт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "deb_v", ReadPreviewObject(closing, "Сумма Дт"));
                AddDatasetAliases(add, ReconciliationPairPrefixes, "cred_v", ReadPreviewObject(closing, "Сумма Кт"));
            }
        }

        private static readonly string[] ReconciliationPairPrefixes =
        {
            "ved_sch.", "ved_sch_",
            "ved_obj.", "ved_obj_",
            "ved_org.", "ved_org_"
        };

        private static void AddDatasetAliases(Action<string, object?> add, IEnumerable<string> prefixes, string field, object? value)
        {
            foreach (var prefix in prefixes)
                add(prefix + field, value);
        }

        private static string ExtractQuotedOrganizationName(string? text)
        {
            var match = Regex.Match(text ?? string.Empty, "[\"«](.+?)[\"»]");
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static bool IsReconciliationDataRow(DataRow row)
        {
            var operation = ReadReconciliationOperation(row);
            var hasIdentity = !string.IsNullOrWhiteSpace(ReadPreviewText(row, "Дебет", "debit_account", "schet", "deb")) ||
                              !string.IsNullOrWhiteSpace(ReadPreviewText(row, "Кредит", "credit_account", "kor_sch", "korsch", "cred")) ||
                              !string.IsNullOrWhiteSpace(ReadPreviewText(row, "N докум", "Документ", "Номер", "document_number", "nom_dok")) ||
                              !string.IsNullOrWhiteSpace(ReadPreviewText(row, "Дата", "date", "document_date", "datobr"));
            var hasAmount = HasMeaningfulPreviewValue(ReadPreviewObject(row, "Сумма Дт", "Дебет сумма", "DEBSUM", "debit_amount")) ||
                            HasMeaningfulPreviewValue(ReadPreviewObject(row, "Сумма Кт", "Кредит сумма", "CREDSUM", "credit_amount"));

            return hasIdentity || (hasAmount && !string.IsNullOrWhiteSpace(operation));
        }

        private static bool HasMeaningfulPreviewValue(object? value)
        {
            if (value == null || value == DBNull.Value)
                return false;

            var text = value.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return !decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentParsed) || currentParsed != 0m ||
                   !decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantParsed) || invariantParsed != 0m;
        }
        private static void AddReconciliationAliases(int number, DataRow row, Action<string, object?> add)
        {
            var prefixes = FoxProReportKnowledgeBase.GetRowDatasetPrefixes(number);
            var name = ReadReconciliationOperation(row);
            var debit = ReadPreviewText(row, "Дебет", "debit_account", "schet", "deb");
            var credit = ReadPreviewText(row, "Кредит", "credit_account", "kor_sch", "korsch", "cred");
            var debitAmount = ReadPreviewObject(row, "Сумма Дт", "Дебет сумма", "DEBSUM", "debit_amount");
            var creditAmount = ReadPreviewObject(row, "Сумма Кт", "Кредит сумма", "CREDSUM", "credit_amount");
            var doc = ReadPreviewText(row, "N докум", "Документ", "Номер", "document_number", "nom_dok");
            var date = ReadPreviewText(row, "Дата", "document_date", "datobr");
            var module = ReadPreviewText(row, "Модуль", "module", "prs", "kod_arm");

            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "operation_name", name);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "debit_account", debit);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "credit_account", credit);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "debit_amount", debitAmount);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "credit_amount", creditAmount);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "document_number", doc);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "document_date", date);
            FoxProReportKnowledgeBase.AddAliases(add, prefixes, "module", module);
        }

        private static void AddConfiguredFoxProRuleAliases(
            int number,
            DataRow row,
            IReadOnlyCollection<FoxProReportFieldRule> rules,
            Action<string, object?> add)
        {
            if (rules.Count == 0)
                return;

            var prefixes = FoxProReportKnowledgeBase.GetRowDatasetPrefixes(number);
            foreach (var rule in rules.Where(rule => rule.IsActive).OrderBy(rule => rule.Priority))
            {
                var candidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(rule.TargetFieldName))
                    candidates.Add(rule.TargetFieldName);
                if (!string.IsNullOrWhiteSpace(rule.TargetDisplayName))
                    candidates.Add(rule.TargetDisplayName);
                if (!string.IsNullOrWhiteSpace(rule.CanonicalField))
                    candidates.AddRange(FoxProReportKnowledgeBase.GetTargetFieldCandidates(rule.CanonicalField));

                var value = ReadPreviewObject(row, candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                if (value == null || value == DBNull.Value)
                    continue;

                var source = FoxProReportKnowledgeBase.NormalizeRuleSource(rule.SourcePattern);
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                add(source, value);
                add(FoxProReportKnowledgeBase.StripDatasetPrefix(source), value);
                foreach (var prefix in prefixes)
                    add(prefix + FoxProReportKnowledgeBase.StripDatasetPrefix(source), value);

                if (!string.IsNullOrWhiteSpace(rule.CanonicalField))
                    FoxProReportKnowledgeBase.AddAliases(add, prefixes, rule.CanonicalField, value);
            }
        }

        private async Task<IReadOnlyCollection<ReportElementMapping>> LoadElementMappingsAsync(Report report)
        {
            if (report.ElementMappings?.Count > 0)
                return report.ElementMappings.ToList();

            await EnsureSchemaAsync();
            return await _context.ReportElementMappings.AsNoTracking()
                .Where(item => item.ReportId == report.Id)
                .OrderBy(item => item.Order)
                .ToListAsync();
        }

        private async Task<CashOrderPrintData> BuildCashOrderDataAsync(
            Dictionary<string, object> row,
            string documentName)
        {
            var amount = GetDecimal(row, "Сумма", "amount");
            var correspondentAccount = GetString(row, "Корр. счет", "correspondent_account");
            correspondentAccount = await ResolveAccountCodeAsync(correspondentAccount);
            var cashDesk = await ResolveCashDeskAsync(GetString(row, "Касса", "cash_desk_id"));
            var orderKind = GetString(row, "Тип КО", "order_kind", "cash_order_kind", "Тип", "document_type");
            var isPayment = orderKind.Contains("расход", StringComparison.OrdinalIgnoreCase) ||
                orderKind.Equals("Payment", StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase);
            var displayDocumentName = isPayment ? "Расходный кассовый ордер" : "Приходный кассовый ордер";
            var data = new CashOrderPrintData
            {
                DocumentName = displayDocumentName,
                Number = GetString(row, "Номер", "Номер документа", "doc_number"),
                Date = GetDate(row, "Дата", "doc_date"),
                Organization = GetString(row, "Организация", "organization_id"),
                Person = GetString(row, "Сотрудник", "employee_id", "МОЛ", "responsible_person_id"),
                CashDesk = cashDesk.DisplayName,
                CorrespondentAccount = correspondentAccount,
                DebitAccount = isPayment ? correspondentAccount : cashDesk.Account,
                CreditAccount = isPayment ? cashDesk.Account : correspondentAccount,
                Amount = amount,
                AmountInWords = RussianMoneyInWords(amount),
                Basis = GetString(row, "Основание", "basis"),
                Note = GetString(row, "Примечание", "description"),
                AmountInCurrency = GetDecimal(row, "Сумма в валюте", "amount_currency", "amount_in_currency")
            };
            var organizationValue = GetString(row, "Организация", "organization_id");
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

        private async Task<(OrganizationPrintInfo Issuer, OrganizationPrintInfo Recipient)> LoadInvoicePartiesAsync(
            InvoiceDocument invoice,
            string documentName)
        {
            var selectedOrganization = OrganizationPrintInfo.FromName(invoice.OrganizationId, invoice.OrganizationName);
            var primaryOrganization = OrganizationPrintInfo.Empty;

            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            if (catalog != null)
            {
                var rows = await new MetadataService(_context).GetCatalogDataAsync(catalog.Id);
                var selectedRow = rows.FirstOrDefault(row =>
                    invoice.OrganizationId.HasValue &&
                    TryGetRowId(row, out var rowId) &&
                    rowId == invoice.OrganizationId.Value);
                var primaryRow = rows.FirstOrDefault(IsPrimaryOrganization) ?? rows.FirstOrDefault();

                if (selectedRow != null)
                    selectedOrganization = BuildOrganizationPrintInfo(selectedRow);
                if (primaryRow != null)
                    primaryOrganization = BuildOrganizationPrintInfo(primaryRow);
            }

            if (primaryOrganization.IsEmpty)
                primaryOrganization = selectedOrganization;
            if (selectedOrganization.IsEmpty)
                selectedOrganization = primaryOrganization;

            return InvoiceDocumentTypes.IsSales(documentName)
                ? (primaryOrganization, selectedOrganization)
                : (selectedOrganization, primaryOrganization);
        }

        private static CashOrderPrintData BuildInvoicePrintData(
            InvoiceDocument invoice,
            string documentName,
            OrganizationPrintInfo issuer,
            OrganizationPrintInfo recipient)
        {
            var extra = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            void AddExtra(string key, object? value)
            {
                var normalized = NormalizeFieldName(key);
                extra[key] = value ?? string.Empty;
                extra[normalized] = value ?? string.Empty;
            }

            void AddOrganizationAliases(string prefix, OrganizationPrintInfo organization)
            {
                AddExtra($"{prefix}_name", organization.DisplayName);
                AddExtra($"{prefix}_full_name", organization.FullName);
                AddExtra($"{prefix}_inn", organization.Inn);
                AddExtra($"{prefix}_okpo", organization.Okpo);
                AddExtra($"{prefix}_address", organization.Address);
                AddExtra($"{prefix}_phone", organization.Phone);
                AddExtra($"{prefix}_email", organization.Email);
                AddExtra($"{prefix}_bank", organization.Bank);
                AddExtra($"{prefix}_bank_account", organization.BankAccount);
                AddExtra($"{prefix}_bic", organization.Bic);
                AddExtra($"{prefix}_director", organization.Director);
                AddExtra($"{prefix}_chief_accountant", organization.ChiefAccountant);
            }

            void AddFactOrganizationAliases(string side, OrganizationPrintInfo organization)
            {
                var name = string.IsNullOrWhiteSpace(organization.FullName)
                    ? organization.DisplayName
                    : organization.FullName;
                foreach (var factPrefix in new[] { $"fact_{side}", $"fact.{side}", side })
                {
                    AddExtra($"{factPrefix}_NAME_ORG", name);
                    AddExtra($"{factPrefix}_ORG", name);
                    AddExtra($"{factPrefix}_INN", organization.Inn);
                    AddExtra($"{factPrefix}_OKPO", organization.Okpo);
                    AddExtra($"{factPrefix}_ADR", organization.Address);
                    AddExtra($"{factPrefix}_PHONE", organization.Phone);
                    AddExtra($"{factPrefix}_EMAIL", organization.Email);
                    AddExtra($"{factPrefix}_BANK", organization.Bank);
                    AddExtra($"{factPrefix}_NAM_BANK", organization.Bank);
                    AddExtra($"{factPrefix}_RS", organization.BankAccount);
                    AddExtra($"{factPrefix}_RSCH", organization.BankAccount);
                    AddExtra($"{factPrefix}_BIK", organization.Bic);
                    AddExtra($"{factPrefix}_MFO", organization.Bic);
                    AddExtra($"{factPrefix}_DIR", organization.Director);
                    AddExtra($"{factPrefix}_BUH", organization.ChiefAccountant);
                    AddExtra($"{factPrefix}_RNI", string.Empty);
                }
            }

            void AddLegacyOrganizationAliases(string side, OrganizationPrintInfo organization)
            {
                var name = string.IsNullOrWhiteSpace(organization.FullName)
                    ? organization.DisplayName
                    : organization.FullName;
                AddExtra($"{side}_NAME_ORG", name);
                AddExtra($"{side}_ORG", name);
                AddExtra($"{side}_IN", organization.Inn);
                AddExtra($"{side}_INN", organization.Inn);
                AddExtra($"{side}_OKPO", organization.Okpo);
                AddExtra($"{side}_ADR", organization.Address);
                AddExtra($"{side}_AD", organization.Address);
                AddExtra($"{side}_NAM_BANK", organization.Bank);
                AddExtra($"{side}_BANK", organization.Bank);
                AddExtra($"{side}_RS", organization.BankAccount);
                AddExtra($"{side}_R", organization.BankAccount);
                AddExtra($"{side}_RC", string.Empty);
                AddExtra($"{side}_RNI", string.Empty);
                AddExtra($"{side}_M", organization.Bic);
                AddExtra($"{side}_BIK", organization.Bic);
                AddExtra($"{side}_TEL", organization.Phone);
                AddExtra($"{side}_PHONE", organization.Phone);
                AddExtra($"{side}_DIR", organization.Director);
                AddExtra($"{side}_BUH", organization.ChiefAccountant);
            }

            var taxBlankNumber = string.IsNullOrWhiteSpace(invoice.TaxBlankNumber)
                ? invoice.EsfNumber
                : invoice.TaxBlankNumber;

            AddExtra("esf_number", invoice.EsfNumber);
            AddExtra("Номер ЭСФ", invoice.EsfNumber);
            AddExtra("tax_blank_number", taxBlankNumber);
            AddExtra("Серия и № бланка", taxBlankNumber);
            AddExtra("module_code", invoice.ModuleCode);
            AddExtra("Модуль", invoice.ModuleCode);
            AddExtra("counterparty_account", invoice.CounterpartyAccountCode);
            AddExtra("Счет", invoice.CounterpartyAccountCode);
            AddExtra("payment_kind", invoice.PaymentKind);
            AddExtra("delivery_kind", invoice.DeliveryKind);
            AddExtra("supply_kind", invoice.SupplyKind);
            AddExtra("amount_without_tax", invoice.AmountWithoutTax);
            AddExtra("vat_total", invoice.VatTotal);
            AddExtra("sales_tax_total", invoice.SalesTaxTotal);
            AddExtra("total_amount", invoice.TotalAmount);
            AddExtra("is_posted", invoice.IsPosted ? "Да" : "Нет");
            AddExtra("fact_SER_BL", taxBlankNumber);
            AddExtra("fact.SER_BL", taxBlankNumber);
            AddExtra("fact_NOM_BL", invoice.DocNumber);
            AddExtra("fact.NOM_BL", invoice.DocNumber);
            AddExtra("fact_D_SALE", invoice.DocDate);
            AddExtra("fact.D_SALE", invoice.DocDate);
            AddExtra("fact_SVDATE", invoice.DocDate);
            AddExtra("fact.SVDATE", invoice.DocDate);
            AddExtra("fact_DT_KOR", string.Empty);
            AddExtra("fact.DT_KOR", string.Empty);
            AddExtra("fact_bl_kor", string.Empty);
            AddExtra("fact.bl_kor", string.Empty);
            AddExtra("fact_PATENT", string.Empty);
            AddExtra("fact.PATENT", string.Empty);
            AddOrganizationAliases("issuer", issuer);
            AddOrganizationAliases("seller", issuer);
            AddOrganizationAliases("recipient", recipient);
            AddOrganizationAliases("buyer", recipient);
            AddFactOrganizationAliases("C", issuer);
            AddFactOrganizationAliases("D", recipient);
            AddLegacyOrganizationAliases("C", issuer);
            AddLegacyOrganizationAliases("D", recipient);
            AddExtra("P_M", recipient.Bic);
            AddExtra("fact_NAME_SALE", invoice.DeliveryKind);
            AddExtra("fact.NAME_SALE", invoice.DeliveryKind);
            AddExtra("fact_NAME_OP", invoice.PaymentKind);
            AddExtra("fact.NAME_OP", invoice.PaymentKind);
            AddExtra("fact_TXT_KOR", invoice.Basis);
            AddExtra("fact.TXT_KOR", invoice.Basis);
            AddExtra("curFACTSW.00", invoice.AmountWithoutTax);
            AddExtra("curFACTSW.sum", invoice.TotalAmount);
            AddExtra("curFACTSW.ndc", invoice.VatTotal);
            AddExtra("curFACTSW.nalog", invoice.SalesTaxTotal);
            AddExtra("irfactsw.ndc", invoice.VatTotal);
            AddExtra("irfactsw.nalog", invoice.SalesTaxTotal);
            var firstLine = invoice.Lines.FirstOrDefault();
            AddExtra("curFACTSW.K_MAT", firstLine?.AccountCode ?? string.Empty);
            AddExtra("curFACTSW.NAME_MAT", firstLine?.Name ?? string.Empty);
            AddExtra("curFACTSW.ED_IZ", firstLine?.UnitName ?? string.Empty);
            AddExtra("curFACTSW.KOL", firstLine?.Quantity ?? 0);
            AddExtra("curFACTSW.CENA", firstLine?.AmountWithoutTax ?? 0);
            AddExtra("curFACTSW.PR_NDC", firstLine?.VatRate ?? 0);
            AddExtra("curFACTSW.PR_OP", firstLine?.SalesTaxRate ?? 0);

            for (var index = 0; index < invoice.Lines.Count; index++)
            {
                var number = index + 1;
                var line = invoice.Lines[index];
                AddExtra($"line{number}_name", line.Name);
                AddExtra($"line{number}_unit", line.UnitName);
                AddExtra($"line{number}_quantity", line.Quantity);
                AddExtra($"line{number}_account", line.AccountCode);
                AddExtra($"line{number}_account_name", string.IsNullOrWhiteSpace(line.AccountName) ? line.AccountCode : line.AccountName);
                AddExtra($"line{number}_amount_without_tax", line.AmountWithoutTax);
                AddExtra($"line{number}_vat", line.VatAmount);
                AddExtra($"line{number}_sales_tax", line.SalesTaxAmount);
                AddExtra($"line{number}_total", line.LineTotal);
                AddExtra($"curFACTSW{number}.name", line.Name);
                AddExtra($"curFACTSW{number}.ed_iz", line.UnitName);
                AddExtra($"curFACTSW{number}.kol", line.Quantity);
                AddExtra($"curFACTSW{number}.account", line.AccountCode);
                AddExtra($"curFACTSW{number}.sum", line.AmountWithoutTax);
                AddExtra($"curFACTSW{number}.ndc", line.VatAmount);
                AddExtra($"curFACTSW{number}.nalog", line.SalesTaxAmount);
            }

            return new CashOrderPrintData
            {
                DocumentName = documentName,
                Number = invoice.DocNumber,
                Date = invoice.DocDate,
                Organization = issuer.DisplayName,
                Inn = issuer.Inn,
                Okpo = issuer.Okpo,
                Person = recipient.DisplayName,
                CorrespondentAccount = invoice.CounterpartyAccountCode,
                DebitAccount = invoice.CounterpartyAccountCode,
                CreditAccount = string.Empty,
                Amount = invoice.TotalAmount,
                AmountInCurrency = invoice.TotalAmount,
                AmountInWords = RussianMoneyInWords(invoice.TotalAmount),
                Basis = invoice.Basis,
                Note = invoice.IsPosted ? "Проведён" : "Не проведён",
                ExtraFields = extra
            };
        }

        private static OrganizationPrintInfo BuildOrganizationPrintInfo(Dictionary<string, object> row)
        {
            var id = TryGetRowId(row, out var rowId) ? rowId : (Guid?)null;
            var shortName = GetString(row, "Наименование", "name");
            var fullName = GetString(row, "Полное наименование", "full_name");
            var displayName = string.IsNullOrWhiteSpace(fullName) ? shortName : fullName;
            var address = GetString(row, "Юридический адрес", "legal_address", "Фактический адрес", "actual_address");

            return new OrganizationPrintInfo(
                id,
                string.IsNullOrWhiteSpace(displayName) ? shortName : displayName,
                fullName,
                shortName,
                GetString(row, "ИНН", "inn"),
                GetString(row, "ОКПО", "okpo"),
                address,
                GetString(row, "Телефон", "phone"),
                GetString(row, "Email", "email"),
                GetString(row, "Банк", "bank_name"),
                GetString(row, "Расчетный счет", "bank_account"),
                GetString(row, "БИК", "bic"),
                GetString(row, "Руководитель", "director"),
                GetString(row, "Главный бухгалтер", "chief_accountant"));
        }

        private static bool IsPrimaryOrganization(Dictionary<string, object> row) =>
            GetBoolean(row, "Первичная организация", "is_primary");

        private static bool TryGetRowId(Dictionary<string, object> row, out Guid id) =>
            Guid.TryParse(GetString(row, "Id"), out id);

        private async Task<string> ResolveAccountCodeAsync(string value)
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));
            if (catalog == null)
                return value;
            var rows = await new MetadataService(_context).GetCatalogDataAsync(catalog.Id);
            Dictionary<string, object>? account;
            if (Guid.TryParse(value, out var id))
            {
                account = rows.FirstOrDefault(row =>
                    Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var rowId) && rowId == id);
            }
            else
            {
                account = rows.FirstOrDefault(row =>
                    GetString(row, "Код", "code").Equals(value, StringComparison.OrdinalIgnoreCase));
            }

            return account == null ? GetAccountCode(value) : GetString(account, "Код", "code");
        }

        private async Task<CashDeskPrintInfo> ResolveCashDeskAsync(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new CashDeskPrintInfo(string.Empty, string.Empty);

            if (!Guid.TryParse(value, out var id))
                return new CashDeskPrintInfo(value, await ResolveAccountCodeAsync(value));

            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Кассы");
            if (catalog == null)
                return new CashDeskPrintInfo(value, value);

            var rows = await new MetadataService(_context).GetCatalogDataAsync(catalog.Id);
            var cashDesk = rows.FirstOrDefault(row =>
                Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var rowId) && rowId == id);
            if (cashDesk == null)
                return new CashDeskPrintInfo(value, value);

            var displayName = GetString(cashDesk, "Наименование кассы", "Наименование", "name");
            var accountCode = GetString(cashDesk, "Счет", "Счет кассы", "Код", "code");
            var account = await ResolveAccountCodeAsync(accountCode);
            return new CashDeskPrintInfo(
                string.IsNullOrWhiteSpace(displayName) ? value : displayName,
                string.IsNullOrWhiteSpace(account) ? accountCode : account);
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
                        table.Cell().Border(1).Padding(4).Text(data.DebitAccount);
                        table.Cell().Border(1).Padding(4).Text(data.CreditAccount);
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
                        table.Cell().Border(1).Padding(5).Text(data.DebitAccount);
                        table.Cell().Border(1).Padding(5).Text(data.CreditAccount);
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

        private static byte[] BuildTemplateLayoutPdf(
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping>? mappings,
            DataTable? dataTable = null,
            IReadOnlyCollection<FoxProReportFieldRule>? rules = null)
        {
            var frxReport = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = report.Name,
                OriginalFileName = string.Empty,
                FrxXml = report.Template
            };
            var parser = new FrxParser();
            var template = parser.GetPrintTemplate(frxReport);
            if (template.Elements.Count == 0)
                throw new InvalidOperationException("Макет печатной формы не содержит элементов для вывода.");
            var layoutTemplate = PrepareTemplateForReportRendering(template, report, data, mappings ?? Array.Empty<ReportElementMapping>(), dataTable, rules ?? Array.Empty<FoxProReportFieldRule>());
            var svg = BuildTemplateSvg(layoutTemplate, report, data, mappings ?? Array.Empty<ReportElementMapping>(), true);
            return QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(report.PageOrientation == "Landscape" ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(8, Unit.Millimetre);
                page.Content().Svg(svg).FitArea();
            })).GeneratePdf();
        }

        private static byte[] BuildTemplateLayoutExcel(
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping>? mappings,
            DataTable? dataTable = null,
            IReadOnlyCollection<FoxProReportFieldRule>? rules = null)
        {
            var frxReport = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = report.Name,
                OriginalFileName = string.Empty,
                FrxXml = report.Template
            };
            var parser = new FrxParser();
            var template = parser.GetPrintTemplate(frxReport);
            if (template.Elements.Count == 0)
                throw new InvalidOperationException("Макет печатной формы не содержит элементов для вывода в Excel.");

            var layoutTemplate = PrepareTemplateForReportRendering(template, report, data, mappings ?? Array.Empty<ReportElementMapping>(), dataTable, rules ?? Array.Empty<FoxProReportFieldRule>());
            var mappingByOrder = (mappings ?? Array.Empty<ReportElementMapping>())
                .GroupBy(item => item.ElementOrder)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(BuildSafeExcelWorksheetName(report.Name));
            ConfigureFrxExcelWorksheet(worksheet, report);

            var pageWidth = Math.Max(1000d, layoutTemplate.PageWidth);
            var pageHeight = Math.Max(1000d, layoutTemplate.PageHeight);
            var landscape = pageWidth >= pageHeight || string.Equals(report.PageOrientation, "Landscape", StringComparison.OrdinalIgnoreCase);
            var maxColumns = landscape ? 88 : 66;
            var maxRows = landscape ? 58 : 82;
            var columnScale = (maxColumns - 1d) / pageWidth;
            var rowScale = (maxRows - 1d) / pageHeight;

            worksheet.Columns(1, maxColumns).Width = landscape ? 1.45 : 1.65;
            worksheet.Rows(1, maxRows).Height = 9;

            foreach (var element in layoutTemplate.Elements.OrderBy(item => item.Order))
            {
                if (mappingByOrder.TryGetValue(element.Order, out var hiddenMapping) && !hiddenMapping.IsVisible)
                    continue;

                if (element.Type is "Line" or "Box" or "Picture")
                    continue;

                var value = ResolveElementValue(report, layoutTemplate, element, data, mappingByOrder);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var startRow = ToExcelIndex(element.Top, rowScale, maxRows);
                var startColumn = ToExcelIndex(element.Left, columnScale, maxColumns);
                var endRow = ToExcelIndex(element.Top + Math.Max(element.Height, 24), rowScale, maxRows, startRow);
                var endColumn = ToExcelIndex(element.Left + Math.Max(element.Width, 24), columnScale, maxColumns, startColumn);
                WriteFrxExcelText(worksheet, element, value, startRow, startColumn, endRow, endColumn);
            }

            foreach (var segment in BuildFrxGridSegments(layoutTemplate, mappingByOrder, 1d))
                ApplyFrxExcelGridSegment(worksheet, segment, rowScale, columnScale, maxRows, maxColumns);

            worksheet.Range(1, 1, maxRows, maxColumns).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static PrintFormTemplate PrepareTemplateForReportRendering(
            PrintFormTemplate template,
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping> mappings,
            DataTable? dataTable,
            IReadOnlyCollection<FoxProReportFieldRule> rules)
        {
            var layoutTemplate = FrxRecognitionProfileService.PrepareForRendering(template);
            if (!IsFoxProTemplate(layoutTemplate, report))
                return layoutTemplate;

            return ShouldPackReconciliationFrxTemplate(layoutTemplate, report, dataTable)
                ? BuildPackedReconciliationFrxTemplate(layoutTemplate, report, data, mappings, dataTable!, rules)
                : BuildPackedStaticFrxTemplate(layoutTemplate, report, data, mappings);
        }

        private static bool IsFoxProTemplate(PrintFormTemplate template, Report report) =>
            template.SourceFormat.Equals("FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
            report.SourceFormat.Equals("FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
            template.OriginalFileName.EndsWith(".frx", StringComparison.OrdinalIgnoreCase);

        private static bool ShouldPackReconciliationFrxTemplate(PrintFormTemplate template, Report report, DataTable? dataTable)
        {
            if (dataTable == null || dataTable.Rows.Count == 0)
                return false;

            return IsFoxProTemplate(template, report) && IsReconciliationActReport(dataTable, report);
        }
        private static bool IsReconciliationActReport(DataTable dataTable, Report report)
        {
            if (string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase) ||
                ContainsIgnoreCase(report.Name, "акт свер") ||
                ContainsIgnoreCase(report.TitleText, "акт свер"))
                return true;

            return dataTable.Rows.Cast<DataRow>().Take(8)
                .Select(ReadReconciliationOperation)
                .Any(value => value.StartsWith("АКТ СВЕРКИ", StringComparison.OrdinalIgnoreCase));
        }

        private static PrintFormTemplate BuildPackedReconciliationFrxTemplate(
            PrintFormTemplate template,
            Report report,
            CashOrderPrintData summaryData,
            IReadOnlyCollection<ReportElementMapping> mappings,
            DataTable dataTable,
            IReadOnlyCollection<FoxProReportFieldRule> rules)
        {
            var bands = BuildEffectiveBands(template);
            if (bands.Count == 0 || template.Elements.Count == 0)
                return template;

            var rows = dataTable.Rows.Cast<DataRow>().ToList();
            var summary = rows
                .Select(ReadReconciliationOperation)
                .LastOrDefault(value => value.Contains("задолж", StringComparison.OrdinalIgnoreCase))
                ?? report.SummaryText;
            var detailRows = rows
                .Where(row => IsReconciliationMovementRow(row, summary))
                .ToList();
            if (detailRows.Count == 0)
                detailRows = rows.Where(IsReconciliationDataRow).ToList();

            var mappingByOrder = mappings
                .GroupBy(item => item.ElementOrder)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());
            var elementsByBand = bands.ToDictionary(
                band => band,
                band => template.Elements
                    .Where(element => ReferenceEquals(FindBandForElement(element, bands), band))
                    .OrderBy(element => element.Top)
                    .ThenBy(element => element.Left)
                    .ThenBy(element => element.Order)
                    .ToList());

            var packedElements = new List<PrintFormElement>();
            var packedBands = new List<PrintFormBand>();
            var currentTop = Math.Clamp(template.PageHeight * 0.018, 60, 240);
            var nextOrder = 1;
            PrintFormBand? previousBand = null;

            foreach (var band in bands)
            {
                if (!elementsByBand.TryGetValue(band, out var bandElements) || bandElements.Count == 0)
                    continue;

                if (IsReconciliationDataBand(band, bandElements))
                {
                    foreach (var row in detailRows)
                    {
                        var rowData = BuildReportPreviewDataForRow(dataTable, report, row, rules, summaryData);
                        AppendPackedBand(template, report, band, bandElements, rowData, mappingByOrder, packedBands, packedElements, ref currentTop, ref nextOrder, previousBand);
                        previousBand = band;
                    }
                    continue;
                }

                AppendPackedBand(template, report, band, bandElements, summaryData, mappingByOrder, packedBands, packedElements, ref currentTop, ref nextOrder, previousBand);
                previousBand = band;
            }

            if (packedElements.Count == 0)
                return template;

            var bottomMargin = Math.Clamp(template.PageHeight * 0.025, 100, 320);
            return new PrintFormTemplate
            {
                SourceFormat = template.SourceFormat,
                OriginalFileName = template.OriginalFileName,
                RecognitionProfileCode = template.RecognitionProfileCode,
                LayoutNormalized = true,
                PageWidth = template.PageWidth,
                PageHeight = Math.Max(1000, packedElements.Max(element => element.Top + Math.Max(element.Height, 1)) + bottomMargin),
                Bands = packedBands,
                Elements = packedElements
            };
        }

        private static PrintFormTemplate BuildPackedStaticFrxTemplate(
            PrintFormTemplate template,
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping> mappings)
        {
            var bands = BuildEffectiveBands(template);
            if (bands.Count <= 1 || template.Elements.Count == 0)
                return CompactImportedTemplateVerticalGaps(template);

            var mappingByOrder = mappings
                .GroupBy(item => item.ElementOrder)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());
            var elementsByBand = bands.ToDictionary(
                band => band,
                band => template.Elements
                    .Where(element => ReferenceEquals(FindBandForElement(element, bands), band))
                    .OrderBy(element => element.Top)
                    .ThenBy(element => element.Left)
                    .ThenBy(element => element.Order)
                    .ToList());

            var packedElements = new List<PrintFormElement>();
            var packedBands = new List<PrintFormBand>();
            var currentTop = Math.Clamp(template.PageHeight * 0.018, 60, 240);
            var nextOrder = 1;
            PrintFormBand? previousBand = null;

            foreach (var band in bands)
            {
                if (!elementsByBand.TryGetValue(band, out var bandElements) || bandElements.Count == 0)
                    continue;

                AppendPackedBand(template, report, band, bandElements, data, mappingByOrder, packedBands, packedElements, ref currentTop, ref nextOrder, previousBand);
                previousBand = band;
            }

            if (packedElements.Count == 0)
                return template;

            var bottomMargin = Math.Clamp(template.PageHeight * 0.025, 100, 320);
            return new PrintFormTemplate
            {
                SourceFormat = template.SourceFormat,
                OriginalFileName = template.OriginalFileName,
                RecognitionProfileCode = template.RecognitionProfileCode,
                LayoutNormalized = true,
                PageWidth = template.PageWidth,
                PageHeight = Math.Max(1000, packedElements.Max(element => element.Top + Math.Max(element.Height, 1)) + bottomMargin),
                Bands = packedBands,
                Elements = packedElements
            };
        }
        private static bool IsReconciliationDataBand(PrintFormBand band, IReadOnlyList<PrintFormElement> bandElements)
        {
            if (IsDetailBand(band) ||
                band.Type.Contains("Data", StringComparison.OrdinalIgnoreCase) ||
                band.Type.Contains("Данные", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsPageHeaderBand(band) || IsSummaryBand(band))
                return false;

            return bandElements.Any(LooksLikeReconciliationMovementElement) &&
                   !bandElements.Any(LooksLikeReconciliationGroupElement);
        }

        private static bool LooksLikeReconciliationMovementElement(PrintFormElement element)
        {
            var source = $"{element.Expression} {element.Text}";
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return Regex.IsMatch(source,
                @"(?i)\b(?:ved\d*|db_crs?|dbcrs?)[._](?:tex|text|name_kod|schet|deb|debet|kredit|cred|korsch|kor_sch|debsum|credsum|dok|dokum|nom_dok|date|datobr|prs|kod_arm|module)\b");
        }

        private static bool LooksLikeReconciliationGroupElement(PrintFormElement element)
        {
            var source = $"{element.Expression} {element.Text}".ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.Contains("сальдо") ||
                   source.Contains("итого") ||
                   source.Contains("пара счет") ||
                   source.Contains("ved_org") ||
                   source.Contains("ved_obj") ||
                   source.Contains("ved_sch") ||
                   source.Contains("deb_beg") ||
                   source.Contains("cred_beg");
        }
        private static void AppendPackedBand(
            PrintFormTemplate template,
            Report report,
            PrintFormBand band,
            IReadOnlyList<PrintFormElement> sourceElements,
            CashOrderPrintData data,
            IReadOnlyDictionary<int, ReportElementMapping> mappingByOrder,
            List<PrintFormBand> packedBands,
            List<PrintFormElement> packedElements,
            ref double currentTop,
            ref int nextOrder,
            PrintFormBand? previousBand)
        {
            var visibleElements = sourceElements
                .Where(element => !mappingByOrder.TryGetValue(element.Order, out var mapping) || mapping.IsVisible)
                .ToList();
            if (visibleElements.Count == 0)
                return;

            currentTop += GetPackedReconciliationBandGap(template, previousBand, band);
            var contentTop = visibleElements.Min(element => element.Top);
            var contentBottom = visibleElements.Max(element => element.Top + Math.Max(element.Height, 1));
            var packedBandTop = currentTop;
            var packedBandHeight = Math.Max(1, contentBottom - contentTop);
            packedBands.Add(new PrintFormBand
            {
                Type = band.Type,
                Top = packedBandTop,
                Height = packedBandHeight,
                Order = packedBands.Count + 1
            });

            foreach (var element in visibleElements.OrderBy(element => element.Top).ThenBy(element => element.Left).ThenBy(element => element.Order))
            {
                var clone = ClonePrintFormElement(element);
                clone.Top = packedBandTop + Math.Max(0, element.Top - contentTop);
                clone.Order = nextOrder++;

                if (clone.Type is "Text" or "Expression")
                {
                    clone.Text = ResolveElementValue(report, template, element, data, mappingByOrder);
                    clone.Expression = string.Empty;
                    clone.Type = "Text";
                }

                packedElements.Add(clone);
            }

            currentTop = packedBandTop + packedBandHeight;
        }

        private static double GetPackedReconciliationBandGap(PrintFormTemplate template, PrintFormBand? previousBand, PrintFormBand currentBand)
        {
            if (previousBand == null)
                return 0;

            var sectionGap = Math.Clamp(template.PageHeight * 0.006, 12, 90);
            if (IsPageHeaderBand(previousBand) || IsSummaryBand(currentBand))
                return sectionGap;

            // FRX borders often sit on band edges. Extra inner gaps draw parallel lines as a thick stripe.
            return 0;
        }

        private static IReadOnlyList<PrintFormBand> BuildEffectiveBands(PrintFormTemplate template)
        {
            if (template.Bands.Count == 0)
            {
                var top = template.Elements.Min(element => element.Top);
                var bottom = template.Elements.Max(element => element.Top + Math.Max(element.Height, 1));
                return new[]
                {
                    new PrintFormBand
                    {
                        Type = "Detail",
                        Top = top,
                        Height = Math.Max(1, bottom - top),
                        Order = 1
                    }
                };
            }

            var bands = template.Bands
                .OrderBy(band => band.Top)
                .ThenBy(band => band.Order)
                .Select(ClonePrintFormBand)
                .ToList();

            if (bands.All(band => Math.Abs(band.Top) < 0.001))
            {
                double top = 0;
                foreach (var band in bands.OrderBy(band => band.Order))
                {
                    band.Top = top;
                    top += Math.Max(1, band.Height);
                }
            }

            return bands.OrderBy(band => band.Top).ThenBy(band => band.Order).ToList();
        }

        private static PrintFormBand FindBandForElement(PrintFormElement element, IReadOnlyList<PrintFormBand> bands)
        {
            const double tolerance = 2d;
            var byCoordinate = bands
                .Where(band => element.Top >= band.Top - tolerance && element.Top <= band.Top + Math.Max(band.Height, 1) + tolerance)
                .OrderBy(band => Math.Abs(element.Top - band.Top))
                .FirstOrDefault();
            if (byCoordinate != null)
                return byCoordinate;

            var previous = bands.LastOrDefault(band => element.Top >= band.Top - tolerance);
            if (previous != null)
                return previous;

            var byType = bands.FirstOrDefault(band => band.Type.Equals(element.BandType, StringComparison.OrdinalIgnoreCase));
            return byType ?? bands[0];
        }

        private static bool IsReconciliationMovementRow(DataRow row, string summary)
        {
            if (ShouldSkipReconciliationBodyRow(row, summary))
                return false;

            var operation = ReadReconciliationOperation(row);
            if (operation.StartsWith("САЛЬДО", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("ИТОГО", StringComparison.OrdinalIgnoreCase) ||
                operation.Contains("Пара счетов", StringComparison.OrdinalIgnoreCase))
                return false;

            return IsReconciliationDataRow(row);
        }

        private static CashOrderPrintData BuildReportPreviewDataForRow(
            DataTable dataTable,
            Report report,
            DataRow row,
            IReadOnlyCollection<FoxProReportFieldRule> rules,
            CashOrderPrintData summaryData)
        {
            var extra = new Dictionary<string, object>(summaryData.ExtraFields, StringComparer.OrdinalIgnoreCase);
            void Add(string key, object? value)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;
                extra[key] = value ?? string.Empty;
                extra[NormalizeFieldName(key)] = value ?? string.Empty;
            }

            foreach (DataColumn column in dataTable.Columns)
                Add(column.ColumnName, row[column]);

            AddReconciliationAliases(0, row, Add);
            AddReconciliationAliases(1, row, Add);
            AddConfiguredFoxProRuleAliases(0, row, rules, Add);
            AddConfiguredFoxProRuleAliases(1, row, rules, Add);

            var dateText = ReadPreviewText(row, "Дата", "document_date", "datobr");
            var rowDate = TryParseFoxDate(dateText, out var parsedDate) ? parsedDate : summaryData.Date;
            var debitAmount = ReadPreviewDecimal(row, "Сумма Дт", "Дебет сумма", "debit_amount", "debsum");
            var creditAmount = ReadPreviewDecimal(row, "Сумма Кт", "Кредит сумма", "credit_amount", "credsum");
            var amount = debitAmount != 0m ? debitAmount : creditAmount;

            return new CashOrderPrintData
            {
                DocumentName = summaryData.DocumentName,
                Number = ReadPreviewText(row, "N докум", "Документ", "Номер", "document_number", "dok"),
                Date = rowDate,
                Organization = summaryData.Organization,
                Inn = summaryData.Inn,
                Okpo = summaryData.Okpo,
                Person = summaryData.Person,
                CashDesk = summaryData.CashDesk,
                CorrespondentAccount = summaryData.CorrespondentAccount,
                DebitAccount = ReadPreviewText(row, "Дебет", "debit_account", "schet", "deb"),
                CreditAccount = ReadPreviewText(row, "Кредит", "credit_account", "kor_sch", "korsch", "cred"),
                Amount = amount,
                AmountInCurrency = amount,
                AmountInWords = RussianMoneyInWords(Math.Abs(amount)),
                Basis = ReadReconciliationOperation(row),
                Note = summaryData.Note,
                ExtraFields = extra
            };
        }

        private static bool IsDetailBand(PrintFormBand band) =>
            band.Type.Contains("Detail", StringComparison.OrdinalIgnoreCase) ||
            band.Type.Contains("Детал", StringComparison.OrdinalIgnoreCase);

        private static bool IsPageHeaderBand(PrintFormBand band) =>
            band.Type.Contains("PageHeader", StringComparison.OrdinalIgnoreCase) ||
            band.Type.Contains("Title", StringComparison.OrdinalIgnoreCase);

        private static bool IsSummaryBand(PrintFormBand band) =>
            band.Type.Contains("Summary", StringComparison.OrdinalIgnoreCase) ||
            band.Type.Contains("PageFooter", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsIgnoreCase(string? value, string fragment) =>
            !string.IsNullOrWhiteSpace(value) && value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        private static PrintFormBand ClonePrintFormBand(PrintFormBand band) => new()
        {
            Type = band.Type,
            Top = band.Top,
            Height = band.Height,
            Order = band.Order
        };

        private static PrintFormElement ClonePrintFormElement(PrintFormElement element) => new()
        {
            Type = element.Type,
            Text = element.Text,
            Expression = element.Expression,
            BandType = element.BandType,
            Left = element.Left,
            Top = element.Top,
            Width = element.Width,
            Height = element.Height,
            FontName = element.FontName,
            FontSize = element.FontSize,
            Bold = element.Bold,
            Italic = element.Italic,
            Alignment = element.Alignment,
            BorderStyle = element.BorderStyle,
            Order = element.Order
        };
        private static void ConfigureFrxExcelWorksheet(IXLWorksheet worksheet, Report report)
        {
            worksheet.ShowGridLines = false;
            worksheet.Style.Font.FontName = string.IsNullOrWhiteSpace(report.FontName) ? "Arial" : report.FontName;
            worksheet.PageSetup.PageOrientation = string.Equals(report.PageOrientation, "Landscape", StringComparison.OrdinalIgnoreCase)
                ? XLPageOrientation.Landscape
                : XLPageOrientation.Portrait;
            worksheet.PageSetup.Margins.Top = 0.25;
            worksheet.PageSetup.Margins.Bottom = 0.25;
            worksheet.PageSetup.Margins.Left = 0.25;
            worksheet.PageSetup.Margins.Right = 0.25;
            worksheet.PageSetup.FitToPages(1, 0);
            worksheet.PageSetup.CenterHorizontally = true;
        }

        private static int ToExcelIndex(double coordinate, double scale, int maxIndex, int minIndex = 1)
        {
            var index = (int)Math.Floor(Math.Max(0d, coordinate) * scale) + 1;
            return Math.Clamp(index, minIndex, maxIndex);
        }

        private static void ApplyFrxExcelBox(IXLWorksheet worksheet, int startRow, int startColumn, int endRow, int endColumn)
        {
            var range = worksheet.Range(startRow, startColumn, endRow, endColumn);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.Black;
        }

        private static void ApplyFrxExcelLine(
            IXLWorksheet worksheet,
            PrintFormElement element,
            int startRow,
            int startColumn,
            int endRow,
            int endColumn)
        {
            var range = worksheet.Range(startRow, startColumn, endRow, endColumn);
            var horizontal = Math.Abs(element.Width) >= Math.Abs(element.Height);
            if (horizontal)
            {
                range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                range.Style.Border.TopBorderColor = XLColor.Black;
                return;
            }

            range.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            range.Style.Border.LeftBorderColor = XLColor.Black;
        }

        private static void ApplyFrxExcelGridSegment(
            IXLWorksheet worksheet,
            FrxGridSegment segment,
            double rowScale,
            double columnScale,
            int maxRows,
            int maxColumns)
        {
            var horizontal = Math.Abs(segment.Y2 - segment.Y1) <= Math.Abs(segment.X2 - segment.X1);
            if (horizontal)
            {
                var row = ToExcelIndex((segment.Y1 + segment.Y2) / 2d, rowScale, maxRows);
                var startColumn = ToExcelIndex(Math.Min(segment.X1, segment.X2), columnScale, maxColumns);
                var endColumn = ToExcelIndex(Math.Max(segment.X1, segment.X2), columnScale, maxColumns, startColumn);
                var range = worksheet.Range(row, startColumn, row, endColumn);
                range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                range.Style.Border.TopBorderColor = XLColor.Black;
                return;
            }

            var column = ToExcelIndex((segment.X1 + segment.X2) / 2d, columnScale, maxColumns);
            var startRow = ToExcelIndex(Math.Min(segment.Y1, segment.Y2), rowScale, maxRows);
            var endRow = ToExcelIndex(Math.Max(segment.Y1, segment.Y2), rowScale, maxRows, startRow);
            var verticalRange = worksheet.Range(startRow, column, endRow, column);
            verticalRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            verticalRange.Style.Border.LeftBorderColor = XLColor.Black;
        }

        private static void WriteFrxExcelText(
            IXLWorksheet worksheet,
            PrintFormElement element,
            string value,
            int startRow,
            int startColumn,
            int endRow,
            int endColumn)
        {
            var range = worksheet.Range(startRow, startColumn, endRow, endColumn);
            var cell = worksheet.Cell(startRow, startColumn);
            cell.Value = value;

            if (startRow != endRow || startColumn != endColumn)
            {
                try
                {
                    range.Merge(false);
                }
                catch
                {
                    // Старые FRX часто имеют наложения. Для Excel важнее открыть отчет, чем упасть на merge.
                }
            }

            range.Style.Font.FontName = string.IsNullOrWhiteSpace(element.FontName) ? "Arial" : element.FontName;
            range.Style.Font.FontSize = Math.Clamp(element.FontSize, 5d, 11d);
            range.Style.Font.Bold = element.Bold;
            range.Style.Font.Italic = element.Italic;
            range.Style.Alignment.WrapText = false;
            range.Style.Alignment.ShrinkToFit = true;
            range.Style.Alignment.Horizontal = element.Alignment switch
            {
                "Right" => XLAlignmentHorizontalValues.Right,
                "Center" => XLAlignmentHorizontalValues.Center,
                _ => XLAlignmentHorizontalValues.Left
            };
        }

        private static string BuildSafeExcelWorksheetName(string? name)
        {
            var invalidChars = new HashSet<char>(new[] { '[', ']', ':', '*', '?', '/', '\\' });
            var safeName = new string((name ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) || char.IsControl(ch) ? ' ' : ch)
                .ToArray())
                .Trim();

            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Report";

            return safeName.Length <= 31 ? safeName : safeName[..31].Trim();
        }

        private readonly record struct FrxGridSegment(double X1, double Y1, double X2, double Y2);

        private static IReadOnlyList<FrxGridSegment> BuildFrxGridSegments(
            PrintFormTemplate template,
            IReadOnlyDictionary<int, ReportElementMapping> mappingByOrder,
            double strokeWidth)
        {
            var segments = new List<FrxGridSegment>();
            foreach (var element in template.Elements)
            {
                if (mappingByOrder.TryGetValue(element.Order, out var mapping) && !mapping.IsVisible)
                    continue;

                if (element.Type == "Line")
                {
                    segments.Add(new FrxGridSegment(element.Left, element.Top, element.Left + element.Width, element.Top + element.Height));
                    continue;
                }

                if (element.Type != "Box")
                    continue;

                var left = element.Left;
                var top = element.Top;
                var right = element.Left + element.Width;
                var bottom = element.Top + element.Height;
                segments.Add(new FrxGridSegment(left, top, right, top));
                segments.Add(new FrxGridSegment(left, bottom, right, bottom));
                segments.Add(new FrxGridSegment(left, top, left, bottom));
                segments.Add(new FrxGridSegment(right, top, right, bottom));
            }

            if (segments.Count == 0)
                return Array.Empty<FrxGridSegment>();

            var tolerance = GetFrxGridSnapTolerance(template, strokeWidth);
            var xAnchors = BuildAxisAnchors(segments.SelectMany(item => new[] { item.X1, item.X2 }), tolerance);
            var yAnchors = BuildAxisAnchors(segments.SelectMany(item => new[] { item.Y1, item.Y2 }), tolerance);
            var snapped = segments
                .Select(segment => SnapGridSegment(segment, xAnchors, yAnchors, tolerance))
                .Where(segment => GetSegmentLength(segment) > tolerance * 0.4d)
                .ToList();

            var merged = MergeFrxGridSegments(snapped, tolerance);
            return CollapseNearDuplicateGridSegments(merged, strokeWidth);
        }

        private static double GetFrxGridSnapTolerance(PrintFormTemplate template, double strokeWidth)
        {
            var pageSize = Math.Max(1d, Math.Min(template.PageWidth, template.PageHeight));
            var coordinateTolerance = pageSize * 0.0035d;
            var strokeTolerance = Math.Max(2d, strokeWidth * 1.5d);
            return Math.Clamp(Math.Max(coordinateTolerance, strokeTolerance), 2d, 60d);
        }

        private static List<double> BuildAxisAnchors(IEnumerable<double> values, double tolerance)
        {
            var sorted = values.Where(value => !double.IsNaN(value) && !double.IsInfinity(value)).OrderBy(value => value).ToList();
            var anchors = new List<double>();
            if (sorted.Count == 0)
                return anchors;

            var group = new List<double> { sorted[0] };
            foreach (var value in sorted.Skip(1))
            {
                var current = group.Average();
                if (Math.Abs(value - current) <= tolerance)
                {
                    group.Add(value);
                    continue;
                }

                anchors.Add(Math.Round(group.Average()));
                group.Clear();
                group.Add(value);
            }

            anchors.Add(Math.Round(group.Average()));
            return anchors;
        }

        private static FrxGridSegment SnapGridSegment(FrxGridSegment segment, IReadOnlyList<double> xAnchors, IReadOnlyList<double> yAnchors, double tolerance)
        {
            var x1 = SnapCoordinate(segment.X1, xAnchors, tolerance);
            var x2 = SnapCoordinate(segment.X2, xAnchors, tolerance);
            var y1 = SnapCoordinate(segment.Y1, yAnchors, tolerance);
            var y2 = SnapCoordinate(segment.Y2, yAnchors, tolerance);

            if (Math.Abs(y2 - y1) <= tolerance)
            {
                var y = SnapCoordinate((y1 + y2) / 2d, yAnchors, tolerance);
                y1 = y;
                y2 = y;
            }

            if (Math.Abs(x2 - x1) <= tolerance)
            {
                var x = SnapCoordinate((x1 + x2) / 2d, xAnchors, tolerance);
                x1 = x;
                x2 = x;
            }

            return new FrxGridSegment(x1, y1, x2, y2);
        }

        private static double SnapCoordinate(double value, IReadOnlyList<double> anchors, double tolerance)
        {
            if (anchors.Count == 0)
                return value;

            var nearest = value;
            var nearestDistance = double.MaxValue;
            foreach (var anchor in anchors)
            {
                var distance = Math.Abs(anchor - value);
                if (distance >= nearestDistance)
                    continue;

                nearest = anchor;
                nearestDistance = distance;
            }

            return nearestDistance <= tolerance ? nearest : value;
        }

        private static double GetSegmentLength(FrxGridSegment segment)
        {
            var dx = segment.X2 - segment.X1;
            var dy = segment.Y2 - segment.Y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static IReadOnlyList<FrxGridSegment> MergeFrxGridSegments(IReadOnlyList<FrxGridSegment> segments, double tolerance)
        {
            var horizontal = segments.Where(segment => Math.Abs(segment.Y2 - segment.Y1) <= tolerance).Select(segment => (Y: (segment.Y1 + segment.Y2) / 2d, Start: Math.Min(segment.X1, segment.X2), End: Math.Max(segment.X1, segment.X2))).ToList();
            var vertical = segments.Where(segment => Math.Abs(segment.X2 - segment.X1) <= tolerance).Select(segment => (X: (segment.X1 + segment.X2) / 2d, Start: Math.Min(segment.Y1, segment.Y2), End: Math.Max(segment.Y1, segment.Y2))).ToList();
            var diagonal = segments.Where(segment => Math.Abs(segment.Y2 - segment.Y1) > tolerance && Math.Abs(segment.X2 - segment.X1) > tolerance).ToList();

            var result = new List<FrxGridSegment>();
            result.AddRange(MergeHorizontalGridSegments(horizontal, tolerance));
            result.AddRange(MergeVerticalGridSegments(vertical, tolerance));
            result.AddRange(diagonal);
            return result.Where(segment => GetSegmentLength(segment) > tolerance * 0.4d).OrderBy(segment => segment.Y1).ThenBy(segment => segment.X1).ToList();
        }

        private static IReadOnlyList<FrxGridSegment> CollapseNearDuplicateGridSegments(IReadOnlyList<FrxGridSegment> segments, double strokeWidth)
        {
            if (segments.Count < 2)
                return segments;

            var duplicateTolerance = Math.Clamp(strokeWidth * 4d, 3d, 140d);
            var result = new List<FrxGridSegment>();
            foreach (var segment in segments.OrderBy(item => Math.Min(item.Y1, item.Y2)).ThenBy(item => Math.Min(item.X1, item.X2)))
            {
                if (TryMergeNearDuplicateGridSegment(result, segment, duplicateTolerance))
                    continue;

                result.Add(segment);
            }

            return result.OrderBy(segment => segment.Y1).ThenBy(segment => segment.X1).ToList();
        }

        private static bool TryMergeNearDuplicateGridSegment(List<FrxGridSegment> result, FrxGridSegment segment, double tolerance)
        {
            var horizontal = IsHorizontalGridSegment(segment, tolerance);
            var vertical = IsVerticalGridSegment(segment, tolerance);
            if (!horizontal && !vertical)
                return false;

            for (var index = 0; index < result.Count; index++)
            {
                var existing = result[index];
                if (horizontal && IsHorizontalGridSegment(existing, tolerance))
                {
                    var existingY = (existing.Y1 + existing.Y2) / 2d;
                    var segmentY = (segment.Y1 + segment.Y2) / 2d;
                    var existingStart = Math.Min(existing.X1, existing.X2);
                    var existingEnd = Math.Max(existing.X1, existing.X2);
                    var segmentStart = Math.Min(segment.X1, segment.X2);
                    var segmentEnd = Math.Max(segment.X1, segment.X2);
                    if (Math.Abs(existingY - segmentY) <= tolerance && IntervalsOverlapEnough(existingStart, existingEnd, segmentStart, segmentEnd))
                    {
                        var y = Math.Round((existingY + segmentY) / 2d);
                        result[index] = new FrxGridSegment(Math.Min(existingStart, segmentStart), y, Math.Max(existingEnd, segmentEnd), y);
                        return true;
                    }
                }

                if (vertical && IsVerticalGridSegment(existing, tolerance))
                {
                    var existingX = (existing.X1 + existing.X2) / 2d;
                    var segmentX = (segment.X1 + segment.X2) / 2d;
                    var existingStart = Math.Min(existing.Y1, existing.Y2);
                    var existingEnd = Math.Max(existing.Y1, existing.Y2);
                    var segmentStart = Math.Min(segment.Y1, segment.Y2);
                    var segmentEnd = Math.Max(segment.Y1, segment.Y2);
                    if (Math.Abs(existingX - segmentX) <= tolerance && IntervalsOverlapEnough(existingStart, existingEnd, segmentStart, segmentEnd))
                    {
                        var x = Math.Round((existingX + segmentX) / 2d);
                        result[index] = new FrxGridSegment(x, Math.Min(existingStart, segmentStart), x, Math.Max(existingEnd, segmentEnd));
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsHorizontalGridSegment(FrxGridSegment segment, double tolerance) =>
            Math.Abs(segment.Y2 - segment.Y1) <= tolerance * 0.5d;

        private static bool IsVerticalGridSegment(FrxGridSegment segment, double tolerance) =>
            Math.Abs(segment.X2 - segment.X1) <= tolerance * 0.5d;

        private static bool IntervalsOverlapEnough(double startA, double endA, double startB, double endB)
        {
            var overlap = Math.Min(endA, endB) - Math.Max(startA, startB);
            if (overlap <= 0)
                return false;

            var shortest = Math.Min(endA - startA, endB - startB);
            return shortest <= 0 || overlap >= shortest * 0.5d;
        }
        private static IEnumerable<FrxGridSegment> MergeHorizontalGridSegments(List<(double Y, double Start, double End)> segments, double tolerance)
        {
            foreach (var group in GroupAxisSegments(segments.Select(item => (Axis: item.Y, item.Start, item.End)).OrderBy(item => item.Axis).ThenBy(item => item.Start), tolerance))
            {
                var y = Math.Round(group.Average(item => item.Axis));
                var intervals = group.Select(item => (item.Start, item.End)).OrderBy(item => item.Start).ToList();
                foreach (var interval in MergeIntervals(intervals, tolerance))
                    yield return new FrxGridSegment(interval.Start, y, interval.End, y);
            }
        }

        private static IEnumerable<FrxGridSegment> MergeVerticalGridSegments(List<(double X, double Start, double End)> segments, double tolerance)
        {
            foreach (var group in GroupAxisSegments(segments.Select(item => (Axis: item.X, item.Start, item.End)).OrderBy(item => item.Axis).ThenBy(item => item.Start), tolerance))
            {
                var x = Math.Round(group.Average(item => item.Axis));
                var intervals = group.Select(item => (item.Start, item.End)).OrderBy(item => item.Start).ToList();
                foreach (var interval in MergeIntervals(intervals, tolerance))
                    yield return new FrxGridSegment(x, interval.Start, x, interval.End);
            }
        }

        private static IEnumerable<List<(double Axis, double Start, double End)>> GroupAxisSegments(IEnumerable<(double Axis, double Start, double End)> orderedSegments, double tolerance)
        {
            var group = new List<(double Axis, double Start, double End)>();
            foreach (var segment in orderedSegments)
            {
                if (group.Count == 0 || Math.Abs(segment.Axis - group.Average(item => item.Axis)) <= tolerance)
                {
                    group.Add(segment);
                    continue;
                }

                yield return group;
                group = new List<(double Axis, double Start, double End)> { segment };
            }

            if (group.Count > 0)
                yield return group;
        }

        private static IEnumerable<(double Start, double End)> MergeIntervals(IReadOnlyList<(double Start, double End)> intervals, double tolerance)
        {
            if (intervals.Count == 0)
                yield break;

            var start = intervals[0].Start;
            var end = intervals[0].End;
            foreach (var interval in intervals.Skip(1))
            {
                if (interval.Start <= end + tolerance)
                {
                    end = Math.Max(end, interval.End);
                    continue;
                }

                yield return (start, end);
                start = interval.Start;
                end = interval.End;
            }

            yield return (start, end);
        }

        private static string BuildTemplateSvg(
            PrintFormTemplate template,
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping> mappings,
            bool templatePrepared = false)
        {
            var layoutTemplate = templatePrepared ? template : FrxRecognitionProfileService.PrepareForRendering(template);
            var width = Math.Max(1000, layoutTemplate.PageWidth);
            var height = Math.Max(1000, layoutTemplate.PageHeight);
            var useCompactScale = width < 5000 && height < 5000;
            var fontScale = useCompactScale ? 10d : 105d;
            var minFontSize = useCompactScale ? 32d : 700d;
            var strokeWidth = useCompactScale ? 5d : 80d;
            var mappingByOrder = mappings
                .GroupBy(item => item.ElementOrder)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());
            var svg = new StringBuilder();
            svg.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {width.ToString(CultureInfo.InvariantCulture)} {height.ToString(CultureInfo.InvariantCulture)}'>");
            svg.Append("<rect x='0' y='0' width='100%' height='100%' fill='white'/>");
            var textElements = layoutTemplate.Elements
                .Where(item => item.Type is "Text" or "Expression")
                .ToList();
            var useClipPaths = layoutTemplate.Elements.Count <= 700 && textElements.Count <= 350;
            if (useClipPaths)
            {
                svg.Append("<defs>");
                foreach (var element in textElements)
                {
                    var clipHeight = Math.Max(element.Height, Math.Max(minFontSize, element.FontSize * fontScale) * 1.4);
                    svg.Append($"<clipPath id='clip{element.Order}'><rect x='{element.Left.ToString(CultureInfo.InvariantCulture)}' y='{element.Top.ToString(CultureInfo.InvariantCulture)}' width='{element.Width.ToString(CultureInfo.InvariantCulture)}' height='{clipHeight.ToString(CultureInfo.InvariantCulture)}'/></clipPath>");
                }
                svg.Append("</defs>");
            }
            foreach (var segment in BuildFrxGridSegments(layoutTemplate, mappingByOrder, strokeWidth))
                AppendSvgGridLine(svg, segment, strokeWidth);

            foreach (var element in layoutTemplate.Elements.OrderBy(item => item.Order))
            {
                if (mappingByOrder.TryGetValue(element.Order, out var hiddenMapping) && !hiddenMapping.IsVisible)
                    continue;

                if (element.Type is "Line" or "Box" or "Picture")
                    continue;
                var value = ResolveElementValue(report, layoutTemplate, element, data, mappingByOrder);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                var anchor = element.Alignment == "Right" ? "end" : element.Alignment == "Center" ? "middle" : "start";
                var textX = element.Alignment == "Right" ? element.Left + element.Width :
                    element.Alignment == "Center" ? element.Left + element.Width / 2 : element.Left;
                var fontSize = Math.Max(minFontSize, element.FontSize * fontScale);
                var fontWeight = element.Bold ? "bold" : "normal";
                var fontStyle = element.Italic ? "italic" : "normal";
                var lines = value.Replace("\r", string.Empty).Split('\n');
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var textY = element.Top + Math.Max(fontSize, element.Height * 0.75) + lineIndex * fontSize * 1.1;
                    var clipAttribute = useClipPaths ? $" clip-path='url(#clip{element.Order})'" : string.Empty;
                    svg.Append($"<text x='{textX.ToString(CultureInfo.InvariantCulture)}' y='{textY.ToString(CultureInfo.InvariantCulture)}'{clipAttribute} text-anchor='{anchor}' font-family='{EscapeXml(element.FontName)}' font-size='{fontSize.ToString(CultureInfo.InvariantCulture)}' font-weight='{fontWeight}' font-style='{fontStyle}'>{EscapeXml(lines[lineIndex])}</text>");
                }
            }
            svg.Append("</svg>");
            return svg.ToString();
        }

        private static void AppendSvgGridLine(StringBuilder svg, FrxGridSegment segment, double strokeWidth)
        {
            var x1 = segment.X1;
            var y1 = segment.Y1;
            var x2 = segment.X2;
            var y2 = segment.Y2;
            SnapLineCoordinates(ref x1, ref y1, ref x2, ref y2, strokeWidth);
            svg.Append($"<line x1='{x1.ToString(CultureInfo.InvariantCulture)}' y1='{y1.ToString(CultureInfo.InvariantCulture)}' x2='{x2.ToString(CultureInfo.InvariantCulture)}' y2='{y2.ToString(CultureInfo.InvariantCulture)}' stroke='black' stroke-width='{strokeWidth.ToString(CultureInfo.InvariantCulture)}' stroke-linecap='square' shape-rendering='crispEdges'/>");
        }

        private static void SnapLineCoordinates(ref double x1, ref double y1, ref double x2, ref double y2, double strokeWidth)
        {
            var tolerance = Math.Max(1d, strokeWidth * 0.75d);
            if (Math.Abs(y2 - y1) <= tolerance)
            {
                var y = Math.Round((y1 + y2) / 2d);
                y1 = y;
                y2 = y;
            }

            if (Math.Abs(x2 - x1) <= tolerance)
            {
                var x = Math.Round((x1 + x2) / 2d);
                x1 = x;
                x2 = x;
            }
        }
        private static PrintFormTemplate CompactImportedTemplateVerticalGaps(PrintFormTemplate template)
        {
            if (!ShouldCompactImportedTemplate(template) || template.Elements.Count < 2)
            {
                return template;
            }

            var orderedElements = template.Elements
                .Where(element => element.Height >= 0)
                .OrderBy(element => element.Top)
                .ThenBy(element => element.Left)
                .ToList();
            if (orderedElements.Count < 2)
                return template;

            var desiredGap = Math.Clamp(template.PageHeight * 0.012, 500, 900);
            var minGapToCompact = desiredGap * 2.2;
            var currentBottom = orderedElements[0].Top + orderedElements[0].Height;
            var shifts = new List<(double ThresholdTop, double Shift)>();

            foreach (var element in orderedElements.Skip(1))
            {
                if (element.Top > currentBottom)
                {
                    var gap = element.Top - currentBottom;
                    if (gap > minGapToCompact)
                    {
                        shifts.Add((element.Top, gap - desiredGap));
                    }
                }

                currentBottom = Math.Max(currentBottom, element.Top + element.Height);
            }

            if (shifts.Count == 0)
                return template;

            var shiftedElements = template.Elements.Select(element =>
            {
                var shift = shifts
                    .Where(item => element.Top >= item.ThresholdTop)
                    .Sum(item => item.Shift);

                return new PrintFormElement
                {
                    Type = element.Type,
                    Text = element.Text,
                    Expression = element.Expression,
                    BandType = element.BandType,
                    Left = element.Left,
                    Top = element.Top - shift,
                    Width = element.Width,
                    Height = element.Height,
                    FontName = element.FontName,
                    FontSize = element.FontSize,
                    Bold = element.Bold,
                    Italic = element.Italic,
                    Alignment = element.Alignment,
                    BorderStyle = element.BorderStyle,
                    Order = element.Order
                };
            }).ToList();

            var shiftedBands = template.Bands.Select(band =>
            {
                var shift = shifts
                    .Where(item => band.Top >= item.ThresholdTop)
                    .Sum(item => item.Shift);

                return new PrintFormBand
                {
                    Type = band.Type,
                    Top = band.Top - shift,
                    Height = band.Height,
                    Order = band.Order
                };
            }).ToList();

            var contentBottom = shiftedElements.Max(element => element.Top + element.Height);
            return new PrintFormTemplate
            {
                SourceFormat = template.SourceFormat,
                OriginalFileName = template.OriginalFileName,
                PageWidth = template.PageWidth,
                PageHeight = Math.Max(1000, contentBottom + desiredGap),
                Bands = shiftedBands,
                Elements = shiftedElements
            };
        }

        private static bool ShouldCompactImportedTemplate(PrintFormTemplate template)
        {
            return template.SourceFormat.Equals("FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
                   template.OriginalFileName.EndsWith(".frx", StringComparison.OrdinalIgnoreCase) ||
                   template.PageWidth > 10000 ||
                   template.PageHeight > 10000;
        }

        private static string ResolveElementValue(
            Report report,
            PrintFormTemplate template,
            PrintFormElement element,
            CashOrderPrintData data,
            IReadOnlyDictionary<int, ReportElementMapping> mappingByOrder)
        {
            if (mappingByOrder.TryGetValue(element.Order, out var mapping))
            {
                if (!string.IsNullOrWhiteSpace(mapping.CustomText))
                    return ReplacePlaceholders(mapping.CustomText, data, mapping.FormatString);

                if (!string.IsNullOrWhiteSpace(mapping.MappedFieldName))
                    return GetPrintDataValue(data, mapping.MappedFieldName, mapping.FormatString);
            }

            if (element.Type == "Expression")
            {
                if (template.SourceFormat == "Native" || report.SourceFormat == "Native")
                    return GetPrintDataValue(data, element.Expression, string.Empty);

                return EvaluateFoxExpression(element.Expression, data);
            }

            var text = UnquoteFoxText(element.Text);
            if (LooksLikeFoxExpression(text))
            {
                var value = EvaluateFoxExpression(text, data);
                return string.Equals(value, text, StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
            }

            return ReplacePlaceholders(text, data, string.Empty);
        }

        private static string ReplacePlaceholders(string text, CashOrderPrintData data, string formatString)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, @"\{([^{}]+)\}", match =>
                GetPrintDataValue(data, match.Groups[1].Value, formatString));
        }

        private static string GetPrintDataValue(CashOrderPrintData data, string fieldName, string formatString)
        {
            var normalized = NormalizeFieldName(fieldName);
            object? value = normalized switch
            {
                "document_name" => data.DocumentName,
                "number" or "doc_number" or "dok1" => data.Number,
                "date" or "doc_date" or "date1" => data.Date,
                "organization" or "organization_id" or "nfil1" => data.Organization,
                "inn" or "inn1" or "organization_inn" => data.Inn,
                "okpo" or "okpo1" or "organization_okpo" => data.Okpo,
                "person" or "employee_id" or "responsible_person_id" or "fio" or "fiop1" or "namep1" => data.Person,
                "cash_desk" or "cash_desk_id" or "cash" => data.CashDesk,
                "correspondent_account" or "corr_account" or "account" => data.CorrespondentAccount,
                "debit_account" or "debit" or "deb" => GetAccountDisplayName(data.DebitAccount),
                "credit_account" or "credit" or "cred" => GetAccountDisplayName(data.CreditAccount),
                "debit_code" or "deb1" => GetAccountCode(data.DebitAccount),
                "credit_code" or "cred1" => GetAccountCode(data.CreditAccount),
                "amount" or "sum" or "sum1" => data.Amount,
                "amount_currency" or "amount_in_currency" or "sum_v" => data.AmountInCurrency,
                "amount_in_words" or "msum1" => data.AmountInWords,
                "basis" or "tex1" => data.Basis,
                "note" or "description" => data.Note,
                "currency" or "nval1" => "KGS",
                "rate" or "kurs_v" => "1,00",
                _ => TryGetExtraFieldValue(data, fieldName, normalized)
            };

            return value switch
            {
                DateTime date => string.IsNullOrWhiteSpace(formatString)
                    ? date.ToString("dd.MM.yyyy")
                    : date.ToString(formatString),
                decimal number => string.IsNullOrWhiteSpace(formatString)
                    ? number.ToString("N2")
                    : number.ToString(formatString),
                double number => string.IsNullOrWhiteSpace(formatString)
                    ? number.ToString("N2")
                    : number.ToString(formatString),
                float number => string.IsNullOrWhiteSpace(formatString)
                    ? number.ToString("N2")
                    : number.ToString(formatString),
                _ => value?.ToString() ?? string.Empty
            };
        }

        private static object? TryGetExtraFieldValue(CashOrderPrintData data, string fieldName, string normalized)
        {
            if (data.ExtraFields.Count == 0)
                return null;

            var direct = (fieldName ?? string.Empty).Trim().Trim('{', '}', '(', ')');
            if (data.ExtraFields.TryGetValue(direct, out var value))
                return value;
            if (data.ExtraFields.TryGetValue(normalized, out value))
                return value;

            var compact = Regex.Replace(normalized, @"[\s\.\-]+", "_");
            return data.ExtraFields.TryGetValue(compact, out value) ? value : null;
        }

        private static string NormalizeFieldName(string fieldName)
        {
            var value = (fieldName ?? string.Empty).Trim().Trim('{', '}', '(', ')').ToLowerInvariant();
            return value switch
            {
                "номер" or "номер документа" => "number",
                "дата" => "date",
                "организация" => "organization",
                "инн" => "inn",
                "окпо" => "okpo",
                "сотрудник" or "мол" or "получатель" or "принято от" or "выдать" => "person",
                "касса" => "cash_desk",
                "корр. счет" or "корр счет" or "корреспондентский счет" => "correspondent_account",
                "дебет" => "debit_account",
                "кредит" => "credit_account",
                "сумма" => "amount",
                "сумма в валюте" => "amount_in_currency",
                "сумма прописью" => "amount_in_words",
                "основание" => "basis",
                "примечание" => "note",
                "валюта" => "currency",
                "номер эсф" or "эсф" => "esf_number",
                "счет" or "счёт" => "counterparty_account",
                "вид оплаты" => "payment_kind",
                "вид поставки" => "delivery_kind",
                "тип поставки" => "supply_kind",
                "сумма без налогов" or "без налогов" => "amount_without_tax",
                "сумма ндс" or "ндс" => "vat_total",
                "налог с продаж" => "sales_tax_total",
                "итого" or "всего" or "всего к оплате" => "total_amount",
                "проведен" or "проведён" => "is_posted",
                _ => Regex.Replace(value, @"[\s\.\-]+", "_")
            };
        }

        private static string EvaluateFoxExpression(string expression, CashOrderPrintData data)
        {
            if (string.IsNullOrWhiteSpace(expression)) return string.Empty;
            var normalized = expression.Trim().Trim('{', '}');
            var lower = normalized.ToLowerInvariant();

            if (IsQuotedFoxLiteral(normalized))
                return UnquoteFoxText(normalized);

            var variables = BuildFoxVariables(data);
            if (TryResolveFoxVariable(variables, normalized, out var variableValue))
                return variableValue;

            var concatParts = SplitFoxTopLevel(normalized, '+');
            if (concatParts.Count > 1)
            {
                var values = concatParts.Select(part => EvaluateFoxExpression(part, data)).ToList();
                if (values.Count > 0 && values.All(TryParseFoxDecimal))
                    return values.Sum(ParseFoxDecimal).ToString("N2");
                return string.Concat(values);
            }

            if (TryParseFoxFunction(normalized, out var functionName, out var arguments))
            {
                if (functionName.Equals("iif", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 3)
                    return EvaluateFoxCondition(arguments[0], data)
                        ? EvaluateFoxExpression(arguments[1], data)
                        : EvaluateFoxExpression(arguments[2], data);

                if ((functionName.Equals("alltrim", StringComparison.OrdinalIgnoreCase) ||
                     functionName.Equals("alltr", StringComparison.OrdinalIgnoreCase) ||
                     functionName.Equals("trim", StringComparison.OrdinalIgnoreCase)) &&
                    arguments.Count >= 1)
                    return EvaluateFoxExpression(arguments[0], data).Trim();

                if ((functionName.Equals("substr", StringComparison.OrdinalIgnoreCase) ||
                     functionName.Equals("subs", StringComparison.OrdinalIgnoreCase)) &&
                    arguments.Count >= 2)
                {
                    var value = EvaluateFoxExpression(arguments[0], data);
                    var start = Math.Max(0, ParseFoxInteger(arguments[1], data) - 1);
                    if (start >= value.Length)
                        return string.Empty;
                    if (arguments.Count < 3)
                        return value[start..];
                    var length = ParseFoxInteger(arguments[2], data);
                    return length <= 0 ? string.Empty : value.Substring(start, Math.Min(length, value.Length - start));
                }

                if (functionName.Equals("left", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 2)
                {
                    var value = EvaluateFoxExpression(arguments[0], data);
                    var length = ParseFoxInteger(arguments[1], data);
                    return length <= 0 ? string.Empty : value[..Math.Min(length, value.Length)];
                }

                if (functionName.Equals("right", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 2)
                {
                    var value = EvaluateFoxExpression(arguments[0], data);
                    var length = ParseFoxInteger(arguments[1], data);
                    return length <= 0 ? string.Empty : value[^Math.Min(length, value.Length)..];
                }

                if (functionName.Equals("str", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
                {
                    var value = EvaluateFoxExpression(arguments[0], data);
                    if (arguments.Count >= 2)
                    {
                        var width = ParseFoxInteger(arguments[1], data);
                        if (width > value.Length)
                            value = value.PadLeft(width);
                    }
                    return value;
                }

                if (functionName.Equals("date", StringComparison.OrdinalIgnoreCase) && arguments.Count == 0)
                    return DateTime.Today.ToString("dd.MM.yyyy");

                if (functionName.Equals("dtoc", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
                {
                    var value = EvaluateFoxExpression(arguments[0], data);
                    return TryParseFoxDate(value, out var dateValue)
                        ? dateValue.ToString("dd.MM.yyyy")
                        : value;
                }

                if (functionName.Equals("ctod", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
                    return EvaluateFoxExpression(arguments[0], data);

                if (functionName.Equals("mr", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
                    return GetRussianMonthName(ParseFoxInteger(arguments[0], data));

                if ((functionName.Equals("tran", StringComparison.OrdinalIgnoreCase) ||
                     functionName.Equals("transform", StringComparison.OrdinalIgnoreCase)) &&
                    arguments.Count >= 1)
                    return EvaluateFoxExpression(arguments[0], data);

                if (functionName.Equals("empty", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
                    return string.IsNullOrWhiteSpace(EvaluateFoxExpression(arguments[0], data)) ? ".T." : ".F.";

                if (functionName.Equals("day", StringComparison.OrdinalIgnoreCase))
                {
                    var dateValue = arguments.Count >= 1 && TryParseFoxDate(EvaluateFoxExpression(arguments[0], data), out var parsedDate)
                        ? parsedDate
                        : data.Date;
                    return dateValue.Day.ToString(CultureInfo.InvariantCulture);
                }
                if (functionName.Equals("month", StringComparison.OrdinalIgnoreCase))
                {
                    var dateValue = arguments.Count >= 1 && TryParseFoxDate(EvaluateFoxExpression(arguments[0], data), out var parsedDate)
                        ? parsedDate
                        : data.Date;
                    return dateValue.Month.ToString(CultureInfo.InvariantCulture);
                }
                if (functionName.Equals("year", StringComparison.OrdinalIgnoreCase))
                {
                    var dateValue = arguments.Count >= 1 && TryParseFoxDate(EvaluateFoxExpression(arguments[0], data), out var parsedDate)
                        ? parsedDate
                        : data.Date;
                    return dateValue.Year.ToString(CultureInfo.InvariantCulture);
                }
            }

            var wrapped = Regex.Match(normalized, @"(?i)^(?:alltrim|alltr|trim)\((.+)\)$");
            if (wrapped.Success)
                return EvaluateFoxExpression(wrapped.Groups[1].Value, data);

            var left = Regex.Match(normalized, @"(?i)^left\((.+),\s*(\d+)\)$");
            if (left.Success)
            {
                var value = EvaluateFoxExpression(left.Groups[1].Value, data);
                var length = int.Parse(left.Groups[2].Value);
                return value[..Math.Min(length, value.Length)];
            }

            var substr = Regex.Match(normalized, @"(?i)^(?:substr|subs)\((.+),\s*(\d+)(?:,\s*(\d+))?\)$");
            if (substr.Success)
            {
                var value = EvaluateFoxExpression(substr.Groups[1].Value, data);
                var start = Math.Max(0, int.Parse(substr.Groups[2].Value) - 1);
                if (start >= value.Length)
                    return string.Empty;
                if (!substr.Groups[3].Success)
                    return value[start..];
                var length = int.Parse(substr.Groups[3].Value);
                return value.Substring(start, Math.Min(length, value.Length - start));
            }

            var directValue = GetPrintDataValue(data, normalized, string.Empty);
            if (!string.IsNullOrWhiteSpace(directValue))
                return directValue;

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

            if (TryResolveFoxVariable(variables, normalized, out var direct)) return direct;
            foreach (var (name, value) in variables.OrderByDescending(item => item.Key.Length))
                normalized = Regex.Replace(normalized, $@"(?<![\w.]){Regex.Escape(name)}(?![\w.])", value.Replace("$", "$$"), RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"(?i)alltrim\(([^()]*)\)", "$1");
            normalized = Regex.Replace(normalized, @"(?i)alltr\(([^()]*)\)", "$1");
            normalized = Regex.Replace(normalized, @"(?i)str\(([^,()]+)(?:,[^()]*)?\)", "$1");
            normalized = normalized.Replace("'+'", string.Empty).Replace("+", string.Empty).Replace("'", string.Empty).Replace("\"", string.Empty);
            normalized = normalized.Trim();
            return LooksLikeFoxExpression(normalized) ? string.Empty : normalized;
        }

        private static Dictionary<string, string> BuildFoxVariables(CashOrderPrintData data)
        {
            var currencyAmount = data.AmountInCurrency == 0 ? data.Amount : data.AmountInCurrency;
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["nfil1"] = data.Organization,
                ["inn1"] = data.Inn,
                ["okpo1"] = data.Okpo,
                ["dok1"] = data.Number,
                ["date1"] = data.Date.ToString("dd.MM.yyyy"),
                ["deb"] = GetAccountCode(data.DebitAccount),
                ["deb1"] = GetAccountCode(data.DebitAccount),
                ["cred"] = GetAccountCode(data.CreditAccount),
                ["cred1"] = GetAccountCode(data.CreditAccount),
                ["sum"] = $"{data.Amount:N2}",
                ["sum1"] = $"{data.Amount:N2}",
                ["sum_v"] = $"{currencyAmount:N2}",
                ["msum1"] = data.AmountInWords,
                ["tex1"] = data.Basis,
                ["fio"] = data.Person,
                ["fiop1"] = data.Person,
                ["namep1"] = data.Person,
                ["kodp1"] = string.Empty,
                ["kurs_v"] = "1,00",
                ["nval1"] = "KGS",
                ["nakl1"] = string.Empty,
                ["dovn1"] = string.Empty,
                ["dovd1"] = string.Empty,
                ["dovf1"] = string.Empty,
                ["_PAGENO"] = "1",
                ["_pageno"] = "1",
                ["pageno"] = "1",
                ["date()"] = DateTime.Today.ToString("dd.MM.yyyy"),
                ["na1"] = data.ExtraFields.TryGetValue("sha2", out var summaryValue) ? FormatPlainValue(summaryValue) : data.Note,
                ["p1"] = "Руководитель",
                ["p2"] = "Главный бухгалтер",
                ["p3"] = "Руководитель контрагента",
                ["p4"] = "Главный бухгалтер контрагента"
            };

            foreach (var pair in FrxRecognitionProfileService.GetDefaultVariables())
                variables[pair.Key] = pair.Value;

            foreach (var extra in data.ExtraFields)
            {
                var rawKey = (extra.Key ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(rawKey))
                    variables[rawKey] = FormatPlainValue(extra.Value);

                var normalizedKey = NormalizeFieldName(extra.Key);
                if (!string.IsNullOrWhiteSpace(normalizedKey))
                    variables[normalizedKey] = FormatPlainValue(extra.Value);
            }

            return variables;
        }

        private static bool TryResolveFoxVariable(
            IReadOnlyDictionary<string, string> variables,
            string expression,
            out string value)
        {
            value = string.Empty;
            var key = (expression ?? string.Empty).Trim().Trim('{', '}', '(', ')');
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (variables.TryGetValue(key, out value))
                return true;

            var normalizedKey = NormalizeFieldName(key);
            if (variables.TryGetValue(normalizedKey, out value))
                return true;

            if (TryParseFoxDecimal(key))
            {
                value = key;
                return true;
            }

            return false;
        }

        private static bool TryParseFoxDate(string value, out DateTime date)
        {
            var text = (value ?? string.Empty).Trim().Trim('"', '\'');
            return DateTime.TryParseExact(text,
                       new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.None,
                       out date) ||
                   DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ||
                   DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static string GetRussianMonthName(int month)
        {
            var months = new[]
            {
                string.Empty,
                "января",
                "февраля",
                "марта",
                "апреля",
                "мая",
                "июня",
                "июля",
                "августа",
                "сентября",
                "октября",
                "ноября",
                "декабря"
            };
            return month is >= 1 and <= 12 ? months[month] : string.Empty;
        }

        private static bool EvaluateFoxCondition(string expression, CashOrderPrintData data)
        {
            var value = (expression ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var notEmpty = Regex.Match(value, @"(?i)^!\s*empty\((.+)\)$");
            if (notEmpty.Success)
                return !string.IsNullOrWhiteSpace(EvaluateFoxExpression(notEmpty.Groups[1].Value, data));
            var empty = Regex.Match(value, @"(?i)^empty\((.+)\)$");
            if (empty.Success)
                return string.IsNullOrWhiteSpace(EvaluateFoxExpression(empty.Groups[1].Value, data));
            if (value.Equals(".T.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals(".F.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            var comparison = Regex.Match(value, @"(?i)^(.+?)\s*(==|=|<>|#|!=)\s*(.+)$");
            if (comparison.Success)
            {
                var left = EvaluateFoxExpression(comparison.Groups[1].Value, data);
                var right = EvaluateFoxExpression(comparison.Groups[3].Value, data);
                var equals = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
                return comparison.Groups[2].Value is "<>" or "#" or "!=" ? !equals : equals;
            }

            return !string.IsNullOrWhiteSpace(EvaluateFoxExpression(value, data));
        }

        private static bool TryParseFoxDecimal(string value) =>
            decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out _) ||
            decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out _);

        private static decimal ParseFoxDecimal(string value)
        {
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
                return current;
            return decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant)
                ? invariant
                : 0;
        }

        private static int ParseFoxInteger(string expression, CashOrderPrintData data)
        {
            var value = EvaluateFoxExpression(expression, data);
            if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                int.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                return parsed;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue) ||
                decimal.TryParse(value?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out decimalValue) ||
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out decimalValue))
                return (int)decimalValue;

            var raw = (expression ?? string.Empty).Trim();
            return int.TryParse(raw, out parsed) ? parsed : 0;
        }

        private static bool IsQuotedFoxLiteral(string value)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Length >= 2 &&
                   ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''));
        }

        private static bool LooksLikeFoxExpression(string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (Regex.IsMatch(text, @"(?i)\b(?:substr|subs|alltrim|alltr|trim|iif|str|dtoc|ctod|day|month|year|transform|tran)\s*\("))
                return true;
            if (Regex.IsMatch(text, @"(?i)\b(?:fact|curFACTSW|irfactsw|ved|ved2|ved3|ved4|db_cr|db_crs|dbcr|dbcrs|ksprorg|avt_p)[._][A-Za-z0-9_]+\b"))
                return true;
            if (Regex.IsMatch(text, @"^[A-Z]{1,4}_[A-Z0-9_]+$"))
                return true;
            return false;
        }

        private static List<string> SplitFoxTopLevel(string value, char delimiter)
        {
            var result = new List<string>();
            var start = 0;
            var depth = 0;
            var quote = '\0';

            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (quote != '\0')
                {
                    if (character == quote)
                        quote = '\0';
                    continue;
                }

                if (character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }

                if (character == '(')
                    depth++;
                else if (character == ')' && depth > 0)
                    depth--;
                else if (character == delimiter && depth == 0)
                {
                    result.Add(value[start..i].Trim());
                    start = i + 1;
                }
            }

            if (start == 0)
                return new List<string> { value };

            result.Add(value[start..].Trim());
            return result;
        }

        private static string FormatPlainValue(object? value)
        {
            return value switch
            {
                DateTime date => date.ToString("dd.MM.yyyy"),
                decimal number => number.ToString("N2"),
                double number => number.ToString("N2"),
                float number => number.ToString("N2"),
                _ => value?.ToString() ?? string.Empty
            };
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

        private static string GetAccountDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            // If value is already a display name with code prefix (e.g. "11100000 - Денежные средства"),
            // extract just the display name part after " - " or " – "
            var match = Regex.Match(value, @"[-–]\s*(.+)$");
            return match.Success ? match.Groups[1].Value.Trim() : GetAccountCode(value);
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

        private static bool GetBoolean(Dictionary<string, object> row, params string[] names)
        {
            foreach (var name in names)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is bool boolValue)
                    return boolValue;

                var value = pair.Value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (bool.TryParse(value, out var parsed))
                    return parsed;

                if (value.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("1", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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
            return await ParseFrxFileTemplateAsync(fileData, fileName);
        }

        public static async Task<string> ParseFrxFileTemplateAsync(byte[] fileData, string fileName)
        {
            try
            {
                // Создаем временный файл
                var extension = Path.GetExtension(fileName);
                var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{(string.IsNullOrWhiteSpace(extension) ? ".frx" : extension)}");
                var tempFrt = Path.ChangeExtension(tempFile, ".FRT");
                await File.WriteAllBytesAsync(tempFile, fileData);
                var sourceFrt = Path.ChangeExtension(fileName, ".FRT");
                if (File.Exists(sourceFrt))
                    File.Copy(sourceFrt, tempFrt, overwrite: true);

                // Используем FrxParser
                var parser = new FrxParser();
                var frxReport = parser.ParseFrxFile(tempFile);

                // Удаляем временный файл
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                if (File.Exists(tempFrt))
                    File.Delete(tempFrt);

                // Получаем PrintFormTemplate из FrxReport
                var template = parser.GetPrintTemplate(frxReport);
                FrxRecognitionProfileService.ApplyImportProfile(template);

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

        // Извлечение полей отчета из PrintFormTemplate (JSON макета FoxPro)
        public static List<ReportField> ExtractReportFieldsFromTemplate(string templateJson)
        {
            var fields = new List<ReportField>();
            if (string.IsNullOrWhiteSpace(templateJson))
                return fields;

            try
            {
                var template = DeserializePrintTemplate(templateJson);
                if (template == null)
                    return fields;

                int order = 0;
                var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in template.Elements.OrderBy(e => e.Order))
                {
                    // Пропускаем линии и рамки — они не являются полями данных
                    if (element.Type is "Line" or "Box" or "Picture")
                        continue;

                    var fieldName = string.IsNullOrWhiteSpace(element.Expression)
                        ? element.Text
                        : element.Expression;

                    if (!TryNormalizeExtractedReportField(fieldName, out var normalizedField, out var displayName))
                        continue;
                    if (!seenFields.Add(normalizedField))
                        continue;

                    fields.Add(new ReportField
                    {
                        Id = Guid.NewGuid(),
                        FieldName = normalizedField,
                        DisplayName = displayName,
                        Width = Math.Max(90, (int)Math.Round(element.Width)),
                        Alignment = element.Alignment.ToLower(),
                        IsVisible = true,
                        Order = order++
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка извлечения полей из шаблона: {ex.Message}");
            }

            return fields;
        }

        private static bool TryNormalizeExtractedReportField(string rawFieldName, out string fieldName, out string displayName)
        {
            fieldName = string.Empty;
            displayName = string.Empty;

            var expression = (rawFieldName ?? string.Empty).Trim().Trim('"', '\'', ' ', '=', '{', '}');
            if (string.IsNullOrWhiteSpace(expression) || expression is "+" or "-")
                return false;

            var token = ExtractFoxDataToken(expression);
            if (string.IsNullOrWhiteSpace(token))
                return false;

            fieldName = token;
            displayName = GetFriendlyReportFieldName(token);
            return true;
        }

        private static string ExtractFoxDataToken(string expression)
        {
            var value = (expression ?? string.Empty).Trim().Trim('"', '\'', ' ', '=', '{', '}');
            for (var i = 0; i < 8; i++)
            {
                if (!TryParseFoxFunction(value, out var functionName, out var arguments) || arguments.Count == 0)
                    break;

                if (functionName.Equals("alltrim", StringComparison.OrdinalIgnoreCase) ||
                    functionName.Equals("alltr", StringComparison.OrdinalIgnoreCase) ||
                    functionName.Equals("trim", StringComparison.OrdinalIgnoreCase) ||
                    functionName.Equals("substr", StringComparison.OrdinalIgnoreCase) ||
                    functionName.Equals("subs", StringComparison.OrdinalIgnoreCase) ||
                    functionName.Equals("left", StringComparison.OrdinalIgnoreCase))
                {
                    value = arguments[0].Trim();
                    continue;
                }

                break;
            }

            var direct = value.Trim().Trim('"', '\'', ' ', '=', '{', '}');
            if (Regex.IsMatch(direct, @"^[A-Za-z][A-Za-z0-9]*[._][A-Za-z0-9_]+$"))
                return direct;

            var match = Regex.Match(direct, @"(?i)\b(?:fact|curFACTSW|irfactsw|ved|ved2|ved3|ved4|db_cr|db_crs|dbcr|dbcrs|ksprorg|avt_p)[._][A-Za-z0-9_]+\b");
            return match.Success ? match.Value : string.Empty;
        }

        private static bool TryParseFoxFunction(string expression, out string functionName, out List<string> arguments)
        {
            functionName = string.Empty;
            arguments = new List<string>();

            var value = (expression ?? string.Empty).Trim();
            var openIndex = value.IndexOf('(');
            if (openIndex <= 0 || !value.EndsWith(")", StringComparison.Ordinal))
                return false;

            functionName = value[..openIndex].Trim();
            if (string.IsNullOrWhiteSpace(functionName) ||
                !Regex.IsMatch(functionName, @"^[A-Za-z][A-Za-z0-9_]*$"))
                return false;

            var inner = value.Substring(openIndex + 1, value.Length - openIndex - 2);
            arguments = SplitFoxArguments(inner);
            return true;
        }

        private static List<string> SplitFoxArguments(string value)
        {
            var result = new List<string>();
            var start = 0;
            var depth = 0;
            var quote = '\0';

            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (quote != '\0')
                {
                    if (character == quote)
                        quote = '\0';
                    continue;
                }

                if (character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }

                if (character == '(')
                    depth++;
                else if (character == ')' && depth > 0)
                    depth--;
                else if (character == ',' && depth == 0)
                {
                    result.Add(value[start..i].Trim());
                    start = i + 1;
                }
            }

            result.Add(value[start..].Trim());
            return result;
        }

        private static string GetFriendlyReportFieldName(string fieldName)
        {
            var normalized = (fieldName ?? string.Empty).Trim();
            var organizationMatch = Regex.Match(normalized, @"(?i)^fact[._]([CD])[_\.]([A-Z0-9_]+)$");
            if (organizationMatch.Success)
            {
                var organization = organizationMatch.Groups[1].Value.Equals("C", StringComparison.OrdinalIgnoreCase)
                    ? "Организация А"
                    : "Организация Б";
                var property = organizationMatch.Groups[2].Value.ToUpperInvariant() switch
                {
                    "NAME_ORG" or "ORG" => "наименование",
                    "INN" => "ИНН",
                    "OKPO" => "ОКПО",
                    "ADR" => "адрес",
                    "PHONE" => "телефон",
                    "EMAIL" => "email",
                    "BANK" => "банк",
                    "RS" => "расчетный счет",
                    "BIK" => "БИК",
                    "DIR" => "руководитель",
                    "BUH" => "главный бухгалтер",
                    _ => organizationMatch.Groups[2].Value
                };
                return $"{organization} - {property}";
            }

            var upper = normalized.ToUpperInvariant();
            return upper switch
            {
                "FACT.SER_BL" or "FACT_SER_BL" => "Счет-фактура - серия/ЭСФ",
                "FACT.NOM_BL" or "FACT_NOM_BL" => "Счет-фактура - номер",
                "FACT.D_SALE" or "FACT_D_SALE" => "Счет-фактура - дата",
                "FACT.NAME_SALE" or "FACT_NAME_SALE" => "Счет-фактура - вид поставки",
                "FACT.NAME_OP" or "FACT_NAME_OP" => "Счет-фактура - вид оплаты",
                "FACT.TXT_KOR" or "FACT_TXT_KOR" => "Счет-фактура - основание",
                "CURFACTSW.SUM" => "Итого",
                "CURFACTSW.NDC" => "НДС",
                "CURFACTSW.NALOG" => "Налог с продаж",
                "CURFACTSW.00" => "Сумма без налогов",
                _ => normalized
            };
        }

        public static PrintFormTemplate DeserializePrintTemplate(string templateJson)
        {
            if (string.IsNullOrWhiteSpace(templateJson))
                return new PrintFormTemplate();
            var template = new FrxParser().GetPrintTemplate(new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = "Template",
                FrxXml = templateJson
            });
            return FrxRecognitionProfileService.ApplyImportProfile(template);
        }

        public static string CleanFoxText(string input)
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

        private static string ReadPreviewText(DataRow row, params string[] columns)
        {
            var value = ReadPreviewObject(row, columns);
            return FormatPlainValue(value);
        }

        private static object? ReadPreviewObject(DataRow row, params string[] columns)
        {
            foreach (var column in columns.Where(column => !string.IsNullOrWhiteSpace(column)))
            {
                if (row.Table.Columns.Contains(column))
                {
                    var value = row[column];
                    if (value != null && value != DBNull.Value)
                        return value;
                }

                var normalizedCandidate = NormalizeFieldName(column);
                foreach (DataColumn dataColumn in row.Table.Columns)
                {
                    if (!NormalizeFieldName(dataColumn.ColumnName).Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = row[dataColumn];
                    if (value != null && value != DBNull.Value)
                        return value;
                }
            }

            return null;
        }

        private static decimal ReadPreviewDecimal(DataRow row, params string[] columns)
        {
            foreach (var column in columns)
            {
                var value = ReadPreviewObject(row, column);
                if (value == null)
                    continue;
                if (value is decimal decimalValue)
                    return decimalValue;
                if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                    decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    return parsed;
            }

            return 0m;
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
            public Dictionary<string, object> ExtraFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed record OrganizationPrintInfo(
            Guid? Id,
            string DisplayName,
            string FullName,
            string ShortName,
            string Inn,
            string Okpo,
            string Address,
            string Phone,
            string Email,
            string Bank,
            string BankAccount,
            string Bic,
            string Director,
            string ChiefAccountant)
        {
            public static OrganizationPrintInfo Empty { get; } = new(
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

            public bool IsEmpty =>
                string.IsNullOrWhiteSpace(DisplayName) &&
                string.IsNullOrWhiteSpace(Inn) &&
                string.IsNullOrWhiteSpace(Okpo);

            public static OrganizationPrintInfo FromName(Guid? id, string name) => new(
                id,
                name ?? string.Empty,
                name ?? string.Empty,
                name ?? string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private readonly record struct CashDeskPrintInfo(string DisplayName, string Account);
    }
}

