using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BIS.ERP.Data
{
    /// <summary>
    /// Нужна только инструментам EF (`dotnet ef migrations add`, `database update`):
    /// приложение создаёт контекст через InfoBaseManager со строкой подключения инфобазы,
    /// поэтому здесь берём строку из переменной BIS_DESIGN_CONNECTION или из appsettings.json.
    /// </summary>
    public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("BIS_DESIGN_CONNECTION");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = AppSettings.Instance.GetMasterConnectionString();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new AppDbContext(options);
        }
    }
}
