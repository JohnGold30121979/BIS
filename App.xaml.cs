using System.Windows;
using BIS.ERP.Services;
using BIS.ERP.Views;

namespace BIS.ERP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Загружаем настройки
            var settings = AppSettings.Instance;

            // Проверяем подключение
            bool needSetup = string.IsNullOrEmpty(settings.Password) || !settings.TestConnection();

            if (needSetup)
            {
                var setupWindow = new SetupWindow();
                setupWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                // Если настройки не сохранены или пользователь закрыл окно - выходим
                if (setupWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // После сохранения настроек, перезагружаем их
                settings = AppSettings.Instance;

                // Проверяем еще раз
                if (!settings.TestConnection())
                {
                    MessageBox.Show("Не удалось подключиться к PostgreSQL. Проверьте параметры подключения.",
                        "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }
            }
        
            var infoBaseWindow = new InfoBaseSelectionWindow();
            infoBaseWindow.Show();
        }
    }
}