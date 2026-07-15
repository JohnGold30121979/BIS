using System;
using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class UserProfileDialog : Window
    {
        private readonly IAuthService _authService;
        private readonly string _infoBaseName;

        public UserProfileDialog(IAuthService authService, string infoBaseName)
        {
            InitializeComponent();
            _authService = authService;
            _infoBaseName = infoBaseName;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var user = _authService.CurrentUser;
            if (user == null)
            {
                ErrorText.Text = "Пользователь не авторизован.";
                return;
            }

            InfoBaseText.Text = $"Инфобаза: {_infoBaseName}";
            FullNameText.Text = string.IsNullOrWhiteSpace(user.FullName) ? user.Login : user.FullName;
            LoginText.Text = user.Login;
            EmailText.Text = user.Email;
            RoleText.Text = user.Role switch
            {
                UserRole.Admin => "Администратор",
                UserRole.Accountant => "Бухгалтер",
                _ => "Пользователь"
            };
            CurrentPasswordBox.Focus();
        }

        private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            var currentPassword = CurrentPasswordBox.Password;
            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                ErrorText.Text = "Введите текущий пароль.";
                CurrentPasswordBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                ErrorText.Text = "Новый пароль должен быть не менее 6 символов.";
                NewPasswordBox.Focus();
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                ErrorText.Text = "Подтверждение нового пароля не совпадает.";
                ConfirmPasswordBox.Focus();
                return;
            }

            var result = await _authService.ChangePasswordAsync(currentPassword, newPassword);
            if (!result.Success)
            {
                ErrorText.Text = result.ErrorMessage ?? "Пароль не изменен.";
                return;
            }

            MessageBox.Show(this, "Пароль успешно изменен.", "Профиль",
                MessageBoxButton.OK, MessageBoxImage.Information);
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            Close();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
