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

                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Код", DbColumnName = "code", FieldType = "String", Length = 20, IsRequired = true, IsUnique = true, Order = 1 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Наименование", DbColumnName = "name", FieldType = "String", Length = 200, IsRequired = true, Order = 2 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Тип счета", DbColumnName = "account_type", FieldType = "String", Length = 20, Order = 3 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Описание", DbColumnName = "description", FieldType = "String", Length = 500, Order = 4 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Уровень", DbColumnName = "level", FieldType = "Int", Order = 5 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", Order = 6 });

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
                    Fields = GetStandardCatalogFields(Guid.NewGuid())
                };
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
        // Контрагенты
        private async Task CreateContractorsCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Контрагенты",
                    TableName = "catalog_contractors",
                    ObjectType = "Catalog",
                    Description = "Контрагенты (клиенты, поставщики)",
                    Icon = "🤝",
                    Order = 5,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = GetContractorFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await CreateTableForCatalogAsync(catalog);
                System.Diagnostics.Debug.WriteLine("Справочник 'Контрагенты' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Контрагенты': {ex.Message}");
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
                // НЕ вызываем AddCurrencyDataToTable - это для валют!
                System.Diagnostics.Debug.WriteLine("Справочник 'Организации' создан");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочника 'Организации': {ex.Message}");
            }
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
    }
}

