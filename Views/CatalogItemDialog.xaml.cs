using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CatalogItemDialog : Window
    {
        private readonly MetadataObject _catalog;
        private readonly Dictionary<string, object> _itemData;
        private readonly Dictionary<string, Control> _controls;
        private readonly MetadataService _metadataService;
        private Dictionary<string, Dictionary<Guid, string>> _referenceCache;

        public Dictionary<string, object> ItemData => _itemData;

        public CatalogItemDialog(MetadataObject catalog, MetadataService metadataService, Dictionary<string, object> existingData = null)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _itemData = new Dictionary<string, object>();
            _controls = new Dictionary<string, Control>();
            _referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

            DialogTitle.Text = $"Добавление в справочник: {catalog.Name}";

            Loaded += async (s, e) => await BuildFieldsAsync(existingData);
        }

        private async System.Threading.Tasks.Task BuildFieldsAsync(Dictionary<string, object> existingData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync START for {_catalog.Name} ===");

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    System.Diagnostics.Debug.WriteLine($"Processing field: {field.Name}, Type: {field.FieldType}");

                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
                    var label = new TextBlock { Text = field.Name, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
                    panel.Children.Add(label);

                    Control inputControl;

                    if (TryCreateChartOfAccountsChoiceControl(field, existingData, out var choiceControl))
                    {
                        inputControl = choiceControl;
                    }
                    // УНИВЕРСАЛЬНАЯ ОБРАБОТКА REFERENCE ПОЛЕЙ
                    else if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                    {
                        inputControl = await CreateReferenceControl(field, catalogsDict, existingData);
                    }
                    else
                    {
                        inputControl = CreateRegularControl(field, existingData);
                    }

                    panel.Children.Add(inputControl);
                    FieldsPanel.Children.Add(panel);
                    _controls[field.Name] = inputControl;
                }

                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BuildFieldsAsync ERROR: {ex.Message}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        // Универсальное создание ComboBox для любого Reference поля       
        private async Task<ComboBox> CreateReferenceControl(
       MetadataField field,
       Dictionary<string, MetadataObject> catalogsDict,
       Dictionary<string, object> existingData)
        {
            var comboBox = new ComboBox
            {
                Height = 35,
                Name = field.Name.Replace(" ", "_").Replace("-", "_"),
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                MinWidth = 200,
                Tag = field
            };

            if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
            {
                comboBox.ItemsSource = new List<ReferenceItem>
        {
            new ReferenceItem { Id = Guid.Empty, DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден" }
        };
                return comboBox;
            }

            var data = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
            var items = new List<ReferenceItem>();

            foreach (var row in data)
            {
                var item = new ReferenceItem();

                if (row.ContainsKey("Id"))
                    item.Id = Guid.Parse(row["Id"].ToString());

                item.DisplayName = GetDisplayValue(row, field, refCatalog.Name);
                items.Add(item);
            }

            comboBox.ItemsSource = items;

            // Выбираем существующее значение
            if (existingData != null && existingData.ContainsKey(field.Name) && existingData[field.Name] != null)
            {
                var existingId = existingData[field.Name].ToString();
                var selectedItem = items.FirstOrDefault(i => i.Id.ToString() == existingId);
                if (selectedItem != null)
                    comboBox.SelectedItem = selectedItem;
            }

            // ========== ДОБАВИТЬ ЭТОТ БЛОК ==========
            // Автоподстановка для поля "Табельный номер"
            if (field.Name == "Табельный номер")
            {
                comboBox.SelectionChanged += async (s, args) =>
                {
                    if (comboBox.SelectedItem is ReferenceItem selectedEmployee)
                    {
                        await AutoFillFromEmployee(selectedEmployee.Id);
                    }
                };
            }
            // =======================================

            return comboBox;
        }

        /// <summary>
        /// Универсальное определение имени поля для отображения
        /// </summary>
        private string GetDisplayFieldName(MetadataObject catalog, List<Dictionary<string, object>> data)
        {
            // Приоритеты для отображения (в порядке убывания)
            var preferredFields = new[]
            {
                "Наименование", "name", "Name",
                "ФИО", "full_name", "FullName",
                "Наименование вида", "Наименование категории",
                "site_name", "Наименование участка",
                "Код", "code", "Code"
            };

            if (data.Count == 0) return "Id";

            var firstRow = data[0];

            foreach (var prefField in preferredFields)
            {
                if (firstRow.ContainsKey(prefField))
                    return prefField;
            }

            // Если ничего не нашли - возвращаем первый строковый ключ
            foreach (var key in firstRow.Keys)
            {
                if (firstRow[key] is string && key != "Id" && key != "CreatedAt" && key != "UpdatedAt")
                    return key;
            }

            return "Id";
        }

        /// <summary>
        /// Универсальное получение отображаемого значения
        /// </summary>
        private string GetDisplayValueOLD(Dictionary<string, object> row, string displayFieldName, string catalogName)
        {
            if (row.ContainsKey(displayFieldName) && row[displayFieldName] != null)
                return row[displayFieldName].ToString();

            // Специальные случаи для конкретных справочников (если нужно форматирование)
            if (catalogName == "Сотрудники (Списочный состав)")
            {
                var personnelNumber = "";
                var fullName = "";

                if (row.ContainsKey("Табельный номер")) personnelNumber = row["Табельный номер"]?.ToString() ?? "";
                if (row.ContainsKey("ФИО")) fullName = row["ФИО"]?.ToString() ?? "";
                if (row.ContainsKey("Наименование")) fullName = row["Наименование"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(personnelNumber) && !string.IsNullOrEmpty(fullName))
                    return $"{personnelNumber} - {fullName}";
                if (!string.IsNullOrEmpty(fullName))
                    return fullName;
                if (!string.IsNullOrEmpty(personnelNumber))
                    return personnelNumber;
            }

            // Если ничего не нашли - возвращаем Id
            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
        }
        /// <summary>
        /// Универсальное получение отображаемого значения по шаблону из метаданных
        /// </summary>
        private string GetDisplayValue(Dictionary<string, object> row, MetadataField field, string catalogName)
        {
            // Если есть шаблон - используем его
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var fld in fieldNames)
                {
                    var fieldKey = fld.Trim();
                    var value = "";

                    if (row.ContainsKey(fieldKey))
                        value = row[fieldKey]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.Replace(" ", "_")))
                        value = row[fieldKey.Replace(" ", "_")]?.ToString() ?? "";

                    result = result.Replace($"{{{fieldKey}}}", value);
                }

                return result;
            }

            // === БЕЗ ШАБЛОНА - автоопределение ===

            // Приоритеты полей для отображения (общие для всех справочников)
            var priorityFields = new[]
            {
        "Наименование", "name", "Name",
        "ФИО", "full_name", "FullName",
        "Наименование вида", "Наименование категории",
        "site_name", "Наименование участка",
        "Код", "code", "Code"
    };

            foreach (var priority in priorityFields)
            {
                if (row.ContainsKey(priority) && row[priority] != null)
                    return row[priority].ToString();
            }

            // Ищем любое строковое поле
            foreach (var key in row.Keys)
            {
                if (key != "Id" && key != "CreatedAt" && key != "UpdatedAt" && row[key] is string strVal && !string.IsNullOrEmpty(strVal))
                    return strVal;
            }

            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
        }
        /// <summary>
        /// Создание обычного контрола (не Reference)
        /// </summary>
        private bool TryCreateChartOfAccountsChoiceControl(
            MetadataField field,
            Dictionary<string, object> existingData,
            out Control control)
        {
            control = null;

            if (_catalog.Name != "План счетов")
                return false;

            var options = field.Name switch
            {
                "Тип счета" => new object[]
                {
                    new ChoiceItem("Active", LocalizationService.DisplayValue("Active")),
                    new ChoiceItem("Passive", LocalizationService.DisplayValue("Passive")),
                    new ChoiceItem("ActivePassive", LocalizationService.DisplayValue("ActivePassive"))
                },
                "Признак печати" => new object[] { "", "по статьям", "по организациям", "по таб.номерам", "по лиц.счетам", "по материалам", "по субсчетам" },
                "Сохранять остатки" => new object[] { "", "по статьям", "по организациям", "по таб.номерам", "по лиц.счетам", "по материалам", "по субсчетам" },
                _ => null
            };

            if (options == null)
                return false;

            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                ItemsSource = options
            };

            var existingValue = existingData != null && existingData.ContainsKey(field.Name)
                ? NormalizeChartOfAccountsChoice(field.Name, existingData[field.Name]?.ToString())
                : string.Empty;

            comboBox.SelectedItem = field.Name == "Тип счета"
                ? options.OfType<ChoiceItem>().FirstOrDefault(option => option.Code == NormalizeAccountTypeCode(existingData?.GetValueOrDefault(field.Name)?.ToString()))
                : options.FirstOrDefault(option => option?.ToString() == existingValue);
            comboBox.SelectedItem ??= options.FirstOrDefault();
            control = comboBox;
            return true;
        }

        private static string NormalizeChartOfAccountsChoice(string fieldName, string value)
        {
            if (fieldName != "Тип счета")
                return value ?? string.Empty;

            return LocalizationService.DisplayValue(value);
        }

        private static string NormalizeAccountTypeCode(string? value) => value switch
        {
            "Активный" => "Active",
            "Пассивный" => "Passive",
            "Активно-пассивный" => "ActivePassive",
            _ => value ?? "Active"
        };

        private Control CreateRegularControl(MetadataField field, Dictionary<string, object> existingData)
        {
            switch (field.FieldType)
            {
                case "Int":
                    var intBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                    if (existingData != null && existingData.ContainsKey(field.Name))
                        intBox.Text = existingData[field.Name]?.ToString();
                    return intBox;

                case "Decimal":
                    var decimalBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                    if (existingData != null && existingData.ContainsKey(field.Name))
                        decimalBox.Text = existingData[field.Name]?.ToString();
                    return decimalBox;

                case "DateTime":
                    var datePicker = new DatePicker { Height = 35 };
                    if (existingData != null && existingData.ContainsKey(field.Name))
                        datePicker.SelectedDate = existingData[field.Name] as DateTime?;
                    return datePicker;

                case "Bool":
                    var checkBox = new CheckBox { Content = "Да", Margin = new Thickness(0, 5, 0, 0) };
                    if (existingData != null && existingData.ContainsKey(field.Name))
                        checkBox.IsChecked = (bool?)existingData[field.Name];
                    return checkBox;

                default: // String
                    var textBox = new TextBox { Height = 35, Padding = new Thickness(10) };

                    // Автоматически определяем ReadOnly поля
                    if (field.Name == "ФИО" || field.Name == "FullName" || field.Name == "full_name")
                    {
                        textBox.IsReadOnly = true;
                        textBox.Background = System.Windows.Media.Brushes.LightGray;
                    }

                    if (existingData != null && existingData.ContainsKey(field.Name))
                        textBox.Text = existingData[field.Name]?.ToString();
                    return textBox;
            }
        }
        private async Task AutoFillFromEmployee(Guid employeeId)
        {
            try
            {
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");

                if (employeeCatalog == null) return;

                var employees = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
                var employee = employees.FirstOrDefault(e => e.ContainsKey("Id") && Guid.Parse(e["Id"].ToString()) == employeeId);

                if (employee == null) return;

                // Заполняем ФИО
                if (_controls.ContainsKey("ФИО") && _controls["ФИО"] is TextBox fioBox)
                {
                    if (employee.ContainsKey("ФИО"))
                        fioBox.Text = employee["ФИО"].ToString();
                    else if (employee.ContainsKey("Наименование"))
                        fioBox.Text = employee["Наименование"].ToString();
                    else if (employee.ContainsKey("name"))
                        fioBox.Text = employee["name"].ToString();
                }

                // Заполняем Должность
                if (_controls.ContainsKey("Должность") && _controls["Должность"] is TextBox positionBox)
                {
                    if (employee.ContainsKey("Должность"))
                        positionBox.Text = employee["Должность"].ToString();
                }

                // Заполняем Телефон
                if (_controls.ContainsKey("Телефон") && _controls["Телефон"] is TextBox phoneBox)
                {
                    if (employee.ContainsKey("Телефон"))
                        phoneBox.Text = employee["Телефон"].ToString();
                }

                // Если есть поле "Участок" (Reference) - его тоже можно заполнить
                if (_controls.ContainsKey("Участок") && _controls["Участок"] is ComboBox siteBox && employee.ContainsKey("Участок"))
                {
                    var siteId = employee["Участок"].ToString();
                    var items = siteBox.ItemsSource as List<ReferenceItem>;
                    if (items != null)
                    {
                        var selectedSite = items.FirstOrDefault(i => i.Id.ToString() == siteId);
                        if (selectedSite != null)
                            siteBox.SelectedItem = selectedSite;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoFillFromEmployee error: {ex.Message}");
            }
        }
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    if (!_controls.ContainsKey(field.Name)) continue;

                    var control = _controls[field.Name];
                    object value = null;

                    if (control is ComboBox comboBox)
                    {
                        if (comboBox.SelectedItem is ReferenceItem selectedItem)
                            value = selectedItem.Id.ToString();
                        else if (comboBox.SelectedItem is ChoiceItem choiceItem)
                            value = choiceItem.Code;
                        else
                            value = comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? "";
                    }
                    else
                    {
                        value = GetValueFromControl(control, field);
                    }

                    _itemData[field.Name] = value ?? DBNull.Value;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed record ChoiceItem(string Code, string Display)
        {
            public override string ToString() => Display;
        }

        private object GetValueFromControl(Control control, MetadataField field)
        {
            switch (field.FieldType)
            {
                case "Int":
                    if (control is TextBox tbInt && int.TryParse(tbInt.Text, out int intVal))
                        return intVal;
                    return null;

                case "Decimal":
                    if (control is TextBox tbDec && decimal.TryParse(tbDec.Text, out decimal decVal))
                        return decVal;
                    return null;

                case "DateTime":
                    if (control is DatePicker dp)
                        return dp.SelectedDate;
                    return null;

                case "Bool":
                    if (control is CheckBox cb)
                        return cb.IsChecked ?? false;
                    return null;

                default:
                    if (control is TextBox tb)
                        return tb.Text;
                    return null;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ReferenceItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
