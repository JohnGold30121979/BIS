using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    /// <summary>Что сделал слой схемы документов ТМЗ за один прогон.</summary>
    public sealed class InventorySchemaReport
    {
        public List<string> Actions { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Приводит базу и метаданные к модели <see cref="InventoryDocumentModel"/>:
    /// три документа (Поступление ТМЗ / Выбытие ТМЗ / Внутреннее перемещение ТМЗ),
    /// у каждого — таблица строк, вид операции — реквизит шапки.
    ///
    /// Идемпотентно: на уже приведённой базе не делает DDL и не меняет данные.
    /// Прежние документы переименовываются (doc_incoming → doc_inventory_incoming),
    /// «лишние» документы-признаки удаляются вместе с пустыми таблицами.
    /// </summary>
    public sealed class InventoryDocumentSchemaService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;

        public InventoryDocumentSchemaService(AppDbContext context)
        {
            _context = context;
            _metadataService = new MetadataService(context);
        }

        public async Task<InventorySchemaReport> EnsureAsync()
        {
            var report = new InventorySchemaReport();

            // Сервис работает в отдельном контексте, но страхуемся от «наследства»:
            // чужие отслеживаемые записи не должны попасть в наши SaveChanges.
            _context.ChangeTracker.Clear();

            await EnsureEntryCatalogAsync(report);

            var createdDocuments = new List<string>();
            foreach (var definition in InventoryDocumentModel.Documents)
            {
                if (await EnsureDocumentAsync(definition, report))
                    createdDocuments.Add(definition.Name);
            }

            await RemoveObsoleteDocumentsAsync(report);
            await EnsureLinesTablesAsync(report);

            // В модуль привязываем только те документы, которые созданы этим прогоном.
            // Иначе ручное снятие галочки в конфигураторе откатывалось при каждом старте.
            await EnsureModuleAssignmentsAsync(createdDocuments, report);

            await _context.SaveChangesAsync();
            return report;
        }

        // ---------- Справочник видов операций ----------

        private async Task EnsureEntryCatalogAsync(InventorySchemaReport report)
        {
            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" && item.Name == InventoryDocumentModel.EntryCatalogName);

            if (catalog == null)
            {
                catalog = new MetadataObject
                {
                    Name = InventoryDocumentModel.EntryCatalogName,
                    TableName = InventoryDocumentModel.EntryCatalogTable,
                    ObjectType = "Catalog",
                    Description = "Виды операций документов ТМЗ",
                    Icon = "📝",
                    Order = 11,
                    IsSystem = true
                };

                foreach (var field in InventoryDocumentModel.EntryCatalogFields(catalog.Id))
                    catalog.Fields.Add(field);

                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await _metadataService.CreateDynamicTableAsync(catalog);

                report.Actions.Add($"Создан справочник «{catalog.Name}» ({catalog.TableName})");
            }
            else
            {
                if (!await TableExistsAsync(catalog.TableName))
                {
                    await _metadataService.CreateDynamicTableAsync(catalog);
                    report.Actions.Add($"«{catalog.Name}»: создана таблица {catalog.TableName}");
                }

                await EnsureColumnsAsync(catalog, InventoryDocumentModel.EntryCatalogFields(catalog.Id), report);
            }

            await SeedKindsAsync(catalog, report);
        }

        private async Task SeedKindsAsync(MetadataObject catalog, InventorySchemaReport report)
        {
            var table = QuoteIdentifier(catalog.TableName);

            foreach (var kind in InventoryDocumentModel.Kinds)
            {
                var exists = await ScalarExistsAsync(
                    $"SELECT COUNT(*)::int AS \"Value\" FROM {table} WHERE \"code\" = @code",
                    new NpgsqlParameter("code", kind.Code));

                if (exists)
                    continue;

                var sql =
                    $"INSERT INTO {table} (\"Id\", \"code\", \"name\", \"document_name\", \"movement_kind\", " +
                    "\"operation_kind\", \"sort_order\", \"description\", \"is_active\") " +
                    "VALUES (@id, @code, @name, @document_name, @movement_kind, @operation_kind, @sort_order, @description, true)";

                await _context.Database.ExecuteSqlRawAsync(
                    sql,
                    new NpgsqlParameter("id", Guid.NewGuid()),
                    new NpgsqlParameter("code", kind.Code),
                    new NpgsqlParameter("name", kind.Name),
                    new NpgsqlParameter("document_name", kind.DocumentName),
                    new NpgsqlParameter("movement_kind", kind.MovementKind),
                    new NpgsqlParameter("operation_kind", kind.OperationKind),
                    new NpgsqlParameter("sort_order", kind.Order),
                    new NpgsqlParameter("description", (object?)kind.Description ?? DBNull.Value));

                report.Actions.Add($"Вид операции «{kind.Name}» → {kind.DocumentName}");
            }

            // Старые строки (без кода операции) получают код из своего code.
            await _context.Database.ExecuteSqlRawAsync(
                $"UPDATE {table} SET \"operation_kind\" = \"code\" " +
                "WHERE COALESCE(btrim(\"operation_kind\"), '') = '';");
        }

        /// <summary>
        /// Точечная миграция справочника видов операций: колонка operation_kind и её заполнение.
        /// Используется шагом миграции, чтобы не гонять весь сервис.
        /// </summary>
        public async Task EnsureEntryCatalogOperationKindAsync()
        {
            var report = new InventorySchemaReport();
            await EnsureEntryCatalogAsync(report);
        }

        // ---------- Документы ----------

        /// <returns><c>true</c>, если документ создан этим прогоном (нужна привязка к модулю).</returns>
        private async Task<bool> EnsureDocumentAsync(
            InventoryDocumentModel.DocumentDefinition definition,
            InventorySchemaReport report)
        {
            var document = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Document" && item.Name == definition.Name);

            if (document == null)
                document = await RenameLegacyDocumentAsync(definition, report);

            if (document == null)
            {
                document = new MetadataObject
                {
                    Name = definition.Name,
                    TableName = definition.Table,
                    ObjectType = "Document",
                    Description = definition.Description,
                    Icon = definition.Icon,
                    Order = definition.Order,
                    IsSystem = true,
                    UsePostings = true
                };

                foreach (var field in InventoryDocumentModel.HeaderFields(document.Id))
                    document.Fields.Add(field);

                await _context.MetadataObjects.AddAsync(document);
                await _context.SaveChangesAsync();
                await _metadataService.CreateDynamicTableAsync(document);

                report.Actions.Add($"Создан документ «{definition.Name}» ({definition.Table})");
                return true;
            }

            document.UsePostings = true;

            if (!await TableExistsAsync(document.TableName))
            {
                await _metadataService.CreateDynamicTableAsync(document);
                report.Actions.Add($"«{document.Name}»: создана таблица {document.TableName}");
            }

            await EnsureHeaderColumnsAsync(document, report);
            return false;
        }

        private async Task<MetadataObject?> RenameLegacyDocumentAsync(
            InventoryDocumentModel.DocumentDefinition definition,
            InventorySchemaReport report)
        {
            foreach (var legacyName in definition.LegacyNames)
            {
                var legacy = await _context.MetadataObjects
                    .Include(item => item.Fields)
                    .FirstOrDefaultAsync(item =>
                        item.ObjectType == "Document" && item.Name == legacyName);

                if (legacy == null)
                    continue;

                var oldTable = legacy.TableName;
                var oldName = legacy.Name;

                legacy.Name = definition.Name;
                legacy.TableName = definition.Table;
                legacy.Description = definition.Description;
                legacy.Icon = definition.Icon;
                legacy.Order = definition.Order;
                legacy.IsSystem = true;
                legacy.UsePostings = true;

                await _context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(oldTable) &&
                    !string.Equals(oldTable, definition.Table, StringComparison.OrdinalIgnoreCase))
                {
                    await RenameTableAsync(oldTable, definition.Table);
                    await RenamePrimaryKeyAsync(definition.Table);
                }

                report.Actions.Add($"Документ «{oldName}» → «{definition.Name}» ({oldTable} → {definition.Table})");
                return legacy;
            }

            return null;
        }

        private async Task EnsureHeaderColumnsAsync(MetadataObject document, InventorySchemaReport report)
        {
            // Старые шапки ТМЦ жили на number/date и invoice_number/vat_amount —
            // канон финансов и окна счёта-фактуры: doc_number/doc_date, esf_number/vat_total.
            await RenameColumnIfExistsAsync(document.TableName, "number", "doc_number");
            await RenameColumnIfExistsAsync(document.TableName, "date", "doc_date");
            await RenameColumnIfExistsAsync(document.TableName, "invoice_number", "esf_number");
            await RenameColumnIfExistsAsync(document.TableName, "vat_amount", "vat_total");

            foreach (var field in document.Fields)
            {
                if (string.Equals(field.DbColumnName, "number", StringComparison.OrdinalIgnoreCase))
                    field.DbColumnName = "doc_number";
                else if (string.Equals(field.DbColumnName, "date", StringComparison.OrdinalIgnoreCase))
                    field.DbColumnName = "doc_date";
                else if (string.Equals(field.DbColumnName, "invoice_number", StringComparison.OrdinalIgnoreCase))
                    field.DbColumnName = "esf_number";
                else if (string.Equals(field.DbColumnName, "vat_amount", StringComparison.OrdinalIgnoreCase))
                    field.DbColumnName = "vat_total";
            }

            // Участок и МОЛ в окне эталона — колонки таблицы строк, не шапки.
            // Одиночный материал (material_id/quantity/price) тоже переехал в строки.
            await RemoveHeaderFieldsAsync(
                document,
                new[] { "site_id", "responsible_person_id", "material_id", "quantity", "price", "description" },
                report);

            await EnsureColumnsAsync(document, InventoryDocumentModel.HeaderFields(document.Id), report);
            await DropLegacySingleLineColumnsAsync(document, report);

            // Старые ссылочные колонки живут в text — модель ждёт uuid.
            foreach (var column in new[] { "organization_id", "debit_account", "credit_account" })
                await EnsureReferenceTypeAsync(document.TableName, column, report);

            // Счета шапки теперь служебные: заполняются проведением, а не в окне.
            await MakeColumnNullableAsync(document.TableName, "debit_account");
            await MakeColumnNullableAsync(document.TableName, "credit_account");
        }

        /// <summary>Приводит текстовую ссылочную колонку к uuid (только на пустой таблице).</summary>
        private async Task EnsureReferenceTypeAsync(
            string tableName,
            string columnName,
            InventorySchemaReport report)
        {
            if (!await ColumnExistsAsync(tableName, columnName))
                return;

            var dataType = await ScalarStringAsync(
                "SELECT COALESCE((SELECT data_type FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = @table_name AND column_name = @column_name), '') AS \"Value\"",
                new NpgsqlParameter("table_name", tableName),
                new NpgsqlParameter("column_name", columnName));

            if (string.Equals(dataType, "uuid", StringComparison.OrdinalIgnoreCase))
                return;

            if (await HasRowsAsync(tableName))
            {
                report.Warnings.Add(
                    $"«{tableName}»: колонка {columnName} осталась {dataType} — в таблице есть данные");
                return;
            }

            // DEFAULT ''::text не приводится к uuid — снимаем его до смены типа.
            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} ALTER COLUMN {QuoteIdentifier(columnName)} DROP DEFAULT;");

            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} ALTER COLUMN {QuoteIdentifier(columnName)} " +
                $"TYPE uuid USING NULLIF(btrim({QuoteIdentifier(columnName)}::text), '')::uuid;");

            report.Actions.Add($"«{tableName}»: {columnName} → uuid");
        }

        private async Task MakeColumnNullableAsync(string tableName, string columnName)
        {
            if (!await TableExistsAsync(tableName) || !await ColumnExistsAsync(tableName, columnName))
                return;

            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} ALTER COLUMN " +
                $"{QuoteIdentifier(columnName)} DROP NOT NULL;");
        }

        /// <summary>Убирает поля шапки из метаданных (колонки в таблице остаются).</summary>
        private async Task RemoveHeaderFieldsAsync(
            MetadataObject document,
            IReadOnlyCollection<string> columns,
            InventorySchemaReport report)
        {
            var fields = document.Fields
                .Where(field => columns.Contains(field.DbColumnName, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (fields.Count == 0)
                return;

            _context.MetadataFields.RemoveRange(fields);
            foreach (var field in fields)
                document.Fields.Remove(field);

            await _context.SaveChangesAsync();
            report.Actions.Add(
                $"«{document.Name}»: убраны из шапки {string.Join(", ", fields.Select(field => field.DbColumnName))} — теперь в строках");
        }

        /// <summary>
        /// Старые документы хранили один материал в шапке (material_id/quantity/price).
        /// Теперь материал живёт в строках; колонки убираем, только если шапка пустая.
        /// </summary>
        private async Task DropLegacySingleLineColumnsAsync(MetadataObject document, InventorySchemaReport report)
        {
            var legacyColumns = new[] { "material_id", "quantity", "price", "site_id", "responsible_person_id", "description" };
            var present = new List<string>();

            foreach (var column in legacyColumns)
            {
                if (await ColumnExistsAsync(document.TableName, column))
                    present.Add(column);
            }

            if (present.Count == 0)
                return;

            if (await HasRowsAsync(document.TableName))
            {
                report.Warnings.Add(
                    $"«{document.Name}»: в шапке остались колонки {string.Join(", ", present)} — есть данные, не удаляю");
                return;
            }

            foreach (var column in present)
            {
                await _context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE public.{QuoteIdentifier(document.TableName)} DROP COLUMN IF EXISTS {QuoteIdentifier(column)};");
            }

            report.Actions.Add($"«{document.Name}»: убраны колонки одиночного материала ({string.Join(", ", present)})");
        }

        // ---------- Удаление документов-признаков ----------

        private async Task RemoveObsoleteDocumentsAsync(InventorySchemaReport report)
        {
            foreach (var name in InventoryDocumentModel.ObsoleteDocumentNames)
            {
                var document = await _context.MetadataObjects
                    .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == name);

                if (document == null)
                    continue;

                var table = document.TableName;

                var moduleItems = await _context.MetadataModuleItems
                    .Where(item => item.ObjectType == "Document" && item.ObjectId == document.Id)
                    .ToListAsync();
                var fields = await _context.MetadataFields
                    .Where(field => field.MetadataObjectId == document.Id)
                    .ToListAsync();
                var rules = await _context.MetadataPostingRules
                    .Where(rule => rule.MetadataObjectId == document.Id)
                    .ToListAsync();

                _context.MetadataModuleItems.RemoveRange(moduleItems);
                _context.MetadataFields.RemoveRange(fields);
                _context.MetadataPostingRules.RemoveRange(rules);

                _context.MetadataObjects.Remove(document);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(table) && await TableExistsAsync(table))
                {
                    if (await HasRowsAsync(table))
                    {
                        report.Warnings.Add($"«{name}»: таблица {table} оставлена — в ней есть данные");
                    }
                    else
                    {
                        await _context.Database.ExecuteSqlRawAsync(
                            $"DROP TABLE IF EXISTS public.{QuoteIdentifier(table)};");
                        report.Actions.Add($"Удалён документ «{name}» и пустая таблица {table}");
                    }
                }
                else
                {
                    report.Actions.Add($"Удалён документ «{name}»");
                }
            }
        }

        // ---------- Строки документов ----------

        private async Task EnsureLinesTablesAsync(InventorySchemaReport report)
        {
            foreach (var definition in InventoryDocumentModel.Documents)
            {
                if (!await TableExistsAsync(definition.Table))
                {
                    report.Warnings.Add(
                        $"«{definition.Name}»: шапки {definition.Table} нет — строки не создаю");
                    continue;
                }

                var lines = QuoteIdentifier(definition.LinesTable);
                var header = QuoteIdentifier(definition.Table);
                var parent = QuoteIdentifier(definition.ParentColumn);
                var fkName = QuoteIdentifier($"fk_{definition.LinesTable}_header");
                var indexName = QuoteIdentifier($"IX_{definition.LinesTable}_parent");
                var indexLineName = QuoteIdentifier($"IX_{definition.LinesTable}_parent_line");

                var columns = new StringBuilder();
                columns.AppendLine("\"Id\" uuid PRIMARY KEY,");
                columns.AppendLine($"{parent} uuid NOT NULL,");
                foreach (var (column, columnDefinition) in InventoryDocumentModel.LineColumns)
                    columns.AppendLine($"{QuoteIdentifier(column)} {columnDefinition},");
                columns.AppendLine("\"CreatedAt\" timestamp NOT NULL DEFAULT NOW(),");
                columns.AppendLine("\"UpdatedAt\" timestamp NOT NULL DEFAULT NOW()");

                var sql = $@"
                    CREATE TABLE IF NOT EXISTS public.{lines} (
                        {columns}
                    );
                    CREATE INDEX IF NOT EXISTS {indexName}
                        ON public.{lines} ({parent});
                    CREATE INDEX IF NOT EXISTS {indexLineName}
                        ON public.{lines} ({parent}, ""line_number"");
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1 FROM pg_constraint
                            WHERE conname = 'fk_{definition.LinesTable}_header'
                              AND conrelid = 'public.{definition.LinesTable}'::regclass) THEN
                            ALTER TABLE public.{lines}
                                ADD CONSTRAINT {fkName} FOREIGN KEY ({parent})
                                REFERENCES public.{header}(""Id"") ON DELETE CASCADE;
                        END IF;
                    END $$;";

                await _context.Database.ExecuteSqlRawAsync(sql);

                // Колонки, добавленные позже (участок, МОЛ, налог с продаж и т.п.),
                // догоняем на уже существующих таблицах строк.
                // Все недостающие колонки — одним ALTER: на удалённой базе
                // это один round-trip вместо десятков.
                var addedColumns = InventoryDocumentModel.LineColumns
                    .Select(pair => $"ADD COLUMN IF NOT EXISTS {QuoteIdentifier(pair.Column)} {pair.Definition}")
                    .ToList();

                if (addedColumns.Count > 0)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE public.{lines} {string.Join(", ", addedColumns)};");
                }
            }
        }

        // ---------- Модуль ----------

        private async Task EnsureModuleAssignmentsAsync(
            IReadOnlyCollection<string> documentNames,
            InventorySchemaReport report)
        {
            if (documentNames.Count == 0)
                return;

            var module = await _context.MetadataModules
                .FirstOrDefaultAsync(item => item.Code == ModuleMetadataService.InventoryCode);

            if (module == null)
                return;

            foreach (var definition in InventoryDocumentModel.Documents.Where(item => documentNames.Contains(item.Name)))
            {
                var document = await _context.MetadataObjects
                    .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == definition.Name);

                if (document == null)
                    continue;

                var assigned = await _context.MetadataModuleItems.AnyAsync(item =>
                    item.ModuleId == module.Id &&
                    item.ObjectType == "Document" &&
                    item.ObjectId == document.Id);

                if (assigned)
                    continue;

                await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
                {
                    ModuleId = module.Id,
                    ObjectId = document.Id,
                    ObjectType = "Document",
                    Order = definition.Order
                });

                report.Actions.Add($"«{definition.Name}» привязан к модулю «{module.Name}»");
            }
        }

        // ---------- Общие помощники ----------

        private async Task EnsureColumnsAsync(
            MetadataObject metadataObject,
            IReadOnlyList<MetadataField> desiredFields,
            InventorySchemaReport report)
        {
            if (!await TableExistsAsync(metadataObject.TableName))
                return;

            foreach (var field in desiredFields)
            {
                var existing = metadataObject.Fields.FirstOrDefault(item =>
                    string.Equals(item.DbColumnName, field.DbColumnName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    await AlignFieldAsync(metadataObject, existing, field, report);
                    continue;
                }

                // Сначала явное Add: сущность, добавленная только в навигацию, при
                // определённых состояниях трекера уходит в UPDATE вместо INSERT (0 строк).
                _context.MetadataFields.Add(field);
                metadataObject.Fields.Add(field);
                await AddColumnAsync(metadataObject.TableName, field);
                report.Actions.Add($"«{metadataObject.Name}»: добавлено поле «{field.Name}» ({field.DbColumnName})");
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Приводит существующее поле к определению модели: имя, порядок, ссылка,
        /// обязательность, тип. Тип колонки меняется только на пустой таблице.
        /// </summary>
        private async Task AlignFieldAsync(
            MetadataObject metadataObject,
            MetadataField existing,
            MetadataField desired,
            InventorySchemaReport report)
        {
            var changed = false;

            if (!string.Equals(existing.Name, desired.Name, StringComparison.Ordinal))
            {
                existing.Name = desired.Name;
                changed = true;
            }

            if (existing.Order != desired.Order)
            {
                existing.Order = desired.Order;
                changed = true;
            }

            if (existing.IsRequired != desired.IsRequired)
            {
                existing.IsRequired = desired.IsRequired;
                changed = true;
            }

            if (!string.Equals(existing.ReferenceCatalog, desired.ReferenceCatalog, StringComparison.Ordinal))
            {
                existing.ReferenceCatalog = desired.ReferenceCatalog;
                changed = true;
            }

            if (!string.Equals(existing.DisplayPattern, desired.DisplayPattern, StringComparison.Ordinal))
            {
                existing.DisplayPattern = desired.DisplayPattern;
                changed = true;
            }

            if (!string.Equals(existing.DisplayFields, desired.DisplayFields, StringComparison.Ordinal))
            {
                existing.DisplayFields = desired.DisplayFields;
                changed = true;
            }

            var typeChanged = !string.Equals(existing.FieldType, desired.FieldType, StringComparison.OrdinalIgnoreCase) ||
                              existing.Length != desired.Length ||
                              existing.Precision != desired.Precision ||
                              existing.Scale != desired.Scale;

            if (typeChanged && !await HasRowsAsync(metadataObject.TableName))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE public.{QuoteIdentifier(metadataObject.TableName)} ALTER COLUMN " +
                    $"{QuoteIdentifier(existing.DbColumnName)} TYPE {GetSqlType(desired)};");

                existing.FieldType = desired.FieldType;
                existing.Length = desired.Length;
                existing.Precision = desired.Precision;
                existing.Scale = desired.Scale;
                changed = true;
            }

            if (changed)
                report.Actions.Add($"«{metadataObject.Name}»: поле «{existing.Name}» выровнено по модели");
        }

        private async Task AddColumnAsync(string tableName, MetadataField field)
        {
            var sqlType = GetSqlType(field);
            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} ADD COLUMN IF NOT EXISTS " +
                $"{QuoteIdentifier(field.DbColumnName)} {sqlType};");
        }

        private async Task RenameColumnIfExistsAsync(string tableName, string oldName, string newName)
        {
            if (!await TableExistsAsync(tableName))
                return;

            if (!await ColumnExistsAsync(tableName, oldName))
                return;

            if (await ColumnExistsAsync(tableName, newName))
                return;

            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} RENAME COLUMN " +
                $"{QuoteIdentifier(oldName)} TO {QuoteIdentifier(newName)};");
        }

        private async Task RenameTableAsync(string oldTable, string newTable)
        {
            if (!await TableExistsAsync(oldTable) || await TableExistsAsync(newTable))
                return;

            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(oldTable)} RENAME TO {QuoteIdentifier(newTable)};");
        }

        private async Task RenamePrimaryKeyAsync(string tableName)
        {
            if (!await TableExistsAsync(tableName))
                return;

            var pkName = await ScalarStringAsync(
                "SELECT COALESCE((SELECT conname FROM pg_constraint " +
                "WHERE conrelid = to_regclass(format('public.%I', @table_name::text)) AND contype = 'p' LIMIT 1), '') AS \"Value\"",
                new NpgsqlParameter("table_name", tableName));

            if (string.IsNullOrWhiteSpace(pkName))
                return;

            var wanted = tableName + "_pkey";
            if (string.Equals(pkName, wanted, StringComparison.OrdinalIgnoreCase))
                return;

            if (await RelationExistsAsync(wanted))
                return;

            await _context.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE public.{QuoteIdentifier(tableName)} RENAME CONSTRAINT " +
                $"{QuoteIdentifier(pkName)} TO {QuoteIdentifier(wanted)};");
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            var value = await ScalarStringAsync(
                "SELECT COALESCE(to_regclass(format('public.%I', @table_name::text))::text, '') AS \"Value\"",
                new NpgsqlParameter("table_name", tableName));

            return !string.IsNullOrWhiteSpace(value);
        }

        private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            var count = await ScalarIntAsync(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = @table_name AND column_name = @column_name",
                new NpgsqlParameter("table_name", tableName),
                new NpgsqlParameter("column_name", columnName));

            return count > 0;
        }

        private async Task<bool> HasRowsAsync(string tableName)
        {
            var count = await ScalarIntAsync(
                $"SELECT CASE WHEN EXISTS (SELECT 1 FROM public.{QuoteIdentifier(tableName)}) THEN 1 ELSE 0 END AS \"Value\"");
            return count > 0;
        }

        private async Task<bool> RelationExistsAsync(string relationName)
        {
            var count = await ScalarIntAsync(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_class WHERE relname = @relation_name",
                new NpgsqlParameter("relation_name", relationName));

            return count > 0;
        }

        private async Task<bool> ScalarExistsAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var count = await ScalarIntAsync(sql, parameters);
            return count > 0;
        }

        private async Task<int> ScalarIntAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var rows = await _context.Database
                .SqlQueryRaw<int>(sql, parameters.Cast<object>().ToArray())
                .ToListAsync();

            return rows.Count > 0 ? rows[0] : 0;
        }

        private async Task<string> ScalarStringAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var rows = await _context.Database
                .SqlQueryRaw<string>(sql, parameters.Cast<object>().ToArray())
                .ToListAsync();

            return rows.Count > 0 ? rows[0] ?? string.Empty : string.Empty;
        }

        private static string QuoteIdentifier(string identifier) =>
            "\"" + (identifier ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static string GetSqlType(MetadataField field) => field.FieldType switch
        {
            "String" => $"VARCHAR({(field.Length > 0 ? field.Length : 255)})",
            "Int" => "INTEGER",
            "Decimal" => $"DECIMAL({(field.Precision > 0 ? field.Precision : 18)}, {(field.Scale > 0 ? field.Scale : 2)})",
            "DateTime" => "TIMESTAMP",
            "Bool" => "BOOLEAN",
            "Reference" => "uuid",
            _ => "TEXT"
        };
    }
}
