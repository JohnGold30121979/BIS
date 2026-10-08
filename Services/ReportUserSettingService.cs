using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Последние введённые пользователем значения параметров отчёта.
    /// Схема правится вручную (CREATE TABLE IF NOT EXISTS), как и для остальных
    /// сервисов проекта — миграции EF здесь не используются.
    /// </summary>
    public sealed class ReportUserSettingService
    {
        private readonly AppDbContext _context;

        public ReportUserSettingService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Схема таблиц параметров отчёта и пользовательских значений.
        /// База создаётся через EnsureCreatedAsync(), который новые таблицы не добавляет,
        /// поэтому обе таблицы готовятся здесь.
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS ""ReportParameters"" (
                    ""Id"" uuid NOT NULL,
                    ""ReportId"" uuid NOT NULL,
                    ""Name"" varchar(120) NOT NULL DEFAULT '',
                    ""Title"" varchar(200) NOT NULL DEFAULT '',
                    ""ParameterType"" varchar(40) NOT NULL DEFAULT 'Date',
                    ""DefaultValue"" varchar(300) NOT NULL DEFAULT '',
                    ""IsRequired"" boolean NOT NULL DEFAULT true,
                    ""ReferenceSource"" varchar(200) NOT NULL DEFAULT '',
                    ""Order"" integer NOT NULL DEFAULT 0,
                    CONSTRAINT ""PK_ReportParameters"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReportParameters_ReportId_Name""
                    ON ""ReportParameters"" (""ReportId"", ""Name"");

                ALTER TABLE ""ReportParameters""
                    ADD COLUMN IF NOT EXISTS ""ReferenceSource"" varchar(200) NOT NULL DEFAULT '';

                ALTER TABLE ""ReportFilters""
                    ADD COLUMN IF NOT EXISTS ""IsOptional"" boolean NOT NULL DEFAULT false;

                CREATE TABLE IF NOT EXISTS ""ReportUserSettings"" (
                    ""Id"" uuid NOT NULL,
                    ""ReportId"" uuid NOT NULL,
                    ""UserLogin"" varchar(120) NOT NULL DEFAULT '',
                    ""ParameterName"" varchar(120) NOT NULL DEFAULT '',
                    ""Value"" varchar(500) NOT NULL DEFAULT '',
                    CONSTRAINT ""PK_ReportUserSettings"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReportUserSettings_Key""
                    ON ""ReportUserSettings"" (""ReportId"", ""UserLogin"", ""ParameterName"");
            ";

            await _context.Database.ExecuteSqlRawAsync(sql);
        }

        /// <summary>Сохранённые значения параметров отчёта для пользователя.</summary>
        public async Task<Dictionary<string, string>> GetValuesAsync(Guid reportId, string? userLogin)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(userLogin))
                return result;

            await EnsureSchemaAsync();

            var rows = await _context.ReportUserSettings
                .AsNoTracking()
                .Where(setting => setting.ReportId == reportId && setting.UserLogin == userLogin)
                .ToListAsync();

            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.ParameterName))
                    result[row.ParameterName] = row.Value ?? string.Empty;
            }

            return result;
        }

        /// <summary>Запоминает введённые значения. Пустые значения удаляются.</summary>
        public async Task SaveValuesAsync(
            Guid reportId,
            string? userLogin,
            IReadOnlyDictionary<string, object> values)
        {
            if (string.IsNullOrWhiteSpace(userLogin) || values.Count == 0)
                return;

            await EnsureSchemaAsync();

            var existing = await _context.ReportUserSettings
                .Where(setting => setting.ReportId == reportId && setting.UserLogin == userLogin)
                .ToListAsync();

            foreach (var pair in values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                var text = ReportParameterResolver.ToStorageText(pair.Value);
                var row = existing.FirstOrDefault(setting =>
                    string.Equals(setting.ParameterName, pair.Key, StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(text))
                {
                    if (row != null)
                        _context.ReportUserSettings.Remove(row);
                    continue;
                }

                if (row == null)
                {
                    _context.ReportUserSettings.Add(new ReportUserSetting
                    {
                        Id = Guid.NewGuid(),
                        ReportId = reportId,
                        UserLogin = userLogin,
                        ParameterName = pair.Key,
                        Value = text
                    });
                }
                else
                {
                    row.Value = text;
                }
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>Забывает сохранённые значения отчёта для пользователя.</summary>
        public async Task ResetAsync(Guid reportId, string? userLogin)
        {
            if (string.IsNullOrWhiteSpace(userLogin))
                return;

            await EnsureSchemaAsync();

            await _context.ReportUserSettings
                .Where(setting => setting.ReportId == reportId && setting.UserLogin == userLogin)
                .ExecuteDeleteAsync();
        }
    }
}
