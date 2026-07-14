using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    public class ModuleMetadataService
    {
        public const string BalanceCode = "Balance";
        public const string FinanceCode = "Finance";
        public const string FixedAssetsCode = "FixedAssets";
        public const string InventoryCode = "Inventory";
        public const string PayrollCode = "Payroll";
        public const string SalesCode = "Sales";
        public const string RawMaterialsCode = "RawMaterials";
        public const string CostAccountingCode = "CostAccounting";

        private readonly AppDbContext _context;

        public ModuleMetadataService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""MetadataModules"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""Code"" varchar(80) NOT NULL,
                    ""Name"" varchar(160) NOT NULL,
                    ""Description"" varchar(600) NOT NULL DEFAULT '',
                    ""Icon"" varchar(20) NOT NULL DEFAULT '',
                    ""Order"" integer NOT NULL DEFAULT 0,
                    ""CloseOrder"" integer NOT NULL DEFAULT 100,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""ParticipatesInPeriodClose"" boolean NOT NULL DEFAULT true,
                    ""RequirePreviousModulesClosed"" boolean NOT NULL DEFAULT false,
                    ""IsSystem"" boolean NOT NULL DEFAULT false
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MetadataModules_Code"" ON ""MetadataModules"" (""Code"");
                ALTER TABLE ""MetadataModules"" ADD COLUMN IF NOT EXISTS ""CloseOrder"" integer NOT NULL DEFAULT 100;
                ALTER TABLE ""MetadataModules"" ADD COLUMN IF NOT EXISTS ""ParticipatesInPeriodClose"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""MetadataModules"" ADD COLUMN IF NOT EXISTS ""RequirePreviousModulesClosed"" boolean NOT NULL DEFAULT false;
                CREATE TABLE IF NOT EXISTS ""MetadataModuleItems"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""ModuleId"" uuid NOT NULL,
                    ""ObjectId"" uuid NOT NULL,
                    ""ObjectType"" varchar(30) NOT NULL,
                    ""Order"" integer NOT NULL DEFAULT 0
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_MetadataModuleItems_Object""
                    ON ""MetadataModuleItems"" (""ObjectType"", ""ObjectId"");
                CREATE INDEX IF NOT EXISTS ""IX_MetadataModuleItems_Module"" ON ""MetadataModuleItems"" (""ModuleId"");
            ");
        }

        public async Task EnsureDefaultModulesAsync()
        {
            await EnsureSchemaAsync();
            await EnsureModuleAsync(
                FinanceCode, "Финансы", "Кассовые, банковские операции и финансовый учет",
                "💰", 10, 900,
                requirePreviousModulesClosed: true,
                participatesInPeriodClose: true,
                isActiveByDefault: true);
            await EnsureModuleAsync(
                BalanceCode, "Баланс", "Итоговая отчетность, баланс и финансовые результаты",
                "📊", 20, 1000,
                requirePreviousModulesClosed: true,
                participatesInPeriodClose: false,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                FixedAssetsCode, "Основные средства", "Учет движения, состояния и амортизации основных средств",
                "🏗", 30, 200,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                InventoryCode, "Материалы", "Поступление, движение, списание и остатки материалов",
                "📦", 40, 300,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                PayrollCode, "Зарплата", "Расчет зарплаты и связанных удержаний",
                "👥", 50, 400,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                SalesCode, "Сбыт", "Продажи, отгрузки и расчеты по реализации",
                "🚚", 60, 500,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                RawMaterialsCode, "Сырье", "Учет сырья и производственных запасов",
                "🌾", 70, 600,
                isActiveByDefault: false);
            await EnsureModuleAsync(
                CostAccountingCode, "Себестоимость", "Расчет и анализ производственной себестоимости",
                "🧮", 80, 800,
                requirePreviousModulesClosed: true,
                isActiveByDefault: false);
            await SynchronizeDefaultAssignmentsAsync();
        }

        public async Task<List<MetadataModule>> GetModulesAsync(bool includeInactive = false)
        {
            await EnsureSchemaAsync();
            var query = _context.MetadataModules.AsNoTracking();
            if (!includeInactive)
                query = query.Where(module => module.IsActive);
            return await query.OrderBy(module => module.Order).ThenBy(module => module.Name).ToListAsync();
        }

        public async Task<List<MetadataModuleItem>> GetItemsAsync()
        {
            await EnsureSchemaAsync();
            return await _context.MetadataModuleItems.AsNoTracking()
                .OrderBy(item => item.Order).ToListAsync();
        }

        public async Task<MetadataModule> SaveModuleAsync(MetadataModule module)
        {
            await EnsureSchemaAsync();
            module.Code = NormalizeCode(module.Code, module.Name);
            module.Name = string.IsNullOrWhiteSpace(module.Name) ? "Новый модуль" : module.Name.Trim();
            module.Order = module.Order <= 0 ? 100 : module.Order;
            module.CloseOrder = module.CloseOrder <= 0 ? module.Order : module.CloseOrder;
            module.Icon = string.IsNullOrWhiteSpace(module.Icon) ? "📁" : module.Icon.Trim();
            module.Description = module.Description?.Trim() ?? string.Empty;
            if (module.Id == Guid.Empty)
                module.Id = Guid.NewGuid();

            var existing = await _context.MetadataModules.FindAsync(module.Id);
            if (existing == null)
                await _context.MetadataModules.AddAsync(module);
            else
            {
                existing.Code = module.Code;
                existing.Name = module.Name;
                existing.Description = module.Description;
                existing.Icon = module.Icon;
                existing.Order = module.Order;
                existing.CloseOrder = module.CloseOrder;
                existing.IsActive = module.IsActive;
                existing.ParticipatesInPeriodClose = module.ParticipatesInPeriodClose;
                existing.RequirePreviousModulesClosed = module.RequirePreviousModulesClosed;
            }
            await _context.SaveChangesAsync();
            return existing ?? module;
        }

        public async Task DeleteModuleAsync(Guid moduleId)
        {
            await EnsureSchemaAsync();
            var module = await _context.MetadataModules.FindAsync(moduleId)
                ?? throw new InvalidOperationException("Модуль не найден.");
            if (module.IsSystem)
                throw new InvalidOperationException("Системный модуль нельзя удалить, но его можно отключить.");
            var items = await _context.MetadataModuleItems.Where(item => item.ModuleId == moduleId).ToListAsync();
            _context.MetadataModuleItems.RemoveRange(items);
            _context.MetadataModules.Remove(module);
            await _context.SaveChangesAsync();
        }

        public async Task SaveAssignmentsAsync(Guid moduleId, IEnumerable<Guid> documentIds, IEnumerable<Guid> reportIds)
        {
            await EnsureSchemaAsync();
            var selectedDocuments = documentIds.Distinct().ToHashSet();
            var selectedReports = reportIds.Distinct().ToHashSet();
            var selectedObjects = selectedDocuments.Concat(selectedReports).ToHashSet();

            var oldItems = await _context.MetadataModuleItems.Where(item => item.ModuleId == moduleId).ToListAsync();
            _context.MetadataModuleItems.RemoveRange(oldItems);

            var conflicting = await _context.MetadataModuleItems
                .Where(item => selectedObjects.Contains(item.ObjectId))
                .ToListAsync();
            _context.MetadataModuleItems.RemoveRange(conflicting);

            var order = 1;
            await _context.MetadataModuleItems.AddRangeAsync(selectedDocuments.Select(id => new MetadataModuleItem
            {
                ModuleId = moduleId, ObjectId = id, ObjectType = "Document", Order = order++
            }));
            await _context.MetadataModuleItems.AddRangeAsync(selectedReports.Select(id => new MetadataModuleItem
            {
                ModuleId = moduleId, ObjectId = id, ObjectType = "Report", Order = order++
            }));
            await _context.SaveChangesAsync();
        }

        private async Task EnsureModuleAsync(
            string code,
            string name,
            string description,
            string icon,
            int order,
            int closeOrder,
            bool requirePreviousModulesClosed = false,
            bool participatesInPeriodClose = true,
            bool isActiveByDefault = false)
        {
            var existing = await _context.MetadataModules.FirstOrDefaultAsync(module => module.Code == code);
            if (existing != null)
            {
                existing.Name = name;
                existing.Description = description;
                existing.Icon = icon;
                existing.Order = order;
                existing.CloseOrder = closeOrder;
                existing.ParticipatesInPeriodClose = participatesInPeriodClose;
                existing.RequirePreviousModulesClosed = requirePreviousModulesClosed;
                existing.IsSystem = true;
                await _context.SaveChangesAsync();
                return;
            }

            await _context.MetadataModules.AddAsync(new MetadataModule
            {
                Code = code,
                Name = name,
                Description = description,
                Icon = icon,
                Order = order,
                CloseOrder = closeOrder,
                IsActive = isActiveByDefault,
                ParticipatesInPeriodClose = participatesInPeriodClose,
                RequirePreviousModulesClosed = requirePreviousModulesClosed,
                IsSystem = true
            });
            await _context.SaveChangesAsync();
        }

        private async Task SynchronizeDefaultAssignmentsAsync()
        {
            var modules = await _context.MetadataModules.ToDictionaryAsync(module => module.Code);
            var documents = await _context.MetadataObjects.AsNoTracking()
                .Where(item => item.ObjectType == "Document").ToListAsync();
            var reports = await _context.Reports.AsNoTracking().ToListAsync();

            await AssignMissingByNameAsync(modules[FinanceCode].Id, "Document", documents.Select(item => (item.Id, item.Name)),
                "Проводки", "Приходный кассовый ордер", "Расходный кассовый ордер", "Платежное поручение",
                "Платежная ведомость", "Доверенность", "Авансовый отчет", "Расчет курсовой разницы",
                InvoiceDocumentTypes.SalesIssue, InvoiceDocumentTypes.PurchaseRegistration);
            await MoveByNameToModuleAsync(modules[FinanceCode].Id, "Document", documents.Select(item => (item.Id, item.Name)),
                InvoiceDocumentTypes.SalesIssue, InvoiceDocumentTypes.PurchaseRegistration);
            await AssignMissingByNameAsync(modules[InventoryCode].Id, "Document", documents.Select(item => (item.Id, item.Name)),
                "Приход товаров", "Расход товаров", "Внутреннее перемещение ТМЦ", "Приход из производства ТМЦ",
                "Расход в производство", "Передача ТМЦ в подотчет", "Инвентаризация ТМЦ");
            await AssignMissingByNameAsync(modules[FixedAssetsCode].Id, "Document", documents.Select(item => (item.Id, item.Name)),
                FixedAssetDocumentNames);

            await AssignMissingByNameAsync(modules[FixedAssetsCode].Id, "Report", reports.Select(item => (item.Id, item.Name)),
                "Оборотная ведомость по ОС", "Ведомость основных средств", "Ведомость ОС по счету",
                "Ведомость амортизации", "Приход ОС за период", "Расшифровка баланса по ОС",
                "Контроль состояния карточек ОС", "Журнал начисления амортизации ОС",
                "Журнал переоценки ОС", "Журнал реализации ОС", "Журнал ликвидации ОС");
            await AssignMissingByNameAsync(modules[InventoryCode].Id, "Report", reports.Select(item => (item.Id, item.Name)),
                "Ведомость наличия материалов");
        }

        private async Task AssignMissingByNameAsync(
            Guid moduleId,
            string objectType,
            IEnumerable<(Guid Id, string Name)> objects,
            params string[] names)
        {
            var namesSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = objects.Where(item => namesSet.Contains(item.Name)).ToList();
            var assignedIds = await _context.MetadataModuleItems.Select(item => item.ObjectId).ToListAsync();
            var assigned = assignedIds.ToHashSet();
            var order = await _context.MetadataModuleItems.Where(item => item.ModuleId == moduleId)
                .Select(item => (int?)item.Order).MaxAsync() ?? 0;
            foreach (var candidate in candidates.Where(item => !assigned.Contains(item.Id)))
            {
                await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
                {
                    ModuleId = moduleId, ObjectId = candidate.Id, ObjectType = objectType, Order = ++order
                });
            }
            await _context.SaveChangesAsync();
        }

        private async Task MoveByNameToModuleAsync(
            Guid moduleId,
            string objectType,
            IEnumerable<(Guid Id, string Name)> objects,
            params string[] names)
        {
            var namesSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = objects.Where(item => namesSet.Contains(item.Name)).ToList();
            if (candidates.Count == 0)
                return;

            var objectIds = candidates.Select(item => item.Id).ToHashSet();
            var existingItems = await _context.MetadataModuleItems
                .Where(item => item.ObjectType == objectType && objectIds.Contains(item.ObjectId))
                .ToListAsync();
            var order = await _context.MetadataModuleItems.Where(item => item.ModuleId == moduleId)
                .Select(item => (int?)item.Order).MaxAsync() ?? 0;

            foreach (var candidate in candidates.OrderBy(item => item.Name))
            {
                var item = existingItems.FirstOrDefault(existing => existing.ObjectId == candidate.Id);
                if (item == null)
                {
                    await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
                    {
                        ModuleId = moduleId,
                        ObjectId = candidate.Id,
                        ObjectType = objectType,
                        Order = ++order
                    });
                    continue;
                }

                if (item.ModuleId != moduleId)
                {
                    item.ModuleId = moduleId;
                    item.Order = ++order;
                }
            }

            await _context.SaveChangesAsync();
        }

        private static string NormalizeCode(string code, string name)
        {
            var source = string.IsNullOrWhiteSpace(code) ? name : code;
            var normalized = new string((source ?? string.Empty)
                .Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
            return string.IsNullOrWhiteSpace(normalized) ? $"Module{Guid.NewGuid():N}" : normalized;
        }

        public static readonly string[] FixedAssetDocumentNames =
        {
            "Покупка ОС", "Ввод ОС в эксплуатацию", "Приход из производства ОС", "Переоценка ОС",
            "Реализация ОС", "Частичная реализация ОС", "Ликвидация ОС", "Укомплектация ОС",
            "Разукомплектация ОС", "Начисление амортизации", "Списание амортизации",
            "Консервация ОС", "Расконсервация ОС", "Передача ОС в подотчет", "Смена затратного счета"
        };
    }
}
