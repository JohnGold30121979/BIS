using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;
using BIS.ERP.Services;

namespace BIS.ERP.ViewModels
{
    public partial class SetupViewModel : ViewModelBase
    {
        private readonly AppSettings _settings;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private string host;

        [ObservableProperty]
        private string port;

        [ObservableProperty]
        private string username;

        [ObservableProperty]
        private string password;

        [ObservableProperty]
        private string theme;

        [ObservableProperty]
        private string testResult = string.Empty;

        [ObservableProperty]
        private string testResultBrush = "Green";

        [ObservableProperty]
        private bool isBusy;

        public bool HasTestResult => !string.IsNullOrWhiteSpace(TestResult);

        public event EventHandler? Saved;

        public SetupViewModel(AppSettings settings, IDialogService dialogService)
        {
            _settings = settings;
            _dialogService = dialogService;

            // ✅ ИСПРАВЛЕНО: Используем ThemeService.DefaultTheme
            host = settings.Host;
            port = settings.Port.ToString();
            username = settings.Username;
            password = settings.Password;
            theme = string.IsNullOrWhiteSpace(settings.Theme)
                ? ThemeService.DefaultTheme
                : settings.Theme;
        }

        partial void OnTestResultChanged(string value)
        {
            OnPropertyChanged(nameof(HasTestResult));
        }

        [RelayCommand]
        private async Task TestAsync()
        {
            if (IsBusy)
                return;

            TestResult = string.Empty;

            try
            {
                IsBusy = true;
                using var connection = new NpgsqlConnection(BuildServerConnectionString());
                await connection.OpenAsync();

                TestResult = "Подключение успешно!";
                TestResultBrush = "Green";
            }
            catch (Exception ex)
            {
                TestResult = $"Ошибка: {ex.Message}";
                TestResultBrush = "Red";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                _settings.Host = Host;
                _settings.Port = int.Parse(Port);
                _settings.Username = Username;
                _settings.Password = Password;

                // ✅ ИСПРАВЛЕНО: Используем ThemeService.DefaultTheme
                _settings.Theme = string.IsNullOrWhiteSpace(Theme)
                    ? ThemeService.DefaultTheme
                    : Theme;

                _settings.Save();
                ThemeService.Apply(_settings.Theme);

                _dialogService.ShowInformation("Настройки сохранены.", "Успех");
                Saved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка сохранения: {ex.Message}");
            }
        }

        private string BuildServerConnectionString()
        {
            return $"Host={Host};Port={int.Parse(Port)};Username={Username};Password={Password}";
        }
    }
}