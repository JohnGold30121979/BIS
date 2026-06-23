using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class ConfigurationExchangeService
    {
        private readonly AppDbContext _context;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ConfigurationExchangeService(AppDbContext context)
        {
            _context = context;
        }

        public async Task ExportAsync(string filePath)
        {
            await new ModuleMetadataService(_context).EnsureSchemaAsync();
            var metadata = await _context.MetadataObjects
                .AsNoTracking()
                .Include(item => item.Fields)
                .Include(item => item.Calculations)
                .Include(item => item.PostingRules)
                .OrderBy(item => item.Order)
                .ToListAsync();

            var reports = await _context.Reports
                .AsNoTracking()
                .Include(item => item.Fields)
                .Include(item => item.Filters)
                .Include(item => item.Groups)
                .OrderBy(item => item.Order)
                .ToListAsync();

            DetachMetadataNavigation(metadata);
            DetachReportNavigation(reports);

            var package = new ConfigurationPackage
            {
                ExportedAt = DateTime.UtcNow,
                SystemConfigurations = await _context.SystemConfigurations.AsNoTracking().ToListAsync(),
                MetadataObjects = metadata,
                Reports = reports,
                Modules = await _context.MetadataModules.AsNoTracking().OrderBy(item => item.Order).ToListAsync(),
                ModuleItems = await _context.MetadataModuleItems.AsNoTracking().OrderBy(item => item.Order).ToListAsync()
            };

            foreach (var obj in metadata.Where(item => !string.IsNullOrWhiteSpace(item.TableName)))
            {
                package.TableData.Add(new ConfigurationTableData
                {
                    MetadataObjectId = obj.Id,
                    ObjectName = obj.Name,
                    TableName = obj.TableName,
                    Rows = await ReadTableAsync(obj.TableName)
                });
            }

            var json = JsonSerializer.Serialize(package, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<ConfigurationPackage> ImportAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            var package = JsonSerializer.Deserialize<ConfigurationPackage>(json, JsonOptions)
                ?? throw new InvalidOperationException("Файл конфигурации пустой или поврежден.");

            if (!string.Equals(package.Format, "BIS.Configuration", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Файл не является выгрузкой конфигурации BIS.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await ReplaceMetadataAsync(package.MetadataObjects);
                await ReplaceReportsAsync(package.Reports);
                await ReplaceSystemConfigurationAsync(package.SystemConfigurations);
                await ReplaceModulesAsync(package.Modules, package.ModuleItems);
                await _context.SaveChangesAsync();

                var metadataService = new MetadataService(_context);
                foreach (var obj in package.MetadataObjects.Where(item => !string.IsNullOrWhiteSpace(item.TableName)))
                    await metadataService.CreateDynamicTableAsync(obj);

                foreach (var table in package.TableData)
                    await ReplaceTableDataAsync(table);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return package;
        }

        private async Task ReplaceMetadataAsync(List<MetadataObject> metadata)
        {
            var existingObjects = await _context.MetadataObjects.Include(item => item.Fields).ToListAsync();
            var existingTables = existingObjects
                .Where(item => !string.IsNullOrWhiteSpace(item.TableName))
                .Select(item => item.TableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var tableName in existingTables)
                await _context.Database.ExecuteSqlRawAsync($"DROP TABLE IF EXISTS {Quote(tableName)} CASCADE;");

            _context.MetadataPostingRules.RemoveRange(await _context.MetadataPostingRules.ToListAsync());
            _context.MetadataCalculations.RemoveRange(await _context.MetadataCalculations.ToListAsync());
            _context.MetadataFields.RemoveRange(await _context.MetadataFields.ToListAsync());
            _context.MetadataObjects.RemoveRange(existingObjects);
            await _context.SaveChangesAsync();

            DetachMetadataNavigation(metadata);
            foreach (var obj in metadata)
            {
                foreach (var field in obj.Fields)
                    field.MetadataObjectId = obj.Id;
                foreach (var calc in obj.Calculations)
                    calc.MetadataObjectId = obj.Id;
                foreach (var rule in obj.PostingRules)
                    rule.MetadataObjectId = obj.Id;
            }

            await _context.MetadataObjects.AddRangeAsync(metadata);
        }

        private async Task ReplaceReportsAsync(List<Report> reports)
        {
            _context.ReportGroups.RemoveRange(await _context.ReportGroups.ToListAsync());
            _context.ReportFilters.RemoveRange(await _context.ReportFilters.ToListAsync());
            _context.ReportFields.RemoveRange(await _context.ReportFields.ToListAsync());
            _context.Reports.RemoveRange(await _context.Reports.ToListAsync());
            await _context.SaveChangesAsync();

            DetachReportNavigation(reports);
            foreach (var report in reports)
            {
                foreach (var field in report.Fields)
                    field.ReportId = report.Id;
                foreach (var filter in report.Filters)
                    filter.ReportId = report.Id;
                foreach (var group in report.Groups)
                    group.ReportId = report.Id;
            }

            await _context.Reports.AddRangeAsync(reports);
        }

        private async Task ReplaceSystemConfigurationAsync(List<SystemConfiguration> configurations)
        {
            _context.SystemConfigurations.RemoveRange(await _context.SystemConfigurations.ToListAsync());
            if (configurations.Count > 0)
                await _context.SystemConfigurations.AddRangeAsync(configurations);
        }

        private async Task ReplaceModulesAsync(List<MetadataModule> modules, List<MetadataModuleItem> items)
        {
            await new ModuleMetadataService(_context).EnsureSchemaAsync();
            _context.MetadataModuleItems.RemoveRange(await _context.MetadataModuleItems.ToListAsync());
            _context.MetadataModules.RemoveRange(await _context.MetadataModules.ToListAsync());
            if (modules.Count > 0)
                await _context.MetadataModules.AddRangeAsync(modules);
            if (items.Count > 0)
                await _context.MetadataModuleItems.AddRangeAsync(items);
        }

        private async Task<List<Dictionary<string, object?>>> ReadTableAsync(string tableName)
        {
            var rows = new List<Dictionary<string, object?>>();
            if (!await TableExistsAsync(tableName))
                return rows;

            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Quote(tableName)}";

            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }

            return rows;
        }

        private async Task ReplaceTableDataAsync(ConfigurationTableData table)
        {
            if (string.IsNullOrWhiteSpace(table.TableName) || !await TableExistsAsync(table.TableName))
                return;

            await _context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {Quote(table.TableName)};");

            foreach (var row in table.Rows)
                await InsertRowAsync(table.TableName, row);
        }

        private async Task InsertRowAsync(string tableName, Dictionary<string, object?> row)
        {
            if (row.Count == 0)
                return;

            var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                var columns = row.Keys.ToList();
                var parameterNames = columns.Select((_, index) => $"@p{index}").ToList();
                command.CommandText =
                    $"INSERT INTO {Quote(tableName)} ({string.Join(", ", columns.Select(Quote))}) VALUES ({string.Join(", ", parameterNames)})";

                for (var i = 0; i < columns.Count; i++)
                    command.Parameters.AddWithValue(parameterNames[i], NormalizeValue(row[columns[i]]) ?? DBNull.Value);

                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = @tableName
                );";
            AddParameter(command, "@tableName", tableName);

            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                return Convert.ToBoolean(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static object? NormalizeValue(object? value)
        {
            if (value is null or DBNull)
                return null;
            if (value is JsonElement element)
                return NormalizeJsonElement(element);
            return value;
        }

        private static object? NormalizeJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String when element.TryGetGuid(out var guidValue) => guidValue,
                JsonValueKind.String when element.TryGetDateTime(out var dateValue) => dateValue,
                JsonValueKind.String => element.GetString(),
                _ => element.GetRawText()
            };
        }

        private static void DetachMetadataNavigation(IEnumerable<MetadataObject> metadata)
        {
            foreach (var obj in metadata)
            {
                obj.MetadataConfig = null;
                foreach (var field in obj.Fields)
                    field.MetadataObject = null!;
                foreach (var calc in obj.Calculations)
                    calc.MetadataObject = null;
                foreach (var rule in obj.PostingRules)
                    rule.MetadataObject = null;
            }
        }

        private static void DetachReportNavigation(IEnumerable<Report> reports)
        {
            foreach (var report in reports)
            {
                foreach (var field in report.Fields)
                    field.Report = null!;
                foreach (var filter in report.Filters)
                    filter.Report = null!;
                foreach (var group in report.Groups)
                    group.Report = null!;
                report.HeadersFooters.Clear();
            }
        }

        private static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Пустой идентификатор базы данных.", nameof(identifier));
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }
    }
}
