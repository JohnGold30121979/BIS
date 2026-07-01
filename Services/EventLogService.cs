using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    public sealed class EventLogService
    {
        private readonly AppDbContext _context;

        public EventLogService(AppDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            string action,
            string entityType,
            string entityName,
            Guid? recordId = null,
            object? details = null)
        {
            try
            {
                var eventId = Guid.NewGuid();
                var timestamp = DateTime.UtcNow;
                var detailsText = details switch
                {
                    null => string.Empty,
                    string text => text,
                    _ => JsonSerializer.Serialize(details)
                };

                await EnsureSchemaAsync();
                await _context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO events
                    (""Id"", event_time, user_name, action, entity_type, entity_name, record_id, details)
                    VALUES (@id, @time, @user, @action, @entityType, @entityName, @recordId, @details);",
                    new NpgsqlParameter("@id", eventId),
                    new NpgsqlParameter("@time", timestamp),
                    new NpgsqlParameter("@user", Environment.UserName),
                    new NpgsqlParameter("@action", action),
                    new NpgsqlParameter("@entityType", entityType),
                    new NpgsqlParameter("@entityName", entityName),
                    new NpgsqlParameter("@recordId", (object?)recordId ?? DBNull.Value),
                    new NpgsqlParameter("@details", detailsText));

                WriteFileLog(timestamp, action, entityType, entityName, recordId, detailsText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка записи события: {ex.Message}");
            }
        }

        public async Task EnsureSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS events (
                    ""Id"" uuid PRIMARY KEY,
                    event_time timestamp NOT NULL,
                    user_name varchar(160) NOT NULL DEFAULT '',
                    action varchar(80) NOT NULL,
                    entity_type varchar(80) NOT NULL DEFAULT '',
                    entity_name varchar(200) NOT NULL DEFAULT '',
                    record_id uuid NULL,
                    details text NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS ""IX_events_time"" ON events (event_time);
                CREATE INDEX IF NOT EXISTS ""IX_events_entity"" ON events (entity_type, entity_name);");
        }

        private static void WriteFileLog(
            DateTime timestamp,
            string action,
            string entityType,
            string entityName,
            Guid? recordId,
            string details)
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var logFile = Path.Combine(logDirectory, $"events_{DateTime.Now:yyyyMMdd}.log");
            var line =
                $"{timestamp:O}\t{Environment.UserName}\t{action}\t{entityType}\t{entityName}\t{recordId?.ToString() ?? ""}\t{details}{Environment.NewLine}";
            File.AppendAllText(logFile, line);
        }
    }
}
