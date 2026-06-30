using System.Windows;

namespace BIS.ERP.Services
{
    public static class ApplicationExitService
    {
        private static bool _isShuttingDown;

        public static bool IsShuttingDown => _isShuttingDown;

        public static bool ConfirmAndShutdown(Window? owner = null)
        {
            if (_isShuttingDown)
                return true;

            var result = owner == null
                ? MessageBox.Show(
                    "Завершить работу приложения?",
                    "Выход",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question)
                : MessageBox.Show(
                    owner,
                    "Завершить работу приложения?",
                    "Выход",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return false;

            ShutdownNow();
            return true;
        }

        public static void ShutdownNow()
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;
            Application.Current?.Shutdown();
        }
    }
}
