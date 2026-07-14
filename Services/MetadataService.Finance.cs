using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services;

public partial class MetadataService
{
    #region Finance catalog fields

    private List<MetadataField> GetAdvancePaymentFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
        {
            new MetadataField { Id = Guid.NewGuid(), Name = "Код", DbColumnName = "code", FieldType = "String", Length = 50, IsRequired = true, IsUnique = true, Order = 1, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Вид расчета", DbColumnName = "name", FieldType = "String", Length = 250, IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Организации", DbColumnName = "use_organizations", FieldType = "Bool", IsRequired = true, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Сотрудники", DbColumnName = "use_personnel", FieldType = "Bool", IsRequired = true, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Валютный учет", DbColumnName = "use_currency", FieldType = "Bool", IsRequired = true, Order = 5, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Остаток брать из модуля", DbColumnName = "module_code", FieldType = "String", Length = 50, IsRequired = false, Order = 6, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Дебет", DbColumnName = "debit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 7, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Кредит", DbColumnName = "credit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 8, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Участвует во взаиморасчетах", DbColumnName = "use_settlements", FieldType = "Bool", IsRequired = true, Order = 9, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Формировать проводки авансовых платежей", DbColumnName = "generate_postings", FieldType = "Bool", IsRequired = true, Order = 10, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Участвует во внутренних взаиморасчетах", DbColumnName = "use_internal_settlements", FieldType = "Bool", IsRequired = true, Order = 11, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", IsRequired = true, Order = 12, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Примечание", DbColumnName = "description", FieldType = "String", Length = 500, IsRequired = false, Order = 13, MetadataObjectId = metadataObjectId }
        };
    }

    private List<MetadataField> GetAccountAccessFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
        {
            new MetadataField { Id = Guid.NewGuid(), Name = "Код", DbColumnName = "code", FieldType = "String", Length = 50, IsRequired = true, IsUnique = true, Order = 1, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет", DbColumnName = "account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Оператор", DbColumnName = "operator_name", FieldType = "String", Length = 200, IsRequired = false, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Доступ", DbColumnName = "has_access", FieldType = "Bool", IsRequired = true, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", IsRequired = true, Order = 5, MetadataObjectId = metadataObjectId }
        };
    }

    private List<MetadataField> GetExchangeRateDiffFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
        {
            new MetadataField { Id = Guid.NewGuid(), Name = "Код", DbColumnName = "code", FieldType = "String", Length = 50, IsRequired = true, IsUnique = true, Order = 1, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Основной счет", DbColumnName = "account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Парный счет", DbColumnName = "paired_account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = false, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет дохода", DbColumnName = "gain_account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = false, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет расхода", DbColumnName = "loss_account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = false, Order = 5, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Разрез расчета", DbColumnName = "calculation_detail_mode", FieldType = "Int", IsRequired = false, Order = 6, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Валюта", DbColumnName = "currency_id", FieldType = "Reference", ReferenceCatalog = "Справочник валют", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = false, Order = 7, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Модуль", DbColumnName = "module_code", FieldType = "String", Length = 50, IsRequired = false, Order = 8, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Алгоритм расчета", DbColumnName = "calculation_algorithm", FieldType = "Int", IsRequired = false, Order = 9, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Строка отчета по дебету", DbColumnName = "debit_report_line", FieldType = "Int", IsRequired = false, Order = 10, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Строка отчета по кредиту", DbColumnName = "credit_report_line", FieldType = "Int", IsRequired = false, Order = 11, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Способ расчета", DbColumnName = "calculation_method", FieldType = "String", Length = 50, IsRequired = false, Order = 12, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", IsRequired = true, Order = 13, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Примечание", DbColumnName = "description", FieldType = "String", Length = 500, IsRequired = false, Order = 14, MetadataObjectId = metadataObjectId }
        };
    }

    #endregion

    #region Finance catalogs creation

    public async Task EnsureFinanceCatalogStructuresAsync()
    {
        await EnsureAdvancePaymentsCatalogStructureAsync();
        await EnsureExchangeRateDiffCatalogStructureAsync();
    }

    private async Task CreateAdvancePaymentsCatalog(MetadataConfiguration config)
    {
        try
        {
            var catalogId = Guid.NewGuid();
            var catalog = new MetadataObject
            {
                Id = catalogId, Name = "Авансовые платежи", TableName = "catalog_advance_payments",
                ObjectType = "Catalog", Description = "Справочник пар счетов для учета авансовых платежей",
                Icon = "💳", Order = 20, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetAdvancePaymentFields(catalogId)
            };
            await _context.MetadataObjects.AddAsync(catalog);
            await _context.SaveChangesAsync();
            await CreateTableForCatalogAsync(catalog);
            await AddAdvancePaymentDataToTable(catalog);
            System.Diagnostics.Debug.WriteLine("Справочник 'Авансовые платежи' создан");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Авансовые платежи': {ex.Message}");
        }
    }

    private async Task EnsureAdvancePaymentsCatalogStructureAsync()
    {
        var catalog = await _context.MetadataObjects
            .Include(metadata => metadata.Fields)
            .FirstOrDefaultAsync(metadata => metadata.ObjectType == "Catalog" && metadata.Name == "Авансовые платежи");

        if (catalog == null)
            return;

        await MigrateAdvancePaymentsModuleFieldAsync(catalog);

        foreach (var field in catalog.Fields)
            NormalizeAdvancePaymentField(field);

        var existingColumns = catalog.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
            .Select(field => field.DbColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in GetAdvancePaymentFields(catalog.Id))
        {
            if (existingColumns.Contains(field.DbColumnName))
                continue;

            field.Id = Guid.NewGuid();
            field.MetadataObjectId = catalog.Id;
            await _context.MetadataFields.AddAsync(field);
            await AddColumnToTableAsync(catalog.TableName, field);
            catalog.Fields.Add(field);
            existingColumns.Add(field.DbColumnName);
        }

        await _context.SaveChangesAsync();
        await NormalizeAdvancePaymentRowsAsync(catalog);
        await AddAdvancePaymentDataToTable(catalog);
    }

    private static void NormalizeAdvancePaymentField(MetadataField field)
    {
        if (field.DbColumnName?.Equals("name", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Вид расчета";
            field.FieldType = "String";
            field.Length = Math.Max(field.Length, 250);
            field.IsRequired = true;
            field.Order = 2;
        }
        else if (field.DbColumnName?.Equals("use_organizations", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Организации";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 3;
        }
        else if (field.DbColumnName?.Equals("use_personnel", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Сотрудники";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 4;
        }
        else if (field.DbColumnName?.Equals("use_currency", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Валютный учет";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 5;
        }
        else if (field.DbColumnName?.Equals("module_code", StringComparison.OrdinalIgnoreCase) == true ||
                 field.DbColumnName?.Equals("arm_code", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Остаток брать из модуля";
            field.DbColumnName = "module_code";
            field.FieldType = "String";
            field.Length = Math.Max(field.Length, 50);
            field.Order = 6;
        }
        else if (field.DbColumnName?.Equals("debit_account", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Дебет";
            field.FieldType = "Reference";
            field.ReferenceCatalog = "План счетов";
            field.DisplayPattern = "{Код} - {Наименование}";
            field.DisplayFields = "Код,Наименование";
            field.IsRequired = true;
            field.Order = 7;
        }
        else if (field.DbColumnName?.Equals("credit_account", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Кредит";
            field.FieldType = "Reference";
            field.ReferenceCatalog = "План счетов";
            field.DisplayPattern = "{Код} - {Наименование}";
            field.DisplayFields = "Код,Наименование";
            field.IsRequired = true;
            field.Order = 8;
        }
        else if (field.DbColumnName?.Equals("use_settlements", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Участвует во взаиморасчетах";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 9;
        }
        else if (field.DbColumnName?.Equals("generate_postings", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Формировать проводки авансовых платежей";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 10;
        }
        else if (field.DbColumnName?.Equals("use_internal_settlements", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Участвует во внутренних взаиморасчетах";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 11;
        }
        else if (field.DbColumnName?.Equals("is_active", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Активен";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 12;
        }
        else if (field.DbColumnName?.Equals("description", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Примечание";
            field.FieldType = "String";
            field.Length = Math.Max(field.Length, 500);
            field.Order = 13;
        }
    }

    private async Task NormalizeAdvancePaymentRowsAsync(MetadataObject catalog)
    {
        try
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{catalog.TableName}""
                SET
                    ""use_organizations"" = COALESCE(""use_organizations"", true),
                    ""use_personnel"" = COALESCE(""use_personnel"", false),
                    ""use_currency"" = COALESCE(""use_currency"", false),
                    ""module_code"" = CASE
                        WHEN COALESCE(NULLIF(""module_code"", ''), '') = '' THEN 'Финансы'
                        WHEN UPPER(""module_code"") IN ('ФИН', 'ФИНАНСЫ', 'FIN', 'FINANCE') THEN 'Финансы'
                        WHEN UPPER(""module_code"") IN ('ОС', 'FIXEDASSETS') THEN 'Основные средства'
                        WHEN UPPER(""module_code"") IN ('ТМЦ', 'INVENTORY') THEN 'Учет материальных ценностей'
                        ELSE ""module_code""
                    END,
                    ""use_settlements"" = COALESCE(""use_settlements"", true),
                    ""generate_postings"" = COALESCE(""generate_postings"", true),
                    ""use_internal_settlements"" = COALESCE(""use_internal_settlements"", false),
                    ""is_active"" = COALESCE(""is_active"", true)
                WHERE true;");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка нормализации справочника 'Авансовые платежи': {ex.Message}");
        }
    }

    private async Task CreateAccountAccessCatalog(MetadataConfiguration config)
    {
        try
        {
            var catalogId = Guid.NewGuid();
            var catalog = new MetadataObject
            {
                Id = catalogId, Name = "Настройка доступа к счетам", TableName = "catalog_account_access",
                ObjectType = "Catalog", Description = "Настройка доступа операторов к счетам",
                Icon = "🔐", Order = 21, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetAccountAccessFields(catalogId)
            };
            await _context.MetadataObjects.AddAsync(catalog);
            await _context.SaveChangesAsync();
            await CreateTableForCatalogAsync(catalog);
            System.Diagnostics.Debug.WriteLine("Справочник 'Настройка доступа к счетам' создан");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Настройка доступа к счетам': {ex.Message}");
        }
    }

    private async Task CreateExchangeRateDiffCatalog(MetadataConfiguration config)
    {
        try
        {
            var catalogId = Guid.NewGuid();
            var catalog = new MetadataObject
            {
                Id = catalogId, Name = "Расчет курсовой разницы", TableName = "catalog_exchange_rate_diff",
                ObjectType = "Catalog", Description = "Справочник счетов для расчета курсовой разницы",
                Icon = "💱", Order = 22, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetExchangeRateDiffFields(catalogId)
            };
            await _context.MetadataObjects.AddAsync(catalog);
            await _context.SaveChangesAsync();
            await CreateTableForCatalogAsync(catalog);
            System.Diagnostics.Debug.WriteLine("Справочник 'Расчет курсовой разницы' создан");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Расчет курсовой разницы': {ex.Message}");
        }
    }

    private async Task EnsureExchangeRateDiffCatalogStructureAsync()
    {
        var catalog = await _context.MetadataObjects
            .Include(metadata => metadata.Fields)
            .FirstOrDefaultAsync(metadata => metadata.ObjectType == "Catalog" && metadata.Name == "Расчет курсовой разницы");

        if (catalog == null)
            return;

        await MigrateExchangeRateDifferenceFieldsAsync(catalog);

        var existingColumns = catalog.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
            .Select(field => field.DbColumnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var template in GetExchangeRateDiffFields(catalog.Id))
        {
            var existingField = catalog.Fields.FirstOrDefault(field =>
                field.DbColumnName.Equals(template.DbColumnName, StringComparison.OrdinalIgnoreCase));

            if (existingField == null)
            {
                template.Id = Guid.NewGuid();
                template.MetadataObjectId = catalog.Id;
                await _context.MetadataFields.AddAsync(template);
                await AddColumnToTableAsync(catalog.TableName, template);
                catalog.Fields.Add(template);
                existingColumns.Add(template.DbColumnName);
                continue;
            }

            existingField.Name = template.Name;
            existingField.FieldType = template.FieldType;
            existingField.Length = template.Length;
            existingField.Precision = template.Precision;
            existingField.Scale = template.Scale;
            existingField.IsRequired = template.IsRequired;
            existingField.IsUnique = template.IsUnique;
            existingField.Order = template.Order;
            existingField.ReferenceCatalog = template.ReferenceCatalog;
            existingField.DisplayPattern = template.DisplayPattern;
            existingField.DisplayFields = template.DisplayFields;
        }

        await _context.SaveChangesAsync();
    }

    private async Task MigrateExchangeRateDifferenceFieldsAsync(MetadataObject catalog)
    {
        var detailModeField = catalog.Fields.FirstOrDefault(field =>
            field.DbColumnName?.Equals("calculation_detail_mode", StringComparison.OrdinalIgnoreCase) == true);
        var legacyDetailModeField = catalog.Fields.FirstOrDefault(field =>
            field.DbColumnName?.Equals("expense_direction", StringComparison.OrdinalIgnoreCase) == true);
        if (legacyDetailModeField != null)
        {
            if (detailModeField == null)
            {
                legacyDetailModeField.DbColumnName = "calculation_detail_mode";
                legacyDetailModeField.Name = "Разрез расчета";
                legacyDetailModeField.FieldType = "Int";
                legacyDetailModeField.Order = 6;
            }
            else
            {
                _context.MetadataFields.Remove(legacyDetailModeField);
                catalog.Fields.Remove(legacyDetailModeField);
            }
        }

        var calculationMethodField = catalog.Fields.FirstOrDefault(field =>
            field.DbColumnName?.Equals("calculation_method", StringComparison.OrdinalIgnoreCase) == true);
        var legacyCalculationMethodField = catalog.Fields.FirstOrDefault(field =>
            field.DbColumnName?.Equals("calc_method", StringComparison.OrdinalIgnoreCase) == true);
        if (legacyCalculationMethodField != null)
        {
            if (calculationMethodField == null)
            {
                legacyCalculationMethodField.DbColumnName = "calculation_method";
                legacyCalculationMethodField.Name = "Способ расчета";
                legacyCalculationMethodField.FieldType = "String";
                legacyCalculationMethodField.Length = Math.Max(legacyCalculationMethodField.Length, 50);
                legacyCalculationMethodField.Order = 12;
            }
            else
            {
                _context.MetadataFields.Remove(legacyCalculationMethodField);
                catalog.Fields.Remove(legacyCalculationMethodField);
            }
        }

        await _context.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""{catalog.TableName}"" ADD COLUMN IF NOT EXISTS ""calculation_detail_mode"" integer;
            ALTER TABLE ""{catalog.TableName}"" ADD COLUMN IF NOT EXISTS ""calculation_method"" varchar(50);
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = '{catalog.TableName}'
                      AND column_name = 'expense_direction') THEN
                    UPDATE ""{catalog.TableName}""
                    SET ""calculation_detail_mode"" = COALESCE(""calculation_detail_mode"", ""expense_direction"")
                    WHERE ""calculation_detail_mode"" IS NULL;
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = '{catalog.TableName}'
                      AND column_name = 'calc_method') THEN
                    UPDATE ""{catalog.TableName}""
                    SET ""calculation_method"" = COALESCE(NULLIF(""calculation_method"", ''), ""calc_method"")
                    WHERE COALESCE(NULLIF(""calculation_method"", ''), '') = '';
                END IF;
            END $$;");
    }

    #endregion

    #region Finance data seeding

    private async Task AddAdvancePaymentDataToTable(MetadataObject catalog)
    {
        var items = new[]
        {
            new { code = "SERVICES", name = "Расчеты по услугам", debit = "18200000", credit = "31200000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = false },
            new { code = "SUPPLIERS", name = "Расчеты с поставщиками", debit = "18100000", credit = "31100000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = false },
            new { code = "EMPLOYEE_ADVANCES", name = "Расчеты с подотчетными лицами", debit = "15200000", credit = "36200000", module = "Финансы", organizations = false, personnel = true, currency = false, settlements = false, postings = false, internalSettlements = false },
            new { code = "ENERGY_ORGANIZATIONS", name = "Расчеты за электроэнергию организаций", debit = "14100000", credit = "32100000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = true },
            new { code = "ENERGY_POPULATION", name = "Расчеты за электроэнергию населения", debit = "14130000", credit = "32130000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = false, internalSettlements = false },
            new { code = "LOANS", name = "Займы", debit = "33200000", credit = "33200000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = false },
            new { code = "ENERGY_SERVICES", name = "Расчеты по услугам электроэнергии", debit = "14170100", credit = "32170100", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = true },
            new { code = "PENALTIES_POPULATION", name = "Расчеты по пени населения", debit = "14170200", credit = "32170200", module = "Финансы", organizations = false, personnel = false, currency = false, settlements = false, postings = false, internalSettlements = true },
            new { code = "ENERGY_PERSONAL_ACCOUNTS", name = "Расчеты за электроэнергию населения по лицевым счетам", debit = "14140000", credit = "32140000", module = "Финансы", organizations = false, personnel = false, currency = false, settlements = false, postings = false, internalSettlements = true },
            new { code = "OTHER_SERVICES", name = "Расчеты по прочим услугам", debit = "14160000", credit = "32160000", module = "Финансы", organizations = true, personnel = false, currency = false, settlements = true, postings = false, internalSettlements = false }
        };

        foreach (var item in items)
        {
            await UpsertCatalogSeedRowAsync(catalog.TableName, item.code, new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["code"] = item.code,
                ["name"] = item.name,
                ["use_organizations"] = item.organizations,
                ["use_personnel"] = item.personnel,
                ["use_currency"] = item.currency,
                ["module_code"] = item.module,
                ["debit_account"] = item.debit,
                ["credit_account"] = item.credit,
                ["use_settlements"] = item.settlements,
                ["generate_postings"] = item.postings,
                ["use_internal_settlements"] = item.internalSettlements,
                ["is_active"] = true,
                ["CreatedAt"] = DateTime.UtcNow,
                ["UpdatedAt"] = DateTime.UtcNow
            });
        }

        await DeactivateLegacyAdvancePaymentRowsAsync(catalog.TableName);
    }

    private async Task DeactivateLegacyAdvancePaymentRowsAsync(string tableName)
    {
        await _context.Database.ExecuteSqlRawAsync($@"
            UPDATE ""{tableName}""
            SET ""is_active"" = false,
                ""UpdatedAt"" = NOW()
            WHERE ""code"" IN ('AP01', 'AP02', 'AP03');");
    }

    private async Task MigrateAdvancePaymentsModuleFieldAsync(MetadataObject catalog)
    {
        var legacyField = catalog.Fields.FirstOrDefault(field =>
            field.DbColumnName?.Equals("arm_code", StringComparison.OrdinalIgnoreCase) == true);
        if (legacyField != null)
        {
            legacyField.DbColumnName = "module_code";
            legacyField.Name = "Остаток брать из модуля";
            legacyField.FieldType = "String";
            legacyField.Length = Math.Max(legacyField.Length, 50);
            legacyField.Order = 6;
        }

        await _context.Database.ExecuteSqlRawAsync($@"
            ALTER TABLE ""{catalog.TableName}"" ADD COLUMN IF NOT EXISTS ""module_code"" varchar(50);
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = '{catalog.TableName}'
                      AND column_name = 'arm_code') THEN
                    UPDATE ""{catalog.TableName}""
                    SET ""module_code"" = COALESCE(NULLIF(""module_code"", ''), ""arm_code"")
                    WHERE COALESCE(NULLIF(""arm_code"", ''), '') <> '';
                END IF;
            END $$;");
    }

    #endregion

    #region Finance service methods

    public async Task<List<Dictionary<string, object>>> GetAdvancePaymentPairsAsync()
    {
        var catalog = await _context.MetadataObjects.FirstOrDefaultAsync(m => m.Name == "Авансовые платежи" && m.ObjectType == "Catalog");
        if (catalog == null) return new List<Dictionary<string, object>>();

        var result = new List<Dictionary<string, object>>();
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $@"SELECT * FROM ""{catalog.TableName}"" WHERE ""is_active"" = true ORDER BY ""code""";
        try
        {
            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                result.Add(row);
            }
        }
        finally { await _context.Database.CloseConnectionAsync(); }
        return result;
    }

    public async Task<List<Dictionary<string, object>>> GetMutualSettlementsAsync(DateTime startDate, DateTime endDate)
    {
        var result = new List<Dictionary<string, object>>();
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = @"
            SELECT p.*, o.""name"" as organization_name FROM doc_postings p
            LEFT JOIN catalog_organizations o ON p.organization_id::text = o.""Id""::text
            WHERE p.posting_date >= @startDate AND p.posting_date <= @endDate AND p.is_active = true
            ORDER BY p.posting_date, o.""name""";
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@startDate", startDate));
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@endDate", endDate));
        try
        {
            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                result.Add(row);
            }
        }
        finally { await _context.Database.CloseConnectionAsync(); }
        return result;
    }

    public async Task<List<Dictionary<string, object>>> GetReconciliationStatementAsync(Guid organizationId, DateTime startDate, DateTime endDate)
    {
        var result = new List<Dictionary<string, object>>();
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = @"
            SELECT posting_date as ""Дата"", doc_number as ""Номер документа"", document_type as ""Тип"",
                   debit_account as ""Дебет"", credit_account as ""Кредит"", amount_kgs as ""Сумма"", description as ""Примечание""
            FROM doc_postings WHERE organization_id::text = @orgId AND posting_date >= @startDate AND posting_date <= @endDate AND is_active = true
            ORDER BY posting_date";
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@orgId", organizationId.ToString()));
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@startDate", startDate));
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@endDate", endDate));
        try
        {
            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                result.Add(row);
            }
        }
        finally { await _context.Database.CloseConnectionAsync(); }
        return result;
    }

    public async Task<OrganizationBalanceCalculationResult> CalculateOrganizationBalancesAsync(DateTime startDate, DateTime endDate)
    {
        return await new OrganizationBalanceService(_context).CalculateAsync(startDate, endDate);
    }

    #endregion
}
