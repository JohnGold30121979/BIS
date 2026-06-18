using BIS.ERP.Models;

namespace BIS.ERP.Services;

public partial class MetadataService
{
    // Стандартные поля для простых справочников
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

    private List<MetadataField> GetChartOfAccountsFields(Guid metadataObjectId)
    {
        var fields = new List<MetadataField>
        {
            new MetadataField { Id = Guid.NewGuid(), Name = "Код", DbColumnName = "code", FieldType = "String", Length = 20, IsRequired = true, IsUnique = true, Order = 1, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Наименование", DbColumnName = "name", FieldType = "String", Length = 200, IsRequired = true, Order = 2, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Тип счета", DbColumnName = "account_type", FieldType = "String", Length = 20, Order = 3, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Описание", DbColumnName = "description", FieldType = "String", Length = 500, Order = 4, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Уровень", DbColumnName = "level", FieldType = "Int", Order = 5, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Активен", DbColumnName = "is_active", FieldType = "Bool", Order = 6, MetadataObjectId = metadataObjectId }
        };

        fields.AddRange(GetChartOfAccountsAnalyticFields(metadataObjectId));
        return fields;
    }

    private List<MetadataField> GetChartOfAccountsAnalyticFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
        {
            new MetadataField { Id = Guid.NewGuid(), Name = "Закрывает АРМ", DbColumnName = "closing_arm", FieldType = "String", Length = 50, Order = 7, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Группа аналитических статей", DbColumnName = "analytic_group", FieldType = "String", Length = 100, Order = 8, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Признак печати", DbColumnName = "print_mode", FieldType = "String", Length = 50, Order = 9, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Сохранять остатки", DbColumnName = "balance_mode", FieldType = "String", Length = 50, Order = 10, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с организациями", DbColumnName = "link_organizations", FieldType = "Bool", Order = 11, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь со списочным составом", DbColumnName = "link_employees", FieldType = "Bool", Order = 12, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с валютами", DbColumnName = "link_currencies", FieldType = "Bool", Order = 13, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с лицевыми счетами", DbColumnName = "link_personal_accounts", FieldType = "Bool", Order = 14, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с материалами", DbColumnName = "link_materials", FieldType = "Bool", Order = 15, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с объектами строительства", DbColumnName = "link_construction_objects", FieldType = "Bool", Order = 16, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Код налога", DbColumnName = "tax_code", FieldType = "String", Length = 30, Order = 17, MetadataObjectId = metadataObjectId },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Валюта счета",
                DbColumnName = "account_currency_id",
                FieldType = "Reference",
                ReferenceCatalog = "Справочник валют",
                DisplayPattern = "{Код} - {Наименование}",
                DisplayFields = "Код,Наименование",
                Order = 18,
                MetadataObjectId = metadataObjectId
            }
        };
    }

    // Поля для справочника материалов
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

    private List<MetadataField> GetCurrencyRateFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
      {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата",
            DbColumnName = "rate_date",
            FieldType = "DateTime",
            IsRequired = true,
            IsUnique = false,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Валюта",
            DbColumnName = "currency_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник валют",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Курс НБ",
            DbColumnName = "rate_nb",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 6,
            IsRequired = true,
            IsUnique = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Курс ком",
            DbColumnName = "rate_commercial",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 6,
            IsRequired = false,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            IsUnique = false,
            Order = 5,
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
            Order = 6,
            MetadataObjectId = metadataObjectId
        }
      };
    }

  
    //Поля справочника Организации
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
          IsRequired = false,
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
          IsRequired = false,
          Order = 4,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Государство",           // ← Теперь Reference!
          DbColumnName = "country_id",
          FieldType = "Reference",
          ReferenceCatalog = "Государства",
          DisplayPattern = "{Наименование}",
          DisplayFields = "Наименование",
          IsRequired = false,
          IsUnique = false,
          Order = 5,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Группа",
          DbColumnName = "group_code",
          FieldType = "String",
          Length = 20,
          IsRequired = false,
          Order = 6,
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
          Order = 7,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Активна",
          DbColumnName = "is_active",
          FieldType = "Bool",
          IsRequired = true,
          Order = 8,
          MetadataObjectId = metadataObjectId
      }
     };
    }

    // Поля для справочника "Расчетные счета организаций"
    private List<MetadataField> GetBankAccountFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Банк",
            DbColumnName = "bank_id",
            FieldType = "Reference",
            ReferenceCatalog = "Банки",
            DisplayPattern = "{Наименование банка}",
            DisplayFields = "Наименование банка",
            IsRequired = true,
            IsUnique = false,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Счет",
            DbColumnName = "account_number",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = true,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "БИК",
            DbColumnName = "bic",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Валюта",
            DbColumnName = "currency_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник валют",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = true,
            IsUnique = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Основной счет",
            DbColumnName = "is_main",
            FieldType = "Bool",
            IsRequired = true,
            IsUnique = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Текущий остаток",
            DbColumnName = "current_balance",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = true,           
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            IsUnique = false,
            Order = 7,
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
            Order = 8,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    // Поля для справочника "Наименования категорий" 
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
            Name = "Наименование банка",
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
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Отделение",
            DbColumnName = "branch",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
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
            IsRequired = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Телефон",
            DbColumnName = "phone",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "SWIFT",
            DbColumnName = "swift",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "CHIPS",
            DbColumnName = "chips",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Адрес на англ.",
            DbColumnName = "address_eng",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            Order = 10,
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
            Order = 11,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    // Поля для справочника "Сотрудники (Списочный состав)" - расширенная версия
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

    // Поля для справочника Участки
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

    // Поля для справочника "Наименования типов категорий"  
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

    // Поля для справочника "Государства"
    private List<MetadataField> GetCountryFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
      {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код",
            DbColumnName = "code",
            FieldType = "String",
            Length = 10,
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
            IsRequired = true,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        }
     };
    }

    // Поля для документа "Проводки"
    private List<MetadataField> GetPostingFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата",
            DbColumnName = "posting_date",
            FieldType = "DateTime",
            IsRequired = true,
            IsUnique = false,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Номер документа",
            DbColumnName = "doc_number",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        { 
            Id = Guid.NewGuid(),
            Name = "Тип документа",
            DbColumnName = "document_type",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дебет",
            DbColumnName = "debit_account",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Кредит",
            DbColumnName = "credit_account",
            FieldType = "String",
            Length = 50,
            IsRequired = true,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Сумма в сом",
            DbColumnName = "amount_kgs",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = true,
            IsUnique = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Сумма в валюте",
            DbColumnName = "amount_currency",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = false,
            IsUnique = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Валюта",
            DbColumnName = "currency_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник валют",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Договор",
            DbColumnName = "contract_id",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            IsUnique = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Сотрудник",
            DbColumnName = "employee_id",
            FieldType = "Reference",
            ReferenceCatalog = "Сотрудники (Списочный состав)",
            DisplayPattern = "{Табельный номер} - {ФИО}",
            DisplayFields = "Табельный номер,ФИО",
            IsRequired = false,
            IsUnique = false,
            Order = 10,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Материал",
            DbColumnName = "material_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник материалов",
            DisplayPattern = "{Код} - {Наименование материала}",
            DisplayFields = "Код,Наименование материала",
            IsRequired = false,
            IsUnique = false,
            Order = 11,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Статья",
            DbColumnName = "article_id",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            IsUnique = false,
            Order = 12,
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
            Order = 13,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            IsUnique = false,
            Order = 14,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    private List<MetadataField> GetCashDeskFields(Guid metadataObjectId)
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
            IsUnique = false,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Валюта",
            DbColumnName = "currency_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник валют",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = true,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Начальный остаток",
            DbColumnName = "initial_balance",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Текущий остаток",
            DbColumnName = "current_balance",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = true,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            Order = 6,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    private List<MetadataField> GetCashReceiptFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Номер",
            DbColumnName = "doc_number",
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
            Name = "Дата",
            DbColumnName = "doc_date",
            FieldType = "DateTime",
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Касса",
            DbColumnName = "cash_desk_id",
            FieldType = "Reference",
            ReferenceCatalog = "Кассы",
            DisplayPattern = "{Наименование}",
            DisplayFields = "Наименование",
            IsRequired = true,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Контрагент",
            DbColumnName = "contractor_id",
            FieldType = "Reference",
            ReferenceCatalog = "Контрагенты",
            DisplayPattern = "{Наименование}",
            DisplayFields = "Наименование",
            IsRequired = false,
            Order = 5,
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
            IsRequired = true,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Основание",
            DbColumnName = "basis",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Корр. счет",
            DbColumnName = "correspondent_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Статья ДДС",
            DbColumnName = "cash_flow_item",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 9,
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
            Order = 10,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Проведён",
            DbColumnName = "is_posted",
            FieldType = "Bool",
            IsRequired = true,
            Order = 11,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    private List<MetadataField> GetCashPaymentFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Номер",
            DbColumnName = "doc_number",
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
            Name = "Дата",
            DbColumnName = "doc_date",
            FieldType = "DateTime",
            IsRequired = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Касса",
            DbColumnName = "cash_desk_id",
            FieldType = "Reference",
            ReferenceCatalog = "Кассы",
            DisplayPattern = "{Наименование}",
            DisplayFields = "Наименование",
            IsRequired = true,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Контрагент",
            DbColumnName = "contractor_id",
            FieldType = "Reference",
            ReferenceCatalog = "Контрагенты",
            DisplayPattern = "{Наименование}",
            DisplayFields = "Наименование",
            IsRequired = false,
            Order = 5,
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
            IsRequired = true,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Основание",
            DbColumnName = "basis",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Корр. счет",
            DbColumnName = "correspondent_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Статья ДДС",
            DbColumnName = "cash_flow_item",
            FieldType = "String",
            Length = 100,
            IsRequired = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Счет кассы",
            DbColumnName = "cash_account",
            FieldType = "Reference",
            ReferenceCatalog = "План счетов",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            Order = 10,
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
            Order = 11,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Проведён",
            DbColumnName = "is_posted",
            FieldType = "Bool",
            IsRequired = true,
            Order = 12,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    // Поля для документа "Платежное поручение"
    private List<MetadataField> GetPaymentOrderFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Номер",
            DbColumnName = "doc_number",
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
            Name = "Дата",
            DbColumnName = "doc_date",
            FieldType = "DateTime",
            IsRequired = true,
            IsUnique = false,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Тип",
            DbColumnName = "order_type",
            FieldType = "String",
            Length = 100,
            IsRequired = true,
            IsUnique = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = true,
            IsUnique = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Контрагент",
            DbColumnName = "contractor_id",
            FieldType = "Reference",
            ReferenceCatalog = "Контрагенты",
            DisplayPattern = "{Наименование}",
            DisplayFields = "Наименование",
            IsRequired = true,
            IsUnique = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Банк",
            DbColumnName = "bank_id",
            FieldType = "Reference",
            ReferenceCatalog = "Банки",
            DisplayPattern = "{Наименование банка}",
            DisplayFields = "Наименование банка",
            IsRequired = true,
            IsUnique = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Расчетный счет контрагента",
            DbColumnName = "counterparty_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            IsUnique = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Наш счет",
            DbColumnName = "our_account_id",
            FieldType = "Reference",
            ReferenceCatalog = "Расчетные счета организаций",
            DisplayPattern = "{Счет} - {Банк}",
            DisplayFields = "Счет,Банк",
            IsRequired = true,
            IsUnique = false,
            Order = 8,
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
            IsRequired = true,
            IsUnique = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Валюта",
            DbColumnName = "currency_id",
            FieldType = "Reference",
            ReferenceCatalog = "Справочник валют",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = true,
            IsUnique = false,
            Order = 10,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Назначение платежа",
            DbColumnName = "purpose",
            FieldType = "String",
            Length = 500,
            IsRequired = false,
            IsUnique = false,
            Order = 11,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Корр. счет",
            DbColumnName = "correspondent_account",
            FieldType = "Reference",
            ReferenceCatalog = "План счетов",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 12,
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
            Order = 13,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Проведён",
            DbColumnName = "is_posted",
            FieldType = "Bool",
            IsRequired = true,
            IsUnique = false,
            Order = 14,
            MetadataObjectId = metadataObjectId
        }
    };
    }

}
