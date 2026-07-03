using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class SystemConfigurationService
    {
        private readonly AppDbContext _context;

        public SystemConfigurationService(AppDbContext? context = null)
        {
            _context = context ?? new AppDbContext();
        }

        public async Task<SystemConfiguration> GetAsync()
        {
            await EnsureSchemaAsync();
            var configuration = await _context.SystemConfigurations.FirstOrDefaultAsync();
            if (configuration != null)
                return configuration;

            configuration = new SystemConfiguration();
            await _context.SystemConfigurations.AddAsync(configuration);
            await _context.SaveChangesAsync();
            return configuration;
        }

        public async Task SaveAsync(
            string systemName,
            string icon,
            string description,
            string companyDetails,
            string email,
            string phone,
            string appUpdateUrl)
        {
            var configuration = await GetAsync();
            configuration.SystemName = string.IsNullOrWhiteSpace(systemName) ? "BIS ERP" : systemName.Trim();
            configuration.Icon = string.IsNullOrWhiteSpace(icon) ? "🏢" : icon.Trim();
            configuration.Description = description?.Trim() ?? string.Empty;
            configuration.CompanyDetails = companyDetails?.Trim() ?? string.Empty;
            configuration.Email = email?.Trim() ?? string.Empty;
            configuration.Phone = phone?.Trim() ?? string.Empty;
            configuration.AppUpdateUrl = appUpdateUrl?.Trim() ?? string.Empty;
            configuration.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private Task EnsureSchemaAsync() => _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""SystemConfigurations"" (
                ""Id"" uuid PRIMARY KEY,
                ""SystemName"" varchar(120) NOT NULL,
                ""Icon"" varchar(20) NOT NULL,
                ""Description"" varchar(1000) NOT NULL DEFAULT '',
                ""CompanyDetails"" varchar(2000) NOT NULL DEFAULT '',
                ""Email"" varchar(160) NOT NULL DEFAULT '',
                ""Phone"" varchar(80) NOT NULL DEFAULT '',
                ""AppUpdateUrl"" varchar(1000) NOT NULL DEFAULT '',
                ""UpdatedAt"" timestamp with time zone NOT NULL
            );
            ALTER TABLE ""SystemConfigurations"" ADD COLUMN IF NOT EXISTS ""Description"" varchar(1000) NOT NULL DEFAULT '';
            ALTER TABLE ""SystemConfigurations"" ADD COLUMN IF NOT EXISTS ""CompanyDetails"" varchar(2000) NOT NULL DEFAULT '';
            ALTER TABLE ""SystemConfigurations"" ADD COLUMN IF NOT EXISTS ""Email"" varchar(160) NOT NULL DEFAULT '';
            ALTER TABLE ""SystemConfigurations"" ADD COLUMN IF NOT EXISTS ""Phone"" varchar(80) NOT NULL DEFAULT '';
            ALTER TABLE ""SystemConfigurations"" ADD COLUMN IF NOT EXISTS ""AppUpdateUrl"" varchar(1000) NOT NULL DEFAULT '';");
    }
}
