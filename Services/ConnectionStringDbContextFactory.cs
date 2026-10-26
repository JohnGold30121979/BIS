using BIS.ERP.Data;
using System;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Фабрика контекста по готовой строке подключения.
    ///
    /// Нужна сервисам, которые должны открыть своё подключение, но не должны
    /// зависеть от ServiceLocator: в тестовом процессе «текущей» инфобазы нет,
    /// и ServiceLocator.InfoBaseManager отдаёт строку без пароля.
    /// </summary>
    public sealed class ConnectionStringDbContextFactory : IAppDbContextFactory
    {
        private readonly string _connectionString;

        public ConnectionStringDbContextFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Не задана строка подключения.", nameof(connectionString));

            _connectionString = connectionString;
        }

        public Task<AppDbContext> CreateAsync() => Task.FromResult(new AppDbContext(_connectionString));
    }
}
