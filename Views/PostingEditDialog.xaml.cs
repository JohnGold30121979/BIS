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
    public partial class PostingEditDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private readonly Dictionary<string, Control> _fieldControls = new();

        // Конструктор для добавления
        public PostingEditDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;

            DialogTitle.Text = $"Добавление: {document.Name}";
            Loaded += async (s, e) => await BuildFormFromMetadata();
        }

        // Конструктор для редактирования (принимает ID)
        public PostingEditDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;

            DialogTitle.Text = $"Редактирование: {document.Name}";
            Loaded += async (s, e) => await BuildFormFromMetadata();
        }

        private async Task<Dictionary<string, object>> LoadExistingDataAsync()
        {
            if (!_editId.HasValue)
                return null;

            try
            {
                var allData = await _metadataService.GetCatalogDataAsync(_document.Id);
                return allData.FirstOrDefault(r => r.ContainsKey("Id") && r["Id"].ToString() == _editId.Value.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки данных: {ex.Message}");
                return null;
            }
        }

        private async Task BuildFormFromMetadata()
        {
            try
            {
                var existingDataDict = await LoadExistingDataAsync();
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

                FieldsPanel.Children.Clear();
                _fieldControls.Clear();

                foreach (var field in _document.Fields.OrderBy(f => f.Order))
                {
                    if (field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt")
                        continue;

                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
                    panel.Children.Add(new TextBlock
                    {
                        Text = field.Name,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5)
                    });

                    Control inputControl;
                    object currentValue = existingDataDict?.ContainsKey(field.Name) == true ? existingDataDict[field.Name] : null;

                    // Reference поле
                    if (field.FieldType == "Reference" && !string.IsNullOrEmpty(field.ReferenceCatalog))
                    {
                        var comboBox = new ComboBox
                        {
                            Height = 30,
                            DisplayMemberPath = "DisplayName",
                            SelectedValuePath = "Id",
                            MinWidth = 200
                        };

                        if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                        {
                            var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                            var items = new List<ReferenceItem>();

                            foreach (var row in refData)
                            {
                                if (!row.ContainsKey("Id")) continue;

                                var item = new ReferenceItem
                                {
                                    Id = Guid.Parse(row["Id"].ToString())
                                };

                                // Формируем отображаемое имя
                                string displayName = "";
                                if (row.ContainsKey("Наименование"))
                                    displayName = row["Наименование"].ToString();
                                else if (row.ContainsKey("name"))
                                    displayName = row["name"].ToString();
                                else if (row.ContainsKey("Код"))
                                    displayName = row["Код"].ToString();
                                else
                                    displayName = "Без имени";

                                item.DisplayName = displayName;
                                items.Add(item);
                            }

                            comboBox.ItemsSource = items;

                            // Выбираем текущее значение
                            if (currentValue != null && Guid.TryParse(currentValue.ToString(), out var currentGuid))
                            {
                                var selectedItem = items.FirstOrDefault(i => i.Id == currentGuid);
                                if (selectedItem != null)
                                    comboBox.SelectedItem = selectedItem;
                            }
                        }
                        else
                        {
                            comboBox.ItemsSource = new List<ReferenceItem>
                    {
                        new ReferenceItem { DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден" }
                    };
                        }

                        inputControl = comboBox;
                    }
                    else if (field.FieldType == "DateTime")
                    {
                        var picker = new DatePicker { Height = 30 };
                        if (currentValue != null && DateTime.TryParse(currentValue.ToString(), out DateTime dt))
                            picker.SelectedDate = dt;
                        inputControl = picker;
                    }
                    else if (field.FieldType == "Bool")
                    {
                        var checkBox = new CheckBox { Content = "Да", Height = 30 };
                        if (currentValue != null && bool.TryParse(currentValue.ToString(), out bool b))
                            checkBox.IsChecked = b;
                        inputControl = checkBox;
                    }
                    else
                    {
                        var textBox = new TextBox { Height = 30 };
                        if (currentValue != null)
                            textBox.Text = currentValue.ToString();
                        inputControl = textBox;
                    }

                    panel.Children.Add(inputControl);
                    FieldsPanel.Children.Add(panel);
                    _fieldControls[field.Name] = inputControl;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка построения формы: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var itemData = new Dictionary<string, object>();

                foreach (var field in _document.Fields.OrderBy(f => f.Order))
                {
                    if (field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt")
                        continue;

                    if (!_fieldControls.ContainsKey(field.Name)) continue;

                    var control = _fieldControls[field.Name];
                    object value = null;

                    if (control is ComboBox comboBox)
                    {
                        var selected = comboBox.SelectedItem as ReferenceItem;
                        if (selected != null && selected.Id != Guid.Empty)
                            value = selected.Id;
                    }
                    else if (control is DatePicker datePicker)
                    {
                        value = datePicker.SelectedDate;
                    }
                    else if (control is CheckBox checkBox)
                    {
                        value = checkBox.IsChecked ?? false;
                    }
                    else if (control is TextBox textBox)
                    {
                        value = textBox.Text;
                    }

                    if (value != null)
                        itemData[field.Name] = value;
                }

                if (_editId.HasValue)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                }
                else
                {
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}