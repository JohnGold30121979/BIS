using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class CashOrderWorkView : UserControl
    {
        private const string CashOrderDocumentName = "Расходный/Приходный КО";
        private const string CashOrderReceiptKind = "Receipt";
        private const string CashOrderPaymentKind = "Payment";
        private const string CashOrderReceiptDocumentType = "Приходный кассовый ордер";
        private const string CashOrderPaymentDocumentType = "Расходный кассовый ордер";
        private const string CashBookReportCode = "standard.frx.finance.cash.cash-book";
        private const string ReceiptExpenseRegisterReportCode = "standard.frx.finance.cash.receipts-expenses-register";

        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private List<CashOrderRow> _allRows = new();
        private List<CashDeskItem> _cashDeskFilterItems = new();
        private readonly ObservableCollection<Dictionary<string, object>> _postingDetails = new();
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private string _moduleName = string.Empty;
        private bool _isLoading;

        public CashOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            var today = DateTime.Today;
            PeriodStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            PeriodEndDatePicker.SelectedDate = today;
            PostingDetailsGrid.ItemsSource = _postingDetails;
            InitializeHeader(documentMetadata.Icon, CashOrderDocumentName, documentMetadata.Description);
        }

        public CashOrderWorkView(MetadataObject receiptDocumentMetadata, MetadataObject paymentDocumentMetadata, MetadataService metadataService)
            : this(ResolveUnifiedDocument(receiptDocumentMetadata, paymentDocumentMetadata), metadataService)
        {
        }

        private static MetadataObject ResolveUnifiedDocument(MetadataObject firstDocument, MetadataObject secondDocument)
        {
            if (firstDocument.Name.Equals(CashOrderDocumentName, StringComparison.OrdinalIgnoreCase) ||
                firstDocument.TableName.Equals("doc_cash_orders", StringComparison.OrdinalIgnoreCase))
            {
                return firstDocument;
            }

            if (secondDocument.Name.Equals(CashOrderDocumentName, StringComparison.OrdinalIgnoreCase) ||
                secondDocument.TableName.Equals("doc_cash_orders", StringComparison.OrdinalIgnoreCase))
            {
                return secondDocument;
            }

            return firstDocument;
        }

        private void InitializeHeader(string icon, string title, string description)
        {
            TitleText.Text = $"{icon} {title}";
            DescriptionText.Text = string.IsNullOrWhiteSpace(description)
                ? "Список приходных и расходных кассовых ордеров"
                : description;
            Loaded += async (_, _) => await LoadData();
        }

        private void UpdateButtonsState()
        {
            var selected = DataGrid.SelectedItem as CashOrderRow;
            var hasSelection = selected != null;
            EditButton.IsEnabled = hasSelection && selected?.IsPosted != true;
            DeleteButton.IsEnabled = hasSelection && selected?.IsPosted != true;
            PostButton.IsEnabled = hasSelection;
            PrintButton.IsEnabled = hasSelection;
            PostButton.Content = selected?.IsPosted == true ? "↩ Отменить проведение" : "✅ Провести";
            PostButton.Width = selected?.IsPosted == true ? 175 : 100;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
            if (DataGrid.SelectedItem is CashOrderRow selected)
            {
                StatusText.Text = selected.IsPosted
                    ? $"Проведен: {selected.OrderTypeDisplay}; Дт {selected.DebitAccount} / Кт {selected.CreditAccount}, {selected.Amount:N2} сом. Двойной щелчок откроет проводку."
                    : $"Не проведен: {selected.OrderTypeDisplay} {selected.DocNumber}, {selected.Amount:N2} сом.";
            }
        }

        private void UpdateSelectedPostingDetails()
        {
            _postingDetails.Clear();

            if (DataGrid?.SelectedItem is not CashOrderRow row)
            {
                SetDetailColumnsVisibility(false, false, false, false);
                _postingDetails.Add(PostingDetailRowFactory.Create(("Документ", "Выберите кассовый ордер в списке выше")));
                return;
            }

            var selectedSettings = new[]
            {
                _accountAnalytics.GetSettingsByCode(row.DebitAccount),
                _accountAnalytics.GetSettingsByCode(row.CreditAccount)
            };
            var showCurrency = ShouldShowPostingAnalytic("Валюта", "Справочник валют", selectedSettings);
            var showOrganization = ShouldShowPostingAnalytic("Организация", "Организации", selectedSettings);
            var showEmployee = ShouldShowPostingAnalytic("Сотрудник", "Сотрудники (Списочный состав)", selectedSettings);
            var showMaterial = ShouldShowPostingAnalytic("Материал", "Справочник материалов", selectedSettings);
            SetDetailColumnsVisibility(showCurrency, showOrganization, showEmployee, showMaterial);

            var detail = PostingDetailRowFactory.Create();
            SetPostingDetail(detail, "Документ", row.DocNumber);
            SetPostingDetail(detail, "Тип документа", row.PostingDocumentType);
            SetPostingDetail(detail, "Дата", row.DocDate.ToString("dd.MM.yyyy"));
            SetPostingDetail(detail, "Модуль", _moduleName);
            SetPostingDetail(detail, "Дебет", ExtractAccountCode(row.DebitAccount));
            SetPostingDetail(detail, "Кредит", ExtractAccountCode(row.CreditAccount));
            SetPostingDetail(detail, "Сумма", row.Amount.ToString("N2"));

            if (showCurrency)
            {
                SetPostingDetail(detail, "Сумма вал.", row.AmountInCurrency != 0m ? row.AmountInCurrency.ToString("N2") : null);
                SetPostingDetail(detail, "Валюта", row.CurrencyName);
            }

            if (showOrganization)
                SetPostingDetail(detail, "Организация", row.OrganizationName);

            if (showEmployee)
                SetPostingDetail(detail, "Сотрудник", row.EmployeeName);

            if (showMaterial)
                SetPostingDetail(detail, "Материал", row.MaterialName);

            SetPostingDetail(detail, "Статус", row.IsPosted ? "Проведён" : "Не проведён");
            SetPostingDetail(detail, "Примечание", row.Description);
            _postingDetails.Add(detail);
        }

        private bool ShouldShowPostingAnalytic(
            string fieldName,
            string referenceCatalog,
            IEnumerable<AccountAnalyticsSettings?> selectedSettings)
        {
            return AccountAnalyticsRules.ShouldShowField(
                fieldName,
                selectedSettings,
                _accountAnalytics.Definitions,
                referenceCatalog,
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);
        }

        private void SetDetailColumnsVisibility(bool showCurrency, bool showOrganization, bool showEmployee, bool showMaterial)
        {
            DetailAmountCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            DetailCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            DetailOrganizationColumn.Visibility = showOrganization ? Visibility.Visible : Visibility.Collapsed;
            DetailEmployeeColumn.Visibility = showEmployee ? Visibility.Visible : Visibility.Collapsed;
            DetailMaterialColumn.Visibility = showMaterial ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SetPostingDetail(Dictionary<string, object> detail, string field, string? value) => 
            PostingDetailRowFactory.Set(detail, field, value);

        private static string ExtractAccountCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var separatorIndex = value.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? value[..separatorIndex].Trim()
                : value.Trim();
        }
        private async Task LoadData()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            try
            {
                StatusText.Text = "Загрузка данных...";

                var documentRows = (await _metadataService.GetCatalogDataAsync(_documentMetadata.Id))
                    .Select(row => (Document: _documentMetadata, Row: row))
                    .ToList();

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsByName = allCatalogs.GroupBy(catalog => catalog.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                _moduleName = await _metadataService.GetAssignedModuleNameAsync(_documentMetadata.Id, _documentMetadata.ObjectType) ?? string.Empty;
                await LoadCashDeskFilterItemsAsync(catalogsByName, _accountAnalytics);
                var referenceCache = await BuildReferenceCacheAsync(documentRows, catalogsByName, _accountAnalytics);

                _allRows = documentRows
                    .Select(item => CreateCashOrderRow(item.Document, item.Row, referenceCache))
                    .OrderByDescending(row => row.DocDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .ToList();

                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Ошибка LoadData: {ex.Message}");
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task LoadCashDeskFilterItemsAsync(
            Dictionary<string, MetadataObject> catalogsByName,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var selectedId = (CashDeskFilterCombo.SelectedItem as CashDeskItem)?.Id;
            var items = new List<CashDeskItem>
            {
                new() { Id = Guid.Empty, DisplayName = "Все кассы" }
            };

            if (catalogsByName.TryGetValue("Кассы", out var cashCatalog))
            {
                var rows = await _metadataService.GetCatalogDataAsync(cashCatalog.Id);
                items.AddRange(rows
                    .Where(row => row.TryGetValue("Id", out var id) && Guid.TryParse(id?.ToString(), out _))
                    .Select(row => CreateCashDeskFilterItem(row, accountAnalytics)));
            }

            _cashDeskFilterItems = items;
            CashDeskFilterCombo.ItemsSource = _cashDeskFilterItems;
            CashDeskFilterCombo.SelectedItem = _cashDeskFilterItems.FirstOrDefault(item => item.Id == selectedId)
                                              ?? _cashDeskFilterItems.First();
            CashPostingsButton.IsEnabled = _cashDeskFilterItems.Count > 1;
        }

        private static CashDeskItem CreateCashDeskFilterItem(
            Dictionary<string, object> row,
            AccountAnalyticsRegistry accountAnalytics)
        {
            return new CashDeskItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = GetRowString(row, "Наименование кассы", "Наименование", "name", "Код", "code"),
                AccountCode = CashOrderDialog.ResolveCashDeskAccountCode(
                    GetRowString(row, "Счет", "Счет кассы", "account_code", "cash_account", "Код", "code"),
                    accountAnalytics),
                CashNumber = GetRowString(row, "Номер кассы", "cash_number"),
                CurrencyName = GetRowString(row, "Валюта", "currency_id")
            };
        }

        private void ApplyFilters()
        {
            var rows = _allRows.AsEnumerable();
            var startDate = PeriodStartDatePicker.SelectedDate?.Date;
            var endDate = PeriodEndDatePicker.SelectedDate?.Date;

            if (startDate.HasValue)
                rows = rows.Where(row => row.DocDate.Date >= startDate.Value);
            if (endDate.HasValue)
                rows = rows.Where(row => row.DocDate.Date <= endDate.Value);

            if (CashDeskFilterCombo.SelectedItem is CashDeskItem selectedCashDesk && selectedCashDesk.Id != Guid.Empty)
                rows = rows.Where(row => RowMatchesCashDesk(row, selectedCashDesk));

            var filteredRows = rows.ToList();
            DataGrid.ItemsSource = filteredRows;
            DataGrid.Items.Refresh();
            StatusText.Text = $"📊 Показано записей: {filteredRows.Count} из {_allRows.Count}";
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
        }

        private static bool RowMatchesCashDesk(CashOrderRow row, CashDeskItem cashDesk)
        {
            if (Guid.TryParse(row.CashDeskId, out var cashDeskId))
                return cashDeskId == cashDesk.Id;

            return row.CashDeskName.Equals(cashDesk.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                   row.CashDeskName.Equals(cashDesk.DisplayNameWithAccount, StringComparison.OrdinalIgnoreCase);
        }

        private async void OnCashPostingsClick(object sender, RoutedEventArgs e)
        {
            if (CashDeskFilterCombo.SelectedItem is not CashDeskItem selectedCashDesk || selectedCashDesk.Id == Guid.Empty)
            {
                MessageBox.Show("Выберите конкретную кассу.", "Проводки по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                MessageBox.Show("У выбранной кассы не указан счет.", "Проводки по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = PeriodStartDatePicker.SelectedDate?.Date ?? DateTime.Today.AddMonths(-1);
            var endDate = PeriodEndDatePicker.SelectedDate?.Date ?? DateTime.Today;
            if (startDate > endDate)
            {
                MessageBox.Show("Дата начала периода не может быть больше даты окончания.", "Проводки по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText.Text = "Загрузка проводок по кассе...";
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var postingService = new PostingService(context);
                var postings = await postingService.GetAllPostingsAsync(startDate, endDate);
                var accountCode = ExtractAccountCode(selectedCashDesk.AccountCode);
                var cashPostings = postings
                    .Where(posting =>
                        ExtractAccountCode(posting.DebitAccount).Equals(accountCode, StringComparison.OrdinalIgnoreCase) ||
                        ExtractAccountCode(posting.CreditAccount).Equals(accountCode, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(posting => posting.Date)
                    .ThenBy(posting => posting.DocumentNumber)
                    .ToList();

                var dialog = new DocumentPostingsDialog(
                    "Проводки по кассе",
                    $"{selectedCashDesk.DisplayNameWithAccount} за {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}",
                    cashPostings)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Проводок по кассе: {cashPostings.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки проводок по кассе";
                MessageBox.Show($"Ошибка загрузки проводок по кассе: {ex.Message}", "Проводки по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            if (_isLoading || DataGrid == null)
                return;

            ApplyFilters();
        }
        private async Task<Dictionary<string, Dictionary<Guid, string>>> BuildReferenceCacheAsync(
            IReadOnlyCollection<(MetadataObject Document, Dictionary<string, object> Row)> documentRows,
            Dictionary<string, MetadataObject> catalogsByName,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var referenceCache = new Dictionary<string, Dictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);
            var referenceFields = _documentMetadata.Fields
                .Where(field => field.FieldType == "Reference" && !string.IsNullOrWhiteSpace(field.ReferenceCatalog))
                .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var field in referenceFields)
            {
                var ids = new HashSet<Guid>();
                foreach (var item in documentRows)
                {
                    if (item.Row.TryGetValue(field.Name, out var value) && Guid.TryParse(value?.ToString(), out var id))
                        ids.Add(id);
                }

                if (ids.Count == 0 || !catalogsByName.TryGetValue(field.ReferenceCatalog!, out var catalog))
                    continue;

                var referenceRows = await _metadataService.GetCatalogDataAsync(catalog.Id);
                var values = new Dictionary<Guid, string>();
                foreach (var row in referenceRows)
                {
                    if (!row.TryGetValue("Id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var id) || !ids.Contains(id))
                        continue;

                    values[id] = BuildReferenceDisplay(row, field);
                }

                referenceCache[field.Name] = values;
            }

            await AddAccountReferenceCacheAsync(referenceCache, catalogsByName);
            await AddCashDeskReferenceCacheAsync(referenceCache, catalogsByName, accountAnalytics);
            return referenceCache;
        }

        private async Task AddAccountReferenceCacheAsync(
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            Dictionary<string, MetadataObject> catalogsByName)
        {
            var chartCatalog = catalogsByName.FirstOrDefault(item => item.Key.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase)).Value;
            if (chartCatalog == null)
                return;

            var accounts = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);
            var values = new Dictionary<Guid, string>();
            foreach (var account in accounts)
            {
                if (!account.TryGetValue("Id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var id))
                    continue;

                var code = GetRowString(account, "Код", "code");
                var name = GetRowString(account, "Наименование", "name");
                values[id] = string.IsNullOrWhiteSpace(name) ? code : $"{code} - {name}";
            }

            referenceCache["correspondent_account"] = values;
            referenceCache["Корр. счет"] = values;
        }

        private async Task AddCashDeskReferenceCacheAsync(
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            Dictionary<string, MetadataObject> catalogsByName,
            AccountAnalyticsRegistry accountAnalytics)
        {
            if (!catalogsByName.TryGetValue("Кассы", out var cashCatalog))
                return;

            var cashRows = await _metadataService.GetCatalogDataAsync(cashCatalog.Id);
            var values = new Dictionary<Guid, string>();
            foreach (var cash in cashRows)
            {
                if (!cash.TryGetValue("Id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var id))
                    continue;

                var name = GetRowString(cash, "Наименование кассы", "Наименование", "name", "Код", "code");
                var account = CashOrderDialog.ResolveCashDeskAccountCode(
                    GetRowString(cash, "Счет", "Счет кассы", "code", "Код"),
                    accountAnalytics);
                values[id] = string.IsNullOrWhiteSpace(account) ? name : $"{name} (счет {account})";
            }

            referenceCache["Касса"] = values;
            referenceCache["cash_desk_id"] = values;
        }

        private CashOrderRow CreateCashOrderRow(
            MetadataObject document,
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache)
        {
            var orderKind = ResolveOrderKind(row, document.Name);
            var result = new CashOrderRow
            {
                DocumentMetadata = document,
                DocumentMetadataId = document.Id,
                DocumentType = document.Name,
                OrderKind = orderKind,
                Id = ReadGuid(row, "Id") ?? Guid.NewGuid(),
                DocNumber = GetRowString(row, "Номер", "doc_number", "Номер документа"),
                DocDate = ReadDate(row, "Дата", "doc_date") ?? DateTime.Now,
                Amount = ReadDecimal(row, "Сумма", "amount"),
                Basis = GetRowString(row, "Основание", "basis"),
                Description = GetRowString(row, "Примечание", "description"),
                IsPosted = ReadBool(row, "Проведён", "is_posted"),
                CreatedAt = ReadDate(row, "CreatedAt") ?? DateTime.Now,
                UpdatedAt = ReadDate(row, "UpdatedAt") ?? DateTime.Now,
                DebitAccount = GetRowString(row, "Дебет", "debit_account"),
                CreditAccount = GetRowString(row, "Кредит", "credit_account"),
                AmountInCurrency = ReadDecimal(row, "Сумма в валюте", "amount_currency"),
                CashDeskId = GetRowString(row, "Касса", "cash_desk_id")
            };

            result.OrganizationName = ResolveReference(row, referenceCache, "Организация", "organization_id");
            result.CurrencyName = ResolveReference(row, referenceCache, "Валюта", "currency_id");
            result.EmployeeName = ResolveReference(row, referenceCache, "Сотрудник", "employee_id");
            result.MaterialName = ResolveReference(row, referenceCache, "Материал", "material_id");
            result.CashDeskName = ResolveReference(row, referenceCache, "Касса", "cash_desk_id");
            result.CorrespondentAccountName = ResolveReference(row, referenceCache, "Корр. счет", "correspondent_account");
            return result;
        }

        private static string ResolveReference(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                if (!row.TryGetValue(fieldName, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (Guid.TryParse(value.ToString(), out var id) &&
                    referenceCache.TryGetValue(fieldName, out var values) &&
                    values.TryGetValue(id, out var displayValue))
                {
                    return displayValue;
                }

                return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string BuildReferenceDisplay(Dictionary<string, object> row, MetadataField field)
        {
            var display = ReferenceDisplayHelper.BuildDisplayValue(row, field);
            if (!string.IsNullOrWhiteSpace(display))
                return display;

            return row.FirstOrDefault(item => item.Key != "Id").Value?.ToString()
                   ?? row.GetValueOrDefault("Id")?.ToString()
                   ?? string.Empty;
        }

        private static Guid? ReadGuid(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out var id))
                    return id;
            }

            return null;
        }

        private static DateTime? ReadDate(Dictionary<string, object> row, params string[] keys)
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

            return null;
        }

        private static decimal ReadDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value &&
                    decimal.TryParse(value.ToString(), out var amount))
                {
                    return amount;
                }
            }

            return 0m;
        }

        private static bool ReadBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is bool flag)
                    return flag;

                if (bool.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return false;
        }

        private static string GetRowString(Dictionary<string, object> row, params string[] keys)
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

            return string.Empty;
        }

        private async void OnAddPaymentClick(object sender, RoutedEventArgs e)
        {
            await CreateCashOrderAsync(CashOrderPaymentKind, "Расходный КО");
        }

        private async void OnAddReceiptClick(object sender, RoutedEventArgs e)
        {
            await CreateCashOrderAsync(CashOrderReceiptKind, "Приходный КО");
        }

        private async Task CreateCashOrderAsync(string orderKind, string title)
        {
            try
            {
                var dialog = new CashOrderDialog(_documentMetadata, _metadataService, orderKind)
                {
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Title = title
                };

                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                    MessageBox.Show("Документ успешно добавлен!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Выберите документ для редактирования!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new CashOrderDialog(_documentMetadata, _metadataService, selectedRow.Id)
                {
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                    MessageBox.Show("Документ успешно обновлён!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка редактирования: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Выберите документ для удаления!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Удалить выбранный документ?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selectedRow.Id);
                await LoadData();
                MessageBox.Show("Документ успешно удалён!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Выберите документ для проведения!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var actionText = selectedRow.IsPosted ? "Отменить проведение выбранного документа?" : "Провести выбранный документ?";
            var result = MessageBox.Show(actionText, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText.Text = selectedRow.IsPosted ? "Отмена проведения..." : "Проведение...";
                if (selectedRow.IsPosted)
                    await _metadataService.UnpostDocumentAsync(_documentMetadata.Id, selectedRow.Id);
                else
                    await _metadataService.PostDocumentAsync(_documentMetadata.Id, selectedRow.Id);

                await LoadData();
                MessageBox.Show(selectedRow.IsPosted ? "Проведение документа отменено." : "Документ успешно проведён!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения проведения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusText.Text = "✅ Готово";
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private bool TryGetCurrentPeriod(string caption, out DateTime startDate, out DateTime endDate)
        {
            startDate = PeriodStartDatePicker.SelectedDate?.Date ?? DateTime.Today;
            endDate = PeriodEndDatePicker.SelectedDate?.Date ?? DateTime.Today;

            if (startDate > endDate)
            {
                MessageBox.Show("Дата начала периода не может быть позже даты окончания.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private bool TryGetSelectedConcreteCashDesk(string caption, out CashDeskItem cashDesk)
        {
            cashDesk = CashDeskFilterCombo.SelectedItem as CashDeskItem ?? new CashDeskItem();
            if (cashDesk.Id == Guid.Empty)
            {
                MessageBox.Show("Выберите конкретную кассу.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.IsNullOrWhiteSpace(cashDesk.AccountCode))
            {
                MessageBox.Show("У выбранной кассы не заполнен счет. Обороты по кассе собрать нельзя.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private async Task<List<PostingViewModel>> LoadCashPostingsAsync(CashDeskItem cashDesk, DateTime startDate, DateTime endDate)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var allPostings = await postingService.GetAllPostingsAsync(startDate.Date, endDate.Date);
            var accountCode = ExtractAccountCode(cashDesk.AccountCode);

            return allPostings
                .Where(posting => string.Equals(ExtractAccountCode(posting.DebitAccount), accountCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ExtractAccountCode(posting.CreditAccount), accountCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(posting => posting.Date)
                .ThenBy(posting => posting.DocumentNumber)
                .ToList();
        }

        private static string CurrentUserName() =>
            ServiceLocator.AuthService.CurrentUser?.Login ?? Environment.UserName;

        private async Task<bool> ConfirmAdminPasswordAsync(string caption)
        {
            if (!ServiceLocator.AuthService.IsAdmin)
            {
                MessageBox.Show("Открытие кассового дня доступно только администратору.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var login = ServiceLocator.AuthService.CurrentUser?.Login;
            if (string.IsNullOrWhiteSpace(login))
            {
                MessageBox.Show("Не удалось определить текущего администратора.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var password = PromptPassword(Window.GetWindow(this), caption, "Введите пароль администратора для открытия кассового дня:");
            if (password == null)
                return false;

            var result = await ServiceLocator.AuthService.LoginAsync(login, password);
            if (!result.Success || !ServiceLocator.AuthService.IsAdmin)
            {
                MessageBox.Show("Пароль администратора не подтвержден.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private static string? PromptPassword(Window? owner, string title, string message)
        {
            var passwordBox = new PasswordBox
            {
                MinWidth = 280,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var okButton = new Button
            {
                Content = "ОК",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 16, 8, 0),
                IsDefault = true
            };
            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 16, 0, 0),
                IsCancel = true
            };

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttonsPanel.Children.Add(okButton);
            buttonsPanel.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(18)
            };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(passwordBox);
            panel.Children.Add(buttonsPanel);

            var dialog = new Window
            {
                Title = title,
                Owner = owner,
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            string? password = null;
            okButton.Click += (_, _) =>
            {
                password = passwordBox.Password;
                dialog.DialogResult = true;
            };
            dialog.Loaded += (_, _) => passwordBox.Focus();

            return dialog.ShowDialog() == true ? password : null;
        }

        private async void OnCloseCashDayClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Закрытие кассового дня";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            var cashDate = (PeriodEndDatePicker.SelectedDate ?? DateTime.Today).Date;
            var confirm = MessageBox.Show($"Закрыть кассовый день {cashDate:dd.MM.yyyy} по кассе \"{cashDesk.DisplayNameWithAccount}\"?",
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                await cashDayService.CloseDayAsync(cashDesk.Id, cashDesk.DisplayNameWithAccount, cashDate, CurrentUserName());

                StatusText.Text = $"Кассовый день {cashDate:dd.MM.yyyy} закрыт";
                MessageBox.Show("Кассовый день закрыт.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка закрытия кассового дня: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnOpenCashDayClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Открытие кассового дня";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            if (!await ConfirmAdminPasswordAsync(caption))
                return;

            var cashDate = (PeriodEndDatePicker.SelectedDate ?? DateTime.Today).Date;
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var affected = await cashDayService.OpenDayAsync(cashDesk.Id, cashDate, CurrentUserName());

                if (affected == 0)
                {
                    MessageBox.Show("Закрытый день для выбранной кассы и даты не найден.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusText.Text = $"Кассовый день {cashDate:dd.MM.yyyy} открыт";
                MessageBox.Show("Кассовый день открыт.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия кассового дня: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnClosedDaysTurnoversClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Обороты по закрытым дням";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk) || !TryGetCurrentPeriod(caption, out var startDate, out var endDate))
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var closedDates = await cashDayService.GetClosedDatesAsync(cashDesk.Id, startDate, endDate);

                if (closedDates.Count == 0)
                {
                    MessageBox.Show("За выбранный период закрытые кассовые дни не найдены.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var closedDateSet = closedDates.Select(date => date.Date).ToHashSet();
                var postings = await LoadCashPostingsAsync(cashDesk, startDate, endDate);
                var closedPostings = postings.Where(posting => closedDateSet.Contains(posting.Date.Date)).ToList();

                var dialog = new DocumentPostingsDialog("Обороты по закрытым дням", $"{cashDesk.DisplayNameWithAccount} за {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}", closedPostings)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Закрытых дней: {closedDates.Count}, проводок: {closedPostings.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка расчета оборотов по закрытым дням: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void OnDayTurnoversClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Обороты за день";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            var cashDate = (PeriodEndDatePicker.SelectedDate ?? DateTime.Today).Date;
            try
            {
                var postings = await LoadCashPostingsAsync(cashDesk, cashDate, cashDate);
                var dialog = new DocumentPostingsDialog("Обороты за день", $"{cashDesk.DisplayNameWithAccount} за {cashDate:dd.MM.yyyy}", postings)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Обороты за день: {postings.Count} проводок";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка расчета оборотов за день: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnCashBookClick(object sender, RoutedEventArgs e)
        {
            await PreviewCashFrxReportAsync(CashBookReportCode, "Кассовая книга");
        }

        private async void OnReceiptExpenseRegisterClick(object sender, RoutedEventArgs e)
        {
            await PreviewCashFrxReportAsync(ReceiptExpenseRegisterReportCode, "Реестр приходов/расходов");
        }

        private async Task PreviewCashFrxReportAsync(string reportCode, string caption)
        {
            try
            {
                var rows = GetCurrentFilteredRows();
                if (rows.Count == 0)
                {
                    MessageBox.Show("Нет строк для формирования отчета.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusText.Text = $"Формирование отчета: {caption}...";
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                await new MetadataService(context).EnsureStandardReportsAsync();

                var report = await context.Reports
                    .AsNoTracking()
                    .Include(item => item.ElementMappings)
                    .FirstOrDefaultAsync(item => item.Code == reportCode);

                if (report == null)
                {
                    MessageBox.Show($"FRX-отчет \"{caption}\" не загружен в конфигурацию.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusText.Text = "FRX-отчет не найден";
                    return;
                }

                if (!report.IsActive)
                {
                    MessageBox.Show($"FRX-отчет \"{caption}\" отключен в конфигураторе.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText.Text = "FRX-отчет отключен";
                    return;
                }

                var printFormService = new PrintFormService(context);
                var pdf = printFormService.ExportReportTemplatePreview(BuildCashOrdersDataTable(rows), report);
                var previewWindow = new PdfPreviewWindow(pdf)
                {
                    Owner = Window.GetWindow(this)
                };
                previewWindow.ShowDialog();
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка отчета";
                MessageBox.Show($"Ошибка формирования отчета \"{caption}\": {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<CashOrderRow> GetCurrentFilteredRows()
        {
            if (DataGrid.ItemsSource is IEnumerable<CashOrderRow> rows)
                return rows.ToList();

            return _allRows.ToList();
        }

        private DataTable BuildCashOrdersDataTable(IEnumerable<CashOrderRow> rows)
        {
            var table = new DataTable("cash_orders");
            AddCashOrderColumn(table, "Id", typeof(Guid));
            AddCashOrderColumn(table, "Дата", typeof(DateTime));
            AddCashOrderColumn(table, "date", typeof(DateTime));
            AddCashOrderColumn(table, "doc_date", typeof(DateTime));
            AddCashOrderColumn(table, "Документ", typeof(string));
            AddCashOrderColumn(table, "document_number", typeof(string));
            AddCashOrderColumn(table, "dok", typeof(string));
            AddCashOrderColumn(table, "Тип", typeof(string));
            AddCashOrderColumn(table, "order_type", typeof(string));
            AddCashOrderColumn(table, "Дебет", typeof(string));
            AddCashOrderColumn(table, "debit", typeof(string));
            AddCashOrderColumn(table, "Кредит", typeof(string));
            AddCashOrderColumn(table, "credit", typeof(string));
            AddCashOrderColumn(table, "Сумма", typeof(decimal));
            AddCashOrderColumn(table, "amount", typeof(decimal));
            AddCashOrderColumn(table, "sum", typeof(decimal));
            AddCashOrderColumn(table, "Сумма в валюте", typeof(decimal));
            AddCashOrderColumn(table, "amount_currency", typeof(decimal));
            AddCashOrderColumn(table, "sum_v", typeof(decimal));
            AddCashOrderColumn(table, "Валюта", typeof(string));
            AddCashOrderColumn(table, "currency", typeof(string));
            AddCashOrderColumn(table, "nval1", typeof(string));
            AddCashOrderColumn(table, "Касса", typeof(string));
            AddCashOrderColumn(table, "cash_desk", typeof(string));
            AddCashOrderColumn(table, "Основание", typeof(string));
            AddCashOrderColumn(table, "basis", typeof(string));
            AddCashOrderColumn(table, "Примечание", typeof(string));
            AddCashOrderColumn(table, "description", typeof(string));
            AddCashOrderColumn(table, "Модуль", typeof(string));
            AddCashOrderColumn(table, "module", typeof(string));

            foreach (var row in rows)
            {
                var dataRow = table.NewRow();
                SetCashOrderValue(dataRow, "Id", row.Id);
                SetCashOrderValue(dataRow, "Дата", row.DocDate);
                SetCashOrderValue(dataRow, "date", row.DocDate);
                SetCashOrderValue(dataRow, "doc_date", row.DocDate);
                SetCashOrderValue(dataRow, "Документ", row.DocNumber);
                SetCashOrderValue(dataRow, "document_number", row.DocNumber);
                SetCashOrderValue(dataRow, "dok", row.DocNumber);
                SetCashOrderValue(dataRow, "Тип", row.OrderTypeDisplay);
                SetCashOrderValue(dataRow, "order_type", row.OrderTypeDisplay);
                SetCashOrderValue(dataRow, "Дебет", ExtractAccountCode(row.DebitAccount));
                SetCashOrderValue(dataRow, "debit", ExtractAccountCode(row.DebitAccount));
                SetCashOrderValue(dataRow, "Кредит", ExtractAccountCode(row.CreditAccount));
                SetCashOrderValue(dataRow, "credit", ExtractAccountCode(row.CreditAccount));
                SetCashOrderValue(dataRow, "Сумма", row.Amount);
                SetCashOrderValue(dataRow, "amount", row.Amount);
                SetCashOrderValue(dataRow, "sum", row.Amount);
                SetCashOrderValue(dataRow, "Сумма в валюте", row.AmountInCurrency);
                SetCashOrderValue(dataRow, "amount_currency", row.AmountInCurrency);
                SetCashOrderValue(dataRow, "sum_v", row.AmountInCurrency == 0 ? row.Amount : row.AmountInCurrency);
                SetCashOrderValue(dataRow, "Валюта", row.CurrencyName);
                SetCashOrderValue(dataRow, "currency", row.CurrencyName);
                SetCashOrderValue(dataRow, "nval1", string.IsNullOrWhiteSpace(row.CurrencyName) ? "KGS" : row.CurrencyName);
                SetCashOrderValue(dataRow, "Касса", row.CashDeskName);
                SetCashOrderValue(dataRow, "cash_desk", row.CashDeskName);
                SetCashOrderValue(dataRow, "Основание", row.Basis);
                SetCashOrderValue(dataRow, "basis", row.Basis);
                SetCashOrderValue(dataRow, "Примечание", row.Description);
                SetCashOrderValue(dataRow, "description", row.Description);
                SetCashOrderValue(dataRow, "Модуль", _moduleName);
                SetCashOrderValue(dataRow, "module", _moduleName);
                table.Rows.Add(dataRow);
            }

            return table;
        }

        private static void AddCashOrderColumn(DataTable table, string name, Type type)
        {
            if (!table.Columns.Contains(name))
                table.Columns.Add(name, type);
        }

        private static void SetCashOrderValue(DataRow row, string columnName, object? value)
        {
            row[columnName] = value ?? DBNull.Value;
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Выберите документ для печати.", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var printFormService = new PrintFormService(context);
                await printFormService.SeedCashOrderFormsAsync();
                var formPrefix = selectedRow.IsReceipt ? "cash.receipt." : "cash.payment.";
                var forms = (await printFormService.GetPrintFormsAsync(_documentMetadata.Id, includeInactive: false))
                    .Where(form => form.Code.StartsWith(formPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(form => form.IsDefault)
                    .ThenBy(form => form.Name)
                    .ToList();

                if (forms.Count == 0)
                {
                    MessageBox.Show("Для выбранного документа не настроены активные печатные формы.", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Report? selectedReport;
                if (ChoosePrintFormCheckBox.IsChecked == true)
                {
                    var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                    if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                        return;

                    selectedReport = selectionDialog.SelectedReport;
                }
                else
                {
                    selectedReport = forms.FirstOrDefault(form => form.IsDefault) ?? forms.First();
                }

                StatusText.Text = "Формирование PDF...";
                var pdf = await printFormService.ExportDocumentAsync(selectedReport, selectedRow.Id);
                StatusText.Text = "PDF сформирован";

                var previewWindow = new PdfPreviewWindow(pdf) { Owner = Window.GetWindow(this) };
                previewWindow.ShowDialog();
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка печати";
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Печать", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selected)
                return;

            PostingViewModel? posting = null;
            if (selected.IsPosted)
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var postingService = new PostingService(context);
                var postings = await postingService.GetAllPostingsAsync(selected.DocDate.Date, selected.DocDate.Date);
                posting = postings.FirstOrDefault(item =>
                    item.DocumentNumber == MetadataService.NormalizeLegacyDocumentNumber(selected.DocNumber) &&
                    item.DocumentType.Equals(selected.PostingDocumentType, StringComparison.OrdinalIgnoreCase));
            }

            posting ??= new PostingViewModel
            {
                DocumentNumber = selected.DocNumber,
                Date = selected.DocDate,
                DocumentType = selected.PostingDocumentType,
                DebitAccount = selected.DebitAccount,
                CreditAccount = selected.CreditAccount,
                CorrespondentAccount = ResolveCorrespondentAccount(selected),
                Direction = selected.IsPosted ? "Проводка документа" : "Документ еще не проведен",
                Amount = selected.Amount,
                AmountCurrency = selected.AmountInCurrency,
                Currency = selected.CurrencyName,
                Organization = selected.OrganizationName,
                Employee = selected.EmployeeName,
                Note = selected.Description
            };

            var dialog = new PostingDetailsDialog(posting) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
        }

        private static string ResolveCorrespondentAccount(CashOrderRow selected)
        {
            if (selected.IsReceipt)
                return string.IsNullOrWhiteSpace(selected.CreditAccount)
                    ? selected.CorrespondentAccountName
                    : selected.CreditAccount;

            return string.IsNullOrWhiteSpace(selected.DebitAccount)
                ? selected.CorrespondentAccountName
                : selected.DebitAccount;
        }

        private static string ResolveOrderKind(Dictionary<string, object> row, string documentName)
        {
            var rawKind = GetRowString(row, "Тип КО", "order_kind", "cash_order_kind", "Тип", "document_type");
            if (rawKind.Contains("приход", StringComparison.OrdinalIgnoreCase) ||
                rawKind.Equals(CashOrderReceiptKind, StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals(CashOrderReceiptDocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return CashOrderReceiptKind;
            }

            return CashOrderPaymentKind;
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.DataContext is CashOrderRow { IsPosted: true })
                e.Row.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
        }
    }

    public class CashOrderRow
    {
        public Guid Id { get; set; }
        public Guid DocumentMetadataId { get; set; }
        public MetadataObject? DocumentMetadata { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string OrderKind { get; set; } = "Payment";
        public bool IsReceipt => OrderKind.Equals("Receipt", StringComparison.OrdinalIgnoreCase);
        public string OrderTypeDisplay => IsReceipt ? "Приходный" : "Расходный";
        public string PostingDocumentType => IsReceipt ? "Приходный кассовый ордер" : "Расходный кассовый ордер";
        public string PostingTypeDisplay => IsReceipt
            ? "Приход: Дт касса / Кт корр. счет"
            : "Расход: Дт корр. счет / Кт касса";
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string CashDeskName { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Basis { get; set; } = string.Empty;
        public string CorrespondentAccountName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string IsPostedDisplay => LocalizationService.DisplayValue(IsPosted);
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public decimal AmountInCurrency { get; set; }
        public string CashDeskId { get; set; } = string.Empty;
    }
}

