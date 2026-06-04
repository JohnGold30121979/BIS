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
using System.Data;

namespace BIS.ERP
{
    public partial class MainWorkWindow : Window
    {
        private AppNavigationService _navigation;
        private IAuthService _authService;
        private InfoBaseManager _infoBaseManager;
        private MetadataService _metadataService;
        private ReportService _reportService;
        private DocumentService _documentService; // Добавляем DocumentService
        private InfoBase _currentInfoBase;
        private bool _isLoadingReport = false;

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
                    _reportService = new ReportService(context);
                    _documentService = new DocumentService(context); // Инициализируем DocumentService

                    await _metadataService.InitializePredefinedCatalogsAsync();
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

            // Добавляем раздел Документы
            Modules.Add(new ModuleInfo
            {
                Id = "Documents",
                Name = "Документы",
                Icon = "📄",
                Type = "DocumentsGroup"
            });

            // Добавляем раздел DBF Документы (импортированные)
            var dbfDocsCount = await _documentService.GetDocumentsCountAsync();
            Modules.Add(new ModuleInfo
            {
                Id = "DynamicDocuments",
                Name = $"DBF Документы {(dbfDocsCount > 0 ? $"({dbfDocsCount})" : "")}",
                Icon = "🗄️",
                Type = "DynamicDocuments"
            });

            // Добавляем Операции
            Modules.Add(new ModuleInfo
            {
                Id = "Operations",
                Name = "Операции",
                Icon = "📋",
                Type = "Operations"
            });

            // Загружаем отчеты
            var reports = await _reportService.GetReportsAsync();
            foreach (var report in reports.OrderBy(r => r.Name))
            {
                Modules.Add(new ModuleInfo
                {
                    Id = report.Id.ToString(),
                    Name = report.Name,
                    Icon = report.Icon,
                    Type = "Report",
                    Report = report
                });
            }

            if (!Modules.Any())
            {
                Modules.Add(new ModuleInfo
                {
                    Id = "Empty",
                    Name = "Нет данных",
                    Icon = "📭",
                    Type = "Empty"
                });
            }
        }

        // Обработчик клика по модулю
        private async void OnModuleClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var module = button?.Tag as ModuleInfo;

            if (module == null || module.Type == "Empty") return;

            if (_isLoadingReport) return;

            if (module.Type == "Catalog" && module.MetadataObject != null)
            {
                try
                {
                    var catalogView = new CatalogDataView(module.MetadataObject, _metadataService);
                    _navigation.NavigateTo(catalogView);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
          //  else if (module.Type == "DocumentsGroup")
         //   {
                // Открываем управление документами
           //     var documentsView = new DocumentsManagementView(_documentService);
           //     _navigation.NavigateTo(documentsView);
           // }
            else if (module.Type == "DynamicDocuments")
            {
                // Открываем список импортированных DBF документов
                var dynamicDocsView = new DynamicDocumentsView(_documentService);
                _navigation.NavigateTo(dynamicDocsView);
            }
            else if (module.Type == "Operations")
            {
                var operationsView = new OperationsView();
                _navigation.NavigateTo(operationsView);
            }
            else if (module.Type == "Report" && module.Report != null)
            {
                _isLoadingReport = true;
                Mouse.OverrideCursor = Cursors.Wait;

                try
                {
                    System.Diagnostics.Debug.WriteLine($"=== Opening report: {module.Report.Name} ===");

                    var context = await _infoBaseManager.GetCurrentDbContextAsync();
                    var reportService = new ReportService(context);

                    System.Diagnostics.Debug.WriteLine("Getting report data...");
                    var data = await reportService.GetReportDataAsync(module.Report);

                    System.Diagnostics.Debug.WriteLine($"Data loaded: {data.Rows.Count} rows, {data.Columns.Count} columns");

                    var preview = new ReportPreviewWindow(data, module.Report);
                    preview.Owner = this;
                    preview.ShowDialog();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: {ex.Message}");
                    MessageBox.Show($"Ошибка формирования отчета: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _isLoadingReport = false;
                    Mouse.OverrideCursor = null;
                }
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
                            // Заполнение worksheet
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

        private void OnSwitchModeClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Выйти в окно выбора режима?\nНесохраненные данные будут потеряны.",
                "Смена режима", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _authService.Logout();
                var modeWindow = new InfoBaseSelectionWindow();
                modeWindow.Show();
                this.Close();
            }
        }
    }

    public class ModuleInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "📄";
        public string Type { get; set; } = "Catalog";
        public MetadataObject MetadataObject { get; set; }
        public Report Report { get; set; }
    }
}