using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
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
        private readonly bool _isReadOnly;
        private readonly Dictionary<string, Control> _fieldControls = new();
        private readonly Dictionary<string, FrameworkElement> _fieldPanels = new();
        private readonly Dictionary<string, MetadataField> _fieldsByName = new();
        private readonly MetadataService _metadataService;
        private readonly Dictionary<string, object>? _initialData;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private Dictionary<string, object>? _existingData;
        private string? _assignedModuleName;

        public Dictionary<string, object> ItemData { get; private set; } = new();

        public DynamicDocumentItemDialog(
            MetadataObject metadata,
            MetadataService metadataService,
            Guid? editId = null,
            bool isReadOnly = false,
            Dictionary<string, object>? initialData = null)
        {
            InitializeComponent();
            _metadata = metadata;
            _metadataService = metadataService;
            _editId = editId;
            _isReadOnly = isReadOnly;
            _initialData = initialData;
            Title = $"{(isReadOnly ? "Просмотр" : editId.HasValue ? "Редактирование" : "Добавление")}: {metadata.Name}";

            if (_isReadOnly)
            {
                SaveButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "Закрыть";
            }

            Loaded += async (s, e) => await BuildFormAsync();
        }

        private async Task BuildFormAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _existingData = await LoadExistingDataAsync();
            _assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(_metadata.Id, _metadata.ObjectType);

            FieldsPanel.Children.Clear();
            _fieldControls.Clear();
            _fieldPanels.Clear();
            _fieldsByName.Clear();

            if (IsFixedAssetMovementDocument())
            {
                await BuildFixedAssetMovementFormAsync(catalogsDict);
                UpdateAccountControlledFieldsVisibility();
                return;
            }

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
                if (_isReadOnly)
                    ApplyReadOnly(inputControl);

                if (_metadata.ObjectType == "Document" &&
                    MetadataService.IsDocumentNumberFieldName(field.Name) &&
                    inputControl is TextBox numberTextBox &&
                    !_editId.HasValue)
                {
                    numberTextBox.IsReadOnly = true;
                    numberTextBox.Background = Brushes.LightGray;

                    try
                    {
                        numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata);
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

        private bool IsFixedAssetMovementDocument()
        {
            return _metadata.ObjectType == "Document" &&
                   string.Equals(_metadata.Name, "Учет движения ОС", StringComparison.OrdinalIgnoreCase);
        }

        private async Task BuildFixedAssetMovementFormAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            Width = Math.Max(Width, 1040);
            Height = Math.Max(Height, 720);
            MinWidth = Math.Max(MinWidth, 860);
            MinHeight = Math.Max(MinHeight, 620);

            FieldsPanel.Children.Add(CreateFixedAssetMovementHeader());

            var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var documentGrid = CreateTwoColumnGrid(3);
            await AddFieldToGridAsync(documentGrid, 0, 0, catalogsDict, usedFields, "Вид документа ОС");
            await AddFieldToGridAsync(documentGrid, 0, 1, catalogsDict, usedFields, "Номер");
            await AddFieldToGridAsync(documentGrid, 1, 0, catalogsDict, usedFields, "Дата");
            await AddFieldToGridAsync(documentGrid, 1, 1, catalogsDict, usedFields, "Серия/№ бланка");
            await AddFieldToGridAsync(documentGrid, 2, 0, catalogsDict, usedFields, "№ счет-фактуры", "invoice_number");
            FieldsPanel.Children.Add(CreateSection("Документ", documentGrid));

            var settlementGrid = CreateTwoColumnGrid(2);
            await AddFieldToGridAsync(settlementGrid, 0, 0, catalogsDict, usedFields, "Счет расчетов");
            await AddFieldToGridAsync(settlementGrid, 0, 1, catalogsDict, usedFields, "Организация");
            await AddFieldToGridAsync(settlementGrid, 1, 0, catalogsDict, usedFields, "Вид покупки");
            FieldsPanel.Children.Add(CreateSection("Поставщик и расчеты", settlementGrid));

            var taxGrid = CreateTwoColumnGrid(3);
            await AddFieldToGridAsync(taxGrid, 0, 0, catalogsDict, usedFields, "Вид НДС");
            await AddFieldToGridAsync(taxGrid, 0, 1, catalogsDict, usedFields, "Вид оплаты");
            await AddFieldToGridAsync(taxGrid, 1, 0, catalogsDict, usedFields, "Вид налога с продаж");
            await AddFieldToGridAsync(taxGrid, 1, 1, catalogsDict, usedFields, "Валюта");
            await AddFieldToGridAsync(taxGrid, 2, 0, catalogsDict, usedFields, "Курс валюты");
            FieldsPanel.Children.Add(CreateSection("Налоги и валюта", taxGrid));

            var lineGrid = CreateFixedAssetLineGrid();
            await AddFieldToGridAsync(lineGrid, 1, 0, catalogsDict, usedFields, "Основное средство");
            await AddFieldToGridAsync(lineGrid, 1, 1, catalogsDict, usedFields, "Счет операции");
            await AddFieldToGridAsync(lineGrid, 1, 2, catalogsDict, usedFields, "Без НДС");
            await AddFieldToGridAsync(lineGrid, 1, 3, catalogsDict, usedFields, "НДС");
            await AddFieldToGridAsync(lineGrid, 1, 4, catalogsDict, usedFields, "% НДС");
            await AddFieldToGridAsync(lineGrid, 1, 5, catalogsDict, usedFields, "Налог с продаж");
            await AddFieldToGridAsync(lineGrid, 1, 6, catalogsDict, usedFields, "Сумма", "amount");
            await AddFieldToGridAsync(lineGrid, 1, 7, catalogsDict, usedFields, "Сумма в валюте");
            FieldsPanel.Children.Add(CreateSection("Строка основного средства", new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = lineGrid
            }));

            var postingGrid = CreateTwoColumnGrid(3);
            await AddFieldToGridAsync(postingGrid, 0, 0, catalogsDict, usedFields, "Счет дебета");
            await AddFieldToGridAsync(postingGrid, 0, 1, catalogsDict, usedFields, "Счет кредита");
            await AddFieldToGridAsync(postingGrid, 1, 0, catalogsDict, usedFields, "Основание");
            await AddFieldToGridAsync(postingGrid, 1, 1, catalogsDict, usedFields, "Примечание");
            await AddFieldToGridAsync(postingGrid, 2, 0, catalogsDict, usedFields, "Проведен");
            FieldsPanel.Children.Add(CreateSection("Проводка и примечание", postingGrid));

            var remainingFields = _metadata.Fields
                .OrderBy(field => field.Order)
                .Where(field => !usedFields.Contains(field.Name))
                .ToList();

            if (remainingFields.Count == 0)
                return;

            var additionalGrid = CreateTwoColumnGrid((remainingFields.Count + 1) / 2);
            for (var index = 0; index < remainingFields.Count; index++)
            {
                await AddFieldByMetadataToGridAsync(
                    additionalGrid,
                    index / 2,
                    index % 2,
                    remainingFields[index],
                    catalogsDict,
                    usedFields);
            }

            FieldsPanel.Children.Add(CreateSection("Дополнительно", additionalGrid));
        }

        private Border CreateFixedAssetMovementHeader()
        {
            var title = _editId.HasValue
                ? "Редактирование документа движения ОС"
                : "Новый документ движения ОС";

            var subtitle = _existingData?.GetValueOrDefault("Вид документа ОС")?.ToString();
            if (string.IsNullOrWhiteSpace(subtitle))
                subtitle = "Сначала выбран вид ввода, далее заполняются реквизиты документа.";

            return new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(238, 247, 255),
                    Color.FromRgb(249, 252, 255),
                    0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 225, 242)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 20,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(14, 45, 77))
                        },
                        new TextBlock
                        {
                            Text = subtitle,
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(72, 99, 124))
                        }
                    }
                }
            };
        }

        private static Border CreateSection(string title, UIElement content)
        {
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 225, 235)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 88)),
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        content
                    }
                }
            };
        }

        private static Grid CreateTwoColumnGrid(int rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var row = 0; row < rows; row++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            return grid;
        }

        private static Grid CreateFixedAssetLineGrid()
        {
            var grid = new Grid { MinWidth = 960 };
            foreach (var width in new[] { 230d, 170d, 110d, 100d, 70d, 130d, 110d, 120d })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headers = new[]
            {
                "Наименование ОС", "Счет операции", "Без НДС", "НДС", "%", "Налог с продаж", "Итого", "В валюте"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                var header = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(213, 225, 236)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = new SolidColorBrush(Color.FromRgb(231, 240, 248)),
                    Padding = new Thickness(6, 5, 6, 5),
                    Child = new TextBlock
                    {
                        Text = headers[index],
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, index);
                grid.Children.Add(header);
            }

            return grid;
        }

        private async Task<bool> AddFieldToGridAsync(
            Grid grid,
            int row,
            int column,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields,
            params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field == null)
                return false;

            return await AddFieldByMetadataToGridAsync(grid, row, column, field, catalogsDict, usedFields);
        }

        private async Task<bool> AddFieldByMetadataToGridAsync(
            Grid grid,
            int row,
            int column,
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields)
        {
            if (_fieldControls.ContainsKey(field.Name))
                return false;

            var panel = await CreateFieldPanelAsync(field, catalogsDict);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            usedFields.Add(field.Name);
            return true;
        }

        private async Task<StackPanel> CreateFieldPanelAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 10) };

            panel.Children.Add(new TextBlock
            {
                Text = field.Name,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var inputControl = await CreateControlAsync(field, catalogsDict);
            if (_isReadOnly)
                ApplyReadOnly(inputControl);

            if (_metadata.ObjectType == "Document" &&
                MetadataService.IsDocumentNumberFieldName(field.Name) &&
                inputControl is TextBox numberTextBox &&
                !_editId.HasValue)
            {
                numberTextBox.IsReadOnly = true;
                numberTextBox.Background = Brushes.LightGray;

                try
                {
                    numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata);
                }
                catch
                {
                    numberTextBox.Text = MetadataService.GenerateFallbackDocumentNumber();
                }
            }

            panel.Children.Add(inputControl);
            _fieldControls[field.Name] = inputControl;
            _fieldPanels[field.Name] = panel;
            _fieldsByName[field.Name] = field;
            return panel;
        }

        private MetadataField? FindDialogField(params string[] aliases)
        {
            foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                var field = _metadata.Fields.FirstOrDefault(item =>
                    string.Equals(item.Name, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DbColumnName, alias, StringComparison.OrdinalIgnoreCase));

                if (field != null)
                    return field;
            }

            return null;
        }
        private async Task<Dictionary<string, object>?> LoadExistingDataAsync()
        {
            if (!_editId.HasValue)
                return _initialData == null
                    ? null
                    : new Dictionary<string, object>(_initialData, StringComparer.OrdinalIgnoreCase);

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
                return AccountPickerControlFactory.Create(
                    _accountAnalytics,
                    currentValue,
                    this,
                    UpdateAccountControlledFieldsVisibility,
                    _assignedModuleName);
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

        private async Task<Control> CreateReferenceControlAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            string safeName,
            object? currentValue)
        {
            if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
            {
                var comboBox = new ComboBox
                {
                    Height = 30,
                    Name = safeName,
                    DisplayMemberPath = "DisplayName",
                    SelectedValuePath = "Id",
                    MinWidth = 200
                };

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

            return await ReferencePickerControlFactory.CreateAsync(
                _metadataService,
                field,
                refCatalog,
                currentValue,
                this,
                UpdateAccountControlledFieldsVisibility);
        }

        private static IEnumerable<string> GetReferenceLookupKeys(
            Dictionary<string, object> row,
            string displayName)
        {
            foreach (var keyName in new[]
                     {
                         "Id", "Код", "code", "Code", "Счет", "account_code",
                         "Код организации", "organization_code",
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

            foreach (var value in row.Values)
            {
                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }
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

        private Control CreateRegularControl(MetadataField field, string safeName, object? currentValue)
        {
            switch (field.FieldType)
            {
                case "DateTime":
                    var datePicker = new DatePicker { Height = 30, Name = safeName };
                    if (currentValue is DateTime dt)
                        datePicker.SelectedDate = dt;
                    else if (!_editId.HasValue)
                        datePicker.SelectedDate = DateTime.Today;
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
                        Text = currentValue?.ToString() ??
                               (!_editId.HasValue && field.FieldType == "Decimal" ? "0" : string.Empty)
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
                .Select(pair => AccountPickerControlFactory.GetSelectedAccount(pair.Value))
                .Where(account => account != null)
                .Select(account => _accountAnalytics.GetSettings(account))
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
                case ReferencePickerControl referencePicker:
                    referencePicker.ClearSelection();
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
            if (_isReadOnly)
            {
                Close();
                return;
            }

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
                case UserControl when AccountPickerControlFactory.GetSelectedAccount(control) != null:
                    return AccountPickerControlFactory.GetSelectedAccountValue(field, control);
                case ReferencePickerControl referencePicker when referencePicker.SelectedReferenceItem is BIS.ERP.Models.ReferenceItem selectedReference:
                    return selectedReference.Id == Guid.Empty ? string.Empty : selectedReference.Id.ToString();
                case ReferencePickerControl:
                    return string.Empty;
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

        private static void ApplyReadOnly(Control control)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.IsReadOnly = true;
                    textBox.Background = Brushes.LightGray;
                    break;
                case ComboBox comboBox:
                    comboBox.IsEnabled = false;
                    break;
                case DatePicker datePicker:
                    datePicker.IsEnabled = false;
                    break;
                case CheckBox checkBox:
                    checkBox.IsEnabled = false;
                    break;
                default:
                    control.IsEnabled = false;
                    break;
            }
        }
    }
}


