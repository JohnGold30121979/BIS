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
            viewModel.CloseRequested += (_, _) => Close();

            DataContext = viewModel;
        }
    }
}
