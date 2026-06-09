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

        public Dictionary<string, object> ItemData => _itemData;

        public CatalogItemDialog(MetadataObject catalog, MetadataService metadataService, Dictionary<string, object> existingData = null)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _itemData = new Dictionary<string, object>();
            _controls = new Dictionary<string, Control>();

            DialogTitle.Text = $"Добавление в справочник: {catalog.Name}";

            Loaded += async (s, e) => await BuildFieldsAsync(existingData);
        }

        private async System.Threading.Tasks.Task BuildFieldsAsync(Dictionary<string, object> existingData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== BuildFieldsAsync START ===");
                System.Diagnostics.Debug.WriteLine($"Catalog: {_catalog.Name}");

                // Предзагружаем все справочники
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

                System.Diagnostics.Debug.WriteLine($"Loaded {allCatalogs.Count} catalogs");

                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    System.Diagnostics.Debug.WriteLine($"Processing field: {field.Name}, Type: {field.FieldType}, ReferenceCatalog: {field.ReferenceCatalog}");

                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

                    var label = new TextBlock
                    {
                        Text = field.Name,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    panel.Children.Add(label);

                    Control inputControl;

                    // Если поле ссылается на справочник
                    if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                    {
                        var safeName = field.Name.Replace(" ", "_").Replace("-", "_");
                        System.Diagnostics.Debug.WriteLine($"Creating ComboBox for {field.Name}, ReferenceCatalog: {field.ReferenceCatalog}");

                        var comboBox = new ComboBox
                        {
                            Height = 35,
                            Name = safeName,
                            DisplayMemberPath = "DisplayName",
                            SelectedValuePath = "Id",
                            MinWidth = 200
                        };

                        if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found reference catalog: {refCatalog.Name}, Id: {refCatalog.Id}");

                            var data = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                            System.Diagnostics.Debug.WriteLine($"Loaded {data.Count} items from {field.ReferenceCatalog}");

                            var items = new List<ReferenceItem>();

                            foreach (var d in data)
                            {
                                var item = new ReferenceItem();

                                if (d.ContainsKey("Id"))
                                    item.Id = Guid.Parse(d["Id"].ToString());

                                string displayName = "Без имени";

                                // Для справочника сотрудников
                                if (field.ReferenceCatalog == "Сотрудники (Списочный состав)")
                                {
                                    var personnelNumber = "";
                                    var fullName = "";

                                    if (d.ContainsKey("Табельный номер"))
                                        personnelNumber = d["Табельный номер"].ToString();
                                    else if (d.ContainsKey("personnel_number"))
                                        personnelNumber = d["personnel_number"].ToString();
                                    else if (d.ContainsKey("Код"))
                                        personnelNumber = d["Код"].ToString();

                                    if (d.ContainsKey("ФИО"))
                                        fullName = d["ФИО"].ToString();
                                    else if (d.ContainsKey("Наименование"))
                                        fullName = d["Наименование"].ToString();
                                    else if (d.ContainsKey("name"))
                                        fullName = d["name"].ToString();

                                    displayName = string.IsNullOrEmpty(personnelNumber) ? fullName : $"{personnelNumber} - {fullName}";
                                }
                                // Для справочника участков
                                else if (field.ReferenceCatalog == "Участки")
                                {
                                    if (d.ContainsKey("site_name"))
                                        displayName = d["site_name"].ToString();
                                    else if (d.ContainsKey("Наименование участка"))
                                        displayName = d["Наименование участка"].ToString();
                                    else if (d.ContainsKey("Наименование"))
                                        displayName = d["Наименование"].ToString();
                                    else if (d.ContainsKey("name"))
                                        displayName = d["name"].ToString();
                                }
                                else
                                {
                                    if (d.ContainsKey("Наименование"))
                                        displayName = d["Наименование"].ToString();
                                    else if (d.ContainsKey("name"))
                                        displayName = d["name"].ToString();
                                    else if (d.ContainsKey("Name"))
                                        displayName = d["Name"].ToString();
                                    else if (d.ContainsKey("Код"))
                                        displayName = d["Код"].ToString();
                                }

                                item.DisplayName = displayName;
                                items.Add(item);
                            }

                            comboBox.ItemsSource = items;
                            System.Diagnostics.Debug.WriteLine($"Added {items.Count} items to ComboBox for {field.Name}");

                            // Обработчик для поля "Табельный номер"
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

                            if (existingData != null && existingData.ContainsKey(field.Name) && existingData[field.Name] != null)
                            {
                                var existingId = existingData[field.Name].ToString();
                                var selectedItem = items.FirstOrDefault(i => i.Id.ToString() == existingId);
                                if (selectedItem != null)
                                    comboBox.SelectedItem = selectedItem;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Reference catalog '{field.ReferenceCatalog}' NOT FOUND!");
                            comboBox.ItemsSource = new List<ReferenceItem> { new ReferenceItem { DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден" } };
                        }

                        inputControl = comboBox;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Creating regular input for {field.Name}, FieldType: {field.FieldType}");

                        // Обычные поля
                        switch (field.FieldType)
                        {
                            case "Int":
                                var intBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                                if (existingData != null && existingData.ContainsKey(field.Name))
                                    intBox.Text = existingData[field.Name]?.ToString();
                                inputControl = intBox;
                                break;

                            case "Decimal":
                                var decimalBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                                if (existingData != null && existingData.ContainsKey(field.Name))
                                    decimalBox.Text = existingData[field.Name]?.ToString();
                                inputControl = decimalBox;
                                break;

                            case "DateTime":
                                var datePicker = new DatePicker { Height = 35 };
                                if (existingData != null && existingData.ContainsKey(field.Name))
                                    datePicker.SelectedDate = existingData[field.Name] as DateTime?;
                                inputControl = datePicker;
                                break;

                            case "Bool":
                                var checkBox = new CheckBox { Content = "Да", Margin = new Thickness(0, 5, 0, 0) };
                                if (existingData != null && existingData.ContainsKey(field.Name))
                                    checkBox.IsChecked = (bool?)existingData[field.Name];
                                inputControl = checkBox;
                                break;

                            default: // String
                                var textBox = new TextBox { Height = 35, Padding = new Thickness(10) };

                                // Если это поле ФИО - делаем ReadOnly
                                if (field.Name == "ФИО")
                                {
                                    textBox.IsReadOnly = true;
                                    textBox.Background = System.Windows.Media.Brushes.LightGray;
                                }

                                if (existingData != null && existingData.ContainsKey(field.Name))
                                    textBox.Text = existingData[field.Name]?.ToString();
                                inputControl = textBox;
                                break;
                        }
                    }

                    panel.Children.Add(inputControl);
                    FieldsPanel.Children.Add(panel);
                    _controls[field.Name] = inputControl;
                }

                System.Diagnostics.Debug.WriteLine("=== BuildFieldsAsync COMPLETED SUCCESSFULLY ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync ERROR ===");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                MessageBox.Show($"Ошибка: {ex.Message}\n\n{ex.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
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

                // Заполняем Табельный номер (если это ComboBox)
                if (_controls.ContainsKey("Табельный номер") && _controls["Табельный номер"] is ComboBox comboBox)
                {
                    // Ищем элемент с соответствующим Id
                    var item = comboBox.ItemsSource?.Cast<ReferenceItem>().FirstOrDefault(i => i.Id == employeeId);
                    if (item != null)
                        comboBox.SelectedItem = item;
                }

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
                    var control = _controls[field.Name];
                    object value = null;

                    if (control is ComboBox comboBox)
                    {
                        var selectedItem = comboBox.SelectedItem as ReferenceItem;
                        value = selectedItem?.Id.ToString() ?? "";
                    }
                    else
                    {
                        switch (field.FieldType)
                        {
                            case "Int":
                                if (control is TextBox tbInt && int.TryParse(tbInt.Text, out int intVal))
                                    value = intVal;
                                break;

                            case "Decimal":
                                if (control is TextBox tbDec && decimal.TryParse(tbDec.Text, out decimal decVal))
                                    value = decVal;
                                break;

                            case "DateTime":
                                if (control is DatePicker dp)
                                    value = dp.SelectedDate;
                                break;

                            case "Bool":
                                if (control is CheckBox cb)
                                    value = cb.IsChecked ?? false;
                                break;

                            default:
                                if (control is TextBox tb)
                                    value = tb.Text;
                                break;
                        }
                    }

                    _itemData[field.Name] = value ?? DBNull.Value;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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