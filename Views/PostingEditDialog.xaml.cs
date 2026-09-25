using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
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
        private readonly Dictionary<string, FrameworkElement> _fieldPanels = new();
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private string _generatedNumber = string.Empty;
        private string? _assignedModuleName;

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
            public Dictionary<string, MetadataObject> ReferenceCatalogs { get; set; } = new();
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
            public string DocumentNumber { get; set; } = string.Empty;
            public string? AssignedModuleName { get; set; }
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
            var catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            result.AssignedModuleName = await _metadataService.GetAssignedModuleNameAsync(_document.Id, _document.ObjectType);

            // Загружаем данные для Reference полей
            foreach (var field in _document.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                {
                    result.ReferenceCatalogs[field.Name] = refCatalog;
                    var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                    var items = new List<ReferenceItem>();

                    foreach (var row in refData)
                    {
                        if (!row.TryGetValue("Id", out var idObj) || idObj == null)
                            continue;

                        var id = Guid.Parse(idObj.ToString());
                        if (field.ReferenceCatalog.Equals("Кассы", StringComparison.OrdinalIgnoreCase))
                        {
                            var accountCode = CashOrderDialog.ResolveCashDeskAccountCode(
                                GetRowString(row, "Счет", "Счет кассы", "Код", "code"),
                                result.AccountAnalytics);

                            items.Add(new CashDeskItem
                            {
                                Id = id,
                                DisplayName = GetRowString(
                                    row,
                                    "Наименование кассы",
                                    "Наименование",
                                    "name",
                                    "Код") ?? "Касса",
                                AccountCode = accountCode,
                                CashNumber = GetRowString(row, "Номер кассы", "cash_number") ?? string.Empty
                            });
                            continue;
                        }

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
                    result.DocumentNumber = await _metadataService.GetNextDocumentNumberAsync(_document);
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
            _fieldPanels.Clear();
            _accountAnalytics = dialogData.AccountAnalytics;
            _assignedModuleName = dialogData.AssignedModuleName;

            foreach (var field in _document.Fields.OrderBy(GetPostingFieldDisplayOrder).ThenBy(f => f.Order))
            {
                if (IsSystemField(field) || IsModuleField(field))
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
                else if (field.Name == "Дебет" || field.Name == "Кредит")
                {
                    inputControl = CreateAccountPickerControl(field.Name, currentValue);
                }
                else if (field.FieldType == "Reference" && !string.IsNullOrEmpty(field.ReferenceCatalog))
                {
                    var comboBox = new ComboBox
                    {
                        Height = 30,
                        DisplayMemberPath = field.ReferenceCatalog.Equals("Кассы", StringComparison.OrdinalIgnoreCase)
                            ? "DisplayNameWithAccount"
                            : "DisplayName",
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

                    if (field.ReferenceCatalog.Equals("Кассы", StringComparison.OrdinalIgnoreCase))
                        comboBox.SelectionChanged += (_, _) => ApplyCashDeskSelectionToPostingAccounts();

                    inputControl = comboBox;
                }
                else if (field.FieldType == "DateTime")
                {
                    var picker = new DatePicker { Height = 30 };
                    if (currentValue is DateTime dt)
                        picker.SelectedDate = dt;
                    else if (!editId.HasValue)
                        picker.SelectedDate = DateTime.Today;
                    inputControl = picker;
                }
                else if (field.FieldType == "Bool")
                {
                    var checkBox = new CheckBox { Content = "Да", Height = 30 };
                    if (currentValue is bool b)
                        checkBox.IsChecked = b;
                    else if (!editId.HasValue && IsActiveField(field))
                        checkBox.IsChecked = true;

                    if (!editId.HasValue && IsActiveField(field))
                    {
                        checkBox.IsChecked = true;
                        checkBox.IsEnabled = false;
                    }

                    inputControl = checkBox;
                }
                else
                {
                    var textBox = new TextBox
                    {
                        Height = 30,
                        Text = !editId.HasValue && field.FieldType == "Decimal" ? "0" : string.Empty
                    };

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
                AttachReferenceEditorIfNeeded(field, inputControl, dialogData);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
                _fieldPanels[field.Name] = panel;
            }

            UpdateAnalyticControlsVisibility();
        }

        private void AttachReferenceEditorIfNeeded(
            MetadataField field,
            Control inputControl,
            PostingDialogData dialogData)
        {
            if (inputControl is not ComboBox comboBox ||
                field.FieldType != "Reference" ||
                string.IsNullOrWhiteSpace(field.ReferenceCatalog) ||
                field.ReferenceCatalog.Equals("Кассы", StringComparison.OrdinalIgnoreCase) ||
                !dialogData.ReferenceCatalogs.TryGetValue(field.Name, out var referenceCatalog))
            {
                return;
            }

            ReferencePickerControlFactory.AttachEditor(
                comboBox,
                _metadataService,
                referenceCatalog,
                this,
                items => comboBox.ItemsSource = items);
        }
        private void ApplyCashDeskSelectionToPostingAccounts()
        {
            if (!_fieldControls.TryGetValue("Касса", out var cashControl) ||
                cashControl is not ComboBox { SelectedItem: CashDeskItem cashDesk })
            {
                return;
            }

            var account = _accountAnalytics.FindAccount(cashDesk.AccountCode);
            if (account == null)
                return;

            if (_fieldControls.TryGetValue("Дебет", out var debitControl) &&
                AccountPickerControlFactory.GetSelectedAccount(debitControl) == null)
            {
                AccountPickerControlFactory.SetSelectedAccount(debitControl, account);
            }
            else if (_fieldControls.TryGetValue("Кредит", out var creditControl) &&
                     AccountPickerControlFactory.GetSelectedAccount(creditControl) == null)
            {
                AccountPickerControlFactory.SetSelectedAccount(creditControl, account);
            }

            UpdateAnalyticControlsVisibility();
        }

        private UserControl CreateAccountPickerControl(string fieldName, object? currentValue)
        {
            return AccountPickerControlFactory.Create(
                _accountAnalytics,
                currentValue,
                this,
                UpdateAnalyticControlsVisibility,
                _assignedModuleName,
                allowManualInput: fieldName is "Дебет" or "Кредит");
        }

        private async Task<bool> TryResolveManualAccountAsync(string fieldName, Control control)
        {
            if (GetSelectedAccountFromControl(control) != null)
                return true;

            var code = AccountPickerControlFactory.GetAccountCode(control);
            var accountKind = fieldName == "Дебет" ? "дебета" : "кредита";
            if (code.Length == 0 ||
                code.Length > 8 ||
                code.Any(character => character is < '0' or > '9'))
            {
                MessageBox.Show(
                    $"Счёт {accountKind} должен содержать от 1 до 8 цифр.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                _document.Id,
                _document.ObjectType);
            var match = accountsData.FirstOrDefault(row =>
                row.TryGetValue("Код", out var codeValue) &&
                string.Equals(codeValue?.ToString()?.Trim(), code, StringComparison.Ordinal));

            if (match == null || !Guid.TryParse(match["Id"]?.ToString(), out var accountId))
            {
                MessageBox.Show(
                    $"Счёт с кодом «{code}» не найден в плане счетов для этого модуля.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var account = _accountAnalytics.FindAccount(accountId);
            if (account == null)
            {
                MessageBox.Show(
                    $"Счёт с кодом «{code}» найден в плане счетов, но не загружен для выбора.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            AccountPickerControlFactory.SetSelectedAccount(control, account);
            UpdateAnalyticControlsVisibility();
            return true;
        }

        private void UpdateAnalyticControlsVisibility()
        {
            var debitSettings = GetSelectedAccountSettings("Дебет");
            var creditSettings = GetSelectedAccountSettings("Кредит");
            var selectedSettings = new[] { debitSettings, creditSettings };
            var showCurrency = AccountAnalyticsRules.ShouldShowField(
                "Валюта",
                selectedSettings,
                _accountAnalytics.Definitions,
                "Справочник валют",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);

            SetAnalyticFieldVisibility("Валюта", showCurrency);
            SetAnalyticFieldVisibility("Сумма в валюте", showCurrency);
            SetAnalyticFieldVisibility("Организация",
                AccountAnalyticsRules.ShouldShowField(
                    "Организация",
                    selectedSettings,
                    _accountAnalytics.Definitions,
                    "Организации",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
            SetAnalyticFieldVisibility("Сотрудник",
                AccountAnalyticsRules.ShouldShowField(
                    "Сотрудник",
                    selectedSettings,
                    _accountAnalytics.Definitions,
                    "Сотрудники (Списочный состав)",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
            SetAnalyticFieldVisibility("Материал",
                AccountAnalyticsRules.ShouldShowField(
                    "Материал",
                    selectedSettings,
                    _accountAnalytics.Definitions,
                    "Справочник материалов",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
            SetAnalyticFieldVisibility("Договор", false);
            SetAnalyticFieldVisibility("Статья", false);
        }

        private AccountAnalyticsSettings? GetSelectedAccountSettings(string fieldName)
        {
            if (!_fieldControls.TryGetValue(fieldName, out var control))
            {
                return null;
            }

            var account = GetSelectedAccountFromControl(control);
            return account == null ? null : _accountAnalytics.GetSettings(account);
        }

        private static AccountReferenceItem? GetSelectedAccountFromControl(Control control)
        {
            return control switch
            {
                UserControl userControl => AccountPickerControlFactory.GetSelectedAccount(userControl),
                ComboBox { SelectedItem: AccountReferenceItem account } => account,
                _ => null
            };
        }

        private void SetAnalyticFieldVisibility(string fieldName, bool isVisible)
        {
            if (!_fieldPanels.TryGetValue(fieldName, out var panel))
                return;

            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && _fieldControls.TryGetValue(fieldName, out var control))
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
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var itemData = new Dictionary<string, object>();

                foreach (var field in _document.Fields.OrderBy(GetPostingFieldDisplayOrder).ThenBy(f => f.Order))
                {
                    if (IsSystemField(field) || IsModuleField(field))
                        continue;

                    if (!_fieldControls.TryGetValue(field.Name, out var control))
                        continue;

                    if ((field.Name == "Дебет" || field.Name == "Кредит") &&
                        !await TryResolveManualAccountAsync(field.Name, control))
                    {
                        return;
                    }

                    object? value = null;

                    switch (control)
                    {
                        case UserControl accountPicker when accountPicker.Tag is AccountReferenceItem account:
                            value = account.Code;
                            break;
                        case ComboBox comboBox when comboBox.SelectedItem is AccountReferenceItem account:
                            value = account.Code;
                            break;
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

                // Добавляем системные значения для ручных проводок.
                if (!itemData.ContainsKey("Тип документа"))
                {
                    itemData["Тип документа"] = "Ручная проводка";
                }

                if (_document.Fields.Any(IsModuleField) && !string.IsNullOrWhiteSpace(_assignedModuleName))
                    itemData["Модуль"] = _assignedModuleName;

                if (!_editId.HasValue && _document.Fields.Any(IsActiveField))
                    itemData["Активен"] = true;

                var debitAccount = itemData.GetValueOrDefault("Дебет")?.ToString();
                var creditAccount = itemData.GetValueOrDefault("Кредит")?.ToString();
                var amount = ReadPostingAmount(itemData);

                if (string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount))
                    throw new Exception("Укажите счета дебета и кредита");
                if (debitAccount.Equals(creditAccount, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Счета дебета и кредита должны отличаться");
                if (amount <= 0)
                    throw new Exception("Сумма проводки должна быть больше нуля");

                if (_editId.HasValue)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                }
                else
                {
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);
                }

                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
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

        private static bool IsSystemField(MetadataField field) =>
            field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt";

        private static bool IsModuleField(MetadataField field) =>
            field.Name.Equals("Модуль", StringComparison.OrdinalIgnoreCase) ||
            field.DbColumnName.Equals("module_code", StringComparison.OrdinalIgnoreCase);

        private static bool IsActiveField(MetadataField field) =>
            field.Name.Equals("Активен", StringComparison.OrdinalIgnoreCase) ||
            field.DbColumnName.Equals("is_active", StringComparison.OrdinalIgnoreCase);

        private static decimal ReadPostingAmount(Dictionary<string, object> itemData)
        {
            foreach (var key in new[] { "Сумма в сом", "Сумма" })
            {
                if (itemData.TryGetValue(key, out var value))
                    return ToDecimal(value);
            }

            return 0m;
        }

        private static decimal ToDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;
            if (value is decimal decimalValue)
                return decimalValue;
            if (value is IConvertible convertible)
            {
                try
                {
                    return convertible.ToDecimal(CultureInfo.CurrentCulture);
                }
                catch
                {
                    // Ниже пробуем строковый разбор с обеими распространенными культурами.
                }
            }

            var text = value.ToString();
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentValue))
                return currentValue;
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
                return invariantValue;
            if (decimal.TryParse(text?.Replace(",", "."), NumberStyles.Number, CultureInfo.InvariantCulture, out var normalizedValue))
                return normalizedValue;

            return 0m;
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
                    "Decimal" => ToDecimal(text),
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
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        private static int GetPostingFieldDisplayOrder(MetadataField field)
        {
            return field.Name switch
            {
                "Номер документа" => 1,
                "Тип документа" => 2,
                "Дата" => 3,
                "Касса" => 4,
                "Дебет" => 5,
                "Кредит" => 6,
                "Сумма в сом" => 7,
                "Сумма" => 7,
                _ => 100 + field.Order
            };
        }

        private static string? GetRowString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }
    }
}

