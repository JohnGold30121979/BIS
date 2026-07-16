using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.ViewModels
{
    public partial class InfoBaseSelectionViewModel : ViewModelBase
    {
        private readonly InfoBaseManager _infoBaseManager;
        private readonly IAuthService _authService;
        private readonly IDialogService _dialogService;

        [ObservableProperty]
        private ObservableCollection<InfoBase> infoBases = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartWorkModeCommand))]
        [NotifyCanExecuteChangedFor(nameof(StartConfigModeCommand))]
        [NotifyCanExecuteChangedFor(nameof(EditCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
        private InfoBase? selectedInfoBase;

        [ObservableProperty]
        private bool isLoading;

        public bool HasInfoBases => InfoBases.Any();
        public bool IsEmpty => !HasInfoBases;
        public string ConnectionPath => SelectedInfoBase == null
            ? "Выберите информационную базу"
            : $"Сервер: {SelectedInfoBase.Host}:{SelectedInfoBase.Port}; База: {SelectedInfoBase.DatabaseName}; {SelectedInfoBase.PatchVersionDisplay}";

        public event EventHandler<bool>? OpenMainWindowRequested;
        public event EventHandler? CloseRequested;
        public event EventHandler? ExitRequested;

        public InfoBaseSelectionViewModel(InfoBaseManager infoBaseManager, IAuthService authService, IDialogService dialogService)
        {
            _infoBaseManager = infoBaseManager;
            _authService = authService;
            _dialogService = dialogService;
        }

        partial void OnSelectedInfoBaseChanged(InfoBase? value)
        {
            OnPropertyChanged(nameof(ConnectionPath));
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            try
            {
                IsLoading = true;
                var bases = await _infoBaseManager.GetInfoBasesAsync();

                InfoBases = new ObservableCollection<InfoBase>(bases ?? Enumerable.Empty<InfoBase>());
                OnPropertyChanged(nameof(HasInfoBases));
                OnPropertyChanged(nameof(IsEmpty));

                SelectedInfoBase = InfoBases.FirstOrDefault(b => b.IsActive) ?? InfoBases.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка загрузки инфобаз: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartWorkModeAsync()
        {
            await StartAsync(false);
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task StartConfigModeAsync()
        {
            await StartAsync(true);
        }

        [RelayCommand]
        private async Task AddAsync()
        {
            if (_dialogService.ShowCreateInfoBase(out var infoBaseName))
            {
                await LoadAsync();
                _dialogService.ShowInformation($"База данных '{infoBaseName}' успешно создана!", "Успех");
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedInfoBase))]
        private async Task EditAsync()
        {
            if (SelectedInfoBase == null || !_dialogService.ShowEditInfoBase(SelectedInfoBase, out var newName, out var newIcon))
                return;
            try
            {
                await _infoBaseManager.UpdateInfoBaseAsync(SelectedInfoBase.Id, newName!, newIcon);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка изменения базы: {ex.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(HasSelectedInfoBase))]
        private async Task DeleteAsync()
        {
            if (SelectedInfoBase == null)
                return;

            if (!_dialogService.Confirm($"Удалить базу '{SelectedInfoBase.Name}'?\nВсе данные будут потеряны!"))
                return;

            try
            {
                await _infoBaseManager.DeleteInfoBaseAsync(SelectedInfoBase.Id);
                await LoadAsync();
                _dialogService.ShowInformation("База удалена", "Успех");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка удаления: {ex.Message}");
            }
        }

        [RelayCommand]
        private void Settings()
        {
            _dialogService.ShowSetup();
        }

        [RelayCommand]
        private void Exit()
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private async Task StartAsync(bool configMode)
        {
            if (SelectedInfoBase == null)
            {
                _dialogService.ShowWarning("Выберите информационную базу");
                return;
            }

            try
            {
                await _infoBaseManager.SetCurrentInfoBaseAsync(SelectedInfoBase.Id);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка выбора информационной базы: {ex.Message}");
                return;
            }

            if (!await _dialogService.ShowLoginAsync())
                return;

            var currentUser = _authService.CurrentUser;
            if (currentUser == null)
            {
                _dialogService.ShowError("Ошибка авторизации");
                return;
            }

            if (configMode && currentUser.Role != UserRole.Admin)
            {
                _dialogService.ShowWarning(
                    "Недостаточно прав для входа в конфигуратор. Требуются права администратора.\n\nВы будете перенаправлены в рабочий режим.",
                    "Доступ запрещен");
                configMode = false;
            }

            try
            {
                OpenMainWindowRequested?.Invoke(this, configMode);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Ошибка открытия: {ex.Message}");
            }
        }

        private bool CanStart()
        {
            return SelectedInfoBase != null;
        }

        private bool HasSelectedInfoBase()
        {
            return SelectedInfoBase != null;
        }
    }
}
