# Сверка миграций перед обновлением партнёров

Проверка активной инфобазы выполнена в режиме PostgreSQL `default_transaction_read_only=on`. Схема, данные и журнал миграций не изменялись.

## Результаты сверки

| Проверка | Результат |
| --- | --- |
| Миграции Entity Framework | В сборке зарегистрированы три миграции. В активной инфобазе таблицы `__EFMigrationsHistory` нет. Проверены 484 колонки текущей модели, отсутствующих колонок не найдено |
| Динамические таблицы | У зарегистрированных справочников и документов не найдено отсутствующих таблиц или колонок, указанных в метаданных |
| Журнал приложения | 28 записей, из них 24 соответствуют зарегистрированным шагам и 4 относятся к отдельным посадкам данных |
| Колонки и представления ТМЗ | Колонки `credit_account_code`, `inventory_cost` и представления движений и остатков уже есть. Шаги `2026.11.10.01–03` ещё не отмечены в журнале |
| Карточка материала | Полное наименование и два кода классификаторов описаны в исходниках, но физических колонок на проверенной базе пока нет |
| Строки документов ТМЗ | У поступления, выбытия и перемещения есть каскадные внешние ключи на шапку |

Запись `2026.11.06.02` в журнале относится к `ImportInitialMaterialsAsync`. Остальные три отдельные записи — посадки документации, счетов-фактур и стандартных отчётов. Переносить их в список схемных шагов или запускать повторно не требуется.

В проверенной базе получатель перемещения хранится в текстовых колонках, а в части других таблиц аналогичные колонки имеют тип UUID. Представление движений уже учитывает оба варианта. Приведение старых колонок к другому типу в эту правку не включено.

## Правки в коде

- В [SchemaMigrationService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/SchemaMigrationService.cs>) добавлен шаг `2026.11.10.04` для справочника материалов. Существующие колонки проверяются отдельно от метаданных. Недостающие колонки и изменения метаданных записываются в одной транзакции.
- Виды материалов и представления остатков перенесены на этап после создания предустановленных справочников. Представление зависит от плана счетов. На новой базе прежний порядок мог вызвать ошибку до создания плана счетов или записать отметку о видах материалов до появления соответствующей таблицы.
- Прогоны журнала сериализуются транзакционной блокировкой PostgreSQL. Параллельные экземпляры приложения не применяют разовые шаги одновременно.
- Ошибки при добавлении колонок теперь возвращаются в журнал миграций как ошибки шага. После неудачного прогона приложение может повторить его в том же процессе.
- Добавление колонок себестоимости и обновление представлений выполняются одним пакетом. Существующие колонки и данные не удаляются.

```csharp
new("2026.11.10.04",
    "Полное наименование и коды классификаторов справочника материалов",
    false,
    ctx => new MetadataService(ctx).EnsureMaterialCatalogStructureAsync(), AfterCatalogs: true)
```

## Проверка перед выпуском

Сборка приложения прошла. На копии базы партнёра следует проверить запуск обновлённой сборки, журнал `schema_migrations`, сохранение карточки материала и открытие «Остатков материалов». Отдельный сценарий — создание пустой инфобазы, чтобы проверить порядок появления справочников и представлений. Эти сценарии с применением миграций на копии в рамках проверки на чтение не выполнялись. Отсутствие истории Entity Framework в активной базе не даёт оснований повторно запускать на ней начальную миграцию.

## Полный код изменений

Ниже приведён полный diff шести изменённых файлов относительно исходной версии. Строки с минусом показывают прежний код, строки с плюсом — итоговый код. У каждого фрагмента указана причина изменения.

### Реестр и выполнение миграций

Раньше все шаги запускались одним проходом. На новой базе справочники и план счетов могли ещё отсутствовать. Теперь зависимые шаги запускаются после создания справочников, а отсутствие таблицы видов материалов считается ошибкой и не отмечается как успешное выполнение. Блокировка PostgreSQL защищает чтение журнала, выполнение шагов и запись результата от параллельного прогона. Колонки себестоимости и представления обновляются одним SQL-пакетом. Шаг 2026.11.10.04 добавляет недостающие поля карточки материала при следующем запуске миграций.

[SchemaMigrationService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/SchemaMigrationService.cs>)

