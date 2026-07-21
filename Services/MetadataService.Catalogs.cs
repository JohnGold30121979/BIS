using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed record CurrencyRateLookupResult(decimal Rate, DateTime RateDate, string Source);

    public partial class MetadataService
    {
        #region catalogs
        private async Task CreateCurrencyRatesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Справочник курсов валют",
                    TableName = "catalog_currency_rates",
                    ObjectType = "Catalog",
                    Description = "Справочник курсов валют",
                    Icon = "💱",
                    Order = 11,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetCurrencyRateFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddCurrencyRatesDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Курсы валют' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Курсы валют': {ex.Message}");
            }
        }

        private async Task CreateCurrencyCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Справочник валют",
                    TableName = "catalog_currencies",
                    ObjectType = "Catalog",
                    Description = "Справочник валют",
                    Icon = "💵",
                    Order = 7,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetCurrencyFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddCurrencyDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник валюы создан!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Курсы валют': {ex.Message}");
            }
        }

        private async Task EnsureCurrencyCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Справочник валют");

            if (catalog == null)
                return;

            var existingColumns = catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .Select(field => field.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetCurrencyFields(catalog.Id))
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
            await AddCurrencyDataToTable(catalog);
        }

        private async Task CreateMaterialTypesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Виды материалов",
                    TableName = "catalog_material_types",
                    ObjectType = "Catalog",
                    Description = "Справочник видов материалов",
                    Icon = "📦",
                    Order = 9,  // после категорий (у категорий было 8)
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetMaterialTypeFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddMaterialTypesDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Виды материалов' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Виды материалов': {ex.Message}");
            }
        }

        private async Task CreateMaterialCatalog(MetadataConfiguration config)
        {
            try
            {
                // Справочник Справочник материалов на складе
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Справочник материалов",
                    TableName = "catalog_materials",
                    ObjectType = "Catalog",
                    Description = "Справочник материалов на складе",
                    Icon = "📦",
                    Order = 10,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetMaterialFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Справочник материалов на складе': {ex.Message}");
            }
        }     

        private async Task CreateMaterialCategoriesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Наименования категорий",
                    TableName = "catalog_material_categories",
                    ObjectType = "Catalog",
                    Description = "Справочник категорий материалов",
                    Icon = "📁",
                    Order = 8,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetMaterialCategoryFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddMaterialCategoriesDataToTable(catalog);  // ← добавление данных

                System.Diagnostics.Debug.WriteLine("Справочник 'Наименования категорий' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Наименования категорий': {ex.Message}");
            }
        }

        private async Task CreateBanksCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Банки",
                    TableName = $"catalog_banks",
                    ObjectType = "Catalog",
                    Description = "Справочник банков Кыргызской Республики",
                    Icon = "🏦",
                    Order = 11,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetBankFields(Guid.NewGuid())
                };            

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddBanksDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Банки' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Банки': {ex.Message}");
            }
        }

        private async Task CreateChartOfAccountsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "План счетов",
                    TableName = $"catalog_plan_schetov_{DateTime.Now:yyyyMMddHHmmss}",
                    ObjectType = "Catalog",
                    Description = "План счетов бухгалтерского учета Кыргызской Республики",
                    Icon = "📊",
                    Order = 10,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = new List<MetadataField>()
                };

                foreach (var field in GetChartOfAccountsFields(catalog.Id))
                {
                    catalog.Fields.Add(field);
                }

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddChartOfAccountsDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'План счетов' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'План счетов': {ex.Message}");
            }
        }

        private async Task EnsureChartOfAccountsCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name == "План счетов");

            if (catalog == null)
                return;

            var existingNames = catalog.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingColumns = catalog.Fields.Select(f => f.DbColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetChartOfAccountsAnalyticFields(catalog.Id))
            {
                if (existingNames.Contains(field.Name) || existingColumns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = catalog.Id;

                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(catalog.TableName, field);

                existingNames.Add(field.Name);
                existingColumns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
        }

        private async Task CreateAccountAnalyticsLinksCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = AccountAnalyticsCatalogNames.CatalogName,
                    TableName = "catalog_account_analytics_links",
                    ObjectType = "Catalog",
                    Description = "Соответствие флагов плана счетов справочникам аналитики",
                    Icon = "🔗",
                    Order = 13,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = new List<MetadataField>()
                };

                foreach (var field in GetAccountAnalyticsLinkFields(catalog.Id))
                {
                    catalog.Fields.Add(field);
                }

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await EnsureAccountAnalyticsLinksDataAsync(catalog);

                System.Diagnostics.Debug.WriteLine($"Справочник '{AccountAnalyticsCatalogNames.CatalogName}' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Ошибка создания справочника '{AccountAnalyticsCatalogNames.CatalogName}': {ex.Message}");
            }
        }

        private async Task EnsureAccountAnalyticsLinksCatalogAsync(MetadataConfiguration? config = null)
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m =>
                    m.ObjectType == "Catalog" &&
                    m.Name == AccountAnalyticsCatalogNames.CatalogName);

            if (catalog == null)
            {
                config ??= await _context.MetadataConfigurations.FirstOrDefaultAsync();
                if (config == null)
                    return;

                await CreateAccountAnalyticsLinksCatalog(config);
                return;
            }

            var existingNames = catalog.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingColumns = catalog.Fields.Select(f => f.DbColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetAccountAnalyticsLinkFields(catalog.Id))
            {
                if (existingNames.Contains(field.Name) || existingColumns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = catalog.Id;

                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(catalog.TableName, field);

                existingNames.Add(field.Name);
                existingColumns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
            await EnsureAccountAnalyticsLinksDataAsync(catalog);
        }

        private async Task EnsureManagedDocumentAnalyticFieldsAsync(IEnumerable<MetadataObject> documents)
        {
            var targetDocumentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                CashOrderDocumentName,
                "Платежное поручение"
            };

            foreach (var document in documents.Where(document => targetDocumentNames.Contains(document.Name)))
            {
                var existingNames = document.Fields
                    .Select(field => field.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var existingColumns = document.Fields
                    .Select(field => field.DbColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nextOrder = document.Fields.Count == 0
                    ? 1
                    : document.Fields.Max(field => field.Order) + 1;

                foreach (var field in GetDocumentAnalyticReferenceFields(document.Id, nextOrder))
                {
                    if (existingNames.Contains(field.Name) || existingColumns.Contains(field.DbColumnName))
                    {
                        nextOrder++;
                        continue;
                    }

                    field.Id = Guid.NewGuid();
                    field.MetadataObjectId = document.Id;
                    field.Order = nextOrder++;

                    await _context.MetadataFields.AddAsync(field);
                    await AddColumnToTableAsync(document.TableName, field);

                    document.Fields.Add(field);
                    existingNames.Add(field.Name);
                    existingColumns.Add(field.DbColumnName);
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsurePaymentOrderDocumentStructureAsync(IEnumerable<MetadataObject> documents)
        {
            var document = documents.FirstOrDefault(item =>
                item.Name.Equals("Платежное поручение", StringComparison.OrdinalIgnoreCase));
            if (document == null)
                return;

            await RemoveDuplicateMetadataFieldsAsync(document);

            var existingByColumn = document.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .GroupBy(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(field => field.Order).First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var desired in GetPaymentOrderFields(document.Id))
            {
                if (existingByColumn.TryGetValue(desired.DbColumnName, out var existing))
                {
                    existing.Name = desired.Name;
                    existing.FieldType = desired.FieldType;
                    existing.ReferenceCatalog = desired.ReferenceCatalog;
                    existing.DisplayPattern = desired.DisplayPattern;
                    existing.DisplayFields = desired.DisplayFields;
                    existing.Order = desired.Order;
                    existing.IsRequired = desired.IsRequired;
                    existing.IsUnique = desired.IsUnique;
                    existing.Length = desired.Length;
                    existing.Precision = desired.Precision;
                    existing.Scale = desired.Scale;
                    continue;
                }

                desired.Id = Guid.NewGuid();
                desired.MetadataObjectId = document.Id;
                await _context.MetadataFields.AddAsync(desired);
                await AddColumnToTableAsync(document.TableName, desired);
                document.Fields.Add(desired);
                existingByColumn[desired.DbColumnName] = desired;
            }

            document.UsePostings = true;
            await _context.SaveChangesAsync();
        }

        public async Task<CurrencyRateLookupResult?> GetCurrencyRateForDateAsync(Guid currencyId, DateTime documentDate)
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    item.Name == "Справочник курсов валют");
            if (catalog == null)
                return null;

            var sql = $@"
                SELECT rate_date,
                       COALESCE(NULLIF(rate_nb, 0), NULLIF(rate_commercial, 0), 0) AS rate,
                       CASE
                           WHEN COALESCE(rate_nb, 0) <> 0 THEN 'НБКР'
                           WHEN COALESCE(rate_commercial, 0) <> 0 THEN 'Коммерческий'
                           ELSE ''
                       END AS source
                FROM {QuoteIdentifier(catalog.TableName)}
                WHERE currency_id::text = @currencyId
                  AND rate_date::date <= @documentDate
                  AND COALESCE(is_active, true) = true
                ORDER BY rate_date DESC
                LIMIT 1;";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            var currencyParameter = command.CreateParameter();
            currencyParameter.ParameterName = "@currencyId";
            currencyParameter.Value = currencyId.ToString();
            command.Parameters.Add(currencyParameter);

            var dateParameter = command.CreateParameter();
            dateParameter.ParameterName = "@documentDate";
            dateParameter.Value = documentDate.Date;
            command.Parameters.Add(dateParameter);

            var connectionOpened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return null;

                var rate = reader["rate"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["rate"], CultureInfo.InvariantCulture);
                if (rate <= 0)
                    return null;

                var rateDate = reader["rate_date"] is DateTime date ? date : documentDate.Date;
                var source = reader["source"]?.ToString() ?? "Справочник курсов валют";
                return new CurrencyRateLookupResult(rate, rateDate, source);
            }
            finally
            {
                if (connectionOpened)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        // Сотрудники
        private async Task CreateEmployeesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Сотрудники (Списочный состав)",
                    TableName = "catalog_employees",
                    ObjectType = "Catalog",
                    Description = "Список сотрудников предприятия",
                    Icon = "👥",
                    Order = 3,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetEmployeeCatalogFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Сотрудники' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Сотрудники': {ex.Message}");
            }
        }

        private async Task EnsureEmployeesCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m =>
                    m.ObjectType == "Catalog" &&
                    m.Name == "Сотрудники (Списочный состав)");

            if (catalog == null)
                return;

            var existingNames = catalog.Fields
                .Select(field => field.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingColumns = catalog.Fields
                .Select(field => field.DbColumnName)
                .Where(column => !string.IsNullOrWhiteSpace(column))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetEmployeeCatalogFields(catalog.Id))
            {
                if (existingNames.Contains(field.Name) || existingColumns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = catalog.Id;

                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(catalog.TableName, field);

                catalog.Fields.Add(field);
                existingNames.Add(field.Name);
                existingColumns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
        }

        // Основные средства
        private async Task CreateAssetsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Основные средства",
                    TableName = "catalog_assets",
                    ObjectType = "Catalog",
                    Description = "Основные средства предприятия",
                    Icon = "⚙️",
                    Order = 4,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = new List<MetadataField>()
                };
                foreach (var field in GetFixedAssetFields(catalog.Id))
                    catalog.Fields.Add(field);
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Основные средства' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Основные средства': {ex.Message}");
            }
        }

        // Справочник государств
        private async Task CreateCountriesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Государства",
                    TableName = "catalog_countries",
                    ObjectType = "Catalog",
                    Description = "Справочник государств",
                    Icon = "🌍",
                    Order = 3,  // после организаций
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetCountryFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddCountriesDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Государства' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Государства': {ex.Message}");
            }
        }
        // Участки
        private async Task CreateSitesCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Участки",
                    TableName = "catalog_sites",
                    ObjectType = "Catalog",
                    Description = "Справочник участков предприятия",
                    Icon = "🏭",
                    Order = 6,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetSiteFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddSiteDataToTable(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Участки' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Участки': {ex.Message}");
            }
        }

        // МОЛ
        private async Task CreateResponsiblePersonsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "МОЛ",
                    TableName = "catalog_responsible_persons",
                    ObjectType = "Catalog",
                    Description = "Материально-ответственные лица",
                    Icon = "👥",
                    Order = 7,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetResponsiblePersonFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'МОЛ' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'МОЛ': {ex.Message}");
            }
        }

        // Организации (исправленный, без лишних данных)
        private async Task CreateOrganizationsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Организации",
                    TableName = "catalog_organizations",
                    ObjectType = "Catalog",
                    Description = "Справочник организаций предприятия",
                    Icon = "🏢",
                    Order = 1,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetOrganizationFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddPrimaryOrganizationDataToTable(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Организации' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Организации': {ex.Message}");
            }
        }

        private async Task EnsureOrganizationsCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name == "Организации");

            if (catalog == null)
                return;

            var existingNames = catalog.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingColumns = catalog.Fields.Select(f => f.DbColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetOrganizationFields(catalog.Id))
            {
                if (existingNames.Contains(field.Name) || existingColumns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = catalog.Id;

                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(catalog.TableName, field);

                catalog.Fields.Add(field);
                existingNames.Add(field.Name);
                existingColumns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
            await EnsurePrimaryOrganizationDataAsync(catalog);
        }

        private async Task CreateBankAccountsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Расчетные счета организаций",
                    TableName = "catalog_bank_accounts",
                    ObjectType = "Catalog",
                    Description = "Расчетные счета организаций",
                    Icon = "🏦",
                    Order = 12,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetBankAccountFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Расчетные счета организаций' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Расчетные счета организаций': {ex.Message}");
            }
        }

        private async Task CreateCashDesksCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Кассы",
                    TableName = "catalog_cash_desks",
                    ObjectType = "Catalog",
                    Description = "Справочник касс предприятия",
                    Icon = "💰",
                    Order = 12,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetCashDeskFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                // Добавим начальные данные: основная касса в сомах
                await AddInitialCashDeskData(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Кассы' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Кассы': {ex.Message}");
            }
        }

        private async Task EnsureCashDesksCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name == "Кассы");

            if (catalog == null)
                return;

            foreach (var field in catalog.Fields)
            {
                if (field.DbColumnName?.Equals("name", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Name = "Наименование кассы";
                    field.Order = 1;
                    field.IsRequired = true;
                    field.Length = 200;
                }
                else if (field.DbColumnName?.Equals("code", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Name = "Счет";
                    field.FieldType = "Reference";
                    field.ReferenceCatalog = "План счетов";
                    field.DisplayPattern = "{Код} - {Наименование}";
                    field.DisplayFields = "Код,Наименование";
                    field.Order = 2;
                    field.IsRequired = true;
                    field.Length = 80;
                }
                else if (field.DbColumnName?.Equals("currency_id", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Order = Math.Max(field.Order, 4);
                }
                else if (field.DbColumnName?.Equals("initial_balance", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Order = Math.Max(field.Order, 5);
                }
                else if (field.DbColumnName?.Equals("current_balance", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Order = Math.Max(field.Order, 6);
                }
                else if (field.DbColumnName?.Equals("is_active", StringComparison.OrdinalIgnoreCase) == true)
                {
                    field.Order = Math.Max(field.Order, 7);
                }
            }

            var existingColumns = catalog.Fields
                .Select(field => field.DbColumnName)
                .Where(column => !string.IsNullOrWhiteSpace(column))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var cashNumberField = GetCashDeskFields(catalog.Id)
                .First(field => field.DbColumnName.Equals("cash_number", StringComparison.OrdinalIgnoreCase));

            if (!existingColumns.Contains(cashNumberField.DbColumnName))
            {
                cashNumberField.Id = Guid.NewGuid();
                cashNumberField.MetadataObjectId = catalog.Id;
                await _context.MetadataFields.AddAsync(cashNumberField);
                await AddColumnToTableAsync(catalog.TableName, cashNumberField);
                catalog.Fields.Add(cashNumberField);
            }

            await _context.SaveChangesAsync();

            try
            {
                await _context.Database.ExecuteSqlRawAsync($@"
                    ALTER TABLE ""{catalog.TableName}""
                    ALTER COLUMN ""code"" TYPE varchar(80);");

                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{catalog.TableName}""
                    SET ""cash_number"" = COALESCE(NULLIF(""cash_number"", ''), '1')
                    WHERE ""cash_number"" IS NULL OR ""cash_number"" = '';");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка заполнения номера кассы: {ex.Message}");
            }
        }


        // Справочник "Налоги"
        private async Task CreateTaxCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Налоги",
                    TableName = "catalog_taxes",
                    ObjectType = "Catalog",
                    Description = "Справочник налогов с кодами ЭСФ для НДС и налога с продаж.",
                    Icon = "💰",
                    Order = 14,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetTaxFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddTaxDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Налоги' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Налоги': {ex.Message}");
            }
        }

        private async Task EnsureTaxCatalogStructureAsync() =>
            await EnsureCatalogStructureAsync("Налоги", GetTaxFields);

        // Справочник "Подразделения"
        private async Task CreateDivisionCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Подразделения",
                    TableName = "catalog_divisions",
                    ObjectType = "Catalog",
                    Description = "Справочник подразделений предприятия",
                    Icon = "🏢",
                    Order = 15,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetStandardCatalogFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Подразделения' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Подразделения': {ex.Message}");
            }
        }

        // Справочник "Должности"
        private async Task CreatePositionsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Должности",
                    TableName = "catalog_positions",
                    ObjectType = "Catalog",
                    Description = "Справочник должностей",
                    Icon = "👔",
                    Order = 14,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetPositionFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddPositionDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Должности' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Должности': {ex.Message}");
            }
        }

        private async Task EnsurePositionCatalogDataAsync()
        {
            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.Name == "Должности" && m.ObjectType == "Catalog");

            if (catalog == null)
                return;

            // Проверяем, есть ли данные
            var checkSql = $"SELECT COUNT(*) FROM \"{catalog.TableName}\"";
            using var checkCommand = _context.Database.GetDbConnection().CreateCommand();
            checkCommand.CommandText = checkSql;
            await _context.Database.OpenConnectionAsync();
            var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
            await _context.Database.CloseConnectionAsync();

            // Если данных нет, добавляем
            if (count == 0)
            {
                await AddPositionDataToTable(catalog);
                System.Diagnostics.Debug.WriteLine("Добавлены начальные данные в справочник 'Должности'");
            }
        }


        // Справочник "Участки"       
        private async Task CreatePlotCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Участки (новые)",
                    TableName = "catalog_plots",
                    ObjectType = "Catalog",
                    Description = "Справочник участков (склады, цеха, отделы)",
                    Icon = "📍",
                    Order = 16,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetPlotFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Участки (новые)' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Участки': {ex.Message}");
            }
        }

        // Справочник "Виды поставки"
        private async Task CreateSupplyKindCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalogId = Guid.NewGuid();
                var catalog = new MetadataObject
                {
                    Id = catalogId,
                    Name = "Виды поставки",
                    TableName = "catalog_supply_kinds",
                    ObjectType = "Catalog",
                    Description = "Классификатор вида поставки ЭСФ для поля invoiceDeliveryTypeCode.",
                    Icon = "📦",
                    Order = 17,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetSupplyKindFields(catalogId)
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddSupplyKindDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Виды поставки' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Виды поставки': {ex.Message}");
            }
        }

        private async Task EnsureSupplyKindCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Виды поставки");

            if (catalog == null)
                return;

            var existingColumns = catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .Select(field => field.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetSupplyKindFields(catalog.Id))
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
        }

        // Справочник "Виды оплаты"
        private async Task CreatePaymentKindCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Виды оплаты",
                    TableName = "catalog_payment_kinds",
                    ObjectType = "Catalog",
                    Description = "Справочник видов оплаты с кодами ЭСФ для paymentTypeCode.",
                    Icon = "💳",
                    Order = 18,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetPaymentKindFields(Guid.NewGuid())
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddPaymentKindDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Виды оплаты' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Виды оплаты': {ex.Message}");
            }
        }

        private async Task EnsurePaymentKindCatalogStructureAsync() =>
            await EnsureCatalogStructureAsync("Виды оплаты", GetPaymentKindFields);

        // Справочник "Классификация платежей"
        private async Task CreatePaymentClassificationCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalogId = Guid.NewGuid();
                var catalog = new MetadataObject
                {
                    Id = catalogId,
                    Name = "Классификация платежей",
                    TableName = "catalog_payment_classifications",
                    ObjectType = "Catalog",
                    Description = "Классификация платежей из файла FoxPro.",
                    Icon = "🏷",
                    Order = 20,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetPaymentClassificationFields(catalogId)
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Классификация платежей' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Классификация платежей': {ex.Message}");
            }
        }

        private async Task EnsurePaymentClassificationCatalogStructureAsync() =>
            await EnsureCatalogStructureAsync("Классификация платежей", GetPaymentClassificationFields);

        // Справочник "Типы поставки"
        private async Task CreateDeliveryTypeCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalogId = Guid.NewGuid();
                var catalog = new MetadataObject
                {
                    Id = catalogId,
                    Name = "Типы поставки",
                    TableName = "catalog_delivery_types",
                    ObjectType = "Catalog",
                    Description = "Классификатор налогового типа поставки ЭСФ для поля vatDeliveryTypeCode.",
                    Icon = "🚚",
                    Order = 19,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetDeliveryTypeFields(catalogId)
                };

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                await AddDeliveryTypeDataToTable(catalog);

                System.Diagnostics.Debug.WriteLine("Справочник 'Типы поставки' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Типы поставки': {ex.Message}");
            }
        }

        private async Task EnsureDeliveryTypeCatalogStructureAsync() =>
            await EnsureCatalogStructureAsync("Типы поставки", GetDeliveryTypeFields);

        private async Task EnsureCatalogStructureAsync(
            string catalogName,
            Func<Guid, List<MetadataField>> getFields)
        {
            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == catalogName);

            if (catalog == null)
                return;

            var isChartOfAccountsCatalog =
                catalog.Name.Equals("План счетов", StringComparison.OrdinalIgnoreCase);

            await RemoveDuplicateMetadataFieldsAsync(catalog);

            if (isChartOfAccountsCatalog)
            {
                await MigrateChartOfAccountsModuleFieldAsync(catalog);
                await RemoveDuplicateMetadataFieldsAsync(catalog);
            }

            var existingByColumn = catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .GroupBy(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(field => field.Order).First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var desired in getFields(catalog.Id))
            {
                if (existingByColumn.TryGetValue(desired.DbColumnName, out var existing))
                {
                    existing.Name = desired.Name;
                    existing.FieldType = desired.FieldType;
                    existing.ReferenceCatalog = desired.ReferenceCatalog;
                    existing.DisplayPattern = desired.DisplayPattern;
                    existing.DisplayFields = desired.DisplayFields;
                    existing.Order = desired.Order;
                    existing.IsRequired = desired.IsRequired;
                    existing.IsUnique = desired.IsUnique;
                    existing.Length = desired.Length;
                    existing.Precision = desired.Precision;
                    existing.Scale = desired.Scale;
                    continue;
                }

                desired.Id = Guid.NewGuid();
                desired.MetadataObjectId = catalog.Id;
                await _context.MetadataFields.AddAsync(desired);
                await AddColumnToTableAsync(catalog.TableName, desired);
                catalog.Fields.Add(desired);
                existingByColumn[desired.DbColumnName] = desired;
            }

            if (isChartOfAccountsCatalog)
                await RemoveObsoleteChartOfAccountsMetadataFieldsAsync(catalog);

            await _context.SaveChangesAsync();

            if (isChartOfAccountsCatalog)
            {
                await NormalizeChartOfAccountsMetadataAsync(catalog);
                await SynchronizeChartOfAccountsSourceDataAsync(catalog);
            }
        }

        private async Task RemoveObsoleteChartOfAccountsMetadataFieldsAsync(MetadataObject catalog)
        {
            var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "code",
                "name",
                "account_type",
                "description",
                "level",
                "is_active",
                "closing_module_code",
                "analytic_group",
                "print_mode",
                "balance_mode",
                "link_organizations",
                "link_employees",
                "link_currencies",
                "link_personal_accounts",
                "link_materials",
                "link_construction_objects",
                "link_sites",
                "tax_code",
                "account_currency_id"
            };

            var obsoleteColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "prsch", "pr_sch", "pr_sc7", "sv_o", "sv_t", "sv_m", "sv_v", "sv_l",
                "nal", "kodf_rs", "kodf_rb", "kod_gr", "sv_uch", "str_bal",
                "kod_arm", "pr_o", "pr_pr", "sv_j", "closing_arm"
            };

            var obsoleteFields = catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName) &&
                                (obsoleteColumns.Contains(field.DbColumnName) ||
                                 !allowedColumns.Contains(field.DbColumnName)))
                .ToList();

            if (obsoleteFields.Count == 0)
                return;

            _context.MetadataFields.RemoveRange(obsoleteFields);
            foreach (var field in obsoleteFields)
                catalog.Fields.Remove(field);
        }

        private async Task NormalizeChartOfAccountsMetadataAsync(MetadataObject catalog)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{catalog.TableName}""
                    SET
                        ""closing_module_code"" = COALESCE(""closing_module_code"", ''),
                        ""analytic_group"" = COALESCE(""analytic_group"", ''),
                        ""print_mode"" = COALESCE(""print_mode"", ''),
                        ""balance_mode"" = COALESCE(""balance_mode"", ''),
                        ""link_organizations"" = COALESCE(""link_organizations"", false),
                        ""link_employees"" = COALESCE(""link_employees"", false),
                        ""link_currencies"" = COALESCE(""link_currencies"", false),
                        ""link_personal_accounts"" = COALESCE(""link_personal_accounts"", false),
                        ""link_materials"" = COALESCE(""link_materials"", false),
                        ""link_construction_objects"" = COALESCE(""link_construction_objects"", false),
                        ""link_sites"" = COALESCE(""link_sites"", false),
                        ""tax_code"" = COALESCE(""tax_code"", '')
                    WHERE
                        ""closing_module_code"" IS NULL OR
                        ""analytic_group"" IS NULL OR
                        ""print_mode"" IS NULL OR
                        ""balance_mode"" IS NULL OR
                        ""link_organizations"" IS NULL OR
                        ""link_employees"" IS NULL OR
                        ""link_currencies"" IS NULL OR
                        ""link_personal_accounts"" IS NULL OR
                        ""link_materials"" IS NULL OR
                        ""link_construction_objects"" IS NULL OR
                        ""link_sites"" IS NULL OR
                        ""tax_code"" IS NULL;");

                await NormalizeChartOfAccountsClosingModulesAsync(catalog);
                await NormalizeChartOfAccountsModeFieldsAsync(catalog);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Ошибка нормализации метаданных плана счетов: {ex.Message}");
            }
        }

        private async Task SynchronizeChartOfAccountsSourceDataAsync(MetadataObject catalog)
        {
            var accounts = InitialDataProvider.GetChartOfAccounts();
            var existingCodes = await GetCatalogCodeSetAsync(catalog.TableName);
            var modules = await GetAvailableModulesAsync();
            var sourceCodes = accounts
                .Select(account => account.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var account in accounts)
            {
                var values = BuildChartOfAccountsCatalogValues(account, modules);
                await UpsertCatalogSeedRowAsync(catalog.TableName, account.Code, values);
                existingCodes.Add(account.Code);
            }

            await DeactivateLegacyFallbackChartAccountsAsync(catalog, sourceCodes);
        }

        private async Task<int> DeactivateLegacyFallbackChartAccountsAsync(
            MetadataObject catalog,
            ISet<string> sourceCodes)
        {
            var fallbackOnlyCodes = InitialDataProvider.GetFallbackChartOfAccounts()
                .Select(account => account.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code) && !sourceCodes.Contains(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return await DeactivateChartAccountsByCodeAsync(catalog.TableName, fallbackOnlyCodes);
        }

        private async Task<int> DeactivateChartAccountsByCodeAsync(string tableName, IReadOnlyCollection<string> codes)
        {
            if (codes.Count == 0)
                return 0;

            var codeSql = string.Join(", ", codes.Select(ToSqlLiteral));
            return await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{tableName}""
                SET ""is_active"" = false,
                    ""UpdatedAt"" = NOW()
                WHERE COALESCE(""code"", '') IN ({codeSql})
                  AND COALESCE(""is_active"", true) = true;");
        }

        private async Task<int> DeactivateChartAccountsOutsideSourceAsync(string tableName, ISet<string> sourceCodes)
        {
            if (sourceCodes.Count == 0)
                return 0;

            var codeSql = string.Join(", ", sourceCodes.Select(ToSqlLiteral));
            return await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{tableName}""
                SET ""is_active"" = false,
                    ""UpdatedAt"" = NOW()
                WHERE COALESCE(""code"", '') <> ''
                  AND ""code"" NOT IN ({codeSql})
                  AND COALESCE(""is_active"", true) = true;");
        }

        public async Task<ChartOfAccountsDbfImportResult> ImportChartOfAccountsFromDbfAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Не указан путь к файлу плана счетов.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл плана счетов не найден.", filePath);

            await EnsureCatalogStructureAsync("План счетов", GetChartOfAccountsFields);

            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");

            if (catalog == null)
                throw new InvalidOperationException("Справочник 'План счетов' не найден.");

            var importService = new ChartOfAccountsDbfImportService();
            var analysis = importService.Analyze(filePath);
            var existingCodes = await GetCatalogCodeSetAsync(catalog.TableName);
            var modules = await GetAvailableModulesAsync();
            var sourceCodes = analysis.Accounts
                .Select(account => account.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var insertedCount = 0;
            var updatedCount = 0;

            foreach (var account in analysis.Accounts)
            {
                if (existingCodes.Contains(account.Code))
                {
                    updatedCount++;
                }
                else
                {
                    insertedCount++;
                    existingCodes.Add(account.Code);
                }

                await UpsertCatalogSeedRowAsync(
                    catalog.TableName,
                    account.Code,
                    BuildChartOfAccountsCatalogValues(account, modules));
            }

            var deactivatedCount = await DeactivateChartAccountsOutsideSourceAsync(catalog.TableName, sourceCodes);
            await NormalizeChartOfAccountsMetadataAsync(catalog);

            return new ChartOfAccountsDbfImportResult
            {
                SourcePath = analysis.SourcePath,
                SourceRecordCount = analysis.SourceRecordCount,
                LoadedAccountsCount = analysis.LoadedAccountsCount,
                InsertedCount = insertedCount,
                UpdatedCount = updatedCount,
                DeactivatedCount = deactivatedCount,
                DuplicateSourceCodesCount = analysis.DuplicateSourceCodesCount,
                FieldMappings = analysis.FieldMappings,
                IgnoredSourceFields = analysis.IgnoredSourceFields
            };
        }

        public async Task<PaymentClassificationDbfImportResult> ImportPaymentClassificationsFromDbfAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Не указан путь к файлу классификации платежей.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл классификации платежей не найден.", filePath);

            await EnsureCatalogStructureAsync("Классификация платежей", GetPaymentClassificationFields);

            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Классификация платежей");

            if (catalog == null)
                throw new InvalidOperationException("Справочник 'Классификация платежей' не найден.");

            var importService = new PaymentClassificationDbfImportService();
            var analysis = importService.Analyze(filePath);
            var existingCodes = await GetCatalogCodeSetAsync(catalog.TableName);
            var sourceCodes = analysis.Items
                .Select(item => item.Code)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var insertedCount = 0;
            var updatedCount = 0;

            foreach (var item in analysis.Items)
            {
                if (existingCodes.Contains(item.Code))
                {
                    updatedCount++;
                }
                else
                {
                    insertedCount++;
                    existingCodes.Add(item.Code);
                }

                await UpsertCatalogSeedRowAsync(
                    catalog.TableName,
                    item.Code,
                    BuildPaymentClassificationCatalogValues(item));
            }

            var deactivatedCount = await DeactivatePaymentClassificationsOutsideSourceAsync(catalog.TableName, sourceCodes);

            return new PaymentClassificationDbfImportResult
            {
                SourcePath = analysis.SourcePath,
                SourceRecordCount = analysis.SourceRecordCount,
                LoadedItemsCount = analysis.LoadedItemsCount,
                InsertedCount = insertedCount,
                UpdatedCount = updatedCount,
                DeactivatedCount = deactivatedCount,
                DuplicateSourceCodesCount = analysis.DuplicateSourceCodesCount,
                FieldMappings = analysis.FieldMappings,
                IgnoredSourceFields = analysis.IgnoredSourceFields
            };
        }

        private static Dictionary<string, object?> BuildChartOfAccountsCatalogValues(
            ChartOfAccount account,
            IReadOnlyCollection<MetadataModule> modules)
        {
            var closingModuleName = ResolveClosingModuleValue(account.ClosingSubsystemCode, modules);

            return new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["code"] = account.Code,
                ["name"] = account.Name,
                ["account_type"] = account.AccountType,
                ["description"] = string.IsNullOrWhiteSpace(account.Description) ? null : account.Description,
                ["level"] = account.Level,
                ["is_active"] = account.IsActive,
                ["closing_module_code"] = string.IsNullOrWhiteSpace(closingModuleName) ? null : closingModuleName,
                ["analytic_group"] = account.AnalyticsGroupCode == 0 ? null : account.AnalyticsGroupCode.ToString(CultureInfo.InvariantCulture),
                ["print_mode"] = ChartOfAccountsSelectionMetadata.NormalizePrintModeValue(
                    account.PrintModeCode == 0 ? null : account.PrintModeCode.ToString(CultureInfo.InvariantCulture)),
                ["balance_mode"] = ChartOfAccountsSelectionMetadata.NormalizeBalanceModeValue(
                    account.BalanceModeCode == 0 ? null : account.BalanceModeCode.ToString(CultureInfo.InvariantCulture)),
                ["link_organizations"] = account.UseOrganizations,
                ["link_employees"] = account.UseEmployees,
                ["link_currencies"] = account.UseCurrencies,
                ["link_personal_accounts"] = account.UsePersonalAccounts,
                ["link_materials"] = account.UseMaterials,
                ["link_sites"] = account.UseSites,
                ["tax_code"] = account.TaxCode == 0 ? null : account.TaxCode.ToString(CultureInfo.InvariantCulture),
                ["CreatedAt"] = DateTime.UtcNow,
                ["UpdatedAt"] = DateTime.UtcNow
            };
        }

        private static Dictionary<string, object?> BuildPaymentClassificationCatalogValues(PaymentClassificationRecord item)
        {
            return new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["code"] = item.Code,
                ["name"] = item.Name,
                ["external_code"] = string.IsNullOrWhiteSpace(item.ExternalCode) ? null : item.ExternalCode,
                ["is_active"] = item.IsActive,
                ["description"] = null,
                ["CreatedAt"] = DateTime.UtcNow,
                ["UpdatedAt"] = DateTime.UtcNow
            };
        }

        private async Task<int> DeactivatePaymentClassificationsOutsideSourceAsync(string tableName, ISet<string> sourceCodes)
        {
            if (sourceCodes.Count == 0)
                return 0;

            var codeSql = string.Join(", ", sourceCodes.Select(ToSqlLiteral));
            return await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{tableName}""
                SET ""is_active"" = false,
                    ""UpdatedAt"" = NOW()
                WHERE COALESCE(""code"", '') <> ''
                  AND ""code"" NOT IN ({codeSql})
                  AND COALESCE(""is_active"", true) = true;");
        }

        private async Task<List<MetadataModule>> GetAvailableModulesAsync()
        {
            try
            {
                return await _context.MetadataModules.AsNoTracking()
                    .OrderBy(module => module.Order)
                    .ThenBy(module => module.Name)
                    .ToListAsync();
            }
            catch
            {
                return [];
            }
        }

        private async Task<HashSet<string>> GetCatalogCodeSetAsync(string tableName)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $@"
                SELECT ""code""
                FROM ""{tableName}""
                WHERE COALESCE(""code"", '') <> ''";

            await _context.Database.OpenConnectionAsync();
            try
            {
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.IsDBNull(0))
                        continue;

                    var code = reader.GetString(0)?.Trim();
                    if (!string.IsNullOrWhiteSpace(code))
                        codes.Add(code);
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return codes;
        }

        private async Task NormalizeChartOfAccountsClosingModulesAsync(MetadataObject catalog)
        {
            var modules = await GetAvailableModulesAsync();
            var updates = new List<(Guid Id, string Value)>();

            using (var selectCommand = _context.Database.GetDbConnection().CreateCommand())
            {
                selectCommand.CommandText = $@"
                    SELECT ""Id"", COALESCE(""closing_module_code"", '')
                    FROM ""{catalog.TableName}""";

                await _context.Database.OpenConnectionAsync();
                try
                {
                    using var reader = await selectCommand.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (reader.IsDBNull(0))
                            continue;

                        var id = reader.GetGuid(0);
                        var currentValue = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var normalizedValue = NormalizeClosingModuleValue(currentValue, modules);

                        if (string.Equals(currentValue?.Trim(), normalizedValue, StringComparison.Ordinal))
                            continue;

                        updates.Add((id, normalizedValue));
                    }
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

            if (updates.Count == 0)
                return;

            await _context.Database.OpenConnectionAsync();
            try
            {
                foreach (var update in updates)
                {
                    using var updateCommand = _context.Database.GetDbConnection().CreateCommand();
                    updateCommand.CommandText = $@"
                        UPDATE ""{catalog.TableName}""
                        SET ""closing_module_code"" = @value
                        WHERE ""Id"" = @id";

                    var valueParameter = updateCommand.CreateParameter();
                    valueParameter.ParameterName = "@value";
                    valueParameter.Value = update.Value;

                    var idParameter = updateCommand.CreateParameter();
                    idParameter.ParameterName = "@id";
                    idParameter.Value = update.Id;

                    updateCommand.Parameters.Add(valueParameter);
                    updateCommand.Parameters.Add(idParameter);

                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task NormalizeChartOfAccountsModeFieldsAsync(MetadataObject catalog)
        {
            var updates = new List<(Guid Id, string PrintMode, string BalanceMode)>();

            using (var selectCommand = _context.Database.GetDbConnection().CreateCommand())
            {
                selectCommand.CommandText = $@"
                    SELECT ""Id"", COALESCE(""print_mode"", ''), COALESCE(""balance_mode"", '')
                    FROM ""{catalog.TableName}""";

                await _context.Database.OpenConnectionAsync();
                try
                {
                    using var reader = await selectCommand.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (reader.IsDBNull(0))
                            continue;

                        var id = reader.GetGuid(0);
                        var currentPrintMode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var currentBalanceMode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var normalizedPrintMode =
                            ChartOfAccountsSelectionMetadata.NormalizePrintModeValue(currentPrintMode);
                        var normalizedBalanceMode =
                            ChartOfAccountsSelectionMetadata.NormalizeBalanceModeValue(currentBalanceMode);

                        if (string.Equals(currentPrintMode?.Trim(), normalizedPrintMode, StringComparison.Ordinal) &&
                            string.Equals(currentBalanceMode?.Trim(), normalizedBalanceMode, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        updates.Add((id, normalizedPrintMode, normalizedBalanceMode));
                    }
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

            if (updates.Count == 0)
                return;

            await _context.Database.OpenConnectionAsync();
            try
            {
                foreach (var update in updates)
                {
                    using var updateCommand = _context.Database.GetDbConnection().CreateCommand();
                    updateCommand.CommandText = $@"
                        UPDATE ""{catalog.TableName}""
                        SET ""print_mode"" = @printMode,
                            ""balance_mode"" = @balanceMode
                        WHERE ""Id"" = @id";

                    var printModeParameter = updateCommand.CreateParameter();
                    printModeParameter.ParameterName = "@printMode";
                    printModeParameter.Value = update.PrintMode;

                    var balanceModeParameter = updateCommand.CreateParameter();
                    balanceModeParameter.ParameterName = "@balanceMode";
                    balanceModeParameter.Value = update.BalanceMode;

                    var idParameter = updateCommand.CreateParameter();
                    idParameter.ParameterName = "@id";
                    idParameter.Value = update.Id;

                    updateCommand.Parameters.Add(printModeParameter);
                    updateCommand.Parameters.Add(balanceModeParameter);
                    updateCommand.Parameters.Add(idParameter);

                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        private static string ResolveClosingModuleValue(int foxArmCode, IReadOnlyCollection<MetadataModule> modules)
        {
            if (foxArmCode == 0)
                return string.Empty;

            return FoxClosingModuleMappings.TryGetValue(foxArmCode, out var mapping)
                ? ResolveClosingModuleValue(mapping, modules)
                : foxArmCode.ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeClosingModuleValue(string? rawValue, IReadOnlyCollection<MetadataModule> modules)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var moduleByCode = modules.FirstOrDefault(module =>
                module.Code.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (moduleByCode != null)
                return moduleByCode.Name;

            var moduleByName = modules.FirstOrDefault(module =>
                module.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (moduleByName != null)
                return moduleByName.Name;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var foxArmCode))
                return ResolveClosingModuleValue(foxArmCode, modules);

            foreach (var mapping in FoxClosingModuleMappings.Values)
            {
                if (mapping.Aliases.Any(alias => alias.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    return ResolveClosingModuleValue(mapping, modules);
            }

            var closestModule = modules.FirstOrDefault(module =>
                module.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(module.Name, StringComparison.OrdinalIgnoreCase));

            return closestModule?.Name ?? value;
        }

        private static string ResolveClosingModuleValue(
            FoxClosingModuleMapping mapping,
            IReadOnlyCollection<MetadataModule> modules)
        {
            if (!string.IsNullOrWhiteSpace(mapping.PreferredModuleCode))
            {
                var moduleByCode = modules.FirstOrDefault(module =>
                    module.Code.Equals(mapping.PreferredModuleCode, StringComparison.OrdinalIgnoreCase));
                if (moduleByCode != null)
                    return moduleByCode.Name;
            }

            foreach (var alias in mapping.Aliases)
            {
                var moduleByCode = modules.FirstOrDefault(module =>
                    module.Code.Equals(alias, StringComparison.OrdinalIgnoreCase));
                if (moduleByCode != null)
                    return moduleByCode.Name;

                var moduleByName = modules.FirstOrDefault(module =>
                    module.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));
                if (moduleByName != null)
                    return moduleByName.Name;

                var similarModule = modules.FirstOrDefault(module =>
                    module.Name.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
                    alias.Contains(module.Name, StringComparison.OrdinalIgnoreCase));
                if (similarModule != null)
                    return similarModule.Name;
            }

            return mapping.FallbackName;
        }

        private static readonly IReadOnlyDictionary<int, FoxClosingModuleMapping> FoxClosingModuleMappings =
            new Dictionary<int, FoxClosingModuleMapping>
            {
                [2] = new(ModuleMetadataService.FinanceCode, "Финансы", "Финансы"),
                [3] = new(ModuleMetadataService.FinanceCode, "Финансы", "Финансы"),
                [4] = new(null, "Сбыт", "Сбыт", "Продажи", "Реализация", "Регистратура"),
                [6] = new(ModuleMetadataService.InventoryCode, "Учет материальных ценностей", "УМЦ", "ТМЦ", "Материалы"),
                [7] = new(ModuleMetadataService.FixedAssetsCode, "Основные средства", "Основные средства", "ОС"),
                [8] = new(null, "Вспомогательное производство", "Вспомогательное производство"),
                [9] = new(ModuleMetadataService.FinanceCode, "Финансы", "Финансы"),
                [12] = new(ModuleMetadataService.InventoryCode, "Сырье", "Сырье", "Учет материальных ценностей", "Материалы"),
                [66] = new(null, "Меню", "Меню")
            };

        private sealed record FoxClosingModuleMapping(
            string? PreferredModuleCode,
            string FallbackName,
            params string[] Aliases);

        private async Task MigrateChartOfAccountsModuleFieldAsync(MetadataObject catalog)
        {
            var legacyField = catalog.Fields.FirstOrDefault(field =>
                field.DbColumnName?.Equals("closing_arm", StringComparison.OrdinalIgnoreCase) == true);
            var currentField = catalog.Fields.FirstOrDefault(field =>
                field.DbColumnName?.Equals("closing_module_code", StringComparison.OrdinalIgnoreCase) == true);

            if (legacyField != null && currentField != null && legacyField.Id != currentField.Id)
            {
                currentField.Name = "Закрывает модуль";
                currentField.FieldType = "String";
                currentField.Length = Math.Max(currentField.Length, 50);
                currentField.Order = Math.Min(currentField.Order, legacyField.Order == 0 ? currentField.Order : legacyField.Order);

                _context.MetadataFields.Remove(legacyField);
                catalog.Fields.Remove(legacyField);
            }
            else if (legacyField != null)
            {
                legacyField.DbColumnName = "closing_module_code";
                legacyField.Name = "Закрывает модуль";
                legacyField.FieldType = "String";
                legacyField.Length = Math.Max(legacyField.Length, 50);
                legacyField.Order = 7;
            }

            await _context.Database.ExecuteSqlRawAsync($@"
                ALTER TABLE ""{catalog.TableName}"" ADD COLUMN IF NOT EXISTS ""closing_module_code"" varchar(50);
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = '{catalog.TableName}'
                          AND column_name = 'closing_arm') THEN
                        UPDATE ""{catalog.TableName}""
                        SET ""closing_module_code"" = COALESCE(NULLIF(""closing_module_code"", ''), ""closing_arm"")
                        WHERE COALESCE(NULLIF(""closing_arm"", ''), '') <> '';
                    END IF;
                END $$;");
        }

        #endregion catalogs

        private async Task EnsureStandardReportTemplatesAsync(MetadataConfiguration config)
        {
            await EnsureMetadataReportAsync(
                "Счет-фактура на материалы (КР)",
                "Печатная форма счет-фактуры на материалы с реквизитами первичной организации.",
                "Справочник материалов",
                "InvoiceMaterialsKg",
                1,
                new[] { "Код", "Наименование материала", "Ед изм", "Ном номер", "Счет хранения" });
            await EnsureMetadataReportAsync(
                "Ведомость основных средств",
                "Перечень карточек основных средств предприятия.",
                "Основные средства",
                "Table",
                10,
                new[] { "Код", "Наименование", "Описание", "Активен" });
            await EnsureMetadataReportAsync(
                "Перечень материалов",
                "Справочник материалов по коду, номенклатурному номеру и счету хранения.",
                "Справочник материалов",
                "Table",
                20,
                new[] { "Код", "Наименование материала", "Ед изм", "Ном номер", "Вид материала", "Счет хранения" });
            await EnsureMetadataReportAsync(
                "Журнал прихода товаров",
                "Документы поступления товарно-материальных запасов.",
                "Приход товаров",
                "Table",
                30,
                new[] { "Номер", "Дата", "Сумма", "Примечание" });
            await EnsureMetadataReportAsync(
                "Журнал расхода товаров",
                "Документы выбытия товарно-материальных запасов.",
                "Расход товаров",
                "Table",
                31,
                new[] { "Номер", "Дата", "Сумма", "Примечание" });
            await EnsureMetadataReportAsync(
                "Журнал бухгалтерских проводок",
                "Единый журнал бухгалтерских проводок системы.",
                "Проводки",
                "Table",
                40,
                new[]
                {
                    "Дата", "Номер документа", "Тип документа", "Дебет", "Кредит",
                    "Сумма в сом", "Сумма в валюте", "Валюта", "Организация", "Сотрудник", "Примечание"
                });
            await _context.SaveChangesAsync();
        }

        private async Task EnsureInventoryDocumentStructureAsync(IEnumerable<MetadataObject> documents)
        {
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Приход товаров", "Расход товаров"
            };

            foreach (var document in documents.Where(document => targetNames.Contains(document.Name)))
            {
                var duplicateFields = document.Fields
                    .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                    .GroupBy(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(group => group.OrderBy(field => field.Order).Skip(1))
                    .ToList();
                if (duplicateFields.Count > 0)
                {
                    _context.MetadataFields.RemoveRange(duplicateFields);
                    foreach (var duplicate in duplicateFields)
                        document.Fields.Remove(duplicate);
                }

                var columns = document.Fields.Select(field => field.DbColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nextOrder = document.Fields.Count == 0 ? 1 : document.Fields.Max(field => field.Order) + 1;
                foreach (var field in GetInventoryDocumentFields(document.Id, nextOrder))
                {
                    if (columns.Contains(field.DbColumnName))
                        continue;

                    field.Id = Guid.NewGuid();
                    field.MetadataObjectId = document.Id;
                    await _context.MetadataFields.AddAsync(field);
                    await AddColumnToTableAsync(document.TableName, field);
                    document.Fields.Add(field);
                    columns.Add(field.DbColumnName);
                }

                document.UsePostings = true;
                if (!await _context.MetadataPostingRules.AnyAsync(rule => rule.MetadataObjectId == document.Id))
                {
                    await _context.MetadataPostingRules.AddAsync(new MetadataPostingRule
                    {
                        Id = Guid.NewGuid(),
                        MetadataObjectId = document.Id,
                        Name = $"Проводка документа {document.Name}",
                        DebitAccountExpression = "{debit_account}",
                        CreditAccountExpression = "{credit_account}",
                        AmountExpression = "{amount}",
                        Order = 1
                    });
                }
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsurePostingDocumentStructureAsync(IEnumerable<MetadataObject> documents)
        {
            var document = documents.FirstOrDefault(item =>
                item.Name.Equals("Проводки", StringComparison.OrdinalIgnoreCase));
            if (document == null)
                return;

            NormalizeCashDeskReferenceFields(document);

            var existingColumns = document.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .Select(field => field.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in GetPostingFields(document.Id))
            {
                if (existingColumns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = document.Id;
                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(document.TableName, field);
                document.Fields.Add(field);
                existingColumns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsureUnifiedCashOrderDocumentAsync(MetadataConfiguration config)
        {
            var legacyDocuments = await _context.MetadataObjects
                .Include(item => item.Fields)
                .Where(item => item.ObjectType == "Document" &&
                    (item.Name == CashOrderReceiptDocumentType || item.Name == CashOrderPaymentDocumentType))
                .ToListAsync();
            var legacyIds = legacyDocuments.Select(item => item.Id).ToList();

            var document = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" &&
                    (item.Name == CashOrderDocumentName || item.TableName == "doc_cash_orders"));

            if (document == null)
            {
                document = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = CashOrderDocumentName,
                    TableName = "doc_cash_orders",
                    ObjectType = "Document",
                    Description = "Единый журнал приходных и расходных кассовых ордеров",
                    Icon = "💵",
                    Order = 4,
                    IsSystem = true,
                    UsePostings = true,
                    MetadataConfigId = config.Id
                };
                document.Fields = GetCashOrderFields(document.Id);
                await _context.MetadataObjects.AddAsync(document);
                await _context.SaveChangesAsync();
            }
            else
            {
                document.Name = CashOrderDocumentName;
                document.TableName = "doc_cash_orders";
                document.Description = "Единый журнал приходных и расходных кассовых ордеров";
                document.Icon = "💵";
                document.Order = document.Order == 0 ? 4 : document.Order;
                document.IsSystem = true;
                document.UsePostings = true;
                if (document.MetadataConfigId == null)
                    document.MetadataConfigId = config.Id;
                await _context.SaveChangesAsync();
            }

            await EnsureCashOrderMetadataFieldsAsync(document);
            await CreateTableForCatalogAsync(document);
            await MigrateLegacyCashOrderRowsAsync();
            await ReassignCashOrderRelatedMetadataAsync(document, legacyIds);
            await RemoveLegacyCashOrderMetadataAsync(legacyDocuments);
            await DropLegacyCashOrderTablesAsync();
            await EnsureIndependentDocumentNumberConfigurationAsync(CashOrderDocumentName);
        }

        private async Task EnsureCashOrderMetadataFieldsAsync(MetadataObject document)
        {
            await RemoveDuplicateMetadataFieldsAsync(document);

            var existingByColumn = document.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .GroupBy(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(field => field.Order).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var desired in GetCashOrderFields(document.Id))
            {
                if (existingByColumn.TryGetValue(desired.DbColumnName, out var existing))
                {
                    existing.Name = desired.Name;
                    existing.FieldType = desired.FieldType;
                    existing.ReferenceCatalog = desired.ReferenceCatalog;
                    existing.DisplayPattern = desired.DisplayPattern;
                    existing.DisplayFields = desired.DisplayFields;
                    existing.Order = desired.Order;
                    existing.IsRequired = desired.IsRequired;
                    existing.IsUnique = desired.IsUnique;
                    existing.Length = desired.Length;
                    existing.Precision = desired.Precision;
                    existing.Scale = desired.Scale;
                    continue;
                }

                desired.Id = Guid.NewGuid();
                desired.MetadataObjectId = document.Id;
                await _context.MetadataFields.AddAsync(desired);
                await AddColumnToTableAsync(document.TableName, desired);
                document.Fields.Add(desired);
                existingByColumn[desired.DbColumnName] = desired;
            }

            await _context.SaveChangesAsync();
        }

        private async Task MigrateLegacyCashOrderRowsAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""doc_number"" varchar(20) NOT NULL DEFAULT '';
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""doc_date"" timestamp NOT NULL DEFAULT NOW();
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""order_kind"" varchar(20) NOT NULL DEFAULT 'Payment';
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""organization_id"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""cash_desk_id"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""amount"" decimal(18,2) NOT NULL DEFAULT 0;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""basis"" varchar(500);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""correspondent_account"" varchar(50);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""cash_flow_item"" varchar(100);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""cash_account"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""description"" varchar(500);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""is_posted"" boolean DEFAULT false;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""currency_id"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""employee_id"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""material_id"" text;
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""debit_account"" varchar(50);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""credit_account"" varchar(50);
                    ALTER TABLE ""doc_cash_orders"" ADD COLUMN IF NOT EXISTS ""amount_currency"" decimal(18,2) DEFAULT 0;
                    IF to_regclass('public.doc_cash_receipt') IS NOT NULL THEN
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""organization_id"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""cash_desk_id"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""basis"" varchar(500);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""correspondent_account"" varchar(50);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""cash_flow_item"" varchar(100);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""cash_account"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""description"" varchar(500);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""is_posted"" boolean DEFAULT false;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""currency_id"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""employee_id"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""material_id"" text;
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""debit_account"" varchar(50);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""credit_account"" varchar(50);
                        ALTER TABLE ""doc_cash_receipt"" ADD COLUMN IF NOT EXISTS ""amount_currency"" decimal(18,2) DEFAULT 0;

                        INSERT INTO ""doc_cash_orders"" (""Id"", ""doc_number"", ""doc_date"", ""order_kind"", ""organization_id"", ""cash_desk_id"", ""amount"", ""basis"", ""correspondent_account"", ""cash_flow_item"", ""cash_account"", ""description"", ""is_posted"", ""currency_id"", ""employee_id"", ""material_id"", ""debit_account"", ""credit_account"", ""amount_currency"", ""CreatedAt"", ""UpdatedAt"")
                        SELECT source.""Id"", COALESCE(source.""doc_number"", ''), COALESCE(source.""doc_date"", NOW()), 'Receipt', source.""organization_id"", source.""cash_desk_id"", COALESCE(source.""amount"", 0), source.""basis"", source.""correspondent_account"", source.""cash_flow_item"", source.""cash_account"", source.""description"", COALESCE(source.""is_posted"", false), source.""currency_id"", source.""employee_id"", source.""material_id"", source.""debit_account"", source.""credit_account"", COALESCE(source.""amount_currency"", 0), COALESCE(source.""CreatedAt"", NOW()), COALESCE(source.""UpdatedAt"", NOW())
                        FROM ""doc_cash_receipt"" source
                        WHERE NOT EXISTS (SELECT 1 FROM ""doc_cash_orders"" target WHERE target.""Id"" = source.""Id"");
                    END IF;

                    IF to_regclass('public.doc_cash_payment') IS NOT NULL THEN
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""organization_id"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""cash_desk_id"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""basis"" varchar(500);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""correspondent_account"" varchar(50);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""cash_flow_item"" varchar(100);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""cash_account"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""description"" varchar(500);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""is_posted"" boolean DEFAULT false;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""currency_id"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""employee_id"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""material_id"" text;
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""debit_account"" varchar(50);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""credit_account"" varchar(50);
                        ALTER TABLE ""doc_cash_payment"" ADD COLUMN IF NOT EXISTS ""amount_currency"" decimal(18,2) DEFAULT 0;

                        INSERT INTO ""doc_cash_orders"" (""Id"", ""doc_number"", ""doc_date"", ""order_kind"", ""organization_id"", ""cash_desk_id"", ""amount"", ""basis"", ""correspondent_account"", ""cash_flow_item"", ""cash_account"", ""description"", ""is_posted"", ""currency_id"", ""employee_id"", ""material_id"", ""debit_account"", ""credit_account"", ""amount_currency"", ""CreatedAt"", ""UpdatedAt"")
                        SELECT source.""Id"", COALESCE(source.""doc_number"", ''), COALESCE(source.""doc_date"", NOW()), 'Payment', source.""organization_id"", source.""cash_desk_id"", COALESCE(source.""amount"", 0), source.""basis"", source.""correspondent_account"", source.""cash_flow_item"", source.""cash_account"", source.""description"", COALESCE(source.""is_posted"", false), source.""currency_id"", source.""employee_id"", source.""material_id"", source.""debit_account"", source.""credit_account"", COALESCE(source.""amount_currency"", 0), COALESCE(source.""CreatedAt"", NOW()), COALESCE(source.""UpdatedAt"", NOW())
                        FROM ""doc_cash_payment"" source
                        WHERE NOT EXISTS (SELECT 1 FROM ""doc_cash_orders"" target WHERE target.""Id"" = source.""Id"");
                    END IF;
                END $$;");
        }

        private async Task ReassignCashOrderRelatedMetadataAsync(MetadataObject document, IReadOnlyCollection<Guid> legacyIds)
        {
            if (legacyIds.Count > 0)
            {
                var reports = await _context.Reports
                    .Where(report => report.DataSourceId.HasValue && legacyIds.Contains(report.DataSourceId.Value))
                    .ToListAsync();
                foreach (var report in reports)
                {
                    report.DataSourceId = document.Id;
                    report.DataSourceType = "Document";
                    report.UpdatedAt = DateTime.UtcNow;
                }

                var legacyModuleItems = await _context.MetadataModuleItems
                    .Where(item => item.ObjectType == "Document" && legacyIds.Contains(item.ObjectId))
                    .ToListAsync();
                _context.MetadataModuleItems.RemoveRange(legacyModuleItems);
            }

            var cashReports = await _context.Reports
                .Where(report => report.Code.StartsWith("cash.receipt.") || report.Code.StartsWith("cash.payment."))
                .ToListAsync();
            foreach (var report in cashReports)
            {
                report.DataSourceId = document.Id;
                report.DataSourceType = "Document";
                report.IsPrintForm = true;
                report.UpdatedAt = DateTime.UtcNow;
            }

            var financeModule = await _context.MetadataModules.FirstOrDefaultAsync(module => module.Code == ModuleMetadataService.FinanceCode);
            if (financeModule != null && !await _context.MetadataModuleItems.AnyAsync(item => item.ObjectType == "Document" && item.ObjectId == document.Id))
            {
                await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
                {
                    Id = Guid.NewGuid(),
                    ModuleId = financeModule.Id,
                    ObjectId = document.Id,
                    ObjectType = "Document",
                    Order = document.Order
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task RemoveLegacyCashOrderMetadataAsync(IReadOnlyCollection<MetadataObject> legacyDocuments)
        {
            if (legacyDocuments.Count == 0)
                return;

            var legacyIds = legacyDocuments.Select(item => item.Id).ToList();
            var postingRules = await _context.MetadataPostingRules.Where(rule => legacyIds.Contains(rule.MetadataObjectId)).ToListAsync();
            var calculations = await _context.MetadataCalculations.Where(calculation => legacyIds.Contains(calculation.MetadataObjectId)).ToListAsync();
            var fields = await _context.MetadataFields.Where(field => legacyIds.Contains(field.MetadataObjectId)).ToListAsync();
            _context.MetadataPostingRules.RemoveRange(postingRules);
            _context.MetadataCalculations.RemoveRange(calculations);
            _context.MetadataFields.RemoveRange(fields);
            _context.MetadataObjects.RemoveRange(legacyDocuments);
            await _context.SaveChangesAsync();
        }

        private async Task DropLegacyCashOrderTablesAsync()
        {
            await CreateDocumentNumberingTableAsync();
            await _context.Database.ExecuteSqlRawAsync(@"
                DROP TABLE IF EXISTS ""doc_cash_receipt"" CASCADE;
                DROP TABLE IF EXISTS ""doc_cash_payment"" CASCADE;
                DELETE FROM doc_numbering
                WHERE document_type = 'Расходный/Приходный КО';");
        }
        private async Task EnsureCashOrderDocumentStructureAsync(IEnumerable<MetadataObject> documents)
        {
            var document = documents.FirstOrDefault(document =>
                document.Name.Equals(CashOrderDocumentName, StringComparison.OrdinalIgnoreCase) ||
                document.TableName.Equals("doc_cash_orders", StringComparison.OrdinalIgnoreCase));
            if (document == null)
                return;

            document.Name = CashOrderDocumentName;
            document.TableName = "doc_cash_orders";
            document.UsePostings = true;
            NormalizeCashDeskReferenceFields(document);
            await EnsureCashOrderMetadataFieldsAsync(document);
        }

        private static void NormalizeCashDeskReferenceFields(MetadataObject document)
        {
            foreach (var field in document.Fields.Where(field =>
                         field.DbColumnName?.Equals("cash_desk_id", StringComparison.OrdinalIgnoreCase) == true ||
                         field.ReferenceCatalog?.Equals("Кассы", StringComparison.OrdinalIgnoreCase) == true))
            {
                field.Name = "Касса";
                field.FieldType = "Reference";
                field.ReferenceCatalog = "Кассы";
                field.DisplayPattern = "{Счет} - {Наименование кассы}";
                field.DisplayFields = "Счет,Наименование кассы";
                field.IsRequired = false;
            }
        }

        private static IEnumerable<MetadataField> GetCashOrderRequiredFields(Guid metadataObjectId, int startOrder)
        {
            yield return new MetadataField
            {
                Name = "Касса",
                DbColumnName = "cash_desk_id",
                FieldType = "Reference",
                ReferenceCatalog = "Кассы",
                DisplayPattern = "{Счет} - {Наименование кассы}",
                DisplayFields = "Счет,Наименование кассы",
                Order = startOrder++,
                MetadataObjectId = metadataObjectId
            };
            yield return new MetadataField
            {
                Name = "Дебет",
                DbColumnName = "debit_account",
                FieldType = "String",
                Length = 50,
                Order = startOrder++,
                MetadataObjectId = metadataObjectId
            };
            yield return new MetadataField
            {
                Name = "Кредит",
                DbColumnName = "credit_account",
                FieldType = "String",
                Length = 50,
                Order = startOrder++,
                MetadataObjectId = metadataObjectId
            };
            yield return new MetadataField
            {
                Name = "Сумма в валюте",
                DbColumnName = "amount_currency",
                FieldType = "Decimal",
                Precision = 18,
                Scale = 2,
                Order = startOrder,
                MetadataObjectId = metadataObjectId
            };
        }

        private async Task EnsureAssetsCatalogStructureAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(metadata => metadata.Fields)
                .FirstOrDefaultAsync(metadata => metadata.ObjectType == "Catalog" && metadata.Name == "Основные средства");
            if (catalog == null)
                return;

            var columns = catalog.Fields.Select(field => field.DbColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var field in GetFixedAssetFields(catalog.Id))
            {
                if (columns.Contains(field.DbColumnName))
                    continue;

                field.Id = Guid.NewGuid();
                field.MetadataObjectId = catalog.Id;
                await _context.MetadataFields.AddAsync(field);
                await AddColumnToTableAsync(catalog.TableName, field);
                columns.Add(field.DbColumnName);
            }

            await _context.SaveChangesAsync();
        }

        private async Task EnsureMetadataReportAsync(
            string name,
            string description,
            string sourceName,
            string reportType,
            int order,
            IEnumerable<string> fieldNames)
        {
            if (await _context.Reports.AnyAsync(report => report.Name == name))
                return;

            var source = await _context.MetadataObjects
                .Include(metadata => metadata.Fields)
                .FirstOrDefaultAsync(metadata => metadata.Name == sourceName);
            if (source == null)
                return;

            var report = new Report
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                DataSourceType = source.ObjectType,
                DataSourceId = source.Id,
                ReportType = reportType,
                Icon = "📄",
                Order = order,
                TitleText = name,
                PageOrientation = source.Name == "Проводки" ? "Landscape" : "Portrait",
                FontName = "Arial",
                FontSize = 9,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var fieldOrder = 1;
            foreach (var fieldName in fieldNames)
            {
                var field = source.Fields.FirstOrDefault(metadataField =>
                    metadataField.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                    metadataField.DbColumnName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                    continue;

                report.Fields.Add(new ReportField
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    FieldName = field.DbColumnName,
                    DisplayName = field.Name,
                    Order = fieldOrder++,
                    Width = field.FieldType == "String" ? 150 : 100,
                    IsVisible = true,
                    Alignment = field.FieldType == "Decimal" ? "Right" : "Left",
                    AggregateType = field.FieldType == "Decimal" ? "Sum" : string.Empty
                });
            }

            if (report.Fields.Count > 0)
                await _context.Reports.AddAsync(report);
        }


    }
}





