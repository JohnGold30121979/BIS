using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views.Dialogs
{
    public partial class DynamicDocumentItemDialog : Window
    {
        private readonly MetadataObject _metadata;
        private readonly Guid? _editId;
        private readonly Dictionary<string, Control> _fieldControls = new();
        private readonly Dictionary<string, FrameworkElement> _fieldPanels = new();
        private readonly Dictionary<string, MetadataField> _fieldsByName = new();
        private readonly MetadataService _metadataService;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private Dictionary<string, object>? _existingData;

        public Dictionary<string, object> ItemData { get; private set; } = new();

        public DynamicDocumentItemDialog(MetadataObject metadata, MetadataService metadataService, Guid? editId = null)
        {
            InitializeComponent();
            _metadata = metadata;
            _metadataService = metadataService;
            _editId = editId;
            Title = $"{(editId.HasValue ? "Редактирование" : "Добавление")}: {metadata.Name}";

            Loaded += async (s, e) => await BuildFormAsync();
        }

        private async Task BuildFormAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _existingData = await LoadExistingDataAsync();

            FieldsPanel.Children.Clear();
            _fieldControls.Clear();
            _fieldPanels.Clear();
            _fieldsByName.Clear();

            foreach (var field in _metadata.Fields.OrderBy(f => f.Order))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

                panel.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var inputControl = await CreateControlAsync(field, catalogsDict);

                if (_metadata.ObjectType == "Document" &&
                    MetadataService.IsDocumentNumberFieldName(field.Name) &&
                    inputControl is TextBox numberTextBox &&
                    !_editId.HasValue)
                {
                    numberTextBox.IsReadOnly = true;
                    numberTextBox.Background = Brushes.LightGray;

                    try
                    {
                        numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata.Name);
                    }
                    catch
                    {
                        numberTextBox.Text = MetadataService.GenerateFallbackDocumentNumber();
                    }
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
                _fieldPanels[field.Name] = panel;
                _fieldsByName[field.Name] = field;
            }

            UpdateAccountControlledFieldsVisibility();
        }

        private async Task<Dictionary<string, object>?> LoadExistingDataAsync()
        {
            if (!_editId.HasValue)
                return null;

            var data = await _metadataService.GetCatalogDataAsync(_metadata.Id);
            return data.FirstOrDefault(row =>
                row.TryGetValue("Id", out var id) && id?.ToString() == _editId.Value.ToString());
        }

        private async Task<Control> CreateControlAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict)
        {
            var safeName = GetSafeControlName(field.Name);
            var currentValue = _existingData?.GetValueOrDefault(field.Name);

            if (AccountAnalyticsRules.IsAccountSelectorField(field) && _accountAnalytics.Accounts.Count > 0)
            {
                var comboBox = new ComboBox
                {
                    Height = 30,
                    Name = safeName,
                    DisplayMemberPath = "DisplayName",
                    SelectedValuePath = "Id",
                    MinWidth = 200,
                    ItemsSource = _accountAnalytics.Accounts
                };

                var selectedAccount = _accountAnalytics.FindAccount(currentValue);
                if (selectedAccount != null)
                    comboBox.SelectedItem = selectedAccount;

                comboBox.SelectionChanged += (s, e) => UpdateAccountControlledFieldsVisibility();
                return comboBox;
            }

            if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                return await CreateReferenceControlAsync(field, catalogsDict, safeName, currentValue);

            if (!string.IsNullOrEmpty(field.Formula))
            {
                return new TextBox
                {
                    Height = 30,
                    Name = safeName,
                    IsReadOnly = true,
                    Background = Brushes.LightGray,
                    Text = currentValue?.ToString() ?? string.Empty
                };
            }

            return CreateRegularControl(field, safeName, currentValue);
        }

        private async Task<ComboBox> CreateReferenceControlAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            string safeName,
            object? currentValue)
        {
            var comboBox = new ComboBox
            {
                Height = 30,
                Name = safeName,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                MinWidth = 200
            };

            if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
            {
                comboBox.ItemsSource = new List<BIS.ERP.Models.ReferenceItem>
                {
                    new BIS.ERP.Models.ReferenceItem
                    {
                        Id = Guid.Empty,
                        DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден"
                    }
                };
                return comboBox;
            }

            var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
            var items = refData
                .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                .Select(row => new BIS.ERP.Models.ReferenceItem
                {
                    Id = Guid.Parse(row["Id"].ToString()),
                    DisplayName = GetDisplayValue(row, field)
                })
                .ToList();

            comboBox.ItemsSource = items;

            if (currentValue != null && Guid.TryParse(currentValue.ToString(), out var currentGuid))
            {
                var selected = items.FirstOrDefault(item => item.Id == currentGuid);
                if (selected != null)
                    comboBox.SelectedItem = selected;
            }

            return comboBox;
        }

        private Control CreateRegularControl(MetadataField field, string safeName, object? currentValue)
        {
            switch (field.FieldType)
            {
                case "DateTime":
                    var datePicker = new DatePicker { Height = 30, Name = safeName };
                    if (currentValue is DateTime dt)
                        datePicker.SelectedDate = dt;
                    return datePicker;

                case "Bool":
                    var checkBox = new CheckBox
                    {
                        Content = "Да",
                        Name = safeName,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (currentValue is bool boolValue)
                        checkBox.IsChecked = boolValue;
                    else if (bool.TryParse(currentValue?.ToString(), out var parsedBool))
                        checkBox.IsChecked = parsedBool;
                    return checkBox;

                default:
                    return new TextBox
                    {
                        Height = 30,
                        Name = safeName,
                        Text = currentValue?.ToString() ?? string.Empty
                    };
            }
        }

        private string GetSafeControlName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "control";
            return new string(fieldName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        private string GetDisplayValue(Dictionary<string, object> row, MetadataField field)
        {
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var displayField in fieldNames.Select(name => name.Trim()))
                {
                    var value = row.GetValueOrDefault(displayField)?.ToString() ??
                                row.GetValueOrDefault(displayField.Replace(" ", "_"))?.ToString() ??
                                string.Empty;

                    result = result.Replace($"{{{displayField}}}", value);
                    result = result.Replace($"{{{displayField.Replace(" ", "_")}}}", value);
                }

                return result;
            }

            var priorityFields = new[]
            {
                "Наименование", "name", "ФИО", "full_name",
                "Наименование материала", "Код", "code", "Счет"
            };

            foreach (var priority in priorityFields)
            {
                if (row.TryGetValue(priority, out var value) && value != null)
                    return value.ToString() ?? string.Empty;
            }

            return row.GetValueOrDefault("Id")?.ToString() ?? "Без имени";
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var selectedSettings = _fieldControls
                .Where(pair => _fieldsByName.TryGetValue(pair.Key, out var field) &&
                               AccountAnalyticsRules.IsAccountSelectorField(field))
                .Select(pair => pair.Value as ComboBox)
                .Where(comboBox => comboBox?.SelectedItem is AccountReferenceItem)
                .Select(comboBox => _accountAnalytics.GetSettings((AccountReferenceItem)comboBox!.SelectedItem))
                .ToList();

            foreach (var field in _metadata.Fields)
            {
                if (field.FieldType != "Reference" ||
                    AccountAnalyticsRules.IsAccountSelectorField(field))
                {
                    continue;
                }

                if (!AccountAnalyticsRules.IsAccountControlledField(field, _accountAnalytics.Definitions))
                {
                    SetFieldVisibility(field.Name, false);
                    continue;
                }

                SetFieldVisibility(
                    field.Name,
                    AccountAnalyticsRules.ShouldShowField(
                        field,
                        selectedSettings,
                        _accountAnalytics.Definitions,
                        showWhenNoAccountSelected: false,
                        showUnmappedFields: false));
            }
        }

        private void SetFieldVisibility(string fieldName, bool isVisible)
        {
            if (!_fieldPanels.TryGetValue(fieldName, out var panel))
                return;

            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && _fieldControls.TryGetValue(fieldName, out var control))
                ClearControl(control);
        }

        private static void ClearControl(Control control)
        {
            switch (control)
            {
                case ComboBox comboBox:
                    comboBox.SelectedItem = null;
                    break;
                case TextBox textBox:
                    textBox.Clear();
                    break;
                case CheckBox checkBox:
                    checkBox.IsChecked = false;
                    break;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ItemData.Clear();

                foreach (var field in _metadata.Fields.OrderBy(f => f.Order))
                {
                    if (!_fieldControls.TryGetValue(field.Name, out var control))
                        continue;

                    object? value;

                    if (_fieldPanels.TryGetValue(field.Name, out var panel) &&
                        panel.Visibility == Visibility.Collapsed)
                    {
                        value = AccountAnalyticsRules.GetEmptyValue(field);
                    }
                    else
                    {
                        value = GetValueFromControl(control, field);
                    }

                    if (_metadata.ObjectType == "Document" &&
                        MetadataService.IsDocumentNumberFieldName(field.Name))
                    {
                        var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(value?.ToString());
                        if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(c => !char.IsDigit(c)))
                        {
                            MessageBox.Show("Номер документа должен содержать только цифры.", "Проверка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            control.Focus();
                            return;
                        }

                        value = documentNumber;
                    }

                    ItemData[field.Name] = value ?? AccountAnalyticsRules.GetEmptyValue(field);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private object? GetValueFromControl(Control control, MetadataField field)
        {
            switch (control)
            {
                case ComboBox comboBox when comboBox.SelectedItem is AccountReferenceItem account:
                    return AccountAnalyticsRules.GetAccountValueForField(field, account);
                case ComboBox comboBox when comboBox.SelectedItem is BIS.ERP.Models.ReferenceItem selectedItem:
                    return selectedItem.Id == Guid.Empty ? string.Empty : selectedItem.Id.ToString();
                case ComboBox:
                    return string.Empty;
                case DatePicker datePicker:
                    return datePicker.SelectedDate;
                case CheckBox checkBox:
                    return checkBox.IsChecked ?? false;
                case TextBox textBox:
                    return ParseFieldValue(textBox.Text, field.FieldType);
                default:
                    return null;
            }
        }

        private object? ParseFieldValue(string text, string fieldType)
        {
            if (string.IsNullOrWhiteSpace(text))
                return AccountAnalyticsRules.GetEmptyValue(new MetadataField { FieldType = fieldType });

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
                return AccountAnalyticsRules.GetEmptyValue(new MetadataField { FieldType = fieldType });
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
