using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class PaymentOrderWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private List<PaymentOrderRow> _rows = new();
        private bool _isLoading;

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
            var row = DataGrid.SelectedItem as PaymentOrderRow;
            bool hasSelection = row != null;
            bool isPosted = row?.IsPosted == true;
            bool canEdit = hasSelection && !isPosted;

            EditButton.IsEnabled = canEdit;
            DeleteButton.IsEnabled = hasSelection;
            PostButton.IsEnabled = hasSelection;
            PostButton.Content = isPosted ? "↩ Отменить проведение" : "✅ Провести";
            PostButton.Background = isPosted ? Brushes.DarkOrange : Brushes.MediumPurple;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow row)
                return;

            if (!row.IsPosted)
            {
                MessageBox.Show("Документ еще не проведен, проводки для просмотра нет.", "Проводки",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var documentType = ResolvePaymentOrderPostingType(row.OrderType);
            var postings = await _metadataService.GetPostingsByDocumentAsync(documentType, row.DocNumber, row.DocDate);
            var posting = postings.FirstOrDefault();
            if (posting == null)
            {
                MessageBox.Show("Связанная проводка в журнале не найдена.", "Проводки",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var details = new PostingDetailsDialog(posting)
            {
                Owner = Window.GetWindow(this)
            };
            details.ShowDialog();
        }

        private async Task LoadData()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            try
            {
                StatusText.Text = "Загрузка данных...";
                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                var referenceCache = await LoadReferenceCacheAsync(catalogsDict);

                await LoadOurSettlementAccountsFallbackAsync(catalogsDict, referenceCache);

                _rows = data.Select(row => BuildRow(row, referenceCache, accountAnalytics)).ToList();
                DataGrid.ItemsSource = _rows;
                UpdateAnalyticColumns(data, accountAnalytics);
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

        private async Task<Dictionary<string, Dictionary<Guid, string>>> LoadReferenceCacheAsync(
            Dictionary<string, MetadataObject> catalogsDict)
        {
            var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();
            foreach (var field in _documentMetadata.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    continue;

                var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                var dict = new Dictionary<Guid, string>();
                foreach (var item in refData)
                {
                    if (item.TryGetValue("Id", out var rawId) && Guid.TryParse(rawId?.ToString(), out var id))
                        dict[id] = ReferenceDisplayHelper.BuildDisplayValue(item, field);
                }

                referenceCache[field.Name] = dict;
                if (!string.IsNullOrWhiteSpace(field.DbColumnName))
                    referenceCache[field.DbColumnName] = dict;
            }

            return referenceCache;
        }

        private async Task LoadOurSettlementAccountsFallbackAsync(
            Dictionary<string, MetadataObject> catalogsDict,
            Dictionary<string, Dictionary<Guid, string>> referenceCache)
        {
            if (referenceCache.ContainsKey("Наш счет") || !catalogsDict.TryGetValue("Расчетные счета организаций", out var accountCatalog))
                return;

            try
            {
                var accountsData = await _metadataService.GetCatalogDataAsync(accountCatalog.Id);
                var bankDict = new Dictionary<Guid, string>();
                if (catalogsDict.TryGetValue("Банки", out var bankCatalog))
                {
                    var banksData = await _metadataService.GetCatalogDataAsync(bankCatalog.Id);
                    foreach (var bank in banksData)
                    {
                        if (bank.TryGetValue("Id", out var rawBankId) && Guid.TryParse(rawBankId?.ToString(), out var bankId))
                        {
                            bankDict[bankId] = ReadString(bank, "Наименование банка", "name");
                        }
                    }
                }

                var ourAccountDict = new Dictionary<Guid, string>();
                foreach (var account in accountsData)
                {
                    if (!account.TryGetValue("Id", out var rawId) || !Guid.TryParse(rawId?.ToString(), out var accountId))
                        continue;

                    var accountNumber = ReadString(account, "Счет", "account_number");
                    var bankName = string.Empty;
                    if (Guid.TryParse(ReadString(account, "Банк", "bank_id"), out var bankId))
                        bankDict.TryGetValue(bankId, out bankName);

                    ourAccountDict[accountId] = string.IsNullOrWhiteSpace(bankName)
                        ? accountNumber
                        : $"{accountNumber} - {bankName}";
                }

                referenceCache["Наш счет"] = ourAccountDict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки расчетных счетов: {ex.Message}");
            }
        }

        private PaymentOrderRow BuildRow(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var result = new PaymentOrderRow
            {
                Id = Guid.TryParse(ReadString(row, "Id"), out var id) ? id : Guid.NewGuid(),
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(ReadString(row, "Номер", "doc_number", "number")),
                DocDate = ReadDate(row, DateTime.Now, "Дата", "doc_date", "date"),
                OrderType = ReadString(row, "Тип", "order_type"),
                Amount = ReadDecimal(row, "Сумма", "amount"),
                AmountCurrency = ReadDecimal(row, "Сумма в валюте", "amount_currency"),
                ExchangeRate = ReadDecimal(row, "Курс", "exchange_rate"),
                Purpose = ReadString(row, "Назначение платежа", "purpose"),
                Description = ReadString(row, "Примечание", "description"),
                IsPosted = ReadBool(row, "Проведён", "is_posted"),
                CreatedAt = ReadDate(row, DateTime.Now, "CreatedAt"),
                UpdatedAt = ReadDate(row, DateTime.Now, "UpdatedAt")
            };

            result.OrganizationName = ResolveReferenceOrRaw(row, referenceCache, "Организация", "organization_id");
            result.BankName = ResolveReferenceOrRaw(row, referenceCache, "Банк", "bank_id");
            result.CurrencyName = ResolveReferenceOrRaw(row, referenceCache, "Валюта", "currency_id");
            result.EmployeeName = ResolveReferenceOrRaw(row, referenceCache, "Сотрудник", "employee_id");
            result.MaterialName = ResolveReferenceOrRaw(row, referenceCache, "Материал", "material_id");
            result.PaymentClassificationName = ResolveReferenceOrRaw(row, referenceCache, "Классификация платежа", "payment_classification_id");
            result.OurAccountName = ResolveAccountDisplay(row, referenceCache, accountAnalytics,
                "our_account_id", "Наш счет", "Дебет", "debit_account");
            result.CorrespondentAccountName = ResolveAccountDisplay(row, referenceCache, accountAnalytics,
                "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Кредит", "credit_account");

            return result;
        }

        private static string ResolveReferenceOrRaw(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            params string[] keys)
        {
            var value = GetFirstValue(row, keys);
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (Guid.TryParse(value, out var id) && TryResolveReference(referenceCache, id, out var displayName, keys))
                return displayName;

            return value;
        }

        private static string ResolveAccountDisplay(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            AccountAnalyticsRegistry accountAnalytics,
            params string[] keys)
        {
            var value = GetFirstValue(row, keys);
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (Guid.TryParse(value, out var id) && TryResolveReference(referenceCache, id, out var displayName, keys))
                return displayName;

            var account = accountAnalytics.FindAccount(value);
            return account?.DisplayName ?? value;
        }

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = new[]
            {
                "our_account_id", "Наш счет", "Дебет", "debit_account",
                "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Кредит", "credit_account"
            };

            OrganizationColumn.Visibility = GetAnalyticColumnVisibility(
                "Организация", "Организации", rows, accountFields, accountAnalytics);
            var currencyVisibility = GetAnalyticColumnVisibility(
                "Валюта", "Справочник валют", rows, accountFields, accountAnalytics);
            CurrencyColumn.Visibility = currencyVisibility;
            AmountCurrencyColumn.Visibility = currencyVisibility;
            ExchangeRateColumn.Visibility = currencyVisibility;
            EmployeeColumn.Visibility = GetAnalyticColumnVisibility(
                "Сотрудник", "Сотрудники (Списочный состав)", rows, accountFields, accountAnalytics);
            MaterialColumn.Visibility = GetAnalyticColumnVisibility(
                "Материал", "Справочник материалов", rows, accountFields, accountAnalytics);
        }

        private static Visibility GetAnalyticColumnVisibility(
            string fieldName,
            string referenceCatalog,
            List<Dictionary<string, object>> rows,
            IEnumerable<string> accountFields,
            AccountAnalyticsRegistry registry)
        {
            return AccountAnalyticsRules.ShouldShowFieldForRows(
                    fieldName, rows, accountFields, registry, referenceCatalog)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string ReadString(Dictionary<string, object> row, params string[] keys)
        {
            return GetFirstValue(row, keys);
        }

        private static decimal ReadDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is decimal decimalValue)
                    return decimalValue;

                if (decimal.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return 0m;
        }

        private static bool ReadBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is bool boolValue)
                    return boolValue;

                if (bool.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return false;
        }

        private static DateTime ReadDate(Dictionary<string, object> row, DateTime fallback, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is DateTime date)
                    return date;

                if (DateTime.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return fallback;
        }

        private static string GetFirstValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool TryResolveReference(
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            Guid id,
            out string displayName,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (referenceCache.TryGetValue(key, out var dict) && dict.TryGetValue(id, out displayName))
                    return true;
            }

            displayName = string.Empty;
            return false;
        }

        private static string ResolvePaymentOrderPostingType(string orderType)
        {
            return orderType.Contains("Вход", StringComparison.OrdinalIgnoreCase)
                ? "Входящее платежное поручение"
                : "Исходящее платежное поручение";
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
                await LoadData();
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected || selected.IsPosted)
                return;

            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService, selected.Id)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
                await LoadData();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected)
                return;

            string message = selected.IsPosted
                ? "Удалить документ? Это также удалит связанные проводки из журнала."
                : "Удалить документ?";
            if (MessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selected.Id);
            await LoadData();
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected)
                return;

            if (selected.IsPosted)
            {
                if (MessageBox.Show("Отменить проведение документа? Связанные проводки будут удалены из журнала.",
                        "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try
                {
                    StatusText.Text = "🔄 Отмена проведения...";
                    await _metadataService.UnpostDocumentAsync(_documentMetadata.Id, selected.Id);
                    await LoadData();
                    MessageBox.Show("Проведение отменено.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка отмены проведения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                }

                return;
            }

            if (selected.Amount <= 0)
            {
                MessageBox.Show("Сумма должна быть больше 0 для проведения.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show("Провести документ?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

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

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadData();
    }

    public class PaymentOrderRow
    {
        public Guid Id { get; set; }
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string OrderType { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string OurAccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public string CurrencyName { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string CorrespondentAccountName { get; set; } = string.Empty;
        public string PaymentClassificationName { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string IsPostedDisplay => LocalizationService.DisplayValue(IsPosted);
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}