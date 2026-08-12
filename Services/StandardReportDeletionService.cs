using System.Data;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services;

public sealed class StandardReportDeletionService
{
    private readonly AppDbContext _context;

    public StandardReportDeletionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EnsureSchemaAsync()
    {
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""DeletedStandardReports"" (
                ""Code"" varchar(160) NOT NULL,
                ""Name"" varchar(500) NOT NULL DEFAULT '',
                ""DeletedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""DeletedBy"" varchar(160) NOT NULL DEFAULT '',
                ""Reason"" text NOT NULL DEFAULT '',
                CONSTRAINT ""PK_DeletedStandardReports"" PRIMARY KEY (""Code"")
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeletedStandardReports_DeletedAt""
                ON ""DeletedStandardReports"" (""DeletedAt"");");
    }

    public async Task<HashSet<string>> GetDeletedCodesAsync()
    {
        await EnsureSchemaAsync();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"SELECT ""Code"" FROM ""DeletedStandardReports"";";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(code))
                    result.Add(code);
            }
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }

        return result;
    }

    public async Task MarkDeletedAsync(string? code, string? name, string reason = "ConfiguratorDelete")
    {
        if (!IsStandardReportCode(code))
            return;

        await EnsureSchemaAsync();
        await _context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO ""DeletedStandardReports""
                (""Code"", ""Name"", ""DeletedAt"", ""DeletedBy"", ""Reason"")
            VALUES
                (@code, @name, @deletedAt, @deletedBy, @reason)
            ON CONFLICT (""Code"") DO UPDATE SET
                ""Name"" = EXCLUDED.""Name"",
                ""DeletedAt"" = EXCLUDED.""DeletedAt"",
                ""DeletedBy"" = EXCLUDED.""DeletedBy"",
                ""Reason"" = EXCLUDED.""Reason"";",
            new NpgsqlParameter("@code", code!.Trim()),
            new NpgsqlParameter("@name", name ?? string.Empty),
            new NpgsqlParameter("@deletedAt", DateTime.UtcNow),
            new NpgsqlParameter("@deletedBy", Environment.UserName),
            new NpgsqlParameter("@reason", reason));
    }

    public static bool IsStandardReportCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        return code.StartsWith("standard.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("cash.receipt.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("cash.payment.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoice.sales.", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("invoice.purchase.", StringComparison.OrdinalIgnoreCase);
    }
}
