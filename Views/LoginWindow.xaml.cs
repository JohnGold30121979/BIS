using System;
using System.Windows;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            LoginButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                var login = LoginBox.Text?.Trim();
                var password = PasswordBox.Password;

                if (string.IsNullOrWhiteSpace(login))
                {
                    ShowError("Введите логин");
                    return;
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    ShowError("Введите пароль");
                    return;
                }

                var result = await ServiceLocator.AuthService.LoginAsync(login, password);

                if (result.Success && result.User != null)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError(result.ErrorMessage ?? "Ошибка входа");
                    LoginButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
                LoginButton.IsEnabled = true;
            }
        }

        private void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            var registerWindow = new RegisterWindow();
            registerWindow.Owner = this;
            registerWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            registerWindow.ShowDialog();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
          //  DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
            LoginButton.IsEnabled = true;
        }
    }
}