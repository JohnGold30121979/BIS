using System.Windows;
using BIS.ERP.Views;

namespace BIS.ERP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Создаем и показываем окно входа
            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }
    }
}