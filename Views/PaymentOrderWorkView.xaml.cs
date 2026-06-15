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
    public partial class PaymentOrderWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private List<PaymentOrderRow> _rows;
        private bool _isLoading = false;

        public PaymentOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;

            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;

            Loaded += async (s, e) => await LoadData();
        }

        private void UpdateButtonsState()
        {
            var hasSelection = DataGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            PostButton.IsEnabled = hasSelection;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private async Task LoadData()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                StatusText.Text = "Загрузка данных...";
                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

                // Кэш для Reference полей
                var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

                foreach (var field in _documentMetadata.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
                {
                    if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    {
                        var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                        var dict = new Dictionary<Guid, string>();
                        foreach (var item in refData)
                        {
                            if (item.ContainsKey("Id") && Guid.TryParse(item["Id"].ToString(), out var id))
                            {
                                string displayName = item.ContainsKey("Наименование") ? item["Наименование"].ToString() :
                                                    (item.ContainsKey("name") ? item["name"].ToString() :
                                                    (item.ContainsKey("Код") ? item["Код"].ToString() : id.ToString().Substring(0, 8)));
                                dict[id] = displayName;
                            }
                        }
                        referenceCache[field.Name] = dict;
                    }
                }
        
                // Загружаем наши расчетные счета для поля "Наш счет"
                try
                {
                    if (catalogsDict.TryGetValue("Расчетные счета организаций", out var accountCatalog))
                    {
                        var accountsData = await _metadataService.GetCatalogDataAsync(accountCatalog.Id);
                        var ourAccountDict = new Dictionary<Guid, string>();

                        // Загружаем банки для подстановки названий
                        var banksForAccountsDict = new Dictionary<Guid, string>();
                        if (catalogsDict.TryGetValue("Банки", out var bankCatalog))
                        {
                            var banksData = await _metadataService.GetCatalogDataAsync(bankCatalog.Id);
                            foreach (var bank in banksData)
                            {
                                if (bank.ContainsKey("Id") && Guid.TryParse(bank["Id"].ToString(), out var bankId))
                                {
                                    string bankName = bank.ContainsKey("Наименование банка") ? bank["Наименование банка"].ToString() :
                                                     (bank.ContainsKey("name") ? bank["name"].ToString() : "");
                                    banksForAccountsDict[bankId] = bankName;
                                }
                            }
                        }

                        foreach (var acc in accountsData)
                        {
                            if (acc.ContainsKey("Id") && Guid.TryParse(acc["Id"].ToString(), out var accId))
                            {
                                string accountNumber = acc.ContainsKey("Счет") ? acc["Счет"].ToString() :
                                                      (acc.ContainsKey("account_number") ? acc["account_number"].ToString() : "");

                                string bankName = "";
                                if (acc.ContainsKey("Банк") && acc["Банк"] != null && Guid.TryParse(acc["Банк"].ToString(), out var bankId))
                                {
                                    banksForAccountsDict.TryGetValue(bankId, out bankName);
                                }

                                ourAccountDict[accId] = string.IsNullOrEmpty(bankName) ? accountNumber : $"{accountNumber} - {bankName}";
                            }
                        }
                        referenceCache["Наш счет"] = ourAccountDict;
                        System.Diagnostics.Debug.WriteLine($"Загружено {ourAccountDict.Count} расчетных счетов");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки расчетных счетов: {ex.Message}");
                }

                _rows = new List<PaymentOrderRow>();

                foreach (var row in data)
                {
                    var newRow = new PaymentOrderRow
                    {
                        Id = row.ContainsKey("Id") ? Guid.Parse(row["Id"].ToString()) : Guid.NewGuid(),
                        DocNumber = row.ContainsKey("Номер") ? row["Номер"]?.ToString() : "",
                        DocDate = row.ContainsKey("Дата") && row["Дата"] != null ? (DateTime)row["Дата"] : DateTime.Now,
                        OrderType = row.ContainsKey("Тип") ? row["Тип"]?.ToString() : "",
                        Amount = row.ContainsKey("Сумма") && row["Сумма"] != null ? Convert.ToDecimal(row["Сумма"]) : 0,
                        Purpose = row.ContainsKey("Назначение платежа") ? row["Назначение платежа"]?.ToString() : "",
                        CounterpartyAccount = row.ContainsKey("Счет контрагента") ? row["Счет контрагента"]?.ToString() : "",
                        Description = row.ContainsKey("Примечание") ? row["Примечание"]?.ToString() : "",
                        IsPosted = row.ContainsKey("Проведён") && row["Проведён"] != null ? (bool)row["Проведён"] : false,
                        CreatedAt = row.ContainsKey("CreatedAt") && row["CreatedAt"] != null ? (DateTime)row["CreatedAt"] : DateTime.Now,
                        UpdatedAt = row.ContainsKey("UpdatedAt") && row["UpdatedAt"] != null ? (DateTime)row["UpdatedAt"] : DateTime.Now
                    };

                    // Организация
                    if (row.ContainsKey("Организация") && row["Организация"] != null && referenceCache.TryGetValue("Организация", out var orgDict) && Guid.TryParse(row["Организация"].ToString(), out var orgId))
                        newRow.OrganizationName = orgDict.ContainsKey(orgId) ? orgDict[orgId] : row["Организация"].ToString();

                    // Контрагент
                    if (row.ContainsKey("Контрагент") && row["Контрагент"] != null && referenceCache.TryGetValue("Контрагент", out var contDict) && Guid.TryParse(row["Контрагент"].ToString(), out var contId))
                        newRow.ContractorName = contDict.ContainsKey(contId) ? contDict[contId] : row["Контрагент"].ToString();

                    // Банк
                    if (row.ContainsKey("Банк") && row["Банк"] != null && referenceCache.TryGetValue("Банк", out var bankDict) && Guid.TryParse(row["Банк"].ToString(), out var bankId))
                        newRow.BankName = bankDict.ContainsKey(bankId) ? bankDict[bankId] : row["Банк"].ToString();

                    // Наш счет
                    if (row.ContainsKey("Наш счет") && row["Наш счет"] != null && referenceCache.TryGetValue("Наш счет", out var accDict) && Guid.TryParse(row["Наш счет"].ToString(), out var accId))
                        newRow.OurAccountName = accDict.ContainsKey(accId) ? accDict[accId] : row["Наш счет"].ToString();

                    // Валюта
                    if (row.ContainsKey("Валюта") && row["Валюта"] != null && referenceCache.TryGetValue("Валюта", out var currDict) && Guid.TryParse(row["Валюта"].ToString(), out var currId))
                        newRow.CurrencyName = currDict.ContainsKey(currId) ? currDict[currId] : row["Валюта"].ToString();

                    // Корр. счет
                    string corrValue = null;
                    string[] possibleCorrNames = { "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет" };
                    foreach (var name in possibleCorrNames)
                    {
                        if (row.ContainsKey(name) && row[name] != null)
                        {
                            corrValue = row[name].ToString();
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(corrValue) && referenceCache.TryGetValue("correspondent_account", out var corrDict) && Guid.TryParse(corrValue, out var corrId))
                        newRow.CorrespondentAccountName = corrDict.ContainsKey(corrId) ? corrDict[corrId] : corrValue;

                    _rows.Add(newRow);
                }

                DataGrid.ItemsSource = _rows;
                StatusText.Text = $"📊 Загружено записей: {_rows.Count}";
                UpdateButtonsState();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) await LoadData();
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selected = DataGrid.SelectedItem as PaymentOrderRow;
            if (selected == null) return;
            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService, selected.Id);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true) await LoadData();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selected = DataGrid.SelectedItem as PaymentOrderRow;
            if (selected == null) return;
            if (MessageBox.Show("Удалить документ?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selected.Id);
                await LoadData();
            }
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            var selected = DataGrid.SelectedItem as PaymentOrderRow;
            if (selected == null) return;
            if (MessageBox.Show("Провести документ?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = "🔄 Проведение...";
                    await _metadataService.PostDocumentAsync(_documentMetadata.Id, selected.Id);
                    await LoadData();
                    MessageBox.Show("Документ проведён!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка проведения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadData();
    }

    public class PaymentOrderRow
    {
        public Guid Id { get; set; }
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string OrderType { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string ContractorName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string OurAccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string CurrencyName { get; set; } = string.Empty;
        public string CorrespondentAccountName { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string CounterpartyAccount { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}