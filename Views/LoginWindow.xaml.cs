using System.Windows;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            var viewModel = new LoginViewModel(ServiceLocator.AuthService, new WindowDialogService(this));
            viewModel.LoginSucceeded += (_, _) =>
            {
                DialogResult = true;
                Close();
            };
            viewModel.CloseRequested += (_, _) =>
            {
                try
                {
                    DialogResult = false;
                }
                catch (InvalidOperationException)
                {
                    // LoginWindow can be shown either as a dialog or as a regular window.
                }

                Application.Current.Shutdown();
            };

            DataContext = viewModel;
            Loaded += (_, _) => LoginBox.Focus();
        }

        private void OnLoginPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            PasswordBox.Focus();
        }

        private void OnPasswordPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            LoginButton.Focus();
        }
    }
}
