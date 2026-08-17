using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EmployeeDialog : Window
    {
        public Employee Employee { get; private set; }
        public bool IsEditMode { get; private set; }
        private List<ReferenceItem> _positions = new();
        private List<ReferenceItem> _departments = new();
        private bool _clearingDatePickerSelection;

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
                var positionCatalog = FindReferenceCatalog(catalogs, "Должности");
                if (positionCatalog != null)
                {
                    _positions = await LoadReferenceItemsAsync(metadataService, positionCatalog);
                    cmbPosition.ItemsSource = _positions;
                    cmbPosition.SelectedValue = Employee.PositionId;
                    ReferencePickerControlFactory.AttachEditor(
                        cmbPosition,
                        metadataService,
                        positionCatalog,
                        this,
                        items => _positions = items,
                        "Код",
                        "Наименование");
                }

                var departmentCatalog = FindReferenceCatalog(catalogs, "Подразделения");
                if (departmentCatalog != null)
                {
                    _departments = await LoadReferenceItemsAsync(metadataService, departmentCatalog);
                    cmbDepartment.ItemsSource = _departments;
                    cmbDepartment.SelectedValue = Employee.DepartmentId;
                    ReferencePickerControlFactory.AttachEditor(
                        cmbDepartment,
                        metadataService,
                        departmentCatalog,
                        this,
                        items => _departments = items,
                        "Код",
                        "Наименование");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки справочников: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static MetadataObject? FindReferenceCatalog(IEnumerable<MetadataObject> catalogs, string catalogName)
        {
            return catalogs.FirstOrDefault(c =>
                c.ObjectType == "Catalog" &&
                c.Name.Equals(catalogName, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            MetadataService metadataService,
            MetadataObject catalog)
        {
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
            SetComboBoxValue(cmbGender, employee.Gender);
            SetComboBoxValue(cmbMaritalStatus, employee.MaritalStatus);
            
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
            
            SetDateText(txtBirthDate, employee.BirthDate);
            SetDateText(txtHireDate, employee.HireDate);
            SetDateText(txtTerminationDate, employee.TerminationDate);
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
            SetDateText(txtPassportIssueDate, employee.PassportIssueDate);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Employee.PersonnelNumber = txtPersonnelNumber.Text;
                Employee.FullName = txtFullName.Text;
                Employee.Gender = GetOptionalComboBoxValue(cmbGender);
                Employee.MaritalStatus = GetOptionalComboBoxValue(cmbMaritalStatus);
                
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
                
                Employee.BirthDate = ReadDateText(txtBirthDate, "Дата рождения");
                Employee.HireDate = ReadDateText(txtHireDate, "Дата приема");
                Employee.TerminationDate = ReadDateText(txtTerminationDate, "Дата увольнения");
                Employee.Address = txtAddress.Text;
                Employee.Notes = txtNotes.Text;
                Employee.Status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Активен";
                Employee.Phone = txtPhone.Text;
                Employee.Email = txtEmail.Text;
                Employee.TaxId = txtTaxId.Text;
                Employee.PassportNumber = txtPassportNumber.Text;
                Employee.PassportIssuedBy = txtPassportIssuedBy.Text.Trim().ToUpperInvariant();
                Employee.PassportIssueDate = ReadDateText(txtPassportIssueDate, "Дата выдачи");
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

        private const string DateInputMask = "../../....";
        private static readonly int[] DateInputPositions = { 0, 1, 3, 4, 6, 7, 8, 9 };

        private static void SetDateText(TextBox textBox, DateTime? value)
        {
            textBox.Text = value.HasValue
                ? value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : DateInputMask;
        }

        private static DateTime? ReadDateText(TextBox textBox, string fieldName)
        {
            var text = NormalizeDateText(textBox.Text);
            textBox.Text = text;

            if (IsDateMaskEmpty(text))
                return null;

            if (!IsCompleteDateText(text))
                throw new InvalidOperationException($"{fieldName}: заполните дату полностью по шаблону ../../....");

            if (DateTime.TryParseExact(
                    text,
                    new[] { "dd/MM/yyyy", "dd.MM.yyyy" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date;
            }

            throw new InvalidOperationException($"{fieldName}: неверная дата. Введите дату по шаблону ../../....");
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_clearingDatePickerSelection || sender is not DatePicker picker || !picker.SelectedDate.HasValue)
                return;

            var target = picker.Name switch
            {
                "calBirthDate" => txtBirthDate,
                "calHireDate" => txtHireDate,
                "calTerminationDate" => txtTerminationDate,
                "calPassportIssueDate" => txtPassportIssueDate,
                _ => null
            };

            if (target == null)
                return;

            SetDateText(target, picker.SelectedDate);

            try
            {
                _clearingDatePickerSelection = true;
                picker.SelectedDate = null;
            }
            finally
            {
                _clearingDatePickerSelection = false;
            }
        }
        private void DateTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            textBox.Text = NormalizeDateText(textBox.Text);
            textBox.CaretIndex = FindFirstEmptyDatePosition(textBox.Text);
        }

        private void DateTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            textBox.Text = NormalizeDateText(textBox.Text);
        }

        private void DateTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            e.Handled = true;
            if (e.Text.Length != 1 || !char.IsDigit(e.Text[0]))
                return;

            textBox.Text = NormalizeDateText(textBox.Text);
            var position = FindNextDatePosition(textBox.CaretIndex);
            if (position >= DateInputMask.Length)
                return;

            var chars = textBox.Text.ToCharArray();
            chars[position] = e.Text[0];
            textBox.Text = new string(chars);
            textBox.CaretIndex = FindNextDatePosition(position + 1);
        }

        private void DateTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (e.Key != Key.Back && e.Key != Key.Delete)
                return;

            e.Handled = true;
            textBox.Text = NormalizeDateText(textBox.Text);

            var position = e.Key == Key.Back
                ? FindPreviousDatePosition(textBox.CaretIndex - 1)
                : FindNextDatePosition(textBox.CaretIndex);

            if (position < 0 || position >= DateInputMask.Length)
                return;

            var chars = textBox.Text.ToCharArray();
            chars[position] = DateInputMask[position];
            textBox.Text = new string(chars);
            textBox.CaretIndex = position;
        }

        private static string NormalizeDateText(string? value)
        {
            var digits = new string((value ?? string.Empty)
                .Where(char.IsDigit)
                .Take(DateInputPositions.Length)
                .ToArray());

            var chars = DateInputMask.ToCharArray();
            for (var i = 0; i < digits.Length; i++)
                chars[DateInputPositions[i]] = digits[i];

            return new string(chars);
        }

        private static bool IsDateMaskEmpty(string value)
        {
            return DateInputPositions.All(position => !char.IsDigit(value[position]));
        }

        private static bool IsCompleteDateText(string value)
        {
            return value.Length == DateInputMask.Length &&
                   DateInputPositions.All(position => char.IsDigit(value[position]));
        }

        private static int FindFirstEmptyDatePosition(string value)
        {
            foreach (var position in DateInputPositions)
            {
                if (position >= value.Length || !char.IsDigit(value[position]))
                    return position;
            }

            return DateInputMask.Length;
        }

        private static int FindNextDatePosition(int start)
        {
            foreach (var position in DateInputPositions)
            {
                if (position >= start)
                    return position;
            }

            return DateInputMask.Length;
        }

        private static int FindPreviousDatePosition(int start)
        {
            for (var i = DateInputPositions.Length - 1; i >= 0; i--)
            {
                if (DateInputPositions[i] <= start)
                    return DateInputPositions[i];
            }

            return -1;
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

        private static void SetComboBoxValue(ComboBox comboBox, string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Не указано" : value;
            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private static string GetOptionalComboBoxValue(ComboBox comboBox)
        {
            var value = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            return value.Equals("Не указано", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
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
