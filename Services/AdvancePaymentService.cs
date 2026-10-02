using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Классическая (нормализованная) модель авансовых платежей — по образцу
    /// счет-фактур (InvoiceService). Шапка документа хранится в таблице документа
    /// (по умолчанию doc_advance_payment), строки затрат — в отдельной таблице
    /// doc_advance_payment_lines со ссылкой на шапку через advance_id
    /// (ON DELETE CASCADE). JSON-поле expense_lines больше не используется.
    /// </summary>
    public class AdvancePaymentService
    {
        /// <summary>Имя таблицы шапки по умолчанию (метаданные задают его в MetadataObject.TableName).</summary>
        public const string DefaultHeaderTableName = "doc_advance_payment";

        private readonly AppDbContext _context;

        public AdvancePaymentService(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>Имя таблицы шапки документа.</summary>
        public string HeaderTableName { get; private set; } = DefaultHeaderTableName;

        /// <summary>Имя таблицы строк затрат (шапка + суффикс "_lines", как у счет-фактур).</summary>
        public string LinesTableName => HeaderTableName + "_lines";

        public void Configure(MetadataObject document)
            => Configure(document?.TableName);

        public void Configure(string? headerTableName)
        {
            HeaderTableName = string.IsNullOrWhiteSpace(headerTableName)
                ? DefaultHeaderTableName
                : headerTableName.Trim();
        }

        /// <summary>
        /// Гарантирует наличие таблицы строк затрат и индекса по advance_id.
        /// Шапка документа создаётся метаданными (CreateDynamicTableAsync) —
        /// здесь она лишь дополняется, если таблицы ещё нет (безопасный no-op).
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            var header = QuoteIdentifier(HeaderTableName);
            var lines = QuoteIdentifier(LinesTableName);

            await _context.Database.ExecuteSqlRawAsync($@"
                CREATE TABLE IF NOT EXISTS {lines} (
                    ""Id"" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""advance_id"" uuid NOT NULL,
                    ""line_number"" integer NOT NULL,
                    ""line_date"" timestamp,
                    ""pair_id"" uuid,
                    ""pair_code"" varchar(50),
                    ""pair_name"" varchar(250),
                    ""debit_account"" varchar(50),
                    ""credit_account"" varchar(50),
                    ""expense_account"" varchar(50),
                    ""expense_account_name"" varchar(250),
                    ""currency_id"" uuid,
                    ""currency_name"" varchar(100),
                    ""amount_currency"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""exchange_rate"" numeric(18,4) NOT NULL DEFAULT 0,
                    ""amount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""description"" varchar(500),
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );");

            // Внешний ключ на шапку добавляем только когда шапка уже существует.
            await _context.Database.ExecuteSqlRawAsync($@"
                DO $$
                BEGIN
                    IF to_regclass('public.{HeaderTableName}') IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1 FROM pg_constraint WHERE conname = 'FK_{LinesTableName}_advance')
                    THEN
                        ALTER TABLE {lines}
                            ADD CONSTRAINT ""FK_{LinesTableName}_advance""
                            FOREIGN KEY (""advance_id"") REFERENCES {header}(""Id"") ON DELETE CASCADE;
                    END IF;
                END $$;");

            await _context.Database.ExecuteSqlRawAsync(
                $@"CREATE INDEX IF NOT EXISTS ""IX_{LinesTableName}_advance"" ON {lines} (""advance_id"");");
        }
/// <summary>Строки затрат документа, упорядоченные по line_number.</summary>
        public async Task<List<AdvancePaymentLine>> GetLinesAsync(Guid documentId)
        {
            var result = new List<AdvancePaymentLine>();
            if (documentId == Guid.Empty)
                return result;

            var sql = $@"
                SELECT ""Id"", ""line_number"", ""line_date"", ""pair_id"", ""pair_code"", ""pair_name"",
                       ""debit_account"", ""credit_account"", ""expense_account"", ""expense_account_name"",
                       ""currency_id"", ""currency_name"", ""amount_currency"", ""exchange_rate"",
                       ""amount"", ""description""
                FROM {QuoteIdentifier(LinesTableName)}
                WHERE ""advance_id"" = @advanceId
                ORDER BY ""line_number"";";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@advanceId", documentId));
            await _context.Database.OpenConnectionAsync();
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(ReadLine(reader));
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }
/// <summary>
        /// Строки затрат сразу для набора документов (одним запросом) —
        /// используется в списке/журнале документов.
        /// </summary>
        public async Task<Dictionary<Guid, List<AdvancePaymentLine>>> GetLinesForDocumentsAsync(
            IReadOnlyCollection<Guid> documentIds)
        {
            var result = new Dictionary<Guid, List<AdvancePaymentLine>>();
            var ids = documentIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0)
                return result;

            var sql = $@"
                SELECT ""advance_id"", ""Id"", ""line_number"", ""line_date"", ""pair_id"", ""pair_code"", ""pair_name"",
                       ""debit_account"", ""credit_account"", ""expense_account"", ""expense_account_name"",
                       ""currency_id"", ""currency_name"", ""amount_currency"", ""exchange_rate"",
                       ""amount"", ""description""
                FROM {QuoteIdentifier(LinesTableName)}
                WHERE ""advance_id"" = ANY(@advanceIds)
                ORDER BY ""advance_id"", ""line_number"";";

            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new NpgsqlParameter("@advanceIds", ids));
            await _context.Database.OpenConnectionAsync();
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var advanceId = reader.GetGuid(0);
                    if (!result.TryGetValue(advanceId, out var list))
                    {
                        list = new List<AdvancePaymentLine>();
                        result[advanceId] = list;
                    }

                    list.Add(ReadLine(reader, offset: 1));
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }

        private static AdvancePaymentLine ReadLine(System.Data.Common.DbDataReader reader, int offset = 0)
        {
            return new AdvancePaymentLine
            {
                Id = reader.GetGuid(offset + 0),
                LineNumber = reader.GetInt32(offset + 1),
                LineDate = reader.IsDBNull(offset + 2) ? null : reader.GetDateTime(offset + 2),
                PairId = reader.IsDBNull(offset + 3) ? Guid.Empty : reader.GetGuid(offset + 3),
                PairCode = reader.IsDBNull(offset + 4) ? string.Empty : reader.GetString(offset + 4),
                PairName = reader.IsDBNull(offset + 5) ? string.Empty : reader.GetString(offset + 5),
                DebitAccount = reader.IsDBNull(offset + 6) ? string.Empty : reader.GetString(offset + 6),
                CreditAccount = reader.IsDBNull(offset + 7) ? string.Empty : reader.GetString(offset + 7),
                ExpenseAccount = reader.IsDBNull(offset + 8) ? string.Empty : reader.GetString(offset + 8),
                ExpenseAccountName = reader.IsDBNull(offset + 9) ? string.Empty : reader.GetString(offset + 9),
                CurrencyId = reader.IsDBNull(offset + 10) ? Guid.Empty : reader.GetGuid(offset + 10),
                CurrencyName = reader.IsDBNull(offset + 11) ? string.Empty : reader.GetString(offset + 11),
                AmountCurrency = reader.IsDBNull(offset + 12) ? 0m : reader.GetDecimal(offset + 12),
                ExchangeRate = reader.IsDBNull(offset + 13) ? 0m : reader.GetDecimal(offset + 13),
                Amount = reader.IsDBNull(offset + 14) ? 0m : reader.GetDecimal(offset + 14),
                Description = reader.IsDBNull(offset + 15) ? string.Empty : reader.GetString(offset + 15)
            };
        }
