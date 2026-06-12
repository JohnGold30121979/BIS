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
    public partial class MetadataService
    {
        private readonly AppDbContext _context;

        public MetadataService(AppDbContext context)
        {
            _context = context;
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
                        IsInitialized = false
                    };
                    await _context.Set<MetadataConfiguration>().AddAsync(config);
                    await _context.SaveChangesAsync();
                }

                // Если уже инициализирована, выходим
                if (config.IsInitialized)
                {
                    System.Diagnostics.Debug.WriteLine("Метаданные уже инициализированы");
                    return;
                }

                // ========== СОЗДАЁМ ТОЛЬКО ДОКУМЕНТЫ ==========
                // (справочники создаются в InitializePredefinedCatalogsAsync)
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

                // ========== ДОБАВЛЯЕМ ДОКУМЕНТ "ПРОВОДКИ" ==========
                var postingsDoc = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Проводки",
                    TableName = "doc_postings",
                    ObjectType = "Document",
                    Description = "Журнал бухгалтерских проводок",
                    Icon = "📝",
                    Order = 3,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                postingsDoc.Fields = GetPostingFields(postingsDoc.Id);
                documents.Add(postingsDoc);
                // =================================================

                await _context.Set<MetadataObject>().AddRangeAsync(documents);
                await _context.SaveChangesAsync();

                // Создаём таблицы для документов
                foreach (var doc in documents)
                {
                    await CreateTableForCatalogAsync(doc);
                }

                config.IsInitialized = true;
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine("✅ Базовая инициализация завершена (документы созданы)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка инициализации: {ex.Message}");
            }
        }
        
        // Получение данных справочника      
        public async Task<List<Dictionary<string, object>>> GetCatalogDataAsync(Guid catalogId)
        {
            var catalog = await _context.MetadataObjects
                .Include(c => c.Fields)
                .FirstOrDefaultAsync(m => m.Id == catalogId);

            if (catalog == null) return new List<Dictionary<string, object>>();

            var result = new List<Dictionary<string, object>>();
            var sql = $"SELECT * FROM \"{catalog.TableName}\" ORDER BY \"CreatedAt\"";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();

                // Безопасное создание fieldMapping (обрабатывает дубликаты)
                var fieldMapping = new Dictionary<string, string>();
                foreach (var field in catalog.Fields)
                {
                    if (string.IsNullOrEmpty(field.DbColumnName)) continue;

                    if (!fieldMapping.ContainsKey(field.DbColumnName))
                        fieldMapping[field.DbColumnName] = field.Name;
                    else
                        System.Diagnostics.Debug.WriteLine($"⚠️ Дубликат колонки: {field.DbColumnName} в {catalog.Name}");
                }

                // Системные поля
                if (!fieldMapping.ContainsKey("Id")) fieldMapping["Id"] = "Id";
                if (!fieldMapping.ContainsKey("CreatedAt")) fieldMapping["CreatedAt"] = "CreatedAt";
                if (!fieldMapping.ContainsKey("UpdatedAt")) fieldMapping["UpdatedAt"] = "UpdatedAt";

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var dbName = reader.GetName(i);
                        var displayName = fieldMapping.TryGetValue(dbName, out var name) ? name : dbName;
                        row[displayName] = reader.GetValue(i);
                    }
                    result.Add(row);
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }


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

                // В конце метода, после справочников
                var existingDocuments = await _context.MetadataObjects
                    .Where(m => m.ObjectType == "Document")
                    .Select(m => m.Name)
                    .ToListAsync();              

                // Создаём  справочники

                if (!existingCatalogs.Contains("Участки"))
                    await CreateSitesCatalog(config);

                if (!existingCatalogs.Contains("Сотрудники (Списочный состав)"))
                    await CreateEmployeesCatalog(config);

                if (!existingCatalogs.Contains("Основные средства"))
                    await CreateAssetsCatalog(config);

                if (!existingCatalogs.Contains("Государства"))
                    await CreateCountriesCatalog(config);

                if (!existingCatalogs.Contains("Организации"))
                    await CreateOrganizationsCatalog(config);

                if (!existingCatalogs.Contains("Расчетные счета организаций"))
                    await CreateBankAccountsCatalog(config);

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

                if (!existingCatalogs.Contains("Справочник валют"))
                    await CreateCurrencyCatalog(config);

                if (!existingCatalogs.Contains("Справочник курсов валют"))
                    await CreateCurrencyRatesCatalog(config);

                if (!existingCatalogs.Contains("Контрагенты"))
                    await CreateContractorsCatalog(config);

                if (!existingCatalogs.Contains("МОЛ"))
                    await CreateResponsiblePersonsCatalog(config);              

                System.Diagnostics.Debug.WriteLine("Все предустановленные справочники созданы");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания предустановленных справочников : {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            }
        }   
       
      
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

       
        // Универсальное удаление записи      
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


        // Выполнение автоматических расчетов        
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

     
        // Создание проводок по правилам       
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

        // Получение данных записи       
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

       // Обновление поля записи        
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

        public async Task<List<MetadataObject>> GetAllMetadataObjectsAsync()
        {
            return await _context.MetadataObjects
                .Include(m => m.Fields)
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

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
    }


}

