using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

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
                await InsertRowIfMissingAsync(taxes.TableName, "NDS12", "НДС 12%", rate: 12m, sortOrder: 1);
                await InsertRowIfMissingAsync(taxes.TableName, "NDS0", "НДС 0%", rate: 0m, sortOrder: 2);
                await InsertRowIfMissingAsync(taxes.TableName, "SALES_TAX", "Налог с продаж", rate: 2m, sortOrder: 3);
                await InsertRowIfMissingAsync(taxes.TableName, "WITHOUT_TAX", "Без налога", rate: 0m, sortOrder: 4);
            }

            if (catalogs.TryGetValue("Виды оплаты", out var paymentKinds))
            {
                await InsertRowIfMissingAsync(paymentKinds.TableName, "TRANSFER", "Безналичный перевод", rate: 0m);
                await InsertRowIfMissingAsync(paymentKinds.TableName, "CASH", "Наличные", rate: 0m);
                await InsertRowIfMissingAsync(paymentKinds.TableName, "CARD", "Банковская карта", rate: 0m);
            }

            if (catalogs.TryGetValue("Виды поставки", out var supplyKinds))
            {
                await InsertRowIfMissingAsync(supplyKinds.TableName, "TAXABLE", "Облагаемые");
                await InsertRowIfMissingAsync(supplyKinds.TableName, "EXEMPT", "Освобожденные");
                await InsertRowIfMissingAsync(supplyKinds.TableName, "EXPORT", "Экспорт");
            }

            if (catalogs.TryGetValue("Типы поставки", out var deliveryTypes))
            {
                await InsertRowIfMissingAsync(deliveryTypes.TableName, "TAXABLE", "Облагаемые");
                await InsertRowIfMissingAsync(deliveryTypes.TableName, "EXEMPT", "Освобожденные");
                await InsertRowIfMissingAsync(deliveryTypes.TableName, "EXPORT", "Экспорт");
            }
        }

        private async Task InsertRowIfMissingAsync(string tableName, string code, string name, decimal? rate = null, int? sortOrder = null)
        {
            var columns = new List<string> { @"""code""", @"""name""", @"""is_active""", @"""CreatedAt""" };
            var values = new List<string> { $"'{EscapeSql(code)}'", $"'{EscapeSql(name)}'", "true", "CURRENT_TIMESTAMP" };
            if (rate.HasValue)
            {
                columns.Insert(2, @"""rate""");
                values.Insert(2, rate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (sortOrder.HasValue)
            {
                columns.Insert(columns.Count - 1, @"""sort_order""");
                values.Insert(values.Count - 1, sortOrder.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            await _context.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""{tableName}"" ({string.Join(", ", columns)})
                SELECT {string.Join(", ", values)}
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""{tableName}"" WHERE ""code"" = '{EscapeSql(code)}'
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
            Field(metadataObjectId, "Вид поставки", "delivery_kind", "Reference", 7, false, "Виды поставки"),
            Field(metadataObjectId, "Тип поставки", "supply_kind", "Reference", 8, false, "Типы поставки"),
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
    }
}
