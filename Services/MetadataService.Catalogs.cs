using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
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
                "Приходный кассовый ордер",
                "Расходный кассовый ордер",
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

            await RemoveDuplicateMetadataFieldsAsync(catalog);

            var existingByColumn = catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                .ToDictionary(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase);

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

            await _context.SaveChangesAsync();
            await BackfillFoxChartOfAccountsCompatibilityFieldsAsync(catalog);
        }

        private async Task BackfillFoxChartOfAccountsCompatibilityFieldsAsync(MetadataObject catalog)
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{catalog.TableName}""
                    SET
                        ""prsch"" = COALESCE(
                            ""prsch"",
                            CASE
                                WHEN ""account_type"" = 'Active' THEN 1
                                WHEN ""account_type"" = 'Passive' THEN 2
                                WHEN ""account_type"" = 'ActivePassive' THEN 3
                                ELSE NULL
                            END),
                        ""sv_o"" = COALESCE(""sv_o"", ""link_organizations"", false),
                        ""pr_sch"" = COALESCE(""pr_sch"", 0),
                        ""pr_sc7"" = COALESCE(""pr_sc7"", 0),
                        ""kodf_rb"" = COALESCE(""kodf_rb"", 0)
                    WHERE
                        ""prsch"" IS NULL OR
                        ""sv_o"" IS NULL OR
                        ""pr_sch"" IS NULL OR
                        ""pr_sc7"" IS NULL OR
                        ""kodf_rb"" IS NULL;");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Ошибка заполнения Fox-полей плана счетов: {ex.Message}");
            }
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

        private async Task EnsureCashOrderDocumentStructureAsync(IEnumerable<MetadataObject> documents)
        {
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Приходный кассовый ордер",
                "Расходный кассовый ордер"
            };

            foreach (var document in documents.Where(document => targetNames.Contains(document.Name)))
            {
                NormalizeCashDeskReferenceFields(document);

                var columns = document.Fields
                    .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                    .Select(field => field.DbColumnName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var nextOrder = document.Fields.Count == 0 ? 1 : document.Fields.Max(field => field.Order) + 1;
                foreach (var field in GetCashOrderRequiredFields(document.Id, nextOrder))
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
            }

            await _context.SaveChangesAsync();
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
