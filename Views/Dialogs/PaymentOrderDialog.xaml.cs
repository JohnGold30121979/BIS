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
    public partial class PaymentOrderDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private List<ReferenceItem> _organizations;
        private List<ReferenceItem> _contractors;
        private List<ReferenceItem> _banks;
        private List<ReferenceItem> _ourAccounts;
        private List<ReferenceItem> _currencies;

        public PaymentOrderDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            DialogTitle.Text = $"Добавление: {document.Name}";
            NumberBox.Text = Guid.NewGuid().ToString().Substring(0, 8);
            DatePicker.SelectedDate = DateTime.Today;
            TypeCombo.SelectedIndex = 0;
            Loaded += async (s, e) => await LoadReferenceData();
        }

        public PaymentOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = $"Редактирование: {document.Name}";
            Loaded += async (s, e) => await LoadData(editId);
        }

        private async Task LoadReferenceData()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();

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
                OrganizationCombo.ItemsSource = _organizations;
            }

            // Загружаем контрагентов
            var contractorCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Контрагенты");
            if (contractorCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(contractorCatalog.Id);
                _contractors = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d.ContainsKey("Наименование") ? d["Наименование"].ToString() : d["name"].ToString()
                }).ToList();
                ContractorCombo.ItemsSource = _contractors;
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
                BankCombo.ItemsSource = _banks;
            }

            // Загружаем наши расчетные счета
            var accountCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Расчетные счета организаций");
            if (accountCatalog != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(accountCatalog.Id);
                _ourAccounts = new List<ReferenceItem>();

                foreach (var d in data)
                {
                    var id = Guid.Parse(d["Id"].ToString());

                    // Получаем номер счета
                    string accountNumber = "";
                    if (d.ContainsKey("Счет"))
                        accountNumber = d["Счет"].ToString();
                    else if (d.ContainsKey("account_number"))
                        accountNumber = d["account_number"].ToString();

                    // Получаем название банка (нужно подгрузить отдельно)
                    string bankName = "";
                    if (d.ContainsKey("Банк") && d["Банк"] != null)
                    {
                        var bankId = Guid.Parse(d["Банк"].ToString());
                        var bank = _banks?.FirstOrDefault(b => b.Id == bankId);
                        if (bank != null)
                            bankName = bank.DisplayName;
                    }

                    string displayName = string.IsNullOrEmpty(bankName) ? accountNumber : $"{accountNumber} - {bankName}";

                    _ourAccounts.Add(new ReferenceItem
                    {
                        Id = id,
                        DisplayName = displayName
                    });
                }
                OurAccountCombo.ItemsSource = _ourAccounts;
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
                CurrencyCombo.ItemsSource = _currencies;
            }
        }

        private async Task LoadData(Guid id)
        {
            await LoadReferenceData();
            var data = await _metadataService.GetCatalogDataAsync(_document.Id);
            var record = data.FirstOrDefault(r => r["Id"].ToString() == id.ToString());
            if (record != null)
            {
                NumberBox.Text = record.ContainsKey("Номер") ? record["Номер"].ToString() : "";
                if (record.ContainsKey("Дата") && record["Дата"] is DateTime dt) DatePicker.SelectedDate = dt;
                if (record.ContainsKey("Сумма")) AmountBox.Text = record["Сумма"].ToString();
                if (record.ContainsKey("Назначение платежа")) PurposeBox.Text = record["Назначение платежа"].ToString();
                if (record.ContainsKey("Примечание")) DescriptionBox.Text = record["Примечание"].ToString();
                if (record.ContainsKey("Счет контрагента")) CounterpartyAccountBox.Text = record["Счет контрагента"].ToString();
            }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var chartCatalog = allCatalogs.FirstOrDefault(c => c.Name.StartsWith("План счетов"));
            if (chartCatalog == null) return;

            var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);
            var dialog = new AccountSelectionDialog(accountsData);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
            {
                var accountCode = dialog.SelectedAccount.ContainsKey("Код") ? dialog.SelectedAccount["Код"].ToString() : "";
                var accountName = dialog.SelectedAccount.ContainsKey("Наименование") ? dialog.SelectedAccount["Наименование"].ToString() : "";
                CorrAccountBox.Text = $"{accountCode} - {accountName}";
                _selectedCorrAccountId = Guid.Parse(dialog.SelectedAccount["Id"].ToString());
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = NumberBox.Text,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Тип"] = ((ComboBoxItem)TypeCombo.SelectedItem)?.Content?.ToString() ?? "Исходящее",
                    ["Сумма"] = decimal.TryParse(AmountBox.Text, out var amount) ? amount : 0,
                    ["Назначение платежа"] = PurposeBox.Text,
                    ["Счет контрагента"] = CounterpartyAccountBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false
                };

                if (OrganizationCombo.SelectedItem is ReferenceItem org) itemData["Организация"] = org.Id;
                if (ContractorCombo.SelectedItem is ReferenceItem contractor) itemData["Контрагент"] = contractor.Id;
                if (BankCombo.SelectedItem is ReferenceItem bank) itemData["Банк"] = bank.Id;
                if (OurAccountCombo.SelectedItem is ReferenceItem account) itemData["Наш счет"] = account.Id;
                if (CurrencyCombo.SelectedItem is ReferenceItem currency) itemData["Валюта"] = currency.Id;
                if (_selectedCorrAccountId != Guid.Empty) itemData["Корр. счет"] = _selectedCorrAccountId;

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
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}