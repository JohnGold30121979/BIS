using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class UserEditDialog : Window
    {
        public UserEditDialog(IReadOnlyList<UserRole> allowedRoles)
        {
            InitializeComponent();
            RoleBox.ItemsSource = allowedRoles.Select(role => new RoleOption(role)).ToList();
            RoleBox.SelectedIndex = RoleBox.Items.Count > 0 ? 0 : -1;
            Loaded += (_, _) => LoginBox.Focus();
        }

        public string UserLogin => LoginBox.Text.Trim();
        public string FullName => FullNameBox.Text.Trim();
        public string Email => EmailBox.Text.Trim();
        public string Password => PasswordBox.Password;
        public UserRole SelectedRole => RoleBox.SelectedItem is RoleOption option ? option.Role : UserRole.User;
        public bool IsUserActive => ActiveCheckBox.IsChecked == true;

        private void OnRoleSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActiveCheckBox.IsEnabled = SelectedRole != UserRole.Admin;
            if (SelectedRole == UserRole.Admin)
                ActiveCheckBox.IsChecked = true;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            if (UserLogin.Length < 3)
            {
                ErrorText.Text = "Логин должен быть не менее 3 символов.";
                LoginBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(FullName))
            {
                ErrorText.Text = "Введите полное имя.";
                FullNameBox.Focus();
                return;
            }

            if (!string.IsNullOrWhiteSpace(Email) && !Regex.IsMatch(Email, @"^[\w\.-]+@([\w-]+\.)+[\w-]{2,4}$"))
            {
                ErrorText.Text = "Введите корректный email или оставьте поле пустым.";
                EmailBox.Focus();
                return;
            }

            if (Password.Length < 6)
            {
                ErrorText.Text = "Пароль должен быть не менее 6 символов.";
                PasswordBox.Focus();
                return;
            }

            if (!string.Equals(Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
            {
                ErrorText.Text = "Пароли не совпадают.";
                ConfirmPasswordBox.Focus();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private sealed class RoleOption
        {
            public RoleOption(UserRole role)
            {
                Role = role;
                Name = UserAccessService.GetRoleDisplayName(role);
            }

            public UserRole Role { get; }
            public string Name { get; }
        }
    }
}