```diff
diff --git a/Services/SchemaMigrationService.cs b/Services/SchemaMigrationService.cs
index 7cf7301..71ab09f 100644
--- a/Services/SchemaMigrationService.cs
+++ b/Services/SchemaMigrationService.cs
@@ -27,7 +27,8 @@ namespace BIS.ERP.Services
             string Version,
             string Description,
             bool Repeatable,
-            Func<AppDbContext, Task> Apply);
+            Func<AppDbContext, Task> Apply,
+            bool AfterCatalogs = false);
 
         private readonly AppDbContext _context;
         private readonly List<Step> _steps;
@@ -98,7 +99,7 @@ namespace BIS.ERP.Services
                     EnsureInventoryEntryOperationKindsAsync),
 
                 new("2026.10.22.02",
-                    "doc_postings: colonnes document_table/document_id (восстановление схемы при отсутствии)",
+                    "doc_postings: колонки document_table/document_id (восстановление схемы при отсутствии)",
                     true,
                     EnsurePostingDocumentTableColumnsAsync),
 
@@ -160,7 +161,7 @@ namespace BIS.ERP.Services
                 new("2026.11.06.01",
                     "Виды материалов: замена начальных данных эталонным списком",
                     false,
-                    ReplaceMaterialTypesAsync),
+                    ReplaceMaterialTypesAsync, AfterCatalogs: true),
 
                 new("2026.11.09.01",
                     "Проводки: счёт-ссылка (uuid) в дебете/кредите заменяется кодом счёта",
@@ -175,12 +176,17 @@ namespace BIS.ERP.Services
                 new("2026.11.10.02",
                     "Движения и остатки материалов по проведённым документам",
                     false,
-                    EnsureInventoryValuationSchemaAsync),
+                    EnsureInventoryValuationSchemaAsync, AfterCatalogs: true),
 
                 new("2026.11.10.03",
                     "Себестоимость в строках выбытия и перемещения материалов",
                     false,
-                    EnsureInventoryValuationSchemaAsync)
+                    EnsureInventoryValuationSchemaAsync, AfterCatalogs: true),
+
+                new("2026.11.10.04",
+                    "Полное наименование и коды классификаторов справочника материалов",
+                    false,
+                    ctx => new MetadataService(ctx).EnsureMaterialCatalogStructureAsync(), AfterCatalogs: true)
             };
         }
 
@@ -223,7 +229,7 @@ namespace BIS.ERP.Services
             if (!await context.Database.SqlQueryRaw<bool>(
                     "SELECT (to_regclass('public.catalog_material_types') IS NOT NULL) AS \"Value\"")
                 .SingleAsync())
-                return;
+                throw new InvalidOperationException("Отсутствует таблица справочника видов материалов.");
 
             await using var transaction = await context.Database.BeginTransactionAsync();
             await context.Database.ExecuteSqlRawAsync(
@@ -581,8 +587,17 @@ namespace BIS.ERP.Services
                 END $$;");
         }
 
-        public async Task<SchemaMigrationReport> EnsureAsync()
+        public async Task<SchemaMigrationReport> EnsureAsync(bool afterCatalogs = false)
         {
+            var connectionString = _context.Database.GetConnectionString()
+                ?? throw new InvalidOperationException("Не задана строка подключения для миграций.");
+            await using var lockConnection = new NpgsqlConnection(connectionString);
+            await lockConnection.OpenAsync();
+            await using var migrationLock = await lockConnection.BeginTransactionAsync();
+            await using var lockCommand = new NpgsqlCommand(
+                "SELECT pg_advisory_xact_lock(3606, 2);", lockConnection, migrationLock);
+            await lockCommand.ExecuteNonQueryAsync();
+
             await EnsureJournalAsync();
 
             var alreadyApplied = await LoadAppliedAsync();
@@ -590,7 +605,7 @@ namespace BIS.ERP.Services
             var skipped = 0;
             var failures = new List<string>();
 
-            foreach (var step in _steps)
+            foreach (var step in _steps.Where(step => step.AfterCatalogs == afterCatalogs))
             {
                 if (!step.Repeatable && alreadyApplied.Contains(step.Version))
                 {
@@ -742,11 +757,9 @@ namespace BIS.ERP.Services
                 SystemLogService.Warning($"ТМЗ: {warning}", "Migration");
         }
 
-        private static async Task EnsureInventoryValuationSchemaAsync(AppDbContext context)
-        {
-            await context.Database.ExecuteSqlRawAsync(InventoryBalanceService.EnsureCostColumnsSql);
-            await context.Database.ExecuteSqlRawAsync(InventoryBalanceService.CreateViewsSql);
-        }
+        private static Task EnsureInventoryValuationSchemaAsync(AppDbContext context) =>
+            context.Database.ExecuteSqlRawAsync(
+                InventoryBalanceService.EnsureCostColumnsSql + "\n" + InventoryBalanceService.CreateViewsSql);
 
         private static Task EnsureReceiptCreditAccountColumnAsync(AppDbContext context) =>
             context.Database.ExecuteSqlRawAsync(
```

