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
    public partial class PaymentOrderDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private List<ReferenceItem> _organizations;
        private List<ReferenceItem> _banks;
        private List<ReferenceItem> _ourAccounts;
        private List<ReferenceItem> _currencies;
        private List<ReferenceItem> _employees;
        private List<ReferenceItem> _materials;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;

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
                Dispatcher.Invoke(() => OurAccountCombo.ItemsSource = _ourAccounts);
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
                    if (record.ContainsKey("Назначение платежа")) PurposeBox.Text = record["Назначение платежа"].ToString();
                    if (record.ContainsKey("Примечание")) DescriptionBox.Text = record["Примечание"].ToString();
                    if (record.TryGetValue("Корр. счет", out var accountValue))
                        ApplySelectedCorrAccount(accountValue);

                    SelectComboByRecordValue(OrganizationCombo, record, "Организация");
                    SelectComboByRecordValue(CurrencyCombo, record, "Валюта");
                    SelectComboByRecordValue(EmployeeCombo, record, "Сотрудник");
                    SelectComboByRecordValue(MaterialCombo, record, "Материал");

                    if (record.ContainsKey("Тип"))
                    {
                        var type = record["Тип"].ToString();
                        for (int i = 0; i < TypeCombo.Items.Count; i++)
                        {
                            if (TypeCombo.Items[i] is ComboBoxItem item && item.Content.ToString() == type)
                            {
                                TypeCombo.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                });
            }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
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
                    CorrAccountBox.Text = $"{accountCode} - {accountName}";
                    _selectedCorrAccountId = Guid.Parse(dialog.SelectedAccount["Id"].ToString());
                    UpdateAccountControlledFieldsVisibility();
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

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Тип"] = documentType,  
                    ["Сумма"] = decimal.TryParse(AmountBox.Text, out var amount) ? amount : 0,
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
                SetFieldValueIfExists(itemData, "Наш счет", string.Empty);
                SetFieldValueIfExists(itemData, "Расчетный счет контрагента", string.Empty);
                SetFieldValueIfExists(itemData, "Счет контрагента", string.Empty);
                SetFieldValueIfExists(itemData, "Валюта",
                    CurrencyCombo.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id
                        : string.Empty);
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

        private void ApplySelectedCorrAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedCorrAccountId = account.Id;
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

            SetAccountControlledFieldVisibility(
                CurrencyLabel,
                CurrencyCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Валюта",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Справочник валют",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledPanelVisibility(
                EmployeePanel,
                EmployeeCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Сотрудник",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Сотрудники (Списочный состав)",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

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

            if (!isVisible)
                comboBox.SelectedItem = null;
        }
    }
}
