using System.Data;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BIS.ERP.Services
{
    public sealed class CashDayInfo
    {
        public Guid Id { get; init; }
        public Guid CashDeskId { get; init; }
        public string CashDeskName { get; init; } = string.Empty;
        public DateTime CloseDate { get; init; }
        public bool IsClosed { get; init; }
    }

    public sealed class CashDayClosureService
    {
        private readonly AppDbContext _context;

        public CashDayClosureService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            try
            {
                if (!await RelationExistsAsync("public.\"CashDayClosures\"") &&
                    await RelationExistsAsync("public.cashdayclosures"))
                {
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE cashdayclosures RENAME TO \"CashDayClosures\"");
                }

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "CashDayClosures" (
                        "Id" uuid NOT NULL,
                        "CashDeskId" uuid NOT NULL,
                        "CashDeskName" character varying(300) NOT NULL DEFAULT '',
                        "CloseDate" date NOT NULL,
                        "IsClosed" boolean NOT NULL DEFAULT true,
                        "ClosedBy" character varying(120) NOT NULL DEFAULT '',
                        "OpenedBy" character varying(120) NOT NULL DEFAULT '',
                        "ClosedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                        "OpenedAt" timestamp with time zone NULL,
                        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                        "OpeningDebit" numeric(18,2) NOT NULL DEFAULT 0,
                        "OpeningCredit" numeric(18,2) NOT NULL DEFAULT 0,
                        "DebitTurnover" numeric(18,2) NOT NULL DEFAULT 0,
                        "CreditTurnover" numeric(18,2) NOT NULL DEFAULT 0,
                        "ClosingDebit" numeric(18,2) NOT NULL DEFAULT 0,
                        "ClosingCredit" numeric(18,2) NOT NULL DEFAULT 0,
                        "Description" text NOT NULL DEFAULT 'Закрытие/открытие кассовых дней по кассам',
                        CONSTRAINT "PK_CashDayClosures" PRIMARY KEY ("Id")
                    )
                    """);

                await RenameLegacyColumnsAsync();
                await RenameLegacyIndexAsync();

                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "Description" text NOT NULL DEFAULT 'Закрытие/открытие кассовых дней по кассам'""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "OpeningDebit" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "OpeningCredit" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "DebitTurnover" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "CreditTurnover" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "ClosingDebit" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "ClosingCredit" numeric(18,2) NOT NULL DEFAULT 0""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "CashDeskName" TYPE character varying(300)""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "ClosedBy" TYPE character varying(120)""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "OpenedBy" TYPE character varying(120)""");

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashDayClosures_CashDesk_CloseDate"
                        ON "CashDayClosures" ("CashDeskId", "CloseDate")
                    """);

                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON TABLE "CashDayClosures" IS 'Служебная таблица: закрытие и открытие кассовых дней по кассам, включая начальные и конечные остатки'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."OpeningDebit" IS 'Дебетовый остаток на начало кассового дня (ДН)'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."OpeningCredit" IS 'Кредитовый остаток на начало кассового дня (КН)'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."DebitTurnover" IS 'Дебетовый оборот за кассовый день'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."CreditTurnover" IS 'Кредитовый оборот за кассовый день'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."ClosingDebit" IS 'Дебетовый остаток на конце кассового дня (ДК)'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."ClosingCredit" IS 'Кредитовый остаток на конце кассового дня (КК)'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."Description" IS 'Описание операции закрытия/открытия кассового дня'""");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка подключения к служебной таблице CashDayClosures.", "CashDayClosureService.EnsureSchemaAsync", ex);
                throw;
            }
        }

        private async Task RenameLegacyColumnsAsync()
        {
            var legacyColumns = new (string OldName, string NewName)[]
            {
                ("id", "Id"),
                ("cash_desk_id", "CashDeskId"),
                ("cash_desk_name", "CashDeskName"),
                ("close_date", "CloseDate"),
                ("is_closed", "IsClosed"),
                ("closed_by", "ClosedBy"),
                ("opened_by", "OpenedBy"),
                ("closed_at", "ClosedAt"),
                ("opened_at", "OpenedAt"),
                ("updated_at", "UpdatedAt")
            };

            foreach (var (oldName, newName) in legacyColumns)
            {
                if (!await ColumnExistsAsync(oldName) || await ColumnExistsAsync(newName))
                    continue;

                await _context.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE "CashDayClosures" RENAME COLUMN {QuoteIdentifier(oldName)} TO {QuoteIdentifier(newName)}""");
            }
        }

        private async Task RenameLegacyIndexAsync()
        {
            if (!await RelationExistsAsync("public.ix_cashdayclosures_cashdesk_closedate") ||
                await RelationExistsAsync("public.\"IX_CashDayClosures_CashDesk_CloseDate\""))
            {
                return;
            }

            await _context.Database.ExecuteSqlRawAsync("ALTER INDEX ix_cashdayclosures_cashdesk_closedate RENAME TO \"IX_CashDayClosures_CashDesk_CloseDate\"");
        }

        private Task<bool> ColumnExistsAsync(string columnName)
        {
            return ExecuteScalarBoolAsync("""
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'CashDayClosures'
                      AND column_name = @columnName
                )
                """, new NpgsqlParameter("@columnName", columnName));
        }

        private Task<bool> RelationExistsAsync(string regclassName)
        {
            return ExecuteScalarBoolAsync(
                "SELECT to_regclass(@regclassName) IS NOT NULL",
                new NpgsqlParameter("@regclassName", regclassName));
        }

        private async Task<bool> ExecuteScalarBoolAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var parameter in parameters)
                    command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                return result is bool value && value;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static string QuoteIdentifier(string identifier)
        {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        private static NpgsqlParameter DateParameter(string name, DateTime date) =>
            new(name, NpgsqlDbType.Date) { Value = date.Date };

        private async Task<List<DateTime>> ExecuteDateListAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var parameter in parameters)
                    command.Parameters.Add(parameter);

                var dates = new List<DateTime>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    dates.Add(reader.GetDateTime(0).Date);

                return dates;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private async Task<DateTime?> ExecuteNullableDateAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var parameter in parameters)
                    command.Parameters.Add(parameter);

                var result = await command.ExecuteScalarAsync();
                return result == null || result == DBNull.Value
                    ? null
                    : ((DateTime)result).Date;
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private async Task<CashDayInfo?> ExecuteCashDayInfoAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var parameter in parameters)
                    command.Parameters.Add(parameter);

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return null;

                return new CashDayInfo
                {
                    Id = reader.GetGuid(0),
                    CashDeskId = reader.GetGuid(1),
                    CashDeskName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CloseDate = reader.GetDateTime(3).Date,
                    IsClosed = reader.GetBoolean(4)
                };
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }
        public async Task EnsureExistsAsync()
        {
            await EnsureSchemaAsync();
            if (!await TableExistsAsync())
                throw new InvalidOperationException("Не удалось создать служебную таблицу CashDayClosures.");
        }

        public async Task<bool> TableExistsAsync()
        {
            return await RelationExistsAsync("public.\"CashDayClosures\"");
        }

        public async Task CloseDayAsync(
            Guid cashDeskId,
            string cashDeskName,
            DateTime closeDate,
            string closedBy,
            decimal openingDebit = 0m,
            decimal openingCredit = 0m,
            decimal debitTurnover = 0m,
            decimal creditTurnover = 0m,
            decimal closingDebit = 0m,
            decimal closingCredit = 0m)
        {
            await EnsureExistsAsync();
            var description = $"Закрытие кассового дня {closeDate:dd.MM.yyyy}";

            try
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "CashDayClosures"
                        ("Id", "CashDeskId", "CashDeskName", "CloseDate", "IsClosed", "ClosedBy", "ClosedAt", "UpdatedAt",
                         "OpeningDebit", "OpeningCredit", "DebitTurnover", "CreditTurnover", "ClosingDebit", "ClosingCredit", "Description")
                    VALUES
                        (@id, @cashDeskId, @cashDeskName, @closeDate, true, @closedBy, NOW(), NOW(),
                         @openingDebit, @openingCredit, @debitTurnover, @creditTurnover, @closingDebit, @closingCredit, @description)
                    ON CONFLICT ("CashDeskId", "CloseDate")
                    DO UPDATE SET
                        "CashDeskName" = EXCLUDED."CashDeskName",
                        "IsClosed" = true,
                        "ClosedBy" = EXCLUDED."ClosedBy",
                        "ClosedAt" = NOW(),
                        "OpenedBy" = '',
                        "OpenedAt" = NULL,
                        "UpdatedAt" = NOW(),
                        "OpeningDebit" = EXCLUDED."OpeningDebit",
                        "OpeningCredit" = EXCLUDED."OpeningCredit",
                        "DebitTurnover" = EXCLUDED."DebitTurnover",
                        "CreditTurnover" = EXCLUDED."CreditTurnover",
                        "ClosingDebit" = EXCLUDED."ClosingDebit",
                        "ClosingCredit" = EXCLUDED."ClosingCredit",
                        "Description" = EXCLUDED."Description"
                    """,
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@cashDeskId", cashDeskId),
                    new NpgsqlParameter("@cashDeskName", cashDeskName ?? string.Empty),
                    DateParameter("@closeDate", closeDate),
                    new NpgsqlParameter("@closedBy", closedBy ?? string.Empty),
                    new NpgsqlParameter("@openingDebit", openingDebit),
                    new NpgsqlParameter("@openingCredit", openingCredit),
                    new NpgsqlParameter("@debitTurnover", debitTurnover),
                    new NpgsqlParameter("@creditTurnover", creditTurnover),
                    new NpgsqlParameter("@closingDebit", closingDebit),
                    new NpgsqlParameter("@closingCredit", closingCredit),
                    new NpgsqlParameter("@description", description));
            }
            catch (Exception ex)
            {
                SystemLogService.Error(
                    $"Ошибка закрытия кассового дня. CashDeskId={cashDeskId}; Date={closeDate:yyyy-MM-dd}",
                    "CashDayClosureService.CloseDayAsync",
                    ex);
                throw;
            }
        }

        public async Task CloseOpenDayAsync(
            CashDayInfo openDay,
            string cashDeskName,
            DateTime closeDate,
            string closedBy,
            decimal openingDebit = 0m,
            decimal openingCredit = 0m,
            decimal debitTurnover = 0m,
            decimal creditTurnover = 0m,
            decimal closingDebit = 0m,
            decimal closingCredit = 0m)
        {
            await EnsureExistsAsync();
            var description = $"Закрытие кассового периода {closeDate:dd.MM.yyyy}";

            if (await IsDayClosedAsync(openDay.CashDeskId, closeDate))
                throw new InvalidOperationException($"Кассовый день {closeDate:dd.MM.yyyy} уже закрыт.");

            try
            {
                var affected = await _context.Database.ExecuteSqlRawAsync("""
                    UPDATE "CashDayClosures"
                    SET "CashDeskName" = @cashDeskName,
                        "CloseDate" = @closeDate,
                        "IsClosed" = true,
                        "ClosedBy" = @closedBy,
                        "ClosedAt" = NOW(),
                        "UpdatedAt" = NOW(),
                        "OpeningDebit" = @openingDebit,
                        "OpeningCredit" = @openingCredit,
                        "DebitTurnover" = @debitTurnover,
                        "CreditTurnover" = @creditTurnover,
                        "ClosingDebit" = @closingDebit,
                        "ClosingCredit" = @closingCredit,
                        "Description" = @description
                    WHERE "Id" = @id
                      AND "IsClosed" = false
                    """,
                    new NpgsqlParameter("@id", openDay.Id),
                    new NpgsqlParameter("@cashDeskName", cashDeskName ?? string.Empty),
                    DateParameter("@closeDate", closeDate),
                    new NpgsqlParameter("@closedBy", closedBy ?? string.Empty),
                    new NpgsqlParameter("@openingDebit", openingDebit),
                    new NpgsqlParameter("@openingCredit", openingCredit),
                    new NpgsqlParameter("@debitTurnover", debitTurnover),
                    new NpgsqlParameter("@creditTurnover", creditTurnover),
                    new NpgsqlParameter("@closingDebit", closingDebit),
                    new NpgsqlParameter("@closingCredit", closingCredit),
                    new NpgsqlParameter("@description", description));

                if (affected == 0)
                    throw new InvalidOperationException("Текущий открытый кассовый период не найден или уже закрыт.");
            }
            catch (Exception ex)
            {
                SystemLogService.Error(
                    $"Ошибка закрытия текущего открытого кассового периода. CashDeskId={openDay.CashDeskId}; Date={closeDate:yyyy-MM-dd}",
                    "CashDayClosureService.CloseOpenDayAsync",
                    ex);
                throw;
            }
        }
        public async Task<int> OpenDayAsync(Guid cashDeskId, DateTime closeDate, string openedBy)
        {
            await EnsureExistsAsync();
            var description = $"Открытие кассового дня {closeDate:dd.MM.yyyy}";
            var currentOpenDay = await GetCurrentOpenDayAsync(cashDeskId);

            if (currentOpenDay != null)
                throw new InvalidOperationException($"По выбранной кассе уже открыт текущий кассовый день {currentOpenDay.CloseDate:dd.MM.yyyy}. Сначала закройте его.");

            try
            {
                return await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "CashDayClosures"
                        ("Id", "CashDeskId", "CashDeskName", "CloseDate", "IsClosed", "OpenedBy", "OpenedAt", "UpdatedAt", "Description")
                    VALUES
                        (@id, @cashDeskId, '', @closeDate, false, @openedBy, NOW(), NOW(), @description)
                    ON CONFLICT ("CashDeskId", "CloseDate")
                    DO UPDATE SET
                        "IsClosed" = false,
                        "OpenedBy" = EXCLUDED."OpenedBy",
                        "OpenedAt" = NOW(),
                        "UpdatedAt" = NOW(),
                        "Description" = EXCLUDED."Description"
                    """,
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@openedBy", openedBy ?? string.Empty),
                    new NpgsqlParameter("@description", description),
                    new NpgsqlParameter("@cashDeskId", cashDeskId),
                    DateParameter("@closeDate", closeDate));
            }
            catch (Exception ex)
            {
                SystemLogService.Error(
                    $"Ошибка открытия кассового дня. CashDeskId={cashDeskId}; Date={closeDate:yyyy-MM-dd}",
                    "CashDayClosureService.OpenDayAsync",
                    ex);
                throw;
            }
        }

        public async Task EnsureDayCanAcceptDocumentsAsync(Guid cashDeskId, string cashDeskName, DateTime cashDate)
        {
            await EnsureExistsAsync();

            var currentOpenDay = await GetCurrentOpenDayAsync(cashDeskId);
            if (currentOpenDay == null)
                throw new InvalidOperationException($"По кассе \"{cashDeskName}\" не открыт текущий кассовый день. Перед созданием, изменением или проведением документов откройте день.");
        }

        public async Task<CashDayInfo?> GetCurrentOpenDayAsync(Guid cashDeskId)
        {
            await EnsureExistsAsync();

            return await ExecuteCashDayInfoAsync("""
                SELECT "Id", "CashDeskId", "CashDeskName", "CloseDate", "IsClosed"
                FROM "CashDayClosures"
                WHERE "CashDeskId" = @cashDeskId
                  AND "IsClosed" = false
                ORDER BY "OpenedAt" DESC NULLS LAST, "CloseDate" DESC
                LIMIT 1
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId));
        }

        public async Task<DateTime?> GetLastClosedDayDateAsync(Guid cashDeskId)
        {
            await EnsureExistsAsync();

            return await ExecuteNullableDateAsync("""
                SELECT MAX("CloseDate")
                FROM "CashDayClosures"
                WHERE "CashDeskId" = @cashDeskId
                  AND "IsClosed" = true
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId));
        }
        public async Task<bool> IsDayClosedAsync(Guid cashDeskId, DateTime cashDate)
        {
            await EnsureExistsAsync();

            return await ExecuteScalarBoolAsync("""
                SELECT EXISTS (
                    SELECT 1
                    FROM "CashDayClosures"
                    WHERE "CashDeskId" = @cashDeskId
                      AND "CloseDate" = @cashDate
                      AND "IsClosed" = true
                )
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId),
                DateParameter("@cashDate", cashDate));
        }

        public async Task<bool> IsDayOpenAsync(Guid cashDeskId, DateTime cashDate)
        {
            await EnsureExistsAsync();

            return await ExecuteScalarBoolAsync("""
                SELECT EXISTS (
                    SELECT 1
                    FROM "CashDayClosures"
                    WHERE "CashDeskId" = @cashDeskId
                      AND "CloseDate" = @cashDate
                      AND "IsClosed" = false
                )
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId),
                DateParameter("@cashDate", cashDate));
        }

        public async Task<List<DateTime>> GetOpenDayDatesAsync(Guid cashDeskId)
        {
            await EnsureExistsAsync();

            return await ExecuteDateListAsync("""
                SELECT "CloseDate"
                FROM "CashDayClosures"
                WHERE "CashDeskId" = @cashDeskId
                  AND "IsClosed" = false
                ORDER BY "CloseDate"
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId));
        }

        public async Task<List<DateTime>> GetOpenDatesAsync(Guid cashDeskId, DateTime startDate, DateTime endDate)
        {
            await EnsureExistsAsync();

            return await ExecuteDateListAsync("""
                SELECT "CloseDate"
                FROM "CashDayClosures"
                WHERE "CashDeskId" = @cashDeskId
                  AND "CloseDate" >= @startDate
                  AND "CloseDate" <= @endDate
                  AND "IsClosed" = false
                ORDER BY "CloseDate"
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId),
                DateParameter("@startDate", startDate),
                DateParameter("@endDate", endDate));
        }

        public async Task<List<DateTime>> GetClosedDatesAsync(Guid cashDeskId, DateTime startDate, DateTime endDate)
        {
            await EnsureExistsAsync();

            return await ExecuteDateListAsync("""
                SELECT "CloseDate"
                FROM "CashDayClosures"
                WHERE "CashDeskId" = @cashDeskId
                  AND "CloseDate" >= @startDate
                  AND "CloseDate" <= @endDate
                  AND "IsClosed" = true
                ORDER BY "CloseDate"
                """,
                new NpgsqlParameter("@cashDeskId", cashDeskId),
                DateParameter("@startDate", startDate),
                DateParameter("@endDate", endDate));
        }
    }
}