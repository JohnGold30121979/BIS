using System.Windows;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();

            var viewModel = new RegisterViewModel(ServiceLocator.AuthService);
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
        }
    }
}
