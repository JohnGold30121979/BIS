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
        private const string CashOrderDocumentName = "Р Р°СЃС…РѕРґРЅС‹Р№/РџСЂРёС…РѕРґРЅС‹Р№ РљРћ";
        private const string CashOrderReceiptKind = "Receipt";
        private const string CashOrderPaymentKind = "Payment";
        private const string CashOrderReceiptDocumentType = "РџСЂРёС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";
        private const string CashOrderPaymentDocumentType = "Р Р°СЃС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";
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
                ? "РЎРїРёСЃРѕРє РїСЂРёС…РѕРґРЅС‹С… Рё СЂР°СЃС…РѕРґРЅС‹С… РєР°СЃСЃРѕРІС‹С… РѕСЂРґРµСЂРѕРІ"
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
            PostButton.Content = selected?.IsPosted == true ? "в†© РћС‚РјРµРЅРёС‚СЊ РїСЂРѕРІРµРґРµРЅРёРµ" : "вњ… РџСЂРѕРІРµСЃС‚Рё";
            PostButton.Width = selected?.IsPosted == true ? 175 : 100;
            BatchPostButton.IsEnabled = GetCurrentFilteredRows().Any(row => row.CanBatchPost);
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
            if (DataGrid.SelectedItem is CashOrderRow selected)
            {
                StatusText.Text = selected.IsPosted
                    ? $"РџСЂРѕРІРµРґРµРЅ: {selected.OrderTypeDisplay}; Р”С‚ {selected.DebitAccount} / РљС‚ {selected.CreditAccount}, {selected.Amount:N2} СЃРѕРј. Р”РІРѕР№РЅРѕР№ С‰РµР»С‡РѕРє РѕС‚РєСЂРѕРµС‚ РїСЂРѕРІРѕРґРєСѓ."
                    : $"РќРµ РїСЂРѕРІРµРґРµРЅ: {selected.OrderTypeDisplay} {selected.DocNumber}, {selected.Amount:N2} СЃРѕРј.";
            }
        }

        private void UpdateSelectedPostingDetails()
        {
            _postingDetails.Clear();

            if (DataGrid?.SelectedItem is not CashOrderRow row)
            {
                SetDetailColumnsVisibility(false, false, false, false);
                _postingDetails.Add(PostingDetailRowFactory.Create(("Р”РѕРєСѓРјРµРЅС‚", "Р’С‹Р±РµСЂРёС‚Рµ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ РІ СЃРїРёСЃРєРµ РІС‹С€Рµ")));
                return;
            }

            var selectedSettings = new[]
            {
                _accountAnalytics.GetSettingsByCode(row.DebitAccount),
                _accountAnalytics.GetSettingsByCode(row.CreditAccount)
            };
            var showCurrency = ShouldShowPostingAnalytic("Р’Р°Р»СЋС‚Р°", "РЎРїСЂР°РІРѕС‡РЅРёРє РІР°Р»СЋС‚", selectedSettings);
            var showOrganization = ShouldShowPostingAnalytic("РћСЂРіР°РЅРёР·Р°С†РёСЏ", "РћСЂРіР°РЅРёР·Р°С†РёРё", selectedSettings);
            var showEmployee = ShouldShowPostingAnalytic("РЎРѕС‚СЂСѓРґРЅРёРє", "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)", selectedSettings);
            var showMaterial = ShouldShowPostingAnalytic("РњР°С‚РµСЂРёР°Р»", "РЎРїСЂР°РІРѕС‡РЅРёРє РјР°С‚РµСЂРёР°Р»РѕРІ", selectedSettings);
            SetDetailColumnsVisibility(showCurrency, showOrganization, showEmployee, showMaterial);

            var detail = PostingDetailRowFactory.Create();
            SetPostingDetail(detail, "Р”РѕРєСѓРјРµРЅС‚", row.DocNumber);
            SetPostingDetail(detail, "РўРёРї РґРѕРєСѓРјРµРЅС‚Р°", row.PostingDocumentType);
            SetPostingDetail(detail, "Р”Р°С‚Р°", row.DocDate.ToString("dd.MM.yyyy"));
            SetPostingDetail(detail, "РњРѕРґСѓР»СЊ", _moduleName);
            SetPostingDetail(detail, "Р”РµР±РµС‚", ExtractAccountCode(row.DebitAccount));
            SetPostingDetail(detail, "РљСЂРµРґРёС‚", ExtractAccountCode(row.CreditAccount));
            SetPostingDetail(detail, "РЎСѓРјРјР°", row.Amount.ToString("N2"));

            if (showCurrency)
            {
                SetPostingDetail(detail, "РЎСѓРјРјР° РІР°Р».", row.AmountInCurrency != 0m ? row.AmountInCurrency.ToString("N2") : null);
                SetPostingDetail(detail, "Р’Р°Р»СЋС‚Р°", row.CurrencyName);
            }

            if (showOrganization)
                SetPostingDetail(detail, "РћСЂРіР°РЅРёР·Р°С†РёСЏ", row.OrganizationName);

            if (showEmployee)
                SetPostingDetail(detail, "РЎРѕС‚СЂСѓРґРЅРёРє", row.EmployeeName);

            if (showMaterial)
                SetPostingDetail(detail, "РњР°С‚РµСЂРёР°Р»", row.MaterialName);

            SetPostingDetail(detail, "РЎС‚Р°С‚СѓСЃ", row.IsPosted ? "РџСЂРѕРІРµРґС‘РЅ" : "РќРµ РїСЂРѕРІРµРґС‘РЅ");
            SetPostingDetail(detail, "РџСЂРёРјРµС‡Р°РЅРёРµ", row.Description);
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
                StatusText.Text = "Р—Р°РіСЂСѓР·РєР° РґР°РЅРЅС‹С…...";

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
                StatusText.Text = $"вќЊ РћС€РёР±РєР°: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"РћС€РёР±РєР° LoadData: {ex.Message}");
                MessageBox.Show($"РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РґР°РЅРЅС‹С…: {ex.Message}", "РћС€РёР±РєР°",
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
                new() { Id = Guid.Empty, DisplayName = "Р’СЃРµ РєР°СЃСЃС‹" }
            };

            if (catalogsByName.TryGetValue("РљР°СЃСЃС‹", out var cashCatalog))
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
                DisplayName = GetRowString(row, "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РєР°СЃСЃС‹", "РќР°РёРјРµРЅРѕРІР°РЅРёРµ", "name", "РљРѕРґ", "code"),
                AccountCode = CashOrderDialog.ResolveCashDeskAccountCode(
                    GetRowString(row, "РЎС‡РµС‚", "РЎС‡РµС‚ РєР°СЃСЃС‹", "account_code", "cash_account", "РљРѕРґ", "code"),
                    accountAnalytics),
                CashNumber = GetRowString(row, "РќРѕРјРµСЂ РєР°СЃСЃС‹", "cash_number"),
                CurrencyName = GetRowString(row, "Р’Р°Р»СЋС‚Р°", "currency_id")
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
            StatusText.Text = $"рџ“Љ РџРѕРєР°Р·Р°РЅРѕ Р·Р°РїРёСЃРµР№: {filteredRows.Count} РёР· {_allRows.Count}";
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


        private async Task ApplyCashDayStatusAsync(List<CashOrderRow> rows, DateTime? startDate, DateTime? endDate)
        {
            ResetCashDayStatus(rows, "РќРµ РѕС‚РєСЂС‹С‚", false);

            if (rows.Count == 0)
                return;

            var periodStart = startDate ?? rows.Min(row => row.DocDate.Date);
            var periodEnd = endDate ?? rows.Max(row => row.DocDate.Date);
            if (periodStart > periodEnd)
            {
                ResetCashDayStatus(rows, "РџРµСЂРёРѕРґ?", false);
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);

                if (CashDeskFilterCombo.SelectedItem is CashDeskItem selectedCashDesk && selectedCashDesk.Id != Guid.Empty)
                {
                    var closedDates = await cashDayService.GetClosedDatesAsync(selectedCashDesk.Id, periodStart, periodEnd);
                    var openDates = await cashDayService.GetOpenDatesAsync(selectedCashDesk.Id, periodStart, periodEnd);
                    ApplyCashDayDates(rows, openDates, closedDates);
                    return;
                }

                var rowsByCashDesk = rows
                    .Select(row => (Row: row, CashDeskId: Guid.TryParse(row.CashDeskId, out var id) ? id : Guid.Empty))
                    .Where(item => item.CashDeskId != Guid.Empty)
                    .GroupBy(item => item.CashDeskId);

                foreach (var group in rowsByCashDesk)
                {
                    var closedDates = await cashDayService.GetClosedDatesAsync(group.Key, periodStart, periodEnd);
                    var openDates = await cashDayService.GetOpenDatesAsync(group.Key, periodStart, periodEnd);
                    ApplyCashDayDates(group.Select(item => item.Row), openDates, closedDates);
                }
            }
            catch (Exception ex)
            {
                ResetCashDayStatus(rows, "РќРµРёР·РІРµСЃС‚РЅРѕ", false);
                SystemLogService.Error("РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё СЃС‚Р°С‚СѓСЃРѕРІ РєР°СЃСЃРѕРІС‹С… РґРЅРµР№.", "CashOrderWorkView.ApplyCashDayStatusAsync", ex);
            }
        }

        private static void ResetCashDayStatus(IEnumerable<CashOrderRow> rows, string status, bool isClosed)
        {
            foreach (var row in rows)
            {
                row.CashDayStatusDisplay = status;
                row.IsCashDayClosed = isClosed;
                if (!row.CanBatchPost)
                    row.IsSelectedForBatchPost = false;
            }
        }

        private static void ApplyCashDayDates(
            IEnumerable<CashOrderRow> rows,
            IEnumerable<DateTime> openDates,
            IEnumerable<DateTime> closedDates)
        {
            var openSet = openDates.Select(date => date.Date).ToHashSet();
            var closedSet = closedDates.Select(date => date.Date).ToHashSet();
            foreach (var row in rows)
            {
                var rowDate = row.DocDate.Date;
                row.IsCashDayClosed = closedSet.Contains(rowDate);
                row.CashDayStatusDisplay = row.IsCashDayClosed
                    ? "Р—Р°РєСЂС‹С‚"
                    : openSet.Contains(rowDate) ? "РћС‚РєСЂС‹С‚" : "РќРµ РѕС‚РєСЂС‹С‚";
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
                DisplayCashTurnoverSummary(_currentCashTurnover, "РћР±С‰РёРµ РѕСЃС‚Р°С‚РєРё Р·Р° РїРµСЂРёРѕРґ РїРѕ РІСЃРµРј РєР°СЃСЃР°Рј");
                return;
            }
            if (periodStart > periodEnd)
            {
                _currentCashTurnover = CashTurnoverSummary.ForPeriod(periodStart, periodEnd, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayCashTurnoverSummary(_currentCashTurnover, "РћР±С‰РёРµ РѕСЃС‚Р°С‚РєРё Р·Р° РїРµСЂРёРѕРґ: РёСЃРїСЂР°РІСЊС‚Рµ РїРµСЂРёРѕРґ");
                return;
            }

            try
            {
                _currentCashTurnover = await CalculateCashTurnoverSummaryAsync(selectedCashDesk, periodStart, periodEnd);
                DisplayCashTurnoverSummary(_currentCashTurnover, $"РћР±С‰РёРµ РѕСЃС‚Р°С‚РєРё Р·Р° РїРµСЂРёРѕРґ РїРѕ РєР°СЃСЃРµ {selectedCashDesk.DisplayNameWithAccount}");
            }
            catch (Exception ex)
            {
                SystemLogService.Error("РћС€РёР±РєР° СЂР°СЃС‡РµС‚Р° РѕСЃС‚Р°С‚РєРѕРІ Рё РѕР±РѕСЂРѕС‚РѕРІ РїРѕ РєР°СЃСЃРµ.", "CashOrderWorkView.CashTurnover", ex);
                _currentCashTurnover = CashTurnoverSummary.ForPeriod(periodStart, periodEnd, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayCashTurnoverSummary(_currentCashTurnover, "РћР±С‰РёРµ РѕСЃС‚Р°С‚РєРё Р·Р° РїРµСЂРёРѕРґ: РѕС€РёР±РєР° СЂР°СЃС‡РµС‚Р°. РџРѕРґСЂРѕР±РЅРѕСЃС‚Рё РІ СЃРёСЃС‚РµРјРЅРѕРј Р»РѕРіРµ.");
            }
        }

        private async Task UpdateOpenCashDaySummaryAsync(CashDeskItem? selectedCashDesk)
        {
            if (selectedCashDesk is null || selectedCashDesk.Id == Guid.Empty || string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                DisplayOpenCashDaySummary(CashTurnoverSummary.Empty, "РўРµРєСѓС‰РёР№ РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ: РІС‹Р±РµСЂРёС‚Рµ РєРѕРЅРєСЂРµС‚РЅСѓСЋ РєР°СЃСЃСѓ", "-");
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDates = await cashDayService.GetOpenDayDatesAsync(selectedCashDesk.Id);
                var openDate = openDates.OrderByDescending(date => date.Date).FirstOrDefault();

                if (openDate == default)
                {
                    var empty = CashTurnoverSummary.ForPeriod(DateTime.Today, DateTime.Today, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                    DisplayOpenCashDaySummary(empty, $"РўРµРєСѓС‰РёР№ РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ РїРѕ РєР°СЃСЃРµ {selectedCashDesk.DisplayNameWithAccount}: РѕС‚РєСЂС‹С‚С‹С… РґРЅРµР№ РЅРµС‚", "-");
                    return;
                }

                var openDaySummary = await CalculateCashTurnoverSummaryAsync(selectedCashDesk, openDate.Date, openDate.Date);
                DisplayOpenCashDaySummary(openDaySummary, $"РўРµРєСѓС‰РёР№ РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ РїРѕ РєР°СЃСЃРµ {selectedCashDesk.DisplayNameWithAccount}", openDate.ToString("dd.MM.yyyy"));
            }
            catch (Exception ex)
            {
                SystemLogService.Error("РћС€РёР±РєР° СЂР°СЃС‡РµС‚Р° С‚РµРєСѓС‰РµРіРѕ РѕС‚РєСЂС‹С‚РѕРіРѕ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ.", "CashOrderWorkView.OpenCashDaySummary", ex);
                var fallback = CashTurnoverSummary.ForPeriod(DateTime.Today, DateTime.Today, selectedCashDesk.DisplayNameWithAccount, ExtractAccountCode(selectedCashDesk.AccountCode));
                DisplayOpenCashDaySummary(fallback, "РўРµРєСѓС‰РёР№ РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ: РѕС€РёР±РєР° СЂР°СЃС‡РµС‚Р°. РџРѕРґСЂРѕР±РЅРѕСЃС‚Рё РІ СЃРёСЃС‚РµРјРЅРѕРј Р»РѕРіРµ.", "-");
            }
        }

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
            CashTurnoverHintText.Text = hint ?? $"РћСЃС‚Р°С‚РєРё РїРѕ РєР°СЃСЃРµ {summary.CashDeskName} (СЃС‡РµС‚ {summary.AccountCode})";
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
                return CashTurnoverSummary.ForPeriod(startDate, endDate, "Р’СЃРµ РєР°СЃСЃС‹", string.Empty);

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var postings = await postingService.GetAllPostingsAsync(null, endDate.Date);
            var summaries = cashAccounts
                .Select(accountCode => CalculateCashTurnoverSummary("Р’СЃРµ РєР°СЃСЃС‹", accountCode, startDate.Date, endDate.Date, postings))
                .ToList();

            var openingNet = summaries.Sum(item => item.OpeningDebit - item.OpeningCredit);
            var closingNet = summaries.Sum(item => item.ClosingDebit - item.ClosingCredit);

            return new CashTurnoverSummary
            {
                CashDeskName = "Р’СЃРµ РєР°СЃСЃС‹",
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
                new KeyValuePair<string, string>("Р”Рќ", FormatCashAmount(summary.OpeningDebit)),
                new KeyValuePair<string, string>("РљРќ", FormatCashAmount(summary.OpeningCredit)),
                new KeyValuePair<string, string>("Р”С‚ РѕР±РѕСЂРѕС‚", FormatCashAmount(summary.DebitTurnover)),
                new KeyValuePair<string, string>("РљС‚ РѕР±РѕСЂРѕС‚", FormatCashAmount(summary.CreditTurnover)),
                new KeyValuePair<string, string>("Р”Рљ", FormatCashAmount(summary.ClosingDebit)),
                new KeyValuePair<string, string>("РљРљ", FormatCashAmount(summary.ClosingCredit))
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
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РєРѕРЅРєСЂРµС‚РЅСѓСЋ РєР°СЃСЃСѓ.", "РџСЂРѕРІРѕРґРєРё РїРѕ РєР°СЃСЃРµ",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedCashDesk.AccountCode))
            {
                MessageBox.Show("РЈ РІС‹Р±СЂР°РЅРЅРѕР№ РєР°СЃСЃС‹ РЅРµ СѓРєР°Р·Р°РЅ СЃС‡РµС‚.", "РџСЂРѕРІРѕРґРєРё РїРѕ РєР°СЃСЃРµ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startDate = PeriodStartDatePicker.SelectedDate?.Date ?? DateTime.Today.AddMonths(-1);
            var endDate = PeriodEndDatePicker.SelectedDate?.Date ?? DateTime.Today;
            if (startDate > endDate)
            {
                MessageBox.Show("Р”Р°С‚Р° РЅР°С‡Р°Р»Р° РїРµСЂРёРѕРґР° РЅРµ РјРѕР¶РµС‚ Р±С‹С‚СЊ Р±РѕР»СЊС€Рµ РґР°С‚С‹ РѕРєРѕРЅС‡Р°РЅРёСЏ.", "РџСЂРѕРІРѕРґРєРё РїРѕ РєР°СЃСЃРµ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText.Text = "Р—Р°РіСЂСѓР·РєР° РїСЂРѕРІРѕРґРѕРє РїРѕ РєР°СЃСЃРµ...";
                var cashPostings = await LoadCashPostingsAsync(selectedCashDesk, startDate, endDate);
                var turnoverSummary = await CalculateCashTurnoverSummaryAsync(selectedCashDesk, startDate, endDate);

                var dialog = new DocumentPostingsDialog(
                    "РџСЂРѕРІРѕРґРєРё РїРѕ РєР°СЃСЃРµ",
                    $"{selectedCashDesk.DisplayNameWithAccount} Р·Р° {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}",
                    cashPostings,
                    BuildCashTurnoverSummaryFields(turnoverSummary))
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"РџСЂРѕРІРѕРґРѕРє РїРѕ РєР°СЃСЃРµ: {cashPostings.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїСЂРѕРІРѕРґРѕРє РїРѕ РєР°СЃСЃРµ";
                MessageBox.Show($"РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё РїСЂРѕРІРѕРґРѕРє РїРѕ РєР°СЃСЃРµ: {ex.Message}", "РџСЂРѕРІРѕРґРєРё РїРѕ РєР°СЃСЃРµ",
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
            var chartCatalog = catalogsByName.FirstOrDefault(item => item.Key.StartsWith("РџР»Р°РЅ СЃС‡РµС‚РѕРІ", StringComparison.OrdinalIgnoreCase)).Value;
            if (chartCatalog == null)
                return;

            var accounts = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);
            var values = new Dictionary<Guid, string>();
            foreach (var account in accounts)
            {
                if (!account.TryGetValue("Id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var id))
                    continue;

                var code = GetRowString(account, "РљРѕРґ", "code");
                var name = GetRowString(account, "РќР°РёРјРµРЅРѕРІР°РЅРёРµ", "name");
                values[id] = string.IsNullOrWhiteSpace(name) ? code : $"{code} - {name}";
            }

            referenceCache["correspondent_account"] = values;
            referenceCache["РљРѕСЂСЂ. СЃС‡РµС‚"] = values;
        }

        private async Task AddCashDeskReferenceCacheAsync(
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            Dictionary<string, MetadataObject> catalogsByName,
            AccountAnalyticsRegistry accountAnalytics)
        {
            if (!catalogsByName.TryGetValue("РљР°СЃСЃС‹", out var cashCatalog))
                return;

            var cashRows = await _metadataService.GetCatalogDataAsync(cashCatalog.Id);
            var values = new Dictionary<Guid, string>();
            foreach (var cash in cashRows)
            {
                if (!cash.TryGetValue("Id", out var idValue) || !Guid.TryParse(idValue?.ToString(), out var id))
                    continue;

                var name = GetRowString(cash, "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РєР°СЃСЃС‹", "РќР°РёРјРµРЅРѕРІР°РЅРёРµ", "name", "РљРѕРґ", "code");
                var account = CashOrderDialog.ResolveCashDeskAccountCode(
                    GetRowString(cash, "РЎС‡РµС‚", "РЎС‡РµС‚ РєР°СЃСЃС‹", "code", "РљРѕРґ"),
                    accountAnalytics);
                values[id] = string.IsNullOrWhiteSpace(account) ? name : $"{name} (СЃС‡РµС‚ {account})";
            }

            referenceCache["РљР°СЃСЃР°"] = values;
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
                DocNumber = GetRowString(row, "РќРѕРјРµСЂ", "doc_number", "РќРѕРјРµСЂ РґРѕРєСѓРјРµРЅС‚Р°"),
                DocDate = ReadDate(row, "Р”Р°С‚Р°", "doc_date") ?? DateTime.Now,
                Amount = ReadDecimal(row, "РЎСѓРјРјР°", "amount"),
                Basis = GetRowString(row, "РћСЃРЅРѕРІР°РЅРёРµ", "basis"),
                Description = GetRowString(row, "РџСЂРёРјРµС‡Р°РЅРёРµ", "description"),
                IsPosted = ReadBool(row, "РџСЂРѕРІРµРґС‘РЅ", "is_posted"),
                CreatedAt = ReadDate(row, "CreatedAt") ?? DateTime.Now,
                UpdatedAt = ReadDate(row, "UpdatedAt") ?? DateTime.Now,
                DebitAccount = GetRowString(row, "Р”РµР±РµС‚", "debit_account"),
                CreditAccount = GetRowString(row, "РљСЂРµРґРёС‚", "credit_account"),
                AmountInCurrency = ReadDecimal(row, "РЎСѓРјРјР° РІ РІР°Р»СЋС‚Рµ", "amount_currency"),
                CashDeskId = GetRowString(row, "РљР°СЃСЃР°", "cash_desk_id")
            };

            result.OrganizationName = ResolveReference(row, referenceCache, "РћСЂРіР°РЅРёР·Р°С†РёСЏ", "organization_id");
            result.CurrencyName = ResolveReference(row, referenceCache, "Р’Р°Р»СЋС‚Р°", "currency_id");
            result.EmployeeName = ResolveReference(row, referenceCache, "РЎРѕС‚СЂСѓРґРЅРёРє", "employee_id");
            result.MaterialName = ResolveReference(row, referenceCache, "РњР°С‚РµСЂРёР°Р»", "material_id");
            result.CashDeskName = ResolveReference(row, referenceCache, "РљР°СЃСЃР°", "cash_desk_id");
            result.CorrespondentAccountName = ResolveReference(row, referenceCache, "РљРѕСЂСЂ. СЃС‡РµС‚", "correspondent_account");
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
            await CreateCashOrderAsync(CashOrderPaymentKind, "Р Р°СЃС…РѕРґРЅС‹Р№ РљРћ");
        }

        private async void OnAddReceiptClick(object sender, RoutedEventArgs e)
        {
            await CreateCashOrderAsync(CashOrderReceiptKind, "РџСЂРёС…РѕРґРЅС‹Р№ РљРћ");
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
                    MessageBox.Show("Р”РѕРєСѓРјРµРЅС‚ СѓСЃРїРµС€РЅРѕ РґРѕР±Р°РІР»РµРЅ!", "РЈСЃРїРµС…",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР°: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РґРѕРєСѓРјРµРЅС‚ РґР»СЏ СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёСЏ!", "Р’РЅРёРјР°РЅРёРµ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await EnsureCashDayAllowsDocumentAsync(selectedRow, "Р РµРґР°РєС‚РёСЂРѕРІР°РЅРёРµ РєР°СЃСЃРѕРІРѕРіРѕ РѕСЂРґРµСЂР°"))
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
                    MessageBox.Show("Р”РѕРєСѓРјРµРЅС‚ СѓСЃРїРµС€РЅРѕ РѕР±РЅРѕРІР»С‘РЅ!", "РЈСЃРїРµС…",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёСЏ: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РґРѕРєСѓРјРµРЅС‚ РґР»СЏ СѓРґР°Р»РµРЅРёСЏ!", "Р’РЅРёРјР°РЅРёРµ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await EnsureCashDayAllowsDocumentAsync(selectedRow, "РЈРґР°Р»РµРЅРёРµ РєР°СЃСЃРѕРІРѕРіРѕ РѕСЂРґРµСЂР°"))
                return;

            var result = MessageBox.Show("РЈРґР°Р»РёС‚СЊ РІС‹Р±СЂР°РЅРЅС‹Р№ РґРѕРєСѓРјРµРЅС‚?", "РџРѕРґС‚РІРµСЂР¶РґРµРЅРёРµ",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selectedRow.Id);
                await LoadData();
                MessageBox.Show("Р”РѕРєСѓРјРµРЅС‚ СѓСЃРїРµС€РЅРѕ СѓРґР°Р»С‘РЅ!", "РЈСЃРїРµС…",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° СѓРґР°Р»РµРЅРёСЏ: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РґРѕРєСѓРјРµРЅС‚ РґР»СЏ РїСЂРѕРІРµРґРµРЅРёСЏ!", "Р’РЅРёРјР°РЅРёРµ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!await EnsureCashDayAllowsDocumentAsync(
                    selectedRow,
                    selectedRow.IsPosted ? "РћС‚РјРµРЅР° РїСЂРѕРІРµРґРµРЅРёСЏ РєР°СЃСЃРѕРІРѕРіРѕ РѕСЂРґРµСЂР°" : "РџСЂРѕРІРµРґРµРЅРёРµ РєР°СЃСЃРѕРІРѕРіРѕ РѕСЂРґРµСЂР°",
                    offerOpenDay: !selectedRow.IsPosted))
                return;

            var actionText = selectedRow.IsPosted ? "РћС‚РјРµРЅРёС‚СЊ РїСЂРѕРІРµРґРµРЅРёРµ РІС‹Р±СЂР°РЅРЅРѕРіРѕ РґРѕРєСѓРјРµРЅС‚Р°?" : "РџСЂРѕРІРµСЃС‚Рё РІС‹Р±СЂР°РЅРЅС‹Р№ РґРѕРєСѓРјРµРЅС‚?";
            var result = MessageBox.Show(actionText, "РџРѕРґС‚РІРµСЂР¶РґРµРЅРёРµ",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText.Text = selectedRow.IsPosted ? "РћС‚РјРµРЅР° РїСЂРѕРІРµРґРµРЅРёСЏ..." : "РџСЂРѕРІРµРґРµРЅРёРµ...";
                if (selectedRow.IsPosted)
                    await _metadataService.UnpostDocumentAsync(_documentMetadata.Id, selectedRow.Id);
                else
                    await _metadataService.PostDocumentAsync(_documentMetadata.Id, selectedRow.Id);

                await LoadData();
                MessageBox.Show(selectedRow.IsPosted ? "РџСЂРѕРІРµРґРµРЅРёРµ РґРѕРєСѓРјРµРЅС‚Р° РѕС‚РјРµРЅРµРЅРѕ." : "Р”РѕРєСѓРјРµРЅС‚ СѓСЃРїРµС€РЅРѕ РїСЂРѕРІРµРґС‘РЅ!", "РЈСЃРїРµС…",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РёР·РјРµРЅРµРЅРёСЏ РїСЂРѕРІРµРґРµРЅРёСЏ: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusText.Text = "вњ… Р“РѕС‚РѕРІРѕ";
            }
        }

        private async void OnBatchPostClick(object sender, RoutedEventArgs e)
        {
            const string caption = "РњР°СЃСЃРѕРІРѕРµ РїСЂРѕРІРµРґРµРЅРёРµ РєР°СЃСЃРѕРІС‹С… РѕСЂРґРµСЂРѕРІ";
            var selectedRows = GetCurrentFilteredRows()
                .Where(row => row.IsSelectedForBatchPost)
                .ToList();

            if (selectedRows.Count == 0)
            {
                MessageBox.Show("РћС‚РјРµС‚СЊС‚Рµ РЅРµРїСЂРѕРІРµРґРµРЅРЅС‹Рµ РґРѕРєСѓРјРµРЅС‚С‹ РґР»СЏ РїСЂРѕРІРµРґРµРЅРёСЏ.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var unavailableRows = selectedRows.Where(row => !row.CanBatchPost).ToList();
            if (unavailableRows.Count > 0)
            {
                MessageBox.Show("РЎСЂРµРґРё РѕС‚РјРµС‡РµРЅРЅС‹С… РµСЃС‚СЊ СѓР¶Рµ РїСЂРѕРІРµРґРµРЅРЅС‹Рµ РґРѕРєСѓРјРµРЅС‚С‹ РёР»Рё РґРѕРєСѓРјРµРЅС‚С‹ Р·Р°РєСЂС‹С‚РѕРіРѕ РґРЅСЏ. РЎРЅРёРјРёС‚Рµ РѕС‚РјРµС‚РєРё Рё РїРѕРІС‚РѕСЂРёС‚Рµ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show($"РџСЂРѕРІРµСЃС‚Рё РІС‹Р±СЂР°РЅРЅС‹Рµ РґРѕРєСѓРјРµРЅС‚С‹: {selectedRows.Count}?", caption, MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                        StatusText.Text = $"РџСЂРѕРІРµРґРµРЅРёРµ РґРѕРєСѓРјРµРЅС‚Р° в„– {row.DocNumber}...";
                        await _metadataService.PostDocumentAsync(_documentMetadata.Id, row.Id);
                        postedCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"в„– {row.DocNumber}: {ex.Message}");
                        SystemLogService.Error($"РћС€РёР±РєР° РјР°СЃСЃРѕРІРѕРіРѕ РїСЂРѕРІРµРґРµРЅРёСЏ РєР°СЃСЃРѕРІРѕРіРѕ РґРѕРєСѓРјРµРЅС‚Р° в„– {row.DocNumber}.", "CashOrderWorkView.OnBatchPostClick", ex);
                    }
                }

                await LoadData();

                if (errors.Count > 0)
                {
                    var details = string.Join(Environment.NewLine, errors.Take(5));
                    if (errors.Count > 5)
                        details += Environment.NewLine + $"... Рё РµС‰Рµ РѕС€РёР±РѕРє: {errors.Count - 5}";
                    MessageBox.Show($"РџСЂРѕРІРµРґРµРЅРѕ РґРѕРєСѓРјРµРЅС‚РѕРІ: {postedCount}. РћС€РёР±РѕРє: {errors.Count}.{Environment.NewLine}{details}", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show($"РџСЂРѕРІРµРґРµРЅРѕ РґРѕРєСѓРјРµРЅС‚РѕРІ: {postedCount}.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                StatusText.Text = "вњ… Р“РѕС‚РѕРІРѕ";
            }
        }
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private List<CashOrderRow> GetUnpostedCashDayRows(CashDeskItem cashDesk, DateTime cashDate)
        {
            return _allRows
                .Where(row => row.DocDate.Date == cashDate.Date)
                .Where(row => RowMatchesCashDesk(row, cashDesk))
                .Where(row => !row.IsPosted)
                .OrderBy(row => row.DocNumber)
                .ToList();
        }

        private static string BuildUnpostedCashDayMessage(DateTime cashDate, List<CashOrderRow> rows)
        {
            var documents = string.Join(", ", rows.Take(8).Select(row => $"{row.OrderTypeDisplay} в„– {row.DocNumber}"));
            if (rows.Count > 8)
                documents += $", ... РµС‰Рµ {rows.Count - 8}";

            return $"РќРµР»СЊР·СЏ Р·Р°РєСЂС‹С‚СЊ РєР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {cashDate:dd.MM.yyyy}: РµСЃС‚СЊ РЅРµРїСЂРѕРІРµРґРµРЅРЅС‹Рµ РґРѕРєСѓРјРµРЅС‚С‹.{Environment.NewLine}" +
                   $"РќРµРїСЂРѕРІРµРґРµРЅРЅС‹С… РґРѕРєСѓРјРµРЅС‚РѕРІ: {rows.Count}.{Environment.NewLine}" +
                   $"Р”РѕРєСѓРјРµРЅС‚С‹: {documents}.{Environment.NewLine}" +
                   "РЎРЅР°С‡Р°Р»Р° РїСЂРѕРІРµРґРёС‚Рµ РґРѕРєСѓРјРµРЅС‚С‹ РёР»Рё СЃРЅРёРјРёС‚Рµ Р»РёС€РЅРёРµ Р·Р°РїРёСЃРё.";
        }
        private bool TryGetCurrentPeriod(string caption, out DateTime startDate, out DateTime endDate)
        {
            startDate = PeriodStartDatePicker.SelectedDate?.Date ?? DateTime.Today;
            endDate = PeriodEndDatePicker.SelectedDate?.Date ?? DateTime.Today;

            if (startDate > endDate)
            {
                MessageBox.Show("Р”Р°С‚Р° РЅР°С‡Р°Р»Р° РїРµСЂРёРѕРґР° РЅРµ РјРѕР¶РµС‚ Р±С‹С‚СЊ РїРѕР·Р¶Рµ РґР°С‚С‹ РѕРєРѕРЅС‡Р°РЅРёСЏ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private bool TryGetSelectedConcreteCashDesk(string caption, out CashDeskItem cashDesk)
        {
            cashDesk = CashDeskFilterCombo.SelectedItem as CashDeskItem ?? new CashDeskItem();
            if (cashDesk.Id == Guid.Empty)
            {
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РєРѕРЅРєСЂРµС‚РЅСѓСЋ РєР°СЃСЃСѓ.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (string.IsNullOrWhiteSpace(cashDesk.AccountCode))
            {
                MessageBox.Show("РЈ РІС‹Р±СЂР°РЅРЅРѕР№ РєР°СЃСЃС‹ РЅРµ Р·Р°РїРѕР»РЅРµРЅ СЃС‡РµС‚. РћР±РѕСЂРѕС‚С‹ РїРѕ РєР°СЃСЃРµ СЃРѕР±СЂР°С‚СЊ РЅРµР»СЊР·СЏ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private async Task<bool> EnsureCashDayAllowsDocumentAsync(CashOrderRow row, string caption, bool offerOpenDay = true)
        {
            if (!Guid.TryParse(row.CashDeskId, out var cashDeskId) || cashDeskId == Guid.Empty)
            {
                MessageBox.Show("РЈ РґРѕРєСѓРјРµРЅС‚Р° РЅРµ РѕРїСЂРµРґРµР»РµРЅР° РєР°СЃСЃР°. РћРїРµСЂР°С†РёСЏ СЃ РєР°СЃСЃРѕРІС‹Рј РґРЅРµРј РЅРµРІРѕР·РјРѕР¶РЅР°.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);

                if (await cashDayService.IsDayClosedAsync(cashDeskId, row.DocDate))
                {
                    var closedMessage = offerOpenDay
                        ? $"РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {row.DocDate:dd.MM.yyyy} РїРѕ РєР°СЃСЃРµ \"{row.CashDeskName}\" Р·Р°РєСЂС‹С‚. РЎРѕР·РґР°РЅРёРµ, РёР·РјРµРЅРµРЅРёРµ Рё РїСЂРѕРІРµРґРµРЅРёРµ РїСЂРѕРІРѕРґРѕРє РІ Р·Р°РєСЂС‹С‚РѕРј РґРЅРµ Р·Р°РїСЂРµС‰РµРЅС‹."
                        : $"РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {row.DocDate:dd.MM.yyyy} РїРѕ РєР°СЃСЃРµ \"{row.CashDeskName}\" Р·Р°РєСЂС‹С‚. Р”Р»СЏ РѕС‚РјРµРЅС‹ РїСЂРѕРІРµРґРµРЅРёСЏ СЃРЅР°С‡Р°Р»Р° РѕС‚РєСЂРѕР№С‚Рµ РґРµРЅСЊ С€С‚Р°С‚РЅРѕР№ РєРЅРѕРїРєРѕР№ \"РћС‚РєСЂС‹С‚СЊ РґРµРЅСЊ\".";
                    MessageBox.Show(closedMessage,
                        caption,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                return true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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

            return $"{row.OrderTypeDisplay} РљРћ в„– {row.DocNumber}";
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
            return prefix.StartsWith("РЎС‚СЂРѕРєР°", StringComparison.OrdinalIgnoreCase) ||
                   prefix.StartsWith("Row", StringComparison.OrdinalIgnoreCase);
        }

        private static string CurrentUserName() =>
            ServiceLocator.AuthService.CurrentUser?.Login ?? Environment.UserName;

        private async Task<bool> ConfirmAdminPasswordAsync(string caption)
        {
            if (!ServiceLocator.AuthService.IsAdmin)
            {
                MessageBox.Show("РћС‚РєСЂС‹С‚РёРµ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ РґРѕСЃС‚СѓРїРЅРѕ С‚РѕР»СЊРєРѕ Р°РґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂСѓ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var login = ServiceLocator.AuthService.CurrentUser?.Login;
            if (string.IsNullOrWhiteSpace(login))
            {
                MessageBox.Show("РќРµ СѓРґР°Р»РѕСЃСЊ РѕРїСЂРµРґРµР»РёС‚СЊ С‚РµРєСѓС‰РµРіРѕ Р°РґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР°.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var password = PromptPassword(Window.GetWindow(this), caption, "Р’РІРµРґРёС‚Рµ РїР°СЂРѕР»СЊ Р°РґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР° РґР»СЏ РѕС‚РєСЂС‹С‚РёСЏ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ:");
            if (password == null)
                return false;

            var result = await ServiceLocator.AuthService.LoginAsync(login, password);
            if (!result.Success || !ServiceLocator.AuthService.IsAdmin)
            {
                MessageBox.Show("РџР°СЂРѕР»СЊ Р°РґРјРёРЅРёСЃС‚СЂР°С‚РѕСЂР° РЅРµ РїРѕРґС‚РІРµСЂР¶РґРµРЅ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
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
                Content = "РћРљ",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 16, 8, 0),
                IsDefault = true
            };
            var cancelButton = new Button
            {
                Content = "РћС‚РјРµРЅР°",
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
            const string caption = "Р—Р°РєСЂС‹С‚РёРµ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            var cashDate = (CashDayDatePicker.SelectedDate ?? DateTime.Today).Date;
            var unpostedRows = GetUnpostedCashDayRows(cashDesk, cashDate);
            if (unpostedRows.Count > 0)
            {
                MessageBox.Show(BuildUnpostedCashDayMessage(cashDate, unpostedRows), caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show($"Р—Р°РєСЂС‹С‚СЊ РєР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {cashDate:dd.MM.yyyy} РїРѕ РєР°СЃСЃРµ \"{cashDesk.DisplayNameWithAccount}\"?",
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                if (await cashDayService.IsDayClosedAsync(cashDesk.Id, cashDate))
                {
                    MessageBox.Show("Р­С‚РѕС‚ РєР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ СѓР¶Рµ Р·Р°РєСЂС‹С‚.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!await cashDayService.IsDayOpenAsync(cashDesk.Id, cashDate))
                {
                    MessageBox.Show("РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ РЅРµ РѕС‚РєСЂС‹С‚. РџРµСЂРµРґ Р·Р°РєСЂС‹С‚РёРµРј СЃРЅР°С‡Р°Р»Р° РѕС‚РєСЂРѕР№С‚Рµ РґРµРЅСЊ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var turnoverSummary = await CalculateCashTurnoverSummaryAsync(cashDesk, cashDate, cashDate);
                await cashDayService.CloseDayAsync(
                    cashDesk.Id,
                    cashDesk.DisplayNameWithAccount,
                    cashDate,
                    CurrentUserName(),
                    turnoverSummary.OpeningDebit,
                    turnoverSummary.OpeningCredit,
                    turnoverSummary.DebitTurnover,
                    turnoverSummary.CreditTurnover,
                    turnoverSummary.ClosingDebit,
                    turnoverSummary.ClosingCredit);

                await LoadData();
                StatusText.Text = $"РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {cashDate:dd.MM.yyyy} Р·Р°РєСЂС‹С‚";
                MessageBox.Show("РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ Р·Р°РєСЂС‹С‚.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° Р·Р°РєСЂС‹С‚РёСЏ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnOpenCashDayClick(object sender, RoutedEventArgs e)
        {
            const string caption = "РћС‚РєСЂС‹С‚РёРµ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ";
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
                    MessageBox.Show("Р—Р°РєСЂС‹С‚С‹Р№ РґРµРЅСЊ РґР»СЏ РІС‹Р±СЂР°РЅРЅРѕР№ РєР°СЃСЃС‹ Рё РґР°С‚С‹ РЅРµ РЅР°Р№РґРµРЅ.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await LoadData();
                StatusText.Text = $"РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {cashDate:dd.MM.yyyy} РѕС‚РєСЂС‹С‚";
                MessageBox.Show("РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ РѕС‚РєСЂС‹С‚.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РѕС‚РєСЂС‹С‚РёСЏ РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnClosedDaysTurnoversClick(object sender, RoutedEventArgs e)
        {
            const string caption = "РћР±РѕСЂРѕС‚С‹ РїРѕ Р·Р°РєСЂС‹С‚С‹Рј РґРЅСЏРј";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk) || !TryGetCurrentPeriod(caption, out var startDate, out var endDate))
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var closedDates = await cashDayService.GetClosedDatesAsync(cashDesk.Id, startDate, endDate);

                if (closedDates.Count == 0)
                {
                    MessageBox.Show("Р—Р° РІС‹Р±СЂР°РЅРЅС‹Р№ РїРµСЂРёРѕРґ Р·Р°РєСЂС‹С‚С‹Рµ РєР°СЃСЃРѕРІС‹Рµ РґРЅРё РЅРµ РЅР°Р№РґРµРЅС‹.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var rows = await BuildClosedDayTurnoverRowsAsync(cashDesk, closedDates, endDate);
                var cashDeskName = string.IsNullOrWhiteSpace(cashDesk.DisplayName)
                    ? cashDesk.DisplayNameWithAccount
                    : cashDesk.DisplayName;

                var dialog = new CashClosedDaysTurnoversDialog(
                    caption,
                    $"{cashDeskName} Р·Р° {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}",
                    rows)
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"Р—Р°РєСЂС‹С‚С‹С… РґРЅРµР№: {rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° СЂР°СЃС‡РµС‚Р° РѕР±РѕСЂРѕС‚РѕРІ РїРѕ Р·Р°РєСЂС‹С‚С‹Рј РґРЅСЏРј: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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
            const string caption = "РћР±РѕСЂРѕС‚С‹ Р·Р° РґРµРЅСЊ";
            if (!TryGetSelectedConcreteCashDesk(caption, out var cashDesk))
                return;

            var cashDate = (CashDayDatePicker.SelectedDate ?? DateTime.Today).Date;
            try
            {
                var postings = await LoadCashPostingsAsync(cashDesk, cashDate, cashDate);
                var turnoverSummary = await CalculateCashTurnoverSummaryAsync(cashDesk, cashDate, cashDate);
                var dialog = new DocumentPostingsDialog("РћР±РѕСЂРѕС‚С‹ Р·Р° РґРµРЅСЊ", $"{cashDesk.DisplayNameWithAccount} Р·Р° {cashDate:dd.MM.yyyy}", postings, BuildCashTurnoverSummaryFields(turnoverSummary))
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();
                StatusText.Text = $"РћР±РѕСЂРѕС‚С‹ Р·Р° РґРµРЅСЊ: {postings.Count} РїСЂРѕРІРѕРґРѕРє";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° СЂР°СЃС‡РµС‚Р° РѕР±РѕСЂРѕС‚РѕРІ Р·Р° РґРµРЅСЊ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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
            const string caption = "РљР°СЃСЃРѕРІР°СЏ РєРЅРёРіР°";
            try
            {
                var rows = GetCurrentFilteredRows()
                    .OrderBy(row => row.DocDate)
                    .ThenBy(row => TryParseDocumentNumber(row.DocNumber, out var number) ? number : int.MaxValue)
                    .ThenBy(row => row.DocNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rows.Count == 0)
                {
                    MessageBox.Show("РќРµС‚ СЃС‚СЂРѕРє РґР»СЏ С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var startDate = PeriodStartDatePicker.SelectedDate ?? rows.Min(row => row.DocDate).Date;
                var endDate = PeriodEndDatePicker.SelectedDate ?? rows.Max(row => row.DocDate).Date;
                var cashDeskName = CashDeskFilterCombo.SelectedItem is CashDeskItem cashDesk
                    ? cashDesk.DisplayNameWithAccount
                    : "Р’СЃРµ РєР°СЃСЃС‹";

                var turnoverSummary = await BuildCashTurnoverSummaryForReportAsync(startDate, endDate);

                CashBookButton.IsEnabled = false;
                Cursor = Cursors.Wait;
                StatusText.Text = ChoosePrintFormCheckBox.IsChecked == true ? "Р’С‹Р±РѕСЂ С„РѕСЂРјС‹ РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё..." : "Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ Excel РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё РїРѕ РјР°РєРµС‚Сѓ РєРѕРЅС„РёРіСѓСЂР°С‚РѕСЂР°...";
                SystemLogService.Info(
                    $"РЎС‚Р°СЂС‚ С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё. РЎС‚СЂРѕРє: {rows.Count}, РєР°СЃСЃР°: {cashDeskName}, РїРµСЂРёРѕРґ: {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}.",
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
                StatusText.Text = "РћС€РёР±РєР° РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё";
                SystemLogService.Error("РћС€РёР±РєР° С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё.", "CashOrderWorkView.CashBook", ex);
                MessageBox.Show($"РћС€РёР±РєР° С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РєР°СЃСЃРѕРІРѕР№ РєРЅРёРіРё: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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
            const string caption = "Р РµРµСЃС‚СЂ РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ";
            try
            {
                var rows = GetCurrentFilteredRows()
                    .OrderBy(row => row.DocDate)
                    .ThenBy(row => TryParseDocumentNumber(row.DocNumber, out var number) ? number : int.MaxValue)
                    .ThenBy(row => row.DocNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (rows.Count == 0)
                {
                    MessageBox.Show("РќРµС‚ СЃС‚СЂРѕРє РґР»СЏ С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ СЂРµРµСЃС‚СЂР°.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var startDate = PeriodStartDatePicker.SelectedDate ?? rows.Min(row => row.DocDate).Date;
                var endDate = PeriodEndDatePicker.SelectedDate ?? rows.Max(row => row.DocDate).Date;
                var cashDeskName = CashDeskFilterCombo.SelectedItem is CashDeskItem cashDesk
                    ? cashDesk.DisplayNameWithAccount
                    : "Р’СЃРµ РєР°СЃСЃС‹";

                var turnoverSummary = await BuildCashTurnoverSummaryForReportAsync(startDate, endDate);

                ReceiptExpenseRegisterButton.IsEnabled = false;
                Cursor = Cursors.Wait;
                StatusText.Text = ChoosePrintFormCheckBox.IsChecked == true ? "Р’С‹Р±РѕСЂ С„РѕСЂРјС‹ СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ..." : "Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ Excel-СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ РїРѕ РјР°РєРµС‚Сѓ РєРѕРЅС„РёРіСѓСЂР°С‚РѕСЂР°...";
                SystemLogService.Info(
                    $"РЎС‚Р°СЂС‚ С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ. РЎС‚СЂРѕРє: {rows.Count}, РєР°СЃСЃР°: {cashDeskName}, РїРµСЂРёРѕРґ: {startDate:dd.MM.yyyy}-{endDate:dd.MM.yyyy}.",
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
                StatusText.Text = "РћС€РёР±РєР° СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ";
                SystemLogService.Error("РћС€РёР±РєР° С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ.", "CashOrderWorkView.ReceiptExpenseRegister", ex);
                MessageBox.Show($"РћС€РёР±РєР° С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ СЂРµРµСЃС‚СЂР° РїСЂРёС…РѕРґРѕРІ/СЂР°СЃС…РѕРґРѕРІ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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
                throw new InvalidOperationException($"РћС‚С‡РµС‚ \"{caption}\" РЅРµ Р·Р°РіСЂСѓР¶РµРЅ РІ РєРѕРЅС„РёРіСѓСЂР°С†РёСЋ.");
            if (!report.IsActive)
                throw new InvalidOperationException($"РћС‚С‡РµС‚ \"{caption}\" РѕС‚РєР»СЋС‡РµРЅ РІ РєРѕРЅС„РёРіСѓСЂР°С‚РѕСЂРµ.");

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
                    StatusText.Text = "Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ РѕС‚С‡РµС‚Р° РѕС‚РјРµРЅРµРЅРѕ";
                    return;
                }

                selectedReport = selectionDialog.SelectedReport;
                selectedFormat = selectionDialog.SelectedFormat;
            }

            selectedReport.SubtitleText = $"{cashDeskName}; РїРµСЂРёРѕРґ {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";
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

            StatusText.Text = $"{caption} РѕС‚РєСЂС‹С‚ РІ {formatName}: {dataTable.Rows.Count} СЃС‚СЂРѕРє";
            SystemLogService.Info($"{caption} РѕС‚РєСЂС‹С‚ РїРѕ РЅР°СЃС‚СЂР°РёРІР°РµРјРѕРјСѓ РјР°РєРµС‚Сѓ ({formatName}): {outputPath}", logSource);
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
            AddCashOrderColumn(table, "Р”Р°С‚Р°", typeof(DateTime));
            AddCashOrderColumn(table, "date", typeof(DateTime));
            AddCashOrderColumn(table, "doc_date", typeof(DateTime));
            AddCashOrderColumn(table, "document_date", typeof(DateTime));
            AddCashOrderColumn(table, "Р”РѕРєСѓРјРµРЅС‚", typeof(string));
            AddCashOrderColumn(table, "document_number", typeof(string));
            AddCashOrderColumn(table, "dok", typeof(string));
            AddCashOrderColumn(table, "nuch", typeof(string));
            AddCashOrderColumn(table, "d_nuch", typeof(string));
            AddCashOrderColumn(table, "РўРёРї", typeof(string));
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
            AddCashOrderColumn(table, "РћСЃС‚Р°С‚РѕРє РЅР° РЅР°С‡Р°Р»Рѕ", typeof(decimal));
            AddCashOrderColumn(table, "РћСЃС‚Р°С‚РѕРє РЅР° РєРѕРЅРµС†", typeof(decimal));
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
            AddCashOrderColumn(table, "Р”Рќ", typeof(decimal));
            AddCashOrderColumn(table, "РљРќ", typeof(decimal));
            AddCashOrderColumn(table, "Р”Рљ", typeof(decimal));
            AddCashOrderColumn(table, "РљРљ", typeof(decimal));
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
            SetCashOrderValue(dataRow, "report_name", "Р Р°СЃС…РѕРґРЅС‹Р№/РџСЂРёС…РѕРґРЅС‹Р№ РљРћ");
            SetCashOrderValue(dataRow, "title", "Р Р°СЃС…РѕРґРЅС‹Р№/РџСЂРёС…РѕРґРЅС‹Р№ РљРћ");
            SetCashOrderValue(dataRow, "subtitle", $"{cashDeskName}; РїРµСЂРёРѕРґ {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
            SetCashOrderValue(dataRow, "period_start", startDate);
            SetCashOrderValue(dataRow, "period_end", endDate);
            SetCashOrderValue(dataRow, "cash_desk", cashDeskName);
            SetCashOrderValue(dataRow, "Р”Р°С‚Р°", row.DocDate);
            SetCashOrderValue(dataRow, "date", row.DocDate);
            SetCashOrderValue(dataRow, "doc_date", row.DocDate);
            SetCashOrderValue(dataRow, "document_date", row.DocDate);
            SetCashOrderValue(dataRow, "Р”РѕРєСѓРјРµРЅС‚", row.DocNumber);
            SetCashOrderValue(dataRow, "document_number", row.DocNumber);
            SetCashOrderValue(dataRow, "dok", row.DocNumber);
            SetCashOrderValue(dataRow, "nuch", row.DocNumber);
            SetCashOrderValue(dataRow, "d_nuch", row.DocNumber);
            SetCashOrderValue(dataRow, "РўРёРї", row.OrderTypeDisplay);
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
            SetCashOrderValue(dataRow, "РћСЃС‚Р°С‚РѕРє РЅР° РЅР°С‡Р°Р»Рѕ", openingBalance);
            SetCashOrderValue(dataRow, "РћСЃС‚Р°С‚РѕРє РЅР° РєРѕРЅРµС†", closingBalance);
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
            SetCashOrderValue(dataRow, "Р”Рќ", turnoverSummary.OpeningDebit);
            SetCashOrderValue(dataRow, "РљРќ", turnoverSummary.OpeningCredit);
            SetCashOrderValue(dataRow, "Р”Рљ", turnoverSummary.ClosingDebit);
            SetCashOrderValue(dataRow, "РљРљ", turnoverSummary.ClosingCredit);
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
                    MessageBox.Show("РќРµС‚ СЃС‚СЂРѕРє РґР»СЏ С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РѕС‚С‡РµС‚Р°.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                StatusText.Text = $"Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ РѕС‚С‡РµС‚Р°: {caption}...";
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                await new MetadataService(context).EnsureStandardReportsAsync();

                var report = await context.Reports
                    .AsNoTracking()
                    .Include(item => item.ElementMappings)
                    .FirstOrDefaultAsync(item => item.Code == reportCode);

                if (report == null)
                {
                    MessageBox.Show($"FRX-РѕС‚С‡РµС‚ \"{caption}\" РЅРµ Р·Р°РіСЂСѓР¶РµРЅ РІ РєРѕРЅС„РёРіСѓСЂР°С†РёСЋ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusText.Text = "FRX-РѕС‚С‡РµС‚ РЅРµ РЅР°Р№РґРµРЅ";
                    return;
                }

                if (!report.IsActive)
                {
                    MessageBox.Show($"FRX-РѕС‚С‡РµС‚ \"{caption}\" РѕС‚РєР»СЋС‡РµРЅ РІ РєРѕРЅС„РёРіСѓСЂР°С‚РѕСЂРµ.", caption, MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText.Text = "FRX-РѕС‚С‡РµС‚ РѕС‚РєР»СЋС‡РµРЅ";
                    return;
                }

                var printFormService = new PrintFormService(context);
                var pdf = printFormService.ExportReportTemplatePreview(BuildCashOrdersDataTable(rows), report);
                var previewWindow = new PdfPreviewWindow(pdf)
                {
                    Owner = Window.GetWindow(this)
                };
                previewWindow.ShowDialog();
                StatusText.Text = "Р“РѕС‚РѕРІРѕ";
            }
            catch (Exception ex)
            {
                StatusText.Text = "РћС€РёР±РєР° РѕС‚С‡РµС‚Р°";
                MessageBox.Show($"РћС€РёР±РєР° С„РѕСЂРјРёСЂРѕРІР°РЅРёСЏ РѕС‚С‡РµС‚Р° \"{caption}\": {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
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
            AddCashOrderColumn(table, "Р”Р°С‚Р°", typeof(DateTime));
            AddCashOrderColumn(table, "date", typeof(DateTime));
            AddCashOrderColumn(table, "doc_date", typeof(DateTime));
            AddCashOrderColumn(table, "Р”РѕРєСѓРјРµРЅС‚", typeof(string));
            AddCashOrderColumn(table, "document_number", typeof(string));
            AddCashOrderColumn(table, "dok", typeof(string));
            AddCashOrderColumn(table, "nuch", typeof(string));
            AddCashOrderColumn(table, "d_nuch", typeof(string));
            AddCashOrderColumn(table, "РўРёРї", typeof(string));
            AddCashOrderColumn(table, "order_type", typeof(string));
            AddCashOrderColumn(table, "Р”РµР±РµС‚", typeof(string));
            AddCashOrderColumn(table, "debit", typeof(string));
            AddCashOrderColumn(table, "РљСЂРµРґРёС‚", typeof(string));
            AddCashOrderColumn(table, "credit", typeof(string));
            AddCashOrderColumn(table, "РЎСѓРјРјР°", typeof(decimal));
            AddCashOrderColumn(table, "amount", typeof(decimal));
            AddCashOrderColumn(table, "sum", typeof(decimal));
            AddCashOrderColumn(table, "РЎСѓРјРјР° РІ РІР°Р»СЋС‚Рµ", typeof(decimal));
            AddCashOrderColumn(table, "amount_currency", typeof(decimal));
            AddCashOrderColumn(table, "sum_v", typeof(decimal));
            AddCashOrderColumn(table, "Р’Р°Р»СЋС‚Р°", typeof(string));
            AddCashOrderColumn(table, "currency", typeof(string));
            AddCashOrderColumn(table, "nval1", typeof(string));
            AddCashOrderColumn(table, "РљР°СЃСЃР°", typeof(string));
            AddCashOrderColumn(table, "cash_desk", typeof(string));
            AddCashOrderColumn(table, "РћСЃРЅРѕРІР°РЅРёРµ", typeof(string));
            AddCashOrderColumn(table, "basis", typeof(string));
            AddCashOrderColumn(table, "РџСЂРёРјРµС‡Р°РЅРёРµ", typeof(string));
            AddCashOrderColumn(table, "description", typeof(string));
            AddCashOrderColumn(table, "РњРѕРґСѓР»СЊ", typeof(string));
            AddCashOrderColumn(table, "module", typeof(string));
            AddCashOrderColumn(table, "Р”Рќ", typeof(decimal));
            AddCashOrderColumn(table, "РљРќ", typeof(decimal));
            AddCashOrderColumn(table, "Р”Рљ", typeof(decimal));
            AddCashOrderColumn(table, "РљРљ", typeof(decimal));
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
                SetCashOrderValue(dataRow, "Р”Р°С‚Р°", row.DocDate);
                SetCashOrderValue(dataRow, "date", row.DocDate);
                SetCashOrderValue(dataRow, "doc_date", row.DocDate);
                SetCashOrderValue(dataRow, "Р”РѕРєСѓРјРµРЅС‚", row.DocNumber);
                SetCashOrderValue(dataRow, "document_number", row.DocNumber);
                SetCashOrderValue(dataRow, "dok", row.DocNumber);
                SetCashOrderValue(dataRow, "nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "d_nuch", row.DocNumber);
                SetCashOrderValue(dataRow, "РўРёРї", row.OrderTypeDisplay);
                SetCashOrderValue(dataRow, "order_type", row.OrderTypeDisplay);
                SetCashOrderValue(dataRow, "Р”РµР±РµС‚", ExtractAccountCode(row.DebitAccount));
                SetCashOrderValue(dataRow, "debit", ExtractAccountCode(row.DebitAccount));
                SetCashOrderValue(dataRow, "РљСЂРµРґРёС‚", ExtractAccountCode(row.CreditAccount));
                SetCashOrderValue(dataRow, "credit", ExtractAccountCode(row.CreditAccount));
                SetCashOrderValue(dataRow, "РЎСѓРјРјР°", row.Amount);
                SetCashOrderValue(dataRow, "amount", row.Amount);
                SetCashOrderValue(dataRow, "sum", row.Amount);
                SetCashOrderValue(dataRow, "РЎСѓРјРјР° РІ РІР°Р»СЋС‚Рµ", row.AmountInCurrency);
                SetCashOrderValue(dataRow, "amount_currency", row.AmountInCurrency);
                SetCashOrderValue(dataRow, "sum_v", row.AmountInCurrency == 0 ? row.Amount : row.AmountInCurrency);
                SetCashOrderValue(dataRow, "Р’Р°Р»СЋС‚Р°", row.CurrencyName);
                SetCashOrderValue(dataRow, "currency", row.CurrencyName);
                SetCashOrderValue(dataRow, "nval1", string.IsNullOrWhiteSpace(row.CurrencyName) ? "KGS" : row.CurrencyName);
                SetCashOrderValue(dataRow, "РљР°СЃСЃР°", row.CashDeskName);
                SetCashOrderValue(dataRow, "cash_desk", row.CashDeskName);
                SetCashOrderValue(dataRow, "РћСЃРЅРѕРІР°РЅРёРµ", row.Basis);
                SetCashOrderValue(dataRow, "basis", row.Basis);
                SetCashOrderValue(dataRow, "РџСЂРёРјРµС‡Р°РЅРёРµ", row.Description);
                SetCashOrderValue(dataRow, "description", row.Description);
                SetCashOrderValue(dataRow, "РњРѕРґСѓР»СЊ", _moduleName);
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
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РґРѕРєСѓРјРµРЅС‚ РґР»СЏ РїРµС‡Р°С‚Рё.", "РџРµС‡Р°С‚СЊ", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    MessageBox.Show("Р”Р»СЏ РІС‹Р±СЂР°РЅРЅРѕРіРѕ РґРѕРєСѓРјРµРЅС‚Р° РЅРµ РЅР°СЃС‚СЂРѕРµРЅС‹ Р°РєС‚РёРІРЅС‹Рµ РїРµС‡Р°С‚РЅС‹Рµ С„РѕСЂРјС‹.", "РџРµС‡Р°С‚СЊ", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    ? "Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ Excel..."
                    : "Р¤РѕСЂРјРёСЂРѕРІР°РЅРёРµ PDF...";
                var output = selectedFormat == PrintFormOutputFormat.Excel
                    ? await printFormService.ExportDocumentExcelAsync(selectedReport, selectedRow.Id)
                    : await printFormService.ExportDocumentAsync(selectedReport, selectedRow.Id);
                var outputPath = await PrintFormOutputFileService.SaveAndOpenAsync(output, selectedReport.Name, selectedFormat);
                StatusText.Text = $"РћС‚РєСЂС‹С‚ С„Р°Р№Р» РїРµС‡Р°С‚РЅРѕР№ С„РѕСЂРјС‹: {outputPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "РћС€РёР±РєР° РїРµС‡Р°С‚Рё";
                MessageBox.Show($"РћС€РёР±РєР° РїРµС‡Р°С‚Рё: {ex.Message}", "РџРµС‡Р°С‚СЊ", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Direction = selected.IsPosted ? "РџСЂРѕРІРѕРґРєР° РґРѕРєСѓРјРµРЅС‚Р°" : "Р”РѕРєСѓРјРµРЅС‚ РµС‰Рµ РЅРµ РїСЂРѕРІРµРґРµРЅ",
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
            var rawKind = GetRowString(row, "РўРёРї РљРћ", "order_kind", "cash_order_kind", "РўРёРї", "document_type");
            if (rawKind.Contains("РїСЂРёС…РѕРґ", StringComparison.OrdinalIgnoreCase) ||
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
        public string OrderTypeDisplay => IsReceipt ? "РџСЂРёС…РѕРґРЅС‹Р№" : "Р Р°СЃС…РѕРґРЅС‹Р№";
        public string PostingDocumentType => IsReceipt ? "РџСЂРёС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ" : "Р Р°СЃС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";
        public string PostingTypeDisplay => IsReceipt
            ? "РџСЂРёС…РѕРґ: Р”С‚ РєР°СЃСЃР° / РљС‚ РєРѕСЂСЂ. СЃС‡РµС‚"
            : "Р Р°СЃС…РѕРґ: Р”С‚ РєРѕСЂСЂ. СЃС‡РµС‚ / РљС‚ РєР°СЃСЃР°";
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public bool IsCashDayClosed { get; set; }
        public string CashDayStatusDisplay { get; set; } = "РќРµ РѕС‚РєСЂС‹С‚";
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
        public bool CanBatchPost => !IsPosted && !IsCashDayClosed;
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







