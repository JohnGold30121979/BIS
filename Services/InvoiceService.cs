using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    public class InvoiceService
    {
        private const string DefaultPurchaseAccount = "31100000";
        private const string DefaultPurchaseLineAccount = "16100000";
        private const string DefaultSalesAccount = "14100000";
        private const string DefaultSalesRevenueAccount = "61100000";
        private const string DefaultVatPayableAccount = "34300000";
        private const string DefaultVatRecoverableAccount = "15400000";
        private const string DefaultSalesTaxAccount = "34900000";

        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;

        public InvoiceService(AppDbContext context)
        {
            _context = context;
            _metadataService = new MetadataService(context);
        }

        public string DocumentName { get; private set; } = string.Empty;
        public string HeaderTableName { get; private set; } = string.Empty;
        public string LinesTableName { get; private set; } = string.Empty;

        public void Configure(MetadataObject document)
        {
            DocumentName = document.Name;
            HeaderTableName = document.TableName;
            LinesTableName = document.TableName + "_lines";
        }

        public async Task EnsureSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""doc_sales_invoice"" (
                    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""doc_number"" varchar(50) NOT NULL,
                    ""doc_date"" timestamp NOT NULL,
                    ""esf_number"" varchar(100),
                    ""tax_blank_number"" varchar(100),
                    ""module_code"" varchar(50),
                    ""exchange_code"" varchar(100),
                    ""tax_status"" varchar(100),
                    ""exported_at"" timestamp,
                    ""tax_status_date"" timestamp,
                    ""organization_id"" uuid,
                    ""counterparty_account"" varchar(50),
                    ""payment_kind"" varchar(100),
                    ""delivery_kind"" varchar(100),
                    ""supply_kind"" varchar(100),
                    ""basis"" varchar(500),
                    ""amount_without_tax"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""vat_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""sales_tax_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""is_posted"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS ""doc_sales_invoice_lines"" (
                    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""invoice_id"" uuid NOT NULL REFERENCES ""doc_sales_invoice""(""Id"") ON DELETE CASCADE,
                    ""line_number"" integer NOT NULL,
                    ""name"" varchar(500) NOT NULL,
                    ""unit_name"" varchar(50),
                    ""quantity"" numeric(18,3) NOT NULL DEFAULT 1,
                    ""account_code"" varchar(50),
                    ""vat_tax_code"" varchar(50),
                    ""amount_without_tax"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""vat_rate"" numeric(8,2) NOT NULL DEFAULT 0,
                    ""vat_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""sales_tax_code"" varchar(50),
                    ""sales_tax_rate"" numeric(8,2) NOT NULL DEFAULT 0,
                    ""sales_tax_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""line_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS ""doc_purchase_invoice"" (
                    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""doc_number"" varchar(50) NOT NULL,
                    ""doc_date"" timestamp NOT NULL,
                    ""esf_number"" varchar(100),
                    ""tax_blank_number"" varchar(100),
                    ""module_code"" varchar(50),
                    ""exchange_code"" varchar(100),
                    ""tax_status"" varchar(100),
                    ""exported_at"" timestamp,
                    ""tax_status_date"" timestamp,
                    ""organization_id"" uuid,
                    ""counterparty_account"" varchar(50),
                    ""payment_kind"" varchar(100),
                    ""delivery_kind"" varchar(100),
                    ""supply_kind"" varchar(100),
                    ""basis"" varchar(500),
                    ""amount_without_tax"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""vat_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""sales_tax_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""is_posted"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS ""doc_purchase_invoice_lines"" (
                    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""invoice_id"" uuid NOT NULL REFERENCES ""doc_purchase_invoice""(""Id"") ON DELETE CASCADE,
                    ""line_number"" integer NOT NULL,
                    ""name"" varchar(500) NOT NULL,
                    ""unit_name"" varchar(50),
                    ""quantity"" numeric(18,3) NOT NULL DEFAULT 1,
                    ""account_code"" varchar(50),
                    ""vat_tax_code"" varchar(50),
                    ""amount_without_tax"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""vat_rate"" numeric(8,2) NOT NULL DEFAULT 0,
                    ""vat_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""sales_tax_code"" varchar(50),
                    ""sales_tax_rate"" numeric(8,2) NOT NULL DEFAULT 0,
                    ""sales_tax_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""line_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS ""IX_doc_sales_invoice_lines_invoice"" ON ""doc_sales_invoice_lines"" (""invoice_id"");
                CREATE INDEX IF NOT EXISTS ""IX_doc_purchase_invoice_lines_invoice"" ON ""doc_purchase_invoice_lines"" (""invoice_id"");
                ALTER TABLE ""doc_sales_invoice_lines"" ADD COLUMN IF NOT EXISTS ""vat_tax_code"" varchar(50);
                ALTER TABLE ""doc_sales_invoice_lines"" ADD COLUMN IF NOT EXISTS ""sales_tax_code"" varchar(50);
                ALTER TABLE ""doc_purchase_invoice_lines"" ADD COLUMN IF NOT EXISTS ""vat_tax_code"" varchar(50);
                ALTER TABLE ""doc_purchase_invoice_lines"" ADD COLUMN IF NOT EXISTS ""sales_tax_code"" varchar(50);
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""tax_blank_number"" varchar(100);
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""module_code"" varchar(50);
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""exchange_code"" varchar(100);
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""tax_status"" varchar(100);
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""exported_at"" timestamp;
                ALTER TABLE ""doc_sales_invoice"" ADD COLUMN IF NOT EXISTS ""tax_status_date"" timestamp;
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""tax_blank_number"" varchar(100);
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""module_code"" varchar(50);
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""exchange_code"" varchar(100);
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""tax_status"" varchar(100);
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""exported_at"" timestamp;
                ALTER TABLE ""doc_purchase_invoice"" ADD COLUMN IF NOT EXISTS ""tax_status_date"" timestamp;
                ALTER TABLE ""doc_sales_invoice_lines"" ADD COLUMN IF NOT EXISTS ""unit_name"" varchar(50);
                ALTER TABLE ""doc_sales_invoice_lines"" ADD COLUMN IF NOT EXISTS ""quantity"" numeric(18,3) NOT NULL DEFAULT 1;
                ALTER TABLE ""doc_purchase_invoice_lines"" ADD COLUMN IF NOT EXISTS ""unit_name"" varchar(50);
                ALTER TABLE ""doc_purchase_invoice_lines"" ADD COLUMN IF NOT EXISTS ""quantity"" numeric(18,3) NOT NULL DEFAULT 1;
                DO $$
                BEGIN
                    IF to_regclass('public.doc_postings') IS NOT NULL THEN
                        ALTER TABLE ""doc_postings"" ADD COLUMN IF NOT EXISTS ""module_code"" varchar(50);
                    END IF;
                END $$;
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'doc_sales_invoice'
                          AND column_name = 'arm_code') THEN
                        UPDATE ""doc_sales_invoice""
                        SET ""module_code"" = COALESCE(NULLIF(""module_code"", ''), ""arm_code"")
                        WHERE COALESCE(NULLIF(""arm_code"", ''), '') <> '';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'doc_purchase_invoice'
                          AND column_name = 'arm_code') THEN
                        UPDATE ""doc_purchase_invoice""
                        SET ""module_code"" = COALESCE(NULLIF(""module_code"", ''), ""arm_code"")
                        WHERE COALESCE(NULLIF(""arm_code"", ''), '') <> '';
                    END IF;
                END $$;
                CREATE INDEX IF NOT EXISTS ""IX_doc_sales_invoice_exchange_code"" ON ""doc_sales_invoice"" (""exchange_code"");
                CREATE INDEX IF NOT EXISTS ""IX_doc_purchase_invoice_exchange_code"" ON ""doc_purchase_invoice"" (""exchange_code"");
            ");
        }

        public async Task<List<InvoiceListRow>> GetInvoicesAsync()
        {
            var organizationMap = await LoadOrganizationMapAsync();
            var sql = $@"
                SELECT ""Id"", ""doc_number"", ""doc_date"", ""organization_id"", ""amount"", ""esf_number"", ""basis"", ""is_posted"",
                       ""tax_blank_number"", ""module_code"", ""exchange_code"", ""tax_status"", ""exported_at"", ""tax_status_date""
                FROM ""{HeaderTableName}""
                ORDER BY ""doc_date"" DESC, ""doc_number"" DESC";

            var result = new List<InvoiceListRow>();
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await _context.Database.OpenConnectionAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var organizationId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
                result.Add(new InvoiceListRow
                {
                    Id = reader.GetGuid(0),
                    DocNumber = MetadataService.NormalizeLegacyDocumentNumber(reader.GetString(1)),
                    DocDate = reader.GetDateTime(2),
                    OrganizationName = organizationId.HasValue && organizationMap.TryGetValue(organizationId.Value, out var name)
                        ? name
                        : string.Empty,
                    TotalAmount = reader.GetDecimal(4),
                    EsfNumber = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Basis = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    IsPosted = reader.GetBoolean(7),
                    TaxBlankNumber = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    ModuleCode = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    ExchangeCode = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    TaxStatus = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                    ExportedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                    TaxStatusDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13)
                });
            }
            await _context.Database.CloseConnectionAsync();
            return result;
        }

        public async Task<List<InvoiceLineRow>> GetLinesAsync(Guid invoiceId)
        {
            var accountMap = await LoadAccountMapAsync();
            var sql = $@"
                SELECT ""Id"", ""line_number"", ""name"", ""unit_name"", ""quantity"", ""account_code"",
                       ""vat_tax_code"", ""amount_without_tax"", ""vat_rate"", ""vat_amount"",
                       ""sales_tax_code"", ""sales_tax_rate"", ""sales_tax_amount"", ""line_total""
                FROM ""{LinesTableName}""
                WHERE ""invoice_id"" = @invoiceId
                ORDER BY ""line_number""";

            var result = new List<InvoiceLineRow>();
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@invoiceId", invoiceId));
            await _context.Database.OpenConnectionAsync();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var accountCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                result.Add(new InvoiceLineRow
                {
                    Id = reader.GetGuid(0),
                    LineNumber = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    UnitName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Quantity = reader.IsDBNull(4) ? 1m : reader.GetDecimal(4),
                    AccountCode = accountCode,
                    AccountName = ResolveAccount(accountCode, accountMap),
                    VatTaxCode = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    AmountWithoutTax = reader.GetDecimal(7),
                    VatRate = reader.GetDecimal(8),
                    VatAmount = reader.GetDecimal(9),
                    SalesTaxCode = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    SalesTaxRate = reader.GetDecimal(11),
                    SalesTaxAmount = reader.GetDecimal(12),
                    LineTotal = reader.GetDecimal(13)
                });
            }
            await _context.Database.CloseConnectionAsync();
            return result;
        }

        public async Task<InvoiceDocument?> GetInvoiceAsync(Guid invoiceId)
        {
            var organizationMap = await LoadOrganizationMapAsync();
            var sql = $@"
                SELECT ""Id"", ""doc_number"", ""doc_date"", ""esf_number"", ""tax_blank_number"", ""module_code"",
                       ""exchange_code"", ""tax_status"", ""exported_at"", ""tax_status_date"", ""organization_id"",
                       ""counterparty_account"", ""payment_kind"", ""delivery_kind"", ""supply_kind"", ""basis"",
                       ""amount_without_tax"", ""vat_total"", ""sales_tax_total"", ""amount"", ""is_posted""
                FROM ""{HeaderTableName}""
                WHERE ""Id"" = @invoiceId";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@invoiceId", invoiceId));
            await _context.Database.OpenConnectionAsync();
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await _context.Database.CloseConnectionAsync();
                return null;
            }

            var organizationId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10);
            var invoice = new InvoiceDocument
            {
                Id = reader.GetGuid(0),
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(reader.GetString(1)),
                DocDate = reader.GetDateTime(2),
                EsfNumber = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                TaxBlankNumber = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                ModuleCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                ExchangeCode = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                TaxStatus = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                ExportedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                TaxStatusDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                OrganizationId = organizationId,
                OrganizationName = organizationId.HasValue && organizationMap.TryGetValue(organizationId.Value, out var name)
                    ? name
                    : string.Empty,
                CounterpartyAccountCode = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                PaymentKind = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                DeliveryKind = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                SupplyKind = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                Basis = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                AmountWithoutTax = reader.GetDecimal(16),
                VatTotal = reader.GetDecimal(17),
                SalesTaxTotal = reader.GetDecimal(18),
                TotalAmount = reader.GetDecimal(19),
                IsPosted = reader.GetBoolean(20)
            };
            await reader.CloseAsync();
            await _context.Database.CloseConnectionAsync();
            invoice.Lines = await GetLinesAsync(invoiceId);
            return invoice;
        }

        public async Task<string> GenerateDocumentNumberAsync()
        {
            var metadata = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Name == DocumentName && item.ObjectType == "Document");
            if (metadata == null)
                return DateTime.Today.ToString("yyMM") + "0001";

            return await _metadataService.GetNextDocumentNumberAsync(metadata.Name);
        }

        public async Task<Guid?> FindInvoiceIdAsync(string documentNumber, DateTime? documentDate = null)
        {
            var normalizedNumber = MetadataService.NormalizeLegacyDocumentNumber(documentNumber);
            if (string.IsNullOrWhiteSpace(normalizedNumber))
                return null;

            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE ""doc_number"" = @number";

            if (documentDate.HasValue)
                sql += @" AND DATE(""doc_date"") = DATE(@date)";

            sql += @" ORDER BY ""UpdatedAt"" DESC LIMIT 1";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@number", normalizedNumber));
            if (documentDate.HasValue)
                command.Parameters.Add(new NpgsqlParameter("@date", documentDate.Value.Date));

            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value is Guid id ? id : null;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<Guid?> FindInvoiceIdByPostingNumberAsync(string postingDocumentNumber, DateTime? documentDate = null)
        {
            var numbers = BuildInvoiceLookupNumbers(postingDocumentNumber);
            if (numbers.Count == 0)
                return null;

            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE LOWER(COALESCE(""doc_number"", '')) = ANY(@numbers)
                   OR LOWER(COALESCE(""tax_blank_number"", '')) = ANY(@numbers)
                   OR LOWER(COALESCE(""esf_number"", '')) = ANY(@numbers)
                   OR LOWER(COALESCE(""exchange_code"", '')) = ANY(@numbers)";

            if (documentDate.HasValue)
            {
                sql += @"
                ORDER BY CASE WHEN DATE(""doc_date"") = DATE(@date) THEN 0 ELSE 1 END,
                         ""UpdatedAt"" DESC
                LIMIT 1";
            }
            else
            {
                sql += @"
                ORDER BY ""UpdatedAt"" DESC
                LIMIT 1";
            }

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@numbers", numbers.Select(item => item.ToLowerInvariant()).ToArray()));
            if (documentDate.HasValue)
                command.Parameters.Add(new NpgsqlParameter("@date", documentDate.Value.Date));

            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value is Guid id ? id : null;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        private static IReadOnlyCollection<string> BuildInvoiceLookupNumbers(string? documentNumber)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddCandidate(documentNumber);
            AddCandidate(MetadataService.NormalizeLegacyDocumentNumber(documentNumber));

            if (!string.IsNullOrWhiteSpace(documentNumber))
            {
                var digitsOnly = new string(documentNumber.Where(char.IsDigit).ToArray());
                AddCandidate(digitsOnly);
            }

            return result.ToArray();

            void AddCandidate(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                result.Add(value.Trim());
            }
        }

        public async Task<Guid?> FindInvoiceIdByExchangeCodeAsync(string exchangeCode)
        {
            if (string.IsNullOrWhiteSpace(exchangeCode))
                return null;

            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE ""exchange_code"" = @exchangeCode
                ORDER BY ""UpdatedAt"" DESC
                LIMIT 1";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@exchangeCode", exchangeCode.Trim()));
            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value is Guid id ? id : null;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<Guid?> FindInvoiceIdByTaxBlankNumberAsync(string taxBlankNumber, DateTime? documentDate = null)
        {
            var normalizedNumber = taxBlankNumber?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedNumber))
                return null;

            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE ""tax_blank_number"" = @number";

            if (documentDate.HasValue)
                sql += @" AND DATE(""doc_date"") = DATE(@date)";

            sql += @" ORDER BY ""UpdatedAt"" DESC LIMIT 1";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@number", normalizedNumber));
            if (documentDate.HasValue)
                command.Parameters.Add(new NpgsqlParameter("@date", documentDate.Value.Date));

            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value is Guid id ? id : null;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<Guid?> FindInvoiceIdByEsfNumberAsync(string esfNumber)
        {
            var normalizedNumber = esfNumber?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedNumber))
                return null;

            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE ""esf_number"" = @number
                ORDER BY ""UpdatedAt"" DESC
                LIMIT 1";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@number", normalizedNumber));
            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value is Guid id ? id : null;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task UpdateEsfExchangeInfoAsync(
            Guid invoiceId,
            string? esfNumber,
            string? exchangeCode,
            string? taxStatus,
            DateTime? exportedAt,
            DateTime? taxStatusDate)
        {
            var invoice = await GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Документ не найден.");

            await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{HeaderTableName}""
                SET ""esf_number"" = @esfNumber,
                    ""exchange_code"" = @exchangeCode,
                    ""tax_status"" = @taxStatus,
                    ""exported_at"" = @exportedAt,
                    ""tax_status_date"" = @taxStatusDate,
                    ""UpdatedAt"" = NOW()
                WHERE ""Id"" = @id;",
                new NpgsqlParameter("@id", invoiceId),
                new NpgsqlParameter("@esfNumber", DbValueOrNull(esfNumber)),
                new NpgsqlParameter("@exchangeCode", DbValueOrNull(exchangeCode)),
                new NpgsqlParameter("@taxStatus", DbValueOrNull(taxStatus)),
                new NpgsqlParameter("@exportedAt", (object?)exportedAt ?? DBNull.Value),
                new NpgsqlParameter("@taxStatusDate", (object?)taxStatusDate ?? DBNull.Value));

            await new EventLogService(_context).LogAsync(
                "UpdateEsfExchange",
                "Document",
                DocumentName,
                invoiceId,
                new
                {
                    Number = invoice.DocNumber,
                    EsfNumber = esfNumber?.Trim() ?? string.Empty,
                    ExchangeCode = exchangeCode?.Trim() ?? string.Empty,
                    TaxStatus = taxStatus?.Trim() ?? string.Empty,
                    ExportedAt = exportedAt,
                    TaxStatusDate = taxStatusDate
                });
        }

        public async Task<Guid> SaveInvoiceAsync(InvoiceDocument invoice, Guid? existingId = null)
        {
            RecalculateTotals(invoice);
            invoice.DocNumber = MetadataService.NormalizeLegacyDocumentNumber(invoice.DocNumber);
            invoice.ModuleCode = await ResolveInvoiceModuleNameAsync(invoice.ModuleCode);
            var id = existingId ?? Guid.NewGuid();
            var isNew = !existingId.HasValue;
            var existing = isNew ? null : await GetInvoiceAsync(id);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (isNew)
                {
                    await _context.Database.ExecuteSqlRawAsync($@"
                        INSERT INTO ""{HeaderTableName}""
                        (""Id"", ""doc_number"", ""doc_date"", ""esf_number"", ""tax_blank_number"", ""module_code"",
                         ""exchange_code"", ""tax_status"", ""exported_at"", ""tax_status_date"", ""organization_id"", ""counterparty_account"",
                         ""payment_kind"", ""delivery_kind"", ""supply_kind"", ""basis"",
                         ""amount_without_tax"", ""vat_total"", ""sales_tax_total"", ""amount"", ""is_posted"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (@id, @number, @date, @esf, @taxBlank, @moduleCode, @exchangeCode, @taxStatus, @exportedAt, @taxStatusDate, @org, @account, @payment, @delivery, @supply, @basis,
                                @amountWithoutTax, @vatTotal, @salesTaxTotal, @amount, false, NOW(), NOW())",
                        new NpgsqlParameter("@id", id),
                        new NpgsqlParameter("@number", invoice.DocNumber),
                        new NpgsqlParameter("@date", invoice.DocDate),
                        new NpgsqlParameter("@esf", (object?)invoice.EsfNumber ?? DBNull.Value),
                        new NpgsqlParameter("@taxBlank", (object?)invoice.TaxBlankNumber ?? DBNull.Value),
                        new NpgsqlParameter("@moduleCode", (object?)invoice.ModuleCode ?? DBNull.Value),
                        new NpgsqlParameter("@exchangeCode", (object?)invoice.ExchangeCode ?? DBNull.Value),
                        new NpgsqlParameter("@taxStatus", (object?)invoice.TaxStatus ?? DBNull.Value),
                        new NpgsqlParameter("@exportedAt", (object?)invoice.ExportedAt ?? DBNull.Value),
                        new NpgsqlParameter("@taxStatusDate", (object?)invoice.TaxStatusDate ?? DBNull.Value),
                        new NpgsqlParameter("@org", (object?)invoice.OrganizationId ?? DBNull.Value),
                        new NpgsqlParameter("@account", (object?)invoice.CounterpartyAccountCode ?? DBNull.Value),
                        new NpgsqlParameter("@payment", (object?)invoice.PaymentKind ?? DBNull.Value),
                        new NpgsqlParameter("@delivery", (object?)invoice.DeliveryKind ?? DBNull.Value),
                        new NpgsqlParameter("@supply", (object?)invoice.SupplyKind ?? DBNull.Value),
                        new NpgsqlParameter("@basis", (object?)invoice.Basis ?? DBNull.Value),
                        new NpgsqlParameter("@amountWithoutTax", invoice.AmountWithoutTax),
                        new NpgsqlParameter("@vatTotal", invoice.VatTotal),
                        new NpgsqlParameter("@salesTaxTotal", invoice.SalesTaxTotal),
                        new NpgsqlParameter("@amount", invoice.TotalAmount));
                }
                else
                {
                    await _context.Database.ExecuteSqlRawAsync($@"
                        UPDATE ""{HeaderTableName}""
                        SET ""doc_number"" = @number, ""doc_date"" = @date, ""esf_number"" = @esf,
                            ""tax_blank_number"" = @taxBlank, ""module_code"" = @moduleCode,
                            ""exchange_code"" = @exchangeCode, ""tax_status"" = @taxStatus,
                            ""exported_at"" = @exportedAt, ""tax_status_date"" = @taxStatusDate,
                            ""organization_id"" = @org, ""counterparty_account"" = @account,
                            ""payment_kind"" = @payment, ""delivery_kind"" = @delivery, ""supply_kind"" = @supply,
                            ""basis"" = @basis, ""amount_without_tax"" = @amountWithoutTax,
                            ""vat_total"" = @vatTotal, ""sales_tax_total"" = @salesTaxTotal,
                            ""amount"" = @amount, ""is_posted"" = false, ""UpdatedAt"" = NOW()
                        WHERE ""Id"" = @id",
                        new NpgsqlParameter("@id", id),
                        new NpgsqlParameter("@number", invoice.DocNumber),
                        new NpgsqlParameter("@date", invoice.DocDate),
                        new NpgsqlParameter("@esf", (object?)invoice.EsfNumber ?? DBNull.Value),
                        new NpgsqlParameter("@taxBlank", (object?)invoice.TaxBlankNumber ?? DBNull.Value),
                        new NpgsqlParameter("@moduleCode", (object?)invoice.ModuleCode ?? DBNull.Value),
                        new NpgsqlParameter("@exchangeCode", (object?)invoice.ExchangeCode ?? DBNull.Value),
                        new NpgsqlParameter("@taxStatus", (object?)invoice.TaxStatus ?? DBNull.Value),
                        new NpgsqlParameter("@exportedAt", (object?)invoice.ExportedAt ?? DBNull.Value),
                        new NpgsqlParameter("@taxStatusDate", (object?)invoice.TaxStatusDate ?? DBNull.Value),
                        new NpgsqlParameter("@org", (object?)invoice.OrganizationId ?? DBNull.Value),
                        new NpgsqlParameter("@account", (object?)invoice.CounterpartyAccountCode ?? DBNull.Value),
                        new NpgsqlParameter("@payment", (object?)invoice.PaymentKind ?? DBNull.Value),
                        new NpgsqlParameter("@delivery", (object?)invoice.DeliveryKind ?? DBNull.Value),
                        new NpgsqlParameter("@supply", (object?)invoice.SupplyKind ?? DBNull.Value),
                        new NpgsqlParameter("@basis", (object?)invoice.Basis ?? DBNull.Value),
                        new NpgsqlParameter("@amountWithoutTax", invoice.AmountWithoutTax),
                        new NpgsqlParameter("@vatTotal", invoice.VatTotal),
                        new NpgsqlParameter("@salesTaxTotal", invoice.SalesTaxTotal),
                        new NpgsqlParameter("@amount", invoice.TotalAmount));

                    await _context.Database.ExecuteSqlRawAsync(
                        $@"DELETE FROM ""{LinesTableName}"" WHERE ""invoice_id"" = @id;",
                        new NpgsqlParameter("@id", id));
                }

                var lineNumber = 1;
                foreach (var line in invoice.Lines)
                {
                    RecalculateLine(line);
                    line.Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id;
                    line.LineNumber = lineNumber;
                    await _context.Database.ExecuteSqlRawAsync($@"
                        INSERT INTO ""{LinesTableName}""
                        (""Id"", ""invoice_id"", ""line_number"", ""name"", ""unit_name"", ""quantity"", ""account_code"", ""vat_tax_code"",
                         ""amount_without_tax"", ""vat_rate"", ""vat_amount"", ""sales_tax_code"", ""sales_tax_rate"", ""sales_tax_amount"",
                         ""line_total"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (@lineId, @invoiceId, @lineNumber, @name, @unitName, @quantity, @account, @vatTaxCode,
                                @amountWithoutTax, @vatRate, @vatAmount, @salesTaxCode, @salesTaxRate, @salesTaxAmount,
                                @lineTotal, NOW(), NOW())",
                        new NpgsqlParameter("@lineId", line.Id),
                        new NpgsqlParameter("@invoiceId", id),
                        new NpgsqlParameter("@lineNumber", lineNumber++),
                        new NpgsqlParameter("@name", line.Name),
                        new NpgsqlParameter("@unitName", (object?)line.UnitName ?? DBNull.Value),
                        new NpgsqlParameter("@quantity", line.Quantity <= 0 ? 1m : line.Quantity),
                        new NpgsqlParameter("@account", (object?)line.AccountCode ?? DBNull.Value),
                        new NpgsqlParameter("@vatTaxCode", (object?)line.VatTaxCode ?? DBNull.Value),
                        new NpgsqlParameter("@amountWithoutTax", line.AmountWithoutTax),
                        new NpgsqlParameter("@vatRate", line.VatRate),
                        new NpgsqlParameter("@vatAmount", line.VatAmount),
                        new NpgsqlParameter("@salesTaxCode", (object?)line.SalesTaxCode ?? DBNull.Value),
                        new NpgsqlParameter("@salesTaxRate", line.SalesTaxRate),
                        new NpgsqlParameter("@salesTaxAmount", line.SalesTaxAmount),
                        new NpgsqlParameter("@lineTotal", line.LineTotal));
                }

                await PostInvoiceWithinCurrentTransactionAsync(invoice, id, existing?.DocNumber);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await new EventLogService(_context).LogAsync(
                isNew ? "Create" : "Update",
                "Document",
                DocumentName,
                id,
                new { Number = invoice.DocNumber, Amount = invoice.TotalAmount });

            await new EventLogService(_context).LogAsync(
                "Post",
                "Document",
                DocumentName,
                id,
                new { Number = invoice.DocNumber, Amount = invoice.TotalAmount });

            return id;
        }

        public async Task UpdateRegistrationInfoAsync(Guid invoiceId, string taxBlankNumber, string moduleCode)
        {
            var invoice = await GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Документ не найден.");

            await _context.Database.ExecuteSqlRawAsync($@"
                UPDATE ""{HeaderTableName}""
                SET ""tax_blank_number"" = @taxBlank, ""module_code"" = @moduleCode, ""UpdatedAt"" = NOW()
                WHERE ""Id"" = @id;",
                new NpgsqlParameter("@id", invoiceId),
                new NpgsqlParameter("@taxBlank", (object?)taxBlankNumber?.Trim() ?? DBNull.Value),
                new NpgsqlParameter("@moduleCode", (object?)moduleCode?.Trim() ?? DBNull.Value));

            await new EventLogService(_context).LogAsync(
                "UpdateRegistration",
                "Document",
                DocumentName,
                invoiceId,
                new
                {
                    Number = invoice.DocNumber,
                    TaxBlankNumber = taxBlankNumber?.Trim() ?? string.Empty,
                    Module = moduleCode?.Trim() ?? string.Empty
                });
        }

        public async Task DeleteInvoiceAsync(Guid invoiceId)
        {
            var invoice = await GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Документ не найден.");
            if (invoice.IsPosted)
                throw new InvalidOperationException("Проведённый документ нельзя удалить.");

            await _context.Database.ExecuteSqlRawAsync(
                $@"DELETE FROM ""{HeaderTableName}"" WHERE ""Id"" = @id;",
                new NpgsqlParameter("@id", invoiceId));
            await new EventLogService(_context).LogAsync(
                "Delete",
                "Document",
                DocumentName,
                invoiceId,
                new { Number = invoice.DocNumber, Amount = invoice.TotalAmount });
        }

        public async Task PostInvoiceAsync(Guid invoiceId)
        {
            var invoice = await GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Документ не найден.");
            if (invoice.IsPosted)
                throw new InvalidOperationException("Документ уже проведён.");
            ValidateInvoiceForPosting(invoice);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await PostInvoiceWithinCurrentTransactionAsync(invoice, invoiceId);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            await new EventLogService(_context).LogAsync(
                "Post",
                "Document",
                DocumentName,
                invoiceId,
                new { Number = invoice.DocNumber, Amount = invoice.TotalAmount });
        }

        public async Task<int> EnsureSavedInvoicesPostedAsync()
        {
            var ids = new List<Guid>();
            var sql = $@"
                SELECT ""Id""
                FROM ""{HeaderTableName}""
                WHERE COALESCE(""is_posted"", false) = false
                ORDER BY ""doc_date"", ""doc_number""";

            await using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = sql;
                await _context.Database.OpenConnectionAsync();
                try
                {
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        ids.Add(reader.GetGuid(0));
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

            var postedCount = 0;
            foreach (var id in ids)
            {
                try
                {
                    await PostInvoiceAsync(id);
                    postedCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Ошибка автоматического проведения счет-фактуры {id}: {ex.Message}");
                }
            }

            return postedCount;
        }

        public static void RecalculateLine(InvoiceLineRow line)
        {
            line.VatAmount = Math.Round(line.AmountWithoutTax * line.VatRate / 100m, 2);
            line.SalesTaxAmount = Math.Round(line.AmountWithoutTax * line.SalesTaxRate / 100m, 2);
            line.LineTotal = line.AmountWithoutTax + line.VatAmount + line.SalesTaxAmount;
        }

        public static void RecalculateTotals(InvoiceDocument invoice)
        {
            foreach (var line in invoice.Lines)
                RecalculateLine(line);

            invoice.AmountWithoutTax = invoice.Lines.Sum(line => line.AmountWithoutTax);
            invoice.VatTotal = invoice.Lines.Sum(line => line.VatAmount);
            invoice.SalesTaxTotal = invoice.Lines.Sum(line => line.SalesTaxAmount);
            invoice.TotalAmount = invoice.Lines.Sum(line => line.LineTotal);
        }

        private static void ValidateInvoiceForPosting(InvoiceDocument invoice)
        {
            if (invoice.Lines.Count == 0)
                throw new InvalidOperationException("Добавьте хотя бы одну строку счета-фактуры.");
            if (invoice.TotalAmount <= 0)
                throw new InvalidOperationException("Сумма документа должна быть больше нуля.");
        }

        private async Task PostInvoiceWithinCurrentTransactionAsync(
            InvoiceDocument invoice,
            Guid invoiceId,
            string? previousDocumentNumber = null)
        {
            ValidateInvoiceForPosting(invoice);

            var counterpartyAccount = string.IsNullOrWhiteSpace(invoice.CounterpartyAccountCode)
                ? InvoiceDocumentTypes.IsSales(DocumentName) ? DefaultSalesAccount : DefaultPurchaseAccount
                : invoice.CounterpartyAccountCode.Trim();
            var taxPostingEntries = await LoadTaxPostingEntriesAsync();

            if (!string.IsNullOrWhiteSpace(previousDocumentNumber) &&
                !previousDocumentNumber.Trim().Equals(invoice.DocNumber, StringComparison.OrdinalIgnoreCase))
            {
                await DeletePostingsAsync(previousDocumentNumber);
            }

            await DeletePostingsAsync(invoice.DocNumber);

            var createdPostings = 0;
            foreach (var line in invoice.Lines.OrderBy(item => item.LineNumber))
            {
                var lineAccount = string.IsNullOrWhiteSpace(line.AccountCode)
                    ? GetDefaultLineAccount()
                    : line.AccountCode.Trim();
                lineAccount = NormalizeLineAccountForPosting(counterpartyAccount, lineAccount);
                var lineDescription = BuildLineDescription(invoice, line);
                var taxPosting = ResolveTaxPostingEntry(line, taxPostingEntries);

                if (line.AmountWithoutTax > 0)
                {
                    if (InvoiceDocumentTypes.IsSales(DocumentName))
                        createdPostings += await CreatePostingAsync(invoice, counterpartyAccount, lineAccount, line.AmountWithoutTax, lineDescription) ? 1 : 0;
                    else
                        createdPostings += await CreatePostingAsync(invoice, lineAccount, counterpartyAccount, line.AmountWithoutTax, lineDescription) ? 1 : 0;
                }

                if (line.VatAmount > 0)
                {
                    if (InvoiceDocumentTypes.IsSales(DocumentName))
                        createdPostings += await CreatePostingAsync(invoice, counterpartyAccount, taxPosting.VatPayableAccount, line.VatAmount, $"НДС: {lineDescription}") ? 1 : 0;
                    else
                        createdPostings += await CreatePostingAsync(invoice, taxPosting.VatRecoverableAccount, counterpartyAccount, line.VatAmount, $"НДС: {lineDescription}") ? 1 : 0;
                }

                if (line.SalesTaxAmount > 0)
                {
                    if (InvoiceDocumentTypes.IsSales(DocumentName))
                        createdPostings += await CreatePostingAsync(invoice, counterpartyAccount, taxPosting.SalesTaxAccount, line.SalesTaxAmount, $"Налог с продаж: {lineDescription}") ? 1 : 0;
                    else
                        createdPostings += await CreatePostingAsync(invoice, lineAccount, counterpartyAccount, line.SalesTaxAmount, $"Налог с продаж: {lineDescription}") ? 1 : 0;
                }
            }

            if (createdPostings == 0)
            {
                throw new InvalidOperationException(
                    "Не удалось сформировать проводки по счет-фактуре. Проверьте счета документа и налоговые счета в справочнике налогов.");
            }

            await _context.Database.ExecuteSqlRawAsync(
                $@"UPDATE ""{HeaderTableName}"" SET ""is_posted"" = true, ""UpdatedAt"" = NOW() WHERE ""Id"" = @id;",
                new NpgsqlParameter("@id", invoiceId));

            invoice.Id = invoiceId;
            invoice.IsPosted = true;
        }

        private async Task<bool> CreatePostingAsync(InvoiceDocument invoice, string debit, string credit, decimal amount, string description)
        {
            if (string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit))
                throw new InvalidOperationException("Не удалось сформировать проводку: не указаны счета.");
            if (debit.Equals(credit, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Пропущена проводка счет-фактуры {invoice.DocNumber}: одинаковые счета дебета и кредита {debit}. {description}");
                return false;
            }

            await _context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""doc_postings""
                (""Id"", ""posting_date"", ""doc_number"", ""document_type"",
                 ""module_code"", ""debit_account"", ""credit_account"", ""amount_kgs"", ""amount_currency"",
                 ""description"", ""organization_id"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                (@id, @date, @number, @type, @moduleCode, @debit, @credit, @amount, 0, @description, @org, true, NOW(), NOW())",
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@date", invoice.DocDate),
                new NpgsqlParameter("@number", invoice.DocNumber),
                new NpgsqlParameter("@type", DocumentName),
                new NpgsqlParameter("@moduleCode", (object?)invoice.ModuleCode ?? DBNull.Value),
                new NpgsqlParameter("@debit", debit),
                new NpgsqlParameter("@credit", credit),
                new NpgsqlParameter("@amount", amount),
                new NpgsqlParameter("@description", description),
                new NpgsqlParameter("@org", (object?)invoice.OrganizationId ?? DBNull.Value));
            return true;
        }

        private async Task<string> ResolveInvoiceModuleNameAsync(string? currentModule)
        {
            if (!string.IsNullOrWhiteSpace(currentModule))
                return NormalizeModuleName(currentModule);

            try
            {
                var metadata = await _context.MetadataObjects.AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.ObjectType == "Document" &&
                        item.Name == DocumentName);

                if (metadata != null)
                {
                    var assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(
                        metadata.Id,
                        metadata.ObjectType);
                    if (!string.IsNullOrWhiteSpace(assignedModuleName))
                        return NormalizeModuleName(assignedModuleName);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка определения модуля счет-фактуры: {ex.Message}");
            }

            return InvoiceDocumentTypes.IsSales(DocumentName) || InvoiceDocumentTypes.IsPurchase(DocumentName)
                ? "Финансы"
                : string.Empty;
        }

        private static string NormalizeModuleName(string moduleName)
        {
            var trimmed = moduleName.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "ФИН" or "ФИНАНСЫ" or "FIN" or "FINANCE" => "Финансы",
                "ОС" or "FIXEDASSETS" => "Основные средства",
                "ТМЦ" or "МАТЕРИАЛЫ" or "INVENTORY" => "Учет материальных ценностей",
                _ => trimmed
            };
        }

        private string NormalizeLineAccountForPosting(string counterpartyAccount, string lineAccount)
        {
            if (!lineAccount.Equals(counterpartyAccount, StringComparison.OrdinalIgnoreCase))
                return lineAccount;

            if (InvoiceDocumentTypes.IsSales(DocumentName))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Счет строки счет-фактуры совпал со счетом расчетов ({lineAccount}). Для реализации использован счет доходов {DefaultSalesRevenueAccount}.");
                return DefaultSalesRevenueAccount;
            }

            System.Diagnostics.Debug.WriteLine(
                $"Счет строки счет-фактуры совпал со счетом расчетов ({lineAccount}). Для поступления использован счет товаров {DefaultPurchaseLineAccount}.");
            return DefaultPurchaseLineAccount;
        }

        private string GetDefaultLineAccount()
        {
            return InvoiceDocumentTypes.IsSales(DocumentName)
                ? DefaultSalesRevenueAccount
                : DefaultPurchaseLineAccount;
        }

        private async Task DeletePostingsAsync(string docNumber)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM doc_postings
                WHERE doc_number = @number AND document_type = @type;",
                new NpgsqlParameter("@number", docNumber),
                new NpgsqlParameter("@type", DocumentName));
        }

        private static string BuildLineDescription(InvoiceDocument invoice, InvoiceLineRow line) =>
            string.IsNullOrWhiteSpace(invoice.Basis)
                ? $"Строка {line.LineNumber}: {line.Name}"
                : $"{invoice.Basis}; строка {line.LineNumber}: {line.Name}";

        private async Task<Dictionary<Guid, string>> LoadOrganizationMapAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            if (catalog == null)
                return new Dictionary<Guid, string>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows.Where(row => Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out _))
                .ToDictionary(
                    row => Guid.Parse(row["Id"].ToString()!),
                    row => ReferenceDisplayHelper.BuildDisplayValue(row, new MetadataField()));
        }

        private async Task<Dictionary<string, string>> LoadAccountMapAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));
            if (catalog == null)
                return new Dictionary<string, string>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var code = row.GetValueOrDefault("Код")?.ToString() ?? row.GetValueOrDefault("code")?.ToString();
                var name = row.GetValueOrDefault("Наименование")?.ToString() ?? row.GetValueOrDefault("name")?.ToString();
                if (!string.IsNullOrWhiteSpace(code))
                    result[code] = string.IsNullOrWhiteSpace(name) ? code : $"{code} - {name}";
            }
            return result;
        }

        private async Task<Dictionary<string, TaxPostingCatalogEntry>> LoadTaxPostingEntriesAsync()
        {
            var catalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Налоги");
            if (catalog == null)
                return new Dictionary<string, TaxPostingCatalogEntry>(StringComparer.OrdinalIgnoreCase);

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var result = new Dictionary<string, TaxPostingCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (!IsActiveRow(row))
                    continue;

                var code = GetRowValue(row, "Код", "code");
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                result[code] = new TaxPostingCatalogEntry(
                    code,
                    GetRowValue(row, "Счет НДС к оплате", "vat_payable_account"),
                    GetRowValue(row, "Счет НДС к возмещению", "vat_recoverable_account"),
                    GetRowValue(row, "Счет налога с продаж", "sales_tax_account"));
            }

            return result;
        }

        private static TaxPostingCatalogEntry ResolveTaxPostingEntry(
            InvoiceLineRow line,
            IReadOnlyDictionary<string, TaxPostingCatalogEntry> entries)
        {
            TaxPostingCatalogEntry? vatEntry = null;
            if (!string.IsNullOrWhiteSpace(line.VatTaxCode) &&
                entries.TryGetValue(line.VatTaxCode.Trim(), out var loadedVatEntry))
            {
                vatEntry = loadedVatEntry;
            }

            TaxPostingCatalogEntry? salesTaxEntry = null;
            if (!string.IsNullOrWhiteSpace(line.SalesTaxCode) &&
                entries.TryGetValue(line.SalesTaxCode.Trim(), out var loadedSalesTaxEntry))
            {
                salesTaxEntry = loadedSalesTaxEntry;
            }

            return new TaxPostingCatalogEntry(
                vatEntry?.Code ?? salesTaxEntry?.Code ?? string.Empty,
                NormalizeAccountOrDefault(vatEntry?.VatPayableAccount, DefaultVatPayableAccount),
                NormalizeAccountOrDefault(vatEntry?.VatRecoverableAccount, DefaultVatRecoverableAccount),
                NormalizeAccountOrDefault(salesTaxEntry?.SalesTaxAccount, DefaultSalesTaxAccount));
        }

        private static string ResolveAccount(string? code, IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;
            return map.TryGetValue(code, out var display) ? display : code;
        }

        private static string GetRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static bool IsActiveRow(Dictionary<string, object> row)
        {
            var value = GetRowValue(row, "Активен", "is_active");
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAccountOrDefault(string? value, string fallback)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        private static object DbValueOrNull(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? DBNull.Value : normalized;
        }

        private sealed record TaxPostingCatalogEntry(
            string Code,
            string VatPayableAccount,
            string VatRecoverableAccount,
            string SalesTaxAccount);
    }
}
