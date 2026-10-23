using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Configurator
{
    public partial class ModuleManagementView : UserControl
    {
        // Общий контекст окна конфигуратора здесь не используем: он занят другими
        // редакторами, а DbContext не переносит параллельных операций — вторая
        // падает с «A second operation was started on this context instance…».
        // Каждая операция идёт на своём подключении
        // (см. Docs/Задержки_запуска_и_оптимизация.md, п. 4.6 и 4.8).
        private readonly string _connectionString;
        private readonly ObservableCollection<ModuleObjectRow> _documents = new();
        private readonly ObservableCollection<ModuleObjectRow> _reports = new();
        private readonly ObservableCollection<ModuleObjectRow> _unassigned = new();
        private MetadataModule? _currentModule;
        private int _loadVersion;

        public ModuleManagementView(string connectionString)
        {
            InitializeComponent();
            _connectionString = string.IsNullOrWhiteSpace(connectionString)
                ? throw new ArgumentException("Не задана строка подключения к информационной базе.", nameof(connectionString))
                : connectionString;
            DocumentsGrid.ItemsSource = _documents;
            ReportsGrid.ItemsSource = _reports;
            UnassignedGrid.ItemsSource = _unassigned;
            Loaded += async (_, _) => await OnLoadedAsync();
        }

        private async Task OnLoadedAsync()
        {
            try
            {
                await LoadAsync();
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка загрузки списка модулей.", nameof(ModuleManagementView), ex);
                StatusText.Text = "Не удалось загрузить список модулей.";
            }
        }

        private async Task LoadAsync(Guid? selectId = null)
        {
            // Схема модулей — на своём подключении и до любых чтений: DDL на общем
            // контексте или одновременно с чтениями давал взаимоблокировку 40P01.
            await EnsureModuleSchemaAsync();

            // Посадка системных модулей — тоже на своём подключении (запись в БД).
            await EnsureDefaultModulesAsync();

            var modules = await LoadOnOwnConnectionAsync(context =>
                new ModuleMetadataService(context).GetModulesAsync(true));
            ModulesList.ItemsSource = modules;
            var selected = selectId.HasValue
                ? modules.FirstOrDefault(module => module.Id == selectId)
                : modules.FirstOrDefault();
            ModulesList.SelectedItem = selected;
        }

        private async void OnModuleSelected(object sender, SelectionChangedEventArgs e)
        {
            if (ModulesList.SelectedItem is not MetadataModule module)
                return;
            _currentModule = module;
            NameBox.Text = module.Name;
            CodeBox.Text = module.Code;
            IconBox.Text = module.Icon;
            DescriptionBox.Text = module.Description;
            OrderBox.Text = module.Order.ToString();
            CloseOrderBox.Text = module.CloseOrder.ToString();
            IsActiveCheck.IsChecked = module.IsActive;
            ParticipatesInPeriodCloseCheck.IsChecked = module.ParticipatesInPeriodClose;
            RequirePreviousModulesClosedCheck.IsChecked = module.RequirePreviousModulesClosed;
            CodeBox.IsReadOnly = module.IsSystem;
            DeleteButton.IsEnabled = !module.IsSystem;
            await LoadObjectsAsync(module.Id, module.Code);

            var state = module.IsActive
                ? "Модуль включён."
                : "Модуль ОТКЛЮЧЁН — в рабочем окне не показывается. Включите «Доступен в инфобазе».";

            StatusText.Text = module.IsSystem
                ? $"Системный модуль можно настраивать и отключать, но нельзя удалить. {state}"
                : state;
        }

        private async Task LoadObjectsAsync(Guid moduleId, string moduleCode)
        {
            // Переключение модулей может начать новую загрузку, пока предыдущая
            // ещё идёт: устаревший результат отбрасываем.
            var version = ++_loadVersion;

            try
            {
                var selectedDocuments = _documents.Where(row => row.IsSelected).Select(row => row.Id).ToHashSet();
                var selectedReports = _reports.Where(row => row.IsSelected).Select(row => row.Id).ToHashSet();
                var selectedUnassigned = _unassigned.Where(row => row.IsSelected).Select(row => row.Id).ToHashSet();

                // Шаг A1: без параллельных запросов — три набора последовательно,
                // каждый на своём подключении.
                var allAssignments = await LoadOnOwnConnectionAsync(context =>
                    new ModuleMetadataService(context).GetItemsAsync());
                var documents = await LoadOnOwnConnectionAsync(context => context.MetadataObjects.AsNoTracking()
                    .Where(item => item.ObjectType == "Document").OrderBy(item => item.Name).ToListAsync());
                var reports = await LoadOnOwnConnectionAsync(context => context.Reports.AsNoTracking()
                    .Where(item => !item.IsPrintForm).OrderBy(item => item.Name).ToListAsync());

                if (version != _loadVersion)
                    return;

                var own = allAssignments
                    .Where(item => item.ModuleId == moduleId)
                    .Select(item => item.ObjectId)
                    .ToHashSet();
                var assignedAnywhere = allAssignments
                    .Select(item => item.ObjectId)
                    .ToHashSet();

                var fixedAssets = new HashSet<string>(ModuleMetadataService.FixedAssetDocumentNames, StringComparer.OrdinalIgnoreCase);
                var inventory = new HashSet<string>(DocumentTableNames.InventoryDocumentNames, StringComparer.OrdinalIgnoreCase);

                IEnumerable<MetadataObject> visibleDocuments;
                IEnumerable<Report> visibleReports;

                if (string.Equals(moduleCode, ModuleMetadataService.FixedAssetsCode, StringComparison.OrdinalIgnoreCase))
                {
                    // Строго свой состав: чужие документы в списке ОС не нужны.
                    visibleDocuments = documents.Where(item => fixedAssets.Contains(item.Name) || own.Contains(item.Id));
                    visibleReports = reports.Where(item =>
                        item.Code.StartsWith("standard.fixed-assets.", StringComparison.OrdinalIgnoreCase) ||
                        item.Code.StartsWith("assets.", StringComparison.OrdinalIgnoreCase) ||
                        own.Contains(item.Id));
                }
                else if (string.Equals(moduleCode, ModuleMetadataService.InventoryCode, StringComparison.OrdinalIgnoreCase))
                {
                    visibleDocuments = documents.Where(item => inventory.Contains(item.Name) || own.Contains(item.Id));
                    visibleReports = reports.Where(item =>
                        item.Code.StartsWith("inventory.", StringComparison.OrdinalIgnoreCase) ||
                        own.Contains(item.Id));
                }
                else
                {
                    // Прочие модули: только свои объекты. Неназначенные — на отдельной вкладке.
                    visibleDocuments = documents.Where(item => own.Contains(item.Id));
                    visibleReports = reports.Where(item => own.Contains(item.Id));
                }

                _documents.Clear();
                foreach (var document in visibleDocuments)
                {
                    var row = new ModuleObjectRow(
                        document.Id, document.Name, document.Description,
                        own.Contains(document.Id) || selectedDocuments.Contains(document.Id), "Document");
                    row.PropertyChanged += OnModuleObjectRowChanged;
                    _documents.Add(row);
                }

                _reports.Clear();
                foreach (var report in visibleReports)
                {
                    var row = new ModuleObjectRow(
                        report.Id, report.Name, report.Description,
                        own.Contains(report.Id) || selectedReports.Contains(report.Id), "Report");
                    row.PropertyChanged += OnModuleObjectRowChanged;
                    _reports.Add(row);
                }

                // На вкладке «Неназначенные» — объекты без модуля, которых нет в списках выше.
                var visibleDocumentIds = visibleDocuments.Select(item => item.Id).ToHashSet();
                var visibleReportIds = visibleReports.Select(item => item.Id).ToHashSet();

                _unassigned.Clear();
                foreach (var document in documents.Where(item =>
                             !assignedAnywhere.Contains(item.Id) && !visibleDocumentIds.Contains(item.Id)))
                {
                    var row = new ModuleObjectRow(
                        document.Id, document.Name, document.Description,
                        selectedUnassigned.Contains(document.Id), "Document");
                    row.PropertyChanged += OnModuleObjectRowChanged;
                    _unassigned.Add(row);
                }

                foreach (var report in reports.Where(item =>
                             !assignedAnywhere.Contains(item.Id) && !visibleReportIds.Contains(item.Id)))
                {
                    var row = new ModuleObjectRow(
                        report.Id, report.Name, report.Description,
                        selectedUnassigned.Contains(report.Id), "Report");
                    row.PropertyChanged += OnModuleObjectRowChanged;
                    _unassigned.Add(row);
                }

                UpdateEmptyStates();
            }
            catch (Exception ex)
            {
                SystemLogService.Error(
                    $"Ошибка загрузки состава модуля «{moduleCode}».", nameof(ModuleManagementView), ex);
                StatusText.Text = "Не удалось загрузить состав модуля: " + ex.Message;
            }
        }

        private bool _updatingSelectAll;

        private void UpdateEmptyStates()
        {
            var hasDocuments = _documents.Count > 0;
            DocumentsGrid.Visibility = hasDocuments ? Visibility.Visible : Visibility.Collapsed;
            DocumentsEmptyText.Visibility = hasDocuments ? Visibility.Collapsed : Visibility.Visible;
            SelectAllDocumentsCheck.IsEnabled = hasDocuments;

            var hasReports = _reports.Count > 0;
            ReportsGrid.Visibility = hasReports ? Visibility.Visible : Visibility.Collapsed;
            ReportsEmptyText.Visibility = hasReports ? Visibility.Collapsed : Visibility.Visible;
            SelectAllReportsCheck.IsEnabled = hasReports;

            var hasUnassigned = _unassigned.Count > 0;
            UnassignedGrid.Visibility = hasUnassigned ? Visibility.Visible : Visibility.Collapsed;
            UnassignedEmptyText.Visibility = hasUnassigned ? Visibility.Collapsed : Visibility.Visible;
            SelectAllUnassignedCheck.IsEnabled = hasUnassigned;

            UpdateSelectAllChecks();
        }

        private void UpdateSelectAllChecks()
        {
            _updatingSelectAll = true;
            try
            {
                SelectAllDocumentsCheck.IsChecked = _documents.Count > 0 && _documents.All(row => row.IsSelected);
                SelectAllReportsCheck.IsChecked = _reports.Count > 0 && _reports.All(row => row.IsSelected);
                SelectAllUnassignedCheck.IsChecked = _unassigned.Count > 0 && _unassigned.All(row => row.IsSelected);
            }
            finally
            {
                _updatingSelectAll = false;
            }
        }

        private void OnModuleObjectRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ModuleObjectRow.IsSelected) && !_updatingSelectAll)
                UpdateSelectAllChecks();
        }

        private void OnSelectAllDocumentsChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll)
                return;

            var select = SelectAllDocumentsCheck.IsChecked == true;
            foreach (var row in _documents)
                row.IsSelected = select;
        }

        private void OnSelectAllReportsChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll)
                return;

            var select = SelectAllReportsCheck.IsChecked == true;
            foreach (var row in _reports)
                row.IsSelected = select;
        }

        private void OnSelectAllUnassignedChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll)
                return;

            var select = SelectAllUnassignedCheck.IsChecked == true;
            foreach (var row in _unassigned)
                row.IsSelected = select;
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            _currentModule = new MetadataModule
            {
                Order = 100,
                CloseOrder = 100,
                IsActive = true,
                ParticipatesInPeriodClose = true,
                Icon = "📁"
            };
            ModulesList.SelectedItem = null;
            NameBox.Text = "Новый модуль";
            CodeBox.Text = string.Empty;
            IconBox.Text = "📁";
            DescriptionBox.Text = string.Empty;
            OrderBox.Text = "100";
            CloseOrderBox.Text = "100";
            IsActiveCheck.IsChecked = true;
            ParticipatesInPeriodCloseCheck.IsChecked = true;
            RequirePreviousModulesClosedCheck.IsChecked = false;
            CodeBox.IsReadOnly = false;
            DeleteButton.IsEnabled = false;
            await LoadObjectsAsync(Guid.Empty, string.Empty);
            StatusText.Text = "Укажите свойства и выберите объекты нового модуля.";
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _currentModule ??= new MetadataModule();
                _currentModule.Name = NameBox.Text;
                _currentModule.Code = CodeBox.Text;
                _currentModule.Icon = IconBox.Text;
                _currentModule.Description = DescriptionBox.Text;
                _currentModule.Order = int.TryParse(OrderBox.Text, out var order) ? order : 100;
                _currentModule.CloseOrder = int.TryParse(CloseOrderBox.Text, out var closeOrder) ? closeOrder : _currentModule.Order;
                _currentModule.IsActive = IsActiveCheck.IsChecked == true;
                _currentModule.ParticipatesInPeriodClose = ParticipatesInPeriodCloseCheck.IsChecked == true;
                _currentModule.RequirePreviousModulesClosed = RequirePreviousModulesClosedCheck.IsChecked == true;
                var documentIds = _documents.Where(row => row.IsSelected).Select(row => row.Id)
                    .Concat(_unassigned.Where(row => row.IsSelected && row.ObjectType == "Document").Select(row => row.Id))
                    .ToList();
                var reportIds = _reports.Where(row => row.IsSelected).Select(row => row.Id)
                    .Concat(_unassigned.Where(row => row.IsSelected && row.ObjectType == "Report").Select(row => row.Id))
                    .ToList();

                // Запись — одна единица работы на своём подключении: свой трекер,
                // общий контекст окна не трогаем.
                await SaveModuleWithAssignmentsAsync(_currentModule, documentIds, reportIds);
                StatusText.Text = "Модуль и его состав сохранены.";
                await LoadAsync(_currentModule.Id);
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка сохранения модуля.", nameof(ModuleManagementView), ex);
                MessageBox.Show(ex.Message, "Модули", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_currentModule == null)
                return;
            if (MessageBox.Show($"Удалить модуль «{_currentModule.Name}»? Объекты останутся в конфигурации.",
                    "Модули", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try
            {
                await DeleteModuleAsync(_currentModule.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка удаления модуля.", nameof(ModuleManagementView), ex);
                MessageBox.Show(ex.Message, "Модули", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task SaveModuleWithAssignmentsAsync(
            MetadataModule module,
            IEnumerable<Guid> documentIds,
            IEnumerable<Guid> reportIds)
        {
            await using var context = CreateContext();
            var service = new ModuleMetadataService(context);
            var saved = await service.SaveModuleAsync(module);
            await service.SaveAssignmentsAsync(saved.Id, documentIds, reportIds);
        }

        private async Task DeleteModuleAsync(Guid moduleId)
        {
            await using var context = CreateContext();
            await new ModuleMetadataService(context).DeleteModuleAsync(moduleId);
        }

        private async Task EnsureModuleSchemaAsync()
        {
            await using var context = CreateContext();
            await new ModuleMetadataService(context).EnsureSchemaAsync();
        }

        private async Task EnsureDefaultModulesAsync()
        {
            await using var context = CreateContext();
            await new ModuleMetadataService(context).EnsureDefaultModulesAsync();
        }

        /// <summary>
        /// Чтение на собственном подключении: общий контекст конфигуратора в это
        /// время занят другими редакторами.
        /// </summary>
        private async Task<T> LoadOnOwnConnectionAsync<T>(Func<AppDbContext, Task<T>> load)
        {
            await using var context = CreateContext();
            return await load(context);
        }

        private AppDbContext CreateContext() => new AppDbContext(_connectionString);
    }

    public class ModuleObjectRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        public ModuleObjectRow(Guid id, string name, string description, bool isSelected, string objectType = "Document")
        {
            Id = id; Name = name; Description = description; _isSelected = isSelected; ObjectType = objectType;
        }
        public Guid Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string ObjectType { get; }
        public string TypeName => ObjectType == "Report" ? "Отчет" : "Документ";
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