/// <summary>
        /// Сохраняет строки затрат документа: полностью заменяет прежний набор
        /// (delete + insert) в одной транзакции — как в InvoiceService.
        /// </summary>
        public async Task SaveLinesAsync(Guid documentId, IReadOnlyList<AdvancePaymentLine> lines)
        {
            if (documentId == Guid.Empty)
                throw new ArgumentException("Не указан документ для строк затрат.", nameof(documentId));

            var linesTable = QuoteIdentifier(LinesTableName);

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    $@"DELETE FROM {linesTable} WHERE ""advance_id"" = @advanceId;",
                    new NpgsqlParameter("@advanceId", documentId));

                var lineNumber = 1;
                foreach (var line in lines ?? Array.Empty<AdvancePaymentLine>())
                {
                    await _context.Database.ExecuteSqlRawAsync($@"
                        INSERT INTO {linesTable}
                        (""Id"", ""advance_id"", ""line_number"", ""line_date"", ""pair_id"", ""pair_code"", ""pair_name"",
                         ""debit_account"", ""credit_account"", ""expense_account"", ""expense_account_name"",
                         ""currency_id"", ""currency_name"", ""amount_currency"", ""exchange_rate"", ""amount"",
                         ""description"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (@id, @advanceId, @lineNumber, @lineDate, @pairId, @pairCode, @pairName,
                                @debitAccount, @creditAccount, @expenseAccount, @expenseAccountName,
                                @currencyId, @currencyName, @amountCurrency, @exchangeRate, @amount,
                                @description, NOW(), NOW())",
                        new NpgsqlParameter("@id", line.Id == Guid.Empty ? Guid.NewGuid() : line.Id),
                        new NpgsqlParameter("@advanceId", documentId),
                        new NpgsqlParameter("@lineNumber", lineNumber++),
                        new NpgsqlParameter("@lineDate", (object?)line.LineDate ?? DBNull.Value),
                        new NpgsqlParameter("@pairId", (object?)(line.PairId == Guid.Empty ? null : line.PairId) ?? DBNull.Value),
                        new NpgsqlParameter("@pairCode", Nullable(line.PairCode)),
                        new NpgsqlParameter("@pairName", Nullable(line.PairName)),
                        new NpgsqlParameter("@debitAccount", Nullable(line.DebitAccount)),
                        new NpgsqlParameter("@creditAccount", Nullable(line.CreditAccount)),
                        new NpgsqlParameter("@expenseAccount", Nullable(line.ExpenseAccount)),
                        new NpgsqlParameter("@expenseAccountName", Nullable(line.ExpenseAccountName)),
                        new NpgsqlParameter("@currencyId", (object?)(line.CurrencyId == Guid.Empty ? null : line.CurrencyId) ?? DBNull.Value),
                        new NpgsqlParameter("@currencyName", Nullable(line.CurrencyName)),
                        new NpgsqlParameter("@amountCurrency", line.AmountCurrency),
                        new NpgsqlParameter("@exchangeRate", line.ExchangeRate),
                        new NpgsqlParameter("@amount", line.Amount),
                        new NpgsqlParameter("@description", Nullable(line.Description)));
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static object Nullable(string? value)
            => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
/// <summary>
        /// Миграция старых данных: переносит JSON-поле expense_lines в таблицу строк
        /// (классическая модель) и удаляет колонку. Идемпотентна.
        /// </summary>
        public async Task MigrateLegacyExpenseLinesAsync()
        {
            if (!await ColumnExistsAsync(HeaderTableName, "expense_lines"))
                return;

            var header = QuoteIdentifier(HeaderTableName);
            var legacyRows = new List<(Guid Id, string Json)>();
            await using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = $@"
                    SELECT ""Id"", ""expense_lines""
                    FROM {header}
                    WHERE ""expense_lines"" IS NOT NULL AND ""expense_lines"" <> '';";
                await _context.Database.OpenConnectionAsync();
                try
                {
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var json = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        if (!string.IsNullOrWhiteSpace(json))
                            legacyRows.Add((reader.GetGuid(0), json));
                    }
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }

            foreach (var (id, json) in legacyRows)
            {
                // Уже перенесённые документы не трогаем — миграция идемпотентна.
                if ((await GetLinesAsync(id)).Count > 0)
                    continue;

                List<LegacyAdvanceExpenseLine>? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<List<LegacyAdvanceExpenseLine>>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    continue;
                }

                if (payload == null || payload.Count == 0)
                    continue;

                await SaveLinesAsync(id, payload.Select(ToLine).ToList());
            }

            await _context.Database.ExecuteSqlRawAsync(
                $@"ALTER TABLE {header} DROP COLUMN IF EXISTS ""expense_lines"";");
        }

        private static AdvancePaymentLine ToLine(LegacyAdvanceExpenseLine source) => new()
        {
            Id = Guid.NewGuid(),
            LineDate = source.LineDate,
            PairId = source.PairId,
            PairCode = source.PairCode ?? string.Empty,
            PairName = source.PairName ?? string.Empty,
            DebitAccount = source.DebitAccount ?? string.Empty,
            CreditAccount = source.CreditAccount ?? string.Empty,
            ExpenseAccount = source.ExpenseAccount ?? string.Empty,
            ExpenseAccountName = source.ExpenseAccountName ?? string.Empty,
            CurrencyId = source.CurrencyId,
            CurrencyName = source.CurrencyName ?? string.Empty,
            AmountCurrency = source.AmountCurrency,
            ExchangeRate = source.ExchangeRate,
            Amount = source.Amount,
            Description = source.Description ?? string.Empty
        };

        private async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @tableName AND column_name = @columnName
                LIMIT 1;";
            command.Parameters.Add(new NpgsqlParameter("@tableName", tableName));
            command.Parameters.Add(new NpgsqlParameter("@columnName", columnName));
            await _context.Database.OpenConnectionAsync();
            try
            {
                var value = await command.ExecuteScalarAsync();
                return value != null && value != DBNull.Value;
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
private static string QuoteIdentifier(string identifier)
        {
            var escaped = identifier.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        /// <summary>Форма прежнего JSON-поля expense_lines (для миграции старых записей).</summary>
        private sealed class LegacyAdvanceExpenseLine
        {
            public DateTime? LineDate { get; set; }
            public Guid PairId { get; set; }
            public string? PairCode { get; set; }
            public string? PairName { get; set; }
            public string? DebitAccount { get; set; }
            public string? CreditAccount { get; set; }
            public string? ExpenseAccount { get; set; }
            public string? ExpenseAccountName { get; set; }
            public Guid CurrencyId { get; set; }
            public string? CurrencyName { get; set; }
            public decimal AmountCurrency { get; set; }
            public decimal ExchangeRate { get; set; }
            public decimal Amount { get; set; }
            public string? Description { get; set; }
        }
    }
}