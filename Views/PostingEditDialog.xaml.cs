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
        private readonly Dictionary<string, FrameworkElement> _fieldPanels = new();
        private AccountAnalyticsRegistry _accountAnalytics = new();
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
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
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
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

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
            _fieldPanels.Clear();
            _accountAnalytics = dialogData.AccountAnalytics;

            foreach (var field in _document.Fields.OrderBy(GetPostingFieldDisplayOrder).ThenBy(f => f.Order))
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
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
                _fieldPanels[field.Name] = panel;
            }

            UpdateAnalyticControlsVisibility();
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
                UpdateAnalyticControlsVisibility);
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
                    if (field.Name == "Id" || field.Name == "CreatedAt" || field.Name == "UpdatedAt")
                        continue;

                    if (!_fieldControls.TryGetValue(field.Name, out var control))
                        continue;

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

                // Добавляем тип документа для ручных проводок
                if (!itemData.ContainsKey("Тип документа"))
                {
                    itemData["Тип документа"] = "Ручная проводка";
                }

                var debitAccount = itemData.GetValueOrDefault("Дебет")?.ToString();
                var creditAccount = itemData.GetValueOrDefault("Кредит")?.ToString();
                var amount = itemData.TryGetValue("Сумма в сом", out var amountValue)
                    ? Convert.ToDecimal(amountValue)
                    : 0m;

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

        private static int GetPostingFieldDisplayOrder(MetadataField field)
        {
            return field.Name switch
            {
                "Номер документа" => 1,
                "Дата" => 2,
                "Касса" => 3,
                "Тип документа" => 4,
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
