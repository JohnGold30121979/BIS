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
            await _metadataService.CreateDynamicTableAsync(existing);
            await _context.SaveChangesAsync();
        }

        private static List<MetadataField> GetInvoiceHeaderFields(Guid metadataObjectId) => new()
        {
            Field(metadataObjectId, "Номер", "doc_number", "String", 1, true),
            Field(metadataObjectId, "Дата", "doc_date", "DateTime", 2, true),
            Field(metadataObjectId, "Номер ЭСФ", "esf_number", "String", 3),
            Field(metadataObjectId, "Организация", "organization_id", "Reference", 4, false, "Организации"),
            Field(metadataObjectId, "Счет", "counterparty_account", "String", 5),
            Field(metadataObjectId, "Вид оплаты", "payment_kind", "String", 6),
            Field(metadataObjectId, "Вид поставки", "delivery_kind", "String", 7),
            Field(metadataObjectId, "Тип поставки", "supply_kind", "String", 8),
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
    }
}