### Структура карточки материала

Раньше добавление физической колонки зависело от отсутствия поля в метаданных. Если поле уже было зарегистрировано, отсутствующая колонка могла остаться незамеченной. Теперь физические колонки и метаданные проверяются отдельно. Изменения сохраняются в одной транзакции. Существующие колонки и значения сохраняются.

[MetadataService.Catalogs.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/MetadataService.Catalogs.cs>)

```diff
diff --git a/Services/MetadataService.Catalogs.cs b/Services/MetadataService.Catalogs.cs
index 3d9bddf..85c3375 100644
--- a/Services/MetadataService.Catalogs.cs
+++ b/Services/MetadataService.Catalogs.cs
@@ -506,7 +506,7 @@ namespace BIS.ERP.Services
         /// Колонки остаются текстовыми — в них хранится код счёта, поэтому
         /// накопленные значения читаются без миграции.
         /// </summary>
-        private async Task EnsureMaterialCatalogStructureAsync()
+        internal async Task EnsureMaterialCatalogStructureAsync()
         {
             var catalog = await _context.MetadataObjects
                 .Include(metadata => metadata.Fields)
@@ -516,7 +516,12 @@ namespace BIS.ERP.Services
                      metadata.TableName == "catalog_materials"));
 
             if (catalog == null)
-                return;
+                throw new InvalidOperationException("Отсутствуют метаданные справочника материалов.");
+
+            await using var transaction = await _context.Database.BeginTransactionAsync();
+            var schema = await DatabaseSchemaCache.LoadAsync(_context.Database.GetConnectionString()!);
+            if (!schema.TryGetColumns(catalog.TableName, out var physicalColumns))
+                throw new InvalidOperationException("Отсутствует таблица справочника материалов.");
 
             var existingByColumn = catalog.Fields
                 .Where(field => !string.IsNullOrWhiteSpace(field.DbColumnName))
@@ -525,12 +530,28 @@ namespace BIS.ERP.Services
 
             foreach (var desired in GetMaterialFields(catalog.Id))
             {
+                if (!physicalColumns.ContainsKey(desired.DbColumnName))
+                {
+                    var fieldType = GetSqlTypeForField(desired);
+                    var required = desired.IsRequired && desired.FieldType != "Reference";
+                    var constraint = required
+                        ? desired.FieldType switch
+                        {
+                            "String" => " NOT NULL DEFAULT ''",
+                            "Bool" => " NOT NULL DEFAULT false",
+                            _ => throw new InvalidOperationException($"Укажите значение по умолчанию для поля «{desired.Name}».")
+                        }
+                        : string.Empty;
+                    await _context.Database.ExecuteSqlRawAsync(
+                        $"ALTER TABLE public.{QuoteIdentifier(catalog.TableName)} " +
+                        $"ADD COLUMN IF NOT EXISTS {QuoteIdentifier(desired.DbColumnName)} {fieldType}{constraint}");
+                }
+
                 if (!existingByColumn.TryGetValue(desired.DbColumnName, out var existing))
                 {
                     desired.Id = Guid.NewGuid();
                     desired.MetadataObjectId = catalog.Id;
                     await _context.MetadataFields.AddAsync(desired);
-                    await AddColumnToTableAsync(catalog.TableName, desired);
                     catalog.Fields.Add(desired);
                     continue;
                 }
@@ -551,6 +572,7 @@ namespace BIS.ERP.Services
             }
 
             await _context.SaveChangesAsync();
+            await transaction.CommitAsync();
         }
 
         private async Task CreateMaterialCategoriesCatalog(MetadataConfiguration config)
```

### Порядок запуска после справочников

Поздний этап запускается после создания плана счетов, видов материалов и справочника материалов. Отдельный контекст изолирует его изменения от состояния инициализации справочников. Импорт начальных материалов выполняется после этого этапа и сохраняет собственную отметку.

[MetadataService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/MetadataService.cs>)

```diff
diff --git a/Services/MetadataService.cs b/Services/MetadataService.cs
index 555c965..a4aec34 100644
--- a/Services/MetadataService.cs
+++ b/Services/MetadataService.cs
@@ -1515,7 +1515,12 @@ namespace BIS.ERP.Services
 
                 if (!existingCatalogs.Contains("Справочник материалов"))
                     await CreateMaterialCatalog(config);
-                await EnsureMaterialCatalogStructureAsync();
+                await using (var migrationContext = CreateIndependentContext())
+                {
+                    await StartupProfiler.MeasureAsync("Миграции после создания справочников",
+                        () => new SchemaMigrationService(migrationContext).EnsureAsync(afterCatalogs: true));
+                }
+
                 try
                 {
                     await SchemaMigrationService.ImportInitialMaterialsAsync(_context);
```

