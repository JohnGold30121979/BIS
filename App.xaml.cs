using BIS.ERP.Behaviors;
using BIS.ERP.Services;
using BIS.ERP.Views;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BIS.ERP
{
    public partial class App : Application
    {
        private static bool _systemLoggingConfigured;
        private TrayManager? _trayManager;
        private InfoBaseSelectionWindow? _infoBaseWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ConfigureSystemLogging();
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
                SystemLogService.Error("Критическая ошибка при запуске приложения.", "App.OnStartup", ex);
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
                SystemLogService.Warning("Ошибка инициализации трея. Работа продолжена без трея.", "App.InitializeTrayManager", ex);
                System.Diagnostics.Debug.WriteLine($"Ошибка инициализации трея: {ex.Message}");
                // Если трей не инициализировался - продолжаем работу без него
            }
        }

        private void ConfigureSystemLogging()
        {
            if (_systemLoggingConfigured)
            {
                return;
            }

            _systemLoggingConfigured = true;

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AddPresentationTraceListener(PresentationTraceSources.DataBindingSource, "WPF Binding");

            SystemLogService.Info("Приложение запущено. Основной системный лог подключен.", "App");
        }

        private static void AddPresentationTraceListener(TraceSource source, string listenerName)
        {
            if (!source.Listeners.OfType<SystemLogTraceListener>().Any(listener => listener.Name == listenerName))
            {
                source.Listeners.Add(new SystemLogTraceListener(listenerName)
                {
                    Name = listenerName
                });
            }

            source.Switch.Level = SourceLevels.Warning;
        }

        private static void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                SystemLogService.Error("Необработанное исключение домена приложения.", "AppDomain", ex);
            }
            else
            {
                SystemLogService.Error($"Необработанное исключение домена приложения: {e.ExceptionObject}", "AppDomain");
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            SystemLogService.Error("Необработанное исключение фоновой задачи.", "TaskScheduler", e.Exception);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            SystemLogService.Error("Необработанное исключение UI-потока.", "Dispatcher", e.Exception);
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
                SystemLogService.Warning("Ошибка при освобождении ресурсов приложения.", "App.OnExit", ex);
                System.Diagnostics.Debug.WriteLine($"Ошибка при освобождении ресурсов: {ex.Message}");
            }

            base.OnExit(e);
        }
    }
}
