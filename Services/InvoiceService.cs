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
        private const string DefaultSalesAccount = "14100000";
        private const string DefaultSalesRevenueAccount = "60100000";
        private const string VatPayableAccount = "34300000";
        private const string VatRecoverableAccount = "24000000";
        private const string SalesTaxAccount = "34900000";

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
            ");
        }

        public async Task<List<InvoiceListRow>> GetInvoicesAsync()
        {
            var organizationMap = await LoadOrganizationMapAsync();
            var sql = $@"
                SELECT ""Id"", ""doc_number"", ""doc_date"", ""organization_id"", ""amount"", ""basis"", ""is_posted""
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
                    Basis = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    IsPosted = reader.GetBoolean(6)
                });
            }
            await _context.Database.CloseConnectionAsync();
            return result;
        }

        public async Task<List<InvoiceLineRow>> GetLinesAsync(Guid invoiceId)
        {
            var accountMap = await LoadAccountMapAsync();
            var sql = $@"
                SELECT ""Id"", ""line_number"", ""name"", ""account_code"",
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
                var accountCode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                result.Add(new InvoiceLineRow
                {
                    Id = reader.GetGuid(0),
                    LineNumber = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    AccountCode = accountCode,
                    AccountName = ResolveAccount(accountCode, accountMap),
                    VatTaxCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    AmountWithoutTax = reader.GetDecimal(5),
                    VatRate = reader.GetDecimal(6),
                    VatAmount = reader.GetDecimal(7),
                    SalesTaxCode = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    SalesTaxRate = reader.GetDecimal(9),
                    SalesTaxAmount = reader.GetDecimal(10),
                    LineTotal = reader.GetDecimal(11)
                });
            }
            await _context.Database.CloseConnectionAsync();
            return result;
        }

        public async Task<InvoiceDocument?> GetInvoiceAsync(Guid invoiceId)
        {
            var organizationMap = await LoadOrganizationMapAsync();
            var sql = $@"
                SELECT ""Id"", ""doc_number"", ""doc_date"", ""esf_number"", ""organization_id"",
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

            var organizationId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
            var invoice = new InvoiceDocument
            {
                Id = reader.GetGuid(0),
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(reader.GetString(1)),
                DocDate = reader.GetDateTime(2),
                EsfNumber = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                OrganizationId = organizationId,
                OrganizationName = organizationId.HasValue && organizationMap.TryGetValue(organizationId.Value, out var name)
                    ? name
                    : string.Empty,
                CounterpartyAccountCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                PaymentKind = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                DeliveryKind = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                SupplyKind = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                Basis = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                AmountWithoutTax = reader.GetDecimal(10),
                VatTotal = reader.GetDecimal(11),
                SalesTaxTotal = reader.GetDecimal(12),
                TotalAmount = reader.GetDecimal(13),
                IsPosted = reader.GetBoolean(14)
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

        public async Task<Guid> SaveInvoiceAsync(InvoiceDocument invoice, Guid? existingId = null)
        {
            RecalculateTotals(invoice);
            var id = existingId ?? Guid.NewGuid();
            var isNew = !existingId.HasValue;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (isNew)
                {
                    await _context.Database.ExecuteSqlRawAsync($@"
                        INSERT INTO ""{HeaderTableName}""
                        (""Id"", ""doc_number"", ""doc_date"", ""esf_number"", ""organization_id"", ""counterparty_account"",
                         ""payment_kind"", ""delivery_kind"", ""supply_kind"", ""basis"",
                         ""amount_without_tax"", ""vat_total"", ""sales_tax_total"", ""amount"", ""is_posted"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (@id, @number, @date, @esf, @org, @account, @payment, @delivery, @supply, @basis,
                                @amountWithoutTax, @vatTotal, @salesTaxTotal, @amount, false, NOW(), NOW())",
                        new NpgsqlParameter("@id", id),
                        new NpgsqlParameter("@number", invoice.DocNumber),
                        new NpgsqlParameter("@date", invoice.DocDate),
                        new NpgsqlParameter("@esf", (object?)invoice.EsfNumber ?? DBNull.Value),
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
                    var existing = await GetInvoiceAsync(id);
                    if (existing?.IsPosted == true)
                        throw new InvalidOperationException("Проведённый документ нельзя изменять.");

                    await _context.Database.ExecuteSqlRawAsync($@"
                        UPDATE ""{HeaderTableName}""
                        SET ""doc_number"" = @number, ""doc_date"" = @date, ""esf_number"" = @esf,
                            ""organization_id"" = @org, ""counterparty_account"" = @account,
                            ""payment_kind"" = @payment, ""delivery_kind"" = @delivery, ""supply_kind"" = @supply,
                            ""basis"" = @basis, ""amount_without_tax"" = @amountWithoutTax,
                            ""vat_total"" = @vatTotal, ""sales_tax_total"" = @salesTaxTotal,
                            ""amount"" = @amount, ""UpdatedAt"" = NOW()
                        WHERE ""Id"" = @id",
                        new NpgsqlParameter("@id", id),
                        new NpgsqlParameter("@number", invoice.DocNumber),
                        new NpgsqlParameter("@date", invoice.DocDate),
                        new NpgsqlParameter("@esf", (object?)invoice.EsfNumber ?? DBNull.Value),
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
                    await _context.Database.ExecuteSqlRawAsync($@"
                        INSERT INTO ""{LinesTableName}""
                        (""Id"", ""invoice_id"", ""line_number"", ""name"", ""account_code"", ""vat_tax_code"",
                         ""amount_without_tax"", ""vat_rate"", ""vat_amount"", ""sales_tax_code"", ""sales_tax_rate"", ""sales_tax_amount"",
                         ""line_total"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (@lineId, @invoiceId, @lineNumber, @name, @account, @vatTaxCode,
                                @amountWithoutTax, @vatRate, @vatAmount, @salesTaxCode, @salesTaxRate, @salesTaxAmount,
                                @lineTotal, NOW(), NOW())",
                        new NpgsqlParameter("@lineId", line.Id == Guid.Empty ? Guid.NewGuid() : line.Id),
                        new NpgsqlParameter("@invoiceId", id),
                        new NpgsqlParameter("@lineNumber", lineNumber++),
                        new NpgsqlParameter("@name", line.Name),
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

                await transaction.CommitAsync();
                return id;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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
        }

        public async Task PostInvoiceAsync(Guid invoiceId)
        {
            var invoice = await GetInvoiceAsync(invoiceId)
                ?? throw new InvalidOperationException("Документ не найден.");
            if (invoice.IsPosted)
                throw new InvalidOperationException("Документ уже проведён.");
            if (invoice.Lines.Count == 0)
                throw new InvalidOperationException("Добавьте хотя бы одну строку счета-фактуры.");
            if (invoice.TotalAmount <= 0)
                throw new InvalidOperationException("Сумма документа должна быть больше нуля.");

            var counterpartyAccount = string.IsNullOrWhiteSpace(invoice.CounterpartyAccountCode)
                ? InvoiceDocumentTypes.IsSales(DocumentName) ? DefaultSalesAccount : DefaultPurchaseAccount
                : invoice.CounterpartyAccountCode.Trim();

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await DeletePostingsAsync(invoice.DocNumber);

                foreach (var line in invoice.Lines.OrderBy(item => item.LineNumber))
                {
                    var lineAccount = string.IsNullOrWhiteSpace(line.AccountCode)
                        ? InvoiceDocumentTypes.IsSales(DocumentName) ? DefaultSalesRevenueAccount : DefaultPurchaseAccount
                        : line.AccountCode.Trim();
                    var lineDescription = BuildLineDescription(invoice, line);

                    if (line.AmountWithoutTax > 0)
                    {
                        if (InvoiceDocumentTypes.IsSales(DocumentName))
                            await CreatePostingAsync(invoice, counterpartyAccount, lineAccount, line.AmountWithoutTax, lineDescription);
                        else
                            await CreatePostingAsync(invoice, lineAccount, counterpartyAccount, line.AmountWithoutTax, lineDescription);
                    }

                    if (line.VatAmount > 0)
                    {
                        if (InvoiceDocumentTypes.IsSales(DocumentName))
                            await CreatePostingAsync(invoice, counterpartyAccount, VatPayableAccount, line.VatAmount, $"НДС: {lineDescription}");
                        else
                            await CreatePostingAsync(invoice, VatRecoverableAccount, counterpartyAccount, line.VatAmount, $"НДС: {lineDescription}");
                    }

                    if (line.SalesTaxAmount > 0)
                    {
                        if (InvoiceDocumentTypes.IsSales(DocumentName))
                            await CreatePostingAsync(invoice, counterpartyAccount, SalesTaxAccount, line.SalesTaxAmount, $"Налог с продаж: {lineDescription}");
                        else
                            await CreatePostingAsync(invoice, lineAccount, counterpartyAccount, line.SalesTaxAmount, $"Налог с продаж: {lineDescription}");
                    }
                }

                await _context.Database.ExecuteSqlRawAsync(
                    $@"UPDATE ""{HeaderTableName}"" SET ""is_posted"" = true, ""UpdatedAt"" = NOW() WHERE ""Id"" = @id;",
                    new NpgsqlParameter("@id", invoiceId));

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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

        private async Task CreatePostingAsync(InvoiceDocument invoice, string debit, string credit, decimal amount, string description)
        {
            if (string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit))
                throw new InvalidOperationException("Не удалось сформировать проводку: не указаны счета.");
            if (debit.Equals(credit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Проводка не может иметь одинаковые счета дебета и кредита.");

            await _context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""doc_postings""
                (""Id"", ""posting_date"", ""doc_number"", ""document_type"",
                 ""debit_account"", ""credit_account"", ""amount_kgs"", ""amount_currency"",
                 ""description"", ""organization_id"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                (@id, @date, @number, @type, @debit, @credit, @amount, 0, @description, @org, true, NOW(), NOW())",
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@date", invoice.DocDate),
                new NpgsqlParameter("@number", invoice.DocNumber),
                new NpgsqlParameter("@type", DocumentName),
                new NpgsqlParameter("@debit", debit),
                new NpgsqlParameter("@credit", credit),
                new NpgsqlParameter("@amount", amount),
                new NpgsqlParameter("@description", description),
                new NpgsqlParameter("@org", (object?)invoice.OrganizationId ?? DBNull.Value));
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

        private static string ResolveAccount(string? code, IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;
            return map.TryGetValue(code, out var display) ? display : code;
        }
    }
}
