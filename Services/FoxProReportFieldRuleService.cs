using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    public sealed class FoxProReportFieldRuleService
    {
        private readonly AppDbContext _context;

        public FoxProReportFieldRuleService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureSchemaAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""FoxProReportFieldRules"" (
                    ""Id"" uuid NOT NULL,
                    ""ProfileCode"" varchar(120) NOT NULL DEFAULT '',
                    ""SourcePattern"" varchar(300) NOT NULL DEFAULT '',
                    ""CanonicalField"" varchar(120) NOT NULL DEFAULT '',
                    ""TargetFieldName"" varchar(300) NOT NULL DEFAULT '',
                    ""TargetDisplayName"" varchar(300) NOT NULL DEFAULT '',
                    ""IsRegex"" boolean NOT NULL DEFAULT false,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""Priority"" integer NOT NULL DEFAULT 100,
                    ""Description"" text NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""PK_FoxProReportFieldRules"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_FoxProReportFieldRules_SourcePattern""
                    ON ""FoxProReportFieldRules"" (""SourcePattern"");
                CREATE INDEX IF NOT EXISTS ""IX_FoxProReportFieldRules_ProfileCode_IsActive""
                    ON ""FoxProReportFieldRules"" (""ProfileCode"", ""IsActive"");");
        }

        public async Task<List<FoxProReportFieldRule>> GetRulesAsync(bool includeInactive = true)
        {
            await EnsureSchemaAsync();
            var query = _context.FoxProReportFieldRules.AsNoTracking();
            if (!includeInactive)
                query = query.Where(rule => rule.IsActive);

            return await query
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.SourcePattern)
                .ToListAsync();
        }

        public IReadOnlyList<FoxProReportFieldRule> GetActiveRulesSafe()
        {
            try
            {
                EnsureSchemaAsync().GetAwaiter().GetResult();
                return _context.FoxProReportFieldRules
                    .AsNoTracking()
                    .Where(rule => rule.IsActive)
                    .OrderBy(rule => rule.Priority)
                    .ThenBy(rule => rule.SourcePattern)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FoxPro rule loading skipped: {ex.Message}");
                return Array.Empty<FoxProReportFieldRule>();
            }
        }

        public async Task SeedDefaultRulesAsync()
        {
            await EnsureSchemaAsync();

            var defaults = FoxProReportKnowledgeBase.GetDefaultRules()
                .Where(rule => !string.IsNullOrWhiteSpace(rule.SourcePattern))
                .ToList();
            if (defaults.Count == 0)
                return;

            var existing = await _context.FoxProReportFieldRules.ToListAsync();
            var changed = false;

            foreach (var defaultRule in defaults)
            {
                var current = existing.FirstOrDefault(rule => AreSameRuleSource(rule, defaultRule));
                if (current == null)
                {
                    var rule = CloneForSave(defaultRule);
                    rule.Id = Guid.NewGuid();
                    rule.CreatedAt = DateTime.UtcNow;
                    rule.UpdatedAt = DateTime.UtcNow;
                    _context.FoxProReportFieldRules.Add(rule);
                    existing.Add(rule);
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(current.CanonicalField))
                {
                    current.CanonicalField = defaultRule.CanonicalField;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(current.TargetFieldName))
                {
                    current.TargetFieldName = defaultRule.TargetFieldName;
                    current.TargetDisplayName = defaultRule.TargetDisplayName;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(current.TargetDisplayName) && !string.IsNullOrWhiteSpace(defaultRule.TargetDisplayName))
                {
                    current.TargetDisplayName = defaultRule.TargetDisplayName;
                    changed = true;
                }

                if (current.Priority <= 0)
                {
                    current.Priority = defaultRule.Priority;
                    changed = true;
                }
            }

            if (changed)
                await _context.SaveChangesAsync();
        }

        private static bool AreSameRuleSource(FoxProReportFieldRule left, FoxProReportFieldRule right)
        {
            if (left.IsRegex || right.IsRegex)
                return left.IsRegex == right.IsRegex &&
                       left.SourcePattern.Equals(right.SourcePattern, StringComparison.OrdinalIgnoreCase);

            return FoxProReportKnowledgeBase.NormalizeLookupKey(left.SourcePattern) ==
                   FoxProReportKnowledgeBase.NormalizeLookupKey(right.SourcePattern);
        }
        public async Task SaveRulesAsync(IEnumerable<FoxProReportFieldRule> rules)
        {
            await EnsureSchemaAsync();

            var incoming = rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.SourcePattern))
                .Select(CloneForSave)
                .ToList();

            var existing = await _context.FoxProReportFieldRules.ToListAsync();
            var incomingIds = incoming.Select(rule => rule.Id).ToHashSet();
            _context.FoxProReportFieldRules.RemoveRange(existing.Where(rule => !incomingIds.Contains(rule.Id)));

            foreach (var rule in incoming)
            {
                var current = existing.FirstOrDefault(item => item.Id == rule.Id);
                if (current == null)
                {
                    rule.CreatedAt = DateTime.UtcNow;
                    rule.UpdatedAt = DateTime.UtcNow;
                    _context.FoxProReportFieldRules.Add(rule);
                    continue;
                }

                current.ProfileCode = rule.ProfileCode;
                current.SourcePattern = rule.SourcePattern;
                current.CanonicalField = rule.CanonicalField;
                current.TargetFieldName = rule.TargetFieldName;
                current.TargetDisplayName = rule.TargetDisplayName;
                current.IsRegex = rule.IsRegex;
                current.IsActive = rule.IsActive;
                current.Priority = rule.Priority;
                current.Description = rule.Description;
                current.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        private static FoxProReportFieldRule CloneForSave(FoxProReportFieldRule source)
        {
            return new FoxProReportFieldRule
            {
                Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
                ProfileCode = (source.ProfileCode ?? string.Empty).Trim(),
                SourcePattern = FoxProReportKnowledgeBase.NormalizeRuleSource(source.SourcePattern),
                CanonicalField = (source.CanonicalField ?? string.Empty).Trim(),
                TargetFieldName = (source.TargetFieldName ?? string.Empty).Trim(),
                TargetDisplayName = (source.TargetDisplayName ?? string.Empty).Trim(),
                IsRegex = source.IsRegex,
                IsActive = source.IsActive,
                Priority = source.Priority,
                Description = (source.Description ?? string.Empty).Trim(),
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };
        }
    }
}
