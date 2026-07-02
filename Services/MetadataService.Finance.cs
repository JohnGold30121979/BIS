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
            new MetadataField { Id = Guid.NewGuid(), Name = "Наименование", DbColumnName = "name", FieldType = "String", Length = 200, IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет дебета", DbColumnName = "debit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Счет кредита", DbColumnName = "credit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", DisplayPattern = "{Код} - {Наименование}", DisplayFields = "Код,Наименование", IsRequired = true, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "АРМ", DbColumnName = "arm_code", FieldType = "String", Length = 50, IsRequired = false, Order = 5, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Взаиморасчеты", DbColumnName = "use_settlements", FieldType = "Bool", IsRequired = true, Order = 6, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Формировать проводки", DbColumnName = "generate_postings", FieldType = "Bool", IsRequired = true, Order = 7, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", IsRequired = true, Order = 8, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Примечание", DbColumnName = "description", FieldType = "String", Length = 500, IsRequired = false, Order = 9, MetadataObjectId = metadataObjectId }
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
            var catalog = new MetadataObject
            {
                Id = Guid.NewGuid(), Name = "Авансовые платежи", TableName = "catalog_advance_payments",
                ObjectType = "Catalog", Description = "Справочник пар счетов для учета авансовых платежей",
                Icon = "💳", Order = 20, IsSystem = true, MetadataConfigId = config.Id,
                Fields = GetAdvancePaymentFields(Guid.NewGuid())
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
            new { code = "AP01", name = "Авансы поставщикам (1610/1110)", debit = "1610", credit = "1110", arm = "Finance", settlements = true, postings = false },
            new { code = "AP02", name = "Авансы от покупателей (1110/4410)", debit = "1110", credit = "4410", arm = "Finance", settlements = true, postings = false },
            new { code = "AP03", name = "Подотчетные лица (1630/1110)", debit = "1630", credit = "1110", arm = "Finance", settlements = true, postings = false }
        };
        foreach (var item in items)
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""{catalog.TableName}"" (""Id"",""code"",""name"",""debit_account"",""credit_account"",""arm_code"",""use_settlements"",""generate_postings"",""is_active"",""CreatedAt"",""UpdatedAt"")
                VALUES ('{Guid.NewGuid()}','{item.code}','{item.name.Replace("'","''")}','{item.debit}','{item.credit}','{item.arm}',{item.settlements.ToString().ToLower()},{item.postings.ToString().ToLower()},true,NOW(),NOW())");
        }
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

    #endregion
}