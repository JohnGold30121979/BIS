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
using System.Text;
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
            var package = await BuildPackageAsync();
            var json = JsonSerializer.Serialize(package, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task ExportEncryptedAsync(string filePath)
        {
            var package = await BuildPackageAsync();
            var json = JsonSerializer.Serialize(package, JsonOptions);
            var encrypted = BisPackageCryptoService.Protect(
                Encoding.UTF8.GetBytes(json),
                "BIS.Configuration");
            await File.WriteAllBytesAsync(filePath, encrypted);
        }

        public async Task<ConfigurationPackage> ImportAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            return await ImportJsonAsync(json);
        }

        public async Task<ConfigurationPackage> ImportEncryptedOrJsonAsync(string filePath)
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            var json = BisPackageCryptoService.IsEncryptedPackage(bytes)
                ? Encoding.UTF8.GetString(BisPackageCryptoService.Unprotect(bytes, "BIS.Configuration"))
                : await File.ReadAllTextAsync(filePath);

            return await ImportJsonAsync(json);
        }

        private async Task<ConfigurationPackage> ImportJsonAsync(string json)
        {
            var package = JsonSerializer.Deserialize<ConfigurationPackage>(json, JsonOptions)
                ?? throw new InvalidOperationException("Файл конфигурации пустой или поврежден.");

            if (!string.Equals(package.Format, "BIS.Configuration", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Файл не является выгрузкой конфигурации BIS.");

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await ReplaceMetadataAsync(package.MetadataObjects);
                await ReplaceSystemReportDataSetsAsync(package.ReportDataSets);
                await ReplaceSystemReportsAsync(package.Reports);
                await ReplaceRegulatedReportTemplatesAsync(package.RegulatedReportTemplates);
                await ReplaceSystemConfigurationAsync(package.SystemConfigurations);
                await ReplaceModulesAsync(package.Modules, package.ModuleItems);
                await _context.SaveChangesAsync();

                // ВАЖНО: лог 09:35 — сырой INSERT в MetadataFields ронял транзакцию
                // (FK 23503), а следующий запрос падал с 25P02. Поэтому: 1) фиксируем
                // транзакцию СРАЗУ после SaveChanges (ядро импорта атомарно);
                // 2) побочные действия (DDL динамических таблиц, сид системных
                // наборов) выполняем уже ВНЕ транзакции — их ошибка не должна
                // отравлять импорт и откатывать успешно загруженные данные.
                await transaction.CommitAsync();
            }
            catch
            {
                // Строгий rollback: продолжать импорт после SQL-ошибки нельзя —
                // PostgreSQL помечает транзакцию aborted. Откатываем целиком.
                await transaction.RollbackAsync();
                throw;
            }

            try
            {
                var metadataService = new MetadataService(_context);
                foreach (var obj in package.MetadataObjects.Where(item => !string.IsNullOrWhiteSpace(item.TableName)))
                    await metadataService.CreateDynamicTableAsync(obj);

                await EnsureBuiltInSchemasBeforeDataImportAsync();

                foreach (var table in package.TableData)
                    await ReplaceTableDataAsync(table);

                // Досоздаем встроенные FRX из текущей сборки после импорта старой/чужой конфигурации.
                // ВНЕ транзакции ядра: сбой сида/EnsureCashOrderTurnoverDataSetAsync
                // (лог 09:35: FK в MetadataFields) не должен откатывать уже
                // зафиксированный импорт и травить соединение.
                await metadataService.EnsureStandardReportsAsync();
            }
            catch (Exception ex)
            {
                // Пост-этап: ядро уже закоммичено выше, здесь только предупреждаем.
                // Пробрасываем InvalidOperationException с понятным текстом, чтобы UI
                // показал причину, но данные импорта остались в БД.
                SystemLogService.Warning(
                    $"Пост-обработка импорта завершилась с ошибкой: {ex.Message}",
                    "ConfigurationExchange",
                    ex);
                throw new InvalidOperationException(
                    $"Конфигурация загружена, но пост-обработка завершилась с ошибкой: {ex.Message}", ex);
            }

            return package;
        }

        private async Task EnsureBuiltInSchemasBeforeDataImportAsync()
        {
            await new InvoiceService(_context).EnsureSchemaAsync();
            await new PrintFormService(_context).EnsureSchemaAsync();
            await new ModuleMetadataService(_context).EnsureSchemaAsync();
            await new RegulatedReportTemplateService(_context).EnsureSchemaAsync();
            await new CashDayClosureService(_context).EnsureSchemaAsync();
        }

        private async Task<ConfigurationPackage> BuildPackageAsync()
        {
            await new ModuleMetadataService(_context).EnsureSchemaAsync();
            await new PrintFormService(_context).EnsureSchemaAsync();
            await new BisPatchService(_context).EnsureSchemaAsync();
            await new RegulatedReportTemplateService(_context).EnsureSchemaAsync();
            await new MetadataService(_context).EnsureStandardReportsAsync();
            await new ReportDataSetService(_context).EnsureStandardDataSetsAsync();
            await MarkKnownStandardReportDataSetsAsSystemAsync();
            await MarkKnownStandardReportsAsSystemAsync();

            var metadata = await _context.MetadataObjects
                .AsNoTracking()
                .Include(item => item.Fields)
                .Include(item => item.Calculations)
                .Include(item => item.PostingRules)
                .OrderBy(item => item.Order)
                .ToListAsync();

            var reportDataSets = await _context.ReportDataSets
                .AsNoTracking()
                .Include(item => item.Fields)
                .Where(item => item.IsSystem)
                .OrderBy(item => item.Code)
                .ToListAsync();

            var reports = await _context.Reports
                .AsNoTracking()
                .Include(item => item.Fields)
                .Include(item => item.Filters)
                .Include(item => item.Groups)
                .Include(item => item.ElementMappings)
                .Include(item => item.HeadersFooters)
                .Where(item => item.IsSystem)
                .OrderBy(item => item.Order)
                .ToListAsync();

            DetachMetadataNavigation(metadata);
            DetachReportDataSetNavigation(reportDataSets);
            DetachReportNavigation(reports);

            var package = new ConfigurationPackage
            {
                ExportedAt = DateTime.UtcNow,
                SystemConfigurations = await _context.SystemConfigurations.AsNoTracking().ToListAsync(),
                MetadataObjects = metadata,
                ReportDataSets = reportDataSets,
                Reports = reports,
                RegulatedReportTemplates = await _context.RegulatedReportTemplates.AsNoTracking()
                    .OrderBy(item => item.Code).ThenBy(item => item.Version).ToListAsync(),
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

            return package;
        }

        private async Task MarkKnownStandardReportDataSetsAsSystemAsync()
        {
            var now = DateTime.UtcNow;
            await _context.ReportDataSets
                .Where(dataSet => !dataSet.IsSystem && dataSet.Code == ReportDataSetService.CashOrderTurnoverDataSetCode)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(dataSet => dataSet.IsSystem, true)
                    .SetProperty(dataSet => dataSet.UpdatedAt, now));
        }

        private async Task MarkKnownStandardReportsAsSystemAsync()
        {
            var now = DateTime.UtcNow;
            await _context.Reports
                .Where(report => !report.IsSystem &&
                    (EF.Functions.Like(report.Code, "standard.%") ||
                     EF.Functions.Like(report.Code, "assets.%") ||
                     EF.Functions.Like(report.Code, "inventory.%") ||
                     EF.Functions.Like(report.Code, "cash.receipt.%") ||
                     EF.Functions.Like(report.Code, "cash.payment.%") ||
                     EF.Functions.Like(report.Code, "invoice.sales.%") ||
                     EF.Functions.Like(report.Code, "invoice.purchase.%") ||
                     EF.Functions.Like(report.Code, "payment.order.%")))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(report => report.IsSystem, true)
                    .SetProperty(report => report.UpdatedAt, now));
        }

        private static bool IsConfigurationSystemReport(Report report) =>
            report.IsSystem || StandardReportDeletionService.IsStandardReportCode(report.Code);

        private static bool IsConfigurationSystemReportDataSet(ReportDataSet dataSet) =>
            dataSet.IsSystem ||
            string.Equals(dataSet.Code, ReportDataSetService.CashOrderTurnoverDataSetCode, StringComparison.OrdinalIgnoreCase);

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

            var metadataConfigId = await EnsureImportMetadataConfigurationAsync();

            DetachMetadataNavigation(metadata);
            foreach (var obj in metadata)
            {
                // Конфигурационный пакет может быть выгружен из другой инфобазы.
                // Ее MetadataConfigId в текущей базе не существует и ломает FK при загрузке.
                obj.MetadataConfigId = metadataConfigId;
                foreach (var field in obj.Fields)
                    field.MetadataObjectId = obj.Id;
                foreach (var calc in obj.Calculations)
                    calc.MetadataObjectId = obj.Id;
                foreach (var rule in obj.PostingRules)
                    rule.MetadataObjectId = obj.Id;
            }

            await _context.MetadataObjects.AddRangeAsync(metadata);
        }

        private async Task<Guid> EnsureImportMetadataConfigurationAsync()
        {
            var config = await _context.MetadataConfigurations
                .OrderByDescending(item => item.IsInitialized)
                .ThenBy(item => item.CreatedAt)
                .FirstOrDefaultAsync();

            if (config == null)
            {
                config = new MetadataConfiguration
                {
                    Id = Guid.NewGuid(),
                    InfoBaseId = Guid.Empty,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Version = "1.0",
                    IsInitialized = true
                };
                await _context.MetadataConfigurations.AddAsync(config);
                await _context.SaveChangesAsync();
                return config.Id;
            }

            config.IsInitialized = true;
            config.UpdatedAt = DateTime.UtcNow;
            return config.Id;
        }

        private async Task ReplaceSystemReportDataSetsAsync(List<ReportDataSet>? dataSets)
        {
            await new ReportDataSetService(_context).EnsureSchemaAsync();
            dataSets ??= new List<ReportDataSet>();

            var systemDataSets = dataSets.Where(IsConfigurationSystemReportDataSet).ToList();
            if (systemDataSets.Count == 0)
                return;

            await MarkCollidingReportDataSetsAsSystemAsync(systemDataSets);

            var existingSystemDataSets = await _context.ReportDataSets
                .Where(item => item.IsSystem)
                .ToListAsync();
            _context.ReportDataSets.RemoveRange(existingSystemDataSets);
            await _context.SaveChangesAsync();

            DetachReportDataSetNavigation(systemDataSets);
            foreach (var dataSet in systemDataSets)
            {
                dataSet.IsSystem = true;
                foreach (var field in dataSet.Fields)
                    field.ReportDataSetId = dataSet.Id;
            }

            await _context.ReportDataSets.AddRangeAsync(systemDataSets);
        }

        private async Task MarkCollidingReportDataSetsAsSystemAsync(List<ReportDataSet> systemDataSets)
        {
            var codes = systemDataSets
                .Select(item => item.Code?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (codes.Length == 0)
                return;

            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""ReportDataSets""
                  SET ""IsSystem"" = true
                  WHERE COALESCE(""Code"", '') = ANY(@codes);",
                new NpgsqlParameter("@codes", codes));
        }
        private async Task ReplaceSystemReportsAsync(List<Report>? reports)
        {
            await new PrintFormService(_context).EnsureSchemaAsync();
            reports ??= new List<Report>();

            var systemReports = reports.Where(IsConfigurationSystemReport).ToList();
            if (systemReports.Count == 0)
                return;

            await MarkCollidingReportsAsSystemAsync(systemReports);

            var existingSystemReports = await _context.Reports
                .Where(item => item.IsSystem)
                .ToListAsync();
            _context.Reports.RemoveRange(existingSystemReports);
            await _context.SaveChangesAsync();

            DetachReportNavigation(systemReports);
            foreach (var report in systemReports)
            {
                report.IsSystem = true;
                foreach (var field in report.Fields)
                    field.ReportId = report.Id;
                foreach (var filter in report.Filters)
                    filter.ReportId = report.Id;
                foreach (var group in report.Groups)
                    group.ReportId = report.Id;
                foreach (var mapping in report.ElementMappings)
                    mapping.ReportId = report.Id;
                foreach (var headerFooter in report.HeadersFooters)
                    headerFooter.ReportId = report.Id;
            }

            await _context.Reports.AddRangeAsync(systemReports);
        }

        private async Task MarkCollidingReportsAsSystemAsync(List<Report> systemReports)
        {
            var codes = systemReports
                .Select(item => item.Code?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (codes.Length == 0)
                return;

            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Reports""
                  SET ""IsSystem"" = true
                  WHERE COALESCE(""Code"", '') = ANY(@codes);",
                new NpgsqlParameter("@codes", codes));
        }

        private async Task ReplaceRegulatedReportTemplatesAsync(List<RegulatedReportTemplate> templates)
        {
            await new RegulatedReportTemplateService(_context).EnsureSchemaAsync();
            _context.RegulatedReportTemplates.RemoveRange(await _context.RegulatedReportTemplates.ToListAsync());
            await _context.SaveChangesAsync();

            if (templates.Count == 0)
                return;

            foreach (var template in templates)
            {
                if (template.Id == Guid.Empty)
                    template.Id = Guid.NewGuid();
            }

            await _context.RegulatedReportTemplates.AddRangeAsync(templates);
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

            // ВАЖНО: лог 09:43/09:59 — INSERT/UPDATE каталогов падали с
            // "A command is already in progress", потому что команда создавалась на
            // ОБЩЕМ соединении _context, уже занятом другим запросом. Выполняем
            // каждую вставку на отдельном соединении из пула, а ошибку НЕ глотаем:
            // проглоченная ошибка SQL отравляет транзакцию импорта (25P02).
            await _context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {Quote(table.TableName)};");

            foreach (var row in table.Rows)
                await InsertRowOnStandaloneConnectionAsync(table.TableName, row);
        }

        private async Task InsertRowOnStandaloneConnectionAsync(string tableName, Dictionary<string, object?> row)
        {
            if (row.Count == 0)
                return;

            var connectionString = _context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось получить строку подключения к инфобазе.");

            await using var connection = new NpgsqlConnection(connectionString);
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
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Ошибка загрузки данных таблицы '{tableName}': {ex.Message}", ex);
            }
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

        private static void DetachReportDataSetNavigation(IEnumerable<ReportDataSet> dataSets)
        {
            foreach (var dataSet in dataSets)
            {
                foreach (var field in dataSet.Fields)
                    field.ReportDataSet = null;
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
                foreach (var mapping in report.ElementMappings)
                    mapping.Report = null!;
                foreach (var headerFooter in report.HeadersFooters)
                    headerFooter.Report = null!;
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

