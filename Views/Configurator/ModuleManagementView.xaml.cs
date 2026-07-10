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
        private readonly AppDbContext _context;
        private readonly ModuleMetadataService _moduleService;
        private MetadataModule? _currentModule;
        private readonly ObservableCollection<ModuleObjectRow> _documents = new();
        private readonly ObservableCollection<ModuleObjectRow> _reports = new();

        public ModuleManagementView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _moduleService = new ModuleMetadataService(context);
            DocumentsGrid.ItemsSource = _documents;
            ReportsGrid.ItemsSource = _reports;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync(Guid? selectId = null)
        {
            await _moduleService.EnsureDefaultModulesAsync();
            var modules = await _moduleService.GetModulesAsync(true);
            ModulesList.ItemsSource = modules;
            var selected = selectId.HasValue ? modules.FirstOrDefault(module => module.Id == selectId) : modules.FirstOrDefault();
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
            await LoadObjectsAsync(module.Id);
            StatusText.Text = module.IsSystem ? "Системный модуль можно настраивать и отключать, но нельзя удалить." : string.Empty;
        }

        private async Task LoadObjectsAsync(Guid moduleId)
        {
            var assignments = (await _moduleService.GetItemsAsync())
                .Where(item => item.ModuleId == moduleId).Select(item => item.ObjectId).ToHashSet();
            var documents = await _context.MetadataObjects.AsNoTracking()
                .Where(item => item.ObjectType == "Document").OrderBy(item => item.Name).ToListAsync();
            var reports = await _context.Reports.AsNoTracking()
                .Where(item => !item.IsPrintForm).OrderBy(item => item.Name).ToListAsync();
            _documents.Clear();
            foreach (var document in documents)
                _documents.Add(new ModuleObjectRow(document.Id, document.Name, document.Description, assignments.Contains(document.Id)));
            _reports.Clear();
            foreach (var report in reports)
                _reports.Add(new ModuleObjectRow(report.Id, report.Name, report.Description, assignments.Contains(report.Id)));
        }

        private void OnAddClick(object sender, RoutedEventArgs e)
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
            foreach (var row in _documents.Concat(_reports)) row.IsSelected = false;
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
                var saved = await _moduleService.SaveModuleAsync(_currentModule);
                await _moduleService.SaveAssignmentsAsync(saved.Id,
                    _documents.Where(row => row.IsSelected).Select(row => row.Id),
                    _reports.Where(row => row.IsSelected).Select(row => row.Id));
                StatusText.Text = "Модуль и его состав сохранены.";
                await LoadAsync(saved.Id);
            }
            catch (Exception ex)
            {
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
                await _moduleService.DeleteModuleAsync(_currentModule.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Модули", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public class ModuleObjectRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        public ModuleObjectRow(Guid id, string name, string description, bool isSelected)
        {
            Id = id; Name = name; Description = description; _isSelected = isSelected;
        }
        public Guid Id { get; }
        public string Name { get; }
        public string Description { get; }
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
