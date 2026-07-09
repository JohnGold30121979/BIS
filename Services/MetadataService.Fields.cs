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
                },
                new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Активен",
                    DbColumnName = "is_active",
                    FieldType = "Bool",
                    IsRequired = true,
                    Order = 4,
                    MetadataObjectId = metadataObjectId
                }
            };
    }

    private List<MetadataField> GetSupplyKindFields(Guid metadataObjectId) =>
        GetEsfClassifierFields(metadataObjectId);

    private List<MetadataField> GetDeliveryTypeFields(Guid metadataObjectId) =>
        GetEsfClassifierFields(metadataObjectId);

    private List<MetadataField> GetEsfClassifierFields(Guid metadataObjectId)
    {
        var fields = GetStandardCatalogFields(metadataObjectId);
        fields.Add(new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код ЭСФ",
            DbColumnName = "esf_code",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            Order = 5,
            MetadataObjectId = metadataObjectId
        });
        fields.Add(new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Порядок",
            DbColumnName = "sort_order",
            FieldType = "Int",
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        });
        fields.Add(new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "По умолчанию",
            DbColumnName = "is_default",
            FieldType = "Bool",
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        });
        return fields;
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
            },
            new MetadataField { Id = Guid.NewGuid(), Name = "Признак счета Fox (prsch)", DbColumnName = "prsch", FieldType = "Int", Order = 19, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Признак НДС Fox (pr_sch)", DbColumnName = "pr_sch", FieldType = "Int", Order = 20, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Признак ОС Fox (pr_sc7)", DbColumnName = "pr_sc7", FieldType = "Int", Order = 21, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Связь с организациями Fox (sv_o)", DbColumnName = "sv_o", FieldType = "Bool", Order = 22, MetadataObjectId = metadataObjectId },
            new MetadataField { Id = Guid.NewGuid(), Name = "Код формы отчета Fox (kodf_rb)", DbColumnName = "kodf_rb", FieldType = "Int", Order = 23, MetadataObjectId = metadataObjectId }
        };
    }

    private List<MetadataField> GetAccountAnalyticsLinkFields(Guid metadataObjectId)
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
                Length = 150,
                IsRequired = true,
                Order = 2,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Поле настройки счета",
                DbColumnName = "account_flag_field",
                FieldType = "String",
                Length = 150,
                IsRequired = true,
                Order = 3,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Справочник",
                DbColumnName = "reference_catalog",
                FieldType = "String",
                Length = 150,
                IsRequired = true,
                Order = 4,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Поля документа",
                DbColumnName = "document_fields",
                FieldType = "String",
                Length = 500,
                IsRequired = true,
                Order = 5,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Описание",
                DbColumnName = "description",
                FieldType = "String",
                Length = 500,
                Order = 6,
                MetadataObjectId = metadataObjectId
            },
            new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = "Активен",
                DbColumnName = "is_active",
                FieldType = "Bool",
                IsRequired = true,
                Order = 7,
                MetadataObjectId = metadataObjectId
            }
        };
    }

    private List<MetadataField> GetDocumentAnalyticReferenceFields(Guid metadataObjectId, int startOrder)
    {
        return new List<MetadataField>
        {
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
                Order = startOrder,
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
                Order = startOrder + 1,
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
                Order = startOrder + 2,
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

    private List<MetadataField> GetFixedAssetFields(Guid metadataObjectId)
    {
        var fields = new List<MetadataField>();

        void Add(string name, string column, string type, int order, bool required = false,
            string? referenceCatalog = null, int length = 0, int scale = 2)
        {
            fields.Add(new MetadataField
            {
                Id = Guid.NewGuid(),
                Name = name,
                DbColumnName = column,
                FieldType = type,
                Order = order,
                IsRequired = required,
                Length = length,
                Precision = 18,
                Scale = scale,
                ReferenceCatalog = referenceCatalog,
                MetadataObjectId = metadataObjectId
            });
        }

        Add("Код", "code", "String", 1, true, length: 50);
        Add("Инвентарный номер", "inventory_number", "String", 2, true, length: 50);
        Add("Наименование", "name", "String", 3, true, length: 300);
        Add("Группа ОС", "asset_group", "String", 4, length: 100);
        Add("Дата приобретения", "acquisition_date", "DateTime", 5);
        Add("Дата ввода в эксплуатацию", "commissioning_date", "DateTime", 6);
        Add("Первоначальная стоимость", "initial_cost", "Decimal", 7);
        Add("Накопленная амортизация", "accumulated_depreciation", "Decimal", 8);
        Add("Остаточная стоимость", "carrying_amount", "Decimal", 9);
        Add("Срок полезного использования, мес.", "useful_life_months", "Int", 10);
        Add("Метод амортизации", "depreciation_method", "String", 11, length: 50);
        Add("Счет учета", "asset_account", "String", 12, length: 50);
        Add("Счет амортизации", "depreciation_account", "String", 13, length: 50);
        Add("Организация", "organization_id", "Reference", 14, referenceCatalog: "Организации");
        Add("МОЛ", "responsible_person_id", "Reference", 15, referenceCatalog: "МОЛ");
        Add("Участок", "site_id", "Reference", 16, referenceCatalog: "Участки");
        Add("Статус", "status", "String", 17, length: 50);
        Add("Активен", "is_active", "Bool", 18, true);
        Add("Описание", "description", "String", 19, length: 500);
        return fields;
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

  
    // Поля справочника "Организации".
    // Первая запись с is_primary=true используется как реквизиты нашего предприятия для печатных форм.
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
          Name = "Первичная организация",
          DbColumnName = "is_primary",
          FieldType = "Bool",
          IsRequired = true,
          Order = 3,
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
          Order = 4,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Организационно-правовая форма",
          DbColumnName = "legal_form",
          FieldType = "String",
          Length = 100,
          IsRequired = false,
          Order = 5,
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
          Order = 6,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "ОКПО",
          DbColumnName = "okpo",
          FieldType = "String",
          Length = 50,
          IsRequired = false,
          Order = 7,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Регистрационный номер",
          DbColumnName = "registration_number",
          FieldType = "String",
          Length = 100,
          IsRequired = false,
          Order = 8,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Государство",
          DbColumnName = "country_id",
          FieldType = "Reference",
          ReferenceCatalog = "Государства",
          DisplayPattern = "{Наименование}",
          DisplayFields = "Наименование",
          IsRequired = false,
          IsUnique = false,
          Order = 9,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Юридический адрес",
          DbColumnName = "legal_address",
          FieldType = "String",
          Length = 500,
          IsRequired = false,
          Order = 10,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Фактический адрес",
          DbColumnName = "actual_address",
          FieldType = "String",
          Length = 500,
          IsRequired = false,
          Order = 11,
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
          Order = 12,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Email",
          DbColumnName = "email",
          FieldType = "String",
          Length = 150,
          IsRequired = false,
          Order = 13,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Банк",
          DbColumnName = "bank_name",
          FieldType = "String",
          Length = 250,
          IsRequired = false,
          Order = 14,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Расчетный счет",
          DbColumnName = "bank_account",
          FieldType = "String",
          Length = 100,
          IsRequired = false,
          Order = 15,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "БИК",
          DbColumnName = "bic",
          FieldType = "String",
          Length = 50,
          IsRequired = false,
          Order = 16,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Руководитель",
          DbColumnName = "director",
          FieldType = "String",
          Length = 200,
          IsRequired = false,
          Order = 17,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Главный бухгалтер",
          DbColumnName = "chief_accountant",
          FieldType = "String",
          Length = 200,
          IsRequired = false,
          Order = 18,
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
          Order = 19,
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
          Order = 20,
          MetadataObjectId = metadataObjectId
      },
      new MetadataField
      {
          Id = Guid.NewGuid(),
          Name = "Активна",
          DbColumnName = "is_active",
          FieldType = "Bool",
          IsRequired = true,
          Order = 21,
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
            Name = "Должность (справочник)",
            DbColumnName = "position_id",
            FieldType = "Reference",
            ReferenceCatalog = "Должности",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            IsUnique = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Должность (текст)",
            DbColumnName = "position_text",
            FieldType = "String",
            Length = 200,
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Подразделение",
            DbColumnName = "department_id",
            FieldType = "Reference",
            ReferenceCatalog = "Подразделения",
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
            Name = "Дата рождения",
            DbColumnName = "birth_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 9,
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
            Order = 10,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата приема",
            DbColumnName = "hire_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 11,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата увольнения",
            DbColumnName = "termination_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 12,
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
            Order = 13,
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
            Order = 14,
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
            Order = 15,
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
            Order = 16,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Паспорт №/ID",
            DbColumnName = "passport_number",
            FieldType = "String",
            Length = 80,
            IsRequired = false,
            Order = 17,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Кем выдан",
            DbColumnName = "passport_issued_by",
            FieldType = "String",
            Length = 300,
            IsRequired = false,
            Order = 18,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дата выдачи",
            DbColumnName = "passport_issue_date",
            FieldType = "DateTime",
            IsRequired = false,
            Order = 19,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = false,
            Order = 20,
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

    // Поля для справочника "Должности"
    private List<MetadataField> GetPositionFields(Guid metadataObjectId)
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

    private List<MetadataField> GetInventoryDocumentFields(Guid metadataObjectId, int startOrder)
    {
        return new List<MetadataField>
        {
            new() { Id = Guid.NewGuid(), Name = "Материал", DbColumnName = "material_id", FieldType = "Reference", ReferenceCatalog = "Справочник материалов", DisplayPattern = "{Код} - {Наименование материала}", DisplayFields = "Код,Наименование материала", IsRequired = true, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Количество", DbColumnName = "quantity", FieldType = "Decimal", Precision = 18, Scale = 6, IsRequired = true, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Цена", DbColumnName = "price", FieldType = "Decimal", Precision = 18, Scale = 6, IsRequired = true, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Ставка НДС", DbColumnName = "vat_rate", FieldType = "Decimal", Precision = 5, Scale = 2, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Сумма НДС", DbColumnName = "vat_amount", FieldType = "Decimal", Precision = 18, Scale = 2, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Номер счета-фактуры", DbColumnName = "invoice_number", FieldType = "String", Length = 50, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Дебет", DbColumnName = "debit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", IsRequired = true, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Кредит", DbColumnName = "credit_account", FieldType = "Reference", ReferenceCatalog = "План счетов", IsRequired = true, Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Организация", DbColumnName = "organization_id", FieldType = "Reference", ReferenceCatalog = "Организации", Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "МОЛ", DbColumnName = "responsible_person_id", FieldType = "Reference", ReferenceCatalog = "МОЛ", Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Участок", DbColumnName = "site_id", FieldType = "Reference", ReferenceCatalog = "Участки", Order = startOrder++, MetadataObjectId = metadataObjectId },
            new() { Id = Guid.NewGuid(), Name = "Проведён", DbColumnName = "is_posted", FieldType = "Bool", IsRequired = true, Order = startOrder, MetadataObjectId = metadataObjectId }
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
            Name = "Номер документа",
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
            DbColumnName = "posting_date",
            FieldType = "DateTime",
            IsRequired = true,
            IsUnique = false,
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
            DisplayPattern = "{Счет} - {Наименование кассы}",
            DisplayFields = "Счет,Наименование кассы",
            IsRequired = false,
            IsUnique = false,
            Order = 3,
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
            Order = 4,
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
            Order = 5,
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
            Order = 6,
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
            Order = 7,
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
            Order = 8,
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
            Order = 9,
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
            Order = 10,
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
            Order = 11,
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
            Order = 12,
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
            Order = 13,
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
            Order = 14,
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
            Order = 15,
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
            Order = 16,
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
            Name = "Наименование кассы",
            DbColumnName = "name",
            FieldType = "String",
            Length = 200,
            IsRequired = true,
            IsUnique = false,
            Order = 1,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Счет",
            DbColumnName = "code",
            FieldType = "Reference",
            ReferenceCatalog = "План счетов",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            Length = 80,
            IsRequired = true,
            IsUnique = true,
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Номер кассы",
            DbColumnName = "cash_number",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            IsUnique = false,
            Order = 3,
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
            Order = 4,
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
            Order = 5,
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
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Активен",
            DbColumnName = "is_active",
            FieldType = "Bool",
            IsRequired = true,
            Order = 7,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    private List<MetadataField> GetCashReceiptFields(Guid metadataObjectId)
    {
        return new List<MetadataField>
    {
        // Существующие поля
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
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Касса",
            DbColumnName = "cash_desk_id",
            FieldType = "Reference",
            ReferenceCatalog = "Кассы",
            DisplayPattern = "{Счет} - {Наименование кассы}",
            DisplayFields = "Счет,Наименование кассы",
            IsRequired = false,
            Order = 4,
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
            Order = 5,
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
            Order = 6,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Корр. счет",
            DbColumnName = "correspondent_account",
            FieldType = "String",  // или Reference на План счетов
            Length = 50,
            IsRequired = false,
            Order = 7,
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
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Проведён",
            DbColumnName = "is_posted",
            FieldType = "Bool",
            IsRequired = true,
            Order = 10,
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
            Order = 11,
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
            Order = 12,
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
            Order = 13,
            MetadataObjectId = metadataObjectId
        },
        
        // ✅ НОВЫЕ ПОЛЯ ДЛЯ ПЕЧАТНОЙ ФОРМЫ
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Дебет",
            DbColumnName = "debit_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 14,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Кредит",
            DbColumnName = "credit_account",
            FieldType = "String",
            Length = 50,
            IsRequired = false,
            Order = 15,
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
            Order = 16,
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
            Name = "Организация",
            DbColumnName = "organization_id",
            FieldType = "Reference",
            ReferenceCatalog = "Организации",
            DisplayPattern = "{Код} - {Наименование}",
            DisplayFields = "Код,Наименование",
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Касса",
            DbColumnName = "cash_desk_id",
            FieldType = "Reference",
            ReferenceCatalog = "Кассы",
            DisplayPattern = "{Счет} - {Наименование кассы}",
            DisplayFields = "Счет,Наименование кассы",
            IsRequired = false,
            Order = 4,
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
            Order = 5,
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
            Order = 6,
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
            Order = 7,
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
            Order = 8,
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
            IsRequired = false,
            IsUnique = false,
            Order = 4,
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
            Order = 5,
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
            Order = 6,
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
            Order = 7,
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
            IsUnique = false,
            Order = 9,
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
            Order = 10,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    #region Поля для новых справочников

   // Поля для справочника "Налоги"
    private List<MetadataField> GetTaxFields(Guid metadataObjectId)
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
            Name = "Ставка",
            DbColumnName = "rate",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = true,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код ЭСФ НДС",
            DbColumnName = "esf_vat_code",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
            Order = 4,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код ЭСФ НСП",
            DbColumnName = "esf_sales_tax_code",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
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
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Порядок",
            DbColumnName = "sort_order",
            FieldType = "Int",
            IsRequired = false,
            Order = 7,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "По умолчанию для НДС",
            DbColumnName = "is_default_vat",
            FieldType = "Bool",
            IsRequired = false,
            Order = 8,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "По умолчанию для налога с продаж",
            DbColumnName = "is_default_sales_tax",
            FieldType = "Bool",
            IsRequired = false,
            Order = 9,
            MetadataObjectId = metadataObjectId
        }
    };
    }
    
    // Поля для справочника "Участки (новые)"
    private List<MetadataField> GetPlotFields(Guid metadataObjectId)
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
            IsRequired = true,
            Order = 4,
            MetadataObjectId = metadataObjectId
        }
    };
    }
   
    // Поля для справочника "Виды оплаты"
    private List<MetadataField> GetPaymentKindFields(Guid metadataObjectId)
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
            Order = 2,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Ставка",
            DbColumnName = "rate",
            FieldType = "Decimal",
            Precision = 18,
            Scale = 2,
            IsRequired = false,
            Order = 3,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "Код ЭСФ",
            DbColumnName = "esf_code",
            FieldType = "String",
            Length = 20,
            IsRequired = false,
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
            Order = 5,
            MetadataObjectId = metadataObjectId
        },
        new MetadataField
        {
            Id = Guid.NewGuid(),
            Name = "По умолчанию",
            DbColumnName = "is_default",
            FieldType = "Bool",
            IsRequired = false,
            Order = 6,
            MetadataObjectId = metadataObjectId
        }
    };
    }

    #endregion

}
