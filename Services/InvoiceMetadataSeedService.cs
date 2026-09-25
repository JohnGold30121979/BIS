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

        /// <summary>
        /// Дата начала действия исторических ставок каталога «Налоги».
        /// Указывается явно, чтобы новые ставки можно было вводить с нужной даты.
        /// </summary>
        private static readonly DateTime LegacyTaxValidFrom = new(2024, 1, 1);

        public async Task EnsureAsync()
        {
            await _invoiceService.EnsureSchemaAsync();

            var configId = await _context.MetadataConfigurations.Select(item => (Guid?)item.Id).FirstOrDefaultAsync();
            await EnsureEsfXmlTagCatalogAsync(configId);
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

        private async Task EnsureEsfXmlTagCatalogAsync(Guid? configId)
        {
            const string catalogName = "Настройки XML ЭСФ";
            const string tableName = "catalog_esf_xml_tags";

            var catalog = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.TableName == tableName);

            if (catalog == null)
            {
                catalog = new MetadataObject
                {
                    Name = catalogName,
                    TableName = tableName,
                    ObjectType = "Catalog",
                    Description = "Настройка соответствия внутренних ключей ЭСФ именам XML-тегов.",
                    Icon = "🏷",
                    Order = 40,
                    IsSystem = true,
                    MetadataConfigId = configId,
                    Fields = GetEsfXmlTagFields(Guid.NewGuid())
                };
                await _context.MetadataObjects.AddAsync(catalog);
                await _context.SaveChangesAsync();
                await _metadataService.CreateDynamicTableAsync(catalog);
            }
            else
            {
                catalog.Name = catalogName;
                catalog.Description = "Настройка соответствия внутренних ключей ЭСФ именам XML-тегов.";
                catalog.Icon = "🏷";
                SynchronizeCatalogFields(catalog, GetEsfXmlTagFields(catalog.Id));
                await _metadataService.CreateDynamicTableAsync(catalog);
                await _context.SaveChangesAsync();
            }

            await EnsureEsfXmlTagRowsAsync(tableName);
        }

        private static void SynchronizeCatalogFields(MetadataObject catalog, IReadOnlyCollection<MetadataField> desiredFields)
        {
            foreach (var desired in desiredFields)
            {
                var existing = catalog.Fields.FirstOrDefault(field =>
                    field.DbColumnName.Equals(desired.DbColumnName, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    desired.MetadataObjectId = catalog.Id;
                    catalog.Fields.Add(desired);
                    continue;
                }

                existing.Name = desired.Name;
                existing.FieldType = desired.FieldType;
                existing.Order = desired.Order;
                existing.IsRequired = desired.IsRequired;
                existing.Length = desired.Length;
                existing.Precision = desired.Precision;
                existing.Scale = desired.Scale;
            }
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
                        new CatalogSeedRow("НДС12", "НДС 12%", Rate: 12m, EsfVatCode: "10", SortOrder: 1, IsDefaultVat: true, TaxKind: "VAT", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            VatPayableAccount: "34300000", VatRecoverableAccount: "15400000"),
                        new CatalogSeedRow("НДС0", "НДС 0%", Rate: 0m, EsfVatCode: "10", SortOrder: 2, TaxKind: "VAT", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            VatPayableAccount: "34300000", VatRecoverableAccount: "15400000"),
                        new CatalogSeedRow("WITHOUT_TAX", "Без НДС / освобождено", Rate: 0m, EsfVatCode: "90", EsfSalesTaxCode: "50", SortOrder: 3, IsDefaultSalesTax: true, TaxKind: "BOTH", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            VatPayableAccount: "34300000", VatRecoverableAccount: "15400000", SalesTaxAccount: "34004000"),
                        new CatalogSeedRow("SALES_TAX", "Налог с продаж (базовый режим)", Rate: 1.5m, EsfSalesTaxCode: "50", SortOrder: 4, TaxKind: "SALES", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            SalesTaxAccount: "34004000"),
                        new CatalogSeedRow("SALES_SERVICE", "Налог с продаж: услуги (неторг. деятельность)", Rate: 2.5m, EsfSalesTaxCode: "70", SortOrder: 5, TaxKind: "SALES", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            SalesTaxAccount: "34004000"),
                        new CatalogSeedRow("SALES_TRADE", "Налог с продаж: торговая деятельность", Rate: 1.5m, EsfSalesTaxCode: "50", SortOrder: 6, TaxKind: "SALES", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            SalesTaxAccount: "34004000"),
                        new CatalogSeedRow("SALES_EXEMPT", "Налог с продаж: необлагаемая деятельность", Rate: 0m, EsfSalesTaxCode: "50", SortOrder: 7, TaxKind: "SALES", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            SalesTaxAccount: "34004000"),
                        new CatalogSeedRow("SALES_RETAIL_2009", "Налог с продаж: розничная продажа до 2009", Rate: 4m, EsfSalesTaxCode: "50", SortOrder: 8, TaxKind: "SALES", ValidFrom: LegacyTaxValidFrom, IsSystem: true,
                            SalesTaxAccount: "34004000")
                    },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default_vat"] = "НДС12",
                        ["is_default_sales_tax"] = "WITHOUT_TAX"
                    });

                // Однократное заполнение вида налога и периода действия для записей,
                // созданных до появления этих колонок. Заполненные значения не перезаписываются.
                await EnsureTaxCatalogClassificationAsync(taxes.TableName);
            }

            if (catalogs.TryGetValue("Виды оплаты", out var paymentKinds))
            {
                await NormalizePaymentKindCodesAsync(paymentKinds.TableName);
                await EnsureCatalogRowsAsync(
                    paymentKinds.TableName,
                    new[]
                    {
                        new CatalogSeedRow("3", "Безналичный перевод", Rate: 0m, EsfCode: "20", IsDefault: true),
                        new CatalogSeedRow("1", "Наличные", Rate: 0m, EsfCode: "10"),
                        new CatalogSeedRow("2", "Банковская карта", Rate: 0m, EsfCode: "11"),
                        new CatalogSeedRow("4", "Чек", Rate: 0m, EsfCode: "30")
                    },
                    deactivateCodes: new[] { "CASH", "CARD", "TRANSFER", "CHEQUE" },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "3"
                    });
            }
            if (catalogs.TryGetValue("Виды поставки", out var supplyKinds))
            {
                await NormalizeDeliveryKindCodesAsync(supplyKinds.TableName);
                await EnsureCatalogRowsAsync(
                    supplyKinds.TableName,
                    new[]
                    {
                        new CatalogSeedRow("1", "Поставка товаров", "Используется для стандартной товарной поставки ЭСФ.", EsfCode: "100", SortOrder: 1, IsDefault: true),
                        new CatalogSeedRow("2", "Работы / услуги", "Наблюдалось в FoxPro-выгрузках как код 101.", EsfCode: "101", SortOrder: 2),
                        new CatalogSeedRow("3", "Прочая поставка", "Наблюдалось в FoxPro-выгрузках как код 299.", EsfCode: "299", SortOrder: 3)
                    },
                    deactivateCodes: new[]
                    {
                        "GOODS", "SERVICE", "OTHER", "OPT", "ROZN", "IMP", "EXPORT",
                        "REMNANTS_2009", "ZERO_SUPPLY", "EXEMPT_SUPPLY", "TAXABLE_SUPPLY", "NON_TAXABLE_SUPPLY",
                        "STANDARD", "EXPRESS", "SAMOVIVOZ"
                    },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "1"
                    });
            }
            if (catalogs.TryGetValue("Типы поставки", out var deliveryTypes))
            {
                await NormalizeDeliveryTypeCodesAsync(deliveryTypes.TableName);
                await EnsureCatalogRowsAsync(
                    deliveryTypes.TableName,
                    new[]
                    {
                        new CatalogSeedRow("1", "Облагаемая поставка", "FoxPro/XML: vatDeliveryTypeCode=100.", EsfCode: "100", SortOrder: 1, IsDefault: true),
                        new CatalogSeedRow("2", "Необлагаемая / без НДС", "FoxPro/XML: vatDeliveryTypeCode=101.", EsfCode: "101", SortOrder: 2),
                        new CatalogSeedRow("3", "Импорт", "Резерв под vatDeliveryTypeCode=200.", EsfCode: "200", SortOrder: 3),
                        new CatalogSeedRow("4", "Экспорт", "Резерв под vatDeliveryTypeCode=300.", EsfCode: "300", SortOrder: 4)
                    },
                    deactivateCodes: new[] { "TAXABLE", "EXEMPT", "IMPORT", "EXPORT", "IMP", "WITHOUT_TAX", "NON_TAXABLE_SUPPLY", "EXEMPT_SUPPLY", "TAXABLE_SUPPLY", "ZERO_SUPPLY", "STANDARD", "EXPRESS", "GOODS", "SERVICE", "OTHER", "SAMOVIVOZ", "OPT", "ROZN", "REMNANTS_2009" },
                    defaultCodesByColumn: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["is_default"] = "1"
                    });
            }
        }

        /// <summary>
        /// Однократно проставляет вид налога (tax_kind), дату начала действия и признак
        /// служебной записи для записей каталога «Налоги», созданных ранее.
        /// Уже заполненные значения не перезаписываются.
        /// </summary>
        private async Task EnsureTaxCatalogClassificationAsync(string tableName)
        {
            var existingColumns = await GetTableColumnsAsync(tableName);
            if (!existingColumns.Contains("tax_kind"))
                return;

            var assignments = new List<string>
            {
                @"""tax_kind"" = COALESCE(NULLIF(TRIM(t.""tax_kind""), ''), s.""tax_kind"")"
            };

            if (existingColumns.Contains("valid_from"))
                assignments.Add(@"""valid_from"" = COALESCE(t.""valid_from"", s.""valid_from"")");

            if (existingColumns.Contains("is_system"))
                assignments.Add(@"""is_system"" = COALESCE(t.""is_system"", s.""is_system"")");

            if (existingColumns.Contains("UpdatedAt"))
                assignments.Add(@"""UpdatedAt"" = NOW()");

            var missingCondition = new List<string>
            {
                @"t.""tax_kind"" IS NULL",
                @"TRIM(t.""tax_kind"") = ''"
            };

            if (existingColumns.Contains("valid_from"))
                missingCondition.Add(@"t.""valid_from"" IS NULL");

            if (existingColumns.Contains("is_system"))
                missingCondition.Add(@"t.""is_system"" IS NULL");

            var classificationSql = $@"
                UPDATE ""{tableName}"" AS t
                SET {string.Join(", ", assignments)}
                FROM (
                    VALUES
                        ('НДС12', 'VAT', DATE '2024-01-01', true),
                        ('НДС0', 'VAT', DATE '2024-01-01', true),
                        ('WITHOUT_TAX', 'BOTH', DATE '2024-01-01', true),
                        ('SALES_TAX', 'SALES', DATE '2024-01-01', true),
                        ('SALES_SERVICE', 'SALES', DATE '2024-01-01', true),
                        ('SALES_TRADE', 'SALES', DATE '2024-01-01', true),
                        ('SALES_EXEMPT', 'SALES', DATE '2024-01-01', true),
                        ('SALES_RETAIL_2009', 'SALES', DATE '2008-01-01', true)
                ) AS s(""code"", ""tax_kind"", ""valid_from"", ""is_system"")
                WHERE t.""code"" = s.""code""
                  AND ({string.Join(" OR ", missingCondition)});";

            await _context.Database.ExecuteSqlRawAsync(classificationSql);
        }

        private async Task EnsureEsfXmlTagRowsAsync(string tableName)
        {
            var existingColumns = await GetTableColumnsAsync(tableName);
            foreach (var tag in GetDefaultEsfXmlTags())
            {
                var values = new Dictionary<string, object?>
                {
                    ["code"] = tag.Key,
                    ["name"] = tag.Title,
                    ["tag_name"] = tag.TagName,
                    ["description"] = tag.Description,
                    ["sort_order"] = tag.Order,
                    ["is_active"] = true,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                if (existingColumns.Contains("CreatedAt"))
                    values["CreatedAt"] = DateTime.UtcNow;

                await UpsertCatalogRowAsync(tableName, existingColumns, values);
            }
        }

        private async Task NormalizeDeliveryKindCodesAsync(string tableName)
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{tableName}"" AS source
                SET ""is_active"" = false,
                    ""UpdatedAt"" = NOW()
                WHERE source.""code"" IN ('GOODS', 'SERVICE', 'OTHER')
                AND EXISTS (
                    SELECT 1
                    FROM ""{tableName}"" AS target
                    WHERE target.""code"" = CASE source.""code""
                        WHEN 'GOODS' THEN '1'
                        WHEN 'SERVICE' THEN '2'
                        WHEN 'OTHER' THEN '3'
                        ELSE source.""code""
                    END
                );

                UPDATE ""{tableName}"" AS source
                SET ""code"" = CASE source.""code""
                        WHEN 'GOODS' THEN '1'
                        WHEN 'SERVICE' THEN '2'
                        WHEN 'OTHER' THEN '3'
                        ELSE source.""code""
                    END,
                    ""UpdatedAt"" = NOW()
                WHERE source.""code"" IN ('GOODS', 'SERVICE', 'OTHER')
                AND NOT EXISTS (
                    SELECT 1
                    FROM ""{tableName}"" AS target
                    WHERE target.""code"" = CASE source.""code""
                        WHEN 'GOODS' THEN '1'
                        WHEN 'SERVICE' THEN '2'
                        WHEN 'OTHER' THEN '3'
                        ELSE source.""code""
                    END
                );

                DO $$
                BEGIN
                    IF to_regclass('public.doc_sales_invoice') IS NOT NULL THEN
                        UPDATE doc_sales_invoice
                        SET delivery_kind = CASE delivery_kind
                                WHEN 'GOODS' THEN '1'
                                WHEN 'SERVICE' THEN '2'
                                WHEN 'OTHER' THEN '3'
                                WHEN 'SAMOVIVOZ' THEN '2'
                                ELSE '1'
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE delivery_kind IN ('GOODS', 'SERVICE', 'OTHER', 'OPT', 'ROZN', 'IMP', 'EXPORT', 'REMNANTS_2009', 'ZERO_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'NON_TAXABLE_SUPPLY', 'STANDARD', 'EXPRESS', 'SAMOVIVOZ', 'TAXABLE');
                    END IF;

                    IF to_regclass('public.doc_purchase_invoice') IS NOT NULL THEN
                        UPDATE doc_purchase_invoice
                        SET delivery_kind = CASE delivery_kind
                                WHEN 'GOODS' THEN '1'
                                WHEN 'SERVICE' THEN '2'
                                WHEN 'OTHER' THEN '3'
                                WHEN 'SAMOVIVOZ' THEN '2'
                                ELSE '1'
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE delivery_kind IN ('GOODS', 'SERVICE', 'OTHER', 'OPT', 'ROZN', 'IMP', 'EXPORT', 'REMNANTS_2009', 'ZERO_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'NON_TAXABLE_SUPPLY', 'STANDARD', 'EXPRESS', 'SAMOVIVOZ', 'TAXABLE');
                    END IF;
                END $$;

                DELETE FROM ""{tableName}""
                WHERE ""code"" IN ('GOODS', 'SERVICE', 'OTHER', 'OPT', 'ROZN', 'IMP', 'EXPORT', 'REMNANTS_2009', 'ZERO_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'NON_TAXABLE_SUPPLY', 'STANDARD', 'EXPRESS', 'SAMOVIVOZ');");
        }
        private async Task NormalizeDeliveryTypeCodesAsync(string tableName)
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                DO $$
                BEGIN
                    IF to_regclass('public.doc_sales_invoice') IS NOT NULL THEN
                        UPDATE doc_sales_invoice
                        SET supply_kind = CASE UPPER(supply_kind)
                                WHEN 'EXEMPT' THEN '2'
                                WHEN 'WITHOUT_TAX' THEN '2'
                                WHEN 'NON_TAXABLE_SUPPLY' THEN '2'
                                WHEN 'EXEMPT_SUPPLY' THEN '2'
                                WHEN 'ZERO_SUPPLY' THEN '2'
                                WHEN 'IMP' THEN '3'
                                WHEN 'IMPORT' THEN '3'
                                WHEN 'EXPORT' THEN '4'
                                ELSE '1'
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE supply_kind IS NOT NULL
                          AND UPPER(supply_kind) IN ('TAXABLE', 'EXEMPT', 'IMPORT', 'EXPORT', 'IMP', 'WITHOUT_TAX', 'NON_TAXABLE_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'ZERO_SUPPLY', 'STANDARD', 'EXPRESS', 'GOODS', 'SERVICE', 'OTHER', 'SAMOVIVOZ', 'OPT', 'ROZN', 'REMNANTS_2009');
                    END IF;

                    IF to_regclass('public.doc_purchase_invoice') IS NOT NULL THEN
                        UPDATE doc_purchase_invoice
                        SET supply_kind = CASE UPPER(supply_kind)
                                WHEN 'EXEMPT' THEN '2'
                                WHEN 'WITHOUT_TAX' THEN '2'
                                WHEN 'NON_TAXABLE_SUPPLY' THEN '2'
                                WHEN 'EXEMPT_SUPPLY' THEN '2'
                                WHEN 'ZERO_SUPPLY' THEN '2'
                                WHEN 'IMP' THEN '3'
                                WHEN 'IMPORT' THEN '3'
                                WHEN 'EXPORT' THEN '4'
                                ELSE '1'
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE supply_kind IS NOT NULL
                          AND UPPER(supply_kind) IN ('TAXABLE', 'EXEMPT', 'IMPORT', 'EXPORT', 'IMP', 'WITHOUT_TAX', 'NON_TAXABLE_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'ZERO_SUPPLY', 'STANDARD', 'EXPRESS', 'GOODS', 'SERVICE', 'OTHER', 'SAMOVIVOZ', 'OPT', 'ROZN', 'REMNANTS_2009');
                    END IF;
                END $$;

                UPDATE ""{tableName}""
                SET ""is_default"" = false, ""UpdatedAt"" = NOW()
                WHERE ""code"" IN ('TAXABLE', 'EXEMPT', 'IMPORT', 'EXPORT', 'IMP', 'WITHOUT_TAX', 'NON_TAXABLE_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'ZERO_SUPPLY', 'STANDARD', 'EXPRESS', 'GOODS', 'SERVICE', 'OTHER', 'SAMOVIVOZ', 'OPT', 'ROZN', 'REMNANTS_2009');

                DELETE FROM ""{tableName}""
                WHERE ""code"" IN ('TAXABLE', 'EXEMPT', 'IMPORT', 'EXPORT', 'IMP', 'WITHOUT_TAX', 'NON_TAXABLE_SUPPLY', 'EXEMPT_SUPPLY', 'TAXABLE_SUPPLY', 'ZERO_SUPPLY', 'STANDARD', 'EXPRESS', 'GOODS', 'SERVICE', 'OTHER', 'SAMOVIVOZ', 'OPT', 'ROZN', 'REMNANTS_2009');");
        }
        private async Task NormalizePaymentKindCodesAsync(string tableName)
        {
            await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{tableName}"" AS source
                SET ""is_active"" = false,
                    ""UpdatedAt"" = NOW()
                WHERE source.""code"" IN ('CASH', 'CARD', 'TRANSFER', 'CHEQUE')
                AND EXISTS (
                    SELECT 1
                    FROM ""{tableName}"" AS target
                    WHERE target.""code"" = CASE source.""code""
                        WHEN 'CASH' THEN '1'
                        WHEN 'CARD' THEN '2'
                        WHEN 'TRANSFER' THEN '3'
                        WHEN 'CHEQUE' THEN '4'
                        ELSE source.""code""
                    END
                );

                UPDATE ""{tableName}"" AS source
                SET ""code"" = CASE source.""code""
                        WHEN 'CASH' THEN '1'
                        WHEN 'CARD' THEN '2'
                        WHEN 'TRANSFER' THEN '3'
                        WHEN 'CHEQUE' THEN '4'
                        ELSE source.""code""
                    END,
                    ""UpdatedAt"" = NOW()
                WHERE source.""code"" IN ('CASH', 'CARD', 'TRANSFER', 'CHEQUE')
                AND NOT EXISTS (
                    SELECT 1
                    FROM ""{tableName}"" AS target
                    WHERE target.""code"" = CASE source.""code""
                        WHEN 'CASH' THEN '1'
                        WHEN 'CARD' THEN '2'
                        WHEN 'TRANSFER' THEN '3'
                        WHEN 'CHEQUE' THEN '4'
                        ELSE source.""code""
                    END
                );

                DO $$
                BEGIN
                    IF to_regclass('public.doc_sales_invoice') IS NOT NULL THEN
                        UPDATE doc_sales_invoice
                        SET payment_kind = CASE payment_kind
                                WHEN 'CASH' THEN '1'
                                WHEN 'CARD' THEN '2'
                                WHEN 'TRANSFER' THEN '3'
                                WHEN 'CHEQUE' THEN '4'
                                ELSE payment_kind
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE payment_kind IN ('CASH', 'CARD', 'TRANSFER', 'CHEQUE');
                    END IF;

                    IF to_regclass('public.doc_purchase_invoice') IS NOT NULL THEN
                        UPDATE doc_purchase_invoice
                        SET payment_kind = CASE payment_kind
                                WHEN 'CASH' THEN '1'
                                WHEN 'CARD' THEN '2'
                                WHEN 'TRANSFER' THEN '3'
                                WHEN 'CHEQUE' THEN '4'
                                ELSE payment_kind
                            END,
                            ""UpdatedAt"" = NOW()
                        WHERE payment_kind IN ('CASH', 'CARD', 'TRANSFER', 'CHEQUE');
                    END IF;
                END $$;");
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
                    ["vat_payable_account"] = row.VatPayableAccount,
                    ["vat_recoverable_account"] = row.VatRecoverableAccount,
                    ["sales_tax_account"] = row.SalesTaxAccount,
                    ["sort_order"] = row.SortOrder,
                    ["is_active"] = row.IsActive,
                    ["is_default"] = row.IsDefault,
                    ["is_default_vat"] = row.IsDefaultVat,
                    ["is_default_sales_tax"] = row.IsDefaultSalesTax,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                // Вид налога и период действия добавляем в набор значений,
                // только когда они заданы в строке сида.
                if (!string.IsNullOrWhiteSpace(row.TaxKind))
                    values["tax_kind"] = row.TaxKind;

                if (row.ValidFrom.HasValue)
                    values["valid_from"] = row.ValidFrom.Value;

                if (row.ValidTo.HasValue)
                    values["valid_to"] = row.ValidTo.Value;

                if (row.IsSystem.HasValue)
                    values["is_system"] = row.IsSystem.Value;

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
            // Ставка, вид налога, период действия и признак служебной записи задаются сидом
            // только при первичной вставке. Значения, изменённые пользователем в справочнике,
            // повторным запуском сида не затираются.
            var seedOnlyColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CreatedAt",
                "is_default",
                "is_default_vat",
                "is_default_sales_tax",
                "rate",
                "tax_kind",
                "valid_from",
                "valid_to",
                "is_system"
            };

            var updateAssignments = filtered
                .Where(item => !item.Key.Equals("code", StringComparison.OrdinalIgnoreCase) &&
                               !seedOnlyColumns.Contains(item.Key))
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

        private static List<MetadataField> GetEsfXmlTagFields(Guid metadataObjectId) => new()
        {
            Field(metadataObjectId, "Ключ", "code", "String", 1, true),
            Field(metadataObjectId, "Наименование", "name", "String", 2, true),
            Field(metadataObjectId, "Имя XML-тега", "tag_name", "String", 3, true),
            Field(metadataObjectId, "Описание", "description", "String", 4),
            Field(metadataObjectId, "Порядок", "sort_order", "Int", 5),
            Field(metadataObjectId, "Активен", "is_active", "Bool", 6)
        };

        private static IReadOnlyList<EsfXmlTagSeedRow> GetDefaultEsfXmlTags() => new[]
        {
            Tag("VFPDataSet", "Корневой тег", "VFPDataSet", "Корневой элемент файла XML ЭСФ.", 1),
            Tag("receipts", "Коллекция receipt", "receipts", "Контейнер записей ЭСФ.", 2),
            Tag("receipt", "Запись receipt", "receipt", "Одна запись ЭСФ.", 3),
            Tag("goods", "Коллекция good", "goods", "Контейнер строк товаров/услуг.", 4),
            Tag("good", "Строка good", "good", "Одна строка товара/услуги.", 5),
            Tag("exchangeCode", "Код обмена", "exchangeCode", "Уникальный GUID обмена с налоговой.", 10),
            Tag("receiptTypeCode", "Тип квитанции", "receiptTypeCode", "Код типа receipt.", 11),
            Tag("createdDate", "Дата создания", "createdDate", "Дата создания выгрузки.", 12),
            Tag("ownedCrmReceiptCode", "Локальный код CRM", "ownedCrmReceiptCode", "Номер документа/бланка в локальной системе.", 13),
            Tag("correctedReceiptCode", "Исправляемый receipt", "correctedReceiptCode", "Код исправляемой записи.", 14),
            Tag("correctionReasonCode", "Код причины исправления", "correctionReasonCode", "Причина корректировки.", 15),
            Tag("bankAccount", "Расчетный счет продавца", "bankAccount", "Банковский счет организации.", 16),
            Tag("contractorPin", "ПИН контрагента", "contractorPin", "ИНН/ПИН контрагента.", 17),
            Tag("contractorBankAccount", "Счет контрагента", "contractorBankAccount", "Банковский счет контрагента.", 18),
            Tag("deliveryContractNumber", "Номер договора", "deliveryContractNumber", "Номер договора поставки.", 19),
            Tag("deliveryContractDate", "Дата договора", "deliveryContractDate", "Дата договора поставки.", 20),
            Tag("goodsDeliveryTypeCode", "Код доставки", "goodsDeliveryTypeCode", "Тип доставки товаров.", 21),
            Tag("paymentTypeCode", "Код оплаты", "paymentTypeCode", "Тип оплаты ЭСФ.", 22),
            Tag("invoiceDeliveryTypeCode", "Код вида поставки", "invoiceDeliveryTypeCode", "Код вида поставки ЭСФ.", 23),
            Tag("vatDeliveryTypeCode", "Код типа поставки НДС", "vatDeliveryTypeCode", "Код типа поставки для НДС.", 24),
            Tag("currencyCode", "Код валюты", "currencyCode", "Код валюты.", 25),
            Tag("exchangeRate", "Курс", "exchangeRate", "Курс валюты.", 26),
            Tag("contractorCitizenshipCode", "Код гражданства", "contractorCitizenshipCode", "Код государства контрагента.", 27),
            Tag("isPriceWithoutTaxes", "Цена без налогов", "isPriceWithoutTaxes", "Признак цены без налогов.", 28),
            Tag("note", "Примечание", "note", "Описание/основание.", 29),
            Tag("vatCode", "Код НДС", "vatCode", "Код НДС ЭСФ.", 30),
            Tag("isResident", "Резидент", "isResident", "Признак резидента.", 31),
            Tag("foreignName", "Иностранное имя", "foreignName", "Наименование иностранного контрагента.", 32),
            Tag("sellerBranchPin", "ПИН филиала продавца", "sellerBranchPin", "ПИН филиала продавца.", 33),
            Tag("isIndustry", "Отраслевой признак", "isIndustry", "Отраслевой признак.", 34),
            Tag("openingBalances", "Начальное сальдо", "openingBalances", "Начальное сальдо.", 35),
            Tag("assessedContributionsAmount", "Начислено взносов", "assessedContributionsAmount", "Сумма начисленных взносов.", 36),
            Tag("paidAmount", "Оплачено", "paidAmount", "Оплаченная сумма.", 37),
            Tag("penaltiesAmount", "Пени", "penaltiesAmount", "Сумма пени.", 38),
            Tag("finesAmount", "Штрафы", "finesAmount", "Сумма штрафов.", 39),
            Tag("closingBalances", "Конечное сальдо", "closingBalances", "Конечное сальдо.", 40),
            Tag("amountToBePaid", "К оплате", "amountToBePaid", "Сумма к оплате.", 41),
            Tag("personalAccountNumber", "Лицевой счет", "personalAccountNumber", "Номер лицевого счета.", 42),
            Tag("markGoods", "Маркированные товары", "markGoods", "Признак маркируемых товаров.", 43),
            Tag("invoiceDate", "Дата ЭСФ", "invoiceDate", "Дата счета-фактуры.", 44),
            Tag("invoiceNumber", "Номер ЭСФ", "invoiceNumber", "Номер ЭСФ.", 45),
            Tag("contractorName", "Контрагент", "contractorName", "Наименование контрагента.", 46),
            Tag("contractorBranchName", "Филиал контрагента", "contractorBranchName", "Филиал контрагента.", 47),
            Tag("currencyName", "Валюта", "currencyName", "Наименование валюты.", 48),
            Tag("contractorCitizenshipName", "Гражданство", "contractorCitizenshipName", "Наименование государства контрагента.", 49),
            Tag("correctedReceiptCreationDate", "Дата исправляемой записи", "correctedReceiptCreationDate", "Дата создания исправляемой записи.", 50),
            Tag("correctionReasonName", "Причина исправления", "correctionReasonName", "Наименование причины исправления.", 51),
            Tag("documentStatusName", "Статус документа", "documentStatusName", "Статус документа в налоговой.", 52),
            Tag("correctionSeries", "Серия исправления", "correctionSeries", "Серия исправления.", 53),
            Tag("type", "Тип", "type", "Тип документа.", 54),
            Tag("costWithoutTaxes", "Сумма без налогов", "costWithoutTaxes", "Итого без налогов.", 55),
            Tag("totalCost", "Итого", "totalCost", "Итоговая сумма.", 56),
            Tag("vatAmount", "НДС строки", "vatAmount", "Сумма НДС по строке.", 70),
            Tag("stCode", "Код НСП строки", "stCode", "Код налога с продаж по строке.", 71),
            Tag("stAmount", "НСП строки", "stAmount", "Сумма налога с продаж по строке.", 72),
            Tag("goodsName", "Наименование строки", "goodsName", "Наименование товара/услуги.", 73),
            Tag("baseCount", "Количество", "baseCount", "Количество товара/услуги.", 74),
            Tag("price", "Цена", "price", "Цена товара/услуги.", 75)
        };

        private static EsfXmlTagSeedRow Tag(string key, string title, string tagName, string description, int order) =>
            new(key, title, tagName, description, order);

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
            string? VatPayableAccount = null,
            string? VatRecoverableAccount = null,
            string? SalesTaxAccount = null,
            int? SortOrder = null,
            bool IsDefault = false,
            bool IsDefaultVat = false,
            bool IsDefaultSalesTax = false,
            bool IsActive = true,
            string? TaxKind = null,
            DateTime? ValidFrom = null,
            DateTime? ValidTo = null,
            bool? IsSystem = null);

        private sealed record EsfXmlTagSeedRow(
            string Key,
            string Title,
            string TagName,
            string Description,
            int Order);
    }
}