### Повтор после сбоя

Раньше соединение попадало в список обработанных даже при ошибках. Теперь неудачный прогон остаётся доступным для повторения в том же процессе. Ошибка запуска пишется в системный лог.

[RuntimeSchemaFixService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/RuntimeSchemaFixService.cs>)

```diff
diff --git a/Services/RuntimeSchemaFixService.cs b/Services/RuntimeSchemaFixService.cs
index d5480d9..479f9ba 100644
--- a/Services/RuntimeSchemaFixService.cs
+++ b/Services/RuntimeSchemaFixService.cs
@@ -96,11 +96,14 @@ namespace BIS.ERP.Services
             // (колонки EF, колонки метаданных, схемы касс/наборов, разовые чистки).
             try
             {
-                await new SchemaMigrationService(_context).EnsureAsync();
+                var report = await new SchemaMigrationService(_context).EnsureAsync();
+                if (report.Failures.Count > 0)
+                    return;
             }
             catch (Exception ex)
             {
-                System.Diagnostics.Debug.WriteLine($"[migration] миграции не выполнены: {ex.Message}");
+                SystemLogService.Warning($"Миграции схемы не завершены: {ex.Message}", "Migration");
+                return;
             }
 
 
```

### Ошибки восстановления колонок Entity Framework

Раньше сбой добавления отдельной колонки логировался внутри восстановления, а сам шаг мог получить отметку об успехе. Теперь ошибки собираются и передаются вызывающему коду. Уже добавленные колонки сохраняются, а повторный прогон пропускает их.

[SchemaRepairService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/SchemaRepairService.cs>)

```diff
diff --git a/Services/SchemaRepairService.cs b/Services/SchemaRepairService.cs
index ed15655..e1f289d 100644
--- a/Services/SchemaRepairService.cs
+++ b/Services/SchemaRepairService.cs
@@ -27,6 +27,7 @@ namespace BIS.ERP.Services
         public async Task<int> RepairAsync()
         {
             var added = 0;
+            var failures = new List<Exception>();
             var connection = _context.Database.GetDbConnection();
             var openedHere = connection.State != System.Data.ConnectionState.Open;
             if (openedHere)
@@ -74,8 +75,8 @@ namespace BIS.ERP.Services
                         }
                         catch (Exception ex)
                         {
-                            SystemLogService.Warning(
-                                $"Схема: не удалось добавить {tableName}.{columnName}: {ex.Message}", "Schema");
+                            failures.Add(new InvalidOperationException(
+                                $"Не удалось добавить {tableName}.{columnName}: {ex.Message}", ex));
                         }
                     }
                 }
@@ -86,6 +87,9 @@ namespace BIS.ERP.Services
                     await connection.CloseAsync();
             }
 
+            if (failures.Count > 0)
+                throw new AggregateException("Не удалось достроить колонки EF-модели.", failures);
+
             return added;
         }
 
```

### Ошибки восстановления динамических таблиц

Тот же принцип применяется к колонкам справочников и документов из метаданных. Ошибка отдельной колонки не прерывает проверку остальных полей, но весь шаг считается неуспешным и может повториться.

[MetadataSchemaRepairService.cs](<D:/C#/BIS.ERP/BIS.ERP/Services/MetadataSchemaRepairService.cs>)

```diff
diff --git a/Services/MetadataSchemaRepairService.cs b/Services/MetadataSchemaRepairService.cs
index a1ab9a0..c98fe2e 100644
--- a/Services/MetadataSchemaRepairService.cs
+++ b/Services/MetadataSchemaRepairService.cs
@@ -33,6 +33,7 @@ namespace BIS.ERP.Services
         {
             var tablesChecked = 0;
             var columnsAdded = 0;
+            var failures = new List<Exception>();
 
             var objects = await _context.Set<MetadataObject>()
                 .Include(item => item.Fields)
@@ -79,12 +80,15 @@ namespace BIS.ERP.Services
                     }
                     catch (Exception ex)
                     {
-                        SystemLogService.Warning(
-                            $"Схема: не удалось добавить {tableName}.{columnName}: {ex.Message}", "Schema");
+                        failures.Add(new InvalidOperationException(
+                            $"Не удалось добавить {tableName}.{columnName}: {ex.Message}", ex));
                     }
                 }
             }
 
+            if (failures.Count > 0)
+                throw new AggregateException("Не удалось достроить колонки по метаданным.", failures);
+
             return (tablesChecked, columnsAdded);
         }
 
```
