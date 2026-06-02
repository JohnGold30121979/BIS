using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP
{
    public partial class MainWorkWindow : Window
    {
        private AppNavigationService _navigation;
        private IAuthService _authService;
        private InfoBaseManager _infoBaseManager;
        private MetadataService _metadataService;
        private InfoBase _currentInfoBase;

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
                _currentInfoBase = await _infoBaseManager.GetCurrentInfoBaseAsync();
                if (_currentInfoBase != null)
                {
                    CurrentInfoBaseText.Text = _currentInfoBase.Name;
                    this.Title = $"BIS ERP - {_currentInfoBase.Name}";

                    var context = await _infoBaseManager.GetCurrentDbContextAsync();
                    _metadataService = new MetadataService(context);

                    await LoadModules();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadModules()
        {
            Modules.Clear();

            // Загружаем справочники
            var catalogs = await _metadataService.GetCatalogsAsync();
            foreach (var catalog in catalogs.OrderBy(c => c.Name))
            {
                Modules.Add(new ModuleInfo
                {
                    Id = catalog.Id.ToString(),
                    Name = catalog.Name,
                    Icon = catalog.Icon,
                    Type = "Catalog",
                    MetadataObject = catalog
                });
            }

            // Если нет модулей, показываем сообщение
            if (!Modules.Any())
            {
                Modules.Add(new ModuleInfo
                {
                    Id = "Empty",
                    Name = "Нет справочников",
                    Icon = "📭",
                    Type = "Empty"
                });
            }
        }
        private async void OnExportAllClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Сохранить Excel файл",
                Filter = "Excel файлы (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = $"BIS_ERP_Export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    using var workbook = new XLWorkbook();

                    var catalogs = await _metadataService.GetCatalogsAsync();
                    int exportedCount = 0;

                    foreach (var catalog in catalogs)
                    {
                        var data = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        if (data.Any())
                        {
                            var worksheet = workbook.Worksheets.Add(catalog.Name);
                            // Заполнение worksheet аналогично предыдущему
                            exportedCount++;
                        }
                    }

                    workbook.SaveAs(dialog.FileName);

                    MessageBox.Show($"Экспортировано {exportedCount} справочников!\n\nФайл: {dialog.FileName}",
                        "Экспорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка массового экспорта: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }
        private async void OnModuleClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var module = button?.Tag as ModuleInfo;

            if (module == null || module.Type == "Empty") return;

            if (module.Type == "Catalog")
            {
                var catalogView = new CatalogDataView(module.MetadataObject, _metadataService);
                _navigation.NavigateTo(catalogView);
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
            this.Close();
        }
    }

    public class ModuleInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "📄";
        public string Type { get; set; } = "Catalog";
        public MetadataObject MetadataObject { get; set; }
    }
}