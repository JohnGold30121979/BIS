using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIS.ERP.Services;
using System.Linq.Expressions;

namespace BIS.ERP.Services
{
    public class MetadataService
    {
        private readonly AppDbContext _context;

        public MetadataService(AppDbContext context)
        {
            _context = context;
        }

        // Проверка инициализации метаданных
        public async Task<bool> IsMetadataInitializedAsync(Guid infoBaseId)
        {
            var config = await _context.Set<MetadataConfiguration>()
                .FirstOrDefaultAsync(c => c.InfoBaseId == infoBaseId);
            return config != null && config.IsInitialized;
        }

        // Инициализация базовых метаданных (как в 1С)
        public async Task InitializeDefaultMetadataAsync(Guid infoBaseId)
        {
            try
            {
                var config = await _context.Set<MetadataConfiguration>()
                .FirstOrDefaultAsync(c => c.InfoBaseId == infoBaseId);

                if (config == null)
                {
                    config = new MetadataConfiguration
                    {
                        Id = Guid.NewGuid(),
                        InfoBaseId = infoBaseId,
                        CreatedAt = DateTime.UtcNow,
                        IsInitialized = true
                    };
                    await _context.Set<MetadataConfiguration>().AddAsync(config);
                    await _context.SaveChangesAsync();
                }
                else if (config.IsInitialized)
                {
                    return;
                }

                // Создаем системные справочники
                var catalogs = new List<MetadataObject>();


                // Справочник "Организации"
                var organizationsCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Организации",
                    TableName = "catalog_organizations",
                    ObjectType = "Catalog",
                    Description = "Справочник организаций предприятия",
                    Icon = "🏢",
                    Order = 1,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                organizationsCatalog.Fields = GetOrganizationFields(organizationsCatalog.Id);
                catalogs.Add(organizationsCatalog);


                // Справочник "Банки"
                var banksCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Банки",
                    TableName = "catalog_banks",
                    ObjectType = "Catalog",
                    Description = "Справочник банков",
                    Icon = "🏦",
                    Order = 2,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                banksCatalog.Fields = GetBankFields(banksCatalog.Id);
                catalogs.Add(banksCatalog);

                // Справочник Список сотрудников предприятия
                var employeesCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Сотрудники (Списочный состав)",
                    TableName = "catalog_employees",
                    ObjectType = "Catalog",
                    Description = "Список сотрудников предприятия",
                    Icon = "👥",
                    Order = 3,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                employeesCatalog.Fields = GetEmployeeCatalogFields(employeesCatalog.Id);
                catalogs.Add(employeesCatalog);

                // Справочник Основные средства предприятия
                var assetsCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Основные средства",
                    TableName = "catalog_assets",
                    ObjectType = "Catalog",
                    Description = "Основные средства предприятия",
                    Icon = "⚙️",
                    Order = 4,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                assetsCatalog.Fields = GetStandardCatalogFields(assetsCatalog.Id);
                catalogs.Add(assetsCatalog);

                // Справочник Структура подразделений
                var departmentsCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Подразделения",
                    TableName = "catalog_departments",
                    ObjectType = "Catalog",
                    Description = "Структура подразделений",
                    Icon = "🏢",
                    Order = 5,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                departmentsCatalog.Fields = GetStandardCatalogFields(departmentsCatalog.Id);
                catalogs.Add(departmentsCatalog);

                // Справочник Контрагенты (клиенты, поставщики)
                var contractorsCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Контрагенты",
                    TableName = "catalog_contractors",
                    ObjectType = "Catalog",
                    Description = "Контрагенты (клиенты, поставщики)",
                    Icon = "🤝",
                    Order = 5,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                contractorsCatalog.Fields = GetContractorFields(contractorsCatalog.Id);
                catalogs.Add(contractorsCatalog);

                // Справочник "Участки"
                var sitesCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Участки",
                    TableName = "catalog_sites",
                    ObjectType = "Catalog",
                    Description = "Справочник участков предприятия",
                    Icon = "🏭",
                    Order = 6,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                sitesCatalog.Fields = GetSiteFields(sitesCatalog.Id);
                catalogs.Add(sitesCatalog);

                // Справочник "Материально-ответственные лица (МОЛ)"
                var responsiblePersonsCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "МОЛ",
                    TableName = "catalog_responsible_persons",
                    ObjectType = "Catalog",
                    Description = "Материально-ответственные лица",
                    Icon = "👥",
                    Order = 7,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                responsiblePersonsCatalog.Fields = GetResponsiblePersonFields(responsiblePersonsCatalog.Id);
                catalogs.Add(responsiblePersonsCatalog);

                // Справочник "Валюты"
                var currenciesCatalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Валюты",
                    TableName = "catalog_currencies",
                    ObjectType = "Catalog",
                    Description = "Справочник валют",
                    Icon = "💵",
                    Order = 7,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                currenciesCatalog.Fields = GetCurrencyFields(currenciesCatalog.Id);
                catalogs.Add(currenciesCatalog);       


                await _context.Set<MetadataObject>().AddRangeAsync(catalogs);

                // Создаем системные документы
                var documents = new List<MetadataObject>();

                var incomingDoc = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Приход товаров",
                    TableName = "doc_incoming",
                    ObjectType = "Document",
                    Description = "Приход товаров на склад",
                    Icon = "📥",
                    Order = 1,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                incomingDoc.Fields = GetStandardDocumentFields(incomingDoc.Id);
                documents.Add(incomingDoc);

                var outgoingDoc = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Расход товаров",
                    TableName = "doc_outgoing",
                    ObjectType = "Document",
                    Description = "Расход товаров со склада",
                    Icon = "📤",
                    Order = 2,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                outgoingDoc.Fields = GetStandardDocumentFields(outgoingDoc.Id);
                documents.Add(outgoingDoc);

                await _context.Set<MetadataObject>().AddRangeAsync(documents);

                config.IsInitialized = true;
                await _context.SaveChangesAsync();

                await CreateTablesFromMetadataAsync();
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания справочников': {ex.Message}");
            }
        }


