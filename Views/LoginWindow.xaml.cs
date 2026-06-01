using System;
using System.Windows;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class LoginWindow : Window
    {
        private readonly IAuthService _authService;

        public LoginWindow()
        {
            InitializeComponent();
            _authService = new AuthService();
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

                var result = await _authService.LoginAsync(login, password);

                if (result.Success && result.User != null)
                {
                    // Открываем окно выбора режима
                    var modeWindow = new ModeSelectionWindow(result.User);
                    modeWindow.Owner = this;
                    modeWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                    var modeResult = modeWindow.ShowDialog();

                    if (modeResult == true)
                    {
                        if (modeWindow.IsConfigMode)
                        {
                            // Открываем конфигуратор
                            var configWindow = new ConfiguratorWindow();
                            configWindow.Show();
                        }
                        else
                        {
                            // Открываем рабочий режим
                            var workWindow = new MainWorkWindow(_authService);
                            workWindow.Show();
                        }

                        Close();
                    }
                    else
                    {
                        LoginButton.IsEnabled = true;
                    }
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

            if (registerWindow.ShowDialog() == true)
            {
                MessageBox.Show("Регистрация успешна! Теперь вы можете войти.",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                LoginBox.Clear();
                PasswordBox.Clear();
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}