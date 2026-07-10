using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BIS.ERP.Services
{
    public class InvoiceMetadataSeedService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;
        private readonly InvoiceService _invoiceService;

        public InvoiceMetadataSeedService(AppDbContext context)
        {
            _context = context;
            _metadataService = new MetadataService(context);
            _invoiceService = new InvoiceService(context);
        }

        public async Task EnsureAsync()
        {
            await _invoiceService.EnsureSchemaAsync();

            var configId = await _context.MetadataConfigurations.Select(item => (Guid?)item.Id).FirstOrDefaultAsync();
            await EnsureDocumentAsync(
                InvoiceDocumentTypes.SalesIssue,
                "doc_sales_invoice",
                "Выписка счет-фактур на реализацию (журнал поставок)",
                "🧾",
                15,
                configId);
            await EnsureDocumentAsync(
                InvoiceDocumentTypes.PurchaseRegistration,
                "doc_purchase_invoice",
                "Регистрация полученных счет-фактур (журнал закупок)",
                "📥",
                16,
                configId);
            await EnsureInvoiceReferenceDataAsync();
            await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
        }

        private async Task EnsureDocumentAsync(
            string name,
            string tableName,
            string description,
            string icon,
            int order,
            Guid? configId)
        {
            var existing = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == name);

            if (existing == null)
            {
                var document = new MetadataObject
                {
                    Name = name,
                    TableName = tableName,
                    ObjectType = "Document",
                    Description = description,
                    Icon = icon,
                    Order = order,
                    IsSystem = true,
                    UsePostings = true,
                    MetadataConfigId = configId,
                    Fields = GetInvoiceHeaderFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(document);
                await _context.SaveChangesAsync();
                await _metadataService.CreateDynamicTableAsync(document);
                return;
            }

            existing.UsePostings = true;
            existing.Description = description;
            SynchronizeInvoiceHeaderFields(existing);
            await _metadataService.CreateDynamicTableAsync(existing);
            await _context.SaveChangesAsync();
        }

        private static void SynchronizeInvoiceHeaderFields(MetadataObject document)
        {
            var desiredFields = GetInvoiceHeaderFields(document.Id);
            foreach (var desired in desiredFields)
            {
                var existing = document.Fields.FirstOrDefault(field =>
                    field.DbColumnName.Equals(desired.DbColumnName, StringComparison.OrdinalIgnoreCase) ||
                    field.Name.Equals(desired.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    document.Fields.Add(desired);
                    continue;
                }

                existing.Name = desired.Name;
                existing.FieldType = desired.FieldType;
                existing.ReferenceCatalog = desired.ReferenceCatalog;
                existing.DisplayFields = desired.DisplayFields;
                existing.DisplayPattern = desired.DisplayPattern;
                existing.Order = desired.Order;
                existing.IsRequired = desired.IsRequired;
                existing.Length = desired.Length;
                existing.Precision = desired.Precision;
                existing.Scale = desired.Scale;
            }
        }

        private async Task EnsureInvoiceReferenceDataAsync()
        {
            var catalogs = await _context.MetadataObjects.AsNoTracking()
                .Where(item => item.ObjectType == "Catalog" &&
                    (item.Name == "Налоги" || item.Name == "Виды оплаты" ||
                     item.Name == "Виды поставки" || item.Name == "Типы поставки"))
                .ToDictionaryAsync(item => item.Name);

            if (catalogs.TryGetValue("Налоги", out var taxes))
            {
                await EnsureCatalogRowsAsync(
                    taxes.TableName,
                    new[]
                    {
                        new CatalogSeedRow("НДС12", "НДС 12%", Rate: 12m, EsfVatCode: "10", SortOrder: 1, IsDefaultVat: true),
                        new CatalogSeedRow("НДС0", "НДС 0%", Rate: 0m, EsfVatCode: "10", SortOrder: 2),
                        new CatalogSeedRow("WITHOUT_TAX", "Без НДС / освобождено", Rate: 0m, EsfVatCode: "90", EsfSalesTaxCode: "50", SortOrder: 3, IsDefaultSalesTax: true),
                        new CatalogSeedRow("SALES_TAX", "Налог с продаж (базовый режим)", Rate: 1.5m, EsfSalesTaxCode: "50", SortOrder: 4),
                        new CatalogSeedRow("SALES_SERVICE", "Налог с продаж: услуги (неторг. деятельность)", Rate: 2.5m, EsfSalesTaxCode: "70", SortOrder: 5),
                        new CatalogSeedRow("SALES_TRADE", "Налог с продаж: торговая деятельность", Rate: 1.5m, EsfSalesTaxCode: "50", SortOrder: 6),
                        new CatalogSeedRow("SALES_EXEMPT", "Налог с продаж: необлагаемая деятельность", Rate: 0m, EsfSalesTaxCode: "50", SortOrder: 7),
                        new CatalogSeedRow("SALES_RETAIL_2009", "Налог с продаж: розничная продажа до 2009", Rate: 4m, EsfSalesTaxCode: "50", SortOrder: 8)
                    },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default_vat"] = "НДС12",
                        ["is_default_sales_tax"] = "WITHOUT_TAX"
                    });
            }

            if (catalogs.TryGetValue("Виды оплаты", out var paymentKinds))
            {
                await EnsureCatalogRowsAsync(
                    paymentKinds.TableName,
                    new[]
                    {
                        new CatalogSeedRow("TRANSFER", "Безналичный перевод", Rate: 0m, EsfCode: "20", IsDefault: true),
                        new CatalogSeedRow("CASH", "Наличные", Rate: 0m, EsfCode: "10"),
                        new CatalogSeedRow("CARD", "Банковская карта", Rate: 0m, EsfCode: "11"),
                        new CatalogSeedRow("CHEQUE", "Чек", Rate: 0m, EsfCode: "30")
                    },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "TRANSFER"
                    });
            }

            if (catalogs.TryGetValue("Виды поставки", out var supplyKinds))
            {
                await EnsureCatalogRowsAsync(
                    supplyKinds.TableName,
                    new[]
                    {
                        new CatalogSeedRow("GOODS", "Поставка товаров", "Используется для стандартной товарной поставки ЭСФ.", EsfCode: "100", SortOrder: 1, IsDefault: true),
                        new CatalogSeedRow("SERVICE", "Работы / услуги", "Наблюдалось в FoxPro-выгрузках как код 101.", EsfCode: "101", SortOrder: 2),
                        new CatalogSeedRow("OTHER", "Прочая поставка", "Наблюдалось в FoxPro-выгрузках как код 299.", EsfCode: "299", SortOrder: 3)
                    },
                    deactivateCodes: new[]
                    {
                        "OPT", "ROZN", "IMP", "EXPORT",
                        "REMNANTS_2009", "ZERO_SUPPLY", "EXEMPT_SUPPLY", "TAXABLE_SUPPLY", "NON_TAXABLE_SUPPLY"
                    },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "GOODS"
                    });
            }

            if (catalogs.TryGetValue("Типы поставки", out var deliveryTypes))
            {
                await EnsureCatalogRowsAsync(
                    deliveryTypes.TableName,
                    new[]
                    {
                        new CatalogSeedRow("TAXABLE", "Облагаемая поставка", "FoxPro/XML: vatDeliveryTypeCode=100.", EsfCode: "100", SortOrder: 1, IsDefault: true),
                        new CatalogSeedRow("EXEMPT", "Необлагаемая / без НДС", "FoxPro/XML: vatDeliveryTypeCode=101.", EsfCode: "101", SortOrder: 2),
                        new CatalogSeedRow("IMPORT", "Импорт", "Резерв под vatDeliveryTypeCode=200.", EsfCode: "200", SortOrder: 3),
                        new CatalogSeedRow("EXPORT", "Экспорт", "Резерв под vatDeliveryTypeCode=300.", EsfCode: "300", SortOrder: 4)
                    },
                    deactivateCodes: new[] { "STANDARD", "EXPRESS", "SAMOVIVOZ" },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "TAXABLE"
                    });
            }
        }

        private async Task EnsureCatalogRowsAsync(
            string tableName,
            IReadOnlyCollection<CatalogSeedRow> rows,
            IReadOnlyCollection<string>? deactivateCodes = null,
            IReadOnlyDictionary<string, string>? defaultCodesByColumn = null)
        {
            var existingColumns = await GetTableColumnsAsync(tableName);
            foreach (var row in rows)
            {
                var values = new Dictionary<string, object?>
                {
                    ["code"] = row.Code,
                    ["name"] = row.Name,
                    ["description"] = row.Description,
                    ["rate"] = row.Rate,
                    ["esf_code"] = row.EsfCode,
                    ["esf_vat_code"] = row.EsfVatCode,
                    ["esf_sales_tax_code"] = row.EsfSalesTaxCode,
                    ["sort_order"] = row.SortOrder,
                    ["is_active"] = row.IsActive,
                    ["is_default"] = row.IsDefault,
                    ["is_default_vat"] = row.IsDefaultVat,
                    ["is_default_sales_tax"] = row.IsDefaultSalesTax,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                if (existingColumns.Contains("CreatedAt"))
                    values["CreatedAt"] = DateTime.UtcNow;

                await UpsertCatalogRowAsync(tableName, existingColumns, values);
            }

            if (deactivateCodes is { Count: > 0 } && existingColumns.Contains("is_active"))
            {
                var inList = string.Join(", ", deactivateCodes.Select(ToSqlLiteral));
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{tableName}""
                    SET ""is_active"" = false, ""UpdatedAt"" = NOW()
                    WHERE ""code"" IN ({inList});");
            }

            if (defaultCodesByColumn is { Count: > 0 })
                await EnsureInitialDefaultValuesAsync(tableName, existingColumns, defaultCodesByColumn);
        }

        private async Task EnsureInitialDefaultValuesAsync(
            string tableName,
            HashSet<string> existingColumns,
            IReadOnlyDictionary<string, string> defaultCodesByColumn)
        {
            foreach (var pair in defaultCodesByColumn)
            {
                if (!existingColumns.Contains(pair.Key))
                    continue;

                var currentCount = await CountRowsByConditionAsync(
                    tableName,
                    $@"COALESCE(""{pair.Key}"", false) = true");

                if (currentCount > 0)
                    continue;

                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{tableName}""
                    SET ""{pair.Key}"" = true, ""UpdatedAt"" = NOW()
                    WHERE ""code"" = {ToSqlLiteral(pair.Value)};");
            }
        }

        private async Task<int> CountRowsByConditionAsync(string tableName, string conditionSql)
        {
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $@"SELECT COUNT(*) FROM ""{tableName}"" WHERE {conditionSql};";

            await _context.Database.OpenConnectionAsync();
            try
            {
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        private async Task<HashSet<string>> GetTableColumnsAsync(string tableName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT column_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @tableName";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            await _context.Database.OpenConnectionAsync();
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(reader.GetString(0));
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }

        private async Task UpsertCatalogRowAsync(
            string tableName,
            HashSet<string> existingColumns,
            IReadOnlyDictionary<string, object?> values)
        {
            var filtered = values
                .Where(item => existingColumns.Contains(item.Key))
                .ToList();
            if (filtered.Count == 0)
                return;

            var codeEntry = filtered.FirstOrDefault(item =>
                item.Key.Equals("code", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(codeEntry.Key))
                return;

            var codeLiteral = ToSqlLiteral(codeEntry.Value);
            var columnSql = string.Join(", ", filtered.Select(item => $@"""{item.Key}"""));
            var valueSql = string.Join(", ", filtered.Select(item => ToSqlLiteral(item.Value)));
            var updateAssignments = filtered
                .Where(item => !item.Key.Equals("code", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default_vat", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default_sales_tax", StringComparison.OrdinalIgnoreCase))
                .Select(item => $@"""{item.Key}"" = {ToSqlLiteral(item.Value)}")
                .ToList();

            if (updateAssignments.Count > 0)
            {
                var updated = await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{tableName}""
                    SET {string.Join(", ", updateAssignments)}
                    WHERE ""code"" = {codeLiteral};");

                if (updated > 0)
                    return;
            }

            await _context.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""{tableName}"" ({columnSql})
                SELECT {valueSql}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM ""{tableName}""
                    WHERE ""code"" = {codeLiteral}
                );");
        }

        private static List<MetadataField> GetInvoiceHeaderFields(Guid metadataObjectId) => new()
        {
            Field(metadataObjectId, "Номер", "doc_number", "String", 1, true),
            Field(metadataObjectId, "Дата", "doc_date", "DateTime", 2, true),
            Field(metadataObjectId, "Номер ЭСФ", "esf_number", "String", 3),
            Field(metadataObjectId, "Организация", "organization_id", "Reference", 4, false, "Организации"),
            Field(metadataObjectId, "Счет", "counterparty_account", "Reference", 5, false, "План счетов"),
            Field(metadataObjectId, "Вид оплаты", "payment_kind", "Reference", 6, false, "Виды оплаты"),
            Field(metadataObjectId, "Вид поставки ЭСФ", "delivery_kind", "Reference", 7, false, "Виды поставки"),
            Field(metadataObjectId, "Тип поставки ЭСФ", "supply_kind", "Reference", 8, false, "Типы поставки"),
            Field(metadataObjectId, "Основание", "basis", "String", 9),
            Field(metadataObjectId, "Сумма без налогов", "amount_without_tax", "Decimal", 10),
            Field(metadataObjectId, "Сумма НДС", "vat_total", "Decimal", 11),
            Field(metadataObjectId, "Налог с продаж", "sales_tax_total", "Decimal", 12),
            Field(metadataObjectId, "Сумма", "amount", "Decimal", 13),
            Field(metadataObjectId, "Проведён", "is_posted", "Bool", 14, true)
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
            Name = name,
            DbColumnName = column,
            FieldType = type,
            Order = order,
            IsRequired = required,
            ReferenceCatalog = reference,
            MetadataObjectId = metadataObjectId,
            Length = type == "String" ? 500 : 0,
            Precision = 18,
            Scale = 2
        };

        private static string EscapeSql(string value) => (value ?? string.Empty).Replace("'", "''");

        private static string ToSqlLiteral(object? value)
        {
            return value switch
            {
                null => "NULL",
                DBNull _ => "NULL",
                bool boolean => boolean ? "true" : "false",
                decimal number => number.ToString(CultureInfo.InvariantCulture),
                double number => number.ToString(CultureInfo.InvariantCulture),
                float number => number.ToString(CultureInfo.InvariantCulture),
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                Guid guid => $"'{guid}'",
                DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss}'",
                _ => $"'{EscapeSql(value.ToString() ?? string.Empty)}'"
            };
        }

        private sealed record CatalogSeedRow(
            string Code,
            string Name,
            string? Description = null,
            decimal? Rate = null,
            string? EsfCode = null,
            string? EsfVatCode = null,
            string? EsfSalesTaxCode = null,
            int? SortOrder = null,
            bool IsDefault = false,
            bool IsDefaultVat = false,
            bool IsDefaultSalesTax = false,
            bool IsActive = true);
    }
}
