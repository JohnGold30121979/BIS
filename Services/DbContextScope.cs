using System.Threading;


namespace BIS.ERP.Services
{
    internal static class DbContextScope
    {
        internal static SemaphoreSlim StartupGate { get; } = new(1, 1);
    }
}
