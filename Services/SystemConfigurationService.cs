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

        public async Task SaveAsync(string systemName, string icon)
        {
            var configuration = await GetAsync();
            configuration.SystemName = string.IsNullOrWhiteSpace(systemName) ? "BIS ERP" : systemName.Trim();
            configuration.Icon = string.IsNullOrWhiteSpace(icon) ? "🏢" : icon.Trim();
            configuration.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private Task EnsureSchemaAsync() => _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""SystemConfigurations"" (
                ""Id"" uuid PRIMARY KEY,
                ""SystemName"" varchar(120) NOT NULL,
                ""Icon"" varchar(20) NOT NULL,
                ""UpdatedAt"" timestamp with time zone NOT NULL
            );");
    }
}
