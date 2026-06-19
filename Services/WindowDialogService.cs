using System.Threading.Tasks;
using System.Windows;
using BIS.ERP.Views;

namespace BIS.ERP.Services
{
    public class WindowDialogService : IDialogService
    {
        private readonly Window _owner;

        public WindowDialogService(Window owner)
        {
            _owner = owner;
        }

        public void ShowInformation(string message, string title = "Информация")
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string message, string title = "Внимание")
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string message, string title = "Ошибка")
        {
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title = "Подтверждение")
        {
            return MessageBox.Show(_owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public Task<bool> ShowLoginAsync()
        {
            var loginWindow = new LoginWindow
            {
                Owner = _owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            return Task.FromResult(loginWindow.ShowDialog() == true);
        }

        public bool ShowRegister()
        {
            var registerWindow = new RegisterWindow
            {
                Owner = _owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            return registerWindow.ShowDialog() == true;
        }

        public bool ShowCreateInfoBase(out string? infoBaseName)
        {
            var dialog = new CreateInfoBaseDialog { Owner = _owner };
            var result = dialog.ShowDialog() == true;
            infoBaseName = result ? dialog.InfoBaseName : null;
            return result;
        }

        public void ShowSetup()
        {
            var setupWindow = new SetupWindow { Owner = _owner };
            setupWindow.ShowDialog();
        }
    }
}
