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
                CREATE INDEX IF NOT EXISTS ""IX_Reports_PrintForms""
                    ON ""Reports"" (""DataSourceId"", ""IsPrintForm"", ""IsActive"");
                CREATE INDEX IF NOT EXISTS ""IX_ReportElementMappings_ReportId_ElementOrder""
                    ON ""ReportElementMappings"" (""ReportId"", ""ElementOrder"");";
            await _context.Database.ExecuteSqlRawAsync(sql);
            lock (SchemaSyncLock)
            {
                EnsuredSchemaKeys.Add(schemaKey);
            }
        }

        public async Task SeedCashOrderFormsAsync()
        {
            await EnsureSchemaAsync();
            var documents = await _context.MetadataObjects.AsNoTracking()
                .Include(m => m.Fields)
                .Where(item => item.ObjectType == "Document" &&
                    (item.Name == "Приходный кассовый ордер" || item.Name == "Расходный кассовый ордер"))
                .ToListAsync();
            var receipt = documents.FirstOrDefault(item => item.Name == "Приходный кассовый ордер");
            var payment = documents.FirstOrDefault(item => item.Name == "Расходный кассовый ордер");

            if (receipt != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.receipt.foxpro"))
            {
                var frxTemplate = GenerateCashReceiptFrxTemplate(receipt);
                await _context.Reports.AddAsync(new Report
                {
                    Code = "cash.receipt.foxpro",
                    Name = "Приходный кассовый ордер с квитанцией",
                    TitleText = "Приходный кассовый ордер",
                    Description = "ПКО и отрывная квитанция на одном листе",
                    DataSourceType = "Document",
                    DataSourceId = receipt.Id,
                    ReportType = "FoxProLayout",
                    IsPrintForm = true,
                    IsActive = true,
                    IsDefault = true,
                    SourceFormat = "FoxProFRX",
                    TemplateVersion = 1,
                    Icon = "🖨",
                    Order = 10,
                    PageOrientation = "Landscape",
                    ShowGridLines = false,
                    ShowHeader = false,
                    ShowFooter = false,
                    ShowPageNumbers = false,
                    Template = JsonSerializer.Serialize(frxTemplate, new JsonSerializerOptions { WriteIndented = true }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (payment != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.payment.foxpro"))
            {
                var frxTemplate = GenerateCashPaymentFrxTemplate(payment);
                await _context.Reports.AddAsync(new Report
                {
                    Code = "cash.payment.foxpro",
                    Name = "Расходный кассовый ордер",
                    TitleText = "Расходный кассовый ордер",
                    Description = "Унифицированная печатная форма РКО",
                    DataSourceType = "Document",
                    DataSourceId = payment.Id,
                    ReportType = "FoxProLayout",
                    IsPrintForm = true,
                    IsActive = true,
                    IsDefault = true,
                    SourceFormat = "FoxProFRX",
                    TemplateVersion = 1,
                    Icon = "🖨",
                    Order = 10,
                    PageOrientation = "Portrait",
                    ShowGridLines = false,
                    ShowHeader = false,
                    ShowFooter = false,
                    ShowPageNumbers = false,
                    Template = JsonSerializer.Serialize(frxTemplate, new JsonSerializerOptions { WriteIndented = true }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (receipt != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.receipt.native"))
            {
                var nativeTemplate = CreateDefaultNativeTemplate("CashReceiptOrder");
                await _context.Reports.AddAsync(new Report
                {
                    Code = "cash.receipt.native",
                    Name = "Приходный кассовый ордер (нативный)",
                    TitleText = "Приходный кассовый ордер",
                    Description = "Настраиваемая нативная форма ПКО с квитанцией",
                    DataSourceType = "Document",
                    DataSourceId = receipt.Id,
                    ReportType = "CashReceiptOrder",
                    IsPrintForm = true,
                    IsActive = true,
                    IsDefault = false,
                    SourceFormat = "Native",
                    TemplateVersion = 1,
                    Icon = "🖨",
                    Order = 20,
                    PageOrientation = "Landscape",
                    ShowGridLines = false,
                    ShowHeader = false,
                    ShowFooter = false,
                    ShowPageNumbers = false,
                    Template = JsonSerializer.Serialize(nativeTemplate, new JsonSerializerOptions { WriteIndented = true }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            if (payment != null && !await _context.Reports.AnyAsync(item => item.Code == "cash.payment.native"))
            {
                var nativeTemplate = CreateDefaultNativeTemplate("CashPaymentOrder");
                await _context.Reports.AddAsync(new Report
                {
                    Code = "cash.payment.native",
                    Name = "Расходный кассовый ордер (нативный)",
                    TitleText = "Расходный кассовый ордер",
                    Description = "Настраиваемая нативная форма РКО",
                    DataSourceType = "Document",
                    DataSourceId = payment.Id,
                    ReportType = "CashPaymentOrder",
                    IsPrintForm = true,
                    IsActive = true,
                    IsDefault = false,
                    SourceFormat = "Native",
                    TemplateVersion = 1,
                    Icon = "🖨",
                    Order = 20,
                    PageOrientation = "Portrait",
                    ShowGridLines = false,
                    ShowHeader = false,
                    ShowFooter = false,
                    ShowPageNumbers = false,
                    Template = JsonSerializer.Serialize(nativeTemplate, new JsonSerializerOptions { WriteIndented = true }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

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
            var headers = new[] { "N", "Наименование", "Счет", "Без налогов", "НДС", "Налог", "Итого" };
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
            var isPayment = documentName.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase);
            var data = new CashOrderPrintData
            {
                DocumentName = documentName,
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

            AddExtra("esf_number", invoice.EsfNumber);
            AddExtra("Номер ЭСФ", invoice.EsfNumber);
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
            AddExtra("fact_SER_BL", invoice.EsfNumber);
            AddExtra("fact.SER_BL", invoice.EsfNumber);
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
            AddExtra("curFACTSW.ED_IZ", string.Empty);
            AddExtra("curFACTSW.KOL", firstLine == null ? 0 : 1);
            AddExtra("curFACTSW.CENA", firstLine?.AmountWithoutTax ?? 0);
            AddExtra("curFACTSW.PR_NDC", firstLine?.VatRate ?? 0);
            AddExtra("curFACTSW.PR_OP", firstLine?.SalesTaxRate ?? 0);

            for (var index = 0; index < invoice.Lines.Count; index++)
            {
                var number = index + 1;
                var line = invoice.Lines[index];
                AddExtra($"line{number}_name", line.Name);
                AddExtra($"line{number}_account", line.AccountCode);
                AddExtra($"line{number}_account_name", string.IsNullOrWhiteSpace(line.AccountName) ? line.AccountCode : line.AccountName);
                AddExtra($"line{number}_amount_without_tax", line.AmountWithoutTax);
                AddExtra($"line{number}_vat", line.VatAmount);
                AddExtra($"line{number}_sales_tax", line.SalesTaxAmount);
                AddExtra($"line{number}_total", line.LineTotal);
                AddExtra($"curFACTSW{number}.name", line.Name);
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
            IReadOnlyCollection<ReportElementMapping>? mappings)
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
            var svg = BuildTemplateSvg(template, report, data, mappings ?? Array.Empty<ReportElementMapping>());
            return QuestPDF.Fluent.Document.Create(document => document.Page(page =>
            {
                page.Size(report.PageOrientation == "Landscape" ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(8, Unit.Millimetre);
                page.Content().Svg(svg).FitArea();
            })).GeneratePdf();
        }

        private static string BuildTemplateSvg(
            PrintFormTemplate template,
            Report report,
            CashOrderPrintData data,
            IReadOnlyCollection<ReportElementMapping> mappings)
        {
            var layoutTemplate = FrxRecognitionProfileService.PrepareForRendering(template);
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
            svg.Append("<defs>");
            foreach (var element in layoutTemplate.Elements.Where(item => item.Type is "Text" or "Expression"))
            {
                var clipHeight = Math.Max(element.Height, Math.Max(minFontSize, element.FontSize * fontScale) * 1.4);
                svg.Append($"<clipPath id='clip{element.Order}'><rect x='{element.Left.ToString(CultureInfo.InvariantCulture)}' y='{element.Top.ToString(CultureInfo.InvariantCulture)}' width='{element.Width.ToString(CultureInfo.InvariantCulture)}' height='{clipHeight.ToString(CultureInfo.InvariantCulture)}'/></clipPath>");
            }
            svg.Append("</defs>");
            foreach (var element in layoutTemplate.Elements.OrderBy(item => item.Order))
            {
                if (mappingByOrder.TryGetValue(element.Order, out var hiddenMapping) && !hiddenMapping.IsVisible)
                    continue;

                var x = element.Left.ToString(CultureInfo.InvariantCulture);
                var y = element.Top.ToString(CultureInfo.InvariantCulture);
                var w = element.Width.ToString(CultureInfo.InvariantCulture);
                var h = element.Height.ToString(CultureInfo.InvariantCulture);
                if (element.Type == "Line")
                {
                    svg.Append($"<line x1='{x}' y1='{y}' x2='{(element.Left + element.Width).ToString(CultureInfo.InvariantCulture)}' y2='{(element.Top + element.Height).ToString(CultureInfo.InvariantCulture)}' stroke='black' stroke-width='{strokeWidth.ToString(CultureInfo.InvariantCulture)}'/>");
                    continue;
                }
                if (element.Type == "Box")
                {
                    svg.Append($"<rect x='{x}' y='{y}' width='{w}' height='{h}' fill='none' stroke='black' stroke-width='{strokeWidth.ToString(CultureInfo.InvariantCulture)}'/>");
                    continue;
                }
                if (element.Type == "Picture")
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
                    svg.Append($"<text x='{textX.ToString(CultureInfo.InvariantCulture)}' y='{textY.ToString(CultureInfo.InvariantCulture)}' clip-path='url(#clip{element.Order})' text-anchor='{anchor}' font-family='{EscapeXml(element.FontName)}' font-size='{fontSize.ToString(CultureInfo.InvariantCulture)}' font-weight='{fontWeight}' font-style='{fontStyle}'>{EscapeXml(lines[lineIndex])}</text>");
                }
            }
            svg.Append("</svg>");
            return svg.ToString();
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

                if (functionName.Equals("dtoc", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 1)
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
                    return data.Date.Day.ToString(CultureInfo.InvariantCulture);
                if (functionName.Equals("month", StringComparison.OrdinalIgnoreCase))
                    return data.Date.Month.ToString(CultureInfo.InvariantCulture);
                if (functionName.Equals("year", StringComparison.OrdinalIgnoreCase))
                    return data.Date.Year.ToString(CultureInfo.InvariantCulture);
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
                ["dovf1"] = string.Empty
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
            if (Regex.IsMatch(text, @"(?i)\b(?:substr|subs|alltrim|alltr|trim|iif|str|dtoc|day|month|year)\s*\("))
                return true;
            if (Regex.IsMatch(text, @"(?i)\b(?:fact|curFACTSW|irfactsw)[._][A-Za-z0-9_]+\b"))
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

            var match = Regex.Match(direct, @"(?i)\b(?:fact|curFACTSW|irfactsw)[._][A-Za-z0-9_]+\b");
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
