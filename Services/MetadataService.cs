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
        private const string GlobalDocumentNumberingKey = "Все документы";

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

                var cashReceiptdoc = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Приходный кассовый ордер",
                    TableName = "doc_cash_receipt",
                    ObjectType = "Document",
                    Description = "Приходный кассовый ордер",
                    Icon = "📥",
                    Order = 4,
                    IsSystem = true,
                    MetadataConfigId = config.Id
                };
                cashReceiptdoc.Fields = GetCashReceiptFields(cashReceiptdoc.Id);
                documents.Add(cashReceiptdoc);

                var cashPaymentdoc = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Расходный кассовый ордер",
                    TableName = "doc_cash_payment",
                    ObjectType = "Document",
                    Description = "Расходный кассовый ордер",
                    Icon = "📤",
                    Order = 5,
                    IsSystem = true,
                    MetadataConfigId = config.Id,

                };
                cashPaymentdoc.Fields = GetCashPaymentFields(cashPaymentdoc.Id);
                documents.Add(cashPaymentdoc);
         
                var paymentOrderdocument = new MetadataObject
                {
                    Id = Guid.NewGuid(),
                    Name = "Платежное поручение",
                    TableName = "doc_payment_order",
                    ObjectType = "Document",
                    Description = "Платежное поручение для банковских операций",
                    Icon = "🏦",
                    Order = 6,
                    IsSystem = true,
                    MetadataConfigId = config.Id                  
                };
                paymentOrderdocument.Fields = GetPaymentOrderFields(paymentOrderdocument.Id);
                documents.Add(paymentOrderdocument);  

                await _context.Set<MetadataObject>().AddRangeAsync(documents);
                await _context.SaveChangesAsync();

                // Создаём таблицы для документов
                foreach (var doc in documents)
                {
                    await CreateTableForCatalogAsync(doc);
                }

                // Создаём таблицу нумерации документов
                await CreateDocumentNumberingTableAsync();

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

            if (catalog.ObjectType == "Document")
            {
                try
                {
                    await EnsureGlobalDocumentNumberConfigurationAsync();
                    await NormalizeDocumentTableNumbersAsync(catalog);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Ошибка синхронизации нумерации для {catalog.Name}: {ex.Message}");
                }
            }

            var result = new List<Dictionary<string, object>>();
            var sql = catalog.Name == "Организации"
                ? $"SELECT * FROM \"{catalog.TableName}\" ORDER BY COALESCE(\"is_primary\", false) DESC, \"code\", \"CreatedAt\""
                : $"SELECT * FROM \"{catalog.TableName}\" ORDER BY \"CreatedAt\"";

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
                        var value = reader.GetValue(i);

                        if (catalog.ObjectType == "Document" &&
                            IsDocumentNumberFieldName(displayName))
                        {
                            row[displayName] = NormalizeLegacyDocumentNumber(value?.ToString());
                        }
                        else
                        {
                            row[displayName] = value;
                        }
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

            foreach (var field in SelectFieldsForWrite(catalog, itemData))
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
            try
            {
                await EnsureChartOfAccountsCatalogStructureAsync();
                await EnsureOrganizationsCatalogStructureAsync();
                await EnsureCashDesksCatalogStructureAsync();
                await EnsureAccountAnalyticsLinksCatalogAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации служебных справочников: {ex.Message}");
            }

            return await _context.Set<MetadataObject>()
                .Where(m => m.ObjectType == "Catalog" && m.Name != "Контрагенты")
                .Include(m => m.Fields)
                .OrderBy(m => m.Order)
                .ToListAsync();
        }

        public async Task<List<MetadataObject>> GetDocumentsAsync()
        {
            var documents = await LoadDocumentMetadataAsync();

            try
            {
                await EnsureManagedDocumentAnalyticFieldsAsync(documents);
                await EnsureInventoryDocumentStructureAsync(documents);
                await EnsurePostingDocumentStructureAsync(documents);
                await EnsureGlobalDocumentNumberConfigurationAsync(documents);

                foreach (var document in documents.Where(IsManagedDocument))
                {
                    await NormalizeDocumentTableNumbersAsync(document);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Ошибка синхронизации общей нумерации документов: {ex.Message}");
            }

            return documents;
        }

        private async Task<List<MetadataObject>> LoadDocumentMetadataAsync()
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

        public async Task InitializePredefinedCatalogsAsync(Guid infoBaseId)
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
                var existingDocumentsWithFields = await _context.MetadataObjects
                    .Include(m => m.Fields)
                    .Where(m => m.ObjectType == "Document")
                    .ToListAsync();
                await EnsureCashOrderDocumentStructureAsync(existingDocumentsWithFields);

                if (!existingCatalogs.Contains("Участки"))
                    await CreateSitesCatalog(config);

                if (!existingCatalogs.Contains("Сотрудники (Списочный состав)"))
                    await CreateEmployeesCatalog(config);

                if (!existingCatalogs.Contains("Основные средства"))
                    await CreateAssetsCatalog(config);
                await EnsureAssetsCatalogStructureAsync();

                if (!existingCatalogs.Contains("Государства"))
                    await CreateCountriesCatalog(config);

                if (!existingCatalogs.Contains("Организации"))
                    await CreateOrganizationsCatalog(config);
                await EnsureOrganizationsCatalogStructureAsync();

                if (!existingCatalogs.Contains("Расчетные счета организаций"))
                    await CreateBankAccountsCatalog(config);

                if (!existingCatalogs.Contains("План счетов"))
                    await CreateChartOfAccountsCatalog(config);
                await EnsureChartOfAccountsCatalogStructureAsync();
                await EnsureCashOrderPostingAccountsAsync();

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

                if (!existingCatalogs.Contains("МОЛ"))
                    await CreateResponsiblePersonsCatalog(config);

                if (!existingCatalogs.Contains("Кассы"))
                    await CreateCashDesksCatalog(config);
                await EnsureCashDesksCatalogStructureAsync();

                // Новые справочники                

                if (!existingCatalogs.Contains("Налоги"))
                    await CreateTaxCatalog(config);

                if (!existingCatalogs.Contains("Подразделения"))
                    await CreateDivisionCatalog(config);

                //if (!existingCatalogs.Contains("Участки (новые)"))
                //    await CreatePlotCatalog(config);

                if (!existingCatalogs.Contains("Виды поставки"))
                    await CreateSupplyKindCatalog(config);

                if (!existingCatalogs.Contains("Виды оплаты"))
                    await CreatePaymentKindCatalog(config);

                if (!existingCatalogs.Contains("Типы поставки"))
                    await CreateDeliveryTypeCatalog(config);

                await EnsureAccountAnalyticsLinksCatalogAsync(config);
                await EnsureStandardReportTemplatesAsync(config);



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

            await EnsureDocumentDateCanBeModifiedAsync(metadata, data);

            NormalizeDocumentNumberData(metadata, data);
            await EnsureDocumentNumberIsUniqueAsync(metadata, data);

            var columns = new List<string> { "\"Id\"", "\"CreatedAt\"" };
            var values = new List<string> { $"'{Guid.NewGuid()}'", "NOW()" };

            foreach (var field in SelectFieldsForWrite(metadata, data))
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
            if (IsPostingsDocument(metadata))
            {
                await SyncManualPostingCashBalanceAsync(
                    metadata,
                    previousRecord: null,
                    currentRecord: await GetRecordDataAsync(metadata.TableName, recordId));
            }

            return recordId;
        }

        public async Task UpdateDynamicRecordAsync(Guid metadataId, Guid recordId, Dictionary<string, object> data)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null) throw new Exception($"Объект метаданных {metadataId} не найден");

            await EnsureDocumentDateCanBeModifiedAsync(metadata, data);

            NormalizeDocumentNumberData(metadata, data);
            await EnsureDocumentNumberIsUniqueAsync(metadata, data, recordId);
            var previousRecord = IsPostingsDocument(metadata) || IsCashDeskCatalog(metadata)
                ? await GetRecordDataAsync(metadata.TableName, recordId)
                : null;

            var setClauses = new List<string>();

            foreach (var field in SelectFieldsForWrite(metadata, data))
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
            if (IsPostingsDocument(metadata))
            {
                await SyncManualPostingCashBalanceAsync(
                    metadata,
                    previousRecord,
                    await GetRecordDataAsync(metadata.TableName, recordId));
            }
            else if (IsCashDeskCatalog(metadata))
            {
                var currentRecord = await GetRecordDataAsync(metadata.TableName, recordId);
                var accountCode = await ResolveAccountCodeValueAsync(GetStringValue(currentRecord, "code", "Счет"));
                await SyncCashDeskAccountReferencesAsync(recordId, accountCode);
            }
        }

        private static List<MetadataField> SelectFieldsForWrite(
            MetadataObject metadata,
            Dictionary<string, object> data)
        {
            var result = new List<MetadataField>();

            foreach (var group in metadata.Fields
                         .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
                         .GroupBy(field => field.DbColumnName, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Дубликат колонки '{group.Key}' в метаданных '{metadata.Name}'. Для записи будет использован один реквизит.");
                }

                var fieldWithData = group
                    .OrderBy(field => field.Order)
                    .FirstOrDefault(field => data.ContainsKey(field.Name));
                var requiredField = group
                    .OrderBy(field => field.Order)
                    .FirstOrDefault(field => field.IsRequired);

                result.Add(fieldWithData ?? requiredField ?? group.OrderBy(field => field.Order).First());
            }

            return result;
        }

        private async Task EnsureDocumentDateCanBeModifiedAsync(
            MetadataObject metadata,
            Dictionary<string, object> data)
        {
            if (metadata.ObjectType != "Document")
                return;

            foreach (var name in new[] { "Дата", "Дата документа", "doc_date", "posting_date", "date" })
            {
                if (!data.TryGetValue(name, out var value) || value == null)
                    continue;
                if (value is DateTime date || DateTime.TryParse(value.ToString(), out date))
                    await new AccountingPeriodService(_context).EnsureDateCanBeModifiedAsync(date);
                return;
            }
        }

        private static void NormalizeDocumentNumberData(MetadataObject metadata, Dictionary<string, object> data)
        {
            if (!IsManagedDocument(metadata))
            {
                return;
            }

            foreach (var fieldName in new[] { "Номер", "Номер документа", "doc_number", "number" })
            {
                if (data.TryGetValue(fieldName, out var value) && value != null)
                {
                    var normalizedNumber = NormalizeLegacyDocumentNumber(value.ToString());
                    if (string.IsNullOrWhiteSpace(normalizedNumber) || normalizedNumber.Any(c => !char.IsDigit(c)))
                    {
                        throw new Exception("Номер документа должен содержать только цифры");
                    }

                    data[fieldName] = normalizedNumber;
                }
            }
        }

        private async Task EnsureDocumentNumberIsUniqueAsync(
            MetadataObject metadata,
            Dictionary<string, object> data,
            Guid? currentRecordId = null)
        {
            if (!IsManagedDocument(metadata))
            {
                return;
            }

            var documentNumber = GetDocumentNumberFromData(data);
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return;
            }

            var documents = await LoadDocumentMetadataAsync();
            var connection = _context.Database.GetDbConnection();
            var connectionOpened = false;

            try
            {
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                foreach (var document in documents.Where(IsManagedDocument))
                {
                    var numberField = FindDocumentNumberField(document);
                    if (numberField == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(document.TableName) ||
                        string.IsNullOrWhiteSpace(numberField.DbColumnName))
                    {
                        continue;
                    }

                    var quotedTableName = DelimitIdentifier(document.TableName);
                    var quotedColumnName = DelimitIdentifier(numberField.DbColumnName);

                    using var command = connection.CreateCommand();
                    command.CommandText = $@"
                        SELECT COUNT(*)
                        FROM {quotedTableName}
                        WHERE REGEXP_REPLACE(COALESCE({quotedColumnName}::text, ''), '\D', '', 'g') = @documentNumber";

                    var documentNumberParameter = command.CreateParameter();
                    documentNumberParameter.ParameterName = "@documentNumber";
                    documentNumberParameter.Value = documentNumber;
                    command.Parameters.Add(documentNumberParameter);

                    if (currentRecordId.HasValue && document.Id == metadata.Id)
                    {
                        command.CommandText += @" AND ""Id"" <> @recordId";

                        var recordIdParameter = command.CreateParameter();
                        recordIdParameter.ParameterName = "@recordId";
                        recordIdParameter.Value = currentRecordId.Value;
                        command.Parameters.Add(recordIdParameter);
                    }

                    var matches = Convert.ToInt32(await command.ExecuteScalarAsync());
                    if (matches > 0)
                    {
                        throw new Exception($"Номер документа {documentNumber} уже используется.");
                    }
                }
            }
            finally
            {
                if (connectionOpened)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }

        private static string? GetDocumentNumberFromData(Dictionary<string, object> data)
        {
            foreach (var fieldName in new[] { "Номер", "Номер документа", "doc_number", "number" })
            {
                if (data.TryGetValue(fieldName, out var value) && value != null)
                {
                    var documentNumber = value.ToString();
                    if (!string.IsNullOrWhiteSpace(documentNumber))
                    {
                        return documentNumber;
                    }
                }
            }

            return null;
        }


        // Универсальное удаление записи      
        public async Task DeleteDynamicRecordAsync(Guid metadataId, Guid recordId)
        {
            var metadata = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata == null) throw new Exception($"Объект метаданных {metadataId} не найден");

            if (metadata.ObjectType == "Document")
            {
                var recordData = await GetRecordDataAsync(metadata.TableName, recordId);
                await EnsureDocumentDateCanBeModifiedAsync(metadata, recordData);
            }

            Dictionary<string, object>? previousRecord = null;
            if (IsPostingsDocument(metadata))
            {
                previousRecord = await GetRecordDataAsync(metadata.TableName, recordId);
            }

            var sql = $"DELETE FROM \"{metadata.TableName}\" WHERE \"Id\" = '{recordId}'";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();

            await SyncManualPostingCashBalanceAsync(metadata, previousRecord, currentRecord: null);
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
            var safeTableName = QuoteIdentifier(tableName);
            var sql = $"SELECT * FROM {safeTableName} WHERE \"Id\" = @recordId";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@recordId";
            parameter.Value = recordId;
            command.Parameters.Add(parameter);

            var connectionOpened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        result[reader.GetName(i)] = reader.GetValue(i);
                    }
                }
            }
            finally
            {
                if (connectionOpened)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

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


        // Провести документ (ПКО или РКО)       
        public async Task PostDocumentAsync(Guid documentId, Guid recordId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                System.Diagnostics.Debug.WriteLine($"=== PostDocumentAsync START ===");
                System.Diagnostics.Debug.WriteLine($"DocumentId: {documentId}, RecordId: {recordId}");

                // 1. Получаем данные документа
                var document = await _context.MetadataObjects
                    .Include(m => m.Fields)
                    .Include(m => m.PostingRules)
                    .FirstOrDefaultAsync(m => m.Id == documentId);

                if (document == null) throw new Exception("Документ не найден");
                System.Diagnostics.Debug.WriteLine($"Document: {document.Name}");

                // 2. Получаем данные записи
                var recordData = await GetRecordDataAsync(document.TableName, recordId);
                await EnsureDocumentDateCanBeModifiedAsync(document, recordData);

                System.Diagnostics.Debug.WriteLine("=== recordData keys ===");
                foreach (var key in recordData.Keys)
                {
                    System.Diagnostics.Debug.WriteLine($"  {key} = {recordData[key]}");
                }

                // 3. Проверяем, не проведён ли уже документ
                if (recordData.ContainsKey("is_posted") && recordData["is_posted"] is bool isPosted && isPosted)
                {
                    throw new Exception("Документ уже проведён!");
                }

                // 4. Получаем сумму
                decimal amount = 0;
                if (recordData.ContainsKey("amount") && recordData["amount"] != null)
                {
                    amount = Convert.ToDecimal(recordData["amount"]);
                }
                else if (recordData.ContainsKey("Сумма") && recordData["Сумма"] != null)
                {
                    amount = Convert.ToDecimal(recordData["Сумма"]);
                }
                else
                {
                    throw new Exception("Сумма не указана");
                }
                System.Diagnostics.Debug.WriteLine($"Amount: {amount}");

                if (amount <= 0)
                    throw new Exception("Сумма проводимого документа должна быть больше нуля");

                // 5. Определяем тип документа и обрабатываем
                if (document.Name == "Приходный кассовый ордер" || document.Name == "Расходный кассовый ордер")
                {
                    await ProcessCashOrderAsync(document, recordData, recordId, amount);
                }
                else if (document.Name == "Платежное поручение")
                {
                    await ProcessPaymentOrderAsync(document, recordData, recordId, amount);
                }
                else if (document.PostingRules.Any())
                {
                    await ProcessDocumentByPostingRulesAsync(document, recordData, recordId, amount);
                }
                else
                {
                    throw new Exception($"Неизвестный тип документа: {document.Name}");
                }

                await transaction.CommitAsync();
                System.Diagnostics.Debug.WriteLine($"=== PostDocumentAsync SUCCESS ===");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                System.Diagnostics.Debug.WriteLine($"=== PostDocumentAsync ERROR ===");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task UnpostDocumentAsync(Guid documentId, Guid recordId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var document = await _context.MetadataObjects.Include(item => item.Fields)
                    .FirstOrDefaultAsync(item => item.Id == documentId)
                    ?? throw new InvalidOperationException("Документ не найден.");
                if (document.Name != "Приходный кассовый ордер" && document.Name != "Расходный кассовый ордер")
                    throw new InvalidOperationException("Отмена проведения пока поддерживается для ПКО и РКО.");

                var record = await GetRecordDataAsync(document.TableName, recordId);
                await EnsureDocumentDateCanBeModifiedAsync(document, record);
                if (record.GetValueOrDefault("is_posted") is not bool isPosted || !isPosted)
                    throw new InvalidOperationException("Документ не проведен.");

                var amount = Convert.ToDecimal(record.GetValueOrDefault("amount") ?? 0m);
                var documentNumber = NormalizeLegacyDocumentNumber(record.GetValueOrDefault("doc_number")?.ToString());
                if (TryGetGuid(record, out var cashDeskId, "cash_desk_id", "Касса", "cashdesk_id", "cashdesk"))
                {
                    var wasReceipt = document.Name == "Приходный кассовый ордер";
                    await UpdateCashDeskBalance(cashDeskId, amount, !wasReceipt);
                }

                await _context.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM doc_postings
                    WHERE doc_number = @number AND document_type = @type;",
                    new NpgsqlParameter("@number", documentNumber),
                    new NpgsqlParameter("@type", document.Name));

                var tableName = QuoteIdentifier(document.TableName);
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE {tableName}
                    SET ""is_posted"" = false, ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = @recordId;",
                    new NpgsqlParameter("@recordId", recordId));
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task ProcessDocumentByPostingRulesAsync(
            MetadataObject document,
            Dictionary<string, object> recordData,
            Guid recordId,
            decimal documentAmount)
        {
            var postingDate = recordData.GetValueOrDefault("doc_date") is DateTime documentDate
                ? documentDate
                : recordData.GetValueOrDefault("date") is DateTime date ? date : DateTime.Today;
            var documentNumber = NormalizeLegacyDocumentNumber(
                recordData.GetValueOrDefault("number")?.ToString() ??
                recordData.GetValueOrDefault("doc_number")?.ToString());
            var description = recordData.GetValueOrDefault("description")?.ToString() ?? string.Empty;

            foreach (var rule in document.PostingRules.OrderBy(rule => rule.Order))
            {
                if (!string.IsNullOrWhiteSpace(rule.Condition) && !EvaluateCondition(rule.Condition, recordData))
                    continue;

                var debit = EvaluateExpression(rule.DebitAccountExpression, recordData).Trim();
                var credit = EvaluateExpression(rule.CreditAccountExpression, recordData).Trim();
                if (Guid.TryParse(debit, out var debitAccountId))
                    debit = await GetAccountCodeById(debitAccountId);
                if (Guid.TryParse(credit, out var creditAccountId))
                    credit = await GetAccountCodeById(creditAccountId);
                var amountText = EvaluateExpression(rule.AmountExpression, recordData);
                var amount = decimal.TryParse(amountText, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedAmount)
                    ? parsedAmount
                    : documentAmount;

                if (string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit))
                    throw new Exception($"Правило '{rule.Name}' не определило счета дебета и кредита");
                if (debit.Equals(credit, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Правило '{rule.Name}' сформировало одинаковые счета дебета и кредита");
                if (amount <= 0)
                    throw new Exception($"Правило '{rule.Name}' сформировало некорректную сумму");

                const string sql = @"
                    INSERT INTO ""doc_postings""
                    (""Id"", ""posting_date"", ""doc_number"", ""document_type"",
                     ""debit_account"", ""credit_account"", ""amount_kgs"", ""amount_currency"",
                     ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                    VALUES
                    (@id, @date, @number, @type, @debit, @credit, @amount, 0,
                     @description, true, NOW(), NOW())";

                await _context.Database.ExecuteSqlRawAsync(sql,
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@date", postingDate),
                    new NpgsqlParameter("@number", documentNumber),
                    new NpgsqlParameter("@type", document.Name),
                    new NpgsqlParameter("@debit", debit),
                    new NpgsqlParameter("@credit", credit),
                    new NpgsqlParameter("@amount", amount),
                    new NpgsqlParameter("@description", description));
            }

            await UpdateDocumentPostedStatus(document.TableName, recordId);
        }

        private async Task ProcessCashOrderAsync(MetadataObject document, Dictionary<string, object> recordData, Guid recordId, decimal amount)
        {
            bool isReceipt = document.Name.Contains("Приходный");
            System.Diagnostics.Debug.WriteLine($"IsReceipt: {isReceipt}");

            // Получаем ID кассы
            TryGetGuid(recordData, out var cashDeskId, "cash_desk_id", "Касса", "cashdesk_id", "cashdesk");

            if (cashDeskId == Guid.Empty)
                throw new Exception("Для проведения кассового документа выберите кассу.");

            System.Diagnostics.Debug.WriteLine($"CashDeskId: {cashDeskId}");

            // Получаем корреспондирующий счёт
            TryGetGuid(recordData, out var corrAccountId, "correspondent_account", "Корр. счет", "Корр. счёт");
            var corrAccountCode = GetStringValue(recordData, "correspondent_account", "Корр. счет", "Корр. счёт");
            var cashAccountCode = await GetCashDeskAccountCodeAsync(cashDeskId);
            if (string.IsNullOrWhiteSpace(cashAccountCode))
                throw new Exception("У выбранной кассы не указан счет. Откройте справочник касс и заполните поле \"Счет\".");

            // Старые записи могут содержать конкретную кассу. Новые документы работают по счету,
            // поэтому остаток справочника касс обновляем только когда касса явно указана.
            if (cashDeskId != Guid.Empty)
            {
                await UpdateCashDeskBalance(cashDeskId, amount, isReceipt);
                System.Diagnostics.Debug.WriteLine("Cash desk balance updated");
            }

            // Получаем данные документа
            string? docNumber = recordData.ContainsKey("doc_number") ? recordData["doc_number"].ToString() : (recordData.ContainsKey("Номер") ? recordData["Номер"].ToString() : "");
            docNumber = NormalizeLegacyDocumentNumber(docNumber);
            DateTime postingDate = recordData.ContainsKey("doc_date") ? (DateTime)recordData["doc_date"] :
                                  (recordData.ContainsKey("Дата") ? (DateTime)recordData["Дата"] : DateTime.Now);
            string? description = recordData.ContainsKey("basis") ? recordData["basis"].ToString() : (recordData.ContainsKey("Основание") ? recordData["Основание"].ToString() : "");


            string debitAccount = "";
            string creditAccount = "";
            string documentType = isReceipt ? "Приходный кассовый ордер" : "Расходный кассовый ордер";

            if (isReceipt)
            {
                debitAccount = cashAccountCode; // Счёт выбранной кассы
                creditAccount = corrAccountId != Guid.Empty ? await GetAccountCodeById(corrAccountId) : corrAccountCode;
            }
            else
            {
                debitAccount = corrAccountId != Guid.Empty ? await GetAccountCodeById(corrAccountId) : corrAccountCode;
                creditAccount = cashAccountCode; // Счёт выбранной кассы
            }

            if (string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount))
            {
                throw new Exception($"Для проведения кассового документа укажите корреспондирующий счет. Приход увеличивает кассу: Дт {cashAccountCode} / Кт корр.счет; расход уменьшает кассу: Дт корр.счет / Кт {cashAccountCode}.");
            }

            // Создаём проводку с указанием типа документа
            await CreatePosting(docNumber!, postingDate, debitAccount, creditAccount, amount, description!, documentType);            

            // Обновляем статус документа
            await UpdateDocumentPostedStatus(document.TableName, recordId);
        }

        private async Task ProcessPaymentOrderAsync(MetadataObject document, Dictionary<string, object> recordData, Guid recordId, decimal amount)
        {
            System.Diagnostics.Debug.WriteLine("Processing PaymentOrder");

            // Определяем тип платежа - используем правильный ключ!
            string orderType = recordData.ContainsKey("order_type") ? recordData["order_type"].ToString() :
                               (recordData.ContainsKey("Тип") ? recordData["Тип"].ToString() : "");

            bool isOutgoing = orderType.Contains("Исходящее");
            string documentType = isOutgoing ? "Исходящее платежное поручение" : "Входящее платежное поручение";

            System.Diagnostics.Debug.WriteLine($"orderType: {orderType}");
            System.Diagnostics.Debug.WriteLine($"isOutgoing: {isOutgoing}");
            System.Diagnostics.Debug.WriteLine($"documentType: {documentType}");

            // Получаем номер документа
            string docNumber = recordData.ContainsKey("doc_number") ? recordData["doc_number"].ToString() :
                               (recordData.ContainsKey("Номер") ? recordData["Номер"].ToString() : "");
            docNumber = NormalizeLegacyDocumentNumber(docNumber);
            if (string.IsNullOrEmpty(docNumber))
                docNumber = recordId.ToString().Substring(0, 8);

            // Получаем дату
            DateTime postingDate = recordData.ContainsKey("doc_date") && recordData["doc_date"] != null ?
                                  (DateTime)recordData["doc_date"] :
                                  (recordData.ContainsKey("Дата") && recordData["Дата"] != null ? (DateTime)recordData["Дата"] : DateTime.Now);

            // Получаем описание
            string? description = recordData.ContainsKey("purpose") ? recordData["purpose"].ToString() :
                                  (recordData.ContainsKey("Назначение платежа") ? recordData["Назначение платежа"].ToString() : "");
            if (string.IsNullOrEmpty(description) && recordData.ContainsKey("description"))
                description = recordData["description"].ToString();
            if (string.IsNullOrEmpty(description) && recordData.ContainsKey("Примечание"))
                description = recordData["Примечание"].ToString();

            // Получаем код нашего счёта
            string ourAccountCode = string.Empty;
            if (recordData.ContainsKey("our_account_id") && recordData["our_account_id"] != null)
            {
                if (Guid.TryParse(recordData["our_account_id"].ToString(), out var ourAccountId))
                {
                    ourAccountCode = await GetAccountCodeById(ourAccountId);
                }
                else
                {
                    ourAccountCode = recordData["our_account_id"].ToString();
                }
            }
            else if (recordData.ContainsKey("Наш счет") && recordData["Наш счет"] != null)
            {
                if (Guid.TryParse(recordData["Наш счет"].ToString(), out var ourAccountId))
                {
                    ourAccountCode = await GetAccountCodeById(ourAccountId);
                }
                else
                {
                    ourAccountCode = recordData["Наш счет"].ToString();
                }
            }
            if (string.IsNullOrWhiteSpace(ourAccountCode))
                throw new Exception("Для платежного поручения укажите наш счет.");

            // Получаем корреспондирующий счёт
            string corrAccountCode = "";
            if (recordData.ContainsKey("correspondent_account") && recordData["correspondent_account"] != null)
            {
                if (Guid.TryParse(recordData["correspondent_account"].ToString(), out var corrAccountId))
                {
                    corrAccountCode = await GetAccountCodeById(corrAccountId);
                }
                else
                {
                    corrAccountCode = recordData["correspondent_account"].ToString();
                }
            }
            else if (recordData.ContainsKey("Корр. счет") && recordData["Корр. счет"] != null)
            {
                if (Guid.TryParse(recordData["Корр. счет"].ToString(), out var corrAccountId))
                {
                    corrAccountCode = await GetAccountCodeById(corrAccountId);
                }
                else
                {
                    corrAccountCode = recordData["Корр. счет"].ToString();
                }
            }

            if (string.IsNullOrEmpty(corrAccountCode))
            {
                throw new Exception("Для платежного поручения укажите корреспондирующий счет.");
            }

            // Определяем счета для проводки
            string debitAccount, creditAccount;
            if (isOutgoing)
            {
                // Исходящее: Дт (корр. счёт) — Кт (наш счёт)
                debitAccount = corrAccountCode;
                creditAccount = ourAccountCode;
            }
            else
            {
                // Входящее: Дт (наш счёт) — Кт (корр. счёт)
                debitAccount = ourAccountCode;
                creditAccount = corrAccountCode;
            }

            System.Diagnostics.Debug.WriteLine($"debitAccount: {debitAccount}, creditAccount: {creditAccount}");

            // Создаём проводку с указанием типа документа
            await CreatePosting(docNumber, postingDate, debitAccount, creditAccount, amount, description, documentType);

            // Обновляем статус документа
            await UpdateDocumentPostedStatus(document.TableName, recordId);
        }

        private async Task CreatePosting(string docNumber, DateTime postingDate, string debitAccount, string creditAccount, decimal amount, string? description, string documentType = "")
        {
            try
            {
                var postingId = Guid.NewGuid();

                if (string.IsNullOrEmpty(documentType))
                {
                    documentType = "Бухгалтерская проводка";
                }

                var sql = @"
                    INSERT INTO doc_postings 
                    (""Id"", posting_date, doc_number, debit_account, credit_account, 
                     amount_kgs, description, document_type, is_active, ""CreatedAt"", ""UpdatedAt"") 
                    VALUES (
                        @id,
                        @postingDate,
                        @docNumber,
                        @debitAccount,
                        @creditAccount,
                        @amount,
                        @description,
                        @documentType,
                        @isActive,
                        NOW(),
                        NOW()
                    )";

                await _context.Database.ExecuteSqlRawAsync(
                    sql,
                    new NpgsqlParameter("@id", postingId),
                    new NpgsqlParameter("@postingDate", postingDate),
                    new NpgsqlParameter("@docNumber", docNumber),
                    new NpgsqlParameter("@debitAccount", debitAccount),
                    new NpgsqlParameter("@creditAccount", creditAccount),
                    new NpgsqlParameter("@amount", amount),
                    new NpgsqlParameter("@description", (object?)description ?? DBNull.Value),
                    new NpgsqlParameter("@documentType", documentType),
                    new NpgsqlParameter("@isActive", true));
                System.Diagnostics.Debug.WriteLine($"✅ Проводка создана: {documentType} | {docNumber} | {amount}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка CreatePosting: {ex.Message}");
                throw;
            }
        }

        private async Task UpdateDocumentPostedStatus(string tableName, Guid recordId)
        {
            var safeTableName = QuoteIdentifier(tableName);
            var updateSql = $@"
        UPDATE {safeTableName} 
        SET ""is_posted"" = true, ""UpdatedAt"" = NOW() 
        WHERE ""Id"" = @recordId";
            await _context.Database.ExecuteSqlRawAsync(updateSql, new NpgsqlParameter("@recordId", recordId));
        }

        private async Task UpdateCashDeskBalance(Guid cashDeskId, decimal amount, bool isIncrease)
        {
            var sql = $@"
            UPDATE ""catalog_cash_desks"" 
            SET ""current_balance"" = COALESCE(""current_balance"", 0) {(isIncrease ? "+" : "-")} @amount,
                ""UpdatedAt"" = NOW()
            WHERE ""Id"" = @cashDeskId";

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@amount", amount),
                new NpgsqlParameter("@cashDeskId", cashDeskId));
        }

        private async Task SyncManualPostingCashBalanceAsync(
            MetadataObject metadata,
            Dictionary<string, object>? previousRecord,
            Dictionary<string, object>? currentRecord)
        {
            if (!IsPostingsDocument(metadata))
                return;

            if (previousRecord != null)
                await ApplyManualPostingCashBalanceDeltaAsync(previousRecord, reverse: true);

            if (currentRecord != null)
                await ApplyManualPostingCashBalanceDeltaAsync(currentRecord, reverse: false);
        }

        private async Task ApplyManualPostingCashBalanceDeltaAsync(
            Dictionary<string, object> record,
            bool reverse)
        {
            if (!IsRecordActive(record))
                return;

            if (!TryGetGuid(record, out var cashDeskId, "cash_desk_id", "Касса"))
                return;

            var amount = GetDecimalValue(record, "amount_kgs", "Сумма в сом", "amount", "Сумма");
            if (amount == 0)
                return;

            var cashAccountCode = await GetCashDeskAccountCodeAsync(cashDeskId);
            if (string.IsNullOrWhiteSpace(cashAccountCode))
                return;

            var debitAccount = GetStringValue(record, "debit_account", "Дебет");
            var creditAccount = GetStringValue(record, "credit_account", "Кредит");
            var accountType = await GetAccountTypeByCodeAsync(cashAccountCode);
            var delta = CalculateAccountBalanceDelta(cashAccountCode, accountType, debitAccount, creditAccount, amount);

            if (delta == 0)
                return;

            if (reverse)
                delta = -delta;

            await UpdateCashDeskBalanceDeltaAsync(cashDeskId, delta);
        }

        private async Task<string> GetCashDeskAccountCodeAsync(Guid cashDeskId)
        {
            const string sql = @"
                SELECT ""code""
                FROM ""catalog_cash_desks""
                WHERE ""Id"" = @cashDeskId
                LIMIT 1;";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@cashDeskId";
            parameter.Value = cashDeskId;
            command.Parameters.Add(parameter);

            var connectionOpened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                return await ResolveAccountCodeValueAsync((await command.ExecuteScalarAsync())?.ToString());
            }
            finally
            {
                if (connectionOpened)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task<string> ResolveAccountCodeValueAsync(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
            if (Guid.TryParse(text, out var accountId))
                return await GetAccountCodeById(accountId);

            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        private async Task SyncCashDeskAccountReferencesAsync(Guid cashDeskId, string accountCode)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return;

            var documents = await _context.MetadataObjects
                .Where(item => item.ObjectType == "Document" &&
                    (item.Name == "Приходный кассовый ордер" || item.Name == "Расходный кассовый ордер"))
                .ToListAsync();

            foreach (var document in documents)
            {
                var isReceipt = document.Name == "Приходный кассовый ордер";
                var accountColumn = isReceipt ? "debit_account" : "credit_account";
                var tableName = QuoteIdentifier(document.TableName);

                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE {tableName}
                    SET ""{accountColumn}"" = @accountCode, ""UpdatedAt"" = NOW()
                    WHERE ""cash_desk_id"" = @cashDeskId;",
                    new NpgsqlParameter("@accountCode", accountCode),
                    new NpgsqlParameter("@cashDeskId", cashDeskId));

                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE doc_postings AS posting
                    SET {accountColumn} = @accountCode, ""UpdatedAt"" = NOW()
                    FROM {tableName} AS document
                    WHERE posting.document_type = @documentType
                      AND posting.doc_number = document.""doc_number""
                      AND document.""cash_desk_id"" = @cashDeskId;",
                    new NpgsqlParameter("@accountCode", accountCode),
                    new NpgsqlParameter("@documentType", document.Name),
                    new NpgsqlParameter("@cashDeskId", cashDeskId));
            }
        }

        private async Task<string> GetAccountTypeByCodeAsync(string accountCode)
        {
            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name.StartsWith("План счетов"));
            if (catalog == null)
                return "Active";

            var safeTableName = QuoteIdentifier(catalog.TableName);
            var sql = $@"
                SELECT ""account_type""
                FROM {safeTableName}
                WHERE ""code"" = @accountCode
                LIMIT 1;";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@accountCode";
            parameter.Value = accountCode;
            command.Parameters.Add(parameter);

            var connectionOpened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                return (await command.ExecuteScalarAsync())?.ToString() ?? "Active";
            }
            finally
            {
                if (connectionOpened)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task UpdateCashDeskBalanceDeltaAsync(Guid cashDeskId, decimal delta)
        {
            const string sql = @"
                UPDATE ""catalog_cash_desks""
                SET ""current_balance"" = COALESCE(""current_balance"", 0) + @delta,
                    ""UpdatedAt"" = NOW()
                WHERE ""Id"" = @cashDeskId;";

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@delta", delta),
                new NpgsqlParameter("@cashDeskId", cashDeskId));
        }

        private static decimal CalculateAccountBalanceDelta(
            string cashAccountCode,
            string accountType,
            string debitAccount,
            string creditAccount,
            decimal amount)
        {
            var isDebit = debitAccount.Equals(cashAccountCode, StringComparison.OrdinalIgnoreCase);
            var isCredit = creditAccount.Equals(cashAccountCode, StringComparison.OrdinalIgnoreCase);

            if (isDebit == isCredit)
                return 0m;

            var passive = IsPassiveAccountType(accountType);
            if (passive)
                return isCredit ? amount : -amount;

            return isDebit ? amount : -amount;
        }

        private static bool IsPassiveAccountType(string accountType)
        {
            return accountType.Equals("Passive", StringComparison.OrdinalIgnoreCase) ||
                   accountType.Equals("Пассивный", StringComparison.OrdinalIgnoreCase);
        }

        private static decimal GetDecimalValue(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!data.TryGetValue(key, out var raw) || raw == null || raw == DBNull.Value)
                    continue;

                if (raw is decimal decimalValue)
                    return decimalValue;

                if (decimal.TryParse(
                        raw.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.CurrentCulture,
                        out var currentCultureValue))
                    return currentCultureValue;

                if (decimal.TryParse(
                        raw.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var invariantValue))
                    return invariantValue;
            }

            return 0m;
        }

        private static bool IsRecordActive(Dictionary<string, object> data)
        {
            foreach (var key in new[] { "is_active", "Активен" })
            {
                if (!data.TryGetValue(key, out var raw) || raw == null || raw == DBNull.Value)
                    continue;

                if (raw is bool boolValue)
                    return boolValue;

                if (bool.TryParse(raw.ToString(), out var parsed))
                    return parsed;

                var text = raw.ToString();
                if (text?.Equals("Да", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
                if (text?.Equals("Нет", StringComparison.OrdinalIgnoreCase) == true)
                    return false;
            }

            return true;
        }

        private static bool TryGetGuid(Dictionary<string, object> data, out Guid value, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var raw) && raw != null && raw != DBNull.Value &&
                    Guid.TryParse(raw.ToString(), out value))
                    return true;
            }

            value = Guid.Empty;
            return false;
        }

        private static string GetStringValue(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var raw) && raw != null && raw != DBNull.Value)
                    return raw.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private async Task<string> GetAccountCodeById(Guid accountId)
        {
            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name.StartsWith("План счетов"));

            if (catalog == null) return "";

            var safeTableName = QuoteIdentifier(catalog.TableName);
            var sql = $"SELECT \"code\" FROM {safeTableName} WHERE \"Id\" = @accountId";
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@accountId";
            parameter.Value = accountId;
            command.Parameters.Add(parameter);

            var connectionOpened = false;

            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                var result = await command.ExecuteScalarAsync();
                return result?.ToString() ?? "";
            }
            finally
            {
                if (connectionOpened)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }


        private async Task CreateDocumentNumberingTableAsync()
        {
            try
            {
                // Проверяем существование таблицы
                var checkSql = @"
                SELECT COUNT(*) FROM information_schema.tables 
                WHERE table_name = 'doc_numbering'";

                using var checkCommand = _context.Database.GetDbConnection().CreateCommand();
                checkCommand.CommandText = checkSql;
                await _context.Database.OpenConnectionAsync();
                var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                await _context.Database.CloseConnectionAsync();

                if (exists > 0)
                {
                    return;
                }

                // Создаём таблицу
                var createSql = @"
                CREATE TABLE IF NOT EXISTS doc_numbering (
                    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    document_type VARCHAR(100) NOT NULL,
                    current_number INTEGER DEFAULT 1,
                    prefix VARCHAR(20) DEFAULT '',
                    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
            
                -- Создаём уникальный индекс для document_type
                CREATE UNIQUE INDEX IF NOT EXISTS idx_doc_numbering_type 
                ON doc_numbering (document_type);";

                await _context.Database.ExecuteSqlRawAsync(createSql);
                System.Diagnostics.Debug.WriteLine("Таблица doc_numbering создана");

                // Добавляем начальные записи
                await AddDefaultNumberingRecordsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания таблицы doc_numbering: {ex.Message}");
            }
        }

        private async Task AddDefaultNumberingRecordsAsync()
        {
            const string insertSql = @"
                INSERT INTO doc_numbering (document_type, current_number, prefix) 
                VALUES (@documentType, 1, '')
                ON CONFLICT (document_type) DO NOTHING";

            await _context.Database.ExecuteSqlRawAsync(
                insertSql,
                new NpgsqlParameter("@documentType", GlobalDocumentNumberingKey));
        }       

        public async Task<string> GetNextDocumentNumberAsync(string documentName)
        {
            try
            {
                await EnsureGlobalDocumentNumberConfigurationAsync();

                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = @"
                UPDATE doc_numbering
                SET current_number = current_number + 1, UpdatedAt = NOW()
                WHERE document_type = @documentType
                RETURNING current_number - 1";

                var documentTypeParameter = command.CreateParameter();
                documentTypeParameter.ParameterName = "@documentType";
                documentTypeParameter.Value = GlobalDocumentNumberingKey;
                command.Parameters.Add(documentTypeParameter);

                var connectionOpened = false;

                try
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;

                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return reader.GetInt32(0).ToString();
                    }
                }
                finally
                {
                    if (connectionOpened)
                    {
                        await _context.Database.CloseConnectionAsync();
                    }
                }

                throw new Exception("Не удалось получить следующий номер документа");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения номера документа: {ex.Message}");
                return GenerateFallbackDocumentNumber();
            }
        }

        private async Task EnsureGlobalDocumentNumberConfigurationAsync(List<MetadataObject>? documents = null)
        {
            await CreateDocumentNumberingTableAsync();
            documents ??= await LoadDocumentMetadataAsync();
            var nextNumber = await GetSuggestedNextGlobalDocumentNumberAsync(documents);

            const string insertSql = @"
                INSERT INTO doc_numbering (document_type, current_number, prefix)
                VALUES (@documentType, @currentNumber, '')
                ON CONFLICT (document_type) DO NOTHING";

            await _context.Database.ExecuteSqlRawAsync(
                insertSql,
                new NpgsqlParameter("@documentType", GlobalDocumentNumberingKey),
                new NpgsqlParameter("@currentNumber", nextNumber));

            const string updateSql = @"
                UPDATE doc_numbering
                SET prefix = '', current_number = GREATEST(current_number, @currentNumber), UpdatedAt = NOW()
                WHERE document_type = @documentType
                  AND (COALESCE(prefix, '') <> '' OR current_number < @currentNumber)";

            await _context.Database.ExecuteSqlRawAsync(
                updateSql,
                new NpgsqlParameter("@documentType", GlobalDocumentNumberingKey),
                new NpgsqlParameter("@currentNumber", nextNumber));

            const string cleanupSql = @"
                DELETE FROM doc_numbering
                WHERE document_type <> @documentType";

            await _context.Database.ExecuteSqlRawAsync(
                cleanupSql,
                new NpgsqlParameter("@documentType", GlobalDocumentNumberingKey));
        }

        private async Task<int> GetSuggestedNextGlobalDocumentNumberAsync(IEnumerable<MetadataObject> documents)
        {
            long maxDocumentNumber = 0;
            long maxCounterNumber = 1;
            var connectionOpened = false;
            var connection = _context.Database.GetDbConnection();

            try
            {
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    connectionOpened = true;
                }

                foreach (var document in documents.Where(IsManagedDocument))
                {
                    var numberField = FindDocumentNumberField(document);
                    if (numberField == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (string.IsNullOrWhiteSpace(document.TableName) ||
                            string.IsNullOrWhiteSpace(numberField.DbColumnName))
                        {
                            continue;
                        }

                        var quotedTableName = DelimitIdentifier(document.TableName);
                        var quotedColumnName = DelimitIdentifier(numberField.DbColumnName);

                        using var maxCommand = connection.CreateCommand();
                        maxCommand.CommandText = $@"
                            SELECT COALESCE(
                                MAX(COALESCE(NULLIF(REGEXP_REPLACE(COALESCE({quotedColumnName}::text, ''), '\D', '', 'g'), ''), '0')::BIGINT),
                                0)
                            FROM {quotedTableName}";

                        var maxValue = await maxCommand.ExecuteScalarAsync();
                        if (maxValue != null && maxValue != DBNull.Value)
                        {
                            maxDocumentNumber = Math.Max(maxDocumentNumber, Convert.ToInt64(maxValue));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Ошибка расчета номера для {document.Name}: {ex.Message}");
                    }
                }

                using var counterCommand = connection.CreateCommand();
                counterCommand.CommandText = "SELECT COALESCE(MAX(current_number), 1) FROM doc_numbering";
                var counterValue = await counterCommand.ExecuteScalarAsync();
                if (counterValue != null && counterValue != DBNull.Value)
                {
                    maxCounterNumber = Convert.ToInt64(counterValue);
                }
            }
            finally
            {
                if (connectionOpened)
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

            var nextNumber = Math.Max(maxCounterNumber, maxDocumentNumber + 1);
            return nextNumber > int.MaxValue ? int.MaxValue : (int)nextNumber;
        }

        private async Task NormalizeDocumentTableNumbersAsync(MetadataObject document)
        {
            if (!IsManagedDocument(document))
            {
                return;
            }

            var numberField = FindDocumentNumberField(document);
            if (numberField == null)
            {
                return;
            }

            var quotedTableName = DelimitIdentifier(document.TableName);
            var quotedColumnName = DelimitIdentifier(numberField.DbColumnName);
            var normalizeSql = $@"
                UPDATE {quotedTableName}
                SET {quotedColumnName} = REGEXP_REPLACE(COALESCE({quotedColumnName}::text, ''), '\D', '', 'g')
                WHERE COALESCE({quotedColumnName}::text, '') <> ''
                  AND COALESCE({quotedColumnName}::text, '') ~ '[^0-9]';";

            await _context.Database.ExecuteSqlRawAsync(normalizeSql);
        }

        private static bool IsManagedDocument(MetadataObject metadata)
        {
            return metadata.ObjectType == "Document" && FindDocumentNumberField(metadata) != null;
        }

        private static bool IsPostingsDocument(MetadataObject metadata)
        {
            return metadata.ObjectType == "Document" &&
                   metadata.Name.Equals("Проводки", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCashDeskCatalog(MetadataObject metadata)
        {
            return metadata.ObjectType == "Catalog" &&
                   metadata.Name.Equals("Кассы", StringComparison.OrdinalIgnoreCase);
        }

        private static MetadataField? FindDocumentNumberField(MetadataObject metadata)
        {
            return metadata.Fields.FirstOrDefault(field =>
                IsDocumentNumberFieldName(field.Name) || IsDocumentNumberColumnName(field.DbColumnName));
        }

        internal static bool IsDocumentNumberFieldName(string? fieldName)
        {
            return fieldName is "Номер" or "Номер документа" or "doc_number" or "number";
        }

        private static bool IsDocumentNumberColumnName(string? columnName)
        {
            return string.Equals(columnName, "doc_number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(columnName, "number", StringComparison.OrdinalIgnoreCase);
        }

        private static string DelimitIdentifier(string identifier)
        {
            return QuoteIdentifier(identifier);
        }

        internal static string NormalizeLegacyDocumentNumber(string? documentNumber)
        {
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                return string.Empty;
            }

            var normalizedNumber = documentNumber.Trim();

            var digitsOnly = new string(normalizedNumber.Where(char.IsDigit).ToArray());
            return string.IsNullOrEmpty(digitsOnly) ? normalizedNumber : digitsOnly;
        }

        internal static string GenerateFallbackDocumentNumber()
        {
            return DateTime.Now.ToString("yyyyMMddHHmmssfff");
        }

        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            {
                throw new ArgumentException("Некорректный SQL идентификатор", nameof(identifier));
            }

            return $"\"{identifier}\"";
        }


    }
}
