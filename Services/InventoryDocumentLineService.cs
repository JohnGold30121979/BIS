using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    /// <summary>Строка документа ТМЗ (таблица &lt;шапка&gt;_lines).</summary>
    public sealed class InventoryLineRow : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private int _lineNumber;
        private Guid? _materialId;
        private string _materialName = string.Empty;
        private string _materialCode = string.Empty;
        private string _materialText = string.Empty;
        private string _unitName = string.Empty;
        private string _accountCode = string.Empty;
        private string _accountText = string.Empty;
        private Guid? _siteId;
        private Guid? _responsiblePersonId;
        private decimal _quantity;
        private decimal _price;
        private decimal _amountWithoutTax;
        private decimal _vatRate;
        private decimal _vatAmount;
        private decimal _salesTaxRate;
        private decimal _salesTaxAmount;
        private decimal _lineTotal;
        private decimal _amountCurrency;
        private string _description = string.Empty;

        public Guid Id { get => _id; set => Set(ref _id, value); }
        public int LineNumber { get => _lineNumber; set => Set(ref _lineNumber, value); }
        public Guid? MaterialId { get => _materialId; set => Set(ref _materialId, value); }
        public string MaterialName { get => _materialName; set => Set(ref _materialName, value); }
        public string MaterialCode { get => _materialCode; set => Set(ref _materialCode, value); }

        /// <summary>
        /// Текст ячейки «Материал» («Код - Наименование»): вводится вручную
        /// или заполняется при выборе из справочника материалов.
        /// </summary>
        public string MaterialText { get => _materialText; set => Set(ref _materialText, value); }
        public string UnitName { get => _unitName; set => Set(ref _unitName, value); }
        public string AccountCode { get => _accountCode; set => Set(ref _accountCode, value); }

        /// <summary>Текст ячейки «Дебет»: «Код - Наименование» счёта плана счетов.</summary>
        public string AccountText { get => _accountText; set => Set(ref _accountText, value); }
        public Guid? SiteId { get => _siteId; set => Set(ref _siteId, value); }
        public Guid? ResponsiblePersonId { get => _responsiblePersonId; set => Set(ref _responsiblePersonId, value); }
        public decimal Quantity { get => _quantity; set => Set(ref _quantity, value); }
        public decimal Price { get => _price; set => Set(ref _price, value); }
        public decimal AmountWithoutTax { get => _amountWithoutTax; set => Set(ref _amountWithoutTax, value); }
        public decimal VatRate { get => _vatRate; set => Set(ref _vatRate, value); }
        public decimal VatAmount { get => _vatAmount; set => Set(ref _vatAmount, value); }
        public decimal SalesTaxRate { get => _salesTaxRate; set => Set(ref _salesTaxRate, value); }
        public decimal SalesTaxAmount { get => _salesTaxAmount; set => Set(ref _salesTaxAmount, value); }
        public decimal LineTotal { get => _lineTotal; set => Set(ref _lineTotal, value); }
        public decimal AmountCurrency { get => _amountCurrency; set => Set(ref _amountCurrency, value); }
        public string Description { get => _description; set => Set(ref _description, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Пересчитывает суммы строки от количества, цены и ставок.</summary>
        public void Recalculate()
        {
            AmountWithoutTax = decimal.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);
            VatAmount = decimal.Round(AmountWithoutTax * VatRate / 100m, 2, MidpointRounding.AwayFromZero);
            SalesTaxAmount = decimal.Round(AmountWithoutTax * SalesTaxRate / 100m, 2, MidpointRounding.AwayFromZero);
            LineTotal = decimal.Round(AmountWithoutTax + VatAmount + SalesTaxAmount, 2, MidpointRounding.AwayFromZero);
        }

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Чтение и запись строк документов ТМЗ.</summary>
    public sealed class InventoryDocumentLineService
    {
        private readonly AppDbContext _context;

        public InventoryDocumentLineService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<InventoryLineRow>> GetLinesAsync(string linesTable, string parentColumn, Guid headerId)
        {
            var result = new List<InventoryLineRow>();
            await using var connection = await OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT ""Id"", ""line_number"", ""material_id"", ""material_name"", ""unit_name"", ""account_code"",
                       ""site_id"", ""responsible_person_id"", ""quantity"", ""price"", ""amount_without_tax"",
                       ""vat_rate"", ""vat_amount"", ""sales_tax_rate"", ""sales_tax_amount"", ""line_total"",
                       ""amount_currency"", ""description""
                FROM ""{linesTable}""
                WHERE ""{parentColumn}"" = @header
                ORDER BY ""line_number""";
            command.Parameters.Add(new NpgsqlParameter("header", headerId));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new InventoryLineRow
                {
                    Id = reader.GetGuid(0),
                    LineNumber = reader.GetInt32(1),
                    MaterialId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    MaterialName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    UnitName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    AccountCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    SiteId = reader.IsDBNull(6) ? null : reader.GetGuid(6),
                    ResponsiblePersonId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
                    Quantity = reader.GetDecimal(8),
                    Price = reader.GetDecimal(9),
                    AmountWithoutTax = reader.GetDecimal(10),
                    VatRate = reader.GetDecimal(11),
                    VatAmount = reader.GetDecimal(12),
                    SalesTaxRate = reader.GetDecimal(13),
                    SalesTaxAmount = reader.GetDecimal(14),
                    LineTotal = reader.GetDecimal(15),
                    AmountCurrency = reader.GetDecimal(16),
                    Description = reader.IsDBNull(17) ? string.Empty : reader.GetString(17)
                });
            }

            return result;
        }

        public async Task ReplaceLinesAsync(
            string linesTable,
            string parentColumn,
            Guid headerId,
            IReadOnlyList<InventoryLineRow> lines)
        {
            await using var connection = await OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = $@"DELETE FROM ""{linesTable}"" WHERE ""{parentColumn}"" = @header";
                delete.Parameters.Add(new NpgsqlParameter("header", headerId));
                await delete.ExecuteNonQueryAsync();
            }

            var number = 1;
            foreach (var line in lines)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $@"
                    INSERT INTO ""{linesTable}""
                        (""Id"", ""{parentColumn}"", ""line_number"", ""material_id"", ""material_name"", ""unit_name"",
                         ""account_code"", ""site_id"", ""responsible_person_id"", ""quantity"", ""price"",
                         ""amount_without_tax"", ""vat_rate"", ""vat_amount"", ""sales_tax_rate"", ""sales_tax_amount"",
                         ""line_total"", ""amount_currency"", ""description"", ""CreatedAt"", ""UpdatedAt"")
                    VALUES
                        (@id, @header, @line_number, @material_id, @material_name, @unit_name,
                         @account_code, @site_id, @responsible_person_id, @quantity, @price,
                         @amount_without_tax, @vat_rate, @vat_amount, @sales_tax_rate, @sales_tax_amount,
                         @line_total, @amount_currency, @description, NOW(), NOW())";

                line.Recalculate();
                line.LineNumber = number++;

                insert.Parameters.Add(new NpgsqlParameter("id", line.Id == Guid.Empty ? Guid.NewGuid() : line.Id));
                insert.Parameters.Add(new NpgsqlParameter("header", headerId));
                insert.Parameters.Add(new NpgsqlParameter("line_number", line.LineNumber));
                insert.Parameters.Add(new NpgsqlParameter("material_id", (object?)line.MaterialId ?? DBNull.Value));
                insert.Parameters.Add(new NpgsqlParameter("material_name", line.MaterialName ?? string.Empty));
                insert.Parameters.Add(new NpgsqlParameter("unit_name", line.UnitName ?? string.Empty));
                insert.Parameters.Add(new NpgsqlParameter("account_code", line.AccountCode ?? string.Empty));
                insert.Parameters.Add(new NpgsqlParameter("site_id", (object?)line.SiteId ?? DBNull.Value));
                insert.Parameters.Add(new NpgsqlParameter("responsible_person_id", (object?)line.ResponsiblePersonId ?? DBNull.Value));
                insert.Parameters.Add(new NpgsqlParameter("quantity", line.Quantity));
                insert.Parameters.Add(new NpgsqlParameter("price", line.Price));
                insert.Parameters.Add(new NpgsqlParameter("amount_without_tax", line.AmountWithoutTax));
                insert.Parameters.Add(new NpgsqlParameter("vat_rate", line.VatRate));
                insert.Parameters.Add(new NpgsqlParameter("vat_amount", line.VatAmount));
                insert.Parameters.Add(new NpgsqlParameter("sales_tax_rate", line.SalesTaxRate));
                insert.Parameters.Add(new NpgsqlParameter("sales_tax_amount", line.SalesTaxAmount));
                insert.Parameters.Add(new NpgsqlParameter("line_total", line.LineTotal));
                insert.Parameters.Add(new NpgsqlParameter("amount_currency", line.AmountCurrency));
                insert.Parameters.Add(new NpgsqlParameter("description", line.Description ?? string.Empty));

                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public static void RecalculateTotals(
            IReadOnlyList<InventoryLineRow> lines,
            out decimal amountWithoutTax,
            out decimal vatAmount,
            out decimal salesTaxAmount,
            out decimal total)
        {
            amountWithoutTax = 0m;
            vatAmount = 0m;
            salesTaxAmount = 0m;
            total = 0m;

            foreach (var line in lines)
            {
                line.Recalculate();
                amountWithoutTax += line.AmountWithoutTax;
                vatAmount += line.VatAmount;
                salesTaxAmount += line.SalesTaxAmount;
                total += line.LineTotal;
            }
        }

        private async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            var connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось получить строку подключения к инфобазе.");

            var connection = new NpgsqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }
}
