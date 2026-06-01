using System;
using System.Text.RegularExpressions;
using System.Windows;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class RegisterWindow : Window
    {
        private readonly IAuthService _authService;

        public RegisterWindow()
        {
            InitializeComponent();
            _authService = new AuthService();
        }

        private async void OnRegisterClick(object sender, RoutedEventArgs e)
        {
            RegisterButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                var login = LoginBox.Text?.Trim();
                var fullName = FullNameBox.Text?.Trim();
                var email = EmailBox.Text?.Trim();
                var password = PasswordBox.Password;
                var confirmPassword = ConfirmPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(login) || login.Length < 3)
                {
                    ShowError("Логин должен быть не менее 3 символов");
                    return;
                }

                if (string.IsNullOrWhiteSpace(fullName))
                {
                    ShowError("Введите полное имя");
                    return;
                }

                if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
                {
                    ShowError("Введите корректный email");
                    return;
                }

                if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                {
                    ShowError("Пароль должен быть не менее 6 символов");
                    return;
                }

                if (password != confirmPassword)
                {
                    ShowError("Пароли не совпадают");
                    return;
                }

                var success = await _authService.RegisterAsync(login, password, fullName, email);

                if (success)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError("Пользователь с таким логином уже существует");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка: {ex.Message}");
            }
            finally
            {
                RegisterButton.IsEnabled = true;
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private bool IsValidEmail(string email)
        {
            var regex = new Regex(@"^[\w-\.]+@([\w-]+\.)+[\w-]{2,4}$");
            return regex.IsMatch(email);
        }
    }
}