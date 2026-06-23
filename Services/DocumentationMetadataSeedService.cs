using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    public class DocumentationMetadataSeedService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;

        public DocumentationMetadataSeedService(AppDbContext context)
        {
            _context = context;
            _metadataService = new MetadataService(context);
        }

        public async Task EnsureAsync()
        {
            await EnsureFixedAssetCatalogsAsync();
            await ExtendFixedAssetCardAsync();
            await EnsureDocumentsAsync();
            await EnsureFixedAssetReportsAsync();
            await EnsureFinanceReportsAsync();
            await EnsureInventoryReportsAsync();
            await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
        }

        private async Task EnsureFixedAssetCatalogsAsync()
        {
            await EnsureObjectAsync("Соответствия счетов ОС", "catalog_asset_account_links", "Catalog",
                "Связь счета учета ОС со счетами амортизации и затрат", "🔗", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Материальный счет", "asset_account", "String", true, null),
                    ("Счет амортизации", "depreciation_account", "String", true, null),
                    ("Затратный счет", "expense_account", "String", false, null),
                    ("Активен", "is_active", "Bool", true, null)));

            await EnsureObjectAsync("Группы ОС", "catalog_asset_groups", "Catalog",
                "Группы основных средств и закрепленные счета", "🗂", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Материальный счет", "asset_account", "String", false, null),
                    ("Счет амортизации", "depreciation_account", "String", false, null),
                    ("Срок использования, мес.", "useful_life_months", "Int", false, null),
                    ("Активен", "is_active", "Bool", true, null)));

            await EnsureObjectAsync("Подгруппы ОС", "catalog_asset_subgroups", "Catalog",
                "Подгруппы основных средств", "🗃", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Группа ОС", "asset_group_id", "Reference", true, "Группы ОС"),
                    ("Срок использования, мес.", "useful_life_months", "Int", false, null),
                    ("Активен", "is_active", "Bool", true, null)));

            await EnsureObjectAsync("Виды ОС", "catalog_asset_types", "Catalog",
                "Классификация основных средств по видам", "🏷", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Подгруппа ОС", "asset_subgroup_id", "Reference", false, "Подгруппы ОС"),
                    ("Налоговая группа", "tax_group", "String", false, null),
                    ("Активен", "is_active", "Bool", true, null)));
        }

        private async Task ExtendFixedAssetCardAsync()
        {
            var asset = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            if (asset == null)
                return;

            var additions = new[]
            {
                Field(asset.Id, "Подгруппа ОС", "asset_subgroup_id", "Reference", 20, false, "Подгруппы ОС"),
                Field(asset.Id, "Вид ОС", "asset_type_id", "Reference", 21, false, "Виды ОС"),
                Field(asset.Id, "Затратный счет", "expense_account", "String", 22),
                Field(asset.Id, "Месячная амортизация", "monthly_depreciation", "Decimal", 23),
                Field(asset.Id, "Налоговая группа", "tax_group", "String", 24),
                Field(asset.Id, "Дата консервации", "conservation_date", "DateTime", 25),
                Field(asset.Id, "Дата расконсервации", "reopening_date", "DateTime", 26)
            };
            foreach (var field in additions.Where(candidate => asset.Fields.All(existing => existing.DbColumnName != candidate.DbColumnName)))
            {
                await _context.MetadataFields.AddAsync(field);
                var sqlType = field.FieldType switch
                {
                    "Reference" => "uuid", "Decimal" => "numeric(18,2)", "DateTime" => "timestamp", _ => "varchar(200)"
                };
                await _context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"{asset.TableName}\" ADD COLUMN IF NOT EXISTS \"{field.DbColumnName}\" {sqlType};");
            }
            await _context.SaveChangesAsync();
        }

        private async Task EnsureDocumentsAsync()
        {
            foreach (var name in ModuleMetadataService.FixedAssetDocumentNames)
                await EnsureObjectAsync(name, $"doc_asset_{Slug(name)}", "Document",
                    $"Документ модуля основных средств: {name}", "🏗", FixedAssetDocumentFields());

            foreach (var name in new[] { "Платежная ведомость", "Доверенность", "Авансовый отчет", "Расчет курсовой разницы" })
                await EnsureObjectAsync(name, $"doc_fin_{Slug(name)}", "Document",
                    $"Документ финансового учета: {name}", "💰", FinanceDocumentFields(name));

            foreach (var name in new[]
            {
                "Внутреннее перемещение ТМЦ", "Приход из производства ТМЦ", "Расход в производство",
                "Передача ТМЦ в подотчет", "Инвентаризация ТМЦ"
            })
                await EnsureObjectAsync(name, $"doc_inventory_{Slug(name)}", "Document",
                    $"Документ учета материальных ценностей: {name}", "📦", InventoryDocumentFields());
        }

        private async Task EnsureFixedAssetReportsAsync()
        {
            var source = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            if (source == null)
                return;

            await EnsureReportAsync("Ведомость основных средств", "assets.statement", source,
                "Инвентарный номер", "Наименование", "Организация", "МОЛ", "Участок",
                "Первоначальная стоимость", "Накопленная амортизация", "Остаточная стоимость", "Статус");
            await EnsureReportAsync("Оборотная ведомость по ОС", "assets.turnover", source,
                "Инвентарный номер", "Наименование", "Дата приобретения", "Первоначальная стоимость",
                "Накопленная амортизация", "Остаточная стоимость", "МОЛ", "Участок");
            await EnsureReportAsync("Ведомость ОС по счету", "assets.by.account", source,
                "Счет учета", "Инвентарный номер", "Наименование", "Первоначальная стоимость",
                "Накопленная амортизация", "Остаточная стоимость");
            await EnsureReportAsync("Ведомость амортизации", "assets.depreciation", source,
                "Инвентарный номер", "Наименование", "Счет амортизации", "Затратный счет",
                "Первоначальная стоимость", "Накопленная амортизация", "Месячная амортизация");
            await EnsureReportAsync("Приход ОС за период", "assets.receipts", source,
                "Инвентарный номер", "Наименование", "Дата приобретения", "Дата ввода в эксплуатацию",
                "Первоначальная стоимость", "Организация", "МОЛ", "Участок");
            await EnsureReportAsync("Расшифровка баланса по ОС", "assets.balance.details", source,
                "Счет учета", "Инвентарный номер", "Наименование", "Первоначальная стоимость",
                "Накопленная амортизация", "Остаточная стоимость");
        }

        private async Task EnsureFinanceReportsAsync()
        {
            var paymentOrder = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Платежное поручение");
            if (paymentOrder != null)
            {
                await EnsureReportAsync("Реестр платежных поручений", "finance.payment.registry", paymentOrder,
                    "Номер", "Дата", "Тип", "Организация", "Сумма", "Валюта", "Назначение платежа", "Проведён");
                await EnsureReportAsync("Выписка банка", "finance.bank.statement", paymentOrder,
                    "Дата", "Номер", "Тип", "Организация", "Корр. счет", "Сумма", "Валюта", "Назначение платежа");
            }
            var advanceReport = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Авансовый отчет");
            if (advanceReport != null)
                await EnsureReportAsync("Акт сверки по подотчетному лицу", "finance.employee.reconciliation", advanceReport,
                    "Дата", "Номер", "Сотрудник", "Организация", "Сумма", "Основание", "Проведен");
        }

        private async Task EnsureInventoryReportsAsync()
        {
            var materials = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Справочник материалов");
            if (materials != null)
                await EnsureReportAsync("Ведомость наличия материалов", "inventory.materials.available", materials,
                    "Код", "Наименование материала", "Ед изм", "Ном номер", "Вид материала", "Счет хранения", "Активен");
        }

        private async Task EnsureReportAsync(string name, string code, MetadataObject source, params string[] fieldNames)
        {
            if (await _context.Reports.AnyAsync(report => report.Code == code || report.Name == name))
                return;
            var report = new Report
            {
                Name = name, Code = code, Description = $"Настраиваемый отчет: {name}",
                DataSourceType = "Catalog", DataSourceId = source.Id, ReportType = "Table",
                Icon = "📊", IsActive = true, SourceFormat = "Native", Order = 100,
                TitleText = name, PageOrientation = "Landscape"
            };
            var order = 1;
            foreach (var fieldName in fieldNames)
            {
                var metadataField = source.Fields.FirstOrDefault(field => field.Name == fieldName);
                if (metadataField == null)
                    continue;
                report.Fields.Add(new ReportField
                {
                    ReportId = report.Id, FieldName = metadataField.DbColumnName,
                    DisplayName = metadataField.Name, Order = order++, Width = 120, IsVisible = true
                });
            }
            await _context.Reports.AddAsync(report);
            await _context.SaveChangesAsync();
        }

        private async Task EnsureObjectAsync(
            string name,
            string tableName,
            string objectType,
            string description,
            string icon,
            IReadOnlyCollection<MetadataField> fields)
        {
            var existing = await _context.MetadataObjects.Include(item => item.Fields).Include(item => item.PostingRules)
                .FirstOrDefaultAsync(item => item.ObjectType == objectType && item.Name == name);
            if (existing != null)
            {
                if (objectType == "Document")
                    await EnsureGenericPostingRuleAsync(existing);
                await _metadataService.CreateDynamicTableAsync(existing);
                return;
            }

            var configId = await _context.MetadataConfigurations.Select(item => (Guid?)item.Id).FirstOrDefaultAsync();
            var obj = new MetadataObject
            {
                Name = name, TableName = tableName, ObjectType = objectType, Description = description,
                Icon = icon, Order = await NextOrderAsync(objectType), IsSystem = true, MetadataConfigId = configId,
                UsePostings = objectType == "Document", Fields = new List<MetadataField>()
            };
            foreach (var sourceField in fields)
            {
                sourceField.MetadataObjectId = obj.Id;
                obj.Fields.Add(sourceField);
            }
            if (objectType == "Document")
                obj.PostingRules.Add(CreateGenericPostingRule(obj.Id));
            await _context.MetadataObjects.AddAsync(obj);
            await _context.SaveChangesAsync();
            await _metadataService.CreateDynamicTableAsync(obj);
        }

        private async Task EnsureGenericPostingRuleAsync(MetadataObject document)
        {
            if (document.PostingRules.Count > 0)
                return;
            document.UsePostings = true;
            document.PostingRules.Add(CreateGenericPostingRule(document.Id));
            await _context.SaveChangesAsync();
        }

        private static MetadataPostingRule CreateGenericPostingRule(Guid metadataObjectId) => new()
        {
            MetadataObjectId = metadataObjectId,
            Name = "Проводка по указанным счетам",
            DebitAccountExpression = "{debit_account}",
            CreditAccountExpression = "{credit_account}",
            AmountExpression = "{amount}",
            Order = 1
        };

        private async Task<int> NextOrderAsync(string objectType) =>
            (await _context.MetadataObjects.Where(item => item.ObjectType == objectType)
                .Select(item => (int?)item.Order).MaxAsync() ?? 0) + 1;

        private static List<MetadataField> CatalogFields(params (string Name, string Column, string Type, bool Required, string? Reference)[] fields) =>
            fields.Select((item, index) => Field(Guid.Empty, item.Name, item.Column, item.Type, index + 1, item.Required, item.Reference)).ToList();

        private static List<MetadataField> FixedAssetDocumentFields()
        {
            var fields = StandardDocumentFields();
            fields.Insert(2, Field(Guid.Empty, "Основное средство", "asset_id", "Reference", 3, true, "Основные средства"));
            fields.Add(Field(Guid.Empty, "Сумма амортизации", "depreciation_amount", "Decimal", 20));
            fields.Add(Field(Guid.Empty, "Новый затратный счет", "new_expense_account", "String", 21));
            fields.Add(Field(Guid.Empty, "Дата окончания", "end_date", "DateTime", 22));
            Reorder(fields);
            return fields;
        }

        private static List<MetadataField> FinanceDocumentFields(string name)
        {
            var fields = StandardDocumentFields();
            fields.Insert(2, Field(Guid.Empty, "Сотрудник", "employee_id", "Reference", 3, name is "Платежная ведомость" or "Авансовый отчет", "Сотрудники (Списочный состав)"));
            fields.Add(Field(Guid.Empty, "Валюта", "currency_id", "Reference", 20, false, "Справочник валют"));
            fields.Add(Field(Guid.Empty, "Курс", "exchange_rate", "Decimal", 21));
            fields.Add(Field(Guid.Empty, "Срок действия", "valid_until", "DateTime", 22));
            Reorder(fields);
            return fields;
        }

        private static List<MetadataField> InventoryDocumentFields()
        {
            var fields = StandardDocumentFields();
            fields.Insert(2, Field(Guid.Empty, "Материал", "material_id", "Reference", 3, true, "Справочник материалов"));
            fields.Add(Field(Guid.Empty, "Количество", "quantity", "Decimal", 20, true));
            fields.Add(Field(Guid.Empty, "Цена", "price", "Decimal", 21));
            fields.Add(Field(Guid.Empty, "Участок-получатель", "destination_site_id", "Reference", 22, false, "Участки"));
            fields.Add(Field(Guid.Empty, "МОЛ-получатель", "destination_person_id", "Reference", 23, false, "МОЛ"));
            Reorder(fields);
            return fields;
        }

        private static List<MetadataField> StandardDocumentFields() => new()
        {
            Field(Guid.Empty, "Номер", "doc_number", "String", 1, true),
            Field(Guid.Empty, "Дата", "doc_date", "DateTime", 2, true),
            Field(Guid.Empty, "Организация", "organization_id", "Reference", 3, false, "Организации"),
            Field(Guid.Empty, "МОЛ", "responsible_person_id", "Reference", 4, false, "МОЛ"),
            Field(Guid.Empty, "Участок", "site_id", "Reference", 5, false, "Участки"),
            Field(Guid.Empty, "Сумма", "amount", "Decimal", 6),
            Field(Guid.Empty, "Счет дебета", "debit_account", "Reference", 7, false, "План счетов"),
            Field(Guid.Empty, "Счет кредита", "credit_account", "Reference", 8, false, "План счетов"),
            Field(Guid.Empty, "Основание", "basis", "String", 9),
            Field(Guid.Empty, "Примечание", "description", "String", 10),
            Field(Guid.Empty, "Проведен", "is_posted", "Bool", 11)
        };

        private static MetadataField Field(
            Guid metadataObjectId,
            string name,
            string column,
            string type,
            int order,
            bool required = false,
            string? reference = null) => new()
        {
            Name = name, DbColumnName = column, FieldType = type, Order = order,
            IsRequired = required, ReferenceCatalog = reference, MetadataObjectId = metadataObjectId,
            Length = type == "String" ? 500 : 0, Precision = 18, Scale = 2
        };

        private static void Reorder(IList<MetadataField> fields)
        {
            for (var index = 0; index < fields.Count; index++)
                fields[index].Order = index + 1;
        }

        private static string Slug(string value)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
        }
    }
}
