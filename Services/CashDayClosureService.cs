using System.Data;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    public sealed class CashDayClosureService
    {
        private const string TableDescription = "Закрытие и открытие кассовых дней по кассам";
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
                        "Description" text NOT NULL DEFAULT 'Закрытие/открытие кассовых дней по кассам',
                        CONSTRAINT "PK_CashDayClosures" PRIMARY KEY ("Id")
                    )
                    """);

                await RenameLegacyColumnsAsync();
                await RenameLegacyIndexAsync();

                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "Description" text NOT NULL DEFAULT 'Закрытие/открытие кассовых дней по кассам'""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "CashDeskName" TYPE character varying(300)""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "ClosedBy" TYPE character varying(120)""");
                await _context.Database.ExecuteSqlRawAsync("""ALTER TABLE "CashDayClosures" ALTER COLUMN "OpenedBy" TYPE character varying(120)""");

                await _context.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashDayClosures_CashDesk_CloseDate"
                        ON "CashDayClosures" ("CashDeskId", "CloseDate")
                    """);

                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON TABLE "CashDayClosures" IS 'Служебная таблица: закрытие и открытие кассовых дней по кассам'""");
                await _context.Database.ExecuteSqlRawAsync("""COMMENT ON COLUMN "CashDayClosures"."Description" IS 'Описание операции закрытия/открытия кассового дня'""");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка подготовки служебной таблицы CashDayClosures.", "CashDayClosureService.EnsureSchemaAsync", ex);
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

        public async Task CloseDayAsync(Guid cashDeskId, string cashDeskName, DateTime closeDate, string closedBy)
        {
            await EnsureExistsAsync();
            var description = $"Закрытие кассового дня {closeDate:dd.MM.yyyy}";
            var closeDateUtc = DateTime.SpecifyKind(closeDate.Date, DateTimeKind.Utc);

            try
            {
                await _context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "CashDayClosures"
                        ("Id", "CashDeskId", "CashDeskName", "CloseDate", "IsClosed", "ClosedBy", "ClosedAt", "UpdatedAt", "Description")
                    VALUES
                        (@id, @cashDeskId, @cashDeskName, @closeDate, true, @closedBy, NOW(), NOW(), @description)
                    ON CONFLICT ("CashDeskId", "CloseDate")
                    DO UPDATE SET
                        "CashDeskName" = EXCLUDED."CashDeskName",
                        "IsClosed" = true,
                        "ClosedBy" = EXCLUDED."ClosedBy",
                        "ClosedAt" = NOW(),
                        "OpenedBy" = '',
                        "OpenedAt" = NULL,
                        "UpdatedAt" = NOW(),
                        "Description" = EXCLUDED."Description"
                    """,
                    new NpgsqlParameter("@id", Guid.NewGuid()),
                    new NpgsqlParameter("@cashDeskId", cashDeskId),
                    new NpgsqlParameter("@cashDeskName", cashDeskName ?? string.Empty),
                    new NpgsqlParameter("@closeDate", closeDateUtc),
                    new NpgsqlParameter("@closedBy", closedBy ?? string.Empty),
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

        public async Task<int> OpenDayAsync(Guid cashDeskId, DateTime closeDate, string openedBy)
        {
            await EnsureExistsAsync();
            var description = $"Открытие кассового дня {closeDate:dd.MM.yyyy}";
            var closeDateUtc = DateTime.SpecifyKind(closeDate.Date, DateTimeKind.Utc);

            try
            {
                return await _context.Database.ExecuteSqlRawAsync("""
                    UPDATE "CashDayClosures"
                    SET "IsClosed" = false,
                        "OpenedBy" = @openedBy,
                        "OpenedAt" = NOW(),
                        "UpdatedAt" = NOW(),
                        "Description" = @description
                    WHERE "CashDeskId" = @cashDeskId
                      AND "CloseDate" = @closeDate
                      AND "IsClosed" = true
                    """,
                    new NpgsqlParameter("@openedBy", openedBy ?? string.Empty),
                    new NpgsqlParameter("@description", description),
                    new NpgsqlParameter("@cashDeskId", cashDeskId),
                    new NpgsqlParameter("@closeDate", closeDateUtc));
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

        public async Task<List<DateTime>> GetClosedDatesAsync(Guid cashDeskId, DateTime startDate, DateTime endDate)
        {
            await EnsureExistsAsync();

            var startDateUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            var endDateUtc = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

            var dates = await _context.Database.SqlQuery<DateTime>($"""
                SELECT "CloseDate"::timestamp AS "Value"
                FROM "CashDayClosures"
                WHERE "CashDeskId" = {cashDeskId}
                  AND "CloseDate" >= {startDateUtc}
                  AND "CloseDate" <= {endDateUtc}
                  AND "IsClosed" = true
                ORDER BY "CloseDate"
                """).ToListAsync();

            return dates.Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToList();
        }
    }
}


