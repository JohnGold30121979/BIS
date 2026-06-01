using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class InfoBaseSelectionWindow : Window
    {
        private readonly InfoBaseManager _infoBaseManager;
        private InfoBase _selectedInfoBase;
        private bool _isConfigModeSelected = false;

        public InfoBaseSelectionWindow()
        {
            InitializeComponent();
            _infoBaseManager = new InfoBaseManager();

            this.Loaded += async (s, e) => await LoadInfoBases();

            // Двойной клик для быстрого выбора рабочего режима
            InfoBasesList.MouseDoubleClick += async (s, e) =>
            {
                if (InfoBasesList.SelectedItem != null)
                {
                    _isConfigModeSelected = false;
                    await StartLoginAndWork();
                }
            };

            // Обе кнопки активны с самого начала
            WorkModeButton.IsEnabled = true;
            ConfigModeButton.IsEnabled = true;
        }

        private async Task StartLoginAndWork()
        {
            if (_selectedInfoBase == null)
            {
                MessageBox.Show("Выберите информационную базу", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Открываем окно логина
            var loginWindow = new LoginWindow();
            loginWindow.Owner = this;
            loginWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var loginResult = loginWindow.ShowDialog();

            if (loginResult == true)
            {
                // Используем ServiceLocator для получения текущего пользователя
                var currentUser = ServiceLocator.AuthService.CurrentUser;

                if (currentUser == null)
                {
                    MessageBox.Show("Ошибка авторизации", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Проверяем права для конфигуратора
                if (_isConfigModeSelected && currentUser.Role != UserRole.Admin)
                {
                    MessageBox.Show("Недостаточно прав для входа в конфигуратор. Требуются права администратора.\n\n" +
                        "Вы будете перенаправлены в рабочий режим.",
                        "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _isConfigModeSelected = false;
                }

                try
                {
                    await _infoBaseManager.SetCurrentInfoBaseAsync(_selectedInfoBase.Id);

                    if (_isConfigModeSelected)
                    {
                        var configWindow = new ConfiguratorWindow();
                        configWindow.Show();
                    }
                    else
                    {
                        var workWindow = new MainWorkWindow(ServiceLocator.AuthService);
                        workWindow.Show();
                    }

                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка открытия: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void OnWorkModeClick(object sender, RoutedEventArgs e)
        {
            _isConfigModeSelected = false;
            await StartLoginAndWork();
        }

        private async void OnConfigModeClick(object sender, RoutedEventArgs e)
        {
            _isConfigModeSelected = true;
            await StartLoginAndWork();
        }

        // Остальные методы без изменений...
        private async Task LoadInfoBases()
        {
            try
            {
                if (LoadingProgress != null)
                    LoadingProgress.Visibility = Visibility.Visible;

                var bases = await _infoBaseManager.GetInfoBasesAsync();
                InfoBasesList.ItemsSource = bases;

                if (bases != null && bases.Any())
                {
                    var activeBase = bases.FirstOrDefault(b => b.IsActive);
                    InfoBasesList.SelectedItem = activeBase ?? bases.First();

                    if (EmptyTextBlock != null)
                        EmptyTextBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (EmptyTextBlock != null)
                        EmptyTextBlock.Visibility = Visibility.Visible;

                    WorkModeButton.IsEnabled = false;
                    ConfigModeButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инфобаз: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (LoadingProgress != null)
                    LoadingProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnInfoBaseSelected(object sender, SelectionChangedEventArgs e)
        {
            _selectedInfoBase = InfoBasesList.SelectedItem as InfoBase;

            if (EditButton != null)
                EditButton.IsEnabled = _selectedInfoBase != null;

            if (DeleteButton != null)
                DeleteButton.IsEnabled = _selectedInfoBase != null;

            bool hasBase = _selectedInfoBase != null;
            WorkModeButton.IsEnabled = hasBase;
            ConfigModeButton.IsEnabled = hasBase;

            if (_selectedInfoBase != null && ConnectionPathText != null)
            {
                ConnectionPathText.Text = $"File=\"{_selectedInfoBase.DatabaseName}\";";
            }
            else if (ConnectionPathText != null)
            {
                ConnectionPathText.Text = "Выберите информационную базу";
            }
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateInfoBaseDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                await LoadInfoBases();
                MessageBox.Show($"База данных '{dialog.InfoBaseName}' успешно создана!",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (_selectedInfoBase != null)
            {
                MessageBox.Show($"Редактирование базы: {_selectedInfoBase.Name}\n\nФункция в разработке.",
                    "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_selectedInfoBase == null) return;

            var result = MessageBox.Show($"Удалить базу '{_selectedInfoBase.Name}'?\nВсе данные будут потеряны!",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _infoBaseManager.DeleteInfoBaseAsync(_selectedInfoBase.Id);
                    await LoadInfoBases();
                    MessageBox.Show("База удалена", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var setupWindow = new SetupWindow();
            setupWindow.Owner = this;
            setupWindow.ShowDialog();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}