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
    public partial class CashOrderDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private string _selectedCorrAccountCode = string.Empty;
        private Guid _selectedCashDeskId;
        private string _selectedCashDeskCode = string.Empty;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private List<CashDeskItem> _cashDesks = new();

        // Для сотрудника
        private Guid _selectedEmployeeId = Guid.Empty;
        private string _selectedEmployeeName = string.Empty;

        public CashOrderDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            DialogTitle.Text = $"Добавление: {document.Name}";
            DatePicker.SelectedDate = DateTime.Today;

            this.ContentRendered += async (s, e) => await InitializeAsync();
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = $"Редактирование: {document.Name}";

            this.ContentRendered += async (s, e) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isDataLoaded || _isLoading) return;
            _isLoading = true;

            try
            {
                this.Cursor = Cursors.Wait;

                var data = await Task.Run(async () => await LoadAllDataAsync());

                await Dispatcher.InvokeAsync(() =>
                {
                    // Заполняем ComboBox (кроме сотрудника)
                    if (data.CashDesks != null)
                        CashDeskCombo.ItemsSource = data.CashDesks;
                    if (data.Organizations != null)
                        OrganizationCombo.ItemsSource = data.Organizations;
                    if (data.Currencies != null)
                        CurrencyCombo.ItemsSource = data.Currencies;
                    if (data.Materials != null)
                        MaterialCombo.ItemsSource = data.Materials;

                    _accountAnalytics = data.AccountAnalytics;

                    // Генерируем номер
                    if (!editId.HasValue)
                    {
                        NumberBox.Text = data.DocumentNumber;

                        if (data.CashDesks != null && data.CashDesks.Any())
                        {
                            CashDeskCombo.SelectedItem = data.CashDesks.First();
                            _selectedCashDeskId = data.CashDesks.First().Id;
                            _selectedCashDeskCode = data.CashDesks.First().AccountCode;
                            CashDeskAccountBox.Text = _selectedCashDeskCode;
                        }
                    }
                    else if (data.Record != null)
                    {
                        // Заполняем данные для редактирования
                        var rawNumber = data.Record.ContainsKey("Номер") ? data.Record["Номер"]?.ToString() :
                                       (data.Record.ContainsKey("doc_number") ? data.Record["doc_number"]?.ToString() : "");
                        NumberBox.Text = MetadataService.NormalizeLegacyDocumentNumber(rawNumber);

                        if (data.Record.ContainsKey("Дата") && data.Record["Дата"] is DateTime dt)
                            DatePicker.SelectedDate = dt;
                        if (data.Record.ContainsKey("Сумма"))
                            AmountBox.Text = data.Record["Сумма"].ToString();
                        if (data.Record.ContainsKey("Основание"))
                            BasisBox.Text = data.Record["Основание"].ToString();
                        if (data.Record.ContainsKey("Примечание"))
                            DescriptionBox.Text = data.Record["Примечание"].ToString();

                        // Загружаем кассу
                        if (data.Record.TryGetValue("Касса", out var cashValue))
                            SelectComboByRecordValue(CashDeskCombo, data.Record, "Касса");

                        // Загружаем корреспондирующий счет
                        if (data.Record.TryGetValue("Корр. счет", out var accountValue))
                            ApplySelectedCorrAccount(accountValue);

                        // Загружаем организацию
                        SelectComboByRecordValue(OrganizationCombo, data.Record, "Организация");

                        // Загружаем валюту
                        SelectComboByRecordValue(CurrencyCombo, data.Record, "Валюта");

                        // Загружаем сотрудника (через отдельный метод)
                        if (data.Record.TryGetValue("Сотрудник", out var employeeValue) && Guid.TryParse(employeeValue?.ToString(), out var empId))
                        {
                            _selectedEmployeeId = empId;
                            var emp = data.Employees?.FirstOrDefault(e => e.Id == empId);
                            EmployeeNameBox.Text = emp != null ? emp.DisplayName : employeeValue.ToString();
                            _selectedEmployeeName = emp?.DisplayName ?? employeeValue.ToString();
                        }

                        // Загружаем материал
                        SelectComboByRecordValue(MaterialCombo, data.Record, "Материал");
                    }

                    UpdateAccountControlledFieldsVisibility();
                    AmountBox.Focus();
                    AmountBox.SelectAll();
                });

                _isDataLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
                _isLoading = false;
            }
        }

        private async Task<DialogData> LoadAllDataAsync()
        {
            var result = new DialogData();
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

            // Кассы
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "Кассы");
            if (cashDesks != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(cashDesks.Id);
                result.CashDesks = data
                    .Where(d => d.TryGetValue("Id", out var id) && Guid.TryParse(id?.ToString(), out _))
                    .Select(d =>
                    {
                        var accountCode = ResolveCashDeskAccountCode(
                            GetRowString(d, "Счет", "Счет кассы", "Код", "code"),
                            result.AccountAnalytics);

                        return new CashDeskItem
                        {
                            Id = Guid.Parse(d["Id"].ToString()),
                            DisplayName = GetRowString(
                                d,
                                "Наименование кассы",
                                "Наименование",
                                "name",
                                "Код") ?? "Касса",
                            AccountCode = accountCode,
                            CashNumber = GetRowString(d, "Номер кассы", "cash_number") ?? string.Empty,
                            CurrencyName = GetRowString(d, "Валюта", "currency_id") ?? string.Empty
                        };
                    })
                    .ToList();
            }

            // Организации
            var orgs = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
            if (orgs != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgs.Id);
                result.Organizations = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                }).ToList();
            }

            // Валюты
            result.Currencies = await LoadReferenceItemsAsync(allCatalogs, "Справочник валют", "Код", "Наименование");
            // Сотрудники (для диалога выбора)
            result.Employees = await LoadReferenceItemsAsync(allCatalogs, "Сотрудники (Списочный состав)", "Табельный номер", "ФИО");
            // Материалы
            result.Materials = await LoadReferenceItemsAsync(allCatalogs, "Справочник материалов", "Код", "Наименование материала");

            // Генерируем номер
            try
            {
                result.DocumentNumber = await _metadataService.GetNextDocumentNumberAsync(_document.Name);
            }
            catch
            {
                result.DocumentNumber = MetadataService.GenerateFallbackDocumentNumber();
            }

            // Если редактирование, загружаем запись
            if (_editId.HasValue)
            {
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                result.Record = data.FirstOrDefault(r => r["Id"].ToString() == _editId.Value.ToString());
            }

            return result;
        }

        private class DialogData
        {
            public List<CashDeskItem> CashDesks { get; set; } = new();
            public List<ReferenceItem> Organizations { get; set; } = new();
            public List<ReferenceItem> Currencies { get; set; } = new();
            public List<ReferenceItem> Employees { get; set; } = new();
            public List<ReferenceItem> Materials { get; set; } = new();
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
            public string DocumentNumber { get; set; } = string.Empty;
            public Dictionary<string, object>? Record { get; set; }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("Для этого модуля нет доступных счетов в плане счетов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new AccountSelectionDialog(accountsData);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    var accountCode = dialog.SelectedAccount.ContainsKey("Код") ? dialog.SelectedAccount["Код"].ToString() : "";
                    var accountName = dialog.SelectedAccount.ContainsKey("Наименование") ? dialog.SelectedAccount["Наименование"].ToString() : "";

                    CorrAccountBox.Text = $"{accountCode} - {accountName}";

                    if (dialog.SelectedAccount.ContainsKey("Id"))
                    {
                        _selectedCorrAccountId = Guid.Parse(dialog.SelectedAccount["Id"].ToString());
                    }

                    _selectedCorrAccountCode = accountCode;

                    UpdateAccountControlledFieldsVisibility();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе счета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text);
                if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(c => !char.IsDigit(c)))
                {
                    MessageBox.Show("Номер документа должен содержать только цифры.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    NumberBox.Focus();
                    return;
                }

                NumberBox.Text = documentNumber;

                var amount = decimal.TryParse(AmountBox.Text, out var parsedAmount) ? parsedAmount : 0;

                // ПРОВЕРКА ЗАПОЛНЕНИЯ ВСЕХ АКТИВНЫХ ПОЛЕЙ
                if (OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите организацию!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    OrganizationCombo.Focus();
                    return;
                }

                if (EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId == Guid.Empty)
                {
                    MessageBox.Show("Выберите сотрудника!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите валюту!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CurrencyCombo.Focus();
                    return;
                }

                if (MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите материал!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    MaterialCombo.Focus();
                    return;
                }

                // Получаем кассу
                string cashDeskId = string.Empty;
                string cashDeskCode = _selectedCashDeskCode;

                if (CashDeskCombo.SelectedItem is CashDeskItem cashDesk)
                {
                    cashDeskId = cashDesk.Id.ToString();
                    cashDeskCode = cashDesk.AccountCode;
                    _selectedCashDeskId = cashDesk.Id;
                    _selectedCashDeskCode = cashDesk.AccountCode;
                }
                else
                {
                    MessageBox.Show("Выберите кассу.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(cashDeskCode))
                {
                    MessageBox.Show("У выбранной кассы не указан счет. Откройте справочник касс и заполните поле \"Счет\".", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                // Получаем корреспондирующий счет
                string corrAccountId = _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId.ToString() : string.Empty;
                string corrAccountCode = _selectedCorrAccountCode;

                if (string.IsNullOrEmpty(corrAccountCode))
                {
                    MessageBox.Show("Выберите корреспондирующий счет (кнопка '?').", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Определяем дебет и кредит
                string debitAccount, creditAccount;
                if (_document.Name == "Приходный кассовый ордер")
                {
                    debitAccount = cashDeskCode;
                    creditAccount = corrAccountCode;
                }
                else if (_document.Name == "Расходный кассовый ордер")
                {
                    debitAccount = corrAccountCode;
                    creditAccount = cashDeskCode;
                }
                else
                {
                    debitAccount = cashDeskCode;
                    creditAccount = corrAccountCode;
                }

                if (debitAccount == creditAccount)
                {
                    MessageBox.Show("Дебет и кредит не могут быть одинаковыми!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Валюта
                bool isCurrencyEnabled = IsCurrencyEnabledForAccount(corrAccountCode);
                decimal amountInCurrency = isCurrencyEnabled ? amount : 0;

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Сумма"] = amount,
                    ["Основание"] = BasisBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false,
                    ["Касса"] = cashDeskId,
                    ["Корр. счет"] = corrAccountId,
                    ["Дебет"] = debitAccount,
                    ["Кредит"] = creditAccount,
                    ["Сумма в валюте"] = amountInCurrency
                };

                // Заполняем остальные поля (только если они видимы, иначе не сохраняем)
                SetFieldValueIfExists(itemData, "Организация",
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem org
                        ? org.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Валюта",
                    CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Сотрудник",
                    EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId != Guid.Empty
                        ? _selectedEmployeeId.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Материал",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Контрагент", string.Empty);

                // Сохраняем документ
                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private bool IsCurrencyEnabledForAccount(string accountCode)
        {
            if (string.IsNullOrEmpty(accountCode))
                return false;

            try
            {
                var settings = _accountAnalytics.GetSettingsByCode(accountCode);
                if (settings == null)
                    return false;

                var currencyDefinition = _accountAnalytics.Definitions
                    .FirstOrDefault(d => d.Code == "currencies");
                if (currencyDefinition == null)
                    return false;

                return settings.Allows(currencyDefinition);
            }
            catch
            {
                return false;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AllowNumberEditCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var canEdit = AllowNumberEditCheckBox.IsChecked == true;
            NumberBox.IsReadOnly = !canEdit;
            NumberBox.Background = canEdit ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightGray;
            if (canEdit)
            {
                NumberBox.Focus();
                NumberBox.SelectAll();
            }
        }

        private void ApplySelectedCorrAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedCorrAccountId = account.Id;
            _selectedCorrAccountCode = account.Code;
            CorrAccountBox.Text = account.DisplayName;
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var settings = _selectedCorrAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedCorrAccountId);

            SetAccountControlledFieldVisibility(
                OrganizationLabel,
                OrganizationCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Организация",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Организации",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledPanelVisibility(
                CurrencyPanel,
                CurrencyCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Валюта",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Справочник валют",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            // Для сотрудника – просто показываем/скрываем панель, ComboBox нет
            EmployeePanel.Visibility = AccountAnalyticsRules.ShouldShowField(
                "Сотрудник",
                new[] { settings },
                _accountAnalytics.Definitions,
                "Сотрудники (Списочный состав)",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false)
                ? Visibility.Visible : Visibility.Collapsed;

            SetAccountControlledPanelVisibility(
                MaterialPanel,
                MaterialCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Материал",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Справочник материалов",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
        }

        private void SetFieldValueIfExists(Dictionary<string, object> itemData, string fieldName, object value)
        {
            if (_document.Fields.Any(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                itemData[fieldName] = value;
        }

        private async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            List<MetadataObject> catalogs,
            string catalogName,
            string firstDisplayField,
            string secondDisplayField)
        {
            var catalog = catalogs.FirstOrDefault(c => c.Name == catalogName);
            if (catalog == null)
                return new List<ReferenceItem>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                .Select(row => new ReferenceItem
                {
                    Id = Guid.Parse(row["Id"].ToString()),
                    DisplayName = BuildDisplayName(row, firstDisplayField, secondDisplayField)
                })
                .ToList();
        }

        private static string BuildDisplayName(Dictionary<string, object> row, string firstField, string secondField)
        {
            var first = row.GetValueOrDefault(firstField)?.ToString() ??
                        row.GetValueOrDefault(firstField.Replace(" ", "_"))?.ToString() ??
                        string.Empty;
            var second = row.GetValueOrDefault(secondField)?.ToString() ??
                         row.GetValueOrDefault(secondField.Replace(" ", "_"))?.ToString() ??
                         string.Empty;

            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return $"{first} - {second}";

            return first.Length > 0
                ? first
                : second.Length > 0
                    ? second
                    : row.GetValueOrDefault("Наименование")?.ToString() ??
                      row.GetValueOrDefault("name")?.ToString() ??
                      row.GetValueOrDefault("Id")?.ToString() ??
                      string.Empty;
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

        public static string ResolveCashDeskAccountCode(
            string? accountCode,
            AccountAnalyticsRegistry accountAnalytics)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return string.Empty;

            var account = accountAnalytics.FindAccount(accountCode);
            return account?.Code ?? NormalizeAccountCodeText(accountCode);
        }

        private static string NormalizeAccountCodeText(string accountCode)
        {
            var text = accountCode.Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        private static void SelectComboByRecordValue(ComboBox comboBox, Dictionary<string, object> record, string fieldName)
        {
            if (!record.TryGetValue(fieldName, out var value) || !Guid.TryParse(value?.ToString(), out var id))
                return;

            comboBox.SelectedItem = comboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == id);
        }

        private static void SetAccountControlledFieldVisibility(
            FrameworkElement label,
            ComboBox comboBox,
            bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            comboBox.Visibility = visibility;

            if (!isVisible)
                comboBox.SelectedItem = null;
        }

        private static void SetAccountControlledPanelVisibility(
            FrameworkElement panel,
            ComboBox comboBox,
            bool isVisible)
        {
            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && comboBox != null)
                comboBox.SelectedItem = null;
        }

        private async void SelectEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");

                if (employeeCatalog == null)
                {
                    MessageBox.Show("Справочник сотрудников не найден!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);

                if (employeesData == null || employeesData.Count == 0)
                {
                    MessageBox.Show("В справочнике сотрудников нет данных!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(employeesData, "Табельный номер", "ФИО");
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                {
                    var employee = dialog.SelectedItem;
                    var displayName = $"{employee.GetValueOrDefault("Табельный номер")} - {employee.GetValueOrDefault("ФИО")}";

                    EmployeeNameBox.Text = displayName;
                    _selectedEmployeeId = Guid.Parse(employee["Id"].ToString());
                    _selectedEmployeeName = employee.GetValueOrDefault("ФИО")?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе сотрудника: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private void CashDeskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CashDeskCombo.SelectedItem is CashDeskItem selected)
            {
                _selectedCashDeskId = selected.Id;
                _selectedCashDeskCode = selected.AccountCode;
                CashDeskAccountBox.Text = _selectedCashDeskCode;
            }
        }
    }

    public class CashDeskItem : ReferenceItem
    {
        public string AccountCode { get; set; } = string.Empty;
        public string CashNumber { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string DisplayNameWithAccount =>
            string.IsNullOrWhiteSpace(AccountCode)
                ? DisplayName
                : $"{DisplayName} (счет {AccountCode})";
    }
}
