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
            new MetadataField { Id = Guid.NewGuid(), Name = "Орг", DbColumnName = "use_organizations", FieldType = "Bool", IsRequired = true, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Таб №", DbColumnName = "use_personnel", FieldType = "Bool", IsRequired = true, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Валюта", DbColumnName = "use_currency", FieldType = "Bool", IsRequired = true, Order = 5, MetadataObjectId = metadataObjectId },
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
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет", DbColumnName = "account_id", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Способ расчета", DbColumnName = "calc_method", FieldType = "String", Length = 50, IsRequired = false, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Валюта", DbColumnName = "currency_id", FieldType = "Reference", ReferenceCatalog = "Справочник валют", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = false, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", IsRequired = true, Order = 5, MetadataObjectId = metadataObjectId }
        };
    }

    #endregion

    #region Finance catalogs creation

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
            field.Name = "Орг";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 3;
        }
        else if (field.DbColumnName?.Equals("use_personnel", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Таб №";
            field.FieldType = "Bool";
            field.IsRequired = true;
            field.Order = 4;
        }
        else if (field.DbColumnName?.Equals("use_currency", StringComparison.OrdinalIgnoreCase) == true)
        {
            field.Name = "Валюта";
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
                    ""module_code"" = COALESCE(NULLIF(""module_code"", ''), 'ФИНАНСЫ'),
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
            var catalog = new MetadataObject
            {
                Id = Guid.NewGuid(), Name = "Настройка доступа к счетам", TableName = "catalog_account_access",
                ObjectType = "Catalog", Description = "Настройка доступа операторов к счетам",
                Icon = "🔐", Order = 21, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetAccountAccessFields(Guid.NewGuid())
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
            var catalog = new MetadataObject
            {
                Id = Guid.NewGuid(), Name = "Расчет курсовой разницы", TableName = "catalog_exchange_rate_diff",
                ObjectType = "Catalog", Description = "Справочник счетов для расчета курсовой разницы",
                Icon = "💱", Order = 22, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetExchangeRateDiffFields(Guid.NewGuid())
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

    #endregion

    #region Finance data seeding

    private async Task AddAdvancePaymentDataToTable(MetadataObject catalog)
    {
        var items = new[]
        {
            new { code = "AP01", name = "Расчеты с заказчиками (сом)", debit = "14100000", credit = "32200000", module = "ФИНАНСЫ", organizations = true, personnel = false, currency = false, settlements = true, postings = true, internalSettlements = false },
            new { code = "AP02", name = "Расчеты по налогу на прибыль", debit = "15300000", credit = "34100000", module = "ФИНАНСЫ", organizations = false, personnel = false, currency = false, settlements = false, postings = true, internalSettlements = false },
            new { code = "AP03", name = "Расчеты с сотрудниками", debit = "15200000", credit = "35200000", module = "ФИНАНСЫ", organizations = false, personnel = true, currency = false, settlements = false, postings = true, internalSettlements = false }
        };
        foreach (var item in items)
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""{catalog.TableName}""
                    (""Id"",""code"",""name"",""use_organizations"",""use_personnel"",""use_currency"",
                     ""module_code"",""debit_account"",""credit_account"",""use_settlements"",
                     ""generate_postings"",""use_internal_settlements"",""is_active"",""CreatedAt"",""UpdatedAt"")
                VALUES
                    ('{Guid.NewGuid()}','{item.code}','{item.name.Replace("'","''")}',
                     {item.organizations.ToString().ToLower()},{item.personnel.ToString().ToLower()},{item.currency.ToString().ToLower()},
                     '{item.module}','{item.debit}','{item.credit}',{item.settlements.ToString().ToLower()},
                     {item.postings.ToString().ToLower()},{item.internalSettlements.ToString().ToLower()},true,NOW(),NOW())");
        }
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
            LEFT JOIN catalog_organizations o ON p.organization_id = o.""Id""
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
            FROM doc_postings WHERE organization_id = @orgId AND posting_date >= @startDate AND posting_date <= @endDate AND is_active = true
            ORDER BY posting_date";
        command.Parameters.Add(new Npgsql.NpgsqlParameter("@orgId", organizationId));
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
