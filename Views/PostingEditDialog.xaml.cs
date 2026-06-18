using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class PostingEditDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private readonly Dictionary<string, Control> _fieldControls = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private string _generatedNumber = string.Empty;

        // Конструктор для добавления
        public PostingEditDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            _generatedNumber = string.Empty;

            DialogTitle.Text = $"Добавление: {document.Name}";
            this.ContentRendered += async (s, e) => await InitializeAsync();
        }

        // Конструктор для редактирования (принимает ID)
        public PostingEditDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            _generatedNumber = string.Empty;

            DialogTitle.Text = $"Редактирование: {document.Name}";
            this.ContentRendered += async (s, e) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isDataLoaded || _isLoading) return;
            _isLoading = true;

            try
            {
                System.Diagnostics.Debug.WriteLine("=== PostingEditDialog InitializeAsync START ===");
                this.Cursor = Cursors.Wait;
                StatusText.Text = "Загрузка...";

                // Загружаем данные в фоновом потоке
                var dialogData = await Task.Run(async () => await LoadAllDataAsync(editId));

                // Обновляем UI в основном потоке
                await Dispatcher.InvokeAsync(() =>
                {
                    System.Diagnostics.Debug.WriteLine("Обновление UI...");
                    BuildFormFromData(dialogData, editId);
                    System.Diagnostics.Debug.WriteLine("UI обновлён");
                });

                _isDataLoaded = true;
                StatusText.Text = "Готово";
                System.Diagnostics.Debug.WriteLine("=== PostingEditDialog InitializeAsync COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== PostingEditDialog ERROR: {ex.Message} ===");
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
                _isLoading = false;
            }
        }

        private class PostingDialogData
        {
            public Dictionary<string, object>? ExistingData { get; set; }
            public Dictionary<string, List<ReferenceItem>> ReferenceData { get; set; } = new();
            public string DocumentNumber { get; set; } = string.Empty;
        }

        private async Task<PostingDialogData> LoadAllDataAsync(Guid? editId)
        {
            System.Diagnostics.Debug.WriteLine("1. Начинаем загрузку данных...");

            var result = new PostingDialogData();

            // Загружаем существующие данные для редактирования
            if (editId.HasValue)
            {
                var allData = await _metadataService.GetCatalogDataAsync(_document.Id);
                result.ExistingData = allData.FirstOrDefault(r =>
                    r.TryGetValue("Id", out var id) && id?.ToString() == editId.Value.ToString());
            }

            // Загружаем все справочники
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

            // Загружаем данные для Reference полей
            foreach (var field in _document.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                {
                    var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                    var items = new List<ReferenceItem>();

                    foreach (var row in refData)
                    {
                        if (!row.TryGetValue("Id", out var idObj) || idObj == null)
                            continue;

                        var id = Guid.Parse(idObj.ToString());
                        var displayName = row.GetValueOrDefault("Наименование")?.ToString() ??
                                          row.GetValueOrDefault("name")?.ToString() ??
                                          row.GetValueOrDefault("Код")?.ToString() ??
                                          "Без имени";

                        items.Add(new ReferenceItem { Id = id, DisplayName = displayName });
                    }

                    result.ReferenceData[field.Name] = items;
                }
            }

            // Генерируем номер для нового документа
            if (!editId.HasValue)
            {
                try
                {
                    result.DocumentNumber = await _metadataService.GetNextDocumentNumberAsync(_document.Name);
                }
                catch
                {
                    result.DocumentNumber = MetadataService.GenerateFallbackDocumentNumber();
                }
            }

            System.Diagnostics.Debug.WriteLine($"2. Загружено Reference полей: {result.ReferenceData.Count}");
            return result;
        }

        private void BuildFormFromData(PostingDialogData dialogData, Guid? editId)
        {
            FieldsPanel.Children.Clear();
            _fieldControls.Clear();

            foreach (var field in _document.Fields.OrderBy(f => f.Order))
            {
                if (field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt")
                    continue;

                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                panel.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 3),
                    FontSize = 13
                });

                Control inputControl = null!; // Инициализируем null с оператором подавления
                object? currentValue = dialogData.ExistingData?.GetValueOrDefault(field.Name);

                // Для поля "Тип документа" - устанавливаем значение "Ручная проводка"
                if (field.Name == "Тип документа")
                {
                    var textBox = new TextBox
                    {
                        Height = 30,
                        IsReadOnly = true,
                        Background = System.Windows.Media.Brushes.LightGray,
                        Text = "Ручная проводка"
                    };

                    if (currentValue != null && !string.IsNullOrEmpty(currentValue.ToString()))
                        textBox.Text = currentValue.ToString();

                    inputControl = textBox;
                }
                else if (field.FieldType == "Reference" && !string.IsNullOrEmpty(field.ReferenceCatalog))
                {
                    var comboBox = new ComboBox
                    {
                        Height = 30,
                        DisplayMemberPath = "DisplayName",
                        SelectedValuePath = "Id",
                        MinWidth = 200
                    };

                    if (dialogData.ReferenceData.TryGetValue(field.Name, out var items))
                    {
                        comboBox.ItemsSource = items;

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
                    if (currentValue is DateTime dt)
                        picker.SelectedDate = dt;
                    inputControl = picker;
                }
                else if (field.FieldType == "Bool")
                {
                    var checkBox = new CheckBox { Content = "Да", Height = 30 };
                    if (currentValue is bool b)
                        checkBox.IsChecked = b;
                    inputControl = checkBox;
                }
                else
                {
                    var textBox = new TextBox { Height = 30 };

                    // Для поля "Номер документа" - делаем ReadOnly
                    if (field.Name == "Номер документа")
                    {
                        textBox.IsReadOnly = true;
                        textBox.Background = System.Windows.Media.Brushes.LightGray;

                        if (!string.IsNullOrEmpty(dialogData.DocumentNumber))
                            textBox.Text = dialogData.DocumentNumber;
                        else if (currentValue != null)
                            textBox.Text = MetadataService.NormalizeLegacyDocumentNumber(currentValue.ToString());
                    }
                    else
                    {
                        if (currentValue != null)
                            textBox.Text = currentValue.ToString();
                    }

                    inputControl = textBox;
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var itemData = new Dictionary<string, object>();

                foreach (var field in _document.Fields.OrderBy(f => f.Order))
                {
                    if (field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt")
                        continue;

                    if (!_fieldControls.TryGetValue(field.Name, out var control))
                        continue;

                    object? value = null;

                    switch (control)
                    {
                        case ComboBox comboBox when comboBox.SelectedItem is ReferenceItem selected && selected.Id != Guid.Empty:
                            value = selected.Id;
                            break;
                        case DatePicker datePicker:
                            value = datePicker.SelectedDate;
                            break;
                        case CheckBox checkBox:
                            value = checkBox.IsChecked ?? false;
                            break;
                        case TextBox textBox:
                            // Безопасное преобразование для числовых полей
                            value = ParseFieldValue(textBox.Text, field.FieldType);
                            break;
                    }

                    if (value != null)
                        itemData[field.Name] = value;
                }

                // Добавляем тип документа для ручных проводок
                if (!itemData.ContainsKey("Тип документа"))
                {
                    itemData["Тип документа"] = "Ручная проводка";
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
            finally
            {
                this.Cursor = null;
            }
        }

        private object? ParseFieldValue(string text, string fieldType)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fieldType switch
                {
                    "Int" => 0,
                    "Decimal" => 0m,
                    _ => text
                };
            }

            try
            {
                return fieldType switch
                {
                    "Int" => int.Parse(text),
                    "Decimal" => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                    _ => text
                };
            }
            catch
            {
                return fieldType switch
                {
                    "Int" => 0,
                    "Decimal" => 0m,
                    _ => text
                };
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
