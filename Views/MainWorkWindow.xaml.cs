using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
using BIS.ERP.Views.Dialogs;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;

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

        private int _order;
        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(nameof(Order)); }
        }

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
        private LocalizationService _localizationService;
        private AccountingPeriodService _accountingPeriodService;
        private UserAccessService _userAccessService;
        private ModuleMetadataService _moduleMetadataService;
        private InfoBase _currentInfoBase;
        private bool _isLoadingReport = false;
        private Point _dragStartPoint;
        private NavigationItem _draggedItem;
        private bool _closeForModeSwitch;

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
            this.Closing += OnWindowClosing;
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (ApplicationExitService.IsShuttingDown || _closeForModeSwitch)
                return;

            e.Cancel = true;
            ApplicationExitService.ConfirmAndShutdown(this);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var systemConfiguration = await new SystemConfigurationService().GetAsync();
                SystemNameText.Text = systemConfiguration.SystemName;
                SystemIconText.Text = systemConfiguration.Icon;
                _currentInfoBase = await _infoBaseManager.GetCurrentInfoBaseAsync();
                if (_currentInfoBase != null)
                {
                    CurrentInfoBaseText.Text = _currentInfoBase.Name;
                    this.Title = $"{systemConfiguration.SystemName} - {_currentInfoBase.Name}";

                    // ✅ Устанавливаем иконку кнопки темы при загрузке
                    var settings = AppSettings.Instance;
                    if (ThemeToggleButton != null)
                    {
                        ThemeToggleButton.Content = settings.Theme == "Dark" ? "🌞" : "🌙";
                        ThemeToggleButton.ToolTip = settings.Theme == "Dark" ? "Светлая тема" : "Темная тема";
                    }

                    var context = await _infoBaseManager.GetCurrentDbContextAsync();
                    _accountingPeriodService = new AccountingPeriodService(context);
                    _localizationService = new LocalizationService(context, settings.Language);
                    _metadataService = new MetadataService(context);
                    _reportService = new ReportService(context);
                    _documentService = new DocumentService(context);
                    _userAccessService = new UserAccessService(context);
                    _moduleMetadataService = new ModuleMetadataService(context);

                    await new RuntimeSchemaFixService(context).EnsureAsync();
                    await _accountingPeriodService.EnsureSchemaAsync();
                    await _userAccessService.EnsureSchemaAsync();
                    await _localizationService.InitializeAsync();
                    await new PrintFormService(context).EnsureSchemaAsync();
                    await _metadataService.InitializePredefinedCatalogsAsync(_currentInfoBase.Id);
                    await new DocumentationMetadataSeedService(context).EnsureAsync();
                    await new InvoiceMetadataSeedService(context).EnsureAsync();
                    var printFormService = new PrintFormService(context);
                    await printFormService.SeedCashOrderFormsAsync();
                    await printFormService.SeedInvoiceFormsAsync();
                    await new TestPostingMetadataSeedService(context).EnsureAsync(createTestPostings: false);
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

            var allMetadata = await _metadataService.GetAllMetadataObjectsAsync();
            var catalogs = allMetadata.Where(item => item.ObjectType == "Catalog" && item.Name != "Контрагенты").ToList();
            var documents = allMetadata.Where(item => item.ObjectType == "Document").ToList();
            var reports = await _reportService.GetNavigationReportsAsync();
            var modules = await _moduleMetadataService.GetModulesAsync();
            var moduleItems = await _moduleMetadataService.GetItemsAsync();
            if (modules.Count == 0 && (await _moduleMetadataService.GetModulesAsync(true)).Count == 0)
            {
                await BuildLegacyNavigationTree();
                return;
            }
            var assignmentByObject = moduleItems.ToDictionary(item => item.ObjectId, item => item);

            var directoriesSection = new NavigationItem
            {
                Id = "DirectoriesSection", Name = "СПРАВОЧНИКИ", Icon = "📚", Type = "Section"
            };
            foreach (var catalog in catalogs.OrderBy(item => item.Name))
            {
                directoriesSection.Children.Add(new NavigationItem
                {
                    Id = catalog.Id.ToString(),
                    Name = catalog.Name == "Сотрудники (Списочный состав)" ? "Сотрудники" : catalog.Name,
                    Icon = catalog.Icon,
                    Type = catalog.Name == "Сотрудники (Списочный состав)" ? "EmployeesCatalog" : "Catalog",
                    Tag = catalog,
                    Order = catalog.Order
                });
            }
            NavigationItems.Add(directoriesSection);

            foreach (var module in modules)
            {
                var moduleSection = new NavigationItem
                {
                    Id = $"Module:{module.Id}", Name = module.Name.ToUpperInvariant(), Icon = module.Icon, Type = "Section",
                    Order = module.Order, Tag = module
                };
                var documentIds = moduleItems.Where(item => item.ModuleId == module.Id && item.ObjectType == "Document")
                    .OrderBy(item => item.Order).Select(item => item.ObjectId).ToHashSet();
                var reportIds = moduleItems.Where(item => item.ModuleId == module.Id && item.ObjectType == "Report")
                    .OrderBy(item => item.Order).Select(item => item.ObjectId).ToHashSet();

                var moduleDocuments = documents.Where(document => documentIds.Contains(document.Id))
                    .OrderBy(document => document.Name)
                    .ToList();
                if (moduleDocuments.Count > 0)
                {
                    var group = new NavigationItem
                    {
                        Id = $"ModuleDocuments:{module.Id}", Name = "Документы", Icon = "📄", Type = "Group"
                    };
                    foreach (var document in moduleDocuments)
                        group.Children.Add(CreateDocumentNavigationItem(document));
                    moduleSection.Children.Add(group);
                }

                var moduleReports = reports.Where(report => reportIds.Contains(report.Id))
                    .OrderBy(report => report.Name)
                    .ToList();
                if (moduleReports.Count > 0)
                {
                    var group = new NavigationItem
                    {
                        Id = $"ModuleReports:{module.Id}", Name = "Отчеты", Icon = "📊", Type = "Group"
                    };
                    foreach (var report in moduleReports)
                        group.Children.Add(new NavigationItem
                        {
                            Id = report.Id.ToString(), Name = report.Name, Icon = report.Icon,
                            Type = "Report", Tag = report, Order = report.Order
                        });
                    moduleSection.Children.Add(group);
                }

                if (module.Code == ModuleMetadataService.FinanceCode)
                {
                    var financeTools = new NavigationItem
                    {
                        Id = "FinanceTools", Name = "Операции и отчетность", Icon = "📈", Type = "Group"
                    };
                    financeTools.Children.Add(new NavigationItem { Id = "Operations", Name = "Операции", Icon = "📋", Type = "Operations" });
                    financeTools.Children.Add(new NavigationItem { Id = "PostingsJournal", Name = "Журнал проводок", Icon = "📋", Type = "PostingsJournal" });
                    financeTools.Children.Add(new NavigationItem { Id = "AccountingReports", Name = "Бухгалтерские отчеты", Icon = "📈", Type = "AccountingReports" });
                    financeTools.Children.Add(new NavigationItem { Id = "AccountingSetup", Name = "Настройка учета", Icon = "⚙", Type = "AccountingSetup" });
                    moduleSection.Children.Add(financeTools);
                }

                if (moduleSection.Children.Count > 0)
                    NavigationItems.Add(moduleSection);
            }

            var unassignedDocuments = documents.Where(document => !assignmentByObject.ContainsKey(document.Id)).ToList();
            var unassignedReports = reports.Where(report => !assignmentByObject.ContainsKey(report.Id)).ToList();
            if (unassignedDocuments.Count > 0 || unassignedReports.Count > 0)
            {
                var otherSection = new NavigationItem
                {
                    Id = "UnassignedSection", Name = "НЕРАСПРЕДЕЛЕННЫЕ ОБЪЕКТЫ", Icon = "📂", Type = "Section"
                };
                foreach (var document in unassignedDocuments.OrderBy(document => document.Name))
                    otherSection.Children.Add(CreateDocumentNavigationItem(document));
                foreach (var report in unassignedReports.OrderBy(report => report.Name))
                    otherSection.Children.Add(new NavigationItem
                    {
                        Id = report.Id.ToString(), Name = report.Name, Icon = report.Icon,
                        Type = "Report", Tag = report, Order = report.Order
                    });
                NavigationItems.Add(otherSection);
            }

            var serviceSection = new NavigationItem
            {
                Id = "ServiceDataSection", Name = "СЛУЖЕБНЫЕ ДАННЫЕ", Icon = "🗄", Type = "Section"
            };
            var dbfCount = await _documentService.GetDocumentsCountAsync();
            serviceSection.Children.Add(new NavigationItem
            {
                Id = "DbfDocuments", Name = "DBF Документы", Icon = "🗄", Type = "DbfDocuments",
                Badge = dbfCount > 0 ? dbfCount.ToString() : string.Empty
            });
            NavigationItems.Add(serviceSection);

            AddHelpAndAdministrationSections();

            if (!_authService.IsAdmin && _authService.CurrentUser != null)
                await ApplyUserPermissionsAsync(_authService.CurrentUser.Id);

            NavigationTree.SelectedItemChanged -= OnNavigationItemSelected;
            NavigationTree.SelectedItemChanged += OnNavigationItemSelected;
        }

        private static NavigationItem CreateDocumentNavigationItem(MetadataObject document)
        {
            var type = document.Name switch
            {
                "Приходный кассовый ордер" or "Расходный кассовый ордер" => "CashOrder",
                "Платежное поручение" => "PaymentOrder",
                "Проводки" => "PostingsDocument",
                InvoiceDocumentTypes.SalesIssue or InvoiceDocumentTypes.PurchaseRegistration => "InvoiceDocument",
                _ => "DynamicDocument"
            };
            return new NavigationItem
            {
                Id = document.Id.ToString(), Name = document.Name, Icon = document.Icon,
                Type = type, Tag = document, Order = document.Order
            };
        }

        private void AddHelpAndAdministrationSections()
        {
            var helpSection = new NavigationItem
            {
                Id = "HelpSection", Name = "СПРАВКА", Icon = "?", Type = "Section"
            };
            helpSection.Children.Add(new NavigationItem
            {
                Id = "AboutSystem", Name = "О системе", Icon = "i", Type = "AboutSystem"
            });
            NavigationItems.Add(helpSection);

            var adminSection = new NavigationItem
            {
                Id = "AdminSection", Name = "АДМИНИСТРИРОВАНИЕ", Icon = "⚙", Type = "Section"
            };
            adminSection.Children.Add(new NavigationItem { Id = "UserProfile", Name = "Профиль", Icon = "👤", Type = "Profile" });
            if (_authService.IsAdmin)
            {
                adminSection.Children.Add(new NavigationItem { Id = "Settings", Name = "Настройки системы", Icon = "⚙", Type = "Settings" });
                adminSection.Children.Add(new NavigationItem { Id = "UserAccessManagement", Name = "Пользователи и права", Icon = "🔐", Type = "UserAccessManagement" });
            }
            adminSection.Children.Add(new NavigationItem { Id = "SwitchMode", Name = "Сменить пользователя или базу", Icon = "🔄", Type = "SwitchMode" });
            adminSection.Children.Add(new NavigationItem { Id = "Logout", Name = "Завершить работу", Icon = "🚪", Type = "Logout" });
            NavigationItems.Add(adminSection);
        }

        private async Task BuildLegacyNavigationTree()
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

                foreach (var catalog in catalogs.OrderBy(c => c.Order).ThenBy(c => c.Name))
                {
                    // Для справочника сотрудников используем кастомный тип
                    if (catalog.Name == "Сотрудники (Списочный состав)")
                    {
                        catalogsGroup.Children.Add(new NavigationItem
                        {
                            Id = catalog.Id.ToString(),
                            Name = "Сотрудники",
                            Icon = catalog.Icon,
                            Type = "EmployeesCatalog",
                            Tag = catalog,
                            Order = catalog.Order
                        });
                    }
                    else
                    {
                        catalogsGroup.Children.Add(new NavigationItem
                        {
                            Id = catalog.Id.ToString(),
                            Name = catalog.Name,
                            Icon = catalog.Icon,
                            Type = "Catalog",
                            Tag = catalog,
                            Order = catalog.Order
                        });
                    }
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

                foreach (var doc in documents.OrderBy(d => d.Order).ThenBy(d => d.Name))
                {
                    // Для кассовых ордеров используем кастомный тип
                    if (doc.Name == "Приходный кассовый ордер" || doc.Name == "Расходный кассовый ордер")
                    {
                        docsGroup.Children.Add(new NavigationItem
                        {
                            Id = doc.Id.ToString(),
                            Name = doc.Name,
                            Icon = doc.Icon,
                            Type = "CashOrder",
                            Tag = doc,
                            Order = doc.Order
                        });
                    }
                    // Для платежных поручений используем кастомный тип
                    else if (doc.Name == "Платежное поручение")
                    {
                        docsGroup.Children.Add(new NavigationItem
                        {
                            Id = doc.Id.ToString(),
                            Name = doc.Name,
                            Icon = doc.Icon,
                            Type = "PaymentOrder",
                            Tag = doc,
                            Order = doc.Order
                        });
                    }
                    // Для документа "Проводки" используем кастомный тип
                    else if (doc.Name == "Проводки")
                    {
                        docsGroup.Children.Add(new NavigationItem
                        {
                            Id = doc.Id.ToString(),
                            Name = doc.Name,
                            Icon = doc.Icon,
                            Type = "PostingsDocument",
                            Tag = doc,
                            Order = doc.Order
                        });
                    }
                    // Для остальных документов - стандартный DynamicDocument
                    else
                    {
                        docsGroup.Children.Add(new NavigationItem
                        {
                            Id = doc.Id.ToString(),
                            Name = doc.Name,
                            Icon = doc.Icon,
                            Type = "DynamicDocument",
                            Tag = doc,
                            Order = doc.Order
                        });
                    }
                }
                dataSection.Children.Add(docsGroup);
            }

            // Импортированные DBF документы
            var dbfCount = await _documentService.GetDocumentsCountAsync();
            dataSection.Children.Add(new NavigationItem
            {
                Id = "DbfDocuments",
                Name = "DBF Документы",
                Icon = "🗄️",
                Type = "DbfDocuments",
                Badge = dbfCount > 0 ? dbfCount.ToString() : ""
            });

            // Операции
            dataSection.Children.Add(new NavigationItem
            {
                Id = "Operations",
                Name = "Операции",
                Icon = "📋",
                Type = "Operations"
            });

            // Журнал проводок
            dataSection.Children.Add(new NavigationItem
            {
                Id = "PostingsJournal",
                Name = "Журнал проводок",
                Icon = "📋",
                Type = "PostingsJournal"
            });

            NavigationItems.Add(dataSection);

            var accountingSection = new NavigationItem
            {
                Id = "AccountingSection",
                Name = "БУХГАЛТЕРСКАЯ ОТЧЕТНОСТЬ",
                Icon = "📊",
                Type = "Section"
            };
            accountingSection.Children.Add(new NavigationItem
            {
                Id = "AccountingReports",
                Name = "Бухгалтерские отчеты",
                Icon = "📈",
                Type = "AccountingReports"
            });
            accountingSection.Children.Add(new NavigationItem
            {
                Id = "AccountingSetup",
                Name = "Настройка учета",
                Icon = "⚙",
                Type = "AccountingSetup"
            });
            NavigationItems.Add(accountingSection);

            // ========== РАЗДЕЛ: ОТЧЕТЫ ==========
            var reports = await _reportService.GetReportsAsync();
            var navigationReports = reports.Where(report => report.IsActive && !report.IsPrintForm).ToList();
            if (navigationReports.Any())
            {
                var reportsSection = new NavigationItem
                {
                    Id = "ReportsSection",
                    Name = "ОТЧЕТЫ",
                    Icon = "📈",
                    Type = "Section"
                };

                foreach (var report in navigationReports.OrderBy(r => r.Order).ThenBy(r => r.Name))
                {
                    reportsSection.Children.Add(new NavigationItem
                    {
                        Id = report.Id.ToString(),
                        Name = report.Name,
                        Icon = report.Icon,
                        Type = "Report",
                        Tag = report,
                        Order = report.Order
                    });
                }
                NavigationItems.Add(reportsSection);
            }

            var helpSection = new NavigationItem
            {
                Id = "HelpSection",
                Name = "СПРАВКА",
                Icon = "?",
                Type = "Section"
            };
            helpSection.Children.Add(new NavigationItem
            {
                Id = "AboutSystem",
                Name = "О системе",
                Icon = "i",
                Type = "AboutSystem"
            });
            NavigationItems.Add(helpSection);

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

            if (_authService.IsAdmin)
            {
                adminSection.Children.Add(new NavigationItem
                {
                    Id = "Settings",
                    Name = "Настройки системы",
                    Icon = "⚙️",
                    Type = "Settings"
                });
                adminSection.Children.Add(new NavigationItem
                {
                    Id = "UserAccessManagement",
                    Name = "Пользователи и права",
                    Icon = "🔐",
                    Type = "UserAccessManagement"
                });
            }

            adminSection.Children.Add(new NavigationItem
            {
                Id = "SwitchMode",
                Name = "Сменить пользователя или базу",
                Icon = "🔄",
                Type = "SwitchMode"
            });

            adminSection.Children.Add(new NavigationItem
            {
                Id = "Logout",
                Name = "Завершить работу",
                Icon = "🚪",
                Type = "Logout"
            });

            NavigationItems.Add(adminSection);

            if (!_authService.IsAdmin && _authService.CurrentUser != null)
                await ApplyUserPermissionsAsync(_authService.CurrentUser.Id);

            // Подписываемся на события выбора
            NavigationTree.SelectedItemChanged -= OnNavigationItemSelected;
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

                    case "EmployeesCatalog":
                        var dbContext = await _infoBaseManager.GetCurrentDbContextAsync();
                        var employeeService = new EmployeeService(dbContext, _metadataService);
                        var employeesView = new EmployeesCatalogView(employeeService, _metadataService);
                        _navigation.NavigateTo(employeesView);
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

                    case "PostingsJournal":
                        var journalContext = await _infoBaseManager.GetCurrentDbContextAsync();
                        var postingService = new PostingService(journalContext);
                        var journalView = new PostingsJournalView(postingService);
                        _navigation.NavigateTo(journalView);
                        break;

                    case "PostingsDocument":
                        if (item.Tag is MetadataObject document1)
                        {
                            var postingsView = new PostingsView(document1, _metadataService);
                            _navigation.NavigateTo(postingsView);
                        }
                        break;

                    case "CashOrder":
                        if (item.Tag is MetadataObject document2)
                        {
                            // Используем кастомное окно для кассовых ордеров
                            var cashOrderView = new CashOrderWorkView(document2, _metadataService);
                            _navigation.NavigateTo(cashOrderView);
                        }
                        break;

                    case "PaymentOrder":
                        if (item.Tag is MetadataObject document3)
                        {
                            var paymentView = new PaymentOrderWorkView(document3, _metadataService);
                            _navigation.NavigateTo(paymentView);
                        }
                        break;

                    case "InvoiceDocument":
                        if (item.Tag is MetadataObject invoiceDocument)
                        {
                            var invoiceView = new InvoiceWorkView(invoiceDocument, _metadataService);
                            _navigation.NavigateTo(invoiceView);
                        }
                        break;

                    case "Report":
                        if (item.Tag is Report report)
                        {
                            await OpenReport(report);
                        }
                        break;

                    case "AccountingReports":
                        var accountingContext = await _infoBaseManager.GetCurrentDbContextAsync();
                        _navigation.NavigateTo(new AccountingReportsView(accountingContext));
                        break;

                    case "AccountingSetup":
                        var setupContext = await _infoBaseManager.GetCurrentDbContextAsync();
                        _navigation.NavigateTo(new AccountingSetupView(setupContext));
                        break;

                    case "Profile":
                        OnProfileClick(null, null);
                        break;

                    case "Settings":
                        OpenSettingsWindow();
                        break;

                    case "UserAccessManagement":
                        var accessContext = await _infoBaseManager.GetCurrentDbContextAsync();
                        _navigation.NavigateTo(new UserAccessManagementView(accessContext, NavigationItems));
                        break;

                    case "AboutSystem":
                        new AboutSystemDialog { Owner = this }.ShowDialog();
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

        private async Task ApplyUserPermissionsAsync(int userId)
        {
            var allowedKeys = await _userAccessService.GetAllowedKeysAsync(userId);
            for (var index = NavigationItems.Count - 1; index >= 0; index--)
            {
                if (!FilterNavigationItem(NavigationItems[index], allowedKeys))
                    NavigationItems.RemoveAt(index);
            }
        }

        private static bool FilterNavigationItem(NavigationItem item, IReadOnlySet<string> allowedKeys)
        {
            var hadChildren = item.Children.Count > 0;
            for (var index = item.Children.Count - 1; index >= 0; index--)
            {
                if (!FilterNavigationItem(item.Children[index], allowedKeys))
                    item.Children.RemoveAt(index);
            }

            if (hadChildren)
                return item.Children.Count > 0;
            if (item.Id is "UserProfile" or "SwitchMode" or "Logout" or "AboutSystem")
                return true;
            return allowedKeys.Contains(item.Id);
        }
      
        // Открытие окна настроек      
        private async void OpenSettingsWindow()
        {
            try
            {
                // Создаем окно настроек
                var settingsWindow = new SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (settingsWindow.ShowDialog() == true)
                {
                    var configuration = await new SystemConfigurationService().GetAsync();
                    SystemNameText.Text = configuration.SystemName;
                    SystemIconText.Text = configuration.Icon;
                    Title = $"{configuration.SystemName} - {_currentInfoBase?.Name}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия настроек: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Drag & Drop

        private void NavigationTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag();
                }
            }
            else
            {
                _dragStartPoint = new Point(0, 0);
            }
        }

        private void StartDrag()
        {
            _draggedItem = GetSelectedNavigationItem();
            if (_draggedItem == null) return;

            // Нельзя перетаскивать заголовки секций
            if (_draggedItem.Type == "Section" || _draggedItem.Type == "Group") return;

            DragDrop.DoDragDrop(NavigationTree, _draggedItem, DragDropEffects.Move);
        }

        private NavigationItem GetSelectedNavigationItem()
        {
            var selectedItem = NavigationTree.SelectedItem;
            return selectedItem as NavigationItem;
        }

        private void NavigationTree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private async void NavigationTree_Drop(object sender, DragEventArgs e)
        {
            var draggedItem = e.Data.GetData(typeof(NavigationItem)) as NavigationItem;
            if (draggedItem == null) return;

            var targetItem = GetTargetNavigationItem(e.GetPosition(NavigationTree));
            if (targetItem == null || targetItem == draggedItem) return;

            // Нельзя перемещать между разными родителями
            if (draggedItem.Type != targetItem.Type) return;

            await ReorderNavigationItems(draggedItem, targetItem);
        }

        private NavigationItem GetTargetNavigationItem(Point point)
        {
            var result = VisualTreeHelper.HitTest(NavigationTree, point);
            if (result == null) return null;

            var treeViewItem = FindVisualParent<TreeViewItem>(result.VisualHit);
            if (treeViewItem == null) return null;

            return treeViewItem.Header as NavigationItem;
        }

        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent)
                    return typedParent;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private async Task ReorderNavigationItems(NavigationItem draggedItem, NavigationItem targetItem)
        {
            // Находим родительские коллекции
            ObservableCollection<NavigationItem> draggedCollection = null;
            ObservableCollection<NavigationItem> targetCollection = null;

            foreach (var section in NavigationItems)
            {
                if (section.Children.Contains(draggedItem))
                    draggedCollection = section.Children;
                if (section.Children.Contains(targetItem))
                    targetCollection = section.Children;

                foreach (var child in section.Children)
                {
                    if (child.Children.Contains(draggedItem))
                        draggedCollection = child.Children;
                    if (child.Children.Contains(targetItem))
                        targetCollection = child.Children;
                }
            }

            if (draggedCollection == null || targetCollection == null) return;
            if (draggedCollection != targetCollection) return;

            var draggedIndex = draggedCollection.IndexOf(draggedItem);
            var targetIndex = targetCollection.IndexOf(targetItem);

            draggedCollection.Move(draggedIndex, targetIndex);

            await SaveOrderToDatabaseAsync(draggedCollection);
        }

        private async Task SaveOrderToDatabaseAsync(ObservableCollection<NavigationItem> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.Order = i + 1;

                if (item.Tag is MetadataObject metadata)
                {
                    metadata.Order = item.Order;
                    await _metadataService.UpdateMetadataObjectOrderAsync(metadata.Id, item.Order);
                }
            }
        }

        #endregion

        private async Task OpenReport(Report report)
        {
            _isLoadingReport = true;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var context = await _infoBaseManager.GetCurrentDbContextAsync();
                var reportService = new ReportService(context);
                var loadedReport = await reportService.GetReportAsync(report.Id) ?? report;
                var data = await reportService.GetReportDataAsync(loadedReport);

                var preview = new ReportPreviewWindow(data, loadedReport, reportService);
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
                ApplicationExitService.ShutdownNow();
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
                _closeForModeSwitch = true;
                this.Close();
            }
        }

        /// <summary>
        /// Переключение темы по кнопке в шапке
        /// </summary>
        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = AppSettings.Instance;

                // Переключаем тему
                var newTheme = settings.Theme == "Dark" ? "Default" : "Dark";

                // Сохраняем в настройках
                settings.Theme = newTheme;
                settings.Save();

                // Применяем тему
                ThemeService.Apply(newTheme);

                // Обновляем иконку кнопки
                if (ThemeToggleButton != null)
                {
                    ThemeToggleButton.Content = newTheme == "Dark" ? "🌞" : "🌙";
                    ThemeToggleButton.ToolTip = newTheme == "Dark" ? "Светлая тема" : "Темная тема";
                }

                // Показываем уведомление (опционально)
                System.Diagnostics.Debug.WriteLine($"✅ Тема изменена на: {newTheme}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка смены темы: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
