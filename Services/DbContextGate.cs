using System;
using System.Threading;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Общие ворота доступа к контексту базы.
    ///
    /// Контекст в приложении один на всё приложение, а ни DbContext, ни соединение Npgsql
    /// не переносят параллельных команд: вторая операция падает с «A second operation was
    /// started on this context instance…» или «A command is already in progress…».
    ///
    /// Поэтому длинные последовательности запросов — загрузка окна, запись карточки —
    /// проходят через одни ворота: вторая операция ждёт первую. Короткие одиночные
    /// запросы можно не запирать, но если они идут вперемешку с загрузкой, лучше звать
    /// и их через эти же ворота.
    /// </summary>
    public static class DbContextGate
    {
        /// <summary>
        /// Общий семафор. Открыт наружу: сервисы, у которых уже есть своя блокировка
        /// (например MetadataService), ссылаются на него, чтобы координироваться
        /// с окнами и диалогами.
        /// </summary>
        public static SemaphoreSlim Gate { get; } = new(1, 1);

        public static async Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            await Gate.WaitAsync();
            try
            {
                return await action();
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task RunAsync(Func<Task> action)
        {
            await Gate.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
