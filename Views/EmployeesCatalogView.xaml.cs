using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    public partial class EmployeesCatalogView : UserControl
    {
        private CancellationTokenSource? _searchCancellation;
        private readonly MetadataService _metadataService;
        private ObservableCollection<Employee> _employees = new();

        public EmployeesCatalogView(MetadataService metadataService)
        {
            InitializeComponent();
            _metadataService = metadataService;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadEmployees();
        }

        private void UpdateButtonsState()
        {
            bool hasSelection = EmployeesGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            TerminateButton.IsEnabled = hasSelection;
        }

        private Task LoadEmployees() => SearchEmployees();

        // Обработчики для кнопок (имена из XAML)
        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var dialog = new EmployeeDialog(null, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    "Добавление сотрудника") == true)
            {
                var savedEmployee = await employeeService.AddEmployeeAsync(dialog.Employee);
                await LoadEmployees();
                // Остаёмся на только что созданном сотруднике.
                EmployeesGrid.SelectRowById(savedEmployee.Id, row => (row as Employee)?.Id);
                // Успех сообщаем в строке состояния, чтобы не отбирать фокус у таблицы.
                StatusText.Text = "✅ Сотрудник добавлен";
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            var dialog = new EmployeeDialog(selected, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    "Редактирование сотрудника") == true)
            {
                var updatedEmployee = await employeeService.UpdateEmployeeAsync(dialog.Employee);
                await LoadEmployees();
                // Возвращаем выделение на отредактированного сотрудника.
                EmployeesGrid.SelectRowById(updatedEmployee.Id, row => (row as Employee)?.Id);
                StatusText.Text = "✅ Данные сотрудника обновлены";
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            if (MessageBox.Show($"Удалить сотрудника {selected.FullName}?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await employeeService.DeleteEmployeeAsync(selected.Id);
                await LoadEmployees();
                StatusText.Text = "✅ Сотрудник удалён";
            }
        }

        private async void TerminateButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            var dialog = new TerminationDialog(selected);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await employeeService.TerminateEmployeeAsync(selected.Id, dialog.TerminationDate, dialog.Reason);
                await LoadEmployees();
                StatusText.Text = "✅ Сотрудник уволен";
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var dlg = new SaveFileDialog
            {
                Filter = "JSON файлы (*.json)|*.json",
                FileName = $"employees_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                var json = await employeeService.ExportEmployeesToJsonAsync();
                await System.IO.File.WriteAllTextAsync(dlg.FileName, json);
                StatusText.Text = $"✅ Экспортировано сотрудников: {_employees.Count} → {dlg.FileName}";
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            await using var context = _metadataService.CreateIndependentContext();
            var employeeService = new EmployeeService(context, new MetadataService(context));
            var dlg = new OpenFileDialog { Filter = "JSON файлы (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                var count = await employeeService.ImportEmployeesFromJsonAsync(json);
                await LoadEmployees();
                StatusText.Text = $"✅ Импортировано сотрудников: {count}";
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadEmployees();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchEmployees();
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            await SearchEmployees(debounce: true);
        }

        private void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private async Task SearchEmployees(bool debounce = false)
        {
            _searchCancellation?.Cancel();
            using var cancellation = new CancellationTokenSource();
            _searchCancellation = cancellation;
            try
            {
                if (debounce)
                    await Task.Delay(275, cancellation.Token);

                var text = SearchBox.Text;
                await using var context = _metadataService.CreateIndependentContext();
                var employeeService = new EmployeeService(context, new MetadataService(context));
                var results = string.IsNullOrWhiteSpace(text)
                    ? await employeeService.GetAllEmployeesAsync()
                    : await employeeService.SearchEmployeesAsync(text);
                cancellation.Token.ThrowIfCancellationRequested();

                _employees.Clear();
                foreach (var employee in results)
                    _employees.Add(employee);

                EmployeesGrid.ItemsSource = _employees;
                TotalInfo.Text = $"Найдено: {_employees.Count}";
                StatusText.Text = "Готово";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    StatusText.Text = $"Ошибка поиска: {ex.Message}";
            }
            finally
            {
                if (_searchCancellation == cancellation)
                    _searchCancellation = null;
            }
        }
    }
}
