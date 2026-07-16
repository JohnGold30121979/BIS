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
            await EnsureFixedAssetSupportCatalogsAsync();
            await SeedFixedAssetSupportCatalogDataAsync();
            await ExtendFixedAssetCardAsync();
            await EnsureFixedAssetCanonicalModelAsync();
            await EnsureDocumentsAsync();
            await EnsureFixedAssetPeriodBalanceReportSourceAsync();
            await EnsureFixedAssetReportsAsync();
            await EnsureFinanceReportsAsync();
            await EnsureInventoryReportsAsync();
            await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
            await _metadataService.EnsureStandardReportsAsync();
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
                    ("Налоговая группа", "tax_group", "Reference", false, "Налоговые группы ОС"),
                    ("Активен", "is_active", "Bool", true, null)));
        }

        private async Task EnsureFixedAssetSupportCatalogsAsync()
        {
            await EnsureObjectAsync("Методы амортизации ОС", "catalog_asset_depreciation_methods", "Catalog",
                "Настройки методов начисления амортизации основных средств", "📐", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Тип расчета", "calculation_type", "String", true, null),
                    ("По умолчанию", "is_default", "Bool", false, null),
                    ("Активен", "is_active", "Bool", true, null),
                    ("Описание", "description", "String", false, null)));

            await EnsureObjectAsync("Статусы ОС", "catalog_asset_statuses", "Catalog",
                "Жизненный цикл основных средств", "🚦", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Этап", "lifecycle_stage", "Int", false, null),
                    ("По умолчанию", "is_default", "Bool", false, null),
                    ("Активен", "is_active", "Bool", true, null),
                    ("Описание", "description", "String", false, null)));

            await EnsureObjectAsync("Налоговые группы ОС", "catalog_asset_tax_groups", "Catalog",
                "Налоговые признаки основных средств", "🏷", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Активен", "is_active", "Bool", true, null),
                    ("Описание", "description", "String", false, null)));

            await EnsureObjectAsync("Параметры контура ОС", "catalog_asset_module_settings", "Catalog",
                "Fox-совместимые параметры и переключатели контура ОС", "⚙", CatalogFields(
                    ("Код", "code", "String", true, null),
                    ("Наименование", "name", "String", true, null),
                    ("Строковое значение", "string_value", "String", false, null),
                    ("Счет", "account_value", "Reference", false, "План счетов"),
                    ("Булево значение", "bool_value", "Bool", false, null),
                    ("Источник Fox", "fox_source", "String", false, null),
                    ("Описание", "description", "String", false, null),
                    ("Активен", "is_active", "Bool", true, null)));
        }

        private async Task SeedFixedAssetSupportCatalogDataAsync()
        {
            await EnsureCatalogRowsAsync("Методы амортизации ОС", new[]
            {
                Row(
                    ("Код", "LINEAR"),
                    ("Наименование", "Линейный"),
                    ("Тип расчета", "Depreciation"),
                    ("По умолчанию", true),
                    ("Активен", true),
                    ("Описание", "Месячная сумма определяется по стоимости и сроку полезного использования.")),
                Row(
                    ("Код", "RATE"),
                    ("Наименование", "По норме амортизации"),
                    ("Тип расчета", "Depreciation"),
                    ("По умолчанию", false),
                    ("Активен", true),
                    ("Описание", "Месячная сумма определяется по процентной норме амортизации."))
            });

            await EnsureCatalogRowsAsync("Статусы ОС", new[]
            {
                Row(
                    ("Код", "RECEIVED"),
                    ("Наименование", "Поступило"),
                    ("Этап", 10),
                    ("По умолчанию", false),
                    ("Активен", true),
                    ("Описание", "ОС поступило, но еще не введено в эксплуатацию.")),
                Row(
                    ("Код", "ACTIVE"),
                    ("Наименование", "В эксплуатации"),
                    ("Этап", 20),
                    ("По умолчанию", true),
                    ("Активен", true),
                    ("Описание", "Основное средство введено в эксплуатацию.")),
                Row(
                    ("Код", "CONSERVATION"),
                    ("Наименование", "На консервации"),
                    ("Этап", 30),
                    ("По умолчанию", false),
                    ("Активен", true),
                    ("Описание", "Основное средство временно не используется.")),
                Row(
                    ("Код", "DISPOSED"),
                    ("Наименование", "Выбыло"),
                    ("Этап", 40),
                    ("По умолчанию", false),
                    ("Активен", true),
                    ("Описание", "Основное средство реализовано, ликвидировано или списано."))
            });

            await EnsureCatalogRowsAsync("Налоговые группы ОС", new[]
            {
                Row(
                    ("Код", "GENERAL"),
                    ("Наименование", "Общая группа"),
                    ("Активен", true),
                    ("Описание", "Базовая налоговая группа для учета ОС.")),
                Row(
                    ("Код", "EXEMPT"),
                    ("Наименование", "Необлагаемая группа"),
                    ("Активен", true),
                    ("Описание", "Используется для ОС с льготным или необлагаемым режимом."))
            });

            await EnsureCatalogRowsAsync("Параметры контура ОС", new[]
            {
                Row(
                    ("Код", "FORM_PR"),
                    ("Наименование", "Форма печатного отчета"),
                    ("Строковое значение", "prd1n"),
                    ("Булево значение", false),
                    ("Источник Fox", "f_osn.FORM_PR"),
                    ("Описание", "Fox-параметр выбора печатной формы отчетности по ОС."),
                    ("Активен", true)),
                Row(
                    ("Код", "PRIZ_RASHET"),
                    ("Наименование", "Признак расчета"),
                    ("Булево значение", false),
                    ("Источник Fox", "f_osn.PRIZ_RASHET"),
                    ("Описание", "Fox-переключатель режима расчета в контуре ОС."),
                    ("Активен", true)),
                Row(
                    ("Код", "SCH_NALP"),
                    ("Наименование", "Счет налогового платежа"),
                    ("Источник Fox", "sp_nal.SCH_NALP / F_OSN3"),
                    ("Описание", "Fox-параметр счета налогового платежа, используемый связанными отчетами."),
                    ("Активен", true))
            });
        }

        private async Task ExtendFixedAssetCardAsync()
        {
            var asset = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            if (asset == null)
                return;

            var additions = new[]
            {
                Field(asset.Id, "Подгруппа ОС", "asset_subgroup_id", "Reference", 5, false, "Подгруппы ОС"),
                Field(asset.Id, "Вид ОС", "asset_type_id", "Reference", 6, false, "Виды ОС"),
                Field(asset.Id, "Дата начала амортизации", "depreciation_start_date", "DateTime", 9),
                Field(asset.Id, "Ликвидационная стоимость", "salvage_value", "Decimal", 11),
                Field(asset.Id, "Месячная амортизация", "monthly_depreciation", "Decimal", 17),
                Field(asset.Id, "Амортизация по пробегу", "use_mileage_depreciation", "Bool", 18),
                Field(asset.Id, "Месячный пробег", "monthly_mileage", "Decimal", 19),
                Field(asset.Id, "Ресурс пробега", "mileage_resource", "Decimal", 20),
                Field(asset.Id, "Затратный счет", "expense_account", "Reference", 23, false, "План счетов"),
                Field(asset.Id, "Налоговая группа", "tax_group", "Reference", 24, false, "Налоговые группы ОС"),
                Field(asset.Id, "Класс ОС", "asset_class", "Int", 28),
                Field(asset.Id, "Дата консервации", "conservation_date", "DateTime", 31),
                Field(asset.Id, "Дата расконсервации", "reopening_date", "DateTime", 32),
                Field(asset.Id, "Дата выбытия", "disposal_date", "DateTime", 33)
            };
            foreach (var field in additions.Where(candidate => asset.Fields.All(existing => existing.DbColumnName != candidate.DbColumnName)))
            {
                await _context.MetadataFields.AddAsync(field);
                await _context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE \"{asset.TableName}\" ADD COLUMN IF NOT EXISTS \"{field.DbColumnName}\" {GetSqlType(field)};");
            }
            await _context.SaveChangesAsync();
        }

        private async Task EnsureFixedAssetCanonicalModelAsync()
        {
            var asset = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            if (asset == null)
                return;

            const string chartPattern = "{Код} - {Наименование}";
            const string chartFields = "Код,Наименование";

            await EnsureMetadataFieldAsync(asset,
                Field(asset.Id, "Норма амортизации, %", "depreciation_rate", "Decimal", 12));

            ConfigureField(asset, "code", type: "String", order: 1, required: true, unique: true, length: 50);
            ConfigureField(asset, "inventory_number", type: "String", order: 2, required: true, unique: true, length: 50);
            ConfigureField(asset, "name", type: "String", order: 3, required: true, length: 300);
            ConfigureField(asset, "asset_group", type: "Reference", order: 4, reference: "Группы ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "asset_subgroup_id", type: "Reference", order: 5, reference: "Подгруппы ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "asset_type_id", type: "Reference", order: 6, reference: "Виды ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "acquisition_date", type: "DateTime", order: 7);
            ConfigureField(asset, "commissioning_date", type: "DateTime", order: 8);
            ConfigureField(asset, "depreciation_start_date", type: "DateTime", order: 9);
            ConfigureField(asset, "initial_cost", type: "Decimal", order: 10);
            ConfigureField(asset, "salvage_value", type: "Decimal", order: 11);
            ConfigureField(asset, "accumulated_depreciation", type: "Decimal", order: 12);
            ConfigureField(asset, "carrying_amount", type: "Decimal", order: 13);
            ConfigureField(asset, "useful_life_months", type: "Int", order: 14);
            ConfigureField(asset, "depreciation_method", type: "Reference", order: 15, reference: "Методы амортизации ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "depreciation_rate", type: "Decimal", order: 16);
            ConfigureField(asset, "monthly_depreciation", type: "Decimal", order: 17);
            ConfigureField(asset, "use_mileage_depreciation", type: "Bool", order: 18);
            ConfigureField(asset, "monthly_mileage", type: "Decimal", order: 19);
            ConfigureField(asset, "mileage_resource", type: "Decimal", order: 20);
            ConfigureField(asset, "asset_account", type: "Reference", order: 21, reference: "План счетов",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "depreciation_account", type: "Reference", order: 22, reference: "План счетов",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "expense_account", type: "Reference", order: 23, reference: "План счетов",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "tax_group", type: "Reference", order: 24, reference: "Налоговые группы ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "organization_id", type: "Reference", order: 25, reference: "Организации");
            ConfigureField(asset, "responsible_person_id", type: "Reference", order: 26, reference: "МОЛ");
            ConfigureField(asset, "site_id", type: "Reference", order: 27, reference: "Участки");
            ConfigureField(asset, "asset_class", type: "Int", order: 28);
            ConfigureField(asset, "status", type: "Reference", order: 29, reference: "Статусы ОС",
                displayPattern: chartPattern, displayFields: chartFields);
            ConfigureField(asset, "is_active", type: "Bool", order: 30, required: true);
            ConfigureField(asset, "conservation_date", type: "DateTime", order: 31);
            ConfigureField(asset, "reopening_date", type: "DateTime", order: 32);
            ConfigureField(asset, "disposal_date", type: "DateTime", order: 33);
            ConfigureField(asset, "description", type: "String", order: 34, length: 500);

            await EnsureFixedAssetAutoCalculationsAsync(asset);
            await _context.SaveChangesAsync();
        }

        private async Task EnsureDocumentsAsync()
        {
            foreach (var name in ModuleMetadataService.FixedAssetDocumentNames)
                await EnsureObjectAsync(name, $"doc_asset_{Slug(name)}", "Document",
                    $"Документ модуля основных средств: {name}", "🏗", FixedAssetDocumentFields(name));

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
            var currentCardSource = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Основные средства");
            var periodBalanceSource = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "ReportSource" && item.Name == "Снимки остатков ОС");
            if (currentCardSource == null || periodBalanceSource == null)
                return;

            await EnsureReportAsync("Ведомость основных средств", "assets.statement", periodBalanceSource,
                "Период конец", "Инвентарный номер", "Наименование", "Организация", "МОЛ", "Участок",
                "Первоначальная стоимость", "Ликвидационная стоимость", "Накопленная амортизация",
                "Остаточная стоимость", "Статус");
            await EnsureReportAsync("Оборотная ведомость по ОС", "assets.turnover", periodBalanceSource,
                "Период начало", "Период конец", "Инвентарный номер", "Наименование",
                "Стоимость на начало", "Амортизация на начало", "Остаточная стоимость на начало",
                "Поступление", "Выбытие", "Внутреннее поступление", "Внутреннее выбытие",
                "Переоценка стоимости", "Автоматическая амортизация", "Ручная амортизация",
                "Списание амортизации", "Амортизация выбывших ОС",
                "Стоимость на конец", "Амортизация на конец", "Остаточная стоимость на конец",
                "МОЛ", "Участок");
            await EnsureReportAsync("Ведомость ОС по счету", "assets.by.account", periodBalanceSource,
                "Период конец", "Счет учета", "Инвентарный номер", "Наименование", "Первоначальная стоимость",
                "Накопленная амортизация", "Остаточная стоимость");
            await EnsureReportAsync("Ведомость амортизации", "assets.depreciation", periodBalanceSource,
                "Период конец", "Инвентарный номер", "Наименование", "Счет амортизации", "Затратный счет",
                "Стоимость на начало", "Амортизация на начало", "Автоматическая амортизация",
                "Ручная амортизация", "Корректировка амортизации", "Списание амортизации",
                "Амортизация на конец", "Остаточная стоимость на конец", "Месячная амортизация");
            await EnsureReportAsync("Приход ОС за период", "assets.receipts", periodBalanceSource,
                "Инвентарный номер", "Наименование", "Дата приобретения", "Дата ввода в эксплуатацию",
                "Дата начала амортизации", "Поступление", "Стоимость на конец", "Ликвидационная стоимость",
                "Организация", "МОЛ", "Участок");
            await EnsureReportAsync("Расшифровка баланса по ОС", "assets.balance.details", periodBalanceSource,
                "Период конец", "Счет учета", "Инвентарный номер", "Наименование", "Стоимость на конец",
                "Амортизация на конец", "Остаточная стоимость на конец");
            await EnsureReportAsync("Контроль состояния карточек ОС", "assets.control", currentCardSource,
                "Инвентарный номер", "Наименование", "Статус", "Активен", "Счет учета",
                "Счет амортизации", "Затратный счет", "Первоначальная стоимость",
                "Накопленная амортизация", "Остаточная стоимость", "Месячная амортизация",
                "Ликвидационная стоимость", "Дата начала амортизации", "Класс ОС",
                "Дата консервации", "Дата расконсервации", "Дата выбытия");

            var depreciationDocument = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Начисление амортизации");
            if (depreciationDocument != null)
                await EnsureReportAsync("Журнал начисления амортизации ОС", "assets.depreciation.journal", depreciationDocument,
                    "Номер", "Дата", "Основное средство", "Организация", "Сумма", "Сумма амортизации",
                    "Счет дебета", "Счет кредита", "Основание", "Проведен");

            var revaluationDocument = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Переоценка ОС");
            if (revaluationDocument != null)
                await EnsureReportAsync("Журнал переоценки ОС", "assets.revaluation.journal", revaluationDocument,
                    "Номер", "Дата", "Основное средство", "Организация", "Сумма", "Счет дебета", "Счет кредита", "Примечание", "Проведен");

            var realizationDocument = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Реализация ОС");
            if (realizationDocument != null)
                await EnsureReportAsync("Журнал реализации ОС", "assets.sale.journal", realizationDocument,
                    "Номер", "Дата", "Основное средство", "Организация", "Сумма", "Счет дебета", "Счет кредита", "Дата выбытия", "Проведен");

            var liquidationDocument = await _context.MetadataObjects.Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Ликвидация ОС");
            if (liquidationDocument != null)
                await EnsureReportAsync("Журнал ликвидации ОС", "assets.liquidation.journal", liquidationDocument,
                    "Номер", "Дата", "Основное средство", "Организация", "Сумма", "Счет дебета", "Счет кредита", "Дата выбытия", "Проведен");
        }

        private async Task EnsureFixedAssetPeriodBalanceReportSourceAsync()
        {
            await new AccountingPeriodService(_context).EnsureSchemaAsync();
            await EnsureObjectAsync("Снимки остатков ОС", "FixedAssetPeriodBalances", "ReportSource",
                "Системный источник отчетов ОС по зафиксированным остаткам периода", "📌",
                new List<MetadataField>
                {
                    Field(Guid.Empty, "Период", "PeriodId", "Reference", 1),
                    Field(Guid.Empty, "Период начало", "PeriodStart", "DateTime", 2),
                    Field(Guid.Empty, "Период конец", "PeriodEnd", "DateTime", 3),
                    Field(Guid.Empty, "Идентификатор ОС", "AssetId", "Reference", 4),
                    Field(Guid.Empty, "Инвентарный номер", "InventoryNumber", "String", 5),
                    Field(Guid.Empty, "Наименование", "AssetName", "String", 6),
                    Field(Guid.Empty, "Организация", "OrganizationName", "String", 7),
                    Field(Guid.Empty, "МОЛ", "ResponsiblePersonName", "String", 8),
                    Field(Guid.Empty, "Участок", "SiteName", "String", 9),
                    Field(Guid.Empty, "Счет учета", "AssetAccount", "String", 10),
                    Field(Guid.Empty, "Счет амортизации", "DepreciationAccount", "String", 11),
                    Field(Guid.Empty, "Затратный счет", "ExpenseAccount", "String", 12),
                    Field(Guid.Empty, "Дата приобретения", "AcquisitionDate", "DateTime", 13),
                    Field(Guid.Empty, "Дата ввода в эксплуатацию", "CommissioningDate", "DateTime", 14),
                    Field(Guid.Empty, "Дата начала амортизации", "DepreciationStartDate", "DateTime", 15),
                    Field(Guid.Empty, "Первоначальная стоимость", "InitialCost", "Decimal", 16),
                    Field(Guid.Empty, "Ликвидационная стоимость", "SalvageValue", "Decimal", 17),
                    Field(Guid.Empty, "Накопленная амортизация", "AccumulatedDepreciation", "Decimal", 18),
                    Field(Guid.Empty, "Остаточная стоимость", "CarryingAmount", "Decimal", 19),
                    Field(Guid.Empty, "Месячная амортизация", "MonthlyDepreciation", "Decimal", 20),
                    Field(Guid.Empty, "Активен", "IsActive", "Bool", 21),
                    Field(Guid.Empty, "Статус", "LifecycleStatus", "String", 22),
                    Field(Guid.Empty, "Стоимость на начало", "OpeningCost", "Decimal", 23),
                    Field(Guid.Empty, "Амортизация на начало", "OpeningDepreciation", "Decimal", 24),
                    Field(Guid.Empty, "Остаточная стоимость на начало", "OpeningCarryingAmount", "Decimal", 25),
                    Field(Guid.Empty, "Поступление", "AcquisitionCost", "Decimal", 26),
                    Field(Guid.Empty, "Выбытие", "DisposalCost", "Decimal", 27),
                    Field(Guid.Empty, "Внутреннее поступление", "TransferInCost", "Decimal", 28),
                    Field(Guid.Empty, "Внутреннее выбытие", "TransferOutCost", "Decimal", 29),
                    Field(Guid.Empty, "Переоценка стоимости", "RevaluationCost", "Decimal", 30),
                    Field(Guid.Empty, "Автоматическая амортизация", "AutomaticDepreciation", "Decimal", 31),
                    Field(Guid.Empty, "Ручная амортизация", "ManualDepreciation", "Decimal", 32),
                    Field(Guid.Empty, "Корректировка амортизации", "DepreciationAdjustment", "Decimal", 33),
                    Field(Guid.Empty, "Списание амортизации", "DepreciationWriteOff", "Decimal", 34),
                    Field(Guid.Empty, "Амортизация выбывших ОС", "DisposalDepreciation", "Decimal", 35),
                    Field(Guid.Empty, "Амортизация внутреннего поступления", "TransferInDepreciation", "Decimal", 36),
                    Field(Guid.Empty, "Амортизация внутреннего выбытия", "TransferOutDepreciation", "Decimal", 37),
                    Field(Guid.Empty, "Стоимость на конец", "ClosingCost", "Decimal", 38),
                    Field(Guid.Empty, "Амортизация на конец", "ClosingDepreciation", "Decimal", 39),
                    Field(Guid.Empty, "Остаточная стоимость на конец", "ClosingCarryingAmount", "Decimal", 40),
                    Field(Guid.Empty, "Пробег на начало", "OpeningMileage", "Decimal", 41),
                    Field(Guid.Empty, "Пробег за период", "PeriodMileage", "Decimal", 42),
                    Field(Guid.Empty, "Пробег на конец", "ClosingMileage", "Decimal", 43)
                });
        }

        private async Task EnsureFinanceReportsAsync()
        {
            await RemoveObsoleteFinanceNavigationReportsAsync();
        }

        private async Task RemoveObsoleteFinanceNavigationReportsAsync()
        {
            await new ModuleMetadataService(_context).EnsureSchemaAsync();

            var obsoleteCodes = new[]
            {
                "finance.payment.registry",
                "finance.bank.statement",
                "finance.employee.reconciliation"
            };
            var obsoleteNames = new[]
            {
                "Реестр платежных поручений",
                "Выписка банка",
                "Акт сверки по подотчетному лицу"
            };

            var reports = await _context.Reports
                .Where(report =>
                    obsoleteCodes.Contains(report.Code) ||
                    (obsoleteNames.Contains(report.Name) && !report.Code.StartsWith("standard.finance.")))
                .ToListAsync();
            if (reports.Count == 0)
                return;

            var reportIds = reports.Select(report => report.Id).ToList();
            var moduleItems = await _context.MetadataModuleItems
                .Where(item => item.ObjectType == "Report" && reportIds.Contains(item.ObjectId))
                .ToListAsync();

            _context.MetadataModuleItems.RemoveRange(moduleItems);
            _context.Reports.RemoveRange(reports);
            await _context.SaveChangesAsync();
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
            var existingReport = await _context.Reports
                .Include(report => report.Fields)
                .FirstOrDefaultAsync(report => report.Code == code || report.Name == name);
            if (existingReport != null)
            {
                var sourceChanged = existingReport.DataSourceId != source.Id ||
                                    !string.Equals(existingReport.DataSourceType, source.ObjectType, StringComparison.OrdinalIgnoreCase);
                existingReport.DataSourceType = source.ObjectType;
                existingReport.DataSourceId = source.Id;
                existingReport.SourceFormat = "Native";
                existingReport.UpdatedAt = DateTime.UtcNow;

                if (sourceChanged && existingReport.Fields.Count > 0)
                {
                    await _context.ReportFields
                        .Where(field => field.ReportId == existingReport.Id)
                        .ExecuteDeleteAsync();
                    foreach (var field in existingReport.Fields.ToList())
                        _context.Entry(field).State = EntityState.Detached;
                    existingReport.Fields.Clear();
                }

                await EnsureReportFieldsAsync(existingReport, source, fieldNames);
                await _context.SaveChangesAsync();
                return;
            }

            var report = new Report
            {
                Name = name, Code = code, Description = $"Настраиваемый отчет: {name}",
                DataSourceType = source.ObjectType, DataSourceId = source.Id, ReportType = "Table",
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

        private async Task EnsureReportFieldsAsync(Report report, MetadataObject source, params string[] fieldNames)
        {
            var existingFieldNames = report.Fields
                .Select(field => field.FieldName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nextOrder = (report.Fields.Select(field => (int?)field.Order).Max() ?? 0) + 1;

            foreach (var fieldName in fieldNames)
            {
                var metadataField = source.Fields.FirstOrDefault(field => field.Name == fieldName);
                if (metadataField == null || existingFieldNames.Contains(metadataField.DbColumnName))
                    continue;

                await _context.ReportFields.AddAsync(new ReportField
                {
                    ReportId = report.Id,
                    FieldName = metadataField.DbColumnName,
                    DisplayName = metadataField.Name,
                    Order = nextOrder++,
                    Width = 120,
                    IsVisible = true
                });
                existingFieldNames.Add(metadataField.DbColumnName);
            }
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
                await EnsureObjectFieldsAsync(existing, fields);
                if (objectType == "Document")
                    await EnsureGenericPostingRuleAsync(existing);
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

        private static List<MetadataField> FixedAssetDocumentFields(string documentName)
        {
            var fields = StandardDocumentFields();
            fields.Insert(2, Field(Guid.Empty, "Основное средство", "asset_id", "Reference", 3, true, "Основные средства"));

            static void AddDepreciationDetails(List<MetadataField> target, int startOrder)
            {
                target.Add(Field(Guid.Empty, "Ликвидационная стоимость", "salvage_value", "Decimal", startOrder));
                target.Add(Field(Guid.Empty, "Дата начала амортизации", "depreciation_start_date", "DateTime", startOrder + 1));
                target.Add(Field(Guid.Empty, "Амортизация по пробегу", "use_mileage_depreciation", "Bool", startOrder + 2));
                target.Add(Field(Guid.Empty, "Месячный пробег", "monthly_mileage", "Decimal", startOrder + 3));
                target.Add(Field(Guid.Empty, "Ресурс пробега", "mileage_resource", "Decimal", startOrder + 4));
                target.Add(Field(Guid.Empty, "Класс ОС", "asset_class", "Int", startOrder + 5));
            }

            switch (documentName)
            {
                case "Покупка ОС":
                case "Приход из производства ОС":
                    RequireDocumentPostingFields(fields);
                    fields.Add(Field(Guid.Empty, "Дата приобретения", "acquisition_date", "DateTime", 20));
                    if (documentName == "Покупка ОС")
                        fields.Add(Field(Guid.Empty, "Поставщик", "supplier_name", "String", 21));
                    fields.Add(Field(Guid.Empty, "Группа ОС", "asset_group_id", "Reference", 22, false, "Группы ОС"));
                    fields.Add(Field(Guid.Empty, "Подгруппа ОС", "asset_subgroup_id", "Reference", 23, false, "Подгруппы ОС"));
                    fields.Add(Field(Guid.Empty, "Вид ОС", "asset_type_id", "Reference", 24, false, "Виды ОС"));
                    fields.Add(Field(Guid.Empty, "Метод амортизации", "depreciation_method", "Reference", 25, false, "Методы амортизации ОС"));
                    fields.Add(Field(Guid.Empty, "Срок полезного использования, мес.", "useful_life_months", "Int", 26));
                    fields.Add(Field(Guid.Empty, "Норма амортизации, %", "depreciation_rate", "Decimal", 27));
                    fields.Add(Field(Guid.Empty, "Счет амортизации", "depreciation_account", "Reference", 28, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Затратный счет", "expense_account", "Reference", 29, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Налоговая группа", "tax_group", "Reference", 30, false, "Налоговые группы ОС"));
                    AddDepreciationDetails(fields, 31);
                    break;
                case "Ввод ОС в эксплуатацию":
                    RequireDocumentPostingFields(fields);
                    fields.Add(Field(Guid.Empty, "Дата ввода в эксплуатацию", "commissioning_date", "DateTime", 20));
                    fields.Add(Field(Guid.Empty, "Метод амортизации", "depreciation_method", "Reference", 21, false, "Методы амортизации ОС"));
                    fields.Add(Field(Guid.Empty, "Срок полезного использования, мес.", "useful_life_months", "Int", 22));
                    fields.Add(Field(Guid.Empty, "Норма амортизации, %", "depreciation_rate", "Decimal", 23));
                    fields.Add(Field(Guid.Empty, "Счет амортизации", "depreciation_account", "Reference", 24, true, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Затратный счет", "expense_account", "Reference", 25, true, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Налоговая группа", "tax_group", "Reference", 26, false, "Налоговые группы ОС"));
                    AddDepreciationDetails(fields, 27);
                    break;
                case "Начисление амортизации":
                    fields.Add(Field(Guid.Empty, "Сумма амортизации", "depreciation_amount", "Decimal", 20));
                    fields.Add(Field(Guid.Empty, "Счет амортизации", "depreciation_account", "Reference", 21, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Затратный счет", "expense_account", "Reference", 22, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Дата окончания", "end_date", "DateTime", 23));
                    break;
                case "Списание амортизации":
                    fields.Add(Field(Guid.Empty, "Сумма амортизации", "depreciation_amount", "Decimal", 20, true));
                    fields.Add(Field(Guid.Empty, "Дата окончания", "end_date", "DateTime", 21));
                    break;
                case "Переоценка ОС":
                    fields.Add(Field(Guid.Empty, "Сумма изменения стоимости", "cost_adjustment_amount", "Decimal", 20));
                    fields.Add(Field(Guid.Empty, "Сумма изменения амортизации", "depreciation_adjustment_amount", "Decimal", 21));
                    fields.Add(Field(Guid.Empty, "Счет переоценки", "revaluation_account", "Reference", 22, false, "План счетов"));
                    break;
                case "Смена затратного счета":
                    fields.Add(Field(Guid.Empty, "Новый затратный счет", "new_expense_account", "Reference", 20, true, "План счетов"));
                    break;
                case "Реализация ОС":
                case "Ликвидация ОС":
                case "Частичная реализация ОС":
                    fields.Add(Field(Guid.Empty, "Дата выбытия", "disposal_date", "DateTime", 20));
                    fields.Add(Field(Guid.Empty, "Сумма реализации", "proceeds_amount", "Decimal", 21));
                    fields.Add(Field(Guid.Empty, "Счет расчетов по реализации", "settlement_account", "Reference", 22, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Счет дохода от реализации", "disposal_income_account", "Reference", 23, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Счет списания остаточной стоимости", "disposal_expense_account", "Reference", 24, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Списываемая стоимость", "disposal_cost_amount", "Decimal", 25));
                    fields.Add(Field(Guid.Empty, "Списываемая амортизация", "disposal_depreciation_amount", "Decimal", 26));
                    fields.Add(Field(Guid.Empty, "Списываемая остаточная стоимость", "disposal_carrying_amount", "Decimal", 27));
                    break;
                case "Консервация ОС":
                case "Расконсервация ОС":
                    fields.Add(Field(Guid.Empty, "Дата окончания", "end_date", "DateTime", 20));
                    break;
                default:
                    fields.Add(Field(Guid.Empty, "Сумма амортизации", "depreciation_amount", "Decimal", 20));
                    fields.Add(Field(Guid.Empty, "Новый затратный счет", "new_expense_account", "Reference", 21, false, "План счетов"));
                    fields.Add(Field(Guid.Empty, "Дата окончания", "end_date", "DateTime", 22));
                    break;
            }

            Reorder(fields);
            return fields;
        }

        private static void RequireDocumentPostingFields(List<MetadataField> fields)
        {
            foreach (var field in fields.Where(field =>
                         field.DbColumnName is "amount" or "debit_account" or "credit_account"))
            {
                field.IsRequired = true;
            }
        }

        private static List<MetadataField> FinanceDocumentFields(string name)
        {
            var fields = name switch
            {
                "Авансовый отчет" => AdvanceReportFields(),
                "Доверенность" => PowerOfAttorneyFields(),
                "Платежная ведомость" => PayrollStatementFields(),
                "Расчет курсовой разницы" => ExchangeRateDifferenceDocumentFields(),
                _ => StandardDocumentFields()
            };

            Reorder(fields);
            return fields;
        }

        private static List<MetadataField> AdvanceReportFields()
        {
            var fields = StandardDocumentFields();
            RequireDocumentPostingFields(fields);
            fields.Insert(2, Field(Guid.Empty, "Сотрудник", "employee_id", "Reference", 3, true, "Сотрудники (Списочный состав)"));
            fields.Add(Field(Guid.Empty, "Вид авансового расчета", "advance_payment_id", "Reference", 20, true, "Авансовые платежи"));
            fields.Add(Field(Guid.Empty, "Дата начала отчета", "report_start_date", "DateTime", 21));
            fields.Add(Field(Guid.Empty, "Дата окончания отчета", "report_end_date", "DateTime", 22));
            fields.Add(Field(Guid.Empty, "Документ выдачи", "issue_document_number", "String", 23));
            fields.Add(Field(Guid.Empty, "Дата выдачи", "issue_document_date", "DateTime", 24));
            fields.Add(Field(Guid.Empty, "Валюта", "currency_id", "Reference", 25, false, "Справочник валют"));
            fields.Add(Field(Guid.Empty, "Курс", "exchange_rate", "Decimal", 26));
            fields.Add(Field(Guid.Empty, "Сумма в валюте", "amount_currency", "Decimal", 27));
            fields.Add(Field(Guid.Empty, "Принято к учету", "accepted_amount", "Decimal", 28));
            fields.Add(Field(Guid.Empty, "Перерасход", "overrun_amount", "Decimal", 29));
            fields.Add(Field(Guid.Empty, "Остаток к возврату", "return_amount", "Decimal", 30));
            return fields;
        }

        private static List<MetadataField> PowerOfAttorneyFields()
        {
            var fields = StandardDocumentFields();
            fields.Insert(2, Field(Guid.Empty, "Представитель", "representative_id", "Reference", 3, true, "Сотрудники (Списочный состав)"));
            fields.Add(Field(Guid.Empty, "Поставщик", "counterparty_id", "Reference", 20, false, "Организации"));
            fields.Add(Field(Guid.Empty, "Расчетный счет", "bank_account", "String", 21));
            fields.Add(Field(Guid.Empty, "Банк", "bank_name", "String", 22));
            fields.Add(Field(Guid.Empty, "Документ личности", "identity_document_name", "String", 23));
            fields.Add(Field(Guid.Empty, "Номер документа личности", "identity_document_number", "String", 24));
            fields.Add(Field(Guid.Empty, "Дата документа личности", "identity_document_date", "DateTime", 25));
            fields.Add(Field(Guid.Empty, "Кем выдан документ", "identity_document_issuer", "String", 26));
            fields.Add(Field(Guid.Empty, "Срок действия", "valid_until", "DateTime", 27, true));
            fields.Add(Field(Guid.Empty, "Документ-основание", "source_document_number", "String", 28));
            fields.Add(Field(Guid.Empty, "Дата документа-основания", "source_document_date", "DateTime", 29));
            fields.Add(Field(Guid.Empty, "Перечень ценностей", "items_description", "String", 30));
            fields.Add(Field(Guid.Empty, "Количество", "quantity", "Decimal", 31));
            fields.Add(Field(Guid.Empty, "Единица измерения", "unit_name", "String", 32));
            return fields;
        }

        private static List<MetadataField> PayrollStatementFields()
        {
            var fields = StandardDocumentFields();
            RequireDocumentPostingFields(fields);
            fields.Insert(2, Field(Guid.Empty, "Сотрудник", "employee_id", "Reference", 3, true, "Сотрудники (Списочный состав)"));
            fields.Add(Field(Guid.Empty, "Дата начала периода", "period_start_date", "DateTime", 20, true));
            fields.Add(Field(Guid.Empty, "Дата окончания периода", "period_end_date", "DateTime", 21, true));
            fields.Add(Field(Guid.Empty, "Счет выплаты", "payment_account", "Reference", 22, true, "План счетов"));
            fields.Add(Field(Guid.Empty, "Начислено", "accrued_amount", "Decimal", 23));
            fields.Add(Field(Guid.Empty, "Удержано", "withheld_amount", "Decimal", 24));
            fields.Add(Field(Guid.Empty, "К выплате", "payable_amount", "Decimal", 25));
            fields.Add(Field(Guid.Empty, "Валюта", "currency_id", "Reference", 26, false, "Справочник валют"));
            fields.Add(Field(Guid.Empty, "Курс", "exchange_rate", "Decimal", 27));
            fields.Add(Field(Guid.Empty, "Сумма в валюте", "amount_currency", "Decimal", 28));
            return fields;
        }

        private static List<MetadataField> ExchangeRateDifferenceDocumentFields()
        {
            var fields = StandardDocumentFields();
            fields.Add(Field(Guid.Empty, "Дата расчета", "calculation_date", "DateTime", 20, true));
            fields.Add(Field(Guid.Empty, "Дата начала периода", "period_start_date", "DateTime", 21));
            fields.Add(Field(Guid.Empty, "Дата окончания периода", "period_end_date", "DateTime", 22, true));
            fields.Add(Field(Guid.Empty, "Валюта", "currency_id", "Reference", 23, false, "Справочник валют"));
            fields.Add(Field(Guid.Empty, "Курс", "exchange_rate", "Decimal", 24));
            fields.Add(Field(Guid.Empty, "Валютный счет", "currency_account", "Reference", 25, false, "План счетов"));
            fields.Add(Field(Guid.Empty, "Счет дохода", "gain_account", "Reference", 26, false, "План счетов"));
            fields.Add(Field(Guid.Empty, "Счет расхода", "loss_account", "Reference", 27, false, "План счетов"));
            fields.Add(Field(Guid.Empty, "Обработано остатков", "processed_balances", "Int", 28));
            fields.Add(Field(Guid.Empty, "Создано проводок", "created_postings", "Int", 29));
            fields.Add(Field(Guid.Empty, "Сумма дохода", "gain_amount", "Decimal", 30));
            fields.Add(Field(Guid.Empty, "Сумма расхода", "loss_amount", "Decimal", 31));
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

        private async Task EnsureMetadataFieldAsync(MetadataObject metadata, MetadataField field)
        {
            if (metadata.Fields.Any(existing =>
                string.Equals(existing.DbColumnName, field.DbColumnName, StringComparison.OrdinalIgnoreCase)))
                return;

            field.MetadataObjectId = metadata.Id;
            metadata.Fields.Add(field);
            await _context.MetadataFields.AddAsync(field);
            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{metadata.TableName}\" ADD COLUMN IF NOT EXISTS \"{field.DbColumnName}\" {GetSqlType(field)};");
        }

        private async Task EnsureObjectFieldsAsync(
            MetadataObject metadata,
            IReadOnlyCollection<MetadataField> fields)
        {
            foreach (var sourceField in fields.OrderBy(field => field.Order))
            {
                var existingField = metadata.Fields.FirstOrDefault(field =>
                    string.Equals(field.DbColumnName, sourceField.DbColumnName, StringComparison.OrdinalIgnoreCase));
                if (existingField == null)
                {
                    var newField = CloneField(metadata.Id, sourceField);
                    metadata.Fields.Add(newField);
                    await _context.MetadataFields.AddAsync(newField);
                    await _context.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE \"{metadata.TableName}\" ADD COLUMN IF NOT EXISTS \"{newField.DbColumnName}\" {GetSqlType(newField)};");
                    continue;
                }

                ApplyFieldTemplate(existingField, sourceField);
            }

            await _context.SaveChangesAsync();
        }

        private static void ConfigureField(
            MetadataObject metadata,
            string columnName,
            string type,
            int order,
            bool required = false,
            bool unique = false,
            int? length = null,
            string? reference = null,
            string? displayPattern = null,
            string? displayFields = null)
        {
            var field = metadata.Fields.FirstOrDefault(existing =>
                string.Equals(existing.DbColumnName, columnName, StringComparison.OrdinalIgnoreCase));
            if (field == null)
                return;

            field.FieldType = type;
            field.Order = order;
            field.IsRequired = required;
            field.IsUnique = unique;
            field.ReferenceCatalog = reference;
            field.DisplayPattern = displayPattern;
            field.DisplayFields = displayFields;

            if (length.HasValue)
                field.Length = length.Value;
        }

        private async Task EnsureFixedAssetAutoCalculationsAsync(MetadataObject asset)
        {
            if (await _context.MetadataCalculations.AnyAsync(item =>
                    item.MetadataObjectId == asset.Id &&
                    item.TargetField == "monthly_depreciation"))
            {
                return;
            }

            await _context.MetadataCalculations.AddAsync(new MetadataCalculation
            {
                MetadataObjectId = asset.Id,
                Name = "Авторасчет месячной амортизации",
                TargetField = "monthly_depreciation",
                CalculationType = "Depreciation",
                IsAuto = true,
                ExecutionOrder = 1
            });
        }

        private async Task EnsureCatalogRowsAsync(string catalogName, IEnumerable<Dictionary<string, object>> rows)
        {
            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == catalogName);
            if (catalog == null)
                return;

            var existingRows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var existingCodes = existingRows
                .Select(row => row.GetValueOrDefault("Код")?.ToString() ?? row.GetValueOrDefault("code")?.ToString())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var code = row.GetValueOrDefault("Код")?.ToString();
                if (string.IsNullOrWhiteSpace(code) || existingCodes.Contains(code))
                    continue;

                await _metadataService.AddCatalogItemAsync(catalog.Id, row);
                existingCodes.Add(code);
            }
        }

        private static Dictionary<string, object> Row(params (string Key, object Value)[] values)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values)
                row[key] = value;
            return row;
        }

        private static MetadataField CloneField(Guid metadataObjectId, MetadataField source) => new()
        {
            MetadataObjectId = metadataObjectId,
            Name = source.Name,
            DbColumnName = source.DbColumnName,
            FieldType = source.FieldType,
            Length = source.Length,
            Precision = source.Precision,
            Scale = source.Scale,
            IsRequired = source.IsRequired,
            IsUnique = source.IsUnique,
            Order = source.Order,
            ReferenceCatalog = source.ReferenceCatalog,
            Formula = source.Formula,
            DisplayPattern = source.DisplayPattern,
            DisplayFields = source.DisplayFields
        };

        private static void ApplyFieldTemplate(MetadataField target, MetadataField source)
        {
            target.Name = source.Name;
            target.FieldType = source.FieldType;
            target.Length = source.Length;
            target.Precision = source.Precision;
            target.Scale = source.Scale;
            target.IsRequired = source.IsRequired;
            target.IsUnique = source.IsUnique;
            target.Order = source.Order;
            target.ReferenceCatalog = source.ReferenceCatalog;
            target.Formula = source.Formula;
            target.DisplayPattern = source.DisplayPattern;
            target.DisplayFields = source.DisplayFields;
        }

        private static string GetSqlType(MetadataField field) => field.FieldType switch
        {
            "Decimal" => "numeric(18,2)",
            "Int" => "integer",
            "DateTime" => "timestamp",
            "Bool" => "boolean",
            "Reference" => "uuid",
            _ => "varchar(200)"
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
