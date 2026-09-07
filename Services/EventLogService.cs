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

                await using var connection = await OpenLogConnectionAsync();
                await EnsureSchemaAsync(connection);

                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO events
                    (""Id"", event_time, user_name, action, entity_type, entity_name, record_id, details)
                    VALUES (@id, @time, @user, @action, @entityType, @entityName, @recordId, @details);";
                command.Parameters.Add(new NpgsqlParameter("@id", eventId));
                command.Parameters.Add(new NpgsqlParameter("@time", timestamp));
                command.Parameters.Add(new NpgsqlParameter("@user", Environment.UserName));
                command.Parameters.Add(new NpgsqlParameter("@action", action));
                command.Parameters.Add(new NpgsqlParameter("@entityType", entityType));
                command.Parameters.Add(new NpgsqlParameter("@entityName", entityName));
                command.Parameters.Add(new NpgsqlParameter("@recordId", (object?)recordId ?? DBNull.Value));
                command.Parameters.Add(new NpgsqlParameter("@details", detailsText));
                await command.ExecuteNonQueryAsync();

                LogFileOnly(action, entityType, entityName, recordId, detailsText, timestamp);
            }
            catch (Exception ex)
            {
                SystemLogService.Warning("Ошибка записи события в журнал событий.", "EventLogService.LogAsync", ex);
                System.Diagnostics.Debug.WriteLine($"Ошибка записи события: {ex.Message}");
            }
        }

        public static void LogFileOnly(
            string action,
            string entityType,
            string entityName,
            Guid? recordId = null,
            object? details = null,
            DateTime? timestamp = null)
        {
            try
            {
                var detailsText = details switch
                {
                    null => string.Empty,
                    string text => text,
                    _ => JsonSerializer.Serialize(details)
                };

                WriteFileLog(timestamp ?? DateTime.UtcNow, action, entityType, entityName, recordId, detailsText);
            }
            catch (Exception ex)
            {
                SystemLogService.Warning("Ошибка записи файлового журнала событий.", "EventLogService.LogFileOnly", ex);
                System.Diagnostics.Debug.WriteLine($"Ошибка файлового лога: {ex.Message}");
            }
        }

        public async Task EnsureSchemaAsync()
        {
            await using var connection = await OpenLogConnectionAsync();
            await EnsureSchemaAsync(connection);
        }

        private async Task<NpgsqlConnection> OpenLogConnectionAsync()
        {
            var connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Не удалось определить строку подключения для журнала событий.");
            }

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

        private static async Task EnsureSchemaAsync(NpgsqlConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
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
                CREATE INDEX IF NOT EXISTS ""IX_events_entity"" ON events (entity_type, entity_name);";
            await command.ExecuteNonQueryAsync();
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
