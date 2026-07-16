using BIS.ERP.Behaviors;
using BIS.ERP.Services;
using BIS.ERP.Views;
using System.Text;
using System.Windows;

namespace BIS.ERP
{
    public partial class App : Application
    {
        private TrayManager? _trayManager;
        private InfoBaseSelectionWindow? _infoBaseWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // Инициализация поведения навигации
                EnterKeyNavigationBehavior.Initialize();
                ResponsiveWindowBehavior.Initialize();

                // Регистрируем кодировки для поддержки CP866 (DOS кириллица)
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Загружаем настройки
                var settings = AppSettings.Instance;

                ThemeService.Initialize();
                ThemeService.Apply(settings?.Theme ?? ThemeService.DefaultTheme);

                // Проверяем подключение
                bool needSetup = !settings.TestConnection();

                if (needSetup)
                {
                    var setupWindow = new SetupWindow();
                    setupWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                    if (setupWindow.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }

                    // Перезагружаем настройки после сохранения
                    settings = AppSettings.Instance;

                    if (!settings.TestConnection())
                    {
                        MessageBox.Show(
                            "Не удалось подключиться к PostgreSQL. Проверьте параметры подключения.",
                            "Ошибка подключения",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        Shutdown();
                        return;
                    }
                }

                // Создаем главное окно
                _infoBaseWindow = new InfoBaseSelectionWindow();

                // ✅ Инициализация трея с привязкой к главному окну
                InitializeTrayManager(_infoBaseWindow);

                // Показываем главное окно
                _infoBaseWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Критическая ошибка при запуске приложения:\n\n{ex.Message}",
                    "Ошибка запуска",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Инициализация менеджера трея
        /// </summary>
        private void InitializeTrayManager(InfoBaseSelectionWindow mainWindow)
        {
            try
            {
                _trayManager = new TrayManager(mainWindow);

                // Подписываемся на событие выхода из трея
                _trayManager.ExitRequested += OnTrayExitRequested;

                // ✅ Подписываемся на событие показа (опционально)
                _trayManager.ShowRequested += OnTrayShowRequested;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка инициализации трея: {ex.Message}");
                // Если трей не инициализировался - продолжаем работу без него
            }
        }

        /// <summary>
        /// Обработчик закрытия главного окна
        /// </summary>
        private void OnMainWindowClosed(object? sender, EventArgs e)
        {
            // Если окно закрывается не через трей - сворачиваем в трей
            if (_trayManager != null && _infoBaseWindow != null)
            {
                // Предотвращаем полное закрытие
                _infoBaseWindow.Hide();

                // Показываем уведомление в трее
              //  _trayManager.ShowBalloonTip("Приложение свернуто в трей", ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// Обработчик запроса выхода из трея
        /// </summary>
        private void OnTrayExitRequested(object? sender, EventArgs e)
        {
            // Спрашиваем подтверждение
            var result = MessageBox.Show(
                "Вы уверены, что хотите выйти из приложения?",
                "Подтверждение выхода",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _trayManager?.Dispose();
                _trayManager = null;

                ApplicationExitService.ShutdownNow();
            }
        }

        /// <summary>
        /// Обработчик запроса показа окна из трея
        /// </summary>
        private void OnTrayShowRequested(object? sender, EventArgs e)
        {
            if (_infoBaseWindow != null)
            {
                _infoBaseWindow.Show();
                _infoBaseWindow.WindowState = WindowState.Normal;
                _infoBaseWindow.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Освобождаем ресурсы
            try
            {
                _trayManager?.Dispose();
                _trayManager = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при освобождении ресурсов: {ex.Message}");
            }

            base.OnExit(e);
        }
    }
}
