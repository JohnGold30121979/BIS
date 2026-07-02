using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EmployeeDialog : Window
    {
        public Employee Employee { get; private set; }
        public bool IsEditMode { get; private set; }
        private List<ReferenceItem> _positions = new();
        private List<ReferenceItem> _departments = new();

        public EmployeeDialog(Employee employee = null, MetadataService metadataService = null)
        {
            InitializeComponent();

            if (employee != null)
            {
                IsEditMode = true;
                Title = $"✏️ Редактирование: {employee.FullName}";
                LoadEmployee(employee);
            }
            else
            {
                IsEditMode = false;
                Title = "➕ Добавление сотрудника";
                Employee = new Employee();
            }

            // Загружаем справочники если сервис передан
            if (metadataService != null)
            {
                LoadCatalogs(metadataService);
            }
        }

        private async void LoadCatalogs(MetadataService metadataService)
        {
            try
            {
                var catalogs = await metadataService.GetCatalogsAsync();

                // Загружаем записи справочников, а не сами объекты метаданных.
                _positions = await LoadReferenceItemsAsync(metadataService, catalogs, "Должности");
                cmbPosition.ItemsSource = _positions;
                cmbPosition.SelectedValue = Employee.PositionId;

                _departments = await LoadReferenceItemsAsync(metadataService, catalogs, "Подразделения");
                cmbDepartment.ItemsSource = _departments;
                cmbDepartment.SelectedValue = Employee.DepartmentId;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки справочников: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            MetadataService metadataService,
            IEnumerable<MetadataObject> catalogs,
            string catalogName)
        {
            var catalog = catalogs.FirstOrDefault(c =>
                c.ObjectType == "Catalog" &&
                c.Name.Equals(catalogName, StringComparison.OrdinalIgnoreCase));

            if (catalog == null)
                return new List<ReferenceItem>();

            var displayField = new MetadataField
            {
                DisplayPattern = "{Наименование}",
                DisplayFields = "Наименование"
            };

            var rows = await metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _) && IsActive(row))
                .Select(row => new ReferenceItem
                {
                    Id = Guid.Parse(row["Id"].ToString()!),
                    DisplayName = ReferenceDisplayHelper.BuildDisplayValue(row, displayField)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.DisplayName))
                .OrderBy(item => item.DisplayName)
                .ToList();
        }

        private void chkManualPosition_Checked(object sender, RoutedEventArgs e)
        {
            if (chkManualPosition.IsChecked == true)
            {
                // Ручной ввод - показываем текстбокс, скрываем комбобокс
                cmbPosition.Visibility = Visibility.Collapsed;
                txtPosition.Visibility = Visibility.Visible;
                txtPosition.Text = Employee.PositionText ?? string.Empty;
            }
            else
            {
                // Выбор из справочника - показываем комбобокс, скрываем текстбокс
                cmbPosition.Visibility = Visibility.Visible;
                txtPosition.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadEmployee(Employee employee)
        {
            Employee = employee;
            txtPersonnelNumber.Text = employee.PersonnelNumber;
            txtFullName.Text = employee.FullName;
            
            // Устанавливаем должность
            if (!string.IsNullOrEmpty(employee.PositionText))
            {
                // Если есть ручной ввод, показываем текст
                chkManualPosition.IsChecked = true;
                txtPosition.Text = employee.PositionText;
                cmbPosition.Visibility = Visibility.Collapsed;
                txtPosition.Visibility = Visibility.Visible;
            }
            else if (employee.PositionId.HasValue)
            {
                // Если есть ссылка на справочник, выбираем в комбобоксе
                chkManualPosition.IsChecked = false;
                cmbPosition.SelectedValue = employee.PositionId.Value;
            }
            
            // Устанавливаем подразделение
            if (employee.DepartmentId.HasValue)
            {
                cmbDepartment.SelectedValue = employee.DepartmentId.Value;
            }
            
            dpBirthDate.SelectedDate = employee.BirthDate;
            dpHireDate.SelectedDate = employee.HireDate;
            dpTerminationDate.SelectedDate = employee.TerminationDate;
            txtAddress.Text = employee.Address;
            txtNotes.Text = employee.Notes;

            // Устанавливаем статус
            for (int i = 0; i < cmbStatus.Items.Count; i++)
            {
                if ((cmbStatus.Items[i] as ComboBoxItem)?.Content?.ToString() == employee.Status)
                {
                    cmbStatus.SelectedIndex = i;
                    break;
                }
            }

            txtPhone.Text = employee.Phone;
            txtEmail.Text = employee.Email;
            txtTaxId.Text = employee.TaxId;
            txtPassportNumber.Text = employee.PassportNumber;
            txtPassportIssuedBy.Text = employee.PassportIssuedBy;
            dpPassportIssueDate.SelectedDate = employee.PassportIssueDate;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Employee.PersonnelNumber = txtPersonnelNumber.Text;
                Employee.FullName = txtFullName.Text;
                
                // Сохраняем должность
                if (chkManualPosition.IsChecked == true)
                {
                    // Ручной ввод
                    Employee.PositionText = txtPosition.Text;
                    Employee.PositionId = null;
                }
                else
                {
                    // Выбор из справочника
                    Employee.PositionId = GetSelectedGuid(cmbPosition);
                    Employee.PositionText = string.Empty;
                    Employee.PositionDisplay = (cmbPosition.SelectedItem as ReferenceItem)?.DisplayName ?? string.Empty;
                }
                
                // Сохраняем подразделение
                Employee.DepartmentId = GetSelectedGuid(cmbDepartment);
                Employee.Department = (cmbDepartment.SelectedItem as ReferenceItem)?.DisplayName ?? string.Empty;
                
                Employee.BirthDate = dpBirthDate.SelectedDate;
                Employee.HireDate = dpHireDate.SelectedDate;
                Employee.TerminationDate = dpTerminationDate.SelectedDate;
                Employee.Address = txtAddress.Text;
                Employee.Notes = txtNotes.Text;
                Employee.Status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Активен";
                Employee.Phone = txtPhone.Text;
                Employee.Email = txtEmail.Text;
                Employee.TaxId = txtTaxId.Text;
                Employee.PassportNumber = txtPassportNumber.Text;
                Employee.PassportIssuedBy = txtPassportIssuedBy.Text.Trim().ToUpperInvariant();
                Employee.PassportIssueDate = dpPassportIssueDate.SelectedDate;
                Employee.IsActive = Employee.Status == "Активен";             

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UppercaseTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var upper = textBox.Text.ToUpperInvariant();
            if (textBox.Text == upper)
                return;

            var caretIndex = textBox.CaretIndex;
            textBox.Text = upper;
            textBox.CaretIndex = Math.Min(caretIndex, textBox.Text.Length);
        }

        private static Guid? GetSelectedGuid(ComboBox comboBox)
        {
            if (comboBox.SelectedValue is Guid id)
                return id;

            return Guid.TryParse(comboBox.SelectedValue?.ToString(), out var parsed)
                ? parsed
                : null;
        }

        private static bool TryGetGuid(object? value, out Guid id)
        {
            if (value is Guid guid)
            {
                id = guid;
                return true;
            }

            return Guid.TryParse(value?.ToString(), out id);
        }

        private static bool IsActive(Dictionary<string, object> row)
        {
            if (!row.TryGetValue("Активен", out var value) &&
                !row.TryGetValue("is_active", out value))
            {
                return true;
            }

            return value switch
            {
                bool isActive => isActive,
                null => true,
                DBNull => true,
                _ => !value.ToString()?.Equals("false", StringComparison.OrdinalIgnoreCase) == true
            };
        }
    }
}
