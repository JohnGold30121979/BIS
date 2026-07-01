using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed class BisPatchService
    {
        public const string PatchKind = "BIS.Patch";

        private readonly AppDbContext _context;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public BisPatchService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS system_patches (
                    patch_id varchar(120) PRIMARY KEY,
                    version varchar(50) NOT NULL,
                    name varchar(200) NOT NULL,
                    description text NOT NULL DEFAULT '',
                    checksum varchar(64) NOT NULL,
                    applied_at timestamp NULL,
                    applied_by varchar(120) NOT NULL DEFAULT '',
                    app_version varchar(50) NOT NULL DEFAULT '',
                    status varchar(30) NOT NULL DEFAULT 'Pending',
                    error text NOT NULL DEFAULT '',
                    created_at timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS description text NOT NULL DEFAULT '';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS checksum varchar(64) NOT NULL DEFAULT '';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS applied_by varchar(120) NOT NULL DEFAULT '';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS app_version varchar(50) NOT NULL DEFAULT '';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS status varchar(30) NOT NULL DEFAULT 'Pending';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS error text NOT NULL DEFAULT '';
                ALTER TABLE system_patches ADD COLUMN IF NOT EXISTS created_at timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;
                CREATE INDEX IF NOT EXISTS ix_system_patches_status ON system_patches(status);
                CREATE INDEX IF NOT EXISTS ix_system_patches_applied_at ON system_patches(applied_at);
            ");
        }

        public async Task<List<SystemPatchRecord>> GetHistoryAsync()
        {
            await EnsureSchemaAsync();
            var result = new List<SystemPatchRecord>();
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT patch_id, version, name, description, checksum, applied_at,
                       applied_by, app_version, status, error, created_at
                FROM system_patches
                ORDER BY COALESCE(applied_at, created_at) DESC, patch_id;";

            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new SystemPatchRecord
                    {
                        PatchId = reader.GetString(0),
                        Version = reader.GetString(1),
                        Name = reader.GetString(2),
                        Description = reader.GetString(3),
                        Checksum = reader.GetString(4),
                        AppliedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                        AppliedBy = reader.GetString(6),
                        AppVersion = reader.GetString(7),
                        Status = reader.GetString(8),
                        Error = reader.GetString(9),
                        CreatedAt = reader.GetDateTime(10)
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

        public async Task<string> GetCurrentPatchVersionAsync()
        {
            await EnsureSchemaAsync();
            var value = await ExecuteScalarAsync(@"
                SELECT version
                FROM system_patches
                WHERE status = 'Applied'
                ORDER BY applied_at DESC NULLS LAST, created_at DESC
                LIMIT 1;");
            return value?.ToString() ?? string.Empty;
        }

        public async Task EnsureBaselinePatchAsync(string version)
        {
            await EnsureSchemaAsync();
            var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim();
            var patchId = $"baseline-{normalizedVersion}";
            var existing = await ExecuteScalarAsync(
                "SELECT COUNT(*) FROM system_patches WHERE patch_id = @patchId;",
                new NpgsqlParameter("@patchId", patchId));
            if (Convert.ToInt32(existing) > 0)
                return;

            await _context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO system_patches
                    (patch_id, version, name, description, checksum, applied_at, applied_by, app_version, status, error, created_at)
                VALUES
                    (@patchId, @version, @name, @description, @checksum, CURRENT_TIMESTAMP, @appliedBy, @appVersion, 'Applied', '', CURRENT_TIMESTAMP)
                ON CONFLICT (patch_id) DO NOTHING;",
                new NpgsqlParameter("@patchId", patchId),
                new NpgsqlParameter("@version", normalizedVersion),
                new NpgsqlParameter("@name", "Начальная версия инфобазы"),
                new NpgsqlParameter("@description", "Базовая запись версии при создании или подключении инфобазы."),
                new NpgsqlParameter("@checksum", BisPackageCryptoService.ComputeSha256(Encoding.UTF8.GetBytes(patchId))),
                new NpgsqlParameter("@appliedBy", Environment.UserName),
                new NpgsqlParameter("@appVersion", GetAppVersion()));
        }

        public async Task<BisPatchInspectionResult> InspectPatchAsync(string filePath)
        {
            var package = await ReadPatchPackageAsync(filePath);
            return new BisPatchInspectionResult
            {
                Manifest = package.Manifest,
                Checksum = package.Checksum,
                Entries = package.Entries
            };
        }

        public async Task<BisPatchApplyResult> ApplyPatchAsync(string filePath)
        {
            await EnsureSchemaAsync();
            var package = await ReadPatchPackageAsync(filePath);
            ValidateManifest(package.Manifest);

            if (await IsPatchAppliedAsync(package.Manifest.PatchId))
                throw new InvalidOperationException($"Патч {package.Manifest.PatchId} уже применен к этой инфобазе.");

            await EnsureDependenciesAsync(package.Manifest);
            await UpsertPatchStatusAsync(package.Manifest, package.Checksum, "Applying", string.Empty);

            var result = new BisPatchApplyResult
            {
                Manifest = package.Manifest,
                Checksum = package.Checksum
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(package.SchemaSql))
                {
                    await _context.Database.ExecuteSqlRawAsync(package.SchemaSql);
                    result.SchemaApplied = true;
                }

                result.MetadataObjects = await ApplyMetadataAsync(package.MetadataObjects);
                result.Reports = await ApplyReportsAsync(package.Reports);
                result.Modules = await ApplyModulesAsync(package.Modules);
                result.DataRows = await ApplyTableDataAsync(package.TableData);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await UpdatePatchStatusAsync(package.Manifest.PatchId, "Applied", string.Empty);
                await new EventLogService(_context).LogAsync(
                    "ApplyPatch",
                    "Patch",
                    package.Manifest.Name,
                    null,
                    new { package.Manifest.PatchId, package.Manifest.Version, package.Checksum });

                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                await UpdatePatchStatusAsync(package.Manifest.PatchId, "Failed", ex.Message);
                throw;
            }
        }

        public async Task CreatePatchFromFolderAsync(string sourceFolder, string destinationFile)
        {
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException(sourceFolder);

            var manifestPath = Path.Combine(sourceFolder, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("В папке патча отсутствует manifest.json.");

            await using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var sourceStream = File.OpenRead(file);
                    await sourceStream.CopyToAsync(entryStream);
                }
            }

            var encrypted = BisPackageCryptoService.Protect(memory.ToArray(), PatchKind);
            await File.WriteAllBytesAsync(destinationFile, encrypted);
        }

        private async Task<PatchPackageContent> ReadPatchPackageAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Файл патча не найден.", filePath);

            var encryptedBytes = await File.ReadAllBytesAsync(filePath);
            var packageBytes = BisPackageCryptoService.Unprotect(encryptedBytes, PatchKind);
            var checksum = BisPackageCryptoService.ComputeSha256(encryptedBytes);

            await using var memory = new MemoryStream(packageBytes);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            var manifestText = await ReadEntryTextAsync(archive, "manifest.json")
                ?? throw new InvalidOperationException("В патче отсутствует manifest.json.");
            var manifest = JsonSerializer.Deserialize<BisPatchManifest>(manifestText, JsonOptions)
                ?? throw new InvalidOperationException("manifest.json поврежден.");

            return new PatchPackageContent
            {
                Manifest = manifest,
                Checksum = checksum,
                Entries = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToList(),
                SchemaSql = await ReadEntryTextAsync(archive, "schema.sql") ?? string.Empty,
                MetadataObjects = await ReadEntryJsonAsync<List<MetadataObject>>(archive, "metadata.json") ?? new(),
                Reports = await ReadEntryJsonAsync<List<Report>>(archive, "reports.json") ?? new(),
                Modules = await ReadEntryJsonAsync<BisPatchModulesPayload>(archive, "modules.json") ?? new(),
                TableData = await ReadEntryJsonAsync<List<BisPatchTableData>>(archive, "data.json") ?? new()
            };
        }

        private static void ValidateManifest(BisPatchManifest manifest)
        {
            if (!PatchKind.Equals(manifest.Format, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Файл не является патчем BIS.");
            if (string.IsNullOrWhiteSpace(manifest.PatchId))
                throw new InvalidOperationException("В патче не указан PatchId.");
            if (string.IsNullOrWhiteSpace(manifest.Version))
                throw new InvalidOperationException("В патче не указана версия.");
            if (string.IsNullOrWhiteSpace(manifest.Name))
                throw new InvalidOperationException("В патче не указано наименование.");
        }

        private async Task<bool> IsPatchAppliedAsync(string patchId)
        {
            var value = await ExecuteScalarAsync(
                "SELECT COUNT(*) FROM system_patches WHERE patch_id = @patchId AND status = 'Applied';",
                new NpgsqlParameter("@patchId", patchId));
            return Convert.ToInt32(value) > 0;
        }

        private async Task EnsureDependenciesAsync(BisPatchManifest manifest)
        {
            foreach (var dependency in manifest.Dependencies.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!await IsPatchAppliedAsync(dependency))
                    throw new InvalidOperationException($"Не применена зависимость патча: {dependency}.");
            }
        }

        private async Task UpsertPatchStatusAsync(
            BisPatchManifest manifest,
            string checksum,
            string status,
            string error)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                INSERT INTO system_patches
                    (patch_id, version, name, description, checksum, applied_at, applied_by, app_version, status, error, created_at)
                VALUES
                    (@patchId, @version, @name, @description, @checksum, NULL, @appliedBy, @appVersion, @status, @error, CURRENT_TIMESTAMP)
                ON CONFLICT (patch_id) DO UPDATE
                SET version = EXCLUDED.version,
                    name = EXCLUDED.name,
                    description = EXCLUDED.description,
                    checksum = EXCLUDED.checksum,
                    applied_by = EXCLUDED.applied_by,
                    app_version = EXCLUDED.app_version,
                    status = EXCLUDED.status,
                    error = EXCLUDED.error;",
                new NpgsqlParameter("@patchId", manifest.PatchId),
                new NpgsqlParameter("@version", manifest.Version),
                new NpgsqlParameter("@name", manifest.Name),
                new NpgsqlParameter("@description", manifest.Description ?? string.Empty),
                new NpgsqlParameter("@checksum", checksum),
                new NpgsqlParameter("@appliedBy", Environment.UserName),
                new NpgsqlParameter("@appVersion", GetAppVersion()),
                new NpgsqlParameter("@status", status),
                new NpgsqlParameter("@error", error ?? string.Empty));
        }

        private async Task UpdatePatchStatusAsync(string patchId, string status, string error)
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE system_patches
                SET status = @status,
                    error = @error,
                    applied_at = CASE WHEN @status = 'Applied' THEN CURRENT_TIMESTAMP ELSE applied_at END
                WHERE patch_id = @patchId;",
                new NpgsqlParameter("@patchId", patchId),
                new NpgsqlParameter("@status", status),
                new NpgsqlParameter("@error", error ?? string.Empty));
        }

        private async Task<int> ApplyMetadataAsync(List<MetadataObject> metadataObjects)
        {
            if (metadataObjects.Count == 0)
                return 0;

            var metadataService = new MetadataService(_context);
            var applied = new List<MetadataObject>();
            foreach (var incoming in metadataObjects)
            {
                var existing = await _context.MetadataObjects
                    .Include(item => item.Fields)
                    .Include(item => item.Calculations)
                    .Include(item => item.PostingRules)
                    .FirstOrDefaultAsync(item =>
                        item.ObjectType == incoming.ObjectType &&
                        item.Name == incoming.Name);

                if (existing == null)
                {
                    DetachMetadata(incoming);
                    await _context.MetadataObjects.AddAsync(incoming);
                    applied.Add(incoming);
                }
                else
                {
                    CopyMetadataScalarValues(incoming, existing);
                    _context.MetadataFields.RemoveRange(existing.Fields);
                    _context.MetadataCalculations.RemoveRange(existing.Calculations);
                    _context.MetadataPostingRules.RemoveRange(existing.PostingRules);
                    existing.Fields = incoming.Fields.Select(field => CloneMetadataField(field, existing.Id)).ToList();
                    existing.Calculations = incoming.Calculations.Select(calc => CloneMetadataCalculation(calc, existing.Id)).ToList();
                    existing.PostingRules = incoming.PostingRules.Select(rule => CloneMetadataPostingRule(rule, existing.Id)).ToList();
                    applied.Add(existing);
                }
            }

            await _context.SaveChangesAsync();

            foreach (var obj in applied.Where(item => !string.IsNullOrWhiteSpace(item.TableName)))
                await metadataService.CreateDynamicTableAsync(obj);

            return metadataObjects.Count;
        }

        private async Task<int> ApplyReportsAsync(List<Report> reports)
        {
            if (reports.Count == 0)
                return 0;

            await new PrintFormService(_context).EnsureSchemaAsync();
            foreach (var incoming in reports)
            {
                var existing = await _context.Reports
                    .Include(item => item.Fields)
                    .Include(item => item.Filters)
                    .Include(item => item.Groups)
                    .Include(item => item.ElementMappings)
                    .Include(item => item.HeadersFooters)
                    .FirstOrDefaultAsync(item =>
                        (!string.IsNullOrWhiteSpace(incoming.Code) && item.Code == incoming.Code) ||
                        item.Name == incoming.Name);

                if (existing == null)
                {
                    DetachReport(incoming);
                    await _context.Reports.AddAsync(incoming);
                }
                else
                {
                    CopyReportScalarValues(incoming, existing);
                    _context.ReportFields.RemoveRange(existing.Fields);
                    _context.ReportFilters.RemoveRange(existing.Filters);
                    _context.ReportGroups.RemoveRange(existing.Groups);
                    _context.ReportElementMappings.RemoveRange(existing.ElementMappings);
                    _context.Set<ReportHeaderFooter>().RemoveRange(existing.HeadersFooters);
                    existing.Fields = incoming.Fields.Select(field => CloneReportField(field, existing.Id)).ToList();
                    existing.Filters = incoming.Filters.Select(filter => CloneReportFilter(filter, existing.Id)).ToList();
                    existing.Groups = incoming.Groups.Select(group => CloneReportGroup(group, existing.Id)).ToList();
                    existing.ElementMappings = incoming.ElementMappings.Select(mapping => CloneReportElementMapping(mapping, existing.Id)).ToList();
                    existing.HeadersFooters = incoming.HeadersFooters.Select(header => CloneReportHeaderFooter(header, existing.Id)).ToList();
                }
            }

            return reports.Count;
        }

        private async Task<int> ApplyModulesAsync(BisPatchModulesPayload modules)
        {
            if (modules.Modules.Count == 0 && modules.ModuleItems.Count == 0)
                return 0;

            await new ModuleMetadataService(_context).EnsureSchemaAsync();
            var moduleIdMap = new Dictionary<Guid, Guid>();

            foreach (var incoming in modules.Modules)
            {
                var existing = await _context.MetadataModules
                    .FirstOrDefaultAsync(item => item.Code == incoming.Code || item.Name == incoming.Name);
                if (existing == null)
                {
                    await _context.MetadataModules.AddAsync(incoming);
                    moduleIdMap[incoming.Id] = incoming.Id;
                }
                else
                {
                    moduleIdMap[incoming.Id] = existing.Id;
                    existing.Code = incoming.Code;
                    existing.Name = incoming.Name;
                    existing.Description = incoming.Description;
                    existing.Icon = incoming.Icon;
                    existing.Order = incoming.Order;
                    existing.IsActive = incoming.IsActive;
                    existing.IsSystem = incoming.IsSystem;
                }
            }

            await _context.SaveChangesAsync();

            foreach (var incoming in modules.ModuleItems)
            {
                var moduleId = moduleIdMap.TryGetValue(incoming.ModuleId, out var mappedId)
                    ? mappedId
                    : incoming.ModuleId;
                var existing = await _context.MetadataModuleItems.FirstOrDefaultAsync(item =>
                    item.ObjectType == incoming.ObjectType && item.ObjectId == incoming.ObjectId);
                if (existing == null)
                {
                    await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
                    {
                        Id = incoming.Id,
                        ModuleId = moduleId,
                        ObjectId = incoming.ObjectId,
                        ObjectType = incoming.ObjectType,
                        Order = incoming.Order
                    });
                }
                else
                {
                    existing.ModuleId = moduleId;
                    existing.Order = incoming.Order;
                }
            }

            return modules.Modules.Count + modules.ModuleItems.Count;
        }

        private async Task<int> ApplyTableDataAsync(List<BisPatchTableData> tables)
        {
            var total = 0;
            foreach (var table in tables.Where(item => !string.IsNullOrWhiteSpace(item.TableName)))
            {
                if (!await TableExistsAsync(table.TableName))
                    continue;

                if (table.Mode.Equals("Replace", StringComparison.OrdinalIgnoreCase))
                    await _context.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {Quote(table.TableName)};");

                foreach (var row in table.Rows)
                {
                    await UpsertTableRowAsync(table.TableName, row);
                    total++;
                }
            }

            return total;
        }

        private async Task UpsertTableRowAsync(string tableName, Dictionary<string, object?> row)
        {
            if (row.Count == 0)
                return;

            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                if (row.TryGetValue("Id", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id) &&
                    await RowExistsAsync(tableName, id))
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
                    var columns = row.Keys.Where(key => !key.Equals("Id", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (columns.Count == 0)
                        return;

                    update.CommandText =
                        $"UPDATE {Quote(tableName)} SET {string.Join(", ", columns.Select((column, index) => $"{Quote(column)} = @p{index}"))} WHERE \"Id\" = @id";
                    for (var i = 0; i < columns.Count; i++)
                        AddParameter(update, $"@p{i}", NormalizeJsonValue(row[columns[i]]) ?? DBNull.Value);
                    AddParameter(update, "@id", id);
                    await update.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
                    var columns = row.Keys.ToList();
                    insert.CommandText =
                        $"INSERT INTO {Quote(tableName)} ({string.Join(", ", columns.Select(Quote))}) VALUES ({string.Join(", ", columns.Select((_, index) => $"@p{index}"))})";
                    for (var i = 0; i < columns.Count; i++)
                        AddParameter(insert, $"@p{i}", NormalizeJsonValue(row[columns[i]]) ?? DBNull.Value);
                    await insert.ExecuteNonQueryAsync();
                }
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private async Task<bool> RowExistsAsync(string tableName, Guid id)
        {
            var value = await ExecuteScalarAsync(
                $"SELECT COUNT(*) FROM {Quote(tableName)} WHERE \"Id\" = @id;",
                new NpgsqlParameter("@id", id));
            return Convert.ToInt32(value) > 0;
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            var value = await ExecuteScalarAsync(@"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = @tableName
                );",
                new NpgsqlParameter("@tableName", tableName));
            return Convert.ToBoolean(value);
        }

        private async Task<object?> ExecuteScalarAsync(string sql, params DbParameter[] parameters)
        {
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters)
                command.Parameters.Add(parameter);

            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                return await command.ExecuteScalarAsync();
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }
        }

        private static async Task<string?> ReadEntryTextAsync(ZipArchive archive, string entryName)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null)
                return null;

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static async Task<T?> ReadEntryJsonAsync<T>(ZipArchive archive, string entryName)
        {
            var text = await ReadEntryTextAsync(archive, entryName);
            return string.IsNullOrWhiteSpace(text)
                ? default
                : JsonSerializer.Deserialize<T>(text, JsonOptions);
        }

        private static void DetachMetadata(MetadataObject obj)
        {
            obj.MetadataConfig = null;
            foreach (var field in obj.Fields)
                field.MetadataObject = null!;
            foreach (var calc in obj.Calculations)
                calc.MetadataObject = null;
            foreach (var rule in obj.PostingRules)
                rule.MetadataObject = null;
        }

        private static void DetachReport(Report report)
        {
            foreach (var field in report.Fields)
                field.Report = null!;
            foreach (var filter in report.Filters)
                filter.Report = null!;
            foreach (var group in report.Groups)
                group.Report = null!;
            foreach (var mapping in report.ElementMappings)
                mapping.Report = null!;
            foreach (var header in report.HeadersFooters)
                header.Report = null!;
        }

        private static void CopyMetadataScalarValues(MetadataObject source, MetadataObject target)
        {
            target.TableName = source.TableName;
            target.Description = source.Description;
            target.Icon = source.Icon;
            target.Order = source.Order;
            target.IsSystem = source.IsSystem;
            target.ParentId = source.ParentId;
            target.UsePostings = source.UsePostings;
            target.UseBalances = source.UseBalances;
            target.UseMovements = source.UseMovements;
            target.BalanceTable = source.BalanceTable;
            target.MovementTable = source.MovementTable;
            target.ReferenceFields = source.ReferenceFields;
        }

        private static MetadataField CloneMetadataField(MetadataField source, Guid metadataObjectId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            MetadataObjectId = metadataObjectId,
            Name = source.Name,
            DbColumnName = source.DbColumnName,
            FieldType = source.FieldType,
            Length = source.Length,
            Precision = source.Precision,
            Scale = source.Scale,
            IsRequired = source.IsRequired,
            IsUnique = source.IsUnique,
            Order = source.Order,
            ReferenceCatalog = source.ReferenceCatalog,
            Formula = source.Formula,
            DisplayPattern = source.DisplayPattern,
            DisplayFields = source.DisplayFields
        };

        private static MetadataCalculation CloneMetadataCalculation(MetadataCalculation source, Guid metadataObjectId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            MetadataObjectId = metadataObjectId,
            Name = source.Name,
            TargetField = source.TargetField,
            CalculationType = source.CalculationType,
            Formula = source.Formula,
            SourceFields = source.SourceFields,
            IsAuto = source.IsAuto,
            ExecutionOrder = source.ExecutionOrder
        };

        private static MetadataPostingRule CloneMetadataPostingRule(MetadataPostingRule source, Guid metadataObjectId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            MetadataObjectId = metadataObjectId,
            Name = source.Name,
            DebitAccountExpression = source.DebitAccountExpression,
            CreditAccountExpression = source.CreditAccountExpression,
            AmountExpression = source.AmountExpression,
            Condition = source.Condition,
            Order = source.Order
        };

        private static void CopyReportScalarValues(Report source, Report target)
        {
            target.Name = source.Name;
            target.Description = source.Description;
            target.DataSourceType = source.DataSourceType;
            target.DataSourceId = source.DataSourceId;
            target.ReportType = source.ReportType;
            target.Template = source.Template;
            target.Settings = source.Settings;
            target.Icon = source.Icon;
            target.Code = source.Code;
            target.IsActive = source.IsActive;
            target.IsPrintForm = source.IsPrintForm;
            target.IsDefault = source.IsDefault;
            target.SourceFormat = source.SourceFormat;
            target.TemplateVersion = source.TemplateVersion;
            target.Order = source.Order;
            target.UpdatedAt = DateTime.UtcNow;
            target.PageTitle = source.PageTitle;
            target.PageOrientation = source.PageOrientation;
            target.PageWidth = source.PageWidth;
            target.PageHeight = source.PageHeight;
            target.LeftMargin = source.LeftMargin;
            target.RightMargin = source.RightMargin;
            target.TopMargin = source.TopMargin;
            target.BottomMargin = source.BottomMargin;
            target.FontName = source.FontName;
            target.FontSize = source.FontSize;
            target.ShowHeader = source.ShowHeader;
            target.ShowFooter = source.ShowFooter;
            target.ShowPageNumbers = source.ShowPageNumbers;
            target.ShowGridLines = source.ShowGridLines;
            target.AlternateRowColor = source.AlternateRowColor;
            target.HeaderTitle = source.HeaderTitle;
            target.HeaderSubtitle = source.HeaderSubtitle;
            target.HeaderLogo = source.HeaderLogo;
            target.HeaderText = source.HeaderText;
            target.FooterText = source.FooterText;
            target.FooterTotalText = source.FooterTotalText;
            target.FooterSignature = source.FooterSignature;
            target.TitleText = source.TitleText;
            target.SubtitleText = source.SubtitleText;
            target.SummaryText = source.SummaryText;
            target.AlternateRowColors = source.AlternateRowColors;
            target.ShowGrandTotal = source.ShowGrandTotal;
            target.HeaderColor = source.HeaderColor;
        }

        private static ReportField CloneReportField(ReportField source, Guid reportId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            ReportId = reportId,
            FieldName = source.FieldName,
            DisplayName = source.DisplayName,
            AggregateType = source.AggregateType,
            Order = source.Order,
            Width = source.Width,
            Alignment = source.Alignment,
            Format = source.Format,
            IsVisible = source.IsVisible
        };

        private static ReportFilter CloneReportFilter(ReportFilter source, Guid reportId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            ReportId = reportId,
            FieldName = source.FieldName,
            Operation = source.Operation,
            Value = source.Value,
            Value2 = source.Value2,
            Order = source.Order
        };

        private static ReportGroup CloneReportGroup(ReportGroup source, Guid reportId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            ReportId = reportId,
            FieldName = source.FieldName,
            Header = source.Header,
            Footer = source.Footer,
            Order = source.Order,
            ShowHeader = source.ShowHeader,
            ShowFooter = source.ShowFooter,
            PageBreak = source.PageBreak
        };

        private static ReportElementMapping CloneReportElementMapping(ReportElementMapping source, Guid reportId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            ReportId = reportId,
            ElementOrder = source.ElementOrder,
            ElementType = source.ElementType,
            ElementText = source.ElementText,
            ElementExpression = source.ElementExpression,
            BandType = source.BandType,
            Left = source.Left,
            Top = source.Top,
            Width = source.Width,
            Height = source.Height,
            FontName = source.FontName,
            FontSize = source.FontSize,
            Bold = source.Bold,
            Italic = source.Italic,
            Alignment = source.Alignment,
            Order = source.Order,
            MappedFieldName = source.MappedFieldName,
            MappedDisplayName = source.MappedDisplayName,
            DataSource = source.DataSource,
            FormatString = source.FormatString,
            IsVisible = source.IsVisible,
            CustomText = source.CustomText
        };

        private static ReportHeaderFooter CloneReportHeaderFooter(ReportHeaderFooter source, Guid reportId) => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            ReportId = reportId,
            SectionType = source.SectionType,
            Height = source.Height,
            Alignment = source.Alignment,
            FontName = source.FontName,
            FontSize = source.FontSize,
            IsBold = source.IsBold,
            Content = source.Content,
            Order = source.Order
        };

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static object? NormalizeJsonValue(object? value)
        {
            if (value is null or DBNull)
                return null;
            if (value is JsonElement element)
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

            return value;
        }

        private static string Quote(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Пустой идентификатор базы данных.", nameof(identifier));
            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        private static string GetAppVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        private sealed class PatchPackageContent
        {
            public BisPatchManifest Manifest { get; set; } = new();
            public string Checksum { get; set; } = string.Empty;
            public List<string> Entries { get; set; } = new();
            public string SchemaSql { get; set; } = string.Empty;
            public List<MetadataObject> MetadataObjects { get; set; } = new();
            public List<Report> Reports { get; set; } = new();
            public BisPatchModulesPayload Modules { get; set; } = new();
            public List<BisPatchTableData> TableData { get; set; } = new();
        }
    }
}
