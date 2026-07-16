using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BIS.ERP.Services;

namespace BIS.ERP.ViewModels
{
    public partial class LoginViewModel : ViewModelBase
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        private string login = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [ObservableProperty]
        private string selectedInfoBaseText = string.Empty;

        [ObservableProperty]
        private bool isBusy;

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
        public bool HasSelectedInfoBase => !string.IsNullOrWhiteSpace(SelectedInfoBaseText);

        public event EventHandler? LoginSucceeded;
        public event EventHandler? CloseRequested;
        public event EventHandler? BackRequested;

        public LoginViewModel(IAuthService authService, string selectedInfoBaseText = "")
        {
            _authService = authService;
            SelectedInfoBaseText = selectedInfoBaseText;
        }

        partial void OnErrorMessageChanged(string value)
        {
            OnPropertyChanged(nameof(HasError));
        }

        partial void OnSelectedInfoBaseTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasSelectedInfoBase));
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (IsBusy)
                return;

            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Login))
            {
                ErrorMessage = "Введите логин";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите пароль";
                return;
            }

            try
            {
                IsBusy = true;
                var result = await _authService.LoginAsync(Login.Trim(), Password);

                if (result.Success && result.User != null)
                {
                    LoginSucceeded?.Invoke(this, EventArgs.Empty);
                    return;
                }

                ErrorMessage = result.ErrorMessage ?? "Ошибка входа";
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
        private void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Back()
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
