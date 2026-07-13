using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class PaymentOrderDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedOurAccountId;
        private Guid _selectedCorrAccountId;
        private Guid _selectedPaymentClassificationId;
        private List<ReferenceItem> _organizations;
        private List<ReferenceItem> _banks;
        private List<ReferenceItem> _ourAccounts;
        private List<ReferenceItem> _currencies;
        private List<ReferenceItem> _employees;
        private List<ReferenceItem> _materials;
        private List<Dictionary<string, object>> _paymentClassificationRows = new();
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private bool _isApplyingCurrencyRate = false;

        public PaymentOrderDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            DialogTitle.Text = $"Добавление: {document.Name}";
            DatePicker.SelectedDate = DateTime.Today;
            TypeCombo.SelectedIndex = 0;

            this.ContentRendered += async (s, e) => await InitializeAsync();
        }

        public PaymentOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
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
            

                // Загружаем справочники в фоновом потоке
                var loadTask = Task.Run(async () => await LoadReferenceDataAsync());
                await loadTask;

                // Загружаем данные документа (если редактирование)
                if (editId.HasValue)
                {
                    await LoadDataAsync(editId.Value);
                }
                else
                {
                    // Генерируем номер в фоновом потоке
                    var number = await Task.Run(async () =>
                    {
                        try
                        {
                            return await _metadataService.GetNextDocumentNumberAsync(_document.Name);
                        }
                        catch
                        {
                            return MetadataService.GenerateFallbackDocumentNumber();
                        }
                    });
                    NumberBox.Text = number;
                }

                UpdateAccountControlledFieldsVisibility();
                _isDataLoaded = true;
               
            }
            catch (Exception ex)
            {
               
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
                _isLoading = false;
            }
        }

        private async Task LoadReferenceDataAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

            // Загружаем организации
            var orgCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
            if (orgCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgCatalog.Id);
                _organizations = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d.ContainsKey("Наименование") ? d["Наименование"].ToString() : d["name"].ToString()
                }).ToList();

                // Обновляем UI в UI потоке
                Dispatcher.Invoke(() => OrganizationCombo.ItemsSource = _organizations);
            }

            // Загружаем банки
            var bankCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Банки");
            if (bankCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(bankCatalog.Id);
                _banks = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d.ContainsKey("Наименование банка") ? d["Наименование банка"].ToString() : d["name"].ToString()
                }).ToList();
                Dispatcher.Invoke(() => BankCombo.ItemsSource = _banks);
            }

            // Загружаем наши расчетные счета
            var accountCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Расчетные счета организаций");
            if (accountCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(accountCatalog.Id);
                _ourAccounts = new List<ReferenceItem>();

                var bankDict = _banks?.ToDictionary(b => b.Id, b => b.DisplayName) ?? new Dictionary<Guid, string>();

                foreach (var d in data)
                {
                    var id = Guid.Parse(d["Id"].ToString());

                    string accountNumber = "";
                    if (d.ContainsKey("Счет"))
                        accountNumber = d["Счет"].ToString();
                    else if (d.ContainsKey("account_number"))
                        accountNumber = d["account_number"].ToString();

                    string bankName = "";
                    if (d.ContainsKey("Банк") && d["Банк"] != null && Guid.TryParse(d["Банк"].ToString(), out var bankId))
                    {
                        bankDict.TryGetValue(bankId, out bankName);
                    }

                    string displayName = string.IsNullOrEmpty(bankName) ? accountNumber : $"{accountNumber} - {bankName}";

                    _ourAccounts.Add(new ReferenceItem
                    {
                        Id = id,
                        DisplayName = displayName
                    });
                }
            }

            // Загружаем валюты
            var currencyCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Справочник валют");
            if (currencyCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(currencyCatalog.Id);
                _currencies = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = $"{d["Код"]} - {d["Наименование"]}"
                }).ToList();
                Dispatcher.Invoke(() => CurrencyCombo.ItemsSource = _currencies);
            }

            _employees = await LoadReferenceItemsAsync(allCatalogs, "Сотрудники (Списочный состав)", "Табельный номер", "ФИО");
            Dispatcher.Invoke(() => EmployeeCombo.ItemsSource = _employees);

            _materials = await LoadReferenceItemsAsync(allCatalogs, "Справочник материалов", "Код", "Наименование материала");
            Dispatcher.Invoke(() => MaterialCombo.ItemsSource = _materials);

            var paymentClassificationCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Классификация платежей");
            _paymentClassificationRows = paymentClassificationCatalog == null
                ? new List<Dictionary<string, object>>()
                : await _metadataService.GetCatalogDataAsync(paymentClassificationCatalog.Id);
        }

        private async Task LoadDataAsync(Guid id)
        {
            var data = await _metadataService.GetCatalogDataAsync(_document.Id);
            var record = data.FirstOrDefault(r => r["Id"].ToString() == id.ToString());

            if (record != null)
            {
                Dispatcher.Invoke(() =>
                {
                    var rawNumber = record.ContainsKey("Номер") ? record["Номер"]?.ToString() :
                                   (record.ContainsKey("doc_number") ? record["doc_number"]?.ToString() : "");
                    NumberBox.Text = MetadataService.NormalizeLegacyDocumentNumber(rawNumber);

                    if (record.ContainsKey("Дата") && record["Дата"] is DateTime dt) DatePicker.SelectedDate = dt;
                    if (record.ContainsKey("Сумма")) AmountBox.Text = record["Сумма"].ToString();
                    if (record.ContainsKey("Сумма в валюте")) AmountCurrencyBox.Text = record["Сумма в валюте"].ToString();
                    if (record.ContainsKey("Курс")) ExchangeRateBox.Text = record["Курс"].ToString();
                    if (record.ContainsKey("Назначение платежа")) PurposeBox.Text = record["Назначение платежа"].ToString();
                    if (record.ContainsKey("Примечание")) DescriptionBox.Text = record["Примечание"].ToString();
                    if (record.TryGetValue("Наш счет", out var ourAccountValue))
                        ApplySelectedOurAccount(ourAccountValue);
                    if (record.TryGetValue("Корр. счет", out var accountValue))
                        ApplySelectedCorrAccount(accountValue);

                    SelectComboByRecordValue(OrganizationCombo, record, "Организация");
                    SelectComboByRecordValue(CurrencyCombo, record, "Валюта");
                    SelectComboByRecordValue(EmployeeCombo, record, "Сотрудник");
                    SelectComboByRecordValue(MaterialCombo, record, "Материал");
                    if (record.TryGetValue("Классификация платежа", out var paymentClassificationValue))
                        ApplySelectedPaymentClassification(paymentClassificationValue);

                    if (record.ContainsKey("Тип"))
                    {
                        var type = record["Тип"].ToString();
                        TypeCombo.SelectedIndex = type.Contains("Входящее", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    }
                });
            }
        }

        private async void SelectOurAccount_Click(object sender, RoutedEventArgs e)
        {
            await SelectPlanAccountAsync((accountId, displayName) =>
            {
                _selectedOurAccountId = accountId;
                OurAccountBox.Text = displayName;
                UpdateAccountControlledFieldsVisibility();
            });
        }

        private async void SelectCorrAccount_Click(object sender, RoutedEventArgs e)
        {
            await SelectPlanAccountAsync((accountId, displayName) =>
            {
                _selectedCorrAccountId = accountId;
                CorrAccountBox.Text = displayName;
                UpdateAccountControlledFieldsVisibility();
            });
        }

        private void SelectPaymentClassification_Click(object sender, RoutedEventArgs e)
        {
            if (_paymentClassificationRows.Count == 0)
            {
                MessageBox.Show(
                    "Справочник классификации платежей пуст. Загрузите данные из DBF в справочнике 'Классификация платежей'.",
                    "Классификация платежей",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new ReferenceSelectionDialog(_paymentClassificationRows, "Код", "Наименование")
            {
                Owner = this,
                Title = "Выбор классификации платежа"
            };

            if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                ApplySelectedPaymentClassification(dialog.SelectedItem.GetValueOrDefault("Id"));
        }

        private async Task SelectPlanAccountAsync(Action<Guid, string> applySelection)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);
                if (accountsData.Count == 0)
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
                    if (Guid.TryParse(dialog.SelectedAccount["Id"].ToString(), out var accountId))
                        applySelection(accountId, $"{accountCode} - {accountName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void OnRateInputChanged(object sender, EventArgs e)
        {
            if (_isLoading || _isApplyingCurrencyRate)
                return;

            await LoadExchangeRateFromCatalogAsync();
        }

        private void OnCurrencyAmountChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingCurrencyRate)
                return;

            RecalculateAmountFromCurrency();
        }

        private async Task LoadExchangeRateFromCatalogAsync()
        {
            if (ExchangeRateBox == null)
                return;

            if (CurrencyCombo.SelectedItem is not ReferenceItem currency || DatePicker.SelectedDate is not DateTime documentDate)
                return;

            var rate = await _metadataService.GetCurrencyRateForDateAsync(currency.Id, documentDate);
            if (rate == null)
                return;

            try
            {
                _isApplyingCurrencyRate = true;
                ExchangeRateBox.Text = rate.Rate.ToString("0.####", CultureInfo.CurrentCulture);
            }
            finally
            {
                _isApplyingCurrencyRate = false;
            }

            RecalculateAmountFromCurrency();
        }

        private void RecalculateAmountFromCurrency()
        {
            if (AmountCurrencyBox == null || ExchangeRateBox == null || AmountBox == null)
                return;

            if (!TryReadDecimal(AmountCurrencyBox.Text, out var amountCurrency) ||
                !TryReadDecimal(ExchangeRateBox.Text, out var exchangeRate) ||
                amountCurrency <= 0 ||
                exchangeRate <= 0)
            {
                return;
            }

            var amount = Math.Round(amountCurrency * exchangeRate, 2, MidpointRounding.AwayFromZero);
            AmountBox.Text = amount.ToString("0.##", CultureInfo.CurrentCulture);
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

                // Определяем тип платежа
                string orderType = ((ComboBoxItem)TypeCombo.SelectedItem)?.Content?.ToString() ?? "Исходящее";
                bool isOutgoing = orderType.Contains("Исходящее");

                // Формируем правильный document_type
                string documentType = isOutgoing ? "Исходящее платежное поручение" : "Входящее платежное поручение";

                if (_selectedOurAccountId == Guid.Empty)
                {
                    MessageBox.Show("Укажите наш счет для формирования проводки.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_selectedCorrAccountId == Guid.Empty)
                {
                    MessageBox.Show("Укажите корреспондирующий счет для формирования проводки.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Тип"] = documentType,  
                    ["Сумма"] = TryReadDecimal(AmountBox.Text, out var amount) ? amount : 0,
                    ["Назначение платежа"] = PurposeBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false
                };

                SetFieldValueIfExists(itemData, "Организация",
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem org
                        ? org.Id
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Контрагент", string.Empty);
                SetFieldValueIfExists(itemData, "Банк", string.Empty);
                SetFieldValueIfExists(itemData, "Наш счет", _selectedOurAccountId);
                SetFieldValueIfExists(itemData, "Расчетный счет контрагента", string.Empty);
                SetFieldValueIfExists(itemData, "Счет контрагента", string.Empty);
                SetFieldValueIfExists(itemData, "Валюта",
                    CurrencyCombo.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Сумма в валюте",
                    TryReadDecimal(AmountCurrencyBox.Text, out var amountCurrency) ? amountCurrency : 0);
                SetFieldValueIfExists(itemData, "Курс",
                    TryReadDecimal(ExchangeRateBox.Text, out var exchangeRate) ? exchangeRate : 0);
                SetFieldValueIfExists(itemData, "Сотрудник",
                    EmployeePanel.Visibility == Visibility.Visible && EmployeeCombo.SelectedItem is ReferenceItem employee
                        ? employee.Id
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Материал",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Корр. счет",
                    _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId : string.Empty);
                SetFieldValueIfExists(itemData, "Классификация платежа",
                    _selectedPaymentClassificationId != Guid.Empty ? _selectedPaymentClassificationId : string.Empty);

                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ApplySelectedOurAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedOurAccountId = account.Id;
            OurAccountBox.Text = account.DisplayName;
        }

        private void ApplySelectedCorrAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedCorrAccountId = account.Id;
            CorrAccountBox.Text = account.DisplayName;
        }

        private void ApplySelectedPaymentClassification(object value)
        {
            if (!Guid.TryParse(value?.ToString(), out var id))
                return;

            var row = _paymentClassificationRows.FirstOrDefault(item =>
                item.TryGetValue("Id", out var rowId) &&
                Guid.TryParse(rowId?.ToString(), out var parsedId) &&
                parsedId == id);
            if (row == null)
                return;

            _selectedPaymentClassificationId = id;
            PaymentClassificationBox.Text = BuildDisplayName(row, "Код", "Наименование");
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var ourAccountSettings = _selectedOurAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedOurAccountId);
            var corrAccountSettings = _selectedCorrAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedCorrAccountId);
            var accountSettings = new[] { ourAccountSettings, corrAccountSettings };

            var showCurrency = AccountAnalyticsRules.ShouldShowField(
                "Валюта",
                accountSettings,
                _accountAnalytics.Definitions,
                "Справочник валют",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);

            SetAccountControlledFieldVisibility(
                OrganizationLabel,
                OrganizationCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Организация",
                    accountSettings,
                    _accountAnalytics.Definitions,
                    "Организации",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledFieldVisibility(
                CurrencyLabel,
                CurrencyCombo,
                showCurrency);
            SetAccountControlledTextFieldVisibility(AmountCurrencyLabel, AmountCurrencyBox, showCurrency);
            SetAccountControlledTextFieldVisibility(ExchangeRateLabel, ExchangeRateBox, showCurrency);

            SetAccountControlledPanelVisibility(
                EmployeePanel,
                EmployeeCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Сотрудник",
                    accountSettings,
                    _accountAnalytics.Definitions,
                    "Сотрудники (Списочный состав)",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledPanelVisibility(
                MaterialPanel,
                MaterialCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Материал",
                    accountSettings,
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

        private static bool TryReadDecimal(string? text, out decimal value)
        {
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
                return true;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
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

        private static void SetAccountControlledTextFieldVisibility(
            FrameworkElement label,
            TextBox textBox,
            bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            textBox.Visibility = visibility;

            if (!isVisible)
                textBox.Text = "0";
        }

        private static void SetAccountControlledPanelVisibility(
            FrameworkElement panel,
            ComboBox comboBox,
            bool isVisible)
        {
            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible)
                comboBox.SelectedItem = null;
        }
    }
}
