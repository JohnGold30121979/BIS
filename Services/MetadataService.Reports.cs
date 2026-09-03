using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BIS.ERP.Services;

public partial class MetadataService
{
    private const string PaymentOrderPrPl23ReportCode = "standard.frx.finance.payment-order.pr-pl23";
    private const string CashBookFrxReportCode = "standard.frx.finance.cash.cash-book";
    private const string ReceiptExpenseRegisterFrxReportCode = "standard.frx.finance.cash.receipts-expenses-register";
    public const string CashOrderTurnoverReportSourceName = "РКО/ПКО обороты по кассе";
    public const string CashOrderTurnoverReportSourceTableName = "report_cash_order_turnover_source";

    public async Task EnsureStandardReportsAsync()
    {
        await new PrintFormService(_context).EnsureSchemaAsync();
        await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
        await new ReportDataSetService(_context).EnsureStandardDataSetsAsync();
        _context.ChangeTracker.Clear();
        await EnsureCashOrderTurnoverReportSourceAsync();
        await DeleteDeprecatedObjectTreeReportsAsync();
        var deletedReportCodes = await new StandardReportDeletionService(_context).GetDeletedCodesAsync();

        foreach (var definition in BuildStandardReportDefinitions()
                     .Where(definition => !IsDeprecatedObjectTreeReportCode(definition.Code))
                     .Where(definition => !deletedReportCodes.Contains(definition.Code)))
            await EnsureStandardReportAsync(definition);
        foreach (var definition in StandardFrxReportTemplates.GetDefinitions()
                     .Where(definition => !IsDeprecatedObjectTreeReportCode(definition.Code))
                     .Where(definition => !deletedReportCodes.Contains(definition.Code)))
            await EnsureStandardFrxReportAsync(definition);

        await _context.SaveChangesAsync();
        await MarkReconciliationFrxReportsAsTemplateVariantsAsync();
        await MarkTrialBalanceFrxReportsAsTemplateVariantsAsync();
    }
    public async Task EnsurePaymentOrderPrintFormAsync()
    {
        await new PrintFormService(_context).EnsureSchemaAsync();
        await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
        _context.ChangeTracker.Clear();

        var deletedReportCodes = await new StandardReportDeletionService(_context).GetDeletedCodesAsync();
        if (deletedReportCodes.Contains(PaymentOrderPrPl23ReportCode))
            return;

        var definition = StandardFrxReportTemplates.GetDefinitions()
            .FirstOrDefault(item => item.Code == PaymentOrderPrPl23ReportCode);
        if (definition == null)
            return;

        await EnsureStandardFrxReportAsync(definition);
        await _context.SaveChangesAsync();
    }

    private async Task EnsureCashOrderTurnoverReportSourceAsync()
    {
        var source = await _context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "ReportSource" &&
                item.Name == CashOrderTurnoverReportSourceName);

