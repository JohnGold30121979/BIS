using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed class RegulatedReportTemplateService
    {
        private readonly AppDbContext _context;
        private static readonly object SchemaSyncLock = new();
        private static readonly HashSet<string> EnsuredSchemaKeys = new(StringComparer.OrdinalIgnoreCase);

        public RegulatedReportTemplateService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            var schemaKey = _context.Database.GetConnectionString() ?? "default";
            lock (SchemaSyncLock)
            {
                if (EnsuredSchemaKeys.Contains(schemaKey))
                    return;
            }

            const string sql = @"
                CREATE TABLE IF NOT EXISTS ""RegulatedReportTemplates"" (
                    ""Id"" uuid NOT NULL,
                    ""Code"" varchar(80) NOT NULL,
                    ""Name"" varchar(200) NOT NULL,
                    ""Version"" varchar(60) NOT NULL DEFAULT '',
                    ""OriginalFileName"" varchar(260) NOT NULL DEFAULT '',
                    ""FileExtension"" varchar(20) NOT NULL DEFAULT '.xlsx',
                    ""MimeType"" varchar(150) NOT NULL DEFAULT 'application/octet-stream',
                    ""TemplateData"" bytea NOT NULL,
                    ""Description"" text NOT NULL DEFAULT '',
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""EffectiveFrom"" timestamp NULL,
                    ""Sha256"" varchar(64) NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT ""PK_RegulatedReportTemplates"" PRIMARY KEY (""Id"")
                );
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""Name"" varchar(200) NOT NULL DEFAULT '';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""Version"" varchar(60) NOT NULL DEFAULT '';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""OriginalFileName"" varchar(260) NOT NULL DEFAULT '';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""FileExtension"" varchar(20) NOT NULL DEFAULT '.xlsx';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""MimeType"" varchar(150) NOT NULL DEFAULT 'application/octet-stream';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""TemplateData"" bytea NOT NULL DEFAULT decode('', 'hex');
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""Description"" text NOT NULL DEFAULT '';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""EffectiveFrom"" timestamp NULL;
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""Sha256"" varchar(64) NOT NULL DEFAULT '';
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE ""RegulatedReportTemplates"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RegulatedReportTemplates_Code_Version""
                    ON ""RegulatedReportTemplates"" (""Code"", ""Version"");
                CREATE INDEX IF NOT EXISTS ""IX_RegulatedReportTemplates_Code_IsActive""
                    ON ""RegulatedReportTemplates"" (""Code"", ""IsActive"");
                CREATE INDEX IF NOT EXISTS ""IX_RegulatedReportTemplates_UpdatedAt""
                    ON ""RegulatedReportTemplates"" (""UpdatedAt"");";

            await _context.Database.ExecuteSqlRawAsync(sql);
            lock (SchemaSyncLock)
            {
                EnsuredSchemaKeys.Add(schemaKey);
            }
        }

        public async Task<List<RegulatedReportTemplate>> GetTemplatesAsync()
        {
            await EnsureSchemaAsync();
            return await _context.RegulatedReportTemplates.AsNoTracking()
                .OrderBy(template => template.Code)
                .ThenByDescending(template => template.IsActive)
                .ThenByDescending(template => template.UpdatedAt)
                .ToListAsync();
        }

        public async Task<RegulatedReportTemplate?> GetTemplateAsync(Guid id)
        {
            await EnsureSchemaAsync();
            return await _context.RegulatedReportTemplates.FirstOrDefaultAsync(template => template.Id == id);
        }

        public async Task<RegulatedReportTemplate?> GetActiveTemplateAsync(string code)
        {
            await EnsureSchemaAsync();
            var normalizedCode = NormalizeCode(code);
            return await _context.RegulatedReportTemplates.AsNoTracking()
                .Where(template => template.Code == normalizedCode && template.IsActive)
                .OrderByDescending(template => template.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<RegulatedReportTemplate> SaveTemplateAsync(
            string filePath,
            RegulatedReportTemplateDraft draft)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Файл шаблона не найден.", filePath);

            await EnsureSchemaAsync();

            var fileBytes = await File.ReadAllBytesAsync(filePath);
            if (fileBytes.Length == 0)
                throw new InvalidOperationException("Файл шаблона пустой.");

            var code = NormalizeCode(draft.Code);
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Код шаблона обязателен.");

            var version = NormalizeText(draft.Version);
            var existing = await _context.RegulatedReportTemplates
                .FirstOrDefaultAsync(template => template.Code == code && template.Version == version);
            var now = DateTime.UtcNow;
            var fileName = Path.GetFileName(filePath);
            var extension = NormalizeExtension(Path.GetExtension(fileName));

            var template = existing ?? new RegulatedReportTemplate
            {
                Id = Guid.NewGuid(),
                Code = code,
                Version = version,
                CreatedAt = now
            };

            template.Code = code;
            template.Name = NormalizeText(draft.Name);
            template.Version = version;
            template.Description = NormalizeText(draft.Description);
            template.OriginalFileName = fileName;
            template.FileExtension = extension;
            template.MimeType = GetMimeType(extension);
            template.TemplateData = fileBytes;
            template.IsActive = draft.IsActive;
            template.Sha256 = BisPackageCryptoService.ComputeSha256(fileBytes);
            template.UpdatedAt = now;

            if (string.IsNullOrWhiteSpace(template.Name))
                template.Name = code;

            if (existing == null)
                await _context.RegulatedReportTemplates.AddAsync(template);

            if (template.IsActive)
                await DeactivateOtherVersionsAsync(template.Code, template.Id);

            await _context.SaveChangesAsync();
            return template;
        }

        public async Task UpdateTemplateMetadataAsync(
            Guid id,
            RegulatedReportTemplateDraft draft)
        {
            await EnsureSchemaAsync();
            var template = await _context.RegulatedReportTemplates.FirstOrDefaultAsync(item => item.Id == id)
                ?? throw new InvalidOperationException("Шаблон не найден.");

            var newCode = NormalizeCode(draft.Code);
            var newVersion = NormalizeText(draft.Version);
            if (string.IsNullOrWhiteSpace(newCode))
                throw new InvalidOperationException("Код шаблона обязателен.");

            var duplicate = await _context.RegulatedReportTemplates
                .AnyAsync(item => item.Id != id && item.Code == newCode && item.Version == newVersion);
            if (duplicate)
                throw new InvalidOperationException("Шаблон с таким кодом и версией уже существует.");

            template.Code = newCode;
            template.Name = string.IsNullOrWhiteSpace(draft.Name) ? newCode : NormalizeText(draft.Name);
            template.Version = newVersion;
            template.Description = NormalizeText(draft.Description);
            template.IsActive = draft.IsActive;
            template.UpdatedAt = DateTime.UtcNow;

            if (template.IsActive)
                await DeactivateOtherVersionsAsync(template.Code, template.Id);

            await _context.SaveChangesAsync();
        }

        public async Task SetActiveTemplateAsync(Guid id)
        {
            await EnsureSchemaAsync();
            var template = await _context.RegulatedReportTemplates.FirstOrDefaultAsync(item => item.Id == id)
                ?? throw new InvalidOperationException("Шаблон не найден.");

            template.IsActive = true;
            template.UpdatedAt = DateTime.UtcNow;
            await DeactivateOtherVersionsAsync(template.Code, template.Id);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTemplateAsync(Guid id)
        {
            await EnsureSchemaAsync();
            var template = await _context.RegulatedReportTemplates.FirstOrDefaultAsync(item => item.Id == id);
            if (template == null)
                return;

            _context.RegulatedReportTemplates.Remove(template);
            await _context.SaveChangesAsync();
        }

        public async Task ExportTemplateCopyAsync(Guid id, string outputPath)
        {
            await EnsureSchemaAsync();
            var template = await _context.RegulatedReportTemplates.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id)
                ?? throw new InvalidOperationException("Шаблон не найден.");

            if (template.TemplateData.Length == 0)
                throw new InvalidOperationException("Для выбранного шаблона отсутствуют бинарные данные.");

            await File.WriteAllBytesAsync(outputPath, template.TemplateData);
        }

        public static RegulatedReportTemplateDraft InferDraftFromFileName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var normalizedStem = (stem ?? string.Empty).Trim();
            var lowerStem = normalizedStem.ToLowerInvariant();

            if (lowerStem.Contains("sti062", StringComparison.Ordinal) ||
                lowerStem.Contains("formsti062", StringComparison.Ordinal))
            {
                var versionMatch = Regex.Match(lowerStem, @"(?:_|-)(\d{4,6})$");
                return new RegulatedReportTemplateDraft
                {
                    Code = "STI-062_7",
                    Name = "Налоговый отчет по НДС",
                    Version = versionMatch.Success ? versionMatch.Groups[1].Value : string.Empty,
                    Description = "Официальный шаблон Excel для формы STI-062_7 по НДС.",
                    IsActive = true
                };
            }

            var fallbackVersion = Regex.Match(lowerStem, @"(?:_|-)(\d{4,8})$");
            return new RegulatedReportTemplateDraft
            {
                Code = NormalizeCode(normalizedStem),
                Name = HumanizeName(normalizedStem),
                Version = fallbackVersion.Success ? fallbackVersion.Groups[1].Value : string.Empty,
                Description = string.Empty,
                IsActive = true
            };
        }

        private async Task DeactivateOtherVersionsAsync(string code, Guid currentId)
        {
            var others = await _context.RegulatedReportTemplates
                .Where(item => item.Code == code && item.Id != currentId && item.IsActive)
                .ToListAsync();

            foreach (var other in others)
            {
                other.IsActive = false;
                other.UpdatedAt = DateTime.UtcNow;
            }
        }

        private static string NormalizeCode(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return ".xlsx";
            return extension.StartsWith(".", StringComparison.Ordinal) ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        }

        private static string HumanizeName(string value)
        {
            var name = Regex.Replace(value ?? string.Empty, @"[_\-]+", " ").Trim();
            return string.IsNullOrWhiteSpace(name) ? "Регламентированный шаблон" : name;
        }

        private static string GetMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => "application/octet-stream"
            };
        }
    }
}