        /// Поля для справочника "Наименования категорий"         
        private List<MetadataField> GetMaterialFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование материала",
            DbColumnName = "name",
            FieldType = "String",
            Length = 500,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Ед изм",
            DbColumnName = "unit",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Ном номер",
            DbColumnName = "article",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Вид материала",
            DbColumnName = "material_type_id",
            FieldType = "Reference",              // ← "Reference" с большой буквы
            ReferenceCatalog = "Виды материалов",
            DisplayPattern = "{Наименование вида}",  // ← добавить
            DisplayFields = "Наименование вида",     // ← добавить
            IsRequired = false,
            IsUnique = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Вместимость",
            DbColumnName = "capacity",
            FieldType = "Decimal",                // ← "Decimal"
            Precision = 18,
            Scale = 6,
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Счет хранения",
            DbColumnName = "storage_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",                   // ← "Bool"
            IsRequired = true,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Примечание",
            DbColumnName = "description",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        }
    };
        }

        private List<MetadataField> GetMaterialCategoryFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
        {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 20,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование категории",
            DbColumnName = "name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            IsUnique = false,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Примечание",
            DbColumnName = "description",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            IsUnique = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            Length = 0,
            IsRequired = false,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        }
        };
        }
        private List<MetadataField> GetSiteFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
        {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код участка",
            DbColumnName = "site_code",
            FieldType = "String",
            Length = 20,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование участка",
            DbColumnName = "site_name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Описание",
            DbColumnName = "description",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        }
        };
        }
       
        private List<MetadataField> GetResponsiblePersonFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
        {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Табельный номер",
            DbColumnName = "personnel_number",
            FieldType = "Reference",           // ← меняем на Reference
            ReferenceCatalog = "Сотрудники (Списочный состав)", // ← ссылка на сотруднико
            DisplayPattern = "{Табельный номер} - {ФИО}",  // ← шаблон
            DisplayFields = "Табельный номер,ФИО",         // ← поля для подстановки
            Length = 0,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "ФИО",
            DbColumnName = "full_name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
           Id = Guid.NewGuid(),
           Name = "Участок",
           DbColumnName = "site_id",
           FieldType = "Reference",           // ← ДОЛЖНО БЫТЬ "Reference", а не "String"!
           ReferenceCatalog = "Участки",      // ← Имя справочника
           Length = 0,
           IsRequired = false,
           IsUnique = false,
           Order = 3,
           MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Должность",
            DbColumnName = "position",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Телефон",
            DbColumnName = "phone",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Примечание",
            DbColumnName = "note",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        }
        };
        }

        private List<MetadataField> GetStandardCatalogFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
            {
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Код",
                    DbColumnName = "code",
                    FieldType = "String",
                    Length = 50,
                    IsRequired = true,
                    IsUnique = true,
                    Order = 1,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Наименование",
                    DbColumnName = "name",
                    FieldType = "String",
                    Length = 200,
                    IsRequired = true,
                    Order = 2,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Примечание",
                    DbColumnName = "description",
                    FieldType = "String",
                    Length = 500,
                    IsRequired = false,
                    Order = 3,
                    MetadataObjectId = metadataObjectId
                }
            };
        }
       
        /// Поля для справочника "Сотрудники (Списочный состав)" - расширенная версия
       
        private List<MetadataField> GetEmployeeCatalogFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
        {
        // Стандартные поля (обязательные)
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование",
            DbColumnName = "name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Примечание",
            DbColumnName = "description",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        
        // Расширенные поля для сотрудников
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Табельный номер",
            DbColumnName = "personnel_number",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            IsUnique = true,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "ФИО",
            DbColumnName = "full_name",
            FieldType = "String",
            Length = 200,
            IsRequired = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Должность",
            DbColumnName = "position",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Подразделение",
            DbColumnName = "department",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата приема",
            DbColumnName = "hire_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата увольнения",
            DbColumnName = "termination_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Статус",
            DbColumnName = "status",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            Order = 10,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Телефон",
            DbColumnName = "phone",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 11,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Email",
            DbColumnName = "email",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 12,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "ИНН",
            DbColumnName = "tax_id",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 13,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = false,
            Order = 14,
            MetadataObjectId = metadataObjectId
        }
    };
        }

        // Поля для справочника "Организации"
        private List<MetadataField> GetOrganizationFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 20,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование",
            DbColumnName = "name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Полное наименование",
            DbColumnName = "full_name",
            FieldType = "String",
            Length = 500,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "ИНН",
            DbColumnName = "inn",
            FieldType = "String",
            Length = 50,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "КПП",
            DbColumnName = "kpp",
            FieldType = "String",
            Length = 50,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Юридический адрес",
            DbColumnName = "legal_address",
            FieldType = "String",
            Length = 500,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Телефон",
            DbColumnName = "phone",
            FieldType = "String",
            Length = 50,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Email",
            DbColumnName = "email",
            FieldType = "String",
            Length = 100,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активна",
            DbColumnName = "is_active",
            FieldType = "Bool",
            Order = 9,
            MetadataObjectId = metadataObjectId
        }
    };
        }

        // Поля для справочника "Валюты"
        private List<MetadataField> GetCurrencyFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 3,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование",
            DbColumnName = "name",
            FieldType = "String",
            Length = 100,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Символ",
            DbColumnName = "symbol",
            FieldType = "String",
            Length = 5,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Курс",
            DbColumnName = "rate",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 4,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Базовая",
            DbColumnName = "is_base",
            FieldType = "Bool",
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активна",
            DbColumnName = "is_active",
            FieldType = "Bool",
            Order = 6,
            MetadataObjectId = metadataObjectId
        }
    };
        }

        // Поля для справочника "Банки"
        private List<MetadataField> GetBankFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 20,
            IsRequired = true,
            IsUnique = true,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наименование",
            DbColumnName = "name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "БИК",
            DbColumnName = "bic",
            FieldType = "String",
            Length = 20,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Корр. счет",
            DbColumnName = "corr_account",
            FieldType = "String",
            Length = 50,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Адрес",
            DbColumnName = "address",
            FieldType = "String",
            Length = 500,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Телефон",
            DbColumnName = "phone",
            FieldType = "String",
            Length = 50,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            Order = 7,
            MetadataObjectId = metadataObjectId
        }
    };
        }
        private List<MetadataField> GetContractorFields(Guid metadataObjectId)
        {
            var fields = GetStandardCatalogFields(metadataObjectId);
            fields.AddRange(new[]
            {
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "ИНН",
                    DbColumnName = "inn",
                    FieldType = "String",
                    Length = 12,
                    Order = 4,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "КПП",
                    DbColumnName = "kpp",
                    FieldType = "String",
                    Length = 9,
                    Order = 5,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Юридический адрес",
                    DbColumnName = "legal_address",
                    FieldType = "String",
                    Length = 300,
                    Order = 6,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Телефон",
                    DbColumnName = "phone",
                    FieldType = "String",
                    Length = 20,
                    Order = 7,
                    MetadataObjectId = metadataObjectId
                }
            });
            return fields;
        }

        private List<MetadataField> GetStandardDocumentFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
            {
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Номер",
                    DbColumnName = "number",
                    FieldType = "String",
                    Length = 20,
                    IsRequired = true,
                    Order = 1,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Дата",
                    DbColumnName = "date",
                    FieldType = "DateTime",
                    IsRequired = true,
                    Order = 2,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Сумма",
                    DbColumnName = "amount",
                    FieldType = "Decimal",
                    Precision = 18,
                    Scale = 2,
                    Order = 3,
                    MetadataObjectId = metadataObjectId
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Примечание",
                    DbColumnName = "description",
                    FieldType = "String",
                    Length = 500,
                    Order = 4,
                    MetadataObjectId = metadataObjectId
                }
            };
        }

        // Получение данных справочника
        public async Task<List<Dictionary<string, object>>> GetCatalogDataAsyncOld(Guid catalogId)
        {
            var catalog = await _context.MetadataObjects
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(m => m.Id == catalogId);

            if (catalog == null) return new List<Dictionary<string, object>>();

            var result = new List<Dictionary<string, object>>();
            var sql = $"SELECT * FROM \"{catalog.TableName}\" ORDER BY \"CreatedAt\"";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await _context.Database.OpenConnectionAsync();

            using var reader = await command.ExecuteReaderAsync();

            var fieldMapping = catalog.Fields.ToDictionary(f => f.DbColumnName, f => f.Name);
            fieldMapping["Id"] = "Id";
            fieldMapping["CreatedAt"] = "CreatedAt";
            fieldMapping["UpdatedAt"] = "UpdatedAt";

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var dbName = reader.GetName(i);
                    var displayName = fieldMapping.ContainsKey(dbName) ? fieldMapping[dbName] : dbName;
                    row[displayName] = reader.GetValue(i);
                }
                result.Add(row);
            }

            await _context.Database.CloseConnectionAsync();
            return result;
        }

        public async Task<List<Dictionary<string, object>>> GetCatalogDataAsync(Guid catalogId)
        {
            var catalog = await _context.MetadataObjects
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(m => m.Id == catalogId);

            if (catalog == null) return new List<Dictionary<string, object>>();

            // 1. Получаем Reference поля
            var referenceFields = catalog.Fields
                .Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog))
                .ToList();

            // 2. Загружаем данные из основной таблицы
            var sql = $"SELECT * FROM \"{catalog.TableName}\" ORDER BY \"CreatedAt\"";
            var result = new List<Dictionary<string, object>>();

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await _context.Database.OpenConnectionAsync();

            using var reader = await command.ExecuteReaderAsync();

            // 3. Для каждого Reference поля загружаем связанные данные один раз
            var referenceDataCache = new Dictionary<string, Dictionary<Guid, string>>();

            foreach (var refField in referenceFields)
            {
                referenceDataCache[refField.DbColumnName] = await LoadReferenceDictionaryAsync(refField.ReferenceCatalog);
            }

            // 4. Читаем данные и подменяем GUID на наименование
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var dbName = reader.GetName(i);
                    var value = reader.GetValue(i);

                    // Проверяем, является ли это поле Reference
                    var refField = referenceFields.FirstOrDefault(f => f.DbColumnName == dbName);

                    if (refField != null && value != DBNull.Value && value is Guid guidValue)
                    {
                        // Подменяем GUID на наименование
                        var dict = referenceDataCache[refField.DbColumnName];
                        if (dict.TryGetValue(guidValue, out var displayName))
                        {
                            row[refField.Name] = displayName;
                        }
                        else
                        {
                            row[refField.Name] = guidValue.ToString();
                        }
                    }
                    else
                    {
                        // Обычное поле
                        var field = catalog.Fields.FirstOrDefault(f => f.DbColumnName == dbName);
                        var displayName = field?.Name ?? dbName;
                        row[displayName] = value;
                    }
                }

                result.Add(row);
            }

            await _context.Database.CloseConnectionAsync();
            return result;
        }

        // Вспомогательный метод: загружает словарь Id -> Name из справочника
        private async Task<Dictionary<Guid, string>> LoadReferenceDictionaryAsync(string catalogName)
        {
            var result = new Dictionary<Guid, string>();

            try
            {
                // Находим справочник по имени
                var catalog = await _context.MetadataObjects
                    .FirstOrDefaultAsync(m => m.Name == catalogName && m.ObjectType == "Catalog");

                if (catalog == null) return result;

                // Загружаем Id и Name (или другое поле для отображения)
                var sql = $"SELECT \"Id\", \"name\" FROM \"{catalog.TableName}\" WHERE \"is_active\" = true";

                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql;

                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                    await _context.Database.OpenConnectionAsync();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0);
                    var name = reader.GetString(1);
                    result[id] = name;
                }

                if (_context.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                    await _context.Database.CloseConnectionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки справочника {catalogName}: {ex.Message}");
            }

            return result;
        }

        // Добавление записи в справочник
        public async Task AddCatalogItemAsync(Guid catalogId, Dictionary<string, object> itemData)
        {
            var catalog = await _context.MetadataObjects
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(m => m.Id == catalogId);

            if (catalog == null) throw new Exception("Справочник не найден");

            var columns = new List<string>();
            var values = new List<string>();
            var parameters = new Dictionary<string, object>();

            foreach (var field in catalog.Fields)
            {
                if (itemData.ContainsKey(field.Name) && itemData[field.Name] != null)
                {
                    columns.Add($"\"{field.DbColumnName}\"");
                    values.Add($"@{field.DbColumnName}");
                    parameters[$"@{field.DbColumnName}"] = itemData[field.Name];
                }
            }

            columns.Add("\"Id\"");
            values.Add("@Id");
            parameters["@Id"] = Guid.NewGuid();

            columns.Add("\"CreatedAt\"");
            values.Add("@CreatedAt");
            parameters["@CreatedAt"] = DateTime.UtcNow;

            var sql = $"INSERT INTO \"{catalog.TableName}\" ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            foreach (var param in parameters)
            {
                var dbParam = command.CreateParameter();
                dbParam.ParameterName = param.Key;
                dbParam.Value = param.Value ?? DBNull.Value;
                command.Parameters.Add(dbParam);
            }

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();
        }

        public async Task SaveCatalogAsync(MetadataObject catalog)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var existingCatalog = await _context.Set<MetadataObject>()
                    .Include(c => c.Fields)
                    .FirstOrDefaultAsync(c => c.Id == catalog.Id);

                if (existingCatalog == null)
                {
                    throw new Exception("Справочник не найден");
                }

                existingCatalog.Name = catalog.Name;
                existingCatalog.Description = catalog.Description;
                existingCatalog.Icon = catalog.Icon;

                foreach (var field in catalog.Fields)
                {
                    var existingField = existingCatalog.Fields.FirstOrDefault(f => f.Id == field.Id);
                    if (existingField != null)
                    {
                        existingField.Name = field.Name;
                        existingField.DbColumnName = field.DbColumnName;
                        existingField.FieldType = field.FieldType;
                        existingField.IsRequired = field.IsRequired;
                        existingField.Order = field.Order;
                    }
                    else
                    {
                        field.Id = Guid.NewGuid();
                        field.MetadataObjectId = catalog.Id;
                        existingCatalog.Fields.Add(field);
                    }
                }

                var fieldsToRemove = existingCatalog.Fields
                    .Where(f => !catalog.Fields.Any(cf => cf.Id == f.Id))
                    .ToList();

                foreach (var field in fieldsToRemove)
                {
                    existingCatalog.Fields.Remove(field);
                    _context.Set<MetadataField>().Remove(field);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await UpdateTableStructureAsync(existingCatalog);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Ошибка сохранения справочника: {ex.Message}");
            }
        }

        public async Task DeleteCatalogAsync(Guid catalogId)
        {
            var catalog = await _context.MetadataObjects
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(c => c.Id == catalogId);

            if (catalog == null) return;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{catalog.TableName}\" CASCADE;");

                foreach (var field in catalog.Fields.ToList())
                {
                    _context.MetadataFields.Remove(field);
                }

                _context.MetadataObjects.Remove(catalog);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Ошибка удаления справочника: {ex.Message}");
            }
        }

        public async Task<MetadataObject> CreateCatalogAsync(string name, string description, string icon, List<FieldInfo> fields)
        {
            var tableName = $"catalog_{Guid.NewGuid():N}";

            var catalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = name,
                TableName = tableName,
                ObjectType = "Catalog",
                Description = description,
                Icon = icon,
                Order = await GetNextOrderAsync(),
                IsSystem = false,
                Fields = new List<MetadataField>()
            };

            int order = 1;
            foreach (var field in fields)
            {
                var dbColumnName = Transliterate(field.Name);

                catalog.Fields.Add(new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = field.Name,
                    DbColumnName = dbColumnName,
                    FieldType = field.Type,
                    IsRequired = field.IsRequired,
                    Order = order++,
                    MetadataObjectId = catalog.Id
                });
            }

            await _context.MetadataObjects.AddAsync(catalog);
            await _context.SaveChangesAsync();

            await CreateTableForCatalogAsync(catalog);

            return catalog;
        }

        private string Transliterate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "field";

            var translitMap = new Dictionary<char, string>
            {
                {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"}, {'е', "e"},
                {'ё', "yo"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"}, {'й', "y"}, {'к', "k"},
                {'л', "l"}, {'м', "m"}, {'н', "n"}, {'о', "o"}, {'п', "p"}, {'р', "r"},
                {'с', "s"}, {'т', "t"}, {'у', "u"}, {'ф', "f"}, {'х', "h"}, {'ц', "ts"},
                {'ч', "ch"}, {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""},
                {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
                {'А', "a"}, {'Б', "b"}, {'В', "v"}, {'Г', "g"}, {'Д', "d"}, {'Е', "e"},
                {'Ё', "yo"}, {'Ж', "zh"}, {'З', "z"}, {'И', "i"}, {'Й', "y"}, {'К', "k"},
                {'Л', "l"}, {'М', "m"}, {'Н', "n"}, {'О', "o"}, {'П', "p"}, {'Р', "r"},
                {'С', "s"}, {'Т', "t"}, {'У', "u"}, {'Ф', "f"}, {'Х', "h"}, {'Ц', "ts"},
                {'Ч', "ch"}, {'Ш', "sh"}, {'Щ', "sch"}, {'Ъ', ""}, {'Ы', "y"}, {'Ь', ""},
                {'Э', "e"}, {'Ю', "yu"}, {'Я', "ya"},
                {' ', "_"}, {'-', "_"}, {'.', "_"}, {',', "_"}, {'№', "n"}, {'#', "sharp"}
            };

            var result = new StringBuilder();
            foreach (char c in text)
            {
                if (translitMap.ContainsKey(c))
                    result.Append(translitMap[c]);
                else if (char.IsLetterOrDigit(c))
                    result.Append(char.ToLower(c));
                else
                    result.Append('_');
            }

            var final = result.ToString();
            while (final.Contains("__"))
                final = final.Replace("__", "_");

            final = final.Trim('_');

            if (string.IsNullOrEmpty(final))
                final = "field";

            return final;
        }

        private async Task CreateTableForCatalogAsync(MetadataObject catalog)
        {
            try
            {
                var sqlBuilder = new StringBuilder();

                sqlBuilder.AppendLine($"CREATE TABLE \"{catalog.TableName}\" (");
                sqlBuilder.AppendLine("    \"Id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");

                foreach (var field in catalog.Fields.OrderBy(f => f.Order))
                {
                    var sqlType = GetSqlTypeForField(field);
                    var nullable = field.IsRequired ? "NOT NULL" : "";
                    sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
                }

                sqlBuilder.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
                sqlBuilder.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
                sqlBuilder.AppendLine(");");

                await _context.Database.ExecuteSqlRawAsync(sqlBuilder.ToString());
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка создания таблицы: {ex.Message}");
            }
        }

        public async Task<List<MetadataObject>> GetCatalogsAsync()
        {
            return await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Catalog")
                .Include(m => m.Fields)
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

        public async Task<List<MetadataObject>> GetDocumentsAsync()
        {
            return await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Document")
                .Include(m => m.Fields)
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

        private async Task UpdateTableStructureAsync(MetadataObject catalog)
        {
            var sqlBuilder = new StringBuilder();

            sqlBuilder.AppendLine($"DROP TABLE IF EXISTS \"{catalog.TableName}\" CASCADE;");
            sqlBuilder.AppendLine($"CREATE TABLE \"{catalog.TableName}\" (");
            sqlBuilder.AppendLine("    \"Id\" uuid PRIMARY KEY DEFAULT gen_random_uuid(),");

            foreach (var field in catalog.Fields.OrderBy(f => f.Order))
            {
                var sqlType = GetSqlTypeForField(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sqlBuilder.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sqlBuilder.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
            sqlBuilder.AppendLine(");");

            await _context.Database.ExecuteSqlRawAsync(sqlBuilder.ToString());
        }

        private string GetSqlTypeForField(MetadataField field)
        {
            return field.FieldType switch
            {
                "String" => $"VARCHAR({(field.Length > 0 ? field.Length : 255)})",
                "Int" => "INTEGER",
                "Decimal" => $"DECIMAL({field.Precision}, {field.Scale})",
                "DateTime" => "TIMESTAMP",
                "Bool" => "BOOLEAN",
                _ => "TEXT"
            };
        }

        private async Task CreateTablesFromMetadataAsync()
        {
            var metadataObjects = await _context.Set<MetadataObject>()
                .Include(m => m.Fields)
                .ToListAsync();

            foreach (var obj in metadataObjects)
            {
                await CreateTableForMetadataObjectAsync(obj);
            }
        }

        private async Task CreateTableForMetadataObjectAsync(MetadataObject obj)
        {
            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"CREATE TABLE IF NOT EXISTS \"{obj.TableName}\" (");
            sqlBuilder.AppendLine("    \"Id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");

            foreach (var field in obj.Fields.OrderBy(f => f.Order))
            {
                var sqlType = GetSqlTypeForField(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sqlBuilder.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sqlBuilder.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
            sqlBuilder.AppendLine(");");

            try
            {
                await _context.Database.ExecuteSqlRawAsync(sqlBuilder.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating table {obj.TableName}: {ex.Message}");
            }
        }      

        private async Task<int> GetNextOrderAsync()
        {
            var maxOrder = await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Catalog")
                .MaxAsync(m => (int?)m.Order) ?? 0;
            return maxOrder + 1;
        }

        // ==================== ПРЕДУСТАНОВЛЕННЫЕ СПРАВОЧНИКИ ====================

        public async Task InitializePredefinedCatalogsAsync(Guid infoBaseId)  // ← добавить параметр
        {
            try
            {
                // Получаем конфигурацию по переданному Id
                var config = await _context.MetadataConfigurations
                    .FirstOrDefaultAsync(c => c.InfoBaseId == infoBaseId);

                if (config == null)
                {
                    config = new MetadataConfiguration
                    {
                        Id = Guid.NewGuid(),
                        InfoBaseId = infoBaseId,
                        IsInitialized = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Version = "1.0"
                    };
                    await _context.MetadataConfigurations.AddAsync(config);
                    await _context.SaveChangesAsync();
                }

                // Проверяем существующие справочники
                var existingCatalogs = await _context.MetadataObjects
                    .Where(m => m.ObjectType == "Catalog")
                    .Select(m => m.Name)
                    .ToListAsync();

                // Создаём недостающие справочники
                if (!existingCatalogs.Contains("План счетов"))
                    await CreateChartOfAccountsCatalog(config);

                if (!existingCatalogs.Contains("Банки"))
                    await CreateBanksCatalog(config);

                if (!existingCatalogs.Contains("Наименования категорий"))
                    await CreateMaterialCategoriesCatalog(config);

                if (!existingCatalogs.Contains("Виды материалов"))
                    await CreateMaterialTypesCatalog(config);

                if (!existingCatalogs.Contains("Справочник материалов"))
                    await CreateMaterialCatalog(config);

                System.Diagnostics.Debug.WriteLine("Все предустановленные справочники созданы");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания предустановленных справочников: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
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

       
        /// Поля для справочника "Наименования типов категорий"       
        private List<MetadataField> GetMaterialTypeFields(Guid metadataObjectId)
        {
            return new List<MetadataField>
        {
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Код",
                DbColumnName = "code",
                FieldType = "String",
                Length = 20,
                IsRequired = true,
                IsUnique = true,
                Order = 1,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Наименование вида",
                DbColumnName = "name",
                FieldType = "String",
                Length = 200,
                IsRequired = true,
                IsUnique = false,
                Order = 2,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Примечание",
                DbColumnName = "description",
                FieldType = "String",
                Length = 500,
                IsRequired = false,
                IsUnique = false,
                Order = 3,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Активен",
                DbColumnName = "is_active",
                FieldType = "Bool",
                Length = 0,
                IsRequired = false,
                IsUnique = false,
                Order = 4,
                MetadataObjectId = metadataObjectId
           }
        };
        }

        private async Task AddMaterialTypesDataToTable(MetadataObject catalog)
        {
            var materialTypes = new[]
            {
        new { code = "1", name = "ОС", description = "Основные средства", is_active = true },
        new { code = "2", name = "Малоценка", description = "Малоценные предметы", is_active = true },
        new { code = "3", name = "Прочие материалы", description = "Прочие материалы", is_active = true },
        new { code = "8", name = "Спец.одежда", description = "Специальная одежда", is_active = true },
        new { code = "9", name = "Бензин, л", description = "Бензин в литрах", is_active = true },
        new { code = "10", name = "Див. топливо, л", description = "Дизельное топливо в литрах", is_active = true },
        new { code = "11", name = "Авто Масла и про", description = "Автомасла и прочие жидкости", is_active = true },
        new { code = "12", name = "Сера, кг", description = "Сера в килограммах", is_active = true },
        new { code = "13", name = "Тринатрий фосфат, кг", description = "Тринатрий фосфат в кг", is_active = true },
        new { code = "14", name = "Известь хлорная, кг", description = "Известь хлорная в кг", is_active = true },
        new { code = "15", name = "Жир технический, кг", description = "Жир технический в кг", is_active = true },
        new { code = "16", name = "Мешки 50 кг, шт", description = "Мешки 50 кг в штуках", is_active = true },
        new { code = "17", name = "Мешки 25 кг, шт", description = "Мешки 25 кг в штуках", is_active = true },
        new { code = "18", name = "Бирки для мешков 25 кг, л", description = "Бирки для мешков 25 кг", is_active = true }
    };

            foreach (var type in materialTypes)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{type.code}',
                '{type.name.Replace("'", "''")}',
                '{type.description?.Replace("'", "''") ?? ""}',
                {type.is_active.ToString().ToLower()},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено видов материалов: {materialTypes.Length}");
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

        private async Task AddMaterialCategoriesDataToTable(MetadataObject catalog)
        {
            var categories = new[]
            {
            new { code = "1", name = "ЗАПАСЫ ОСНОВНОГО ПРОИЗВОДСТВА", description = "Основные производственные запасы", is_active = true },
            new { code = "2", name = "ТОПЛИВО", description = "Топливные материалы", is_active = true },
            new { code = "3", name = "ТАРА", description = "Тара и упаковка", is_active = true },
            new { code = "4", name = "ЗАПЧАСТИ", description = "Запасные части для оборудования", is_active = true },
            new { code = "5", name = "СТРОЙМАТЕРИАЛЫ", description = "Строительные материалы", is_active = true },
            new { code = "6", name = "ПРОЧИЕ МАТЕРИАЛЫ", description = "Прочие материалы", is_active = true }
        };

            foreach (var cat in categories)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{cat.code}',
                '{cat.name.Replace("'", "''")}',
                '{cat.description?.Replace("'", "''") ?? ""}',
                {cat.is_active},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено категорий: {categories.Length}");
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

        private async Task CreateBanksCatalog(MetadataConfiguration config)
        {
            try
            {
                var catalog = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Банки",
                    TableName = $"catalog_banks_{DateTime.Now:yyyyMMddHHmmss}",
                    ObjectType = "Catalog",
                    Description = "Справочник банков Кыргызской Республики",
                    Icon = "🏦",
                    Order = 11,
                    IsSystem = true,
                    MetadataConfigId = config.Id,
                    Fields = new List<MetadataField>()
                };

                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Наименование", DbColumnName = "name", FieldType = "String", Length = 200, IsRequired = true, Order = 1 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Краткое наименование", DbColumnName = "short_name", FieldType = "String", Length = 100, Order = 2 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "БИК", DbColumnName = "bic", FieldType = "String", Length = 20, Order = 3 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "ИНН", DbColumnName = "inn", FieldType = "String", Length = 50, Order = 4 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Адрес", DbColumnName = "address", FieldType = "String", Length = 500, Order = 5 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Телефон", DbColumnName = "phone", FieldType = "String", Length = 100, Order = 6 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Сайт", DbColumnName = "website", FieldType = "String", Length = 200, Order = 7 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "E-mail", DbColumnName = "email", FieldType = "String", Length = 100, Order = 8 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "SWIFT", DbColumnName = "swift", FieldType = "String", Length = 50, Order = 9 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Корр. счет", DbColumnName = "corr_account", FieldType = "String", Length = 50, Order = 10 });
                catalog.Fields.Add(new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", Order = 11 });

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

        private async Task AddChartOfAccountsDataToTable(MetadataObject catalog)
        {
            var accounts = InitialDataProvider.GetChartOfAccounts();

            foreach (var account in accounts)
            {
                try
                {
                    var sql = $@"
                        INSERT INTO ""{catalog.TableName}"" 
                        (""Id"", ""code"", ""name"", ""account_type"", ""description"", ""level"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
                        VALUES (
                            '{Guid.NewGuid()}',
                            '{account.Code}',
                            '{account.Name.Replace("'", "''")}',
                            '{account.AccountType}',
                            '{account.Description?.Replace("'", "''") ?? ""}',
                            {account.Level},
                            true,
                            NOW(),
                            NOW()
                        )";
                    await _context.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка добавления счета {account.Code}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено счетов: {accounts.Count}");
        }

        private async Task AddBanksDataToTable(MetadataObject catalog)
        {
            var banks = InitialDataProvider.GetBanks();

            foreach (var bank in banks)
            {
                try
                {
                    var sql = $@"
                        INSERT INTO ""{catalog.TableName}"" 
                        (""Id"", ""name"", ""short_name"", ""bic"", ""inn"", ""address"", ""phone"", ""website"", ""email"", ""swift"", ""corr_account"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
                        VALUES (
                            '{Guid.NewGuid()}',
                            '{bank.Name.Replace("'", "''")}',
                            '{bank.ShortName?.Replace("'", "''") ?? ""}',
                            '{bank.BIC}',
                            '{bank.INN}',
                            '{bank.Address?.Replace("'", "''") ?? ""}',
                            '{bank.Phone}',
                            '{bank.Website}',
                            '{bank.Email}',
                            '{bank.SwiftCode}',
                            '{bank.CorrespondentAccount}',
                            true,
                            NOW(),
                            NOW()
                        )";
                    await _context.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка добавления банка {bank.Name}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено банков: {banks.Count}");
        }

        // Добавьте эти методы в конец класса MetadataService

        #region Универсальные динамические методы

        /// <summary>
        /// Универсальное создание записи через метаданные
        /// </summary>
        public async Task<Guid> CreateDynamicRecordAsync(Guid metadataId, Dictionary<string, object> data)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null) throw new Exception($"Объект метаданных {metadataId} не найден");

            var columns = new List<string> { "\"Id\"", "\"CreatedAt\"" };
            var values = new List<string> { $"'{Guid.NewGuid()}'", "NOW()" };

            foreach (var field in metadata.Fields)
            {
                if (data.ContainsKey(field.Name) && data[field.Name] != null)
                {
                    columns.Add($"\"{field.DbColumnName}\"");
                    values.Add(FormatSqlValue(data[field.Name], field.FieldType));
                }
                else if (field.IsRequired && (!data.ContainsKey(field.Name) || data[field.Name] == null))
                {
                    throw new Exception($"Поле '{field.Name}' обязательно для заполнения");
                }
            }

            var sql = $@"
        INSERT INTO ""{metadata.TableName}"" ({string.Join(", ", columns)}) 
        VALUES ({string.Join(", ", values)}) 
        RETURNING ""Id""";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            var newId = await command.ExecuteScalarAsync();
            await _context.Database.CloseConnectionAsync();

            var recordId = Guid.Parse(newId.ToString());

            // Выполняем автоматические расчеты
            await ExecuteAutoCalculationsAsync(metadataId, recordId);

            return recordId;
        }

        /// <summary>
        /// Универсальное обновление записи
        /// </summary>
        public async Task UpdateDynamicRecordAsync(Guid metadataId, Guid recordId, Dictionary<string, object> data)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null) throw new Exception($"Объект метаданных {metadataId} не найден");

            var setClauses = new List<string>();

            foreach (var field in metadata.Fields)
            {
                if (data.ContainsKey(field.Name))
                {
                    setClauses.Add($"\"{field.DbColumnName}\" = {FormatSqlValue(data[field.Name], field.FieldType)}");
                }
            }

            setClauses.Add("\"UpdatedAt\" = NOW()");

            var sql = $@"
        UPDATE ""{metadata.TableName}"" 
        SET {string.Join(", ", setClauses)} 
        WHERE ""Id"" = '{recordId}'";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();

            // Выполняем автоматические расчеты
            await ExecuteAutoCalculationsAsync(metadataId, recordId);
        }

        /// <summary>
        /// Универсальное удаление записи
        /// </summary>
        public async Task DeleteDynamicRecordAsync(Guid metadataId, Guid recordId)
        {
            var metadata = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null) throw new Exception($"Объект метаданных {metadataId} не найден");

            var sql = $"DELETE FROM \"{metadata.TableName}\" WHERE \"Id\" = '{recordId}'";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();
        }

        /// <summary>
        /// Выполнение автоматических расчетов
        /// </summary>
        public async Task ExecuteAutoCalculationsAsync(Guid metadataId, Guid recordId)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Calculations)
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null || !metadata.Calculations.Any(c => c.IsAuto)) return;

            var recordData = await GetRecordDataAsync(metadata.TableName, recordId);

            foreach (var calc in metadata.Calculations.Where(c => c.IsAuto).OrderBy(c => c.ExecutionOrder))
            {
                try
                {
                    object result = null;

                    switch (calc.CalculationType)
                    {
                        case "Depreciation":
                            result = CalculateDepreciation(calc, recordData);
                            break;
                        case "Sum":
                            result = CalculateSum(calc, recordData);
                            break;
                        case "Average":
                            result = CalculateAverage(calc, recordData);
                            break;
                        case "Formula":
                            result = EvaluateFormula(calc.Formula, recordData);
                            break;
                    }

                    if (result != null)
                    {
                        await UpdateRecordFieldAsync(metadata.TableName, recordId, calc.TargetField, result);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка расчета {calc.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Создание проводок по правилам
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GeneratePostingsAsync(Guid metadataId, Guid recordId)
        {
            var postings = new List<Dictionary<string, object>>();

            var metadata = await _context.MetadataObjects
                .Include(m => m.PostingRules)
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null || !metadata.PostingRules.Any()) return postings;

            var recordData = await GetRecordDataAsync(metadata.TableName, recordId);

            foreach (var rule in metadata.PostingRules.OrderBy(r => r.Order))
            {
                // Проверяем условие
                if (!string.IsNullOrEmpty(rule.Condition))
                {
                    var conditionMet = EvaluateCondition(rule.Condition, recordData);
                    if (!conditionMet) continue;
                }

                var posting = new Dictionary<string, object>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["ObjectId"] = recordId,
                    ["ObjectType"] = metadata.Name,
                    ["ObjectTypeId"] = metadataId,
                    ["Date"] = recordData.ContainsKey("Date") ? recordData["Date"] : DateTime.Now,
                    ["DebitAccount"] = EvaluateExpression(rule.DebitAccountExpression, recordData),
                    ["CreditAccount"] = EvaluateExpression(rule.CreditAccountExpression, recordData),
                    ["Amount"] = Convert.ToDecimal(EvaluateExpression(rule.AmountExpression, recordData)),
                    ["CreatedAt"] = DateTime.Now
                };

                postings.Add(posting);
            }

            return postings;
        }

        /// <summary>
        /// Получение данных записи
        /// </summary>
        private async Task<Dictionary<string, object>> GetRecordDataAsync(string tableName, Guid recordId)
        {
            var result = new Dictionary<string, object>();
            var sql = $"SELECT * FROM \"{tableName}\" WHERE \"Id\" = '{recordId}'";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result[reader.GetName(i)] = reader.GetValue(i);
                }
            }

            await _context.Database.CloseConnectionAsync();
            return result;
        }

        /// <summary>
        /// Обновление поля записи
        /// </summary>
        /// 

        private async Task UpdateRecordFieldAsync(string tableName, Guid recordId, string fieldName, object value)
        {
            var formattedValue = FormatSqlValue(value, "Unknown");
            var sql = $"UPDATE \"{tableName}\" SET \"{fieldName}\" = {formattedValue}, \"UpdatedAt\" = NOW() WHERE \"Id\" = '{recordId}'";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();
        }

        #endregion

        #region Вычислительные методы

        private decimal CalculateDepreciation(MetadataCalculation calc, Dictionary<string, object> data)
        {
            var initialCost = Convert.ToDecimal(data.GetValueOrDefault("InitialCost", 0));
            var usefulLife = Convert.ToInt32(data.GetValueOrDefault("UsefulLife", 0));
            var depreciationRate = Convert.ToDecimal(data.GetValueOrDefault("DepreciationRate", 0));

            if (usefulLife > 0)
                return initialCost / usefulLife;
            else if (depreciationRate > 0)
                return initialCost * depreciationRate / 100;

            return 0;
        }

        private decimal CalculateSum(MetadataCalculation calc, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(calc.SourceFields)) return 0;

            var fields = System.Text.Json.JsonSerializer.Deserialize<List<string>>(calc.SourceFields);
            decimal sum = 0;

            foreach (var field in fields)
            {
                if (data.ContainsKey(field))
                    sum += Convert.ToDecimal(data[field]);
            }

            return sum;
        }

        private decimal CalculateAverage(MetadataCalculation calc, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(calc.SourceFields)) return 0;

            var fields = System.Text.Json.JsonSerializer.Deserialize<List<string>>(calc.SourceFields);
            decimal sum = 0;
            int count = 0;

            foreach (var field in fields)
            {
                if (data.ContainsKey(field))
                {
                    sum += Convert.ToDecimal(data[field]);
                    count++;
                }
            }

            return count > 0 ? sum / count : 0;
        }

        private decimal EvaluateFormula(string formula, Dictionary<string, object> data)
        {
            var result = formula;
            foreach (var kvp in data)
            {
                var value = kvp.Value?.ToString() ?? "0";
                result = result.Replace($"{{{kvp.Key}}}", value);
            }

            using var table = new System.Data.DataTable();
            return Convert.ToDecimal(table.Compute(result, ""));
        }

        private bool EvaluateCondition(string condition, Dictionary<string, object> data)
        {
            var result = condition;
            foreach (var kvp in data)
            {
                var value = kvp.Value?.ToString() ?? "null";
                result = result.Replace($"{{{kvp.Key}}}", value);
            }

            using var table = new System.Data.DataTable();
            return Convert.ToBoolean(table.Compute(result, ""));
        }

        private string EvaluateExpression(string expression, Dictionary<string, object> data)
        {
            var result = expression;
            foreach (var kvp in data)
            {
                var value = kvp.Value?.ToString() ?? "";
                result = result.Replace($"{{{kvp.Key}}}", value);
            }
            return result;
        }

        private string FormatSqlValue(object value, string fieldType)
        {
            if (value == null) return "NULL";

            return fieldType switch
            {
                "String" => $"'{value.ToString().Replace("'", "''")}'",
                "DateTime" => $"'{Convert.ToDateTime(value):yyyy-MM-dd HH:mm:ss}'",
                "Bool" => Convert.ToBoolean(value) ? "TRUE" : "FALSE",
                "Int" => Convert.ToInt32(value).ToString(),
                "Decimal" => Convert.ToDecimal(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => $"'{value}'"
            };
        }

        // Добавьте эти методы в MetadataService.cs

        public async Task CreateMetadataObjectAsync(MetadataObject obj)
        {
            await _context.MetadataObjects.AddAsync(obj);
            await _context.SaveChangesAsync();
        }     

        public async Task DeleteMetadataObjectAsync(Guid id)
        {
            var obj = await _context.MetadataObjects.FindAsync(id);
            if (obj != null)
            {
                _context.MetadataObjects.Remove(obj);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<MetadataObject>> GetAllMetadataObjectsAsync()
        {
            return await _context.MetadataObjects
                .Include(m => m.Fields)
                .OrderBy(m => m.Order)
                .ToListAsync();
        }
        #endregion



        // Добавьте эти методы в конец класса MetadataService



        public async Task UpdateMetadataObjectAsync(MetadataObject obj)
        {
            try
            {
                // Загружаем существующий объект
                var existing = await _context.MetadataObjects
                    .FirstOrDefaultAsync(m => m.Id == obj.Id);

                if (existing == null)
                {
                    throw new Exception($"Объект с ID {obj.Id} не найден");
                }

                // Получаем существующие поля из БД
                var existingFields = await _context.MetadataFields
                    .Where(f => f.MetadataObjectId == obj.Id)
                    .ToListAsync();

                // ОЧИЩАЕМ obj.Fields от дубликатов по имени
                var uniqueFields = obj.Fields
                    .GroupBy(f => f.Name)
                    .Select(g => g.First())
                    .ToList();
                obj.Fields = uniqueFields;

                // Обновляем основные поля
                existing.Name = obj.Name;
                existing.Description = obj.Description;
                existing.Icon = obj.Icon;
                existing.Order = obj.Order;
                existing.UsePostings = obj.UsePostings;
                existing.UseBalances = obj.UseBalances;
                existing.UseMovements = obj.UseMovements;

                // Находим новые поля (которых нет в БД) - по имени
                var existingFieldNames = existingFields.Select(f => f.Name).ToHashSet();
                var newFields = obj.Fields.Where(f => !existingFieldNames.Contains(f.Name)).ToList();

                // Обновляем существующие поля
                foreach (var field in obj.Fields)
                {
                    var existingField = existingFields.FirstOrDefault(f => f.Id == field.Id);
                    if (existingField != null)
                    {
                        existingField.Name = field.Name;
                        existingField.DbColumnName = field.DbColumnName;
                        existingField.FieldType = field.FieldType;
                        existingField.Length = field.Length;
                        existingField.Precision = field.Precision;
                        existingField.Scale = field.Scale;
                        existingField.IsRequired = field.IsRequired;
                        existingField.IsUnique = field.IsUnique;
                        existingField.Order = field.Order;
                    }
                }

                // Добавляем новые поля в метаданные
                foreach (var field in newFields)
                {
                    field.Id = Guid.NewGuid();
                    field.MetadataObjectId = obj.Id;
                    await _context.MetadataFields.AddAsync(field);

                    // Добавляем новую колонку в таблицу
                    await AddColumnToTableAsync(existing.TableName, field);
                }

                // Удаляем поля, которых больше нет в метаданных
                var fieldIdsToKeep = obj.Fields.Select(f => f.Id).ToHashSet();
                var fieldsToRemove = existingFields.Where(f => !fieldIdsToKeep.Contains(f.Id)).ToList();

                foreach (var field in fieldsToRemove)
                {
                    await DropColumnFromTableAsync(existing.TableName, field.DbColumnName);
                    _context.MetadataFields.Remove(field);
                }

                // Обновляем расчеты
                var existingCalcs = await _context.MetadataCalculations
                    .Where(c => c.MetadataObjectId == obj.Id)
                    .ToListAsync();
                _context.MetadataCalculations.RemoveRange(existingCalcs);
                foreach (var calc in obj.Calculations)
                {
                    calc.Id = Guid.NewGuid();
                    calc.MetadataObjectId = obj.Id;
                    await _context.MetadataCalculations.AddAsync(calc);
                }

                // Обновляем правила проводок
                var existingRules = await _context.MetadataPostingRules
                    .Where(r => r.MetadataObjectId == obj.Id)
                    .ToListAsync();
                _context.MetadataPostingRules.RemoveRange(existingRules);
                foreach (var rule in obj.PostingRules)
                {
                    rule.Id = Guid.NewGuid();
                    rule.MetadataObjectId = obj.Id;
                    await _context.MetadataPostingRules.AddAsync(rule);
                }

                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Объект {obj.Name} обновлен. Добавлено {newFields.Count} новых полей.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обновления: {ex.Message}");
            }
        }

        // Добавление новой колонки в существующую таблицу
        private async Task AddColumnToTableAsync(string tableName, MetadataField field)
        {
            try
            {
                // Проверяем существование колонки
                var checkSql = $@"
            SELECT COUNT(*) 
            FROM information_schema.columns 
            WHERE table_name = '{tableName}' 
            AND column_name = '{field.DbColumnName}'";

                using var checkCommand = _context.Database.GetDbConnection().CreateCommand();
                checkCommand.CommandText = checkSql;
                await _context.Database.OpenConnectionAsync();
                var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                await _context.Database.CloseConnectionAsync();

                if (exists > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Колонка {field.DbColumnName} уже существует");
                    return;
                }

                // Добавляем колонку
                var sqlType = GetSqlTypeForField(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";

                var defaultValue = "";
                if (field.IsRequired)
                {
                    defaultValue = field.FieldType switch
                    {
                        "String" => " DEFAULT ''",
                        "Int" => " DEFAULT 0",
                        "Decimal" => " DEFAULT 0",
                        "DateTime" => " DEFAULT CURRENT_TIMESTAMP",
                        "Bool" => " DEFAULT false",
                        _ => ""
                    };
                }

                var sql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{field.DbColumnName}\" {sqlType} {nullable} {defaultValue}";
                await _context.Database.ExecuteSqlRawAsync(sql);

                System.Diagnostics.Debug.WriteLine($"Добавлена колонка {field.DbColumnName} в таблицу {tableName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка добавления колонки: {ex.Message}");
            }
        }

        // Удаление колонки из таблицы
        private async Task DropColumnFromTableAsync(string tableName, string columnName)
        {
            try
            {
                var sql = $"ALTER TABLE \"{tableName}\" DROP COLUMN IF EXISTS \"{columnName}\" CASCADE";
                await _context.Database.ExecuteSqlRawAsync(sql);

                System.Diagnostics.Debug.WriteLine($"Удалена колонка {columnName} из таблицы {tableName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления колонки: {ex.Message}");
                // Не выбрасываем исключение, чтобы не прерывать операцию
            }
        }

        public async Task UpdateMetadataObjectOrderAsync(Guid id, int order)
        {
            var obj = await _context.MetadataObjects.FindAsync(id);
            if (obj != null)
            {
                obj.Order = order;
                await _context.SaveChangesAsync();
            }
        }

        public async Task CreateDynamicTableAsync(MetadataObject obj)
        {
            var sql = new StringBuilder();
            sql.AppendLine($"CREATE TABLE IF NOT EXISTS \"{obj.TableName}\" (");
            sql.AppendLine("    \"Id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");

            foreach (var field in obj.Fields.OrderBy(f => f.Order))
            {
                var sqlType = GetSqlTypeForField(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sql.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sql.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sql.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
            sql.AppendLine(");");

            await _context.Database.ExecuteSqlRawAsync(sql.ToString());
        }

        public async Task UpdateDynamicTableAsync(MetadataObject obj)
        {
            // Пересоздаем таблицу с новой структурой
            await _context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{obj.TableName}\" CASCADE;");
            await CreateDynamicTableAsync(obj);
        }

        // Добавьте эти методы в конец класса MetadataService

        public async Task<List<DynamicDocument>> GetAllPostingsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _context.DynamicDocuments.AsQueryable();

            if (startDate.HasValue)
                query = query.Where(d => d.Date >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(d => d.Date <= endDate.Value);

            return await query.OrderByDescending(d => d.Date).ToListAsync();
        }           
    }
}