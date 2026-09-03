using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Табличная часть документа «Учет движения ОС» (аналог счет-фактур).
    /// Хранит строки в таблице doc_fixed_asset_movement_lines.
    /// </summary>
    public partial class FixedAssetMovementLineService
    {
        public const string LinesTableName = "doc_fixed_asset_movement_lines";

        private readonly AppDbContext _context;

        public FixedAssetMovementLineService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            var sql = $@"
                CREATE TABLE IF NOT EXISTS ""{LinesTableName}"" (
                    ""Id"" uuid PRIMARY KEY,
                    ""movement_id"" uuid NOT NULL,
                    ""line_number"" integer NOT NULL DEFAULT 0,
                    ""asset_id"" uuid NULL,
                    ""is_active"" boolean NOT NULL DEFAULT true,
                    ""account_code"" varchar(50) NULL,
                    ""vat_tax_code"" varchar(50) NULL,
                    ""sales_tax_code"" varchar(50) NULL,
                    ""amount_without_vat"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""vat_rate"" numeric(5,2) NOT NULL DEFAULT 0,
                    ""vat_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""sales_tax_rate"" numeric(5,2) NOT NULL DEFAULT 0,
                    ""sales_tax_amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""line_total"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""amount_currency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS ""IX_{LinesTableName}_movement""
                    ON ""{LinesTableName}"" (""movement_id"");
                CREATE INDEX IF NOT EXISTS ""IX_{LinesTableName}_movement_line""
                    ON ""{LinesTableName}"" (""movement_id"", ""line_number"");";
            await _context.Database.ExecuteSqlRawAsync(sql);
        }

        public async Task<List<FixedAssetMovementLineRow>> GetLinesAsync(Guid movementId)
        {
            await EnsureSchemaAsync();
            var result = new List<FixedAssetMovementLineRow>();

            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = $@"SELECT ""Id"", ""line_number"", ""asset_id"", ""is_active"",
                ""account_code"", ""vat_tax_code"", ""sales_tax_code"",
                ""amount_without_vat"", ""vat_rate"", ""vat_amount"",
                ""sales_tax_rate"", ""sales_tax_amount"", ""line_total"", ""amount_currency""
                FROM ""{LinesTableName}"" WHERE ""movement_id"" = @id ORDER BY ""line_number""";
            command.Parameters.Add(new NpgsqlParameter("id", movementId));

            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new FixedAssetMovementLineRow
                    {
                        Id = reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                        LineNumber = reader.GetInt32(1),
                        AssetId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
                        IsActive = reader.GetBoolean(3),
                        AccountCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        VatTaxCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        SalesTaxCode = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                        AmountWithoutTax = reader.GetDecimal(7),
                        VatRate = reader.GetDecimal(8),
                        VatAmount = reader.GetDecimal(9),
                        SalesTaxRate = reader.GetDecimal(10),
                        SalesTaxAmount = reader.GetDecimal(11),
                        LineTotal = reader.GetDecimal(12),
                        AmountCurrency = reader.GetDecimal(13)
                    });
                }
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }

            return result;
        }

        /// <summary>
        /// Заменяет строки документа: удаляет старые и вставляет новые, перенумеровывая line_number.
        /// </summary>
        public async Task ReplaceLinesAsync(Guid movementId, IReadOnlyList<FixedAssetMovementLineRow> lines)
        {
            await EnsureSchemaAsync();

            await _context.Database.ExecuteSqlRawAsync(
                $@"DELETE FROM ""{LinesTableName}"" WHERE ""movement_id"" = @id;",
                new NpgsqlParameter("id", movementId));

            var lineNumber = 1;
            foreach (var line in lines)
            {
                var id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id;
                await _context.Database.ExecuteSqlRawAsync($@"
                    INSERT INTO ""{LinesTableName}"" (
                        ""Id"", ""movement_id"", ""line_number"", ""asset_id"", ""is_active"",
                        ""account_code"", ""vat_tax_code"", ""sales_tax_code"",
                        ""amount_without_vat"", ""vat_rate"", ""vat_amount"",
                        ""sales_tax_rate"", ""sales_tax_amount"", ""line_total"", ""amount_currency"",
                        ""CreatedAt"", ""UpdatedAt"")
                    VALUES (@id, @movementId, @lineNumber, @assetId, @isActive,
                        @account, @vatTaxCode, @salesTaxCode,
                        @amountWithoutVat, @vatRate, @vatAmount,
                        @salesTaxRate, @salesTaxAmount, @lineTotal, @amountCurrency,
                        NOW(), NOW());",
                    new NpgsqlParameter("id", id),
                    new NpgsqlParameter("movementId", movementId),
                    new NpgsqlParameter("lineNumber", lineNumber++),
                    new NpgsqlParameter("assetId", (object?)line.AssetId ?? DBNull.Value),
                    new NpgsqlParameter("isActive", line.IsActive),
                    new NpgsqlParameter("account", (object?)line.AccountCode ?? DBNull.Value),
                    new NpgsqlParameter("vatTaxCode", (object?)line.VatTaxCode ?? DBNull.Value),
                    new NpgsqlParameter("salesTaxCode", (object?)line.SalesTaxCode ?? DBNull.Value),
                    new NpgsqlParameter("amountWithoutVat", line.AmountWithoutTax),
                    new NpgsqlParameter("vatRate", line.VatRate),
                    new NpgsqlParameter("vatAmount", line.VatAmount),
                    new NpgsqlParameter("salesTaxRate", line.SalesTaxRate),
                    new NpgsqlParameter("salesTaxAmount", line.SalesTaxAmount),
                    new NpgsqlParameter("lineTotal", line.LineTotal),
                    new NpgsqlParameter("amountCurrency", line.AmountCurrency));
            }
        }

        /// <summary>
        /// Считает итоги по строкам документа движения ОС.
        /// </summary>
        public static void RecalculateTotals(IReadOnlyCollection<FixedAssetMovementLineRow> lines,
            out decimal amountWithoutTax, out decimal vatAmount, out decimal salesTaxAmount, out decimal total)
        {
            amountWithoutTax = 0m;
            vatAmount = 0m;
            salesTaxAmount = 0m;
            total = 0m;

            foreach (var line in lines)
            {
                line.VatAmount = decimal.Round(line.AmountWithoutTax * line.VatRate / 100m, 2, MidpointRounding.AwayFromZero);
                line.SalesTaxAmount = decimal.Round(line.AmountWithoutTax * line.SalesTaxRate / 100m, 2, MidpointRounding.AwayFromZero);
                line.LineTotal = decimal.Round(line.AmountWithoutTax + line.VatAmount + line.SalesTaxAmount, 2, MidpointRounding.AwayFromZero);

                amountWithoutTax += line.AmountWithoutTax;
                vatAmount += line.VatAmount;
                salesTaxAmount += line.SalesTaxAmount;
                total += line.LineTotal;
            }
        }
    }
}