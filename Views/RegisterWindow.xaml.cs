using System.Windows;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow(string selectedInfoBaseText = "")
        {
            InitializeComponent();

            var viewModel = new RegisterViewModel(ServiceLocator.AuthService, selectedInfoBaseText);
            viewModel.RegisterSucceeded += (_, _) =>
            {
                DialogResult = true;
                Close();
            };
            viewModel.CloseRequested += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            DataContext = viewModel;
            Loaded += (_, _) => LoginBox.Focus();
        }

        private void OnLoginPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;
            FullNameBox.Focus();
        }

        private void OnFullNamePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;
            EmailBox.Focus();
        }

        private void OnEmailPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;
            RoleBox.Focus();
        }

        private void OnRolePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;
            PasswordBox.Focus();
        }

        private void OnPasswordPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;
            ConfirmPasswordBox.Focus();
        }

        private void OnConfirmPasswordPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!HandleEnter(e))
                return;

            var command = (DataContext as RegisterViewModel)?.RegisterCommand;
            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }

        private static bool HandleEnter(KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return false;
            e.Handled = true;
            return true;
        }
    }
}
