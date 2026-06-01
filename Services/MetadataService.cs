using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

            var employeesCatalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = "Сотрудники",
                TableName = "catalog_employees",
                ObjectType = "Catalog",
                Description = "Список сотрудников предприятия",
                Icon = "👥",
                Order = 1,
                IsSystem = true,
                MetadataConfigId = config.Id
            };
            employeesCatalog.Fields = GetStandardCatalogFields(employeesCatalog.Id);
            catalogs.Add(employeesCatalog);

            var materialsCatalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = "Материалы",
                TableName = "catalog_materials",
                ObjectType = "Catalog",
                Description = "Номенклатура материалов и товаров",
                Icon = "📦",
                Order = 2,
                IsSystem = true,
                MetadataConfigId = config.Id
            };
            materialsCatalog.Fields = GetStandardCatalogFields(materialsCatalog.Id);
            catalogs.Add(materialsCatalog);

            var assetsCatalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = "Основные средства",
                TableName = "catalog_assets",
                ObjectType = "Catalog",
                Description = "Основные средства предприятия",
                Icon = "⚙️",
                Order = 3,
                IsSystem = true,
                MetadataConfigId = config.Id
            };
            assetsCatalog.Fields = GetStandardCatalogFields(assetsCatalog.Id);
            catalogs.Add(assetsCatalog);

            var departmentsCatalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = "Подразделения",
                TableName = "catalog_departments",
                ObjectType = "Catalog",
                Description = "Структура подразделений",
                Icon = "🏢",
                Order = 4,
                IsSystem = true,
                MetadataConfigId = config.Id
            };
            departmentsCatalog.Fields = GetStandardCatalogFields(departmentsCatalog.Id);
            catalogs.Add(departmentsCatalog);

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
            await AddTestDataAsync();
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

        // === НОВЫЕ МЕТОДЫ ДЛЯ РАБОТЫ СО СПРАВОЧНИКАМИ ===

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
            var catalog = await _context.Set<MetadataObject>()
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(c => c.Id == catalogId);

            if (catalog == null) return;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS \"{catalog.TableName}\" CASCADE;");

                foreach (var field in catalog.Fields)
                {
                    _context.Set<MetadataField>().Remove(field);
                }

                _context.Set<MetadataObject>().Remove(catalog);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Ошибка удаления справочника: {ex.Message}");
            }
        }

        // В методе CreateCatalogAsync
        public async Task<MetadataObject> CreateCatalogAsync(string name, string description, string icon, List<Models.FieldInfo> fields)
        {
            var catalog = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = name,
                TableName = $"catalog_{name.ToLower().Replace(" ", "_")}_{Guid.NewGuid():N}",
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
                catalog.Fields.Add(new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = field.Name,
                    DbColumnName = field.Name.ToLower().Replace(" ", "_"),
                    FieldType = field.Type,
                    IsRequired = field.IsRequired,
                    Order = order++,
                    MetadataObjectId = catalog.Id
                });
            }

            await _context.MetadataObjects.AddAsync(catalog);
            await _context.SaveChangesAsync();

            // Создаем физическую таблицу
            await CreateTableForCatalogAsync(catalog);

            return catalog;
        }

        private async Task CreateTableForCatalogAsync(MetadataObject catalog)
        {
            try
            {
                var sqlBuilder = new System.Text.StringBuilder();

                // Экранируем имя таблицы двойными кавычками для сохранения регистра
                sqlBuilder.AppendLine($"CREATE TABLE \"{catalog.TableName}\" (");
                sqlBuilder.AppendLine("    \"Id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");

                foreach (var field in catalog.Fields.OrderBy(f => f.Order))
                {
                    var sqlType = GetSqlTypeForField(field);
                    var nullable = field.IsRequired ? "NOT NULL" : "";
                    // Используем двойные кавычки для имен колонок
                    sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
                }

                sqlBuilder.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
                sqlBuilder.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
                sqlBuilder.AppendLine(");");

                // Создаем индексы с правильными именами колонок
                sqlBuilder.AppendLine($"CREATE INDEX \"IX_{catalog.TableName}_Name\" ON \"{catalog.TableName}\" (\"Name\");");

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
            var sqlBuilder = new System.Text.StringBuilder();

            sqlBuilder.AppendLine($"DROP TABLE IF EXISTS \"{catalog.TableName}\" CASCADE;");
            sqlBuilder.AppendLine($"CREATE TABLE \"{catalog.TableName}\" (");
            sqlBuilder.AppendLine("    \"Id\" uuid PRIMARY KEY DEFAULT gen_random_uuid(),");

            foreach (var field in catalog.Fields.OrderBy(f => f.Order))
            {
                var sqlType = GetFieldSqlType(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sqlBuilder.AppendLine("    \"CreatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sqlBuilder.AppendLine("    \"UpdatedAt\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
            sqlBuilder.AppendLine(");");

            sqlBuilder.AppendLine($"CREATE INDEX IF NOT EXISTS idx_{catalog.TableName}_name ON \"{catalog.TableName}\" (\"name\");");

            await _context.Database.ExecuteSqlRawAsync(sqlBuilder.ToString());
        }

        private string GetFieldSqlType(MetadataField field)
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
            var sqlBuilder = new System.Text.StringBuilder();
            sqlBuilder.AppendLine($"CREATE TABLE IF NOT EXISTS \"{obj.TableName}\" (");
            sqlBuilder.AppendLine("    \"id\" UUID PRIMARY KEY DEFAULT gen_random_uuid(),");

            foreach (var field in obj.Fields.OrderBy(f => f.Order))
            {
                var sqlType = GetFieldSqlType(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sqlBuilder.AppendLine("    \"created_at\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sqlBuilder.AppendLine("    \"updated_at\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
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

        private async Task AddTestDataAsync()
        {
            var employeesTable = "catalog_employees";
            var employees = new[]
            {
                new { code = "001", name = "Иванов Иван Иванович", description = "Генеральный директор" },
                new { code = "002", name = "Петрова Мария Сергеевна", description = "Главный бухгалтер" },
                new { code = "003", name = "Сидоров Алексей Владимирович", description = "Менеджер по продажам" }
            };

            foreach (var emp in employees)
            {
                var sql = $@"
                    INSERT INTO ""{employeesTable}"" (code, name, description) 
                    VALUES ('{emp.code}', '{emp.name}', '{emp.description}')
                    ON CONFLICT (code) DO NOTHING";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }

            var materialsTable = "catalog_materials";
            var materials = new[]
            {
                new { code = "001", name = "Ноутбук Lenovo ThinkPad", description = "Для сотрудников" },
                new { code = "002", name = "Стол офисный", description = "Мебель" },
                new { code = "003", name = "Бумага А4", description = "Расходные материалы" }
            };

            foreach (var mat in materials)
            {
                var sql = $@"
                    INSERT INTO ""{materialsTable}"" (code, name, description) 
                    VALUES ('{mat.code}', '{mat.name}', '{mat.description}')
                    ON CONFLICT (code) DO NOTHING";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
        }

        private async Task<int> GetNextOrderAsync()
        {
            var maxOrder = await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Catalog")
                .MaxAsync(m => (int?)m.Order) ?? 0;
            return maxOrder + 1;
        }

        private async Task<Guid> GetCurrentConfigIdAsync()
        {
            var infoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();
            var config = await _context.Set<MetadataConfiguration>()
                .FirstOrDefaultAsync(c => c.InfoBaseId == infoBase.Id);

            if (config == null)
            {
                config = new MetadataConfiguration
                {
                    Id = Guid.NewGuid(),
                    InfoBaseId = infoBase.Id,
                    IsInitialized = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Set<MetadataConfiguration>().Add(config);
                await _context.SaveChangesAsync();
            }

            return config.Id;
        }
    }
}