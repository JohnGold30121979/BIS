using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using BIS.ERP.Models;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP
{
    public partial class MainWorkWindow : Window
    {
        private AppNavigationService _navigation;
        private IAuthService _authService;
        private InfoBaseManager _infoBaseManager;
        private InfoBase _currentInfoBase;

        // Коллекция модулей для отображения в меню
        public ObservableCollection<ModuleInfo> Modules { get; set; }

        public MainWorkWindow(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            _infoBaseManager = new InfoBaseManager();
            _navigation = new AppNavigationService(ContentArea);
            Modules = new ObservableCollection<ModuleInfo>();
            ModulesMenu.ItemsSource = Modules;
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeDefaultBases();
                await SelectInfoBase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private async Task InitializeDefaultBases()
        {
            await _infoBaseManager.InitializeDefaultBasesAsync();
        }

        private async Task SelectInfoBase()
        {
            var infoBases = await _infoBaseManager.GetInfoBasesAsync();

            if (infoBases == null || infoBases.Count == 0)
            {
                var result = MessageBox.Show(
                    "Не найдено ни одной информационной базы.\nХотите создать новую?",
                    "Создание базы",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await ShowCreateInfoBaseDialog();
                    // После создания пробуем снова
                    await SelectInfoBase();
                }
                else
                {
                    Close();
                }
                return;
            }

            // Проверяем активную базу
            var activeBase = infoBases.FirstOrDefault(b => b.IsActive);

            if (activeBase != null)
            {
                var result = MessageBox.Show(
                    $"Активная информационная база: {activeBase.Name}\n\nИспользовать эту базу?",
                    "Выбор базы",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await SetCurrentInfoBase(activeBase);
                }
                else if (result == MessageBoxResult.No)
                {
                    await ShowInfoBaseSelectionDialog(infoBases);
                }
                else
                {
                    Close();
                }
            }
            else
            {
                await ShowInfoBaseSelectionDialog(infoBases);
            }
        }

        private async Task ShowInfoBaseSelectionDialog(System.Collections.Generic.List<InfoBase> infoBases)
        {
            var dialog = new InfoBaseSelectionDialog(infoBases);
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (dialog.ShowDialog() == true && dialog.SelectedInfoBase != null)
            {
                await SetCurrentInfoBase(dialog.SelectedInfoBase);
            }
            else
            {
                var result = MessageBox.Show(
                    "База не выбрана.\nХотите создать новую?",
                    "Создание базы",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await ShowCreateInfoBaseDialog();
                    await SelectInfoBase(); // Повторяем выбор
                }
                else
                {
                    Close();
                }
            }
        }

        private async Task ShowCreateInfoBaseDialog()
        {
            var dialog = new CreateInfoBaseDialog();
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (dialog.ShowDialog() == true)
            {
                await _infoBaseManager.CreateInfoBaseAsync(
                    dialog.InfoBaseName,
                    dialog.InfoBaseType,
                    dialog.Host,
                    dialog.Port,
                    dialog.Username,
                    dialog.Password);

                MessageBox.Show("Информационная база успешно создана!",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task SetCurrentInfoBase(InfoBase infoBase)
        {
            _currentInfoBase = infoBase;
            await _infoBaseManager.SetCurrentInfoBaseAsync(infoBase.Id);

            // Обновляем заголовок окна
            this.Title = $"BIS ERP - {infoBase.Name}";
            CurrentInfoBaseText.Text = infoBase.Name;

            // Загружаем модули для выбранной базы
            await LoadModulesForInfoBase(infoBase);
        }

        private async Task LoadModulesForInfoBase(InfoBase infoBase)
        {
            try
            {
                // Получаем контекст для выбранной базы
                var context = await _infoBaseManager.GetCurrentDbContextAsync();

                // Здесь можно загружать модули из метаданных или из конфигурации
                // Пока используем стандартные модули, но без жесткой привязки к типу базы

                Modules.Clear();

                // Всегда показываем управление базами
                Modules.Add(new ModuleInfo
                {
                    Id = "InfoBases",
                    Name = "Информационные базы",
                    Icon = "📊",
                    ViewType = typeof(InfoBasesView)
                });

                // Добавляем модули в зависимости от того, что есть в метаданных
                // В реальном приложении здесь нужно загружать модули из БД
                var availableModules = await GetAvailableModules(context);

                foreach (var module in availableModules)
                {
                    Modules.Add(module);
                }

                // Если модулей нет, показываем приглашение к созданию
                if (Modules.Count == 0)
                {
                    Modules.Add(new ModuleInfo
                    {
                        Id = "Empty",
                        Name = "Нет модулей",
                        Icon = "⚠️",
                        ViewType = null
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки модулей: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

                // Показываем хотя бы управление базами
                Modules.Clear();
                Modules.Add(new ModuleInfo
                {
                    Id = "InfoBases",
                    Name = "Информационные базы",
                    Icon = "📊",
                    ViewType = typeof(InfoBasesView)
                });
            }
        }

        private async Task<List<ModuleInfo>> GetAvailableModules(AppDbContext context)
        {
            var modules = new List<ModuleInfo>();

            try
            {
                // Загружаем доступные модули из метаданных
                // В реальном приложении здесь запрос к таблице модулей

                // Пример: проверяем наличие таблиц в БД
                var metadataService = new MetadataService(context);
                var catalogs = await metadataService.GetCatalogsAsync();

                if (catalogs.Any())
                {
                    modules.Add(new ModuleInfo
                    {
                        Id = "Catalogs",
                        Name = "Справочники",
                        Icon = "📚",
                        ViewType = null // TODO: создать универсальный view для справочников
                    });
                }

                // Добавляем стандартные модули-заглушки, которые будут заменены реальными
                modules.Add(new ModuleInfo
                {
                    Id = "Finance",
                    Name = "Финансы",
                    Icon = "💰",
                    ViewType = typeof(FinanceView)
                });

                modules.Add(new ModuleInfo
                {
                    Id = "Inventory",
                    Name = "Учет ТМЦ",
                    Icon = "📦",
                    ViewType = typeof(InventoryView)
                });

                modules.Add(new ModuleInfo
                {
                    Id = "Salary",
                    Name = "Зарплата и кадры",
                    Icon = "👥",
                    ViewType = typeof(SalaryView)
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading modules: {ex.Message}");
            }

            return modules;
        }

        private void OnModuleClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var module = button?.Tag as ModuleInfo;

            if (module?.ViewType != null)
            {
                // Создаем экземпляр View
                var view = Activator.CreateInstance(module.ViewType) as UserControl;
                if (view != null)
                {
                    _navigation.NavigateTo(view);
                }
            }
            else
            {
                MessageBox.Show($"Модуль '{module?.Name}' в разработке",
                    "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnProfileClick(object sender, RoutedEventArgs e)
        {
            var user = _authService.CurrentUser;
            if (user != null)
            {
                MessageBox.Show($"Пользователь: {user.FullName}\n" +
                               $"Логин: {user.Login}\n" +
                               $"Email: {user.Email}\n" +
                               $"Роль: {(user.Role == UserRole.Admin ? "Администратор" : "Пользователь")}",
                               "Профиль", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OnLogoutClick(object sender, RoutedEventArgs e)
        {
            _authService.Logout();
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            Close();
        }
    }

    // Класс для описания модуля
    public class ModuleInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "📄";
        public Type? ViewType { get; set; }
    }
}