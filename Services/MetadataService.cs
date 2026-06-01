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
            // Проверяем, есть ли уже конфигурация
            var config = await _context.Set<MetadataConfiguration>()
                .FirstOrDefaultAsync(c => c.InfoBaseId == infoBaseId);

            if (config == null)
            {
                config = new MetadataConfiguration
                {
                    Id = Guid.NewGuid(),
                    InfoBaseId = infoBaseId,
                    CreatedAt = DateTime.Now,
                    IsInitialized = true
                };
                await _context.Set<MetadataConfiguration>().AddAsync(config);
                await _context.SaveChangesAsync();
            }
            else if (config.IsInitialized)
            {
                return; // Уже инициализировано
            }

            // Создаем системные справочники
            var catalogs = new List<MetadataObject>();

            // 1. Справочник "Сотрудники"
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

            // 2. Справочник "Материалы"
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

            // 3. Справочник "Основные средства"
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

            // 4. Справочник "Подразделения"
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

            // 5. Справочник "Контрагенты"
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

            // Добавляем все справочники
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

            // Создаем физические таблицы в БД
            await CreateTablesFromMetadataAsync();

            // Добавляем тестовые данные
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
                var sqlType = GetSqlType(field);
                var nullable = field.IsRequired ? "NOT NULL" : "";
                sqlBuilder.AppendLine($"    \"{field.DbColumnName}\" {sqlType} {nullable},");
            }

            sqlBuilder.AppendLine("    \"created_at\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,");
            sqlBuilder.AppendLine("    \"updated_at\" TIMESTAMP DEFAULT CURRENT_TIMESTAMP");
            sqlBuilder.AppendLine(");");

            // Добавляем индексы
            sqlBuilder.AppendLine($"CREATE INDEX IF NOT EXISTS idx_{obj.TableName}_name ON \"{obj.TableName}\" (\"name\");");
            sqlBuilder.AppendLine($"CREATE INDEX IF NOT EXISTS idx_{obj.TableName}_code ON \"{obj.TableName}\" (\"code\");");

            try
            {
                await _context.Database.ExecuteSqlRawAsync(sqlBuilder.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating table {obj.TableName}: {ex.Message}");
            }
        }

        private string GetSqlType(MetadataField field)
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

        private async Task AddTestDataAsync()
        {
            // Добавляем тестовых сотрудников
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

            // Добавляем тестовые материалы
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

        // Получение всех справочников
        public async Task<List<MetadataObject>> GetCatalogsAsync()
        {
            return await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Catalog")
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

        // Получение всех документов
        public async Task<List<MetadataObject>> GetDocumentsAsync()
        {
            return await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Document")
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

        // Получение данных справочника
        public async Task<List<Dictionary<string, object>>> GetCatalogDataAsync(Guid catalogId)
        {
            var catalog = await _context.Set<MetadataObject>()
                .FirstOrDefaultAsync(m => m.Id == catalogId);

            if (catalog == null) return new List<Dictionary<string, object>>();

            var sql = $"SELECT * FROM \"{catalog.TableName}\" ORDER BY code";
            var result = new List<Dictionary<string, object>>();

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = sql;
                await _context.Database.OpenConnectionAsync();

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.GetValue(i);
                        }
                        result.Add(row);
                    }
                }
            }

            return result;
        }
    }
}