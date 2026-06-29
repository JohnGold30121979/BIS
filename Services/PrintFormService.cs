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
                return new CashDeskPrintInfo(string.Empty, "3010");

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
            var layoutTemplate = CompactImportedTemplateVerticalGaps(template);
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

            return ReplacePlaceholders(UnquoteFoxText(element.Text), data, string.Empty);
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
            object value = normalized switch
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
                "debit_account" or "debit" or "deb" => GetAccountCode(data.DebitAccount),
                "credit_account" or "credit" or "cred" => GetAccountCode(data.CreditAccount),
                "debit_code" or "deb1" => GetAccountCode(data.DebitAccount),
                "credit_code" or "cred1" => GetAccountCode(data.CreditAccount),
                "amount" or "sum" or "sum1" => data.Amount,
                "amount_currency" or "amount_in_currency" or "sum_v" => data.AmountInCurrency,
                "amount_in_words" or "msum1" => data.AmountInWords,
                "basis" or "tex1" => data.Basis,
                "note" or "description" => data.Note,
                "currency" or "nval1" => "KGS",
                "rate" or "kurs_v" => "1,00",
                _ => string.Empty
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

        private static string NormalizeFieldName(string fieldName)
        {
            var value = (fieldName ?? string.Empty).Trim().Trim('{', '}').ToLowerInvariant();
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
                _ => value
            };
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
                ["deb"] = data.DebitAccount, ["deb1"] = GetAccountCode(data.DebitAccount),
                ["cred"] = data.CreditAccount, ["cred1"] = GetAccountCode(data.CreditAccount),
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
                foreach (var element in template.Elements.OrderBy(e => e.Order))
                {
                    // Пропускаем линии и рамки — они не являются полями данных
                    if (element.Type is "Line" or "Box" or "Picture")
                        continue;

                    var fieldName = string.IsNullOrWhiteSpace(element.Expression)
                        ? element.Text
                        : element.Expression;

                    // Очищаем от кавычек и лишнего
                    if (!string.IsNullOrWhiteSpace(fieldName))
                    {
                        fieldName = fieldName.Trim('"', '\'', ' ', '=');
                        // Пропускаем пустые или состоящие только из пробелов/знаков
                        if (string.IsNullOrWhiteSpace(fieldName) || fieldName == "+" || fieldName == "-")
                            continue;
                    }
                    else
                    {
                        continue;
                    }

                    var displayName = element.Type == "Text"
                        ? element.Text?.Trim('"', '\'', ' ')
                        : fieldName;

                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = displayName.Trim('"', '\'', ' ');
                        if (string.IsNullOrWhiteSpace(displayName))
                            displayName = fieldName;
                    }

                    fields.Add(new ReportField
                    {
                        Id = Guid.NewGuid(),
                        FieldName = fieldName,
                        DisplayName = displayName ?? fieldName,
                        Width = Math.Max(1, (int)Math.Round(element.Width)),
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

        public static PrintFormTemplate DeserializePrintTemplate(string templateJson)
        {
            if (string.IsNullOrWhiteSpace(templateJson))
                return new PrintFormTemplate();
            return new FrxParser().GetPrintTemplate(new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = "Template",
                FrxXml = templateJson
            });
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
        }

        private readonly record struct CashDeskPrintInfo(string DisplayName, string Account);
    }
}
