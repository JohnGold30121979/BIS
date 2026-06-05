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
using System.ComponentModel;
using System.Windows.Data;

namespace BIS.ERP
{
    public class NavigationItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "📄";
        public string Type { get; set; } = "Item";
        public string Badge { get; set; } = string.Empty;
        public bool HasBadge => !string.IsNullOrEmpty(Badge);
        public object Tag { get; set; }
        public ObservableCollection<NavigationItem> Children { get; set; } = new ObservableCollection<NavigationItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWorkWindow : Window
    {
        private AppNavigationService _navigation;
        private IAuthService _authService;
        private InfoBaseManager _infoBaseManager;
        private MetadataService _metadataService;
        private ReportService _reportService;
        private DocumentService _documentService;
        private InfoBase _currentInfoBase;
        private bool _isLoadingReport = false;

        public ObservableCollection<NavigationItem> NavigationItems { get; set; }

        public MainWorkWindow(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
            _infoBaseManager = new InfoBaseManager();
            _navigation = new AppNavigationService(ContentArea);
            NavigationItems = new ObservableCollection<NavigationItem>();
            NavigationTree.ItemsSource = NavigationItems;
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
                    _documentService = new DocumentService(context);

                    await _metadataService.InitializePredefinedCatalogsAsync();
                    await BuildNavigationTree();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task BuildNavigationTree()
        {
            NavigationItems.Clear();

            // ========== РАЗДЕЛ: ДАННЫЕ ==========
            var dataSection = new NavigationItem
            {
                Id = "DataSection",
                Name = "ДАННЫЕ",
                Icon = "📊",
                Type = "Section"
            };

            // Справочники
            var catalogs = await _metadataService.GetCatalogsAsync();
            if (catalogs.Any())
            {
                var catalogsGroup = new NavigationItem
                {
                    Id = "CatalogsGroup",
                    Name = "Справочники",
                    Icon = "📚",
                    Type = "Group"
                };

                foreach (var catalog in catalogs.OrderBy(c => c.Name))
                {
                    catalogsGroup.Children.Add(new NavigationItem
                    {
                        Id = catalog.Id.ToString(),
                        Name = catalog.Name,
                        Icon = catalog.Icon,
                        Type = "Catalog",
                        Tag = catalog
                    });
                }
                dataSection.Children.Add(catalogsGroup);
            }

            // Динамические документы
            var documents = await _metadataService.GetDocumentsAsync();
            if (documents.Any())
            {
                var docsGroup = new NavigationItem
                {
                    Id = "DocumentsGroup",
                    Name = "Документы",
                    Icon = "📄",
                    Type = "Group"
                };

                foreach (var doc in documents.OrderBy(d => d.Name))
                {
                    docsGroup.Children.Add(new NavigationItem
                    {
                        Id = doc.Id.ToString(),
                        Name = doc.Name,
                        Icon = doc.Icon,
                        Type = "DynamicDocument",
                        Tag = doc
                    });
                }
                dataSection.Children.Add(docsGroup);
            }

            // Импортированные DBF документы
            var dbfCount = await _documentService.GetDocumentsCountAsync();
            var dbfItem = new NavigationItem
            {
                Id = "DbfDocuments",
                Name = "DBF Документы",
                Icon = "🗄️",
                Type = "DbfDocuments",
                Badge = dbfCount > 0 ? dbfCount.ToString() : ""
            };
            dataSection.Children.Add(dbfItem);

            // Операции
            dataSection.Children.Add(new NavigationItem
            {
                Id = "Operations",
                Name = "Операции",
                Icon = "📋",
                Type = "Operations"
            });

            NavigationItems.Add(dataSection);

            // ========== РАЗДЕЛ: ОТЧЕТЫ ==========
            var reports = await _reportService.GetReportsAsync();
            if (reports.Any())
            {
                var reportsSection = new NavigationItem
                {
                    Id = "ReportsSection",
                    Name = "ОТЧЕТЫ",
                    Icon = "📈",
                    Type = "Section"
                };

                foreach (var report in reports.OrderBy(r => r.Name))
                {
                    reportsSection.Children.Add(new NavigationItem
                    {
                        Id = report.Id.ToString(),
                        Name = report.Name,
                        Icon = report.Icon,
                        Type = "Report",
                        Tag = report
                    });
                }
                NavigationItems.Add(reportsSection);
            }

            // ========== РАЗДЕЛ: АДМИНИСТРИРОВАНИЕ ==========
            var adminSection = new NavigationItem
            {
                Id = "AdminSection",
                Name = "АДМИНИСТРИРОВАНИЕ",
                Icon = "⚙️",
                Type = "Section"
            };

            adminSection.Children.Add(new NavigationItem
            {
                Id = "UserProfile",
                Name = "Профиль",
                Icon = "👤",
                Type = "Profile"
            });

            adminSection.Children.Add(new NavigationItem
            {
                Id = "SwitchMode",
                Name = "Сменить режим",
                Icon = "🔄",
                Type = "SwitchMode"
            });

            adminSection.Children.Add(new NavigationItem
            {
                Id = "Logout",
                Name = "Выход",
                Icon = "🚪",
                Type = "Logout"
            });

            NavigationItems.Add(adminSection);

            // Подписываемся на события выбора
            NavigationTree.SelectedItemChanged += OnNavigationItemSelected;

            // Раскрываем секции по умолчанию
            foreach (var item in NavigationItems)
            {
                var treeItem = GetTreeViewItem(NavigationTree, item);
                if (treeItem != null)
                    treeItem.IsExpanded = true;
            }
        }

        private TreeViewItem GetTreeViewItem(ItemsControl container, object item)
        {
            if (container == null) return null;

            var treeItem = container.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (treeItem != null) return treeItem;

            foreach (object child in container.Items)
            {
                var childContainer = container.ItemContainerGenerator.ContainerFromItem(child) as ItemsControl;
                if (childContainer != null)
                {
                    treeItem = GetTreeViewItem(childContainer, item);
                    if (treeItem != null) return treeItem;
                }
            }
            return null;
        }

        private async void OnNavigationItemSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var item = e.NewValue as NavigationItem;
            if (item == null) return;

            if (_isLoadingReport) return;

            try
            {
                switch (item.Type)
                {
                    case "Catalog":
                        if (item.Tag is MetadataObject catalog)
                        {
                            var catalogView = new CatalogDataView(catalog, _metadataService);
                            _navigation.NavigateTo(catalogView);
                        }
                        break;

                    case "DynamicDocument":
                        if (item.Tag is MetadataObject document)
                        {
                            var dynamicView = new DynamicDocumentWorkView(document, _metadataService);
                            _navigation.NavigateTo(dynamicView);
                        }
                        break;

                    case "DbfDocuments":
                        var dbfView = new DynamicDocumentsView(_documentService);
                        _navigation.NavigateTo(dbfView);
                        break;

                    case "Operations":
                        var operationsView = new OperationsView();
                        _navigation.NavigateTo(operationsView);
                        break;

                    case "Report":
                        if (item.Tag is Report report)
                        {
                            await OpenReport(report);
                        }
                        break;

                    case "Profile":
                        OnProfileClick(null, null);
                        break;

                    case "SwitchMode":
                        OnSwitchModeClick(null, null);
                        break;

                    case "Logout":
                        OnLogoutClick(null, null);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OpenReport(Report report)
        {
            _isLoadingReport = true;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var context = await _infoBaseManager.GetCurrentDbContextAsync();
                var reportService = new ReportService(context);
                var data = await reportService.GetReportDataAsync(report);

                var preview = new ReportPreviewWindow(data, report);
                preview.Owner = this;
                preview.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingReport = false;
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnProfileClick(object sender, RoutedEventArgs e)
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
            var result = MessageBox.Show("Вы действительно хотите выйти?", "Выход",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _authService.Logout();
                var loginWindow = new LoginWindow();
                loginWindow.Show();
                this.Close();
            }
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
}