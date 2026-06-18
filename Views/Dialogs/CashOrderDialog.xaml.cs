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
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;

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
                System.Diagnostics.Debug.WriteLine("=== InitializeAsync START ===");
                this.Cursor = Cursors.Wait;             

                // Загружаем данные в фоновом потоке
                var data = await Task.Run(async () => await LoadAllDataAsync());

                System.Diagnostics.Debug.WriteLine("2. LoadReferenceDataAsync завершён");

                // Обновляем UI в основном потоке
                await Dispatcher.InvokeAsync(() =>
                {
                    System.Diagnostics.Debug.WriteLine("3. Обновление UI...");

                    // Заполняем ComboBox
                    if (data.CashDesks != null)
                        CashDeskCombo.ItemsSource = data.CashDesks;
                    if (data.Organizations != null)
                        OrganizationCombo.ItemsSource = data.Organizations;
                    if (data.Currencies != null)
                        CurrencyCombo.ItemsSource = data.Currencies;
                    if (data.Employees != null)
                        EmployeeCombo.ItemsSource = data.Employees;
                    if (data.Materials != null)
                        MaterialCombo.ItemsSource = data.Materials;
                    _accountAnalytics = data.AccountAnalytics;

                    // Генерируем номер
                    if (!editId.HasValue)
                    {
                        NumberBox.Text = data.DocumentNumber;
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

                        if (data.Record.TryGetValue("Корр. счет", out var accountValue))
                            ApplySelectedCorrAccount(accountValue);

                        SelectComboByRecordValue(OrganizationCombo, data.Record, "Организация");
                        SelectComboByRecordValue(CurrencyCombo, data.Record, "Валюта");
                        SelectComboByRecordValue(EmployeeCombo, data.Record, "Сотрудник");
                        SelectComboByRecordValue(MaterialCombo, data.Record, "Материал");
                    }

                    UpdateAccountControlledFieldsVisibility();
                    System.Diagnostics.Debug.WriteLine("4. UI обновлён");
                });

                _isDataLoaded = true;               
                System.Diagnostics.Debug.WriteLine("=== InitializeAsync COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== InitializeAsync ERROR: {ex.Message} ===");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");               
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
            System.Diagnostics.Debug.WriteLine("1. Начинаем LoadReferenceDataAsync...");

            var result = new DialogData();
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

            // Загружаем кассы
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "Кассы");
            if (cashDesks != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(cashDesks.Id);
                result.CashDesks = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                }).ToList();
            }

            // Загружаем организации
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

            result.Currencies = await LoadReferenceItemsAsync(allCatalogs, "Справочник валют", "Код", "Наименование");
            result.Employees = await LoadReferenceItemsAsync(allCatalogs, "Сотрудники (Списочный состав)", "Табельный номер", "ФИО");
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

            System.Diagnostics.Debug.WriteLine("2. LoadReferenceDataAsync завершён");
            return result;
        }

        private class DialogData
        {
            public List<ReferenceItem> CashDesks { get; set; }
            public List<ReferenceItem> Organizations { get; set; }
            public List<ReferenceItem> Currencies { get; set; }
            public List<ReferenceItem> Employees { get; set; }
            public List<ReferenceItem> Materials { get; set; }
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
            public string DocumentNumber { get; set; }
            public Dictionary<string, object> Record { get; set; }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var chartCatalog = allCatalogs.FirstOrDefault(c => c.Name.StartsWith("План счетов"));

                if (chartCatalog == null)
                {
                    MessageBox.Show("План счетов не найден!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("В плане счетов нет данных!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Сумма"] = decimal.TryParse(AmountBox.Text, out var amount) ? amount : 0,
                    ["Основание"] = BasisBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false
                };

                SetFieldValueIfExists(itemData, "Касса",
                    CashDeskCombo.SelectedItem is ReferenceItem cashDesk ? cashDesk.Id.ToString() : string.Empty);
                SetFieldValueIfExists(itemData, "Организация",
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem org
                        ? org.Id.ToString()
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Валюта",
                    CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id.ToString()
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Сотрудник",
                    EmployeePanel.Visibility == Visibility.Visible && EmployeeCombo.SelectedItem is ReferenceItem employee
                        ? employee.Id.ToString()
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Материал",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id.ToString()
                        : string.Empty);
                SetFieldValueIfExists(itemData, "Контрагент", string.Empty);
                SetFieldValueIfExists(itemData, "Корр. счет",
                    _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId.ToString() : string.Empty);

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
