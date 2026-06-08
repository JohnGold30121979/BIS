using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        private readonly EmployeeService _employeeService;
        private ObservableCollection<Employee> _employees = new();

        public EmployeesCatalogView(EmployeeService employeeService)
        {
            InitializeComponent();
            _employeeService = employeeService;
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

        private async Task LoadEmployees()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка...";

                var list = await _employeeService.GetAllEmployeesAsync();
                _employees.Clear();
                foreach (var emp in list)
                    _employees.Add(emp);

                EmployeesGrid.ItemsSource = _employees;
                TotalInfo.Text = $"Всего: {_employees.Count} сотрудников";
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // Обработчики для кнопок (имена из XAML)
        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EmployeeDialog();
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await _employeeService.AddEmployeeAsync(dialog.Employee);
                await LoadEmployees();
                MessageBox.Show("Сотрудник добавлен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            var dialog = new EmployeeDialog(selected);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await _employeeService.UpdateEmployeeAsync(dialog.Employee);
                await LoadEmployees();
                MessageBox.Show("Данные обновлены!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            if (MessageBox.Show($"Удалить сотрудника {selected.FullName}?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _employeeService.DeleteEmployeeAsync(selected.Id);
                await LoadEmployees();
                MessageBox.Show("Сотрудник удален!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void TerminateButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = EmployeesGrid.SelectedItem as Employee;
            if (selected == null) return;

            var dialog = new TerminationDialog(selected);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await _employeeService.TerminateEmployeeAsync(selected.Id, dialog.TerminationDate, dialog.Reason);
                await LoadEmployees();
                MessageBox.Show("Сотрудник уволен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "JSON файлы (*.json)|*.json",
                FileName = $"employees_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                var json = await _employeeService.ExportEmployeesToJsonAsync();
                await System.IO.File.WriteAllTextAsync(dlg.FileName, json);
                MessageBox.Show($"Экспортировано {_employees.Count} сотрудников!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "JSON файлы (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = await System.IO.File.ReadAllTextAsync(dlg.FileName);
                var count = await _employeeService.ImportEmployeesFromJsonAsync(json);
                await LoadEmployees();
                MessageBox.Show($"Импортировано {count} сотрудников!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
            await SearchEmployees();
        }

        private void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private async Task SearchEmployees()
        {
            string text = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                await LoadEmployees();
                return;
            }

            var results = await _employeeService.SearchEmployeesAsync(text);
            _employees.Clear();
            foreach (var emp in results)
                _employees.Add(emp);

            EmployeesGrid.ItemsSource = _employees;
            TotalInfo.Text = $"Найдено: {_employees.Count}";
        }
    }
}