using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private AccountAnalyticsRegistry? _accountAnalytics;
        private string? _assignedModuleName;

        public Dictionary<string, object> ItemData => _itemData;

        public CatalogItemDialog(MetadataObject catalog, MetadataService metadataService, Dictionary<string, object> existingData = null)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _itemData = new Dictionary<string, object>();
            _controls = new Dictionary<string, Control>();
            _referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

            DialogTitle.Text = existingData == null
                ? $"Добавление в справочник: {catalog.Name}"
                : $"Редактирование справочника: {catalog.Name}";

            Loaded += async (s, e) => await BuildFieldsAsync(existingData);
        }

        private async System.Threading.Tasks.Task BuildFieldsAsync(Dictionary<string, object> existingData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync START for {_catalog.Name} ===");

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
                _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                _assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(_catalog.Id, _catalog.ObjectType);

                foreach (var field in GetEditableFields())
                {
                    System.Diagnostics.Debug.WriteLine($"Processing field: {field.Name}, Type: {field.FieldType}");

                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
                    var label = new TextBlock { Text = field.Name, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
                    panel.Children.Add(label);

                    Control inputControl;

                    var choiceControl = await CreateChartOfAccountsChoiceControlAsync(field, existingData);
                    if (choiceControl != null)
                    {
                        inputControl = choiceControl;
                    }
                    else if (ShouldUseAccountPicker(field))
                    {
                        inputControl = AccountPickerControlFactory.Create(
                            _accountAnalytics!,
                            GetExistingValue(field, existingData),
                            this,
                            moduleCodeOrName: _assignedModuleName);
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

        private bool ShouldUseAccountPicker(MetadataField field)
        {
            return AccountAnalyticsRules.IsAccountSelectorField(field) &&
                   _accountAnalytics?.Accounts.Count > 0;
        }

        private static object? GetExistingValue(MetadataField field, Dictionary<string, object> existingData)
        {
            if (existingData == null)
                return null;

            if (existingData.TryGetValue(field.Name, out var byName))
                return byName;

            if (!string.IsNullOrWhiteSpace(field.DbColumnName) &&
                existingData.TryGetValue(field.DbColumnName, out var byColumn))
                return byColumn;

            return null;
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
                foreach (var key in GetReferenceLookupKeys(row, item.DisplayName))
                    item.LookupKeys.Add(key);
                items.Add(item);
            }

            comboBox.ItemsSource = items;

            // Выбираем существующее значение: поддерживаем и Id, и уже отображенное значение.
            var existingValue = GetExistingValue(field, existingData)?.ToString();
            if (!string.IsNullOrWhiteSpace(existingValue))
            {
                var selectedItem = items.FirstOrDefault(i =>
                    string.Equals(i.Id.ToString(), existingValue, StringComparison.OrdinalIgnoreCase) ||
                    i.LookupKeys.Contains(NormalizeReferenceLookupKey(existingValue)));
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

        private static IEnumerable<string> GetReferenceLookupKeys(
            Dictionary<string, object> row,
            string displayName)
        {
            foreach (var keyName in new[]
                     {
                         "Id", "Код", "code", "Code", "Счет", "account_code",
                         "Наименование", "name", "ФИО", "full_name"
                     })
            {
                if (!row.TryGetValue(keyName, out var value))
                    continue;

                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                yield return displayName.Trim();

            var displayKey = NormalizeReferenceLookupKey(displayName);
            if (!string.IsNullOrWhiteSpace(displayKey))
                yield return displayKey;
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? normalized[..separatorIndex].Trim()
                : normalized;
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
        private async Task<Control?> CreateChartOfAccountsChoiceControlAsync(
            MetadataField field,
            Dictionary<string, object> existingData)
        {
            if (_catalog.Name != "План счетов")
                return null;

            IReadOnlyList<object>? options = field.Name switch
            {
                "Тип счета" => new object[]
                {
                    new ChoiceItem("Active", LocalizationService.DisplayValue("Active"), "Активный"),
                    new ChoiceItem("Passive", LocalizationService.DisplayValue("Passive"), "Пассивный"),
                    new ChoiceItem("ActivePassive", LocalizationService.DisplayValue("ActivePassive"), "Активно-пассивный")
                },
                "Закрывает модуль" => await GetClosingModuleOptionsAsync(),
                "Признак печати" => BuildModeChoiceItems(ChartOfAccountsSelectionMetadata.PrintModeOptions),
                "Сохранять остатки" => BuildModeChoiceItems(ChartOfAccountsSelectionMetadata.BalanceModeOptions),
                _ => null
            };

            if (options == null || options.Count == 0)
                return null;

            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                ItemsSource = options
            };

            var existingValue = NormalizeChartOfAccountsChoice(
                field.Name,
                GetExistingValue(field, existingData)?.ToString());

            comboBox.SelectedItem = options
                .OfType<ChoiceItem>()
                .FirstOrDefault(option => option.Matches(existingValue));
            comboBox.SelectedItem ??= options.FirstOrDefault(option =>
                string.Equals(option?.ToString(), existingValue, StringComparison.OrdinalIgnoreCase));
            comboBox.SelectedItem ??= field.Name == "Тип счета"
                ? options.OfType<ChoiceItem>().FirstOrDefault(option => option.Code == "Active")
                : options.FirstOrDefault();

            comboBox.SelectedItem ??= options.FirstOrDefault();
            return comboBox;
        }

        private async Task<IReadOnlyList<object>> GetClosingModuleOptionsAsync()
        {
            var modules = await _metadataService.GetModulesAsync();
            var options = new List<object>();
            var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in modules)
            {
                if (string.IsNullOrWhiteSpace(module.Name) || !usedValues.Add(module.Name))
                    continue;

                options.Add(new ChoiceItem(module.Name, module.Name, module.Code));
            }

            foreach (var option in GetFoxClosingModuleChoices())
            {
                if (usedValues.Add(option.Code))
                    options.Add(option);
            }

            return options;
        }

        private static IEnumerable<ChoiceItem> GetFoxClosingModuleChoices()
        {
            yield return new ChoiceItem("Финансы", "Финансы", "3", "Finance");
            yield return new ChoiceItem("Сбыт", "Сбыт", "4", "Продажи", "Реализация", "Регистратура");
            yield return new ChoiceItem("Учет материальных ценностей", "Учет материальных ценностей", "6", "Inventory", "УМЦ", "ТМЦ", "Материалы");
            yield return new ChoiceItem("Основные средства", "Основные средства", "7", "FixedAssets", "ОС");
            yield return new ChoiceItem("Вспомогательное производство", "Вспомогательное производство", "8");
            yield return new ChoiceItem("Сырье", "Сырье", "12");
            yield return new ChoiceItem("Меню", "Меню", "66");
        }

        private static string NormalizeChartOfAccountsChoice(string fieldName, string? value)
        {
            value ??= string.Empty;

            if (fieldName == "Закрывает модуль")
                return NormalizeClosingModuleChoice(value);

            if (fieldName is "Признак печати" or "Сохранять остатки")
                return ChartOfAccountsSelectionMetadata.NormalizeModeValue(fieldName, value);

            if (fieldName != "Тип счета")
                return value;

            return LocalizationService.DisplayValue(value);
        }

        private static IReadOnlyList<object> BuildModeChoiceItems(
            IEnumerable<ChartOfAccountsChoiceDefinition> options)
        {
            return options
                .Select(option => (object)new ChoiceItem(option.Code, option.Display, option.Aliases))
                .ToList();
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
                    else
                        datePicker.SelectedDate = DateTime.Today;
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
                foreach (var field in GetEditableFields())
                {
                    if (!_controls.ContainsKey(field.Name)) continue;

                    var control = _controls[field.Name];
                    object value = null;

                    if (control is UserControl && AccountPickerControlFactory.GetSelectedAccount(control) != null)
                    {
                        value = AccountPickerControlFactory.GetSelectedAccountValue(field, control);
                    }
                    else if (control is ComboBox comboBox)
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

        private static string NormalizeClosingModuleChoice(string? value) => value?.Trim() switch
        {
            "3" or "Finance" => "Финансы",
            "4" => "Сбыт",
            "6" or "Inventory" => "Учет материальных ценностей",
            "7" or "FixedAssets" => "Основные средства",
            "8" => "Вспомогательное производство",
            "12" => "Сырье",
            "66" => "Меню",
            _ => value?.Trim() ?? string.Empty
        };

        private sealed record ChoiceItem(string Code, string Display, params string[] LookupKeys)
        {
            public bool Matches(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                return Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                       Display.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                       LookupKeys.Any(key => key.Equals(value, StringComparison.OrdinalIgnoreCase));
            }

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

        private List<MetadataField> GetEditableFields()
        {
            var allowedColumns = GetAllowedCatalogColumns();
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<MetadataField>();

            foreach (var field in _catalog.Fields.OrderBy(field => field.Order))
            {
                if (allowedColumns != null &&
                    (string.IsNullOrWhiteSpace(field.DbColumnName) || !allowedColumns.Contains(field.DbColumnName)))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(field.DbColumnName) &&
                    !usedColumns.Add(field.DbColumnName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(field.Name) &&
                    !usedNames.Add(field.Name))
                {
                    continue;
                }

                result.Add(field);
            }

            return result;
        }

        private HashSet<string>? GetAllowedCatalogColumns()
        {
            if (_catalog.Name == "План счетов")
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "account_type",
                    "description",
                    "level",
                    "is_active",
                    "closing_module_code",
                    "analytic_group",
                    "print_mode",
                    "balance_mode",
                    "link_organizations",
                    "link_employees",
                    "link_currencies",
                    "link_personal_accounts",
                    "link_materials",
                    "link_construction_objects",
                    "link_sites",
                    "tax_code",
                    "account_currency_id"
                };
            }

            if (_catalog.Name == "Авансовые платежи")
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "use_organizations",
                    "use_personnel",
                    "use_currency",
                    "module_code",
                    "debit_account",
                    "credit_account",
                    "use_settlements",
                    "generate_postings",
                    "use_internal_settlements",
                    "is_active",
                    "description"
                };
            }

            return null;
        }
    }

    public class ReferenceItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public HashSet<string> LookupKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
