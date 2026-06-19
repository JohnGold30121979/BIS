using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BIS.ERP.Services;

namespace BIS.ERP.ViewModels
{
    public partial class RegisterViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        private string login = string.Empty;

        [ObservableProperty]
        private string fullName = string.Empty;

        [ObservableProperty]
        private string email = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string confirmPassword = string.Empty;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [ObservableProperty]
        private bool isBusy;

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public event EventHandler? RegisterSucceeded;
        public event EventHandler? CloseRequested;

        public RegisterViewModel(IAuthService authService)
        {
            _authService = authService;
        }

        partial void OnErrorMessageChanged(string value)
        {
            OnPropertyChanged(nameof(HasError));
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            if (IsBusy)
                return;

            ErrorMessage = string.Empty;

            if (!Validate())
                return;

            try
            {
                IsBusy = true;
                var success = await _authService.RegisterAsync(Login.Trim(), Password, FullName.Trim(), Email.Trim());

                if (success)
                {
                    RegisterSucceeded?.Invoke(this, EventArgs.Empty);
                    return;
                }

                ErrorMessage = "Пользователь с таким логином уже существует";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void Back()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool Validate()
        {
            if (string.IsNullOrWhiteSpace(Login) || Login.Trim().Length < 3)
            {
                ErrorMessage = "Логин должен быть не менее 3 символов";
                return false;
            }

            if (string.IsNullOrWhiteSpace(FullName))
            {
                ErrorMessage = "Введите полное имя";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Email) || !IsValidEmail(Email))
            {
                ErrorMessage = "Введите корректный email";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
            {
                ErrorMessage = "Пароль должен быть не менее 6 символов";
                return false;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Пароли не совпадают";
                return false;
            }

            return true;
        }

        private static bool IsValidEmail(string value)
        {
            return Regex.IsMatch(value, @"^[\w\.-]+@([\w-]+\.)+[\w-]{2,4}$");
        }
    }
}
