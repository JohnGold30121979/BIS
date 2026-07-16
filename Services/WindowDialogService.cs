using System.Threading.Tasks;
using System.Windows;
using BIS.ERP.Views;
using BIS.ERP.Models;
using BIS.ERP.Views.Dialogs;

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

        public async Task<bool> ShowLoginAsync()
        {
            var currentInfoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();
            var infoBaseText = currentInfoBase == null
                ? string.Empty
                : $"Инфобаза: {currentInfoBase.Name} ({currentInfoBase.DatabaseName})";

            var loginWindow = new LoginWindow(infoBaseText)
            {
                Owner = _owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            return loginWindow.ShowDialog() == true;
        }

        public bool ShowCreateInfoBase(out string? infoBaseName)
        {
            var dialog = new CreateInfoBaseDialog { Owner = _owner };
            var result = dialog.ShowDialog() == true;
            infoBaseName = result ? dialog.InfoBaseName : null;
            return result;
        }

        public bool ShowEditInfoBase(InfoBase infoBase, out string? infoBaseName, out string? infoBaseIcon)
        {
            var dialog = new EditInfoBaseDialog(infoBase.Name, infoBase.Icon) { Owner = _owner };
            var result = dialog.ShowDialog() == true;
            infoBaseName = result ? dialog.InfoBaseName : null;
            infoBaseIcon = result ? dialog.InfoBaseIcon : null;
            return result;
        }

        public void ShowSetup()
        {
            var setupWindow = new SetupWindow { Owner = _owner };
            setupWindow.ShowDialog();
        }
    }
}