        if (source == null)
        {
            var configId = await _context.MetadataConfigurations
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync();

            source = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = CashOrderTurnoverReportSourceName,
                TableName = CashOrderTurnoverReportSourceTableName,
                ObjectType = "ReportSource",
                Description = "Виртуальный источник кассовых отчетов: РКО/ПКО сгруппированы по корреспондентскому счету с суммами прихода, расхода и остатками.",
                Icon = "📒",
                Order = 95,
                IsSystem = true,
                MetadataConfigId = configId,
                Fields = new List<MetadataField>()
            };
            await _context.MetadataObjects.AddAsync(source);
        }

        foreach (var desired in BuildCashOrderTurnoverReportSourceFields(source.Id))
        {
            var existing = source.Fields.FirstOrDefault(field =>
                field.DbColumnName.Equals(desired.DbColumnName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                source.Fields.Add(desired);
                await _context.MetadataFields.AddAsync(desired);
                continue;
            }

            ApplyCashOrderTurnoverFieldTemplate(existing, desired);
        }

        await _context.SaveChangesAsync();
    }

    private static List<MetadataField> BuildCashOrderTurnoverReportSourceFields(Guid metadataObjectId) => new()
    {
        CashOrderTurnoverField(metadataObjectId, "Дата", "report_date", "DateTime", 1),
        CashOrderTurnoverField(metadataObjectId, "Дата FRX", "d_xls", "DateTime", 2),
        CashOrderTurnoverField(metadataObjectId, "Документ", "doc_number", "String", 3),
        CashOrderTurnoverField(metadataObjectId, "Документ FRX", "dok", "String", 4),
        CashOrderTurnoverField(metadataObjectId, "Номер учета", "nuch", "String", 5),
        CashOrderTurnoverField(metadataObjectId, "Номер учета FRX", "d_nuch", "String", 6),
        CashOrderTurnoverField(metadataObjectId, "Тип КО", "order_kind", "String", 7),
        CashOrderTurnoverField(metadataObjectId, "Касса", "cash_desk", "String", 8),
        CashOrderTurnoverField(metadataObjectId, "Счет кассы", "cash_account", "String", 9),
        CashOrderTurnoverField(metadataObjectId, "Корр. счет", "correspondent_account", "String", 10),
        CashOrderTurnoverField(metadataObjectId, "Остаток на начало", "opening_balance", "Decimal", 11),
        CashOrderTurnoverField(metadataObjectId, "Сумма Приход", "sum_debet", "Decimal", 12),
        CashOrderTurnoverField(metadataObjectId, "Сумма Расход", "sum_credit", "Decimal", 13),
        CashOrderTurnoverField(metadataObjectId, "Остаток на конец", "closing_balance", "Decimal", 14),
        CashOrderTurnoverField(metadataObjectId, "Приход FRX", "deb", "Decimal", 15),
        CashOrderTurnoverField(metadataObjectId, "Расход FRX", "cred", "Decimal", 16),
        CashOrderTurnoverField(metadataObjectId, "Сумма", "sum", "Decimal", 17),
        CashOrderTurnoverField(metadataObjectId, "Содержание", "tex", "String", 18),
        CashOrderTurnoverField(metadataObjectId, "Наименование", "name_kod", "String", 19),
        CashOrderTurnoverField(metadataObjectId, "Основание", "basis", "String", 20),
        CashOrderTurnoverField(metadataObjectId, "Примечание", "description", "String", 21),
        CashOrderTurnoverField(metadataObjectId, "Количество документов", "document_count", "Int", 22),
        CashOrderTurnoverField(metadataObjectId, "Модуль", "module", "String", 23)
    };

    private static MetadataField CashOrderTurnoverField(
        Guid metadataObjectId,
        string name,
        string dbColumnName,
        string fieldType,
        int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DbColumnName = dbColumnName,
        FieldType = fieldType,
        Length = fieldType == "String" ? 500 : 0,
        Precision = 18,
        Scale = 2,
        IsRequired = false,
        Order = order,
        MetadataObjectId = metadataObjectId
    };

    private static void ApplyCashOrderTurnoverFieldTemplate(MetadataField existing, MetadataField desired)
    {
        existing.Name = desired.Name;
        existing.FieldType = desired.FieldType;
        existing.Length = desired.Length;
        existing.Precision = desired.Precision;
        existing.Scale = desired.Scale;
        existing.Order = desired.Order;
        existing.IsRequired = false;
        existing.IsUnique = false;
        existing.ReferenceCatalog = desired.ReferenceCatalog;
        existing.DisplayPattern = desired.DisplayPattern;
        existing.DisplayFields = desired.DisplayFields;
    }

    private static bool IsCashOrderTurnoverFrxReportCode(string? code) =>
        string.Equals(code, CashBookFrxReportCode, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, ReceiptExpenseRegisterFrxReportCode, StringComparison.OrdinalIgnoreCase);
    private static readonly string[] DeprecatedObjectTreeReportCodes =
    {
        "standard.finance.trial-balance",
        "standard.finance.general-ledger",
        "standard.frx.finance.trial-balance",
        "standard.frx.finance.general-ledger"
    };

    private static readonly string[] DeprecatedObjectTreeReportNames =
    {
        "Оборотно-сальдовая ведомость",
        "Главная книга",
        "Оборотно-сальдовая ведомость (FRX FoxPro)",
        "Главная книга (FRX FoxPro)"
    };

    private static bool IsDeprecatedObjectTreeReportCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        DeprecatedObjectTreeReportCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    private async Task DeleteDeprecatedObjectTreeReportsAsync()
    {
        var reportIds = await _context.Reports
            .Where(report =>
                DeprecatedObjectTreeReportCodes.Contains(report.Code) ||
                (DeprecatedObjectTreeReportNames.Contains(report.Name) &&
                 ((report.Code == null || report.Code == string.Empty) ||
                  report.Code.StartsWith("standard.finance.") ||
                  report.Code.StartsWith("standard.frx.finance."))))
            .Select(report => report.Id)
            .ToListAsync();

        if (reportIds.Count == 0)
            return;

        await _context.MetadataModuleItems
            .Where(item => item.ObjectType == "Report" && reportIds.Contains(item.ObjectId))
            .ExecuteDeleteAsync();

        foreach (var reportId in reportIds)
            await DeleteStandardReportDetailsAsync(reportId);

        await _context.Reports
            .Where(report => reportIds.Contains(report.Id))
            .ExecuteDeleteAsync();

        await _context.SaveChangesAsync();
    }

    private async Task MarkReconciliationFrxReportsAsTemplateVariantsAsync()
    {
        var candidateReports = await _context.Reports
            .Where(report =>
                EF.Functions.Like(report.Code, "standard.frx.finance.reconciliation.%") ||
                report.SourceFormat == "FoxProFRX" ||
                report.ReportType == "FoxProLayout")
            .Select(report => new Report
            {
                Id = report.Id,
                Name = report.Name,
                Description = report.Description,
                Code = report.Code,
                SourceFormat = report.SourceFormat,
                ReportType = report.ReportType,
                Template = report.Template
            })
            .ToListAsync();
        var reportIds = candidateReports
            .Where(ReportClassificationService.IsReconciliationReport)
            .Select(report => report.Id)
            .ToList();

        if (reportIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        await _context.Reports
            .Where(report => reportIds.Contains(report.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(report => report.IsActive, true)
                .SetProperty(report => report.IsPrintForm, true)
                .SetProperty(report => report.IsDefault, false)
                .SetProperty(report => report.UpdatedAt, now));

        var moduleItems = await _context.MetadataModuleItems
            .Where(item => item.ObjectType == "Report" && reportIds.Contains(item.ObjectId))
            .ToListAsync();
        if (moduleItems.Count > 0)
        {
            _context.MetadataModuleItems.RemoveRange(moduleItems);
            await _context.SaveChangesAsync();
        }
    }


    private async Task MarkTrialBalanceFrxReportsAsTemplateVariantsAsync()
    {
        var reportIds = await _context.Reports
            .Where(report => EF.Functions.Like(report.Code, "standard.frx.finance.trial-balance.%"))
            .Select(report => report.Id)
            .ToListAsync();

        if (reportIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        await _context.Reports
            .Where(report => reportIds.Contains(report.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(report => report.IsActive, true)
                .SetProperty(report => report.IsPrintForm, true)
                .SetProperty(report => report.IsDefault, false)
                .SetProperty(report => report.UpdatedAt, now));

        var moduleItems = await _context.MetadataModuleItems
            .Where(item => item.ObjectType == "Report" && reportIds.Contains(item.ObjectId))
            .ToListAsync();
        if (moduleItems.Count > 0)
        {
            _context.MetadataModuleItems.RemoveRange(moduleItems);
            await _context.SaveChangesAsync();
        }
    }
    private async Task EnsureStandardFrxReportAsync(StandardFrxReportTemplateDefinition definition)
    {
        var sourceName = IsCashOrderTurnoverFrxReportCode(definition.Code)
            ? CashOrderTurnoverReportSourceName
            : definition.SourceName;
        var sourceObjectType = IsCashOrderTurnoverFrxReportCode(definition.Code)
            ? "ReportSource"
            : definition.SourceObjectType;

        var source = await _context.MetadataObjects
            .AsNoTracking()
            .Include(metadata => metadata.Fields)
            .FirstOrDefaultAsync(metadata =>
                metadata.Name == sourceName &&
                metadata.ObjectType == sourceObjectType);

        var report = await _context.Reports
            .FirstOrDefaultAsync(item => item.Code == definition.Code);
        var isNewReport = report == null;
        if (report == null)
        {
            report = new Report
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
            await _context.Reports.AddAsync(report);
        }

        var templateJson = DecodeStandardFrxTemplate(definition.TemplateCompressedBase64);
        var shouldSeedTemplate = isNewReport || string.IsNullOrWhiteSpace(report.Template);
        var isPaymentOrderPrintForm = string.Equals(
            definition.Code,
            PaymentOrderPrPl23ReportCode,
            StringComparison.OrdinalIgnoreCase);

        report.Code = definition.Code;
        report.IsSystem = true;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Name))
            report.Name = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Description))
            report.Description = definition.Description;
        report.DataSourceType = sourceObjectType;
        report.DataSourceId = source?.Id;
        report.ReportType = definition.ReportType;
        if (shouldSeedTemplate)
            report.Template = templateJson;
        report.Settings = string.IsNullOrWhiteSpace(report.Settings) ? "{}" : report.Settings;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Icon))
            report.Icon = definition.Icon;
        if (isNewReport || isPaymentOrderPrintForm)
            report.IsActive = source != null || IsStandaloneStandardFrxVariant(definition.Code);
        report.IsPrintForm = definition.IsPrintForm;
        if (isNewReport)
            report.IsDefault = definition.IsDefault && source != null;
        report.SourceFormat = "FoxProFRX";
        report.TemplateVersion = isNewReport ? 1 : Math.Max(report.TemplateVersion, 1);
        if (isNewReport)
            report.Order = definition.Order;
        report.UpdatedAt = DateTime.UtcNow;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageTitle))
            report.PageTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageOrientation))
            report.PageOrientation = definition.PageOrientation;
        if (isNewReport || report.PageWidth <= 0)
            report.PageWidth = report.PageOrientation == "Landscape" ? 297 : 210;
        if (isNewReport || report.PageHeight <= 0)
            report.PageHeight = report.PageOrientation == "Landscape" ? 210 : 297;
        if (isNewReport || report.LeftMargin <= 0)
            report.LeftMargin = 10;
        if (isNewReport || report.RightMargin <= 0)
            report.RightMargin = 10;
        if (isNewReport || report.TopMargin <= 0)
            report.TopMargin = 12;
        if (isNewReport || report.BottomMargin <= 0)
            report.BottomMargin = 12;
        if (isNewReport || string.IsNullOrWhiteSpace(report.FontName))
            report.FontName = "Segoe UI";
        if (isNewReport || report.FontSize <= 0)
            report.FontSize = 9;
        if (isNewReport)
        {
            report.ShowHeader = false;
            report.ShowFooter = false;
            report.ShowPageNumbers = false;
            report.ShowGridLines = false;
        }
        if (isNewReport || string.IsNullOrWhiteSpace(report.TitleText))
            report.TitleText = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.SubtitleText))
            report.SubtitleText = definition.Description;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderTitle))
            report.HeaderTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderSubtitle))
            report.HeaderSubtitle = definition.Description;
        var hasFields = await _context.ReportFields.AnyAsync(item => item.ReportId == report.Id);
        if (!hasFields)
            await AddStandardFrxReportFieldsAsync(report.Id, report.Template);

        await _context.SaveChangesAsync();

        if (report.IsPrintForm && report.IsDefault && report.DataSourceId.HasValue)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""Reports"" SET ""IsDefault"" = false
                WHERE ""DataSourceId"" = {report.DataSourceId.Value}
                  AND ""IsPrintForm"" = true AND ""Id"" <> {report.Id}");
        }

        await AssignStandardReportToModuleAsync(report.Id, definition.ModuleCode, definition.Order);
    }

    private static bool IsStandaloneStandardFrxVariant(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        (code.StartsWith("standard.frx.finance.reconciliation.", StringComparison.OrdinalIgnoreCase) ||
         code.StartsWith("standard.frx.finance.trial-balance.", StringComparison.OrdinalIgnoreCase));

    private async Task EnsureStandardReportAsync(StandardReportDefinition definition)
    {
        var source = await _context.MetadataObjects
            .AsNoTracking()
            .Include(metadata => metadata.Fields)
            .FirstOrDefaultAsync(metadata =>
                metadata.Name == definition.SourceName &&
                metadata.ObjectType == definition.SourceObjectType);

        var report = await FindExistingStandardReportAsync(definition);
        var isNewReport = report == null;

        if (report == null)
        {
            report = new Report
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
            await _context.Reports.AddAsync(report);
        }

        report.Code = definition.Code;
        report.IsSystem = true;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Name))
            report.Name = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Description))
            report.Description = definition.Description;
        report.DataSourceType = definition.SourceObjectType;
        report.DataSourceId = source?.Id;
        report.ReportType = definition.ReportType;
        if (isNewReport)
        {
            report.Template = string.Empty;
            report.Settings = "{}";
        }
        else if (string.IsNullOrWhiteSpace(report.Settings))
        {
            report.Settings = "{}";
        }
        if (isNewReport || string.IsNullOrWhiteSpace(report.Icon))
            report.Icon = definition.Icon;
        if (isNewReport)
        {
            // Native standard reports stay in metadata, but are hidden until page-perfect output is implemented.
            report.IsActive = false;
            report.IsPrintForm = false;
            report.IsDefault = false;
        }
        report.SourceFormat = "Native";
        report.TemplateVersion = isNewReport ? 1 : Math.Max(report.TemplateVersion, 1);
        if (isNewReport)
            report.Order = definition.Order;
        report.UpdatedAt = DateTime.UtcNow;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageTitle))
            report.PageTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageOrientation))
            report.PageOrientation = definition.PageOrientation;
        if (isNewReport || report.PageWidth <= 0)
            report.PageWidth = report.PageOrientation == "Landscape" ? 297 : 210;
        if (isNewReport || report.PageHeight <= 0)
            report.PageHeight = report.PageOrientation == "Landscape" ? 210 : 297;
        if (isNewReport || report.LeftMargin <= 0)
            report.LeftMargin = 10;
        if (isNewReport || report.RightMargin <= 0)
            report.RightMargin = 10;
        if (isNewReport || report.TopMargin <= 0)
            report.TopMargin = 12;
        if (isNewReport || report.BottomMargin <= 0)
            report.BottomMargin = 12;
        if (isNewReport || string.IsNullOrWhiteSpace(report.FontName))
            report.FontName = "Segoe UI";
        if (isNewReport || report.FontSize <= 0)
            report.FontSize = 9;
        if (isNewReport)
        {
            report.ShowHeader = true;
            report.ShowFooter = true;
            report.ShowPageNumbers = true;
            report.ShowGridLines = true;
        }
        if (isNewReport || string.IsNullOrWhiteSpace(report.TitleText))
            report.TitleText = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.SubtitleText))
            report.SubtitleText = definition.Description;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderTitle))
            report.HeaderTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderSubtitle))
            report.HeaderSubtitle = definition.Description;

        var hasFields = await _context.ReportFields.AnyAsync(item => item.ReportId == report.Id);
        if (source != null && !hasFields)
            await AddStandardReportFieldsAsync(report.Id, source, definition.Fields);
        await _context.SaveChangesAsync();
        await AssignStandardReportToModuleAsync(report.Id, definition.ModuleCode, definition.Order);
    }

    private async Task<Report?> FindExistingStandardReportAsync(StandardReportDefinition definition)
    {
        var report = await _context.Reports
            .FirstOrDefaultAsync(item => item.Code == definition.Code);
        if (report != null)
            return report;

        return await _context.Reports
            .FirstOrDefaultAsync(item =>
                item.Name == definition.Name &&
                (string.IsNullOrWhiteSpace(item.Code) ||
                 item.Code.StartsWith("assets.") ||
                 item.Code.StartsWith("finance.") ||
                 item.Code.StartsWith("inventory.")));
    }

    private async Task DeleteStandardReportDetailsAsync(Guid reportId)
    {
        await _context.ReportFields
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportFilters
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportGroups
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportElementMappings
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
    }

    private async Task AddStandardReportFieldsAsync(
        Guid reportId,
        MetadataObject source,
        IReadOnlyList<string> fieldNames)
    {
        var fields = new List<ReportField>();
        var order = 1;
        foreach (var fieldName in fieldNames)
        {
            var field = source.Fields.FirstOrDefault(metadataField =>
                metadataField.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                metadataField.DbColumnName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null)
                continue;

            fields.Add(new ReportField
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                FieldName = field.DbColumnName,
                DisplayName = field.Name,
                AggregateType = field.FieldType == "Decimal" ? "Sum" : string.Empty,
                Order = order++,
                Width = GetStandardReportFieldWidth(field),
                Alignment = field.FieldType == "Decimal" ? "Right" : "Left",
                Format = GetStandardReportFieldFormat(field),
                IsVisible = true
            });
        }

        if (fields.Count > 0)
            await _context.ReportFields.AddRangeAsync(fields);
    }

    private async Task AddStandardFrxReportFieldsAsync(Guid reportId, string templateJson)
    {
        var fields = PrintFormService.ExtractReportFieldsFromTemplate(templateJson)
            .GroupBy(field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(field => field.Order)
            .ToList();

        if (fields.Count == 0)
            return;

        var order = 1;
        foreach (var field in fields)
        {
            field.Id = Guid.NewGuid();
            field.ReportId = reportId;
            field.Order = order++;
        }

        await _context.ReportFields.AddRangeAsync(fields);
    }

    private static string DecodeStandardFrxTemplate(string compressedBase64)
    {
        var compressed = Convert.FromBase64String(compressedBase64);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task AssignStandardReportToModuleAsync(Guid reportId, string moduleCode, int order)
    {
        var module = await _context.MetadataModules
            .FirstOrDefaultAsync(item => item.Code == moduleCode);
        if (module == null)
            return;

        var existing = await _context.MetadataModuleItems
            .FirstOrDefaultAsync(item => item.ObjectType == "Report" && item.ObjectId == reportId);
        if (existing == null)
        {
            await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
            {
                Id = Guid.NewGuid(),
                ModuleId = module.Id,
                ObjectId = reportId,
                ObjectType = "Report",
                Order = order
            });
        }
        else
        {
            existing.ModuleId = module.Id;
            existing.Order = order;
        }

        await _context.SaveChangesAsync();
    }

    private static int GetStandardReportFieldWidth(MetadataField field) => field.FieldType switch
    {
        "Decimal" => 120,
        "DateTime" => 105,
        "Bool" => 80,
        "Reference" => 170,
        _ => field.Length > 250 ? 220 : 150
    };

    private static string GetStandardReportFieldFormat(MetadataField field) => field.FieldType switch
    {
        "Decimal" => "N2",
        "DateTime" => "dd.MM.yyyy",
        _ => string.Empty
    };

    private static IReadOnlyList<StandardReportDefinition> BuildStandardReportDefinitions() => new[]
    {
        new StandardReportDefinition(
            Code: "standard.finance.postings-journal",
            Name: "Журнал проводок",
            Description: "Единый журнал бухгалтерских проводок финансового модуля.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "Table",
            Icon: "📒",
            Order: 100,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "module_code", "debit_account",
                "credit_account", "amount_kgs", "amount_currency", "currency_id",
                "organization_id", "employee_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.trial-balance",
            Name: "Оборотно-сальдовая ведомость",
            Description: "Финансовая ведомость по оборотам и остаткам на основании общего журнала проводок.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "AccountingTrialBalance",
            Icon: "📊",
            Order: 110,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "debit_account", "credit_account", "amount_kgs",
                "amount_currency", "currency_id", "organization_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.general-ledger",
            Name: "Главная книга",
            Description: "Главная книга по счетам на основании проводок и закрытых остатков.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "GeneralLedger",
            Icon: "📘",
            Order: 120,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "debit_account",
                "credit_account", "amount_kgs", "organization_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.bank-statement",
            Name: "Выписка банка",
            Description: "Банковская выписка по платежным поручениям и связанным проводкам.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Платежное поручение",
            SourceObjectType: "Document",
            ReportType: "BankStatement",
            Icon: "🏦",
            Order: 130,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "order_type", "organization_id", "amount",
                "amount_currency", "currency_id", "our_account_id", "correspondent_account",
                "payment_classification_id", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.payment-order-registry",
            Name: "Реестр платежных поручений",
            Description: "Реестр платежных поручений за период.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Платежное поручение",
            SourceObjectType: "Document",
            ReportType: "PaymentOrderRegistry",
            Icon: "📋",
            Order: 140,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "order_type", "organization_id", "amount",
                "currency_id", "our_account_id", "correspondent_account", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.reconciliation-act",
            Name: "Акт сверки",
            Description: "Акт сверки расчетов по организации или подотчетному лицу.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "ReconciliationAct",
            Icon: "🤝",
            Order: 150,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "debit_account",
                "credit_account", "amount_kgs", "organization_id", "employee_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.sales-invoice-journal",
            Name: "Журнал продаж",
            Description: "Журнал выписанных счет-фактур и налоговых сумм.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: InvoiceDocumentTypes.SalesIssue,
            SourceObjectType: "Document",
            ReportType: "SalesInvoiceJournal",
            Icon: "🧾",
            Order: 160,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "esf_number", "organization_id",
                "payment_kind", "delivery_kind", "supply_kind", "amount_without_tax",
                "vat_total", "sales_tax_total", "amount", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.purchase-invoice-journal",
            Name: "Журнал закупок",
            Description: "Журнал зарегистрированных полученных счет-фактур и налоговых сумм.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: InvoiceDocumentTypes.PurchaseRegistration,
            SourceObjectType: "Document",
            ReportType: "PurchaseInvoiceJournal",
            Icon: "📥",
            Order: 170,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "esf_number", "organization_id",
                "payment_kind", "delivery_kind", "supply_kind", "amount_without_tax",
                "vat_total", "sales_tax_total", "amount", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.cash-receipts",
            Name: "Журнал приходных кассовых ордеров",
            Description: "Реестр приходных кассовых документов.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расходный/Приходный КО",
            SourceObjectType: "Document",
            ReportType: "CashReceiptRegistry",
            Icon: "💵",
            Order: 180,
            PageOrientation: "Landscape",
            Fields: new[] { "doc_date", "doc_number", "organization_id", "cash_desk_id", "amount", "basis", "is_posted" }),
        new StandardReportDefinition(
            Code: "standard.finance.cash-payments",
            Name: "Журнал расходных кассовых ордеров",
            Description: "Реестр расходных кассовых документов.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расходный/Приходный КО",
            SourceObjectType: "Document",
            ReportType: "CashPaymentRegistry",
            Icon: "💸",
            Order: 190,
            PageOrientation: "Landscape",
            Fields: new[] { "doc_date", "doc_number", "organization_id", "cash_desk_id", "amount", "basis", "is_posted" }),
        new StandardReportDefinition(
            Code: "standard.finance.advance-reports",
            Name: "Журнал авансовых отчетов",
            Description: "Реестр авансовых отчетов с суммами к учету, перерасходом и возвратом.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Авансовые платежи",
            SourceObjectType: "Document",
            ReportType: "AdvanceReportRegistry",
            Icon: "🧳",
            Order: 200,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "employee_id", "advance_payment_id", "amount",
                "accepted_amount", "overrun_amount", "return_amount", "currency_id", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.exchange-rate-differences",
            Name: "Журнал курсовой разницы",
            Description: "Журнал документов расчета курсовой разницы.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расчет курсовой разницы",
            SourceObjectType: "Document",
            ReportType: "ExchangeRateDifferenceRegistry",
            Icon: "💱",
            Order: 210,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "calculation_date", "period_start_date", "period_end_date", "currency_id",
                "exchange_rate", "processed_balances", "created_postings", "gain_amount", "loss_amount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.list",
            Name: "Ведомость основных средств",
            Description: "Ведомость основных средств по зафиксированным остаткам периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetStatement",
            Icon: "🏗",
            Order: 300,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "InventoryNumber", "AssetName", "OrganizationName", "ResponsiblePersonName",
                "SiteName", "InitialCost", "SalvageValue", "AccumulatedDepreciation",
                "CarryingAmount", "LifecycleStatus"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.balances",
            Name: "Оборотная ведомость по ОС",
            Description: "Оборотная ведомость основных средств по снимкам закрытого периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetTurnover",
            Icon: "📊",
            Order: 310,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName",
                "OpeningCost", "OpeningDepreciation", "OpeningCarryingAmount",
                "AcquisitionCost", "DisposalCost", "TransferInCost", "TransferOutCost",
                "RevaluationCost", "AutomaticDepreciation", "ManualDepreciation",
                "DepreciationWriteOff", "DisposalDepreciation",
                "ClosingCost", "ClosingDepreciation", "ClosingCarryingAmount",
                "ResponsiblePersonName", "SiteName"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.by-account",
            Name: "Ведомость ОС по счету",
            Description: "Ведомость основных средств в разрезе счетов учета.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetByAccount",
            Icon: "📒",
            Order: 320,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "AssetAccount", "InventoryNumber", "AssetName",
                "InitialCost", "AccumulatedDepreciation", "CarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.depreciation",
            Name: "Ведомость амортизации",
            Description: "Ведомость начисленной и накопленной амортизации основных средств.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetDepreciation",
            Icon: "📉",
            Order: 330,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "InventoryNumber", "AssetName", "DepreciationAccount",
                "ExpenseAccount", "OpeningDepreciation", "AutomaticDepreciation",
                "ManualDepreciation", "DepreciationAdjustment", "DepreciationWriteOff",
                "ClosingDepreciation", "ClosingCarryingAmount", "MonthlyDepreciation"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.card",
            Name: "Карточка основного средства",
            Description: "Печатная карточка основного средства по текущим реквизитам.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Основные средства",
            SourceObjectType: "Catalog",
            ReportType: "FixedAssetCard",
            Icon: "🪪",
            Order: 340,
            PageOrientation: "Portrait",
            Fields: new[]
            {
                "inventory_number", "name", "asset_group", "asset_account",
                "depreciation_account", "expense_account", "initial_cost",
                "accumulated_depreciation", "carrying_amount", "status", "is_active"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.journal",
            Name: "Журнал движения ОС",
            Description: "Журнал движения основных средств по документам и снимкам периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetMovementJournal",
            Icon: "📚",
            Order: 350,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName", "OrganizationName",
                "ResponsiblePersonName", "SiteName", "AssetAccount", "OpeningCost",
                "AcquisitionCost", "DisposalCost", "TransferInCost", "TransferOutCost",
                "ClosingCost", "ClosingCarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.responsible-person",
            Name: "Ведомость ОС по МОЛ",
            Description: "Ведомость основных средств по материально ответственным лицам.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetResponsiblePersonStatement",
            Icon: "👤",
            Order: 360,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "ResponsiblePersonName", "InventoryNumber", "AssetName",
                "SiteName", "InitialCost", "AccumulatedDepreciation", "CarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.directory",
            Name: "Справочник ОС",
            Description: "Контрольная печатная форма справочника основных средств.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Основные средства",
            SourceObjectType: "Catalog",
            ReportType: "FixedAssetDirectory",
            Icon: "📇",
            Order: 370,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "code", "inventory_number", "name", "asset_group", "asset_subgroup_id",
                "asset_type_id", "asset_account", "responsible_person_id", "site_id", "status", "is_active"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.inventory-transfer",
            Name: "Инвентарная передача ОС",
            Description: "Печатная ведомость документов передачи основных средств в подотчет.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Передача ОС в подотчет",
            SourceObjectType: "Document",
            ReportType: "FixedAssetInventoryTransfer",
            Icon: "🔁",
            Order: 380,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "asset_id", "organization_id",
                "responsible_person_id", "site_id", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.movement",
            Name: "Движение основных средств",
            Description: "Движение основных средств по периоду и местам эксплуатации.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetMovement",
            Icon: "🚚",
            Order: 390,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName",
                "OrganizationName", "ResponsiblePersonName", "SiteName",
                "AssetAccount", "OpeningCost", "AcquisitionCost", "DisposalCost",
                "TransferInCost", "TransferOutCost", "RevaluationCost",
                "ClosingCost", "ClosingDepreciation", "ClosingCarryingAmount"
            })
    };

    private sealed record StandardReportDefinition(
        string Code,
        string Name,
        string Description,
        string ModuleCode,
        string SourceName,
        string SourceObjectType,
        string ReportType,
        string Icon,
        int Order,
        string PageOrientation,
        IReadOnlyList<string> Fields);
}



