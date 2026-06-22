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

