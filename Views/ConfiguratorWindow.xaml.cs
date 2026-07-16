using BIS.ERP.Configurator.Views;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace BIS.ERP.Views
{
    public partial class ConfiguratorWindow : Window
    {
        private MetadataService _metadataService;
        private ReportService _reportService;
        private List<MetadataObject> _catalogs;
        private List<MetadataObject> _documents;
        private List<Report> _reports;
        private RegulatedReportTemplateService? _regulatedTemplateService;
        private bool _isLoading = false;
        private AppDbContext _context;
        private bool _closeForModeSwitch;
        private FrameworkElement? _fixedPropertiesContent;
        private string _metadataSearchText = string.Empty;
        private string _catalogSearchText = string.Empty;
        private string _documentSearchText = string.Empty;
        private string _reportSearchText = string.Empty;
        private string _metadataViewMode = "Категории";
        private string _catalogViewMode = "Плитки";
        private string _documentViewMode = "Плитки";
        private string _reportViewMode = "Плитки";

        public ConfiguratorWindow()
        {
            InitializeComponent();
            this.Loaded += async (s, e) => await LoadMetadata();
            this.Closing += OnWindowClosing;
            PropertiesScrollViewer.SizeChanged += (_, _) => UpdateFixedPropertiesContentHeight();
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (ApplicationExitService.IsShuttingDown || _closeForModeSwitch)
                return;

            e.Cancel = true;
            ApplicationExitService.ConfirmAndShutdown(this);
        }

        private async Task LoadMetadata()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                if (!ServiceLocator.AuthService.IsAdmin)
                {
                    MessageBox.Show(
                        "Доступ к конфигуратору разрешен только администратору.",
                        "Доступ запрещен",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    _closeForModeSwitch = true;
                    Close();
                    return;
                }

                var systemConfiguration = await new SystemConfigurationService().GetAsync();
                SystemNameText.Text = systemConfiguration.SystemName;
                LogoDisplayHelper.Apply(SystemLogoImage, SystemIconText, systemConfiguration.LogoImage, GetSystemIcon(systemConfiguration.Icon));
                var currentInfoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();
                if (currentInfoBase != null)
                    LogoDisplayHelper.Apply(InfoBaseLogoImage, InfoBaseIconText, currentInfoBase.LogoImage, currentInfoBase.DisplayIcon);
                InfoBaseNameText.Text = currentInfoBase == null
                    ? "Инфобаза: не выбрана"
                    : $"Инфобаза: {currentInfoBase.Name}";
                InfoBaseNameText.ToolTip = currentInfoBase == null
                    ? "Инфобаза не выбрана"
                    : $"{currentInfoBase.Name}\nБаза: {currentInfoBase.DatabaseName}\nСервер: {currentInfoBase.Host}:{currentInfoBase.Port}";
                Title = currentInfoBase == null
                    ? $"{systemConfiguration.SystemName} - Конфигуратор"
                    : $"{systemConfiguration.SystemName} - Конфигуратор - {currentInfoBase.Name}";

                _context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                await new RuntimeSchemaFixService(_context).EnsureAsync();
                await new SystemConfigurationService(_context).GetAsync();
                var patchService = new BisPatchService(_context);
                await patchService.EnsureSchemaAsync();
                var patchVersion = await patchService.GetCurrentPatchVersionAsync();
                PatchVersionText.Text = string.IsNullOrWhiteSpace(patchVersion)
                    ? "Патч: не применен"
                    : $"Патч: {patchVersion}";
                _metadataService = new MetadataService(_context);
                _reportService = new ReportService(_context);
                await new LocalizationService(_context, AppSettings.Instance.Language).InitializeAsync();
                var printFormService = new PrintFormService(_context);
                await printFormService.EnsureSchemaAsync();
                _regulatedTemplateService = new RegulatedReportTemplateService(_context);
                await _regulatedTemplateService.EnsureSchemaAsync();
                await new DocumentationMetadataSeedService(_context).EnsureAsync();
                await new InvoiceMetadataSeedService(_context).EnsureAsync();
                await printFormService.SeedCashOrderFormsAsync();
                await printFormService.SeedInvoiceFormsAsync();
                await _metadataService.EnsureStandardReportsAsync();

                var allMetadata = await _metadataService.GetAllMetadataObjectsAsync();
                _catalogs = allMetadata.Where(m => m.ObjectType == "Catalog").OrderBy(m => m.Name).ToList();
                _documents = allMetadata.Where(m => m.ObjectType == "Document").OrderBy(m => m.Name).ToList();
                _reports = await _reportService.GetReportHeadersAsync(includePrintForms: true);
                BuildMetadataTree();
                ShowMetadataOverview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки метаданных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private Report? _selectedReport;
        private TreeViewItem? _selectedReportTreeItem;

        private void BuildMetadataTree()
        {
            MetadataTree.Items.Clear();

            var rootItem = new TreeViewItem
            {
                Header = "📁 Метаданные",
                IsExpanded = true
            };
            rootItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowMetadataOverview();
            };

            // Справочники
            var catalogsItem = new TreeViewItem
            {
                Header = "📚 Справочники",
                IsExpanded = true
            };
            catalogsItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowCatalogsList();
            };

            if (_catalogs != null && _catalogs.Any())
            {
                foreach (var catalog in _catalogs)
                {
                    var catalogItem = new TreeViewItem
                    {
                        Header = $"{catalog.Icon} {catalog.Name}",
                        Tag = catalog
                    };
                    catalogItem.Selected += (s, e) =>
                    {
                        e.Handled = true;
                        ShowCatalogEditor(catalog);
                    };
                    catalogsItem.Items.Add(catalogItem);
                }
            }

            // Документы
            var documentsItem = new TreeViewItem
            {
                Header = "📄 Документы",
                IsExpanded = true
            };
            documentsItem.Selected += async (s, e) =>
            {
                e.Handled = true;
                await ShowDocumentsList();
            };

            if (_documents != null && _documents.Any())
            {
                foreach (var doc in _documents)
                {
                    var docItem = new TreeViewItem
                    {
                        Header = $"{doc.Icon} {doc.Name}",
                        Tag = doc
                    };
                    docItem.Selected += (s, e) =>
                    {
                        e.Handled = true;
                        ShowDocumentEditor(doc);
                    };
                    documentsItem.Items.Add(docItem);
                }
            }

            // Отчеты
            var reportsItem = new TreeViewItem
            {
                Header = "📊 Отчеты и печатные формы",
                IsExpanded = true
            };
            reportsItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowReportsList();
            };

            if (_reports != null && _reports.Any())
            {
                foreach (var report in _reports)
                {
                    var reportItem = new TreeViewItem
                    {
                        Header = $"{report.Icon} {report.Name}{(report.IsActive ? string.Empty : " [отключен]")}",
                        Tag = report
                    };
                    reportItem.Selected += (s, e) =>
                    {
                        e.Handled = true;
                        ShowReportEditor(report);
                    };
                    reportItem.Tag = report;
                    reportItem.ContextMenu = (ContextMenu)MetadataTree.Resources["ReportContextMenu"];
                    reportsItem.Items.Add(reportItem);
                }
            }

            rootItem.Items.Add(catalogsItem);
            var modulesItem = new TreeViewItem
            {
                Header = "Модули и разделы"
            };
            modulesItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowModulesEditor();
            };
            rootItem.Items.Add(modulesItem);
            var accountingSetupItem = new TreeViewItem
            {
                Header = "⚙ Настройка бухгалтерского учета"
            };
            accountingSetupItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowAccountingSetupEditor();
            };
            rootItem.Items.Add(accountingSetupItem);
            var usersItem = new TreeViewItem
            {
                Header = "🔐 Пользователи и права"
            };
            usersItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowUsersEditor();
            };
            rootItem.Items.Add(usersItem);
            rootItem.Items.Add(documentsItem);
            rootItem.Items.Add(reportsItem);
            var regulatedTemplatesItem = new TreeViewItem
            {
                Header = "🧾 Шаблоны регламентированных отчетов"
            };
            regulatedTemplatesItem.Selected += async (s, e) =>
            {
                e.Handled = true;
                await ShowRegulatedTemplatesEditorAsync();
            };
            rootItem.Items.Add(regulatedTemplatesItem);
            var translationsItem = new TreeViewItem
            {
                Header = "🌐 Переводы интерфейса"
            };
            translationsItem.Selected += (s, e) =>
            {
                e.Handled = true;
                ShowTranslationsEditor();
            };
            rootItem.Items.Add(translationsItem);
            MetadataTree.Items.Add(rootItem);
        }

        private void ShowModulesEditor()
        {
            if (_context == null)
                return;
            SetPropertiesScrollEnabled(false);
            EditorTitle.Text = "Модули и разделы";
            EditorDescription.Text = "Состав документов и отчетов рабочего интерфейса";
            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(CreateFixedPropertiesHost(
                new BIS.ERP.Views.Configurator.ModuleManagementView(_context)));
        }

        private void ShowAccountingSetupEditor()
        {
            if (_context == null)
                return;
            SetPropertiesScrollEnabled(false);
            EditorTitle.Text = "Настройка бухгалтерского учета";
            EditorDescription.Text = "Служебные параметры учета: входящие остатки, строки отчетности и расчет курсовой разницы";
            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(CreateFixedPropertiesHost(new AccountingSetupView(_context)));
        }

        private void ShowUsersEditor()
        {
            if (_context == null)
                return;
            SetPropertiesScrollEnabled(false);
            EditorTitle.Text = "Пользователи и права";
            EditorDescription.Text = "Пользователи текущей информационной базы и доступ к рабочему окну";
            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(CreateFixedPropertiesHost(
                new UserAccessManagementView(_context, BuildUserAccessNavigationItems(), ServiceLocator.AuthService.CurrentUser)));
        }

        private IEnumerable<BIS.ERP.NavigationItem> BuildUserAccessNavigationItems()
        {
            var items = new List<BIS.ERP.NavigationItem>();
            var catalogsSection = new BIS.ERP.NavigationItem
            {
                Id = "DirectoriesSection",
                Name = "СПРАВОЧНИКИ",
                Type = "Section"
            };
            foreach (var catalog in (_catalogs ?? new List<MetadataObject>()).OrderBy(item => item.Name))
                catalogsSection.Children.Add(new BIS.ERP.NavigationItem
                {
                    Id = catalog.Id.ToString(),
                    Name = catalog.Name,
                    Type = "Catalog",
                    Tag = catalog
                });
            items.Add(catalogsSection);

            var documentsSection = new BIS.ERP.NavigationItem
            {
                Id = "DocumentsSection",
                Name = "ДОКУМЕНТЫ",
                Type = "Section"
            };
            foreach (var document in (_documents ?? new List<MetadataObject>()).OrderBy(item => item.Name))
                documentsSection.Children.Add(new BIS.ERP.NavigationItem
                {
                    Id = document.Id.ToString(),
                    Name = document.Name,
                    Type = "Document",
                    Tag = document
                });
            items.Add(documentsSection);

            var reportsSection = new BIS.ERP.NavigationItem
            {
                Id = "ReportsSection",
                Name = "ОТЧЕТЫ",
                Type = "Section"
            };
            foreach (var report in (_reports ?? new List<Report>()).OrderBy(item => item.Name))
                reportsSection.Children.Add(new BIS.ERP.NavigationItem
                {
                    Id = report.Id.ToString(),
                    Name = report.Name,
                    Type = "Report",
                    Tag = report
                });
            reportsSection.Children.Add(new BIS.ERP.NavigationItem { Id = "PostingsJournal", Name = "Журнал проводок", Type = "PostingsJournal" });
            reportsSection.Children.Add(new BIS.ERP.NavigationItem { Id = "AccountingReports", Name = "Бухгалтерские отчеты", Type = "AccountingReports" });
            reportsSection.Children.Add(new BIS.ERP.NavigationItem { Id = "MutualSettlements", Name = "Взаиморасчеты с организациями", Type = "MutualSettlements" });
            items.Add(reportsSection);
            return items;
        }

        private FrameworkElement CreateFixedPropertiesHost(UIElement content)
        {
            var host = new Grid
            {
                MinHeight = 360
            };
            host.Children.Add(content);
            _fixedPropertiesContent = host;
            UpdateFixedPropertiesContentHeight();
            return host;
        }

        private void SetPropertiesScrollEnabled(bool enabled)
        {
            _fixedPropertiesContent = null;
            PropertiesScrollViewer.VerticalScrollBarVisibility = enabled
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;
        }

        private void UpdateFixedPropertiesContentHeight()
        {
            if (_fixedPropertiesContent == null || PropertiesScrollViewer.ActualHeight <= 80)
                return;

            // Учитываем Padding=20 у внутренней белой панели, чтобы внешний ScrollViewer не перехватывал прокрутку.
            _fixedPropertiesContent.Height = Math.Max(360, PropertiesScrollViewer.ActualHeight - 42);
        }

        private void OnModulesClick(object sender, RoutedEventArgs e) => ShowModulesEditor();

        private void OnAccountingSetupClick(object sender, RoutedEventArgs e) => ShowAccountingSetupEditor();

        private void OnUsersClick(object sender, RoutedEventArgs e) => ShowUsersEditor();

        private async void OnRegulatedTemplatesClick(object sender, RoutedEventArgs e) =>
            await ShowRegulatedTemplatesEditorAsync();

        private async void ShowTranslationsEditor()
        {
            SetPropertiesScrollEnabled(true);
            if (_context == null)
                return;

            EditorTitle.Text = "🌐 Переводы интерфейса";
            EditorDescription.Text = "Единый словарь подписей и системных значений";
            var entries = new ObservableCollection<LocalizationEntry>(await _context.LocalizationEntries
                .OrderBy(entry => entry.Culture).ThenBy(entry => entry.Category).ThenBy(entry => entry.Key)
                .ToListAsync());
            var grid = new DataGrid
            {
                ItemsSource = entries,
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                MinHeight = 500,
                Margin = new Thickness(0, 8, 0, 0)
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Язык", Binding = new Binding(nameof(LocalizationEntry.Culture)), Width = 85 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Ключ", Binding = new Binding(nameof(LocalizationEntry.Key)), Width = 220 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Перевод", Binding = new Binding(nameof(LocalizationEntry.Value)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Категория", Binding = new Binding(nameof(LocalizationEntry.Category)), Width = 120 });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Активен", Binding = new Binding(nameof(LocalizationEntry.IsActive)), Width = 75 });

            var saveButton = new Button { Content = "Сохранить переводы", Width = 160, Height = 34 };
            saveButton.Click += async (_, _) =>
            {
                try
                {
                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Culture) || string.IsNullOrWhiteSpace(entry.Key))
                            throw new InvalidOperationException("Язык и ключ перевода обязательны.");
                        if (_context.Entry(entry).State == EntityState.Detached)
                            await _context.LocalizationEntries.AddAsync(entry);
                        entry.UpdatedAt = DateTime.UtcNow;
                    }
                    await _context.SaveChangesAsync();
                    if (LocalizationService.Current != null)
                        await LocalizationService.Current.SetCultureAsync(AppSettings.Instance.Language);
                    MessageBox.Show("Переводы сохранены.", "Локализация", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Локализация", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var deleteButton = new Button { Content = "Удалить строку", Width = 130, Height = 34, Margin = new Thickness(8, 0, 0, 0) };
            deleteButton.Click += async (_, _) =>
            {
                if (grid.SelectedItem is not LocalizationEntry selected)
                    return;
                entries.Remove(selected);
                if (_context.Entry(selected).State != EntityState.Detached)
                {
                    _context.LocalizationEntries.Remove(selected);
                    await _context.SaveChangesAsync();
                }
            };

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            toolbar.Children.Add(saveButton);
            toolbar.Children.Add(deleteButton);
            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(toolbar);
            PropertiesPanel.Children.Add(grid);
        }

        private async Task ShowRegulatedTemplatesEditorAsync()
        {
            SetPropertiesScrollEnabled(true);
            if (_context == null)
                return;

            _regulatedTemplateService ??= new RegulatedReportTemplateService(_context);
            await _regulatedTemplateService.EnsureSchemaAsync();

            EditorTitle.Text = "🧾 Шаблоны регламентированных отчетов";
            EditorDescription.Text = "Хранение Excel-шаблонов в БД с автоматическим использованием в прикладных отчетах";

            var templates = new ObservableCollection<RegulatedReportTemplate>(
                await _regulatedTemplateService.GetTemplatesAsync());

            var note = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFrom("#F4ECF7"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock
                {
                    Text = "Для экспорта НДС система автоматически берет активный шаблон с кодом STI-062_7. " +
                           "Шаблон хранится в БД, а временный файл создается только на время выгрузки и затем удаляется.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)new BrushConverter().ConvertFrom("#4A235A")
                }
            };

            var grid = new DataGrid
            {
                ItemsSource = templates,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                IsReadOnly = true,
                MinHeight = 460,
                Margin = new Thickness(0, 8, 0, 0),
                SelectionMode = DataGridSelectionMode.Single
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Код", Binding = new Binding(nameof(RegulatedReportTemplate.Code)), Width = 130 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Название", Binding = new Binding(nameof(RegulatedReportTemplate.Name)), Width = 220 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Версия", Binding = new Binding(nameof(RegulatedReportTemplate.Version)), Width = 90 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Файл", Binding = new Binding(nameof(RegulatedReportTemplate.OriginalFileName)), Width = 170 });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Активен", Binding = new Binding(nameof(RegulatedReportTemplate.IsActive)), Width = 75 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Размер", Binding = new Binding(nameof(RegulatedReportTemplate.TemplateSizeDisplay)), Width = 90 });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Обновлен",
                Binding = new Binding(nameof(RegulatedReportTemplate.UpdatedAt)) { StringFormat = "dd.MM.yyyy HH:mm" },
                Width = 130
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Описание",
                Binding = new Binding(nameof(RegulatedReportTemplate.Description)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });

            var uploadButton = new Button
            {
                Content = "Загрузить шаблон",
                Width = 150,
                Height = 34,
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            uploadButton.Click += async (_, _) =>
            {
                var openDialog = new OpenFileDialog
                {
                    Title = "Выберите Excel-шаблон регламентированного отчета",
                    Filter = "Excel шаблоны (*.xls;*.xlsx)|*.xls;*.xlsx|Все файлы (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (openDialog.ShowDialog(this) != true)
                    return;

                var draft = RegulatedReportTemplateService.InferDraftFromFileName(openDialog.FileName);
                var dialog = new RegulatedTemplateUploadDialog(openDialog.FileName, draft) { Owner = this };
                if (dialog.ShowDialog() != true)
                    return;

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _regulatedTemplateService.SaveTemplateAsync(openDialog.FileName, dialog.Draft);
                    await ReloadRegulatedTemplatesAsync(templates);
                    MessageBox.Show("Шаблон сохранен в БД.", "Шаблоны", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки шаблона: {ex.Message}", "Шаблоны",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var activateButton = new Button
            {
                Content = "Сделать активным",
                Width = 150,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#2980B9"),
                Foreground = Brushes.White
            };
            activateButton.Click += async (_, _) =>
            {
                if (grid.SelectedItem is not RegulatedReportTemplate selected)
                    return;

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _regulatedTemplateService.SetActiveTemplateAsync(selected.Id);
                    await ReloadRegulatedTemplatesAsync(templates);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка активации шаблона: {ex.Message}", "Шаблоны",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var editButton = new Button
            {
                Content = "Изменить реквизиты",
                Width = 160,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#8E44AD"),
                Foreground = Brushes.White
            };
            editButton.Click += async (_, _) =>
            {
                if (grid.SelectedItem is not RegulatedReportTemplate selected)
                    return;

                var draft = new RegulatedReportTemplateDraft
                {
                    Code = selected.Code,
                    Name = selected.Name,
                    Version = selected.Version,
                    Description = selected.Description,
                    IsActive = selected.IsActive
                };

                var dialog = new RegulatedTemplateUploadDialog(selected.OriginalFileName, draft)
                {
                    Owner = this,
                    Title = "Реквизиты шаблона"
                };

                if (dialog.ShowDialog() != true)
                    return;

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _regulatedTemplateService.UpdateTemplateMetadataAsync(selected.Id, dialog.Draft);
                    await ReloadRegulatedTemplatesAsync(templates);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка изменения реквизитов: {ex.Message}", "Шаблоны",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var exportButton = new Button
            {
                Content = "Выгрузить копию",
                Width = 140,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            exportButton.Click += async (_, _) =>
            {
                if (grid.SelectedItem is not RegulatedReportTemplate selected)
                    return;

                var defaultExtension = string.Equals(selected.FileExtension, ".xls", StringComparison.OrdinalIgnoreCase)
                    ? "xls"
                    : "xlsx";
                var saveDialog = new SaveFileDialog
                {
                    Title = "Сохранить копию шаблона",
                    Filter = "Excel файлы (*.xlsx)|*.xlsx|Excel 97-2003 (*.xls)|*.xls",
                    DefaultExt = defaultExtension,
                    FileName = string.IsNullOrWhiteSpace(selected.OriginalFileName)
                        ? $"{selected.Code}_{selected.Version}.{defaultExtension}"
                        : selected.OriginalFileName
                };
                if (saveDialog.ShowDialog(this) != true)
                    return;

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _regulatedTemplateService.ExportTemplateCopyAsync(selected.Id, saveDialog.FileName);
                    MessageBox.Show("Копия шаблона сохранена.", "Шаблоны", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка выгрузки шаблона: {ex.Message}", "Шаблоны",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var deleteButton = new Button
            {
                Content = "Удалить",
                Width = 110,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#C0392B"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (_, _) =>
            {
                if (grid.SelectedItem is not RegulatedReportTemplate selected)
                    return;

                if (MessageBox.Show(
                        $"Удалить шаблон \"{selected.Name}\" ({selected.Code}) из БД?",
                        "Шаблоны",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _regulatedTemplateService.DeleteTemplateAsync(selected.Id);
                    await ReloadRegulatedTemplatesAsync(templates);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления шаблона: {ex.Message}", "Шаблоны",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var refreshButton = new Button
            {
                Content = "Обновить",
                Width = 110,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0)
            };
            refreshButton.Click += async (_, _) => await ReloadRegulatedTemplatesAsync(templates);

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            toolbar.Children.Add(uploadButton);
            toolbar.Children.Add(activateButton);
            toolbar.Children.Add(editButton);
            toolbar.Children.Add(exportButton);
            toolbar.Children.Add(deleteButton);
            toolbar.Children.Add(refreshButton);

            PropertiesPanel.Children.Clear();
            PropertiesPanel.Children.Add(note);
            PropertiesPanel.Children.Add(toolbar);
            PropertiesPanel.Children.Add(grid);
        }

        private async Task ReloadRegulatedTemplatesAsync(ObservableCollection<RegulatedReportTemplate> target)
        {
            if (_regulatedTemplateService == null)
                return;

            var items = await _regulatedTemplateService.GetTemplatesAsync();
            target.Clear();
            foreach (var item in items)
                target.Add(item);
        }

        // ДИНАМИЧЕСКИЕ МЕТОДЫ СОЗДАНИЯ
        private async void OnCreateDynamicCatalogClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateDynamicObjectDialog("Catalog");
            dialog.Owner = this;
            dialog.Title = "Создание справочника";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var newObject = new MetadataObject
                    {
                        Name = dialog.ObjectName,
                        ObjectType = "Catalog",
                        Description = dialog.Description,
                        Icon = dialog.ObjectIcon,
                        TableName = $"catalog_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                        Order = (_catalogs?.Count ?? 0) + 1,
                        IsSystem = false,
                        UsePostings = false,
                        UseBalances = false,
                        UseMovements = false,
                        Fields = new List<MetadataField>()
                    };

                    await _metadataService.CreateMetadataObjectAsync(newObject);
                    await _metadataService.CreateDynamicTableAsync(newObject);
                    await LoadMetadata();

                    MessageBox.Show($"Справочник '{dialog.ObjectName}' успешно создан!",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private async void OnCreateDynamicDocumentClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateDynamicObjectDialog("Document");
            dialog.Owner = this;
            dialog.Title = "Создание документа";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var newObject = new MetadataObject
                    {
                        Name = dialog.ObjectName,
                        ObjectType = "Document",
                        Description = dialog.Description,
                        Icon = dialog.ObjectIcon,
                        TableName = $"doc_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                        Order = (_documents?.Count ?? 0) + 1,
                        IsSystem = false,
                        UsePostings = dialog.UsePostings,
                        UseBalances = dialog.UseBalances,
                        UseMovements = false,
                        Fields = new List<MetadataField>()
                    };

                    await _metadataService.CreateMetadataObjectAsync(newObject);
                    await _metadataService.CreateDynamicTableAsync(newObject);
                    await LoadMetadata();

                    // ОТКРЫВАЕМ РЕДАКТОР ДЛЯ СОЗДАННОГО ОБЪЕКТА
                    var editorView = new DynamicObjectsConfigView(_metadataService);
                    // Нужно передать выбранный объект в редактор
                    editorView.LoadObject(newObject);

                    // Открываем в новой вкладке или окне
                    var tabItem = new TabItem { Header = newObject.Name, Content = editorView };
                    // Добавляем в TabControl (если есть) или открываем окно

                    MessageBox.Show($"Документ '{dialog.ObjectName}' успешно создан! Теперь добавьте поля.",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }



        // ОТОБРАЖЕНИЕ СПИСКОВ
        private void ShowMetadataOverview()
        {
            SetPropertiesScrollEnabled(true);
            _selectedReport = null;
            _selectedReportTreeItem = null;
            DeleteReportMenuItem.IsEnabled = false;

            EditorTitle.Text = "📁 Метаданные";
            EditorDescription.Text = "Общий список справочников, документов, отчетов и печатных форм";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();
            var contentPanel = new StackPanel();
            var toolbar = CreateSearchAndViewToolbar(
                _metadataSearchText,
                _metadataViewMode,
                new[] { "Категории", "Список" },
                (search, viewMode) =>
                {
                    _metadataSearchText = search;
                    _metadataViewMode = viewMode;
                    FillMetadataOverview(contentPanel);
                });
            stackPanel.Children.Add(toolbar);
            stackPanel.Children.Add(contentPanel);
            FillMetadataOverview(contentPanel);

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void FillMetadataOverview(Panel target)
        {
            target.Children.Clear();

            var catalogs = (_catalogs ?? new List<MetadataObject>())
                .Where(item => MatchesSearch(item.Name, item.TableName, item.Description, _metadataSearchText))
                .OrderBy(item => item.Name)
                .ToList();
            var documents = (_documents ?? new List<MetadataObject>())
                .Where(item => MatchesSearch(item.Name, item.TableName, item.Description, _metadataSearchText))
                .OrderBy(item => item.Name)
                .ToList();
            var reports = (_reports ?? new List<Report>())
                .Where(item => !item.IsPrintForm)
                .Where(item => MatchesSearch(item.Name, item.Description, item.Code, _metadataSearchText))
                .OrderBy(item => item.Name)
                .ToList();
            var printForms = (_reports ?? new List<Report>())
                .Where(item => item.IsPrintForm)
                .Where(item => MatchesSearch(item.Name, item.Description, item.Code, _metadataSearchText))
                .OrderBy(item => item.Name)
                .ToList();

            if (_metadataViewMode == "Список")
            {
                AddCompactSectionRows(target, "📚 Справочники", catalogs.Select(CreateCompactCatalogCard), "Справочники не найдены.");
                AddCompactSectionRows(target, "📄 Документы", documents.Select(CreateCompactDocumentCard), "Документы не найдены.");
                AddCompactSectionRows(target, "📊 Отчеты", reports.Select(CreateCompactReportCard), "Отчеты не найдены.");
                AddCompactSectionRows(target, "🧾 Печатные формы", printForms.Select(CreateCompactReportCard), "Печатные формы не найдены.");
                return;
            }

            var wrapPanel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            wrapPanel.Children.Add(CreateMetadataCategoryBlock("📚 Справочники", catalogs.Count, catalogs.Select(CreateCompactCatalogCard), "Справочники не найдены."));
            wrapPanel.Children.Add(CreateMetadataCategoryBlock("📄 Документы", documents.Count, documents.Select(CreateCompactDocumentCard), "Документы не найдены."));
            wrapPanel.Children.Add(CreateMetadataCategoryBlock("📊 Отчеты", reports.Count, reports.Select(CreateCompactReportCard), "Отчеты не найдены."));
            wrapPanel.Children.Add(CreateMetadataCategoryBlock("🧾 Печатные формы", printForms.Count, printForms.Select(CreateCompactReportCard), "Печатные формы не найдены."));
            target.Children.Add(wrapPanel);
        }

        private Border CreateMetadataCategoryBlock(
            string title,
            int count,
            IEnumerable<UIElement> cards,
            string emptyText)
        {
            var block = new Border
            {
                Width = 360,
                MinHeight = 260,
                MaxHeight = 520,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 14, 14),
                BorderThickness = new Thickness(1)
            };
            block.SetResourceReference(Border.BackgroundProperty, "AppSurfaceBrush");
            block.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var titleText = new TextBlock
            {
                Text = $"{title}",
                FontSize = 16,
                FontWeight = FontWeights.Bold
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "AppBodyTextBrush");
            header.Children.Add(titleText);
            var countText = new TextBlock
            {
                Text = $"{count}",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            countText.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
            DockPanel.SetDock(countText, Dock.Right);
            header.Children.Add(countText);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var stackPanel = new StackPanel();
            var added = false;
            foreach (var card in cards)
            {
                stackPanel.Children.Add(card);
                added = true;
            }

            if (!added)
            {
                var emptyBlock = new TextBlock
                {
                    Text = emptyText,
                    Margin = new Thickness(5, 18, 5, 5),
                    TextAlignment = TextAlignment.Center
                };
                emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
                stackPanel.Children.Add(emptyBlock);
            }

            var scrollViewer = new ScrollViewer
            {
                Content = stackPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 445
            };
            Grid.SetRow(scrollViewer, 1);
            grid.Children.Add(scrollViewer);

            block.Child = grid;
            return block;
        }

        private static void AddCompactSectionRows(
            Panel target,
            string title,
            IEnumerable<UIElement> cards,
            string emptyText)
        {
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 10)
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppBodyTextBrush");
            target.Children.Add(titleBlock);

            var added = false;
            foreach (var card in cards)
            {
                target.Children.Add(card);
                added = true;
            }

            if (!added)
            {
                var emptyBlock = new TextBlock { Text = emptyText, Margin = new Thickness(8, 0, 0, 12) };
                emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
                target.Children.Add(emptyBlock);
            }

            target.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });
        }

        private static void AddResponsiveTiles(
            Panel target,
            IEnumerable<UIElement> cards,
            string emptyText)
        {
            var wrapPanel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var added = false;
            foreach (var card in cards)
            {
                if (card is FrameworkElement element)
                {
                    element.Width = 360;
                    element.Margin = new Thickness(0, 0, 14, 14);
                }

                wrapPanel.Children.Add(card);
                added = true;
            }

            if (!added)
            {
                var emptyBlock = new TextBlock
                {
                    Text = emptyText,
                    Margin = new Thickness(8, 18, 8, 18),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                emptyBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
                target.Children.Add(emptyBlock);
                return;
            }

            target.Children.Add(wrapPanel);
        }

        private static Grid CreateSearchAndViewToolbar(
            string searchText,
            string selectedViewMode,
            IReadOnlyList<string> viewModes,
            Action<string, string> onChanged)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var searchPanel = new DockPanel { LastChildFill = true };
            var searchLabel = new TextBlock
            {
                Text = "Поиск:",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            searchLabel.SetResourceReference(TextBlock.ForegroundProperty, "AppBodyTextBrush");
            DockPanel.SetDock(searchLabel, Dock.Left);
            searchPanel.Children.Add(searchLabel);

            var searchBox = new TextBox
            {
                Text = searchText,
                Height = 36,
                MinWidth = 260,
                Padding = new Thickness(10, 7, 10, 7),
                ToolTip = "Поиск по названию, таблице, коду или описанию"
            };
            searchBox.SetResourceReference(Control.BackgroundProperty, "AppInputBackgroundBrush");
            searchBox.SetResourceReference(Control.ForegroundProperty, "AppInputForegroundBrush");
            searchBox.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");
            searchPanel.Children.Add(searchBox);
            Grid.SetColumn(searchPanel, 0);
            grid.Children.Add(searchPanel);

            var viewCombo = new ComboBox
            {
                Height = 36,
                Width = 190,
                SelectedValuePath = "Content"
            };
            foreach (var mode in viewModes)
                viewCombo.Items.Add(new ComboBoxItem { Content = mode });
            var selectedIndex = viewModes.ToList().IndexOf(selectedViewMode);
            viewCombo.SelectedIndex = Math.Max(0, selectedIndex);

            var viewPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            viewPanel.Children.Add(new TextBlock
            {
                Text = "Вид:",
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            viewPanel.Children.Add(viewCombo);
            Grid.SetColumn(viewPanel, 2);
            grid.Children.Add(viewPanel);

            var isApplying = false;
            void RaiseChanged()
            {
                if (isApplying)
                    return;

                isApplying = true;
                try
                {
                    var viewMode = (viewCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? selectedViewMode;
                    onChanged(searchBox.Text, viewMode);
                }
                finally
                {
                    isApplying = false;
                }
            }

            searchBox.TextChanged += (_, _) => RaiseChanged();
            viewCombo.SelectionChanged += (_, _) => RaiseChanged();
            return grid;
        }

        private static bool MatchesSearch(string? first, string? second, string? third, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            var search = searchText.Trim();
            return Contains(first, search) || Contains(second, search) || Contains(third, search);
        }

        private static bool Contains(string? value, string search) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(search, StringComparison.OrdinalIgnoreCase);

        private Border CreateCompactCatalogCard(MetadataObject catalog) =>
            CreateCompactMetadataObjectCard(
                catalog.Icon,
                catalog.Name,
                catalog.TableName,
                $"Полей: {catalog.Fields?.Count ?? 0}",
                () => ShowCatalogEditor(catalog));

        private Border CreateCompactDocumentCard(MetadataObject document) =>
            CreateCompactMetadataObjectCard(
                document.Icon,
                document.Name,
                document.TableName,
                $"Полей: {document.Fields?.Count ?? 0}",
                () => ShowDocumentEditor(document));

        private Border CreateCompactImportedDocumentCard(DynamicDocument document) =>
            CreateCompactCard(
                "📄",
                $"№{document.Number}",
                $"{document.DocumentType}; {document.Date:dd.MM.yyyy}",
                $"Строк: {document.TotalRows}; файл: {document.SourceFile}",
                () => _ = ShowDynamicDocumentDetails(document),
                "📋",
                "Детали");

        private Border CreateCompactReportCard(Report report)
        {
            var typeText = report.IsPrintForm ? "Печатная форма" : "Отчет";
            var statusText = report.IsActive ? "Доступен" : "Отключен";
            var card = CreateCompactCard(
                string.IsNullOrWhiteSpace(report.Icon) ? "📊" : report.Icon,
                report.Name,
                $"{typeText}; {report.SourceFormat}",
                statusText,
                () => ShowReportEditor(report));

            if (card.Child is Grid grid && grid.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 2) is { } actions)
            {
                var availabilityButton = CreateCompactActionButton(report.IsActive ? "⏸" : "✓", report.IsActive ? "Отключить" : "Включить");
                availabilityButton.Background = (Brush)new BrushConverter().ConvertFrom(report.IsActive ? "#D68910" : "#27AE60");
                availabilityButton.Foreground = Brushes.White;
                availabilityButton.Click += async (_, _) => await ToggleReportAvailabilityAndShowListAsync(report);
                actions.Children.Add(availabilityButton);

                var deleteButton = CreateCompactActionButton("🗑", "Удалить");
                deleteButton.Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C");
                deleteButton.Foreground = Brushes.White;
                deleteButton.Click += async (_, _) => await DeleteReportAndShowListAsync(report);
                actions.Children.Add(deleteButton);
            }

            return card;
        }

        private Border CreateCompactMetadataObjectCard(
            string icon,
            string title,
            string subtitle,
            string detail,
            Action editAction) =>
            CreateCompactCard(icon, title, subtitle, detail, editAction);

        private Border CreateCompactCard(
            string icon,
            string title,
            string subtitle,
            string detail,
            Action editAction,
            string actionContent = "✏",
            string actionTooltip = "Редактировать")
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1)
            };
            card.SetResourceReference(Border.BackgroundProperty, "AppSurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(icon) ? "📄" : icon,
                FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            var info = new StackPanel { Margin = new Thickness(8, 0, 8, 0) };
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppBodyTextBrush");
            info.Children.Add(titleBlock);

            var subtitleBlock = new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            subtitleBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
            info.Children.Add(subtitleBlock);

            var detailBlock = new TextBlock
            {
                Text = detail,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            detailBlock.SetResourceReference(TextBlock.ForegroundProperty, "AppSecondaryTextBrush");
            info.Children.Add(detailBlock);
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var editButton = CreateCompactActionButton(actionContent, actionTooltip);
            editButton.Background = (Brush)new BrushConverter().ConvertFrom("#3498DB");
            editButton.Foreground = Brushes.White;
            editButton.Click += (_, _) => editAction();
            actions.Children.Add(editButton);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        private static Button CreateCompactActionButton(string content, string tooltip) =>
            new()
            {
                Content = content,
                Width = 30,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = tooltip,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

        private void ShowCatalogsList()
        {
            SetPropertiesScrollEnabled(true);
            EditorTitle.Text = "📚 Справочники";
            EditorDescription.Text = "Список справочников в системе";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();
            var contentPanel = new StackPanel();
            var toolbar = CreateSearchAndViewToolbar(
                _catalogSearchText,
                _catalogViewMode,
                new[] { "Плитки", "Список" },
                (search, viewMode) =>
                {
                    _catalogSearchText = search;
                    _catalogViewMode = viewMode;
                    FillCatalogsList(contentPanel);
                });
            stackPanel.Children.Add(toolbar);
            stackPanel.Children.Add(contentPanel);
            FillCatalogsList(contentPanel);

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void FillCatalogsList(Panel target)
        {
            target.Children.Clear();

            var catalogs = (_catalogs ?? new List<MetadataObject>())
                .Where(item => MatchesSearch(item.Name, item.TableName, item.Description, _catalogSearchText))
                .OrderBy(item => item.Name)
                .ToList();

            if (_catalogViewMode == "Список")
            {
                foreach (var catalog in catalogs)
                    target.Children.Add(CreateCatalogCard(catalog));

                if (!catalogs.Any())
                    AddResponsiveTiles(target, Enumerable.Empty<UIElement>(), "Справочники не найдены.");

                return;
            }

            AddResponsiveTiles(target, catalogs.Select(CreateCompactCatalogCard), "Справочники не найдены.");
        }

        private async Task ShowDocumentsList()
        {
            SetPropertiesScrollEnabled(true);
            EditorTitle.Text = "📄 Документы";
            EditorDescription.Text = "Управление документами";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();
            var contentPanel = new StackPanel();
            var toolbar = CreateSearchAndViewToolbar(
                _documentSearchText,
                _documentViewMode,
                new[] { "Плитки", "Список" },
                (search, viewMode) =>
                {
                    _documentSearchText = search;
                    _documentViewMode = viewMode;
                    _ = FillDocumentsListAsync(contentPanel);
                });
            stackPanel.Children.Add(toolbar);

            var importButton = new Button
            {
                Content = "📁 Импорт из DBF",
                Height = 40,
                Margin = new Thickness(0, 0, 0, 14),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            importButton.Click += (s, e) => OnImportDbfClick(s, e);
            stackPanel.Children.Add(importButton);
            stackPanel.Children.Add(contentPanel);
            PropertiesPanel.Children.Add(stackPanel);

            await FillDocumentsListAsync(contentPanel);
        }

        private async Task FillDocumentsListAsync(Panel target)
        {
            target.Children.Clear();
            var tileBlocks = _documentViewMode == "Плитки"
                ? new WrapPanel { HorizontalAlignment = HorizontalAlignment.Stretch }
                : null;
            if (tileBlocks != null)
                target.Children.Add(tileBlocks);

            var metadataDocuments = (_documents ?? new List<MetadataObject>())
                .Where(item => MatchesSearch(item.Name, item.TableName, item.Description, _documentSearchText))
                .OrderBy(item => item.Name)
                .ToList();

            if (_documentViewMode == "Список")
            {
                AddCompactSectionRows(
                    target,
                    "📄 Документы из метаданных",
                    metadataDocuments.Select(CreateDynamicMetadataCard),
                    "Документы из метаданных не найдены.");
            }
            else
            {
                tileBlocks!.Children.Add(CreateMetadataCategoryBlock(
                    "📄 Документы из метаданных",
                    metadataDocuments.Count,
                    metadataDocuments.Select(CreateCompactDocumentCard),
                    "Документы из метаданных не найдены."));
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var documentService = new DocumentService(context);
                var importedDocuments = (await documentService.GetDocumentsAsync())
                    .Where(item => MatchesSearch(item.Number, item.DocumentType, item.SourceFile, _documentSearchText))
                    .OrderByDescending(item => item.Date)
                    .ToList();

                if (_documentViewMode == "Список")
                {
                    AddCompactSectionRows(
                        target,
                        "📥 Импортированные DBF документы",
                        importedDocuments.Select(CreateDynamicDocumentCard),
                        "Импортированные DBF документы не найдены.");
                }
                else
                {
                    tileBlocks!.Children.Add(CreateMetadataCategoryBlock(
                        "📥 Импортированные DBF документы",
                        importedDocuments.Count,
                        importedDocuments.Select(CreateCompactImportedDocumentCard),
                        "Импортированные DBF документы не найдены."));
                }
            }
            catch (Exception ex)
            {
                target.Children.Add(new TextBlock
                {
                    Text = $"Ошибка загрузки DBF документов: {ex.Message}",
                    Foreground = Brushes.Red,
                    Margin = new Thickness(10)
                });
            }
        }

        private void ShowReportsList()
        {
            SetPropertiesScrollEnabled(true);
            _selectedReport = null;
            _selectedReportTreeItem = null;
            DeleteReportMenuItem.IsEnabled = false;

            EditorTitle.Text = "📊 Отчеты и печатные формы";
            EditorDescription.Text = "Список отчетов и печатных форм в системе";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();
            var contentPanel = new StackPanel();
            var toolbar = CreateSearchAndViewToolbar(
                _reportSearchText,
                _reportViewMode,
                new[] { "Плитки", "Список" },
                (search, viewMode) =>
                {
                    _reportSearchText = search;
                    _reportViewMode = viewMode;
                    FillReportsList(contentPanel);
                });
            stackPanel.Children.Add(toolbar);

            var createButton = new Button
            {
                Content = "➕ Создать отчет",
                Height = 40,
                Margin = new Thickness(0, 0, 0, 14),
                Background = (Brush)new BrushConverter().ConvertFrom("#9B59B6"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            createButton.Click += OnCreateReportClick;
            stackPanel.Children.Add(createButton);
            stackPanel.Children.Add(contentPanel);
            FillReportsList(contentPanel);

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void FillReportsList(Panel target)
        {
            target.Children.Clear();

            var reports = (_reports ?? new List<Report>())
                .Where(item => MatchesSearch(item.Name, item.Description, item.Code, _reportSearchText))
                .OrderBy(item => item.IsPrintForm)
                .ThenBy(item => item.Name)
                .ToList();

            if (_reportViewMode == "Список")
            {
                foreach (var report in reports)
                    target.Children.Add(CreateReportCard(report));

                if (!reports.Any())
                    AddResponsiveTiles(target, Enumerable.Empty<UIElement>(), "Отчеты и печатные формы не найдены.");

                return;
            }

            AddResponsiveTiles(
                target,
                reports.Select(CreateCompactReportCard),
                "Отчеты и печатные формы не найдены.");
        }

        private Border CreateReportCard(Report report)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = report,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(report.Icon) ? "📊" : report.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var typeText = report.IsPrintForm ? "Печатная форма" : "Отчет";
            var availabilityText = report.IsActive ? "Доступен" : "Отключен";
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = report.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"{typeText}; формат: {report.SourceFormat}; статус: {availabilityText}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(report.Description) ? "Описание не указано" : report.Description,
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(infoStack, 1);

            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 120,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (_, _) => ShowReportEditor(report);
            actionsPanel.Children.Add(editButton);

            var availabilityButton = new Button
            {
                Content = report.IsActive ? "Отключить" : "Включить",
                Width = 92,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)new BrushConverter().ConvertFrom(report.IsActive ? "#D68910" : "#27AE60"),
                Foreground = Brushes.White
            };
            availabilityButton.Click += async (_, _) => await ToggleReportAvailabilityAndShowListAsync(report);
            actionsPanel.Children.Add(availabilityButton);

            var deleteButton = new Button
            {
                Content = "🗑️",
                Width = 42,
                Height = 32,
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White,
                ToolTip = "Удалить отчет"
            };
            deleteButton.Click += async (_, _) => await DeleteReportAndShowListAsync(report);
            actionsPanel.Children.Add(deleteButton);

            Grid.SetColumn(actionsPanel, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(actionsPanel);
            card.Child = grid;

            return card;
        }

        private async Task RefreshReportsTreeAndListAsync()
        {
            _reports = await _reportService.GetReportHeadersAsync(includePrintForms: true);
            BuildMetadataTree();
            ShowReportsList();
        }

        private async Task ToggleReportAvailabilityAndShowListAsync(Report report)
        {
            await new PrintFormService(_context).SetAvailabilityAsync(report.Id, !report.IsActive);
            await RefreshReportsTreeAndListAsync();
        }

        private async Task DeleteReportAndShowListAsync(Report report)
        {
            var result = MessageBox.Show(
                $"Удалить отчет \"{report.Name}\"?\n\nЭто действие нельзя отменить.",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await _reportService.DeleteReportAsync(report.Id);
                await RefreshReportsTreeAndListAsync();
                MessageBox.Show($"Отчет \"{report.Name}\" удален.", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Карточка для динамического документа (метаданных)
        private Border CreateDynamicMetadataCard(MetadataObject doc)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = doc,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = doc.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = doc.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Таблица: {doc.TableName}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {doc.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 80,
                Height = 30,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowDocumentEditor(doc);
            Grid.SetColumn(editButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            card.Child = grid;

            return card;
        }

        // Создание карточки для DynamicDocument
        private Border CreateDynamicDocumentCard(DynamicDocument doc)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "📄",
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = $"№{doc.Number} от {doc.Date:dd.MM.yyyy HH:mm:ss}",
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Тип: {doc.DocumentType}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Строк: {doc.TotalRows} | Файл: {doc.SourceFile}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var detailButton = new Button
            {
                Content = "📋 Детали",
                Width = 80,
                Height = 30,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            detailButton.Click += async (s, e) => await ShowDynamicDocumentDetails(doc);
            Grid.SetColumn(detailButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(detailButton);
            card.Child = grid;

            return card;
        }

        private async Task ShowOperationsList()
        {
            SetPropertiesScrollEnabled(true);
            EditorTitle.Text = "📋 Операции";
            EditorDescription.Text = "Список всех операций";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var documentService = new DocumentService(context);
            var documents = await documentService.GetDocumentsAsync();

            // Таблица с документами
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 400,
                Margin = new Thickness(0, 0, 0, 10)
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Номер", Binding = new System.Windows.Data.Binding("Number"), Width = 120 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Дата", Binding = new System.Windows.Data.Binding("Date"), Width = 120 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Сумма", Binding = new System.Windows.Data.Binding("TotalAmount"), Width = 100 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Статус", Binding = new System.Windows.Data.Binding("IsPosted"), Width = 80 });

            dataGrid.ItemsSource = documents;
            stackPanel.Children.Add(dataGrid);

            // Кнопка обновления
            var refreshButton = new Button
            {
                Content = "🔄 Обновить",
                Width = 100,
                Height = 35,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            refreshButton.Click += async (s, e) => await ShowOperationsList();
            stackPanel.Children.Add(refreshButton);

            PropertiesPanel.Children.Add(stackPanel);
        }

        // Показ деталей документа
        private async Task ShowDynamicDocumentDetails(DynamicDocument doc)
        {
            SetPropertiesScrollEnabled(true);
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var documentService = new DocumentService(context);

            // Получаем первую строку для отображения полей
            var firstRow = doc.Rows?.FirstOrDefault();
            Dictionary<string, object> rowData = new Dictionary<string, object>();

            if (firstRow != null)
            {
                rowData = documentService.GetRowData(firstRow);
            }

            var details = $"📄 Документ №{doc.Number}\n" +
                          $"📅 Дата: {doc.Date:dd.MM.yyyy HH:mm:ss}\n" +
                          $"📂 Тип: {doc.DocumentType}\n" +
                          $"📁 Файл: {doc.SourceFile}\n" +
                          $"📊 Строк: {doc.TotalRows}\n\n" +
                          $"📋 Поля в документе:\n";

            foreach (var field in rowData.Keys.OrderBy(k => k))
            {
                var value = rowData[field]?.ToString();
                if (value?.Length > 50) value = value.Substring(0, 50) + "...";
                details += $"  • {field}: {value}\n";
            }

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                Width = 500,
                Content = new TextBlock
                {
                    Text = details,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Margin = new Thickness(10)
                }
            };

            var window = new Window
            {
                Title = $"Детали документа №{doc.Number}",
                Content = scrollViewer,
                Width = 550,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            window.ShowDialog();
        }
        private Border CreateCatalogCard(MetadataObject obj)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = obj,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = obj.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = obj.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = obj.TableName,
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {obj.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 110,
                Height = 35,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowCatalogEditor(obj);
            Grid.SetColumn(editButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            card.Child = grid;

            return card;
        }

        private void ShowCatalogEditor(MetadataObject obj)
        {
            SetPropertiesScrollEnabled(true);
            EditorTitle.Text = $"✏️ Редактирование: {obj.Name}";
            EditorDescription.Text = $"Таблица: {obj.TableName} | Тип: {(obj.ObjectType == "Catalog" ? "Справочник" : "Документ")}";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel();
            mainPanel.Children.Add(CreateBackToMetadataListButton(obj));

            var tabControl = new TabControl { Margin = new Thickness(0, 0, 0, 10) };

            // ==================== ВКЛАДКА "ОСНОВНЫЕ" ====================
            var basicTab = new TabItem { Header = "📋 Основные" };
            var basicPanel = new StackPanel { Margin = new Thickness(10) };

            basicPanel.Children.Add(new TextBlock { Text = "Наименование:", FontWeight = FontWeights.Bold });
            var nameBox = new TextBox { Text = obj.Name, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(nameBox);

            basicPanel.Children.Add(new TextBlock { Text = "Описание:", FontWeight = FontWeights.Bold });
            var descBox = new TextBox { Text = obj.Description, Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(descBox);

            basicPanel.Children.Add(new TextBlock { Text = "Иконка:", FontWeight = FontWeights.Bold });
            var iconBox = new TextBox { Text = obj.Icon, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(iconBox);

            basicPanel.Children.Add(new TextBlock { Text = "Порядок:", FontWeight = FontWeights.Bold });
            var orderBox = new TextBox { Text = obj.Order.ToString(), Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(orderBox);

            var optionsPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
            var usePostingsCheck = new CheckBox { Content = "Использует проводки", IsChecked = obj.UsePostings, Margin = new Thickness(0, 0, 15, 0) };
            var useBalancesCheck = new CheckBox { Content = "Использует балансы", IsChecked = obj.UseBalances, Margin = new Thickness(0, 0, 15, 0) };
            var useMovementsCheck = new CheckBox { Content = "Использует движения", IsChecked = obj.UseMovements };
            optionsPanel.Children.Add(usePostingsCheck);
            optionsPanel.Children.Add(useBalancesCheck);
            optionsPanel.Children.Add(useMovementsCheck);
            basicPanel.Children.Add(optionsPanel);

            basicTab.Content = basicPanel;
            tabControl.Items.Add(basicTab);

            // ==================== ВКЛАДКА "ПОЛЯ" ====================
            var fieldsTab = new TabItem { Header = "📊 Поля" };
            var fieldsPanel = new StackPanel { Margin = new Thickness(10) };

            var fieldsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 300,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Имя поля", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Колонка БД", Binding = new System.Windows.Data.Binding("DbColumnName"), Width = 150 });
            fieldsGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Тип",
                SelectedItemBinding = new System.Windows.Data.Binding("FieldType"),
                ItemsSource = new List<string> { "String", "Int", "Decimal", "DateTime", "Bool", "Reference"},
                Width = 100
            });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Длина", Binding = new System.Windows.Data.Binding("Length"), Width = 80 });
            fieldsGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Обязательное", Binding = new System.Windows.Data.Binding("IsRequired"), Width = 100 });
            fieldsGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Уникальное", Binding = new System.Windows.Data.Binding("IsUnique"), Width = 100 });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("Order"), Width = 80 });

            // ========== КОЛОНКА "СПРАВОЧНИК" - ТЕПЕРЬ С ВЫПАДАЮЩИМ СПИСКОМ ==========
            var referenceColumn = new DataGridComboBoxColumn
            {
                Header = "Справочник",
                SelectedValueBinding = new System.Windows.Data.Binding("ReferenceCatalog"),
                Width = 150
            };

            // ========== НОВЫЕ ПОЛЯ ==========
            // DisplayPattern - шаблон отображения
            var displayPatternColumn = new DataGridTextColumn
            {
                Header = "Шаблон отображения",
                Binding = new System.Windows.Data.Binding("DisplayPattern"),
                Width = 150
            };
            fieldsGrid.Columns.Add(displayPatternColumn);

            // DisplayFields - поля для подстановки
            var displayFieldsColumn = new DataGridTextColumn
            {
                Header = "Поля для подстановки",
                Binding = new System.Windows.Data.Binding("DisplayFields"),
                Width = 150
            };
            fieldsGrid.Columns.Add(displayFieldsColumn);

            // Загружаем список доступных справочников
            var catalogNames = _catalogs?.Select(c => c.Name).ToList() ?? new List<string>();
            referenceColumn.ItemsSource = catalogNames;
            fieldsGrid.Columns.Add(referenceColumn);

            var fieldsList = new System.Collections.ObjectModel.ObservableCollection<MetadataField>();
            if (obj.Fields != null)
            {
                // Удаляем дубликаты по имени
                var uniqueFields = obj.Fields
                    .GroupBy(f => f.Name)
                    .Select(g => g.First())
                    .ToList();

                foreach (var field in uniqueFields.OrderBy(f => f.Order))
                {
                    fieldsList.Add(field);
                }
            }
            fieldsGrid.ItemsSource = fieldsList;

            fieldsPanel.Children.Add(fieldsGrid);

            var addFieldButton = new Button
            {
                Content = "+ Добавить поле",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addFieldButton.Click += (s, args) =>
            {
                fieldsList.Add(new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Новое_поле",
                    DbColumnName = "new_field",
                    FieldType = "String",
                    Length = 100,
                    Order = fieldsList.Count + 1,
                    MetadataObjectId = obj.Id,
                    ReferenceCatalog = null,
                    DisplayPattern = null,     
                    DisplayFields = null 
                });
            };
            fieldsPanel.Children.Add(addFieldButton);

            fieldsTab.Content = fieldsPanel;
            tabControl.Items.Add(fieldsTab);

            // ==================== ВКЛАДКА "РАСЧЕТЫ" ====================
            var calculationsTab = new TabItem { Header = "🧮 Расчеты" };
            var calcPanel = new StackPanel { Margin = new Thickness(10) };

            var calcGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 200,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Наименование", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            calcGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Тип расчета",
                SelectedItemBinding = new System.Windows.Data.Binding("CalculationType"),
                ItemsSource = new List<string> { "Depreciation", "Sum", "Average", "Formula" },
                Width = 150
            });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Целевое поле", Binding = new System.Windows.Data.Binding("TargetField"), Width = 150 });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Формула", Binding = new System.Windows.Data.Binding("Formula"), Width = 200 });
            calcGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Авто", Binding = new System.Windows.Data.Binding("IsAuto"), Width = 80 });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("ExecutionOrder"), Width = 80 });

            var calcList = new System.Collections.ObjectModel.ObservableCollection<MetadataCalculation>();
            if (obj.Calculations != null)
            {
                foreach (var calc in obj.Calculations)
                {
                    calcList.Add(calc);
                }
            }
            calcGrid.ItemsSource = calcList;

            calcPanel.Children.Add(calcGrid);

            var addCalcButton = new Button
            {
                Content = "+ Добавить расчет",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addCalcButton.Click += (s, args) =>
            {
                calcList.Add(new MetadataCalculation
                {
                    Id = Guid.NewGuid(),
                    Name = "Новый_расчет",
                    CalculationType = "Formula",
                    IsAuto = true,
                    ExecutionOrder = calcList.Count + 1,
                    MetadataObjectId = obj.Id
                });
            };
            calcPanel.Children.Add(addCalcButton);

            calculationsTab.Content = calcPanel;
            tabControl.Items.Add(calculationsTab);

            // ==================== ВКЛАДКА "ПРОВОДКИ" ====================
            var postingsTab = new TabItem { Header = "📝 Проводки" };
            var postingsPanel = new StackPanel { Margin = new Thickness(10) };

            var postingGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 250,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Наименование", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Дебет", Binding = new System.Windows.Data.Binding("DebitAccountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Кредит", Binding = new System.Windows.Data.Binding("CreditAccountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Сумма", Binding = new System.Windows.Data.Binding("AmountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Условие", Binding = new System.Windows.Data.Binding("Condition"), Width = 150 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("Order"), Width = 80 });

            var postingList = new System.Collections.ObjectModel.ObservableCollection<MetadataPostingRule>();
            if (obj.PostingRules != null)
            {
                foreach (var rule in obj.PostingRules)
                {
                    postingList.Add(rule);
                }
            }
            postingGrid.ItemsSource = postingList;

            postingsPanel.Children.Add(postingGrid);

            var addRuleButton = new Button
            {
                Content = "+ Добавить проводку",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addRuleButton.Click += (s, args) =>
            {
                postingList.Add(new MetadataPostingRule
                {
                    Id = Guid.NewGuid(),
                    Name = "Новая_проводка",
                    DebitAccountExpression = "{AccountDebet}",
                    CreditAccountExpression = "{AccountCredit}",
                    AmountExpression = "{Amount}",
                    Order = postingList.Count + 1,
                    MetadataObjectId = obj.Id
                });
            };
            postingsPanel.Children.Add(addRuleButton);

            // Подсказка по выражениям
            var hintText = new TextBlock
            {
                Text = "💡 Доступные выражения: {FieldName} - подстановка значения поля\nПример: {Amount} * 0.2 - НДС 20%",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            postingsPanel.Children.Add(hintText);

            postingsTab.Content = postingsPanel;
            tabControl.Items.Add(postingsTab);

            mainPanel.Children.Add(tabControl);

            // Кнопки сохранения и удаления
            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };

            var saveButton = new Button
            {
                Content = "💾 Сохранить",
                Height = 35,
                Width = 120,
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            saveButton.Click += async (s, e) =>
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    obj.Name = nameBox.Text;
                    obj.Description = descBox.Text;
                    obj.Icon = iconBox.Text;
                    obj.Order = int.TryParse(orderBox.Text, out var order) ? order : obj.Order;
                    obj.UsePostings = usePostingsCheck.IsChecked ?? false;
                    obj.UseBalances = useBalancesCheck.IsChecked ?? false;
                    obj.UseMovements = useMovementsCheck.IsChecked ?? false;
                    obj.Fields = fieldsList.ToList();
                    obj.Calculations = calcList.ToList();
                    obj.PostingRules = postingList.ToList();

                    await _metadataService.UpdateMetadataObjectAsync(obj);
                    await LoadMetadata();

                    MessageBox.Show("Сохранено!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var deleteButton = new Button
            {
                Content = "🗑️ Удалить",
                Height = 35,
                Width = 120,
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (s, e) => await DeleteDynamicObject(obj);

            buttonsPanel.Children.Add(saveButton);
            buttonsPanel.Children.Add(deleteButton);
            mainPanel.Children.Add(buttonsPanel);

            PropertiesPanel.Children.Add(mainPanel);
        }

        private void ShowDocumentEditor(MetadataObject obj)
        {
            ShowCatalogEditor(obj); // Используем тот же редактор, так как функционал одинаковый
        }      

        private Button CreateBackToMetadataListButton(MetadataObject obj)
        {
            var isDocument = obj.ObjectType == "Document";
            var button = new Button
            {
                Content = isDocument ? "← К списку документов" : "← К списку справочников",
                Height = 34,
                MinWidth = 190,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 14),
                Cursor = Cursors.Hand
            };

            button.SetResourceReference(Control.BackgroundProperty, "ConfiguratorSidebarCardBrush");
            button.SetResourceReference(Control.ForegroundProperty, "ConfiguratorTreeItemForegroundBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "AppBorderBrush");

            button.Click += async (_, _) =>
            {
                if (isDocument)
                    await ShowDocumentsList();
                else
                    ShowCatalogsList();
            };

            return button;
        }

        private void ShowReportEditor(Report report)
        {
            SetPropertiesScrollEnabled(true);
            _selectedReport = report;
            DeleteReportMenuItem.IsEnabled = true;

            EditorTitle.Text = $"✏️ Редактирование отчета: {report.Name}";
            EditorDescription.Text = "";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel { MaxWidth = 620 };
            mainPanel.Children.Add(new TextBlock
            {
                Text = $"{report.Description}\nСтатус: {report.AvailabilityDisplay}; формат: {report.SourceFormat}; " +
                       $"тип: {(report.IsPrintForm ? "печатная форма" : "отчет")}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            });

            var designerButton = new Button
            {
                Content = "Открыть профессиональный конструктор",
                Height = 40,
                MinWidth = 260,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            designerButton.Click += async (s, e) =>
            {
                var fullReport = await LoadFullReportAsync(report);
                var designer = new ReportDesignerWindow(fullReport) { Owner = this };
                if (designer.ShowDialog() == true)
                    await LoadMetadata();
            };
            mainPanel.Children.Add(designerButton);

            var availabilityButton = new Button
            {
                Content = report.IsActive ? "Отключить форму" : "Сделать доступной",
                Height = 36,
                MinWidth = 180,
                Margin = new Thickness(0, 10, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom(report.IsActive ? "#D68910" : "#27AE60"),
                Foreground = Brushes.White
            };
            availabilityButton.Click += async (_, _) =>
            {
                await new PrintFormService(_context).SetAvailabilityAsync(report.Id, !report.IsActive);
                await LoadMetadata();
            };
            mainPanel.Children.Add(availabilityButton);

            // Кнопка удаления
            var deleteButton = new Button
            {
                Content = "🗑️ Удалить отчет",
                Height = 36,
                MinWidth = 180,
                Margin = new Thickness(0, 10, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (_, _) => await DeleteSelectedReport(report);
            mainPanel.Children.Add(deleteButton);

            // Предпросмотр
            if (report.SourceFormat == "FoxProFRX" && !string.IsNullOrWhiteSpace(report.Template))
            {
                var previewButton = new Button
                {
                    Content = "🚫 Предпросмотр печатной формы",
                    Height = 36,
                    MinWidth = 180,
                    Margin = new Thickness(0, 10, 0, 0),
                    Background = (Brush)new BrushConverter().ConvertFrom("#8E44AD"),
                    Foreground = Brushes.White
                };
                previewButton.Click += (_, _) =>
                {
                    try
                    {
                        var pdf = new PrintFormService(_context).ExportTemplatePreview(report);
                        var tempFile = Path.Combine(Path.GetTempPath(), $"preview_{report.Id:N}.pdf");
                        File.WriteAllBytes(tempFile, pdf);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempFile,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка предпросмотра: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                mainPanel.Children.Add(previewButton);
            }

            PropertiesPanel.Children.Add(mainPanel);
        }

        private async Task<Report> LoadFullReportAsync(Report report)
        {
            await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            return await new ReportService(context).GetReportAsync(report.Id) ?? report;
        }

        private async Task DeleteSelectedReport(Report? report = null)
        {
            report ??= _selectedReport;
            if (report == null) return;

            var result = MessageBox.Show(
                $"Удалить отчет \"{report.Name}\"?\n\nЭто действие нельзя отменить.",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _reportService.DeleteReportAsync(report.Id);
                    await LoadMetadata();
                    PropertiesPanel.Children.Clear();
                    EditorTitle.Text = "📊 Отчет удален";
                    EditorDescription.Text = "";
                    _selectedReport = null;
                    DeleteReportMenuItem.IsEnabled = false;
                    MessageBox.Show($"Отчет \"{report.Name}\" удален.", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void OnDeleteReportClick(object sender, RoutedEventArgs e)
        {
            _ = DeleteSelectedReport();
        }

        private void OnTreeSelectedChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is Report report)
            {
                _selectedReport = report;
                _selectedReportTreeItem = item;
                DeleteReportMenuItem.IsEnabled = true;
            }
            else if (e.NewValue is TreeViewItem && !(e.NewValue as TreeViewItem)?.Header?.ToString()?.Contains("Отчет") == true)
            {
                _selectedReport = null;
                DeleteReportMenuItem.IsEnabled = false;
            }
        }

        private void OnTreeRightClick(object sender, MouseButtonEventArgs e)
        {
            var treeItem = sender as TreeViewItem;
            if (treeItem == null)
            {
                // Находим TreeViewItem под курсором
                var element = e.OriginalSource as FrameworkElement;
                while (element != null && element is not TreeViewItem)
                    element = VisualTreeHelper.GetParent(element) as FrameworkElement;
                treeItem = element as TreeViewItem;
            }

            if (treeItem?.Tag is Report report)
            {
                _selectedReport = report;
                _selectedReportTreeItem = treeItem;
                DeleteReportMenuItem.IsEnabled = true;
                treeItem.IsSelected = true;

                // Показываем контекстное меню
                var contextMenu = (ContextMenu)MetadataTree.Resources["ReportContextMenu"];
                contextMenu.PlacementTarget = treeItem;
                contextMenu.IsOpen = true;
            }
        }

        private async void OnOpenReportDesignerClick(object sender, RoutedEventArgs e)
        {
            if (_selectedReport != null)
            {
                var fullReport = await LoadFullReportAsync(_selectedReport);
                var designer = new ReportDesignerWindow(fullReport) { Owner = this };
                if (designer.ShowDialog() == true)
                    await LoadMetadata();
            }
        }

        private async void OnToggleReportAvailabilityClick(object sender, RoutedEventArgs e)
        {
            if (_selectedReport != null)
            {
                await new PrintFormService(_context).SetAvailabilityAsync(_selectedReport.Id, !_selectedReport.IsActive);
                await LoadMetadata();
            }
        }

        private async void OnPreviewReportPdfClick(object sender, RoutedEventArgs e)
        {
            if (_selectedReport == null) return;
            try
            {
                var fullReport = await LoadFullReportAsync(_selectedReport);
                var pdf = new PrintFormService(_context).ExportTemplatePreview(fullReport);
                var tempFile = Path.Combine(Path.GetTempPath(), $"preview_{_selectedReport.Id:N}.pdf");
                File.WriteAllBytes(tempFile, pdf);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предпросмотра: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnPreviewReportClick(object sender, RoutedEventArgs e)
        {
            if (_selectedReport == null) return;
            try
            {
                var fullReport = await LoadFullReportAsync(_selectedReport);
                var pdf = new PrintFormService(_context).ExportTemplatePreview(fullReport);
                var tempFile = Path.Combine(Path.GetTempPath(), $"preview_{_selectedReport.Id:N}.pdf");
                File.WriteAllBytes(tempFile, pdf);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предпросмотра: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ОСТАЛЬНЫЕ МЕТОДЫ
        private async void OnCreateCatalogClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateCatalogDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnCreateReportClick(object sender, RoutedEventArgs e)
        {
            var designer = new ReportDesignerWindow();
            designer.Owner = this;
            if (designer.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnImportFrxClick(object sender, RoutedEventArgs e)
        {
            var dialog = new FrxImportWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnExportConfigurationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_context == null)
                    _context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();

                var dialog = new SaveFileDialog
                {
                    Title = "Выгрузить конфигурацию",
                    Filter = "BIS encrypted configuration (*.bisconfig)|*.bisconfig|Legacy JSON (*.bisconfig.json)|*.bisconfig.json|JSON (*.json)|*.json",
                    FileName = $"bis_configuration_{DateTime.Now:yyyyMMdd_HHmm}.bisconfig"
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                Mouse.OverrideCursor = Cursors.Wait;
                var exchange = new ConfigurationExchangeService(_context);
                if (dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    await exchange.ExportAsync(dialog.FileName);
                else
                    await exchange.ExportEncryptedAsync(dialog.FileName);
                MessageBox.Show("Конфигурация выгружена.", "Выгрузка конфигурации",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выгрузки конфигурации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnImportConfigurationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_context == null)
                    _context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();

                var dialog = new OpenFileDialog
                {
                    Title = "Загрузить конфигурацию",
                    Filter = "BIS configuration (*.bisconfig;*.bisconfig.json)|*.bisconfig;*.bisconfig.json|Encrypted BIS (*.bisconfig)|*.bisconfig|JSON (*.json)|*.json|Все файлы (*.*)|*.*",
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                var result = MessageBox.Show(
                    "Загрузка заменит текущую структуру конфигурации, отчеты и данные динамических таблиц. Продолжить?",
                    "Загрузка конфигурации",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                Mouse.OverrideCursor = Cursors.Wait;
                var package = await new ConfigurationExchangeService(_context).ImportEncryptedOrJsonAsync(dialog.FileName);
                await LoadMetadata();
                MessageBox.Show(
                    $"Конфигурация загружена.\nОбъектов: {package.MetadataObjects.Count}\nОтчетов: {package.Reports.Count}\nТаблиц данных: {package.TableData.Count}",
                    "Загрузка конфигурации",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки конфигурации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnPatchesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_context == null)
                    _context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();

                var dialog = new PatchManagerDialog(_context) { Owner = this };
                dialog.ShowDialog();
                await LoadMetadata();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия патчей: {ex.Message}", "Патчи",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAppUpdatesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new AppUpdateManagerDialog { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия обновлений программы: {ex.Message}", "Обновления программы",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFoxProAnalysisClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new FoxProAnalysisDialog { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия анализа FoxPro: {ex.Message}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadMetadata();
        }

        private void OnSwitchModeClick(object sender, RoutedEventArgs e)
        {
            var modeWindow = new InfoBaseSelectionWindow();
            modeWindow.Show();
            _closeForModeSwitch = true;
            this.Close();
        }

        private void OnImportDbfClick(object sender, RoutedEventArgs e)
        {
            var dialog = new DbfImportWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OnLeftNavigationPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            LeftNavigationScrollViewer.ScrollToVerticalOffset(
                LeftNavigationScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            ApplicationExitService.ConfirmAndShutdown(this);
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            new AboutSystemDialog { Owner = this }.ShowDialog();
        }

        private async void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(allowSystemLogoManagement: true) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;
            var configuration = await new SystemConfigurationService().GetAsync();
            SystemNameText.Text = configuration.SystemName;
            LogoDisplayHelper.Apply(SystemLogoImage, SystemIconText, configuration.LogoImage, GetSystemIcon(configuration.Icon));
            var currentInfoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();
            if (currentInfoBase != null)
                LogoDisplayHelper.Apply(InfoBaseLogoImage, InfoBaseIconText, currentInfoBase.LogoImage, currentInfoBase.DisplayIcon);
            InfoBaseNameText.Text = currentInfoBase == null
                ? "Инфобаза: не выбрана"
                : $"Инфобаза: {currentInfoBase.Name}";
            InfoBaseNameText.ToolTip = currentInfoBase == null
                ? "Инфобаза не выбрана"
                : $"{currentInfoBase.Name}\nБаза: {currentInfoBase.DatabaseName}\nСервер: {currentInfoBase.Host}:{currentInfoBase.Port}";
            Title = currentInfoBase == null
                ? $"{configuration.SystemName} - Конфигуратор"
                : $"{configuration.SystemName} - Конфигуратор - {currentInfoBase.Name}";
        }

        private static string GetSystemIcon(string? icon)
        {
            return string.IsNullOrWhiteSpace(icon) ? "⚙" : icon.Trim();
        }


        private async Task DeleteDynamicObject(MetadataObject obj)
        {
            var result = MessageBox.Show(
                $"Удалить объект '{obj.Name}'?\n\nВНИМАНИЕ! Все данные будут потеряны!",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _metadataService.DeleteMetadataObjectAsync(obj.Id);
                    await LoadMetadata();

                    MessageBox.Show("Объект удален!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }
    }
}
