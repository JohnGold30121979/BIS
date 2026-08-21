using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
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
        private CashTurnoverSummary _currentCashTurnover = CashTurnoverSummary.Empty;
        private bool _isLoading;

        public CashOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            var today = DateTime.Today;
            PeriodStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            PeriodEndDatePicker.SelectedDate = today;
            CashDayDatePicker.SelectedDate = today;
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

            var batchRows = GetCurrentFilteredRows()
                .Where(row => row.CanBatchPost)
                .ToList();
            var hasSelectedRows = batchRows.Any(row => row.IsSelectedForBatchPost);
            BatchPostButton.IsEnabled = hasSelectedRows;

            if (SelectAllBatchPostCheckBox != null)
            {
                SelectAllBatchPostCheckBox.IsEnabled = batchRows.Count > 0;
                SelectAllBatchPostCheckBox.IsChecked = batchRows.Count > 0 && batchRows.All(row => row.IsSelectedForBatchPost);
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
            if (DataGrid.SelectedItem is CashOrderRow selected)
            {
                StatusText.Text = selected.IsPosted
                    ? $"Проведен: {selected.OrderTypeDisplay}; Дт {selected.DebitAccount} / Кт {selected.CreditAccount}, {selected.Amount:N2} сум. Двойной запись открыта проведению."
                    : $"Не проведен: {selected.OrderTypeDisplay} {selected.DocNumber}, {selected.Amount:N2} сум.";
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
                SetPostingDetail(detail, "Сумма в вал.", row.AmountInCurrency != 0m ? row.AmountInCurrency.ToString("N2") : null);
                SetPostingDetail(detail, "Валюта", row.CurrencyName);
            }

            if (showOrganization)
                SetPostingDetail(detail, "Организация", row.OrganizationName);

            if (showEmployee)
                SetPostingDetail(detail, "Сотрудник", row.EmployeeName);

            if (showMaterial)
                SetPostingDetail(detail, "Материал", row.MaterialName);

            SetPostingDetail(detail, "Статус", row.IsPosted ? "Проведен" : "Не проведен");
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

                await ApplyFiltersAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка LoadData: {ex.Message}");
                MessageBox.Show($"❌ Ошибка загрузки данных: {ex.Message}", "❌ Ошибка",
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
                DisplayName = GetRowString(row, "Название кассы", "Название", "name", "Код", "code"),
                AccountCode = CashOrderDialog.ResolveCashDeskAccountCode(
                    GetRowString(row, "Счет", "Счет кассы", "account_code", "cash_account", "Код", "code"),
                    accountAnalytics),
                CashNumber = GetRowString(row, "Номер кассы", "cash_number"),
                CurrencyName = GetRowString(row, "Валюта", "currency_id")
            };
        }

        private async Task ApplyFiltersAsync()
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
            await ApplyCashDayStatusAsync(filteredRows, startDate, endDate);
            DataGrid.ItemsSource = filteredRows;
            DataGrid.Items.Refresh();
            StatusText.Text = $"Показано записей: {filteredRows.Count} из {_allRows.Count}";
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
            await UpdateCashTurnoverSummaryAsync(startDate, endDate);
        }

        private static bool RowMatchesCashDesk(CashOrderRow row, CashDeskItem cashDesk)
        {
            if (Guid.TryParse(row.CashDeskId, out var cashDeskId))
                return cashDeskId == cashDesk.Id;

            return row.CashDeskName.Equals(cashDesk.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                   row.CashDeskName.Equals(cashDesk.DisplayNameWithAccount, StringComparison.OrdinalIgnoreCase);
        }



        private static void ResetCashDayStatus(IEnumerable<CashOrderRow> rows, string status, bool isClosed)
        {
            foreach (var row in rows)
            {
                row.CashDayStatusDisplay = status;
                row.IsCashDayClosed = isClosed;
                row.IsCashDayOpen = false;
                if (!row.CanBatchPost)
                    row.IsSelectedForBatchPost = false;
            }
        }
        private async Task ApplyCashDayStatusAsync(List<CashOrderRow> rows, DateTime? startDate, DateTime? endDate)
        {
            ResetCashDayStatus(rows, "Не открыт", false);

            if (rows.Count == 0)
                return;

            var periodStart = startDate ?? rows.Min(row => row.DocDate.Date);
            var periodEnd = endDate ?? rows.Max(row => row.DocDate.Date);
            if (periodStart > periodEnd)
            {
                ResetCashDayStatus(rows, "Период?", false);
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);

                if (CashDeskFilterCombo.SelectedItem is CashDeskItem selectedCashDesk && selectedCashDesk.Id != Guid.Empty)
                {
                    await ApplyCashDayStateAsync(rows, cashDayService, selectedCashDesk.Id);
                    return;
                }

                var rowsByCashDesk = rows
                    .Select(row => (Row: row, CashDeskId: Guid.TryParse(row.CashDeskId, out var id) ? id : Guid.Empty))
                    .Where(item => item.CashDeskId != Guid.Empty)
                    .GroupBy(item => item.CashDeskId);

                foreach (var group in rowsByCashDesk)
                    await ApplyCashDayStateAsync(group.Select(item => item.Row), cashDayService, group.Key);
            }
            catch (Exception ex)
            {
                ResetCashDayStatus(rows, "Неизвестно", false);
                SystemLogService.Error("Ошибка загрузки статусов кассовых дней.", "CashOrderWorkView.ApplyCashDayStatusAsync", ex);
            }
        }

        private static async Task ApplyCashDayStateAsync(
            IEnumerable<CashOrderRow> rows,
            CashDayClosureService cashDayService,
            Guid cashDeskId)
        {
            var rowList = rows.ToList();
            if (rowList.Count == 0)
                return;

            var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDeskId);
            var currentOpenDay = await cashDayService.GetCurrentOpenDayAsync(cashDeskId);
            foreach (var row in rowList)
            {
                row.IsCashDayClosed = lastClosedDate.HasValue && row.DocDate.Date <= lastClosedDate.Value.Date;
                row.IsCashDayOpen = !row.IsCashDayClosed && currentOpenDay != null;
                row.CashDayStatusDisplay = row.IsCashDayClosed
                    ? "Закрыт"
                    : row.IsCashDayOpen ? "Открыт" : "Не открыт";
                if (!row.CanBatchPost)
                    row.IsSelectedForBatchPost = false;
            }
        }

        private async Task UpdateCashTurnoverSummaryAsync(DateTime? startDate, DateTime? endDate)
        {
            var periodStart = startDate ?? DateTime.Today;
            var periodEnd = endDate ?? periodStart;
            var selectedCashDesk = CashDeskFilterCombo.SelectedItem as CashDeskItem;

            await UpdateOpenCashDaySummaryAsync(selectedCashDesk);

            if (selectedCashDesk is null ||
                selectedCashDesk.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                _currentCashTurnover = await CalculateAllCashTurnoverSummaryAsync(periodStart, periodEnd);
                DisplayCashTurnoverSummary(_currentCashTurnover, "Общие остатки за период по всем кассам");
                return;
            }
            if (periodStart > periodEnd)
            {
                _currentCashTurnover = CashTurnoverSummary.ForPeriod(periodStart, periodEnd, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayCashTurnoverSummary(_currentCashTurnover, "Общие остатки за период: исправьте период");
                return;
            }

            try
            {
                _currentCashTurnover = await CalculateCashTurnoverSummaryAsync(selectedCashDesk, periodStart, periodEnd);
                DisplayCashTurnoverSummary(_currentCashTurnover, $"Общие остатки за период по кассе {selectedCashDesk.DisplayNameWithAccount}");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка расчета остатков и оборотов по кассе.", "CashOrderWorkView.CashTurnover", ex);
                _currentCashTurnover = CashTurnoverSummary.ForPeriod(periodStart, periodEnd, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayCashTurnoverSummary(_currentCashTurnover, "Общие остатки за период: ошибка расчета. Подробности в системном логе.");
            }
        }

        private async Task UpdateOpenCashDaySummaryAsync(CashDeskItem? selectedCashDesk)
        {
            if (selectedCashDesk is null || selectedCashDesk.Id == Guid.Empty || string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                DisplayOpenCashDaySummary(CashTurnoverSummary.Empty, "Текущий открытый период: выберите конкретную кассу", "-");
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDay = await cashDayService.GetCurrentOpenDayAsync(selectedCashDesk.Id);

                if (openDay == null)
                {
                    var empty = CashTurnoverSummary.ForPeriod(DateTime.Today, DateTime.Today, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                    DisplayOpenCashDaySummary(empty, $"Текущий открытый период по кассе {selectedCashDesk.DisplayNameWithAccount}: не открыт", "-");
                    return;
                }

                var closeDate = GetSelectedCashDayDate();
                var openDaySummary = await CalculateCurrentOpenCashDaySummaryAsync(selectedCashDesk, cashDayService, closeDate);
                var dateText = openDaySummary.StartDate.Date == openDaySummary.EndDate.Date
                    ? openDaySummary.EndDate.ToString("dd.MM.yyyy")
                    : $"{openDaySummary.StartDate:dd.MM.yyyy}-{openDaySummary.EndDate:dd.MM.yyyy}";
                DisplayOpenCashDaySummary(openDaySummary, $"Текущий открытый период по кассе {selectedCashDesk.DisplayNameWithAccount}", dateText);
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка расчета текущего открытого кассового дня.", "CashOrderWorkView.OpenCashDaySummary", ex);
                var fallback = CashTurnoverSummary.ForPeriod(DateTime.Today, DateTime.Today, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayOpenCashDaySummary(fallback, "Текущий открытый период: ошибка расчета. Подробности в системном логе.", "-");
            }
        }

        private DateTime GetSelectedCashDayDate() => (CashDayDatePicker?.SelectedDate ?? DateTime.Today).Date;

        private void DisplayOpenCashDaySummary(CashTurnoverSummary summary, string hint, string dateText)
        {
            OpenCashDayHintText.Text = hint;
            OpenCashDayDateText.Text = dateText;
            OpenCashOpeningDebitText.Text = FormatCashAmount(summary.OpeningDebit);
            OpenCashOpeningCreditText.Text = FormatCashAmount(summary.OpeningCredit);
            OpenCashDebitTurnoverText.Text = FormatCashAmount(summary.DebitTurnover);
            OpenCashCreditTurnoverText.Text = FormatCashAmount(summary.CreditTurnover);
            OpenCashClosingDebitText.Text = FormatCashAmount(summary.ClosingDebit);
            OpenCashClosingCreditText.Text = FormatCashAmount(summary.ClosingCredit);
        }

        private void DisplayCashTurnoverSummary(CashTurnoverSummary summary, string? hint = null)
        {
            CashTurnoverHintText.Text = hint ?? $"Остатки по кассе {summary.CashDeskName} (счет {summary.AccountCode})";
            CashTurnoverDateText.Text = summary.StartDate.Date == summary.EndDate.Date
                ? summary.StartDate.ToString("dd.MM.yyyy")
                : $"{summary.StartDate:dd.MM.yyyy}-{summary.EndDate:dd.MM.yyyy}";
            CashOpeningDebitText.Text = FormatCashAmount(summary.OpeningDebit);
            CashOpeningCreditText.Text = FormatCashAmount(summary.OpeningCredit);
            CashDebitTurnoverText.Text = FormatCashAmount(summary.DebitTurnover);
            CashCreditTurnoverText.Text = FormatCashAmount(summary.CreditTurnover);
            CashClosingDebitText.Text = FormatCashAmount(summary.ClosingDebit);
            CashClosingCreditText.Text = FormatCashAmount(summary.ClosingCredit);
        }

        private async Task<CashTurnoverSummary> BuildCashTurnoverSummaryForReportAsync(DateTime startDate, DateTime endDate)
        {
            if (CashDeskFilterCombo.SelectedItem is CashDeskItem cashDesk &&
                cashDesk.Id != Guid.Empty &&
                !string.IsNullOrWhiteSpace(cashDesk.AccountCode))
            {
                return await CalculateCashTurnoverSummaryAsync(cashDesk, startDate, endDate);
            }

            return await CalculateAllCashTurnoverSummaryAsync(startDate, endDate);
        }

        private async Task<CashTurnoverSummary> CalculateAllCashTurnoverSummaryAsync(DateTime startDate, DateTime endDate)
        {
            var cashAccounts = _cashDeskFilterItems
                .Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.AccountCode))
                .Select(item => ExtractAccountCode(item.AccountCode))
                .Where(accountCode => !string.IsNullOrWhiteSpace(accountCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cashAccounts.Count == 0)
                return CashTurnoverSummary.ForPeriod(startDate, endDate, "Все кассы", string.Empty);

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var postings = await postingService.GetAllPostingsAsync(null, endDate.Date);
            var summaries = cashAccounts
                .Select(accountCode => CalculateCashTurnoverSummary("Все кассы", accountCode, startDate.Date, endDate.Date, postings))
                .ToList();

            var openingNet = summaries.Sum(item => item.OpeningDebit - item.OpeningCredit);
            var closingNet = summaries.Sum(item => item.ClosingDebit - item.ClosingCredit);

            return new CashTurnoverSummary
            {
                CashDeskName = "Все кассы",
                AccountCode = string.Join(", ", cashAccounts),
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                OpeningDebit = DebitSide(openingNet),
                OpeningCredit = CreditSide(openingNet),
                DebitTurnover = summaries.Sum(item => item.DebitTurnover),
                CreditTurnover = summaries.Sum(item => item.CreditTurnover),
                ClosingDebit = DebitSide(closingNet),
                ClosingCredit = CreditSide(closingNet)
            };
        }

        private async Task<CashTurnoverSummary> CalculateCurrentOpenCashDaySummaryAsync(
            CashDeskItem cashDesk,
            CashDayClosureService cashDayService,
            DateTime closeDate)
        {
            var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDesk.Id);
            var startDate = GetCurrentCashDayStartDate(cashDesk, closeDate.Date, lastClosedDate);
            return await CalculateCashTurnoverSummaryAsync(cashDesk, startDate, closeDate.Date);
        }

        private DateTime GetCurrentCashDayStartDate(CashDeskItem cashDesk, DateTime closeDate, DateTime? lastClosedDate)
        {
            if (lastClosedDate.HasValue)
                return lastClosedDate.Value.Date.AddDays(1);

            return _allRows
                .Where(row => RowMatchesCashDesk(row, cashDesk))
                .Where(row => row.DocDate.Date <= closeDate.Date)
                .Select(row => row.DocDate.Date)
                .DefaultIfEmpty(closeDate.Date)
                .Min();
        }
        private async Task<CashTurnoverSummary> CalculateCashTurnoverSummaryAsync(CashDeskItem cashDesk, DateTime startDate, DateTime endDate)
        {
            var accountCode = ExtractAccountCode(cashDesk.AccountCode);
            if (string.IsNullOrWhiteSpace(accountCode))
                return CashTurnoverSummary.ForPeriod(startDate, endDate, cashDesk.DisplayNameWithAccount, string.Empty);

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var postings = await postingService.GetAllPostingsAsync(null, endDate.Date);
            return CalculateCashTurnoverSummary(cashDesk.DisplayNameWithAccount, accountCode, startDate.Date, endDate.Date, postings);
        }

        private static CashTurnoverSummary CalculateCashTurnoverSummary(
            string cashDeskName,
            string accountCode,
            DateTime startDate,
            DateTime endDate,
            IEnumerable<PostingViewModel> postings)
        {
            var cashPostings = postings
                .Where(posting => PostingTouchesAccount(posting, accountCode))
                .ToList();

            var openingDebit = cashPostings
                .Where(posting => posting.Date.Date < startDate && AccountCodeEquals(posting.DebitAccount, accountCode))
                .Sum(posting => posting.Amount);
            var openingCredit = cashPostings
                .Where(posting => posting.Date.Date < startDate && AccountCodeEquals(posting.CreditAccount, accountCode))
                .Sum(posting => posting.Amount);
            var debitTurnover = cashPostings
                .Where(posting => posting.Date.Date >= startDate && posting.Date.Date <= endDate && AccountCodeEquals(posting.DebitAccount, accountCode))
                .Sum(posting => posting.Amount);
            var creditTurnover = cashPostings
                .Where(posting => posting.Date.Date >= startDate && posting.Date.Date <= endDate && AccountCodeEquals(posting.CreditAccount, accountCode))
                .Sum(posting => posting.Amount);

            var openingNet = openingDebit - openingCredit;
            var closingNet = openingNet + debitTurnover - creditTurnover;

            return new CashTurnoverSummary
            {
                CashDeskName = cashDeskName,
                AccountCode = accountCode,
                StartDate = startDate,
                EndDate = endDate,
                OpeningDebit = DebitSide(openingNet),
                OpeningCredit = CreditSide(openingNet),
                DebitTurnover = debitTurnover,
                CreditTurnover = creditTurnover,
                ClosingDebit = DebitSide(closingNet),
                ClosingCredit = CreditSide(closingNet)
            };
        }

        private static IReadOnlyList<KeyValuePair<string, string>> BuildCashTurnoverSummaryFields(CashTurnoverSummary summary)
        {
            return new[]
            {
                new KeyValuePair<string, string>("ДН", FormatCashAmount(summary.OpeningDebit)),
                new KeyValuePair<string, string>("КН", FormatCashAmount(summary.OpeningCredit)),
                new KeyValuePair<string, string>("Дт оборот", FormatCashAmount(summary.DebitTurnover)),
                new KeyValuePair<string, string>("Кт оборот", FormatCashAmount(summary.CreditTurnover)),
                new KeyValuePair<string, string>("ДК", FormatCashAmount(summary.ClosingDebit)),
                new KeyValuePair<string, string>("КК", FormatCashAmount(summary.ClosingCredit))
            };
        }

        private static bool PostingTouchesAccount(PostingViewModel posting, string accountCode) =>
            AccountCodeEquals(posting.DebitAccount, accountCode) || AccountCodeEquals(posting.CreditAccount, accountCode);

        private static bool AccountCodeEquals(string? value, string accountCode) =>
            string.Equals(ExtractAccountCode(value ?? string.Empty), accountCode, StringComparison.OrdinalIgnoreCase);

        private static decimal DebitSide(decimal netAmount) => netAmount > 0 ? netAmount : 0m;

        private static decimal CreditSide(decimal netAmount) => netAmount < 0 ? -netAmount : 0m;

        private static string FormatCashAmount(decimal amount) => amount.ToString("N2");
        private async void OnCashPostingsClick(object sender, RoutedEventArgs e)
        {
            if (CashDeskFilterCombo.SelectedItem is not CashDeskItem selectedCashDesk || selectedCashDesk.Id == Guid.Empty)
            {
                MessageBox.Show("Выберите конкретную кассу.", "Продажи по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                MessageBox.Show("В выбранной кассе не указан счет.", "Продажи по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = PeriodStartDatePicker.SelectedDate?.Date ?? DateTime.Today.AddMonths(-1);
            var endDate = PeriodEndDatePicker.SelectedDate?.Date ?? DateTime.Today;
            if (startDate > endDate)
            {
                MessageBox.Show("Дата начала периода не может быть больше даты окончания.", "Продажи по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText.Text = "Загрузка проводок по кассе...";
                var cashPostings = await LoadCashPostingsAsync(selectedCashDesk, startDate, endDate);
                var turnoverSummary = await CalculateCashTurnoverSummaryAsync(selectedCashDesk, startDate, endDate);

                var dialog = new DocumentPostingsDialog(
                    "Продажи по кассе",
                    $"{selectedCashDesk.DisplayNameWithAccount} за {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}",
                    cashPostings,
                    BuildCashTurnoverSummaryFields(turnoverSummary))
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Продажи по кассе: {cashPostings.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки проводок по кассе";
                MessageBox.Show($"Ошибка загрузки проводок по кассе: {ex.Message}", "Продажи по кассе",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnFilterChanged(object sender, EventArgs e)
        {
            if (_isLoading || DataGrid == null)
                return;

            await ApplyFiltersAsync();
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
                IsPosted = ReadBool(row, "Проведён", "Проведен", "is_posted"),
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

                if (value is int intValue)
                    return intValue != 0;

                if (value is long longValue)
                    return longValue != 0;

                var text = value.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (string.Equals(text, "да", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                    text == "1")
                {
                    return true;
                }

                if (string.Equals(text, "нет", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                    text == "0")
                {
                    return false;
                }

                if (bool.TryParse(text, out var parsed))
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

            if (!await EnsureCashDayAllowsDocumentAsync(selectedRow, "Редактирование кассового ордера"))
                return;

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
                    MessageBox.Show("Документ успешно обновлен!", "Успех",
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

            if (!await EnsureCashDayAllowsDocumentAsync(selectedRow, "Удаление кассового ордера"))
                return;

            var result = MessageBox.Show("Удалить выбранный документ?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selectedRow.Id);
                await LoadData();
                MessageBox.Show("Документ успешно удален!", "Успех",
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

            if (!await EnsureCashDayAllowsDocumentAsync(
                    selectedRow,
                    selectedRow.IsPosted ? "Отмена проведения кассового ордера" : "Проведение кассового ордера",
                    offerOpenDay: !selectedRow.IsPosted))
                return;

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

        private void OnBatchPostRowCheckBoxClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: CashOrderRow row } && !row.CanBatchPost)
                row.IsSelectedForBatchPost = false;

            UpdateButtonsState();
        }

        private void OnSelectAllBatchPostClick(object sender, RoutedEventArgs e)
        {
            var shouldSelect = SelectAllBatchPostCheckBox.IsChecked == true;
            foreach (var row in GetCurrentFilteredRows())
                row.IsSelectedForBatchPost = shouldSelect && row.CanBatchPost;

            DataGrid.Items.Refresh();
            UpdateButtonsState();
        }

        private async void OnBatchPostClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Массовое проведение кассовых ордеров";
            var currentRows = GetCurrentFilteredRows();
            foreach (var row in currentRows.Where(row => row.IsSelectedForBatchPost && !row.CanBatchPost))
                row.IsSelectedForBatchPost = false;

            var selectedRows = currentRows
                .Where(row => row.IsSelectedForBatchPost && row.CanBatchPost)
                .ToList();

            if (selectedRows.Count == 0)
            {
                DataGrid.Items.Refresh();
                UpdateButtonsState();
                var hasOpenUnpostedRows = currentRows.Any(row => row.CanBatchPost);
                var message = hasOpenUnpostedRows
                    ? "Отметьте непроведенные документы открытого периода для проведения."
                    : "Нет непроведенных документов открытого периода для массового проведения.";
                MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rowsWithoutCorrespondent = selectedRows
                .Where(row => !row.HasCorrespondentAccount)
                .Take(8)
                .Select(row => $"№ {row.DocNumber}")
                .ToList();
            if (rowsWithoutCorrespondent.Count > 0)
            {
                MessageBox.Show(
                    $"У выбранных документов не указан корреспондирующий счет: {string.Join(", ", rowsWithoutCorrespondent)}. Откройте документ и заполните поле 'Корр. счет', затем повторите проведение.",
                    caption,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                DataGrid.Items.Refresh();
                UpdateButtonsState();
                return;
            }

            var confirm = MessageBox.Show($"Провести выбранные документы: {selectedRows.Count}?", caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            var postedCount = 0;
            var errors = new List<string>();
            try
            {
                foreach (var row in selectedRows)
                {
                    if (!await EnsureCashDayAllowsDocumentAsync(row, caption))
                        return;

                    try
                    {
                        StatusText.Text = $"Проведение документа № {row.DocNumber}...";
                        await _metadataService.PostDocumentAsync(_documentMetadata.Id, row.Id);
                        postedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"№ {row.DocNumber}: {ex.Message}");
                        SystemLogService.Error($"Ошибка массового проведения кассового документа № {row.DocNumber}.", "CashOrderWorkView.OnBatchPostClick", ex);
                    }
                }

                await LoadData();

                if (errors.Count > 0)
                {
                    var details = string.Join(Environment.NewLine, errors.Take(5));
                    if (errors.Count > 5)
                        details += Environment.NewLine + $"... и еще ошибок: {errors.Count - 5}";
                    MessageBox.Show($"Проведено документов: {postedCount}. Ошибок: {errors.Count}.{Environment.NewLine}{details}", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show($"Проведено документов: {postedCount}.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
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

        private async Task<List<CashOrderRow>> GetUnpostedCurrentCashDayRowsAsync(
            CashDeskItem cashDesk,
            CashDayClosureService cashDayService,
            DateTime closeDate)
        {
            var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDesk.Id);
            var startDate = GetCurrentCashDayStartDate(cashDesk, closeDate.Date, lastClosedDate);

            return _allRows
                .Where(row => row.DocDate.Date >= startDate && row.DocDate.Date <= closeDate.Date)
                .Where(row => RowMatchesCashDesk(row, cashDesk))
                .Where(row => !row.IsPosted)
                .OrderBy(row => row.DocDate)
                .ThenBy(row => TryParseDocumentNumber(row.DocNumber, out var number) ? number : int.MaxValue)
                .ThenBy(row => row.DocNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildUnpostedCashDayMessage(DateTime closeDate, List<CashOrderRow> rows)
        {
            var documents = string.Join(", ", rows.Take(8).Select(row => $"{row.OrderTypeDisplay} № {row.DocNumber}"));
            if (rows.Count > 8)
                documents += $", ... и еще {rows.Count - 8}";

            return $"Нельзя закрыть кассовый период датой {closeDate:dd.MM.yyyy}: есть непроведенные документы.{Environment.NewLine}" +
                   $"Непроведенных документов: {rows.Count}.{Environment.NewLine}" +
                   $"Документы: {documents}.{Environment.NewLine}" +
                   "Сначала проведите документы или снимите отметки с лишних записей.";
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

        private async Task<bool> EnsureCashDayAllowsDocumentAsync(CashOrderRow row, string caption, bool offerOpenDay = true)
        {
            if (!Guid.TryParse(row.CashDeskId, out var cashDeskId) || cashDeskId == Guid.Empty)
            {
                MessageBox.Show("У документа не определена касса. Операция с кассовым днем невозможна.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDeskId);

                if (lastClosedDate.HasValue && row.DocDate.Date <= lastClosedDate.Value.Date)
                {
                    var closedMessage = offerOpenDay
                        ? $"Кассовый день {row.DocDate:dd.MM.yyyy} по кассе \"{row.CashDeskName}\" уже закрыт. Создание, изменение и проведение документов в закрытом периоде запрещено."
                        : $"Кассовый день {row.DocDate:dd.MM.yyyy} по кассе \"{row.CashDeskName}\" уже закрыт. Для отмены проведения сначала откройте день штатной кнопкой \"Открыть день\".";
                    MessageBox.Show(closedMessage, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var currentOpenDay = await cashDayService.GetCurrentOpenDayAsync(cashDeskId);
                if (currentOpenDay != null)
                    return true;

                if (!offerOpenDay)
                {
                    MessageBox.Show($"По кассе \"{row.CashDeskName}\" нет открытого кассового дня. Откройте день штатной кнопкой \"Открыть день\", затем повторите операцию.",
                        caption,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                var openDate = CashDayDatePicker.SelectedDate?.Date ?? DateTime.Today;
                var answer = MessageBox.Show($"По кассе \"{row.CashDeskName}\" нет открытого кассового дня. Открыть текущий кассовый день {openDate:dd.MM.yyyy} для работы?",
                    caption,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return false;

                await cashDayService.OpenDayAsync(cashDeskId, openDate, CurrentUserName());
                CashDayDatePicker.SelectedDate = openDate;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки кассового дня: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        private async Task<List<PostingViewModel>> LoadCashPostingsAsync(CashDeskItem cashDesk, DateTime startDate, DateTime endDate)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var allPostings = await postingService.GetAllPostingsAsync(startDate.Date, endDate.Date);
            var accountCode = ExtractAccountCode(cashDesk.AccountCode);

            var cashPostings = allPostings
                .Where(posting => string.Equals(ExtractAccountCode(posting.DebitAccount), accountCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ExtractAccountCode(posting.CreditAccount), accountCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(posting => posting.Date)
                .ThenBy(posting => posting.DocumentNumber)
                .ToList();

            FillCashPostingContent(cashPostings);
            return cashPostings;
        }


        private void FillCashPostingContent(IEnumerable<PostingViewModel> postings)
        {
            var cashRowsByKey = _allRows
                .GroupBy(row => BuildCashPostingKey(row.PostingDocumentType, row.DocNumber, row.DocDate))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var posting in postings)
            {
                if (!IsWeakCashPostingNote(posting.Note) ||
                    !cashRowsByKey.TryGetValue(BuildCashPostingKey(posting.DocumentType, posting.DocumentNumber, posting.Date), out var row))
                {
                    continue;
                }

                posting.Note = BuildCashPostingContent(row);
            }
        }

        private static string BuildCashPostingKey(string documentType, string documentNumber, DateTime date) =>
            $"{documentType}|{MetadataService.NormalizeLegacyDocumentNumber(documentNumber)}|{date:yyyyMMdd}";

        private static string BuildCashPostingContent(CashOrderRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.Basis))
                return row.Basis;

            if (!string.IsNullOrWhiteSpace(row.Description))
                return row.Description;

            return $"{row.OrderTypeDisplay} КО №  {row.DocNumber}";
        }

        private static bool IsWeakCashPostingNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return true;

            var trimmed = note.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0)
                return false;

            var prefix = trimmed[..colonIndex].Trim();
            return prefix.StartsWith("Строка", StringComparison.OrdinalIgnoreCase) ||
                   prefix.StartsWith("Row", StringComparison.OrdinalIgnoreCase);
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

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDay = await cashDayService.GetCurrentOpenDayAsync(cashDesk.Id);
                if (openDay == null)
                {
                    MessageBox.Show("По выбранной кассе нет открытого текущего кассового дня. Перед закрытием откройте день.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var closeDate = GetSelectedCashDayDate();
                var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDesk.Id);
                if (lastClosedDate.HasValue && closeDate <= lastClosedDate.Value.Date)
                {
                    MessageBox.Show($"Дата закрытия {closeDate:dd.MM.yyyy} уже относится к закрытому кассовому периоду. Выберите дату позже последнего закрытого дня {lastClosedDate:dd.MM.yyyy}.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (await cashDayService.IsDayClosedAsync(cashDesk.Id, closeDate))
                {
                    MessageBox.Show($"Кассовый день {closeDate:dd.MM.yyyy} уже закрыт.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var unpostedRows = await GetUnpostedCurrentCashDayRowsAsync(cashDesk, cashDayService, closeDate);
                if (unpostedRows.Count > 0)
                {
                    MessageBox.Show(BuildUnpostedCashDayMessage(closeDate, unpostedRows), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var confirm = MessageBox.Show($"Закрыть открытый кассовый период датой {closeDate:dd.MM.yyyy} по кассе \"{cashDesk.DisplayNameWithAccount}\"?",
                    caption,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                    return;

                var turnoverSummary = await CalculateCurrentOpenCashDaySummaryAsync(cashDesk, cashDayService, closeDate);
                await cashDayService.CloseOpenDayAsync(
                    openDay,
                    cashDesk.DisplayNameWithAccount,
                    closeDate,
                    CurrentUserName(),
                    turnoverSummary.OpeningDebit,
                    turnoverSummary.OpeningCredit,
                    turnoverSummary.DebitTurnover,
                    turnoverSummary.CreditTurnover,
                    turnoverSummary.ClosingDebit,
                    turnoverSummary.ClosingCredit);

                await LoadData();
                StatusText.Text = $"Кассовый период закрыт датой {closeDate:dd.MM.yyyy}";
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

            var cashDate = (CashDayDatePicker.SelectedDate ?? DateTime.Today).Date;
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

                await LoadData();
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
                    MessageBox.Show("Выбранный период закрытых кассовых дней не найден.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var rows = await BuildClosedDayTurnoverRowsAsync(cashDesk, closedDates, endDate);
                var cashDeskName = string.IsNullOrWhiteSpace(cashDesk.DisplayName)
                    ? cashDesk.DisplayNameWithAccount
                    : cashDesk.DisplayName;

                var dialog = new CashClosedDaysTurnoversDialog(
                    caption,
                    $"{cashDeskName} за {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}",
                    rows)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Закрытых дней: {rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка расчета оборотов по закрытым дням: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<CashClosedDayTurnoverRow>> BuildClosedDayTurnoverRowsAsync(
            CashDeskItem cashDesk,
            IEnumerable<DateTime> closedDates,
            DateTime endDate)
        {
            var accountCode = ExtractAccountCode(cashDesk.AccountCode);
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var postings = await postingService.GetAllPostingsAsync(null, endDate.Date);

            return closedDates
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .Select(date =>
                {
                    var summary = CalculateCashTurnoverSummary(cashDesk.DisplayName, accountCode, date, date, postings);
                    return new CashClosedDayTurnoverRow
                    {
                        Date = date,
                        OpeningBalance = summary.OpeningDebit - summary.OpeningCredit,
                        DebitTurnover = summary.DebitTurnover,
                        CreditTurnover = summary.CreditTurnover,
                        ClosingBalance = summary.ClosingDebit - summary.ClosingCredit
                    };
                })
                .ToList();
        }

        private async void OnDayTurnoversClick(object sender, RoutedEventArgs e)
        {
            const string caption = "Обороты за день";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDay = await cashDayService.GetCurrentOpenDayAsync(cashDesk.Id);
                if (openDay == null)
                {
                    MessageBox.Show("По выбранной кассе нет открытого текущего кассового дня. Откройте день, чтобы посмотреть обороты.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var closeDate = GetSelectedCashDayDate();
                var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDesk.Id);
                if (lastClosedDate.HasValue && closeDate <= lastClosedDate.Value.Date)
                {
                    MessageBox.Show($"Дата {closeDate:dd.MM.yyyy} относится к закрытому кассовому периоду. Выберите дату открытого периода.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var turnoverSummary = await CalculateCurrentOpenCashDaySummaryAsync(cashDesk, cashDayService, closeDate);
                var periodText = turnoverSummary.StartDate.Date == turnoverSummary.EndDate.Date
                    ? turnoverSummary.EndDate.ToString("dd.MM.yyyy")
                    : $"{turnoverSummary.StartDate:dd.MM.yyyy}-{turnoverSummary.EndDate:dd.MM.yyyy}";
                var dialog = new CashDayTurnoverDialog(
                    caption,
                    $"{cashDesk.DisplayNameWithAccount} | открытый кассовый период {periodText}",
                    new[]
                    {
                        new CashDayTurnoverRow
                        {
                            Date = turnoverSummary.EndDate,
                            OpeningDebit = turnoverSummary.OpeningDebit,
                            OpeningCredit = turnoverSummary.OpeningCredit,
                            DebitTurnover = turnoverSummary.DebitTurnover,
                            CreditTurnover = turnoverSummary.CreditTurnover,
                            ClosingDebit = turnoverSummary.ClosingDebit,
                            ClosingCredit = turnoverSummary.ClosingCredit
                        }
                    })
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Обороты открытого периода {periodText}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка расчета оборотов за день: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void OnCashBookClick(object sender, RoutedEventArgs e)
        {
            await OpenCashBookExcelAsync();
        }

        private async void OnReceiptExpenseRegisterClick(object sender, RoutedEventArgs e)
        {
            await OpenReceiptExpenseRegisterExcelAsync();
        }

        private async Task OpenCashBookExcelAsync()
        {
            const string caption = "Кассовая книга";
            try
            {
                var rows = GetCurrentFilteredRows()
                    .OrderBy(row => row.DocDate)
                    .ThenBy(row => TryParseDocumentNumber(row.DocNumber, out var number) ? number : int.MaxValue)
                    .ThenBy(row => row.DocNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rows.Count == 0)
                {
                    MessageBox.Show("Нет строк для формирования кассовой книги.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var startDate = PeriodStartDatePicker.SelectedDate ?? rows.Min(row => row.DocDate).Date;
                var endDate = PeriodEndDatePicker.SelectedDate ?? rows.Max(row => row.DocDate).Date;
                var cashDeskName = CashDeskFilterCombo.SelectedItem is CashDeskItem cashDesk
                    ? cashDesk.DisplayNameWithAccount
                    : "Все кассы";

                var turnoverSummary = await BuildCashTurnoverSummaryForReportAsync(startDate, endDate);

                CashBookButton.IsEnabled = false;
                Cursor = Cursors.Wait;
                StatusText.Text = ChoosePrintFormCheckBox.IsChecked == true ? "Выбор формы кассовой книги..." : "Формирование Excel кассовой книги по.market конфигуратору...";
                SystemLogService.Info(
                    $"Старт формирования кассовой книги. Строк: {rows.Count}, касса: {cashDeskName}, период: {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}.",
                    "CashOrderWorkView.CashBook");

                await OpenConfiguredCashReportAsync(
                    CashBookReportCode,
                    caption,
                    BuildCashBookReportDataTable(rows, startDate, endDate, cashDeskName, turnoverSummary),
                    startDate,
                    endDate,
                    cashDeskName,
                    "cash_book",
                    "CashOrderWorkView.CashBook");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка формирования кассовой книги";
                SystemLogService.Error("Ошибка формирования кассовой книги.", "CashOrderWorkView.CashBook", ex);
                MessageBox.Show($"Ошибка формирования кассовой книги: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CashBookButton.IsEnabled = true;
                Cursor = null;
            }
        }

        private static string BuildCashBookText(CashOrderRow row)
        {
            var party = row.IsReceipt
                ? FirstNotEmpty(row.OrganizationName, row.EmployeeName, row.CashDeskName)
                : FirstNotEmpty(row.EmployeeName, row.OrganizationName, row.CashDeskName);
            var text = FirstNotEmpty(row.Basis, row.Description, row.OrderTypeDisplay);
            return string.IsNullOrWhiteSpace(party)
                ? text
                : $"{party}    {text}";
        }

        private async Task OpenReceiptExpenseRegisterExcelAsync()
        {
            const string caption = "Реестр приходов / расходов";
            try
            {
                var rows = GetCurrentFilteredRows()
                    .OrderBy(row => row.DocDate)
                    .ThenBy(row => TryParseDocumentNumber(row.DocNumber, out var number) ? number : int.MaxValue)
                    .ThenBy(row => row.DocNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rows.Count == 0)
                {
                    MessageBox.Show("Нет строк для формирования реестра.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var startDate = PeriodStartDatePicker.SelectedDate ?? rows.Min(row => row.DocDate).Date;
                var endDate = PeriodEndDatePicker.SelectedDate ?? rows.Max(row => row.DocDate).Date;
                var cashDeskName = CashDeskFilterCombo.SelectedItem is CashDeskItem cashDesk
                    ? cashDesk.DisplayNameWithAccount
                    : "Все кассы";

                var turnoverSummary = await BuildCashTurnoverSummaryForReportAsync(startDate, endDate);

                ReceiptExpenseRegisterButton.IsEnabled = false;
                Cursor = Cursors.Wait;
                StatusText.Text = ChoosePrintFormCheckBox.IsChecked == true ? "Выбор формы реестра приходов/расходов..." : "Формирование Excel-реестра приходов/расходов по market конфигуратору...";
                SystemLogService.Info(
                    $"Старт формирования реестра приходов/расходов. Строк: {rows.Count}, касса: {cashDeskName}, период: {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}.",
                    "CashOrderWorkView.ReceiptExpenseRegister");

                await OpenConfiguredCashReportAsync(
                    ReceiptExpenseRegisterReportCode,
                    caption,
                    BuildReceiptExpenseRegisterReportDataTable(rows, startDate, endDate, cashDeskName, turnoverSummary),
                    startDate,
                    endDate,
                    cashDeskName,
                    "reestr_pko_rko",
                    "CashOrderWorkView.ReceiptExpenseRegister");
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка формирования реестра приходов/расходов";
                SystemLogService.Error("Ошибка формирования реестра приходов/расходов.", "CashOrderWorkView.ReceiptExpenseRegister", ex);
                MessageBox.Show($"Ошибка формирования реестра приходов/расходов: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ReceiptExpenseRegisterButton.IsEnabled = true;
                Cursor = null;
            }
        }

        private static string BuildCashReportExcelPath(string baseName)
        {
            var directory = Path.Combine(Path.GetTempPath(), "BIS.ERP", "cash_reports");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        private static string BuildReceiptExpenseRegisterName(CashOrderRow row)
        {
            var party = row.IsReceipt
                ? FirstNotEmpty(row.OrganizationName, row.EmployeeName, row.CashDeskName)
                : FirstNotEmpty(row.EmployeeName, row.OrganizationName, row.CashDeskName);
            return string.IsNullOrWhiteSpace(party)
                ? row.OrderTypeDisplay
                : $"{row.OrderTypeDisplay}: {party}";
        }


        private async Task OpenConfiguredCashReportAsync(
            string reportCode,
            string caption,
            DataTable dataTable,
            DateTime startDate,
            DateTime endDate,
            string cashDeskName,
            string outputBaseName,
            string logSource)
        {
            using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new MetadataService(context).EnsureStandardReportsAsync();

            var report = await context.Reports
                .AsNoTracking()
                .Include(item => item.ElementMappings)
                .FirstOrDefaultAsync(item => item.Code == reportCode);

            if (report == null)
                throw new InvalidOperationException($"Отчет \"{caption}\" не загружен в конфигурации.");
            if (!report.IsActive)
                throw new InvalidOperationException($"Отчет \"{caption}\" отключен в конфигураторе.");

            var selectedReport = report;
            var selectedFormat = PrintFormOutputFormat.Excel;
            if (ChoosePrintFormCheckBox.IsChecked == true)
            {
                var selectionDialog = new PrintFormSelectionDialog(new[] { report })
                {
                    Owner = Window.GetWindow(this)
                };

                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                {
                    StatusText.Text = "Формирование отчета отменено";
                    return;
                }

                selectedReport = selectionDialog.SelectedReport;
                selectedFormat = selectionDialog.SelectedFormat;
            }

            selectedReport.SubtitleText = $"{cashDeskName}; период {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";
            var ruleService = new FoxProReportFieldRuleService(context);
            await ruleService.SeedDefaultRulesAsync();
            var rules = await ruleService.GetRulesAsync(includeInactive: false);
            var dataSnapshot = dataTable.Copy();
            var printFormService = new PrintFormService(context);
            var output = selectedFormat == PrintFormOutputFormat.Excel
                ? await Task.Run(() => printFormService.ExportReportTemplateExcel(dataSnapshot, selectedReport, rules))
                : await Task.Run(() => printFormService.ExportReportTemplatePreview(dataSnapshot, selectedReport, rules));

            var outputPath = await PrintFormOutputFileService.SaveAndOpenAsync(output, selectedReport.Name, selectedFormat);
            var formatName = PrintFormOutputFileService.GetDisplayName(selectedFormat);

            StatusText.Text = $"{caption} открыть в {formatName}: {dataTable.Rows.Count} строк";
            SystemLogService.Info($"{caption} открыть по настройкам макета ({formatName}): {outputPath}", logSource);
        }

        private DataTable BuildCashBookReportDataTable(
            IEnumerable<CashOrderRow> rows,
            DateTime startDate,
            DateTime endDate,
            string cashDeskName,
            CashTurnoverSummary turnoverSummary)
        {
            var table = new DataTable("ved2");
            AddCashReportCommonColumns(table);
            AddCashOrderColumn(table, "d_xls", typeof(DateTime));
            AddCashOrderColumn(table, "ved2.d_xls", typeof(DateTime));
            AddCashOrderColumn(table, "ved2.dok", typeof(string));
            AddCashOrderColumn(table, "ved2.nuch", typeof(string));
            AddCashOrderColumn(table, "ved2.d_nuch", typeof(string));
            AddCashOrderColumn(table, "ved2.name_kod", typeof(string));
            AddCashOrderColumn(table, "ved2.deb", typeof(decimal));
            AddCashOrderColumn(table, "ved2.cred", typeof(decimal));
            AddCashOrderColumn(table, "ved2.sum_debet", typeof(decimal));
            AddCashOrderColumn(table, "ved2.sum_credit", typeof(decimal));
            AddCashOrderColumn(table, "ved2.opening_balance", typeof(decimal));
            AddCashOrderColumn(table, "ved2.closing_balance", typeof(decimal));
            AddCashOrderColumn(table, "name_kod", typeof(string));
            AddCashOrderColumn(table, "deb", typeof(decimal));
            AddCashOrderColumn(table, "cred", typeof(decimal));

            foreach (var row in rows)
            {
                var receiptAmount = row.IsReceipt ? row.Amount : 0m;
                var paymentAmount = row.IsReceipt ? 0m : row.Amount;
                var dataRow = table.NewRow();
                FillCashReportCommonValues(dataRow, row, startDate, endDate, cashDeskName, turnoverSummary);
                SetCashOrderValue(dataRow, "d_xls", row.DocDate);
                SetCashOrderValue(dataRow, "ved2.d_xls", row.DocDate);
                SetCashOrderValue(dataRow, "ved2.dok", row.DocNumber);
                SetCashOrderValue(dataRow, "ved2.nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "ved2.d_nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "ved2.name_kod", BuildCashBookText(row));
                SetCashOrderValue(dataRow, "ved2.deb", receiptAmount);
                SetCashOrderValue(dataRow, "ved2.cred", paymentAmount);
                SetCashOrderValue(dataRow, "ved2.sum_debet", receiptAmount);
                SetCashOrderValue(dataRow, "ved2.sum_credit", paymentAmount);
                SetCashOrderValue(dataRow, "ved2.opening_balance", turnoverSummary.OpeningDebit - turnoverSummary.OpeningCredit);
                SetCashOrderValue(dataRow, "ved2.closing_balance", turnoverSummary.ClosingDebit - turnoverSummary.ClosingCredit);
                SetCashOrderValue(dataRow, "name_kod", BuildCashBookText(row));
                SetCashOrderValue(dataRow, "deb", receiptAmount);
                SetCashOrderValue(dataRow, "cred", paymentAmount);
                table.Rows.Add(dataRow);
            }

            return table;
        }

        private DataTable BuildReceiptExpenseRegisterReportDataTable(
            IEnumerable<CashOrderRow> rows,
            DateTime startDate,
            DateTime endDate,
            string cashDeskName,
            CashTurnoverSummary turnoverSummary)
        {
            var table = new DataTable("pr_ras2");
            AddCashReportCommonColumns(table);
            AddCashOrderColumn(table, "d_xls", typeof(DateTime));
            AddCashOrderColumn(table, "d_nuch", typeof(string));
            AddCashOrderColumn(table, "nuch", typeof(string));
            AddCashOrderColumn(table, "dovf", typeof(string));
            AddCashOrderColumn(table, "tex", typeof(string));
            AddCashOrderColumn(table, "deb", typeof(string));
            AddCashOrderColumn(table, "sum", typeof(decimal));
            AddCashOrderColumn(table, "pr_ras2.d_xls", typeof(DateTime));
            AddCashOrderColumn(table, "pr_ras2.dok", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.d_nuch", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.nuch", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.dovf", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.tex", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.deb", typeof(string));
            AddCashOrderColumn(table, "pr_ras2.sum", typeof(decimal));
            AddCashOrderColumn(table, "pr_ras2.sum_debet", typeof(decimal));
            AddCashOrderColumn(table, "pr_ras2.sum_credit", typeof(decimal));
            AddCashOrderColumn(table, "pr_ras2.opening_balance", typeof(decimal));
            AddCashOrderColumn(table, "pr_ras2.closing_balance", typeof(decimal));

            foreach (var row in rows)
            {
                var correspondentAccount = ExtractAccountCode(ResolveCorrespondentAccount(row));
                var party = row.IsReceipt
                    ? FirstNotEmpty(row.OrganizationName, row.EmployeeName, row.CashDeskName)
                    : FirstNotEmpty(row.EmployeeName, row.OrganizationName, row.CashDeskName);
                var basis = FirstNotEmpty(row.Basis, row.Description, row.OrderTypeDisplay);
                var dataRow = table.NewRow();
                FillCashReportCommonValues(dataRow, row, startDate, endDate, cashDeskName, turnoverSummary);
                SetCashOrderValue(dataRow, "d_xls", row.DocDate);
                SetCashOrderValue(dataRow, "d_nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "dovf", party);
                SetCashOrderValue(dataRow, "tex", basis);
                SetCashOrderValue(dataRow, "deb", correspondentAccount);
                SetCashOrderValue(dataRow, "sum", row.Amount);
                SetCashOrderValue(dataRow, "pr_ras2.d_xls", row.DocDate);
                SetCashOrderValue(dataRow, "pr_ras2.dok", row.DocNumber);
                SetCashOrderValue(dataRow, "pr_ras2.d_nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "pr_ras2.nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "pr_ras2.dovf", party);
                SetCashOrderValue(dataRow, "pr_ras2.tex", basis);
                SetCashOrderValue(dataRow, "pr_ras2.deb", correspondentAccount);
                SetCashOrderValue(dataRow, "pr_ras2.sum", row.Amount);
                SetCashOrderValue(dataRow, "pr_ras2.sum_debet", row.IsReceipt ? row.Amount : 0m);
                SetCashOrderValue(dataRow, "pr_ras2.sum_credit", row.IsReceipt ? 0m : row.Amount);
                SetCashOrderValue(dataRow, "pr_ras2.opening_balance", turnoverSummary.OpeningDebit - turnoverSummary.OpeningCredit);
                SetCashOrderValue(dataRow, "pr_ras2.closing_balance", turnoverSummary.ClosingDebit - turnoverSummary.ClosingCredit);
                table.Rows.Add(dataRow);
            }

            return table;
        }

        private static void AddCashReportCommonColumns(DataTable table)
        {
            AddCashOrderColumn(table, "Id", typeof(Guid));
            AddCashOrderColumn(table, "report_name", typeof(string));
            AddCashOrderColumn(table, "title", typeof(string));
            AddCashOrderColumn(table, "subtitle", typeof(string));
            AddCashOrderColumn(table, "period_start", typeof(DateTime));
            AddCashOrderColumn(table, "period_end", typeof(DateTime));
            AddCashOrderColumn(table, "cash_desk", typeof(string));
            AddCashOrderColumn(table, "Дата", typeof(DateTime));
            AddCashOrderColumn(table, "date", typeof(DateTime));
            AddCashOrderColumn(table, "doc_date", typeof(DateTime));
            AddCashOrderColumn(table, "document_date", typeof(DateTime));
            AddCashOrderColumn(table, "Документ", typeof(string));
            AddCashOrderColumn(table, "document_number", typeof(string));
            AddCashOrderColumn(table, "dok", typeof(string));
            AddCashOrderColumn(table, "nuch", typeof(string));
            AddCashOrderColumn(table, "d_nuch", typeof(string));
            AddCashOrderColumn(table, "Тип", typeof(string));
            AddCashOrderColumn(table, "order_type", typeof(string));
            AddCashOrderColumn(table, "debit", typeof(string));
            AddCashOrderColumn(table, "credit", typeof(string));
            AddCashOrderColumn(table, "debit_account", typeof(string));
            AddCashOrderColumn(table, "credit_account", typeof(string));
            AddCashOrderColumn(table, "schet", typeof(string));
            AddCashOrderColumn(table, "korsch", typeof(string));
            AddCashOrderColumn(table, "kor_sch", typeof(string));
            AddCashOrderColumn(table, "debit_amount", typeof(decimal));
            AddCashOrderColumn(table, "credit_amount", typeof(decimal));
            AddCashOrderColumn(table, "sum_debet", typeof(decimal));
            AddCashOrderColumn(table, "sum_debit", typeof(decimal));
            AddCashOrderColumn(table, "sum_credit", typeof(decimal));
            AddCashOrderColumn(table, "opening_balance", typeof(decimal));
            AddCashOrderColumn(table, "closing_balance", typeof(decimal));
            AddCashOrderColumn(table, "Остаток на начало", typeof(decimal));
            AddCashOrderColumn(table, "Остаток на конец", typeof(decimal));
            AddCashOrderColumn(table, "amount", typeof(decimal));
            AddCashOrderColumn(table, "amount_currency", typeof(decimal));
            AddCashOrderColumn(table, "currency", typeof(string));
            AddCashOrderColumn(table, "nval1", typeof(string));
            AddCashOrderColumn(table, "basis", typeof(string));
            AddCashOrderColumn(table, "description", typeof(string));
            AddCashOrderColumn(table, "operation_text", typeof(string));
            AddCashOrderColumn(table, "cash_account", typeof(string));
            AddCashOrderColumn(table, "correspondent_account", typeof(string));
            AddCashOrderColumn(table, "module", typeof(string));
            AddCashOrderColumn(table, "ДН", typeof(decimal));
            AddCashOrderColumn(table, "КН", typeof(decimal));
            AddCashOrderColumn(table, "ДК", typeof(decimal));
            AddCashOrderColumn(table, "КК", typeof(decimal));
            AddCashOrderColumn(table, "deb_beg", typeof(decimal));
            AddCashOrderColumn(table, "cred_beg", typeof(decimal));
            AddCashOrderColumn(table, "debsum", typeof(decimal));
            AddCashOrderColumn(table, "credsum", typeof(decimal));
            AddCashOrderColumn(table, "deb_end", typeof(decimal));
            AddCashOrderColumn(table, "cred_end", typeof(decimal));
            AddCashOrderColumn(table, "opening_debit", typeof(decimal));
            AddCashOrderColumn(table, "opening_credit", typeof(decimal));
            AddCashOrderColumn(table, "debit_turnover", typeof(decimal));
            AddCashOrderColumn(table, "credit_turnover", typeof(decimal));
            AddCashOrderColumn(table, "closing_debit", typeof(decimal));
            AddCashOrderColumn(table, "closing_credit", typeof(decimal));
            AddCashOrderColumn(table, "ost_n", typeof(decimal));
            AddCashOrderColumn(table, "ost_k", typeof(decimal));
            AddCashOrderColumn(table, "balance_start", typeof(decimal));
            AddCashOrderColumn(table, "balance_end", typeof(decimal));
        }

        private void FillCashReportCommonValues(
            DataRow dataRow,
            CashOrderRow row,
            DateTime startDate,
            DateTime endDate,
            string cashDeskName,
            CashTurnoverSummary turnoverSummary)
        {
            var debitAccount = ExtractAccountCode(row.DebitAccount);
            var creditAccount = ExtractAccountCode(row.CreditAccount);
            var correspondentAccount = ExtractAccountCode(ResolveCorrespondentAccount(row));
            var receiptAmount = row.IsReceipt ? row.Amount : 0m;
            var paymentAmount = row.IsReceipt ? 0m : row.Amount;
            var basis = FirstNotEmpty(row.Basis, row.Description, row.OrderTypeDisplay);
            var openingBalance = turnoverSummary.OpeningDebit - turnoverSummary.OpeningCredit;
            var closingBalance = turnoverSummary.ClosingDebit - turnoverSummary.ClosingCredit;

            SetCashOrderValue(dataRow, "Id", row.Id);
            SetCashOrderValue(dataRow, "report_name", "Расходный/Приходный КО");
            SetCashOrderValue(dataRow, "title", "Расходный / Приходный КО");
            SetCashOrderValue(dataRow, "subtitle", $"{cashDeskName}; период {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
            SetCashOrderValue(dataRow, "period_start", startDate);
            SetCashOrderValue(dataRow, "period_end", endDate);
            SetCashOrderValue(dataRow, "cash_desk", cashDeskName);
            SetCashOrderValue(dataRow, "Дата", row.DocDate);
            SetCashOrderValue(dataRow, "date", row.DocDate);
            SetCashOrderValue(dataRow, "doc_date", row.DocDate);
            SetCashOrderValue(dataRow, "document_date", row.DocDate);
            SetCashOrderValue(dataRow, "Документ", row.DocNumber);
            SetCashOrderValue(dataRow, "document_number", row.DocNumber);
            SetCashOrderValue(dataRow, "dok", row.DocNumber);
            SetCashOrderValue(dataRow, "nuch", row.DocNumber);
            SetCashOrderValue(dataRow, "d_nuch", row.DocNumber);
            SetCashOrderValue(dataRow, "Тип", row.OrderTypeDisplay);
            SetCashOrderValue(dataRow, "order_type", row.OrderTypeDisplay);
            SetCashOrderValue(dataRow, "debit", debitAccount);
            SetCashOrderValue(dataRow, "credit", creditAccount);
            SetCashOrderValue(dataRow, "debit_account", debitAccount);
            SetCashOrderValue(dataRow, "credit_account", creditAccount);
            SetCashOrderValue(dataRow, "schet", debitAccount);
            SetCashOrderValue(dataRow, "korsch", creditAccount);
            SetCashOrderValue(dataRow, "kor_sch", creditAccount);
            SetCashOrderValue(dataRow, "debit_amount", receiptAmount);
            SetCashOrderValue(dataRow, "credit_amount", paymentAmount);
            SetCashOrderValue(dataRow, "sum_debet", receiptAmount);
            SetCashOrderValue(dataRow, "sum_debit", receiptAmount);
            SetCashOrderValue(dataRow, "sum_credit", paymentAmount);
            SetCashOrderValue(dataRow, "opening_balance", openingBalance);
            SetCashOrderValue(dataRow, "closing_balance", closingBalance);
            SetCashOrderValue(dataRow, "Остаток на начало", openingBalance);
            SetCashOrderValue(dataRow, "Остаток на конец", closingBalance);
            SetCashOrderValue(dataRow, "amount", row.Amount);
            SetCashOrderValue(dataRow, "amount_currency", row.AmountInCurrency == 0 ? row.Amount : row.AmountInCurrency);
            SetCashOrderValue(dataRow, "currency", row.CurrencyName);
            SetCashOrderValue(dataRow, "nval1", string.IsNullOrWhiteSpace(row.CurrencyName) ? "KGS" : row.CurrencyName);
            SetCashOrderValue(dataRow, "basis", basis);
            SetCashOrderValue(dataRow, "description", row.Description);
            SetCashOrderValue(dataRow, "operation_text", row.IsReceipt ? BuildCashBookText(row) : BuildReceiptExpenseRegisterName(row));
            SetCashOrderValue(dataRow, "cash_account", row.IsReceipt ? debitAccount : creditAccount);
            SetCashOrderValue(dataRow, "correspondent_account", correspondentAccount);
            SetCashOrderValue(dataRow, "module", _moduleName);
            SetCashOrderValue(dataRow, "ДН", turnoverSummary.OpeningDebit);
            SetCashOrderValue(dataRow, "КН", turnoverSummary.OpeningCredit);
            SetCashOrderValue(dataRow, "ДК", turnoverSummary.ClosingDebit);
            SetCashOrderValue(dataRow, "КК", turnoverSummary.ClosingCredit);
            SetCashOrderValue(dataRow, "deb_beg", turnoverSummary.OpeningDebit);
            SetCashOrderValue(dataRow, "cred_beg", turnoverSummary.OpeningCredit);
            SetCashOrderValue(dataRow, "debsum", turnoverSummary.DebitTurnover);
            SetCashOrderValue(dataRow, "credsum", turnoverSummary.CreditTurnover);
            SetCashOrderValue(dataRow, "deb_end", turnoverSummary.ClosingDebit);
            SetCashOrderValue(dataRow, "cred_end", turnoverSummary.ClosingCredit);
            SetCashOrderValue(dataRow, "opening_debit", turnoverSummary.OpeningDebit);
            SetCashOrderValue(dataRow, "opening_credit", turnoverSummary.OpeningCredit);
            SetCashOrderValue(dataRow, "debit_turnover", turnoverSummary.DebitTurnover);
            SetCashOrderValue(dataRow, "credit_turnover", turnoverSummary.CreditTurnover);
            SetCashOrderValue(dataRow, "closing_debit", turnoverSummary.ClosingDebit);
            SetCashOrderValue(dataRow, "closing_credit", turnoverSummary.ClosingCredit);
            SetCashOrderValue(dataRow, "ost_n", openingBalance);
            SetCashOrderValue(dataRow, "ost_k", closingBalance);
            SetCashOrderValue(dataRow, "balance_start", openingBalance);
            SetCashOrderValue(dataRow, "balance_end", closingBalance);
        }
        private static string FirstNotEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static bool TryParseDocumentNumber(string? value, out int number)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out number);
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
                    MessageBox.Show($"FRX-отчет \"{caption}\" отключен в конфигурации.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
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
                StatusText.Text = "Формирование отчета завершено";
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
            AddCashOrderColumn(table, "nuch", typeof(string));
            AddCashOrderColumn(table, "d_nuch", typeof(string));
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
            AddCashOrderColumn(table, "ДН", typeof(decimal));
            AddCashOrderColumn(table, "КН", typeof(decimal));
            AddCashOrderColumn(table, "ДК", typeof(decimal));
            AddCashOrderColumn(table, "КК", typeof(decimal));
            AddCashOrderColumn(table, "deb_beg", typeof(decimal));
            AddCashOrderColumn(table, "cred_beg", typeof(decimal));
            AddCashOrderColumn(table, "debsum", typeof(decimal));
            AddCashOrderColumn(table, "credsum", typeof(decimal));
            AddCashOrderColumn(table, "deb_end", typeof(decimal));
            AddCashOrderColumn(table, "cred_end", typeof(decimal));
            AddCashOrderColumn(table, "opening_debit", typeof(decimal));
            AddCashOrderColumn(table, "opening_credit", typeof(decimal));
            AddCashOrderColumn(table, "debit_turnover", typeof(decimal));
            AddCashOrderColumn(table, "credit_turnover", typeof(decimal));
            AddCashOrderColumn(table, "closing_debit", typeof(decimal));
            AddCashOrderColumn(table, "closing_credit", typeof(decimal));
            AddCashOrderColumn(table, "ost_n", typeof(decimal));
            AddCashOrderColumn(table, "ost_k", typeof(decimal));
            AddCashOrderColumn(table, "balance_start", typeof(decimal));
            AddCashOrderColumn(table, "balance_end", typeof(decimal));

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
                SetCashOrderValue(dataRow, "nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "d_nuch", row.DocNumber);
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
                var forms = await printFormService.GetCashOrderPrintFormsAsync(
                    _documentMetadata.Id,
                    selectedRow.IsReceipt,
                    includeInactive: false);

                if (forms.Count == 0)
                {
                    MessageBox.Show("Для выбранного документа не найдены активные печатные формы.", "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Report? selectedReport;
                var selectedFormat = PrintFormOutputFormat.Pdf;
                if (ChoosePrintFormCheckBox.IsChecked == true)
                {
                    var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                    if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                        return;

                    selectedReport = selectionDialog.SelectedReport;
                    selectedFormat = selectionDialog.SelectedFormat;
                }
                else
                {
                    selectedReport = forms.FirstOrDefault(form => form.IsDefault) ?? forms.First();
                }

                StatusText.Text = selectedFormat == PrintFormOutputFormat.Excel
                    ? "Формирование Excel..."
                    : "Формирование PDF...";
                var output = selectedFormat == PrintFormOutputFormat.Excel
                    ? await printFormService.ExportDocumentExcelAsync(selectedReport, selectedRow.Id)
                    : await printFormService.ExportDocumentAsync(selectedReport, selectedRow.Id);
                var outputPath = await PrintFormOutputFileService.SaveAndOpenAsync(output, selectedReport.Name, selectedFormat);
                StatusText.Text = $"Открыт файл печатной формы: {outputPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка печати";
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Печать", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
                return;

            var row = FindVisualParent<DataGridRow>(source);
            if (row == null)
                return;

            row.IsSelected = true;
            DataGrid.SelectedItem = row.Item;
            DataGrid.Focus();
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
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
                Direction = selected.IsPosted ? "Проведение документа" : "Документ еще не проведен",
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
            ? "Приход: Дт касса / Кт корр. счёт"
            : "Расход: Дт корр. счёт / Кт касса";
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public bool IsCashDayClosed { get; set; }
        public bool IsCashDayOpen { get; set; }
        public string CashDayStatusDisplay { get; set; } = "Не открыт";
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
        public bool IsSelectedForBatchPost { get; set; }
        public string EffectiveCorrespondentAccount => IsReceipt ? CreditAccount : DebitAccount;
        public bool HasCorrespondentAccount => !string.IsNullOrWhiteSpace(CorrespondentAccountName);
        public bool CanBatchPost => !IsPosted && IsCashDayOpen;
        public string BatchPostAvailabilityHint
        {
            get
            {
                if (IsPosted)
                    return "Документ уже проведен.";
                if (IsCashDayClosed)
                    return "Кассовый день закрыт.";
                if (!IsCashDayOpen)
                    return "Откройте кассовый период для выбранной кассы.";
                if (!HasCorrespondentAccount)
                    return "Можно отметить, но перед проведением нужно указать корреспондирующий счет.";
                return "Отметить документ для массового проведения.";
            }
        }
        public string IsPostedDisplay => LocalizationService.DisplayValue(IsPosted);
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public decimal AmountInCurrency { get; set; }
        public string CashDeskId { get; set; } = string.Empty;
    }
    public class CashTurnoverSummary
    {
        public static CashTurnoverSummary Empty => ForPeriod(DateTime.Today, DateTime.Today, string.Empty, string.Empty);

        public string CashDeskName { get; set; } = string.Empty;
        public string AccountCode { get; set; } = string.Empty;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal DebitTurnover { get; set; }
        public decimal CreditTurnover { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }

        public static CashTurnoverSummary ForPeriod(DateTime startDate, DateTime endDate, string cashDeskName, string accountCode)
        {
            return new CashTurnoverSummary
            {
                StartDate = startDate.Date,
                EndDate = endDate.Date,
                CashDeskName = cashDeskName,
                AccountCode = accountCode
            };
        }
    }
}




