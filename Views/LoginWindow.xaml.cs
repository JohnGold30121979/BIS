using System.Windows;
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
        }
    }
}
