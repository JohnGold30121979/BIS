using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Views
{
    public partial class FinanceDocumentDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;

        /// <summary>
        /// Идентификатор сохранённого документа. Заполняется после успешного сохранения,
        /// чтобы вызывающий раздел выделил этот документ в списке.
        /// </summary>
        public Guid? SavedRecordId { get; private set; }
        private readonly FinanceDocumentKind _documentKind;

        private AccountAnalyticsRegistry _accountAnalytics = new();
        private List<ReferenceItem> _organizations = new();
        private List<ReferenceItem> _employees = new();
        private List<ReferenceItem> _currencies = new();
        private List<ReferenceItem> _advancePayments = new();
        private List<Dictionary<string, object>> _advancePaymentRows = new();
        private readonly ObservableCollection<AdvanceExpenseLineRow> _advanceExpenseLines = new();

        /// <summary>Строки затрат, подготовленные к записи в таблицу строк при сохранении.</summary>
        private List<AdvancePaymentLine> _pendingAdvanceLines = new();
        private List<Dictionary<string, object>>? _accountSelectionData;

        private object? _debitAccountValue;
        private object? _creditAccountValue;
        private object? _paymentAccountValue;
        private bool _isLoading;
        private bool _isInitialized;
        private bool _isApplyingCurrencyRate;
        private bool _isApplyingExpenseAccount;
        private bool _isCalculatingPayroll;
        private bool _isUpdatingAdvanceBalances;

        public FinanceDocumentDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            ConfigureWindowPlacement();
            _document = document;
            _metadataService = metadataService;
            _documentKind = FinanceDocumentKindHelper.FromName(document.Name);
            DialogTitle.Text = $"Добавление: {document.Name}";
            DatePicker.SelectedDate = DateTime.Today;
            SetDefaultDates();
            ConfigureMode();
            ConfigureAdvanceExpenseGrid();
            ConfigureAdvanceRuntimeBindings();
            ContentRendered += async (_, _) => await InitializeAsync();
        }

        public FinanceDocumentDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            ConfigureWindowPlacement();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            _documentKind = FinanceDocumentKindHelper.FromName(document.Name);
            DialogTitle.Text = $"Редактирование: {document.Name}";
            ConfigureMode();
            ConfigureAdvanceExpenseGrid();
            ConfigureAdvanceRuntimeBindings();
            ContentRendered += async (_, _) => await InitializeAsync(editId);
        }


        private void ConfigureWindowPlacement()
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) => CenterWindow();
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Maximized)
                    return;

                WindowState = WindowState.Normal;
                CenterWindow();
            };
        }

        private void CenterWindow()
        {
            UpdateLayout();
            var width = ActualWidth > 0 ? ActualWidth : Width;
            var height = ActualHeight > 0 ? ActualHeight : Height;

            if (Owner != null && Owner.IsVisible)
            {
                var ownerWidth = Owner.ActualWidth > 0 ? Owner.ActualWidth : Owner.Width;
                var ownerHeight = Owner.ActualHeight > 0 ? Owner.ActualHeight : Owner.Height;
                Left = Owner.Left + Math.Max(0, (ownerWidth - width) / 2);
                Top = Owner.Top + Math.Max(0, (ownerHeight - height) / 2);
                return;
            }

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
            Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        }
        private void ConfigureAdvanceRuntimeBindings()
        {
            FinanceEmployeeCombo.SelectionChanged += (_, _) => QueueAdvanceBalancesUpdate();
            DatePicker.SelectedDateChanged += (_, _) => QueueAdvanceBalancesUpdate();
        }

        private void QueueAdvanceBalancesUpdate()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport || _isLoading || !_isInitialized)
                return;

            _ = UpdateAdvanceBalancesAsync();
        }
        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isLoading || _isInitialized)
                return;

            _isLoading = true;
            try
            {
                Cursor = Cursors.Wait;
                await LoadReferenceDataAsync();

                if (editId.HasValue)
                {
                    await LoadRecordAsync(editId.Value);
                }
                else
                {
                    NumberBox.Text = await GetNextNumberAsync();
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                Cursor = null;
                UpdateAccountControlledFieldsVisibility();
                if (_documentKind == FinanceDocumentKind.AdvanceReport)
                    QueueAdvanceBalancesUpdate();
            }
        }

        private async Task LoadReferenceDataAsync()
        {
            var catalogs = await _metadataService.GetCatalogsAsync();
            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _organizations = await LoadReferenceItemsAsync(catalogs, "Организации", "Код", "Наименование");
            _employees = await LoadReferenceItemsAsync(catalogs, "Сотрудники (Списочный состав)", "Табельный номер", "ФИО");
            _currencies = await LoadReferenceItemsAsync(catalogs, "Справочник валют", "Код", "Наименование");
            _advancePaymentRows = await _metadataService.GetAdvancePaymentPairsAsync();
            // В списке — все активные пары счетов. Раньше здесь оставлялись только
            // пары с признаком сотрудников/«подотчет», из-за чего в «Пара счетов»
            // попадала единственная «3 - Расчеты с подотчетными лицами» и выбрать
            // другую пару было невозможно.
            _advancePayments = BuildAdvancePaymentReferenceItems(_advancePaymentRows);
            AdvanceCurrencyColumn.ItemsSource = _currencies;

            ReferenceComboBoxSearchHelper.Attach(OrganizationCombo, _organizations);
            ReferenceComboBoxSearchHelper.Attach(FinanceEmployeeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(AdvanceEmployeeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(PayrollEmployeeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(CurrencyCombo, _currencies);
            ReferenceComboBoxSearchHelper.Attach(AdvancePaymentCombo, _advancePayments);

            var organizationCatalog = catalogs.FirstOrDefault(catalog => catalog.Name == "Организации");
            if (organizationCatalog != null)
            {
                ReferencePickerControlFactory.AttachEditor(
                    OrganizationCombo,
                    _metadataService,
                    organizationCatalog,
                    this,
                    items => _organizations = items,
                    "Код организации",
                    "Наименование");
            }

            var employeeCatalog = catalogs.FirstOrDefault(catalog => catalog.Name == "Сотрудники (Списочный состав)");
            if (employeeCatalog != null)
            {
                ReferencePickerControlFactory.AttachEditor(
                    FinanceEmployeeCombo,
                    _metadataService,
                    employeeCatalog,
                    this,
                    items => _employees = items,
                    "Табельный номер",
                    "ФИО");
                ReferencePickerControlFactory.AttachEditor(
                    AdvanceEmployeeCombo,
                    _metadataService,
                    employeeCatalog,
                    this,
                    items => _employees = items,
                    "Табельный номер",
                    "ФИО");
                ReferencePickerControlFactory.AttachEditor(
                    PayrollEmployeeCombo,
                    _metadataService,
                    employeeCatalog,
                    this,
                    items => _employees = items,
                    "Табельный номер",
                    "ФИО");
            }

            var currencyCatalog = catalogs.FirstOrDefault(catalog => catalog.Name == "Справочник валют");
            if (currencyCatalog != null)
            {
                ReferencePickerControlFactory.AttachEditor(
                    CurrencyCombo,
                    _metadataService,
                    currencyCatalog,
                    this,
                    items => _currencies = items,
                    "Код",
                    "Наименование");
            }

            var advancePaymentCatalog = catalogs.FirstOrDefault(catalog => catalog.Name == "Пары счетов")
                ?? catalogs.FirstOrDefault(catalog => catalog.Name == "Авансовые платежи");
            if (advancePaymentCatalog != null)
            {
                ReferencePickerControlFactory.AttachEditor(
                    AdvancePaymentCombo,
                    _metadataService,
                    advancePaymentCatalog,
                    this,
                    items =>
                    {
                        _ = Dispatcher.BeginInvoke(new Action(async () => await ReloadAdvancePaymentPairsAsync()));
                    },
                    "code",
                    "name");
            }
        }

        private List<ReferenceItem> BuildAdvancePaymentReferenceItems(IEnumerable<Dictionary<string, object>> rows)
        {
            return rows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                .Select(CreateAdvancePaymentReferenceItem)
                .ToList();
        }

        private ReferenceItem CreateAdvancePaymentReferenceItem(Dictionary<string, object> row)
        {
            var item = CreateReferenceItem(row, "code", "name");
            var debitAccount = ResolveAccountCodeForCalculation(GetFirstValue(row, "debit_account", "Дебет"));
            var creditAccount = ResolveAccountCodeForCalculation(GetFirstValue(row, "credit_account", "Кредит"));
            var accounts = BuildAdvancePairAccountsDisplay(debitAccount, creditAccount);

            if (!string.IsNullOrWhiteSpace(accounts) && !item.DisplayName.Contains(accounts, StringComparison.OrdinalIgnoreCase))
                item.DisplayName = $"{item.DisplayName} ({accounts})";

            if (!string.IsNullOrWhiteSpace(debitAccount))
                item.LookupKeys.Add(debitAccount);
            if (!string.IsNullOrWhiteSpace(creditAccount))
                item.LookupKeys.Add(creditAccount);
            if (!string.IsNullOrWhiteSpace(accounts))
                item.LookupKeys.Add(accounts);
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                item.LookupKeys.Add(item.DisplayName);

            return item;
        }

        private async Task ReloadAdvancePaymentPairsAsync(Guid? selectedId = null)
        {
            try
            {
                selectedId ??= GetSelectedReferenceId(AdvancePaymentCombo);
                _advancePaymentRows = await _metadataService.GetAdvancePaymentPairsAsync();
                _advancePayments = BuildAdvancePaymentReferenceItems(_advancePaymentRows);
                ReferenceComboBoxSearchHelper.Attach(AdvancePaymentCombo, _advancePayments);

                if (selectedId.HasValue && selectedId.Value != Guid.Empty)
                    AdvancePaymentCombo.SelectedItem = _advancePayments.FirstOrDefault(item => item.Id == selectedId.Value);
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка обновления списка пар счетов.", "FinanceDocumentDialog.ReloadAdvancePaymentPairsAsync", ex);
            }
        }

        private static string BuildAdvancePairAccountsDisplay(string debitAccount, string creditAccount)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(debitAccount))
                parts.Add($"Дт {debitAccount}");
            if (!string.IsNullOrWhiteSpace(creditAccount))
                parts.Add($"Кт {creditAccount}");

            return string.Join(" / ", parts);
        }
        private async Task LoadRecordAsync(Guid recordId)
        {
            var rows = await _metadataService.GetCatalogDataAsync(_document.Id);
            var record = rows.FirstOrDefault(row =>
                TryGetGuid(row.GetValueOrDefault("Id"), out var rowId) && rowId == recordId);
            if (record == null)
                return;

            NumberBox.Text = MetadataService.NormalizeLegacyDocumentNumber(GetString(record, "Номер", "doc_number"));
            DatePicker.SelectedDate = GetDate(record, "Дата", "doc_date") ?? DateTime.Today;
            AmountBox.Text = FormatDecimal(GetDecimal(record, "Сумма", "amount"));
            BasisBox.Text = GetString(record, "Основание", "basis");
            DescriptionBox.Text = GetString(record, "Примечание", "description");
            var hasDescription = !string.IsNullOrWhiteSpace(DescriptionBox.Text);
            ShowDescriptionCheckBox.IsChecked = hasDescription;
            DescriptionBox.Visibility = hasDescription ? Visibility.Visible : Visibility.Collapsed;

            SelectComboByRecordValue(OrganizationCombo, record, "Организация", "organization_id");
            ApplyDebitAccountValue(GetFirstValue(record, "Счет дебета", "debit_account"));
            ApplyCreditAccountValue(GetFirstValue(record, "Счет кредита", "credit_account"));
            SelectComboByRecordValue(CurrencyCombo, record, "Валюта", "currency_id");
            AmountCurrencyBox.Text = FormatDecimal(GetDecimal(record, "Сумма в валюте", "amount_currency"));
            ExchangeRateBox.Text = FormatDecimal(GetDecimal(record, "Курс", "exchange_rate"));

            await LoadModeSpecificRecordAsync(record, recordId);
        }

        private async Task LoadModeSpecificRecordAsync(Dictionary<string, object> record, Guid recordId)
        {
            if (_documentKind == FinanceDocumentKind.AdvanceReport)
            {
                SelectComboByRecordValue(FinanceEmployeeCombo, record, "Сотрудник", "employee_id");
                SelectComboByRecordValue(AdvanceEmployeeCombo, record, "Сотрудник", "employee_id");
                SelectComboByRecordValue(AdvancePaymentCombo, record, "Вид авансового расчета", "advance_payment_id");
                await LoadAdvanceExpenseLinesAsync(record, recordId);
                SelectAdvancePairFromLines();
                ReportStartDatePicker.SelectedDate = GetDate(record, "Дата начала отчета", "report_start_date");
                ReportEndDatePicker.SelectedDate = GetDate(record, "Дата окончания отчета", "report_end_date");
                IssueDocumentBox.Text = GetString(record, "Документ выдачи", "issue_document_number");
                IssueDocumentDatePicker.SelectedDate = GetDate(record, "Дата выдачи", "issue_document_date");
                AcceptedAmountBox.Text = FormatDecimal(GetDecimal(record, "Принято к учету", "accepted_amount"));
                OverrunAmountBox.Text = FormatDecimal(GetDecimal(record, "Перерасход", "overrun_amount"));
                ReturnAmountBox.Text = FormatDecimal(GetDecimal(record, "Остаток к возврату", "return_amount"));
            }
            else if (_documentKind == FinanceDocumentKind.PayrollStatement)
            {
                SelectComboByRecordValue(PayrollEmployeeCombo, record, "Сотрудник", "employee_id");
                PeriodStartDatePicker.SelectedDate = GetDate(record, "Дата начала периода", "period_start_date");
                PeriodEndDatePicker.SelectedDate = GetDate(record, "Дата окончания периода", "period_end_date");
                ApplyPaymentAccountValue(GetFirstValue(record, "Счет выплаты", "payment_account"));
                AccruedAmountBox.Text = FormatDecimal(GetDecimal(record, "Начислено", "accrued_amount"));
                WithheldAmountBox.Text = FormatDecimal(GetDecimal(record, "Удержано", "withheld_amount"));
                PayableAmountBox.Text = FormatDecimal(GetDecimal(record, "К выплате", "payable_amount"));
            }
        }

        private void ConfigureMode()
        {
            var isAdvanceReport = _documentKind == FinanceDocumentKind.AdvanceReport;
            var isPayrollStatement = _documentKind == FinanceDocumentKind.PayrollStatement;

            AdvancePaymentHeaderPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            OrganizationPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            FinanceEmployeePanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvanceBalancesPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvanceExpensePanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvanceReportPanel.Visibility = Visibility.Collapsed;
            PayrollStatementPanel.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            CommonAmountPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            DebitAccountPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            CreditAccountPanel.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Collapsed : Visibility.Visible;
            DescriptionBox.Visibility = Visibility.Collapsed;

            AmountBox.IsReadOnly = false;
        }
        private void SetDefaultDates()
        {
            var today = DateTime.Today;
            ReportStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            ReportEndDatePicker.SelectedDate = today;
            PeriodStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            PeriodEndDatePicker.SelectedDate = today;
        }

        private async Task<string> GetNextNumberAsync()
        {
            try
            {
                return await _metadataService.GetNextDocumentNumberAsync(_document);
            }
            catch
            {
                return MetadataService.GenerateFallbackDocumentNumber();
            }
        }

        private async void SelectDebitAccount_Click(object sender, RoutedEventArgs e)
        {
            await SelectPlanAccountAsync((id, displayName) => ApplyDebitAccountValue(id, displayName));
        }

        private async void SelectCreditAccount_Click(object sender, RoutedEventArgs e)
        {
            await SelectPlanAccountAsync((id, displayName) => ApplyCreditAccountValue(id, displayName));
        }

        private async void SelectPaymentAccount_Click(object sender, RoutedEventArgs e)
        {
            await SelectPlanAccountAsync((id, displayName) =>
            {
                ApplyPaymentAccountValue(id, displayName);
                if (_creditAccountValue == null)
                    ApplyCreditAccountValue(id, displayName);
            });
        }

        private async Task SelectPlanAccountAsync(Action<Guid, string> applySelection)
        {
            try
            {
                Cursor = Cursors.Wait;
                _accountSelectionData ??= await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);
                var accountsData = _accountSelectionData;
                if (accountsData.Count == 0)
                {
                    MessageBox.Show("Для этого модуля нет доступных счетов в плане счетов.", "План счетов",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selection = new AccountSelectionView(accountsData);

                if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(this, "Выбор счета", selection) == true && selection.SelectedAccount != null)
                {
                    var accountCode = selection.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
                    var accountName = selection.SelectedAccount.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty;
                    var displayName = string.IsNullOrWhiteSpace(accountName) ? accountCode : $"{accountCode} - {accountName}";
                    if (Guid.TryParse(selection.SelectedAccount.GetValueOrDefault("Id")?.ToString(), out var accountId))
                        applySelection(accountId, displayName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выбора счета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private void AdvancePaymentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AdvancePaymentCombo.SelectedItem is not ReferenceItem selected)
            {
                UpdateAdvanceAccountDependentVisibility();
                QueueAdvanceBalancesUpdate();
                return;
            }

            var row = _advancePaymentRows.FirstOrDefault(item =>
                TryGetGuid(item.GetValueOrDefault("Id"), out var id) && id == selected.Id);
            if (row != null)
            {
                ApplyDebitAccountValue(GetFirstValue(row, "debit_account", "Дебет"));
                ApplyCreditAccountValue(GetFirstValue(row, "credit_account", "Кредит"));
            }

            if (!_isLoading)
            {
                var targetRow = AdvanceExpenseGrid?.SelectedItem as AdvanceExpenseLineRow
                    ?? _advanceExpenseLines.FirstOrDefault(IsAdvanceExpenseRowEmpty)
                    ?? _advanceExpenseLines.FirstOrDefault();
                if (targetRow != null && targetRow.PairId == Guid.Empty)
                    targetRow.PairId = selected.Id;
            }

            UpdateAdvanceAccountDependentVisibility();
            QueueAdvanceBalancesUpdate();
        }
        private async void OnRateInputChanged(object sender, EventArgs e)
        {
            if (_documentKind == FinanceDocumentKind.AdvanceReport)
                QueueAdvanceBalancesUpdate();

            if (_isLoading || _isApplyingCurrencyRate || CurrencyCombo == null || DatePicker == null)
                return;

            await LoadExchangeRateFromCatalogAsync();
        }
        private void CurrencyAmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingCurrencyRate || AmountCurrencyBox == null || ExchangeRateBox == null || AmountBox == null)
                return;

            RecalculateAmountFromCurrency();
        }

        private void PayrollAmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || _isCalculatingPayroll || AccruedAmountBox == null || WithheldAmountBox == null ||
                PayableAmountBox == null || AmountBox == null)
            {
                return;
            }

            try
            {
                _isCalculatingPayroll = true;
                var accrued = ReadDecimal(AccruedAmountBox.Text);
                var withheld = ReadDecimal(WithheldAmountBox.Text);
                var payable = Math.Max(0m, accrued - withheld);
                PayableAmountBox.Text = FormatDecimal(payable);
                AmountBox.Text = FormatDecimal(payable);
            }
            finally
            {
                _isCalculatingPayroll = false;
            }
        }

        private async Task LoadExchangeRateFromCatalogAsync()
        {
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
            var amountCurrency = ReadDecimal(AmountCurrencyBox.Text);
            var exchangeRate = ReadDecimal(ExchangeRateBox.Text);
            if (amountCurrency <= 0 || exchangeRate <= 0)
                return;

            AmountBox.Text = FormatDecimal(Math.Round(amountCurrency * exchangeRate, 2, MidpointRounding.AwayFromZero));
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                _pendingAdvanceLines = new List<AdvancePaymentLine>();
                var data = BuildRecordData();

                Guid savedId;
                if (_editId.HasValue)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, data);
                    savedId = _editId.Value;
                }
                else
                {
                    savedId = await _metadataService.CreateDynamicRecordAsync(_document.Id, data);
                }

                SavedRecordId = savedId;

                if (_documentKind == FinanceDocumentKind.AdvanceReport)
                {
                    // Строки затрат — в отдельную таблицу (классическая модель).
                    await _metadataService.SaveAdvancePaymentLinesAsync(
                        _document.TableName, savedId, _pendingAdvanceLines);
                    await _metadataService.PostDocumentAsync(_document.Id, savedId);
                }

                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }
        private static AdvancePaymentLine ToAdvancePaymentLine(AdvanceExpenseLineRecord source) => new()
        {
            LineDate = source.LineDate,
            PairId = source.PairId,
            PairCode = source.PairCode,
            PairName = source.PairName,
            DebitAccount = source.DebitAccount,
            CreditAccount = source.CreditAccount,
            ExpenseAccount = source.ExpenseAccount,
            ExpenseAccountName = source.ExpenseAccountName,
            CurrencyId = source.CurrencyId,
            CurrencyName = source.CurrencyName,
            AmountCurrency = source.AmountCurrency,
            ExchangeRate = source.ExchangeRate,
            Amount = source.Amount,
            Description = source.Description
        };

        private Dictionary<string, object> BuildRecordData()
        {
            var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text);
            if (string.IsNullOrWhiteSpace(documentNumber))
                throw new InvalidOperationException("Номер документа не заполнен.");

            var amount = ReadDecimal(AmountBox.Text);
            var data = new Dictionary<string, object>
            {
                ["Номер"] = documentNumber,
                ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                ["Сумма"] = amount,
                ["Основание"] = BasisBox.Text,
                ["Примечание"] = ShowDescriptionCheckBox?.IsChecked == true ? DescriptionBox.Text : string.Empty
            };

            if (!_editId.HasValue)
                data["Проведен"] = false;

            SetFieldValueIfExists(data, "Организация", GetSelectedReferenceId(OrganizationCombo));
            SetFieldValueIfExists(data, "Счет дебета", GetAccountValueForSave(_debitAccountValue));
            SetFieldValueIfExists(data, "Счет кредита", GetAccountValueForSave(_creditAccountValue));
            SetCurrencyValues(data);

            if (_documentKind == FinanceDocumentKind.AdvanceReport)
                ApplyAdvanceReportData(data);
            else if (_documentKind == FinanceDocumentKind.PayrollStatement)
                ApplyPayrollStatementData(data);

            var finalAmount = ReadDecimal(data.TryGetValue("Сумма", out var finalAmountValue)
                ? finalAmountValue?.ToString()
                : AmountBox.Text);
            if (finalAmount <= 0)
                throw new InvalidOperationException("Сумма документа должна быть больше нуля.");
            data["Сумма"] = finalAmount;

            return data;
        }

        private void ApplyAdvanceReportData(Dictionary<string, object> data)
        {
            var employeeId = GetSelectedReferenceId(FinanceEmployeeCombo);
            // Сотрудник обязателен, только если его поле показано (пометка пары счетов).
            if (employeeId == Guid.Empty && ShouldShowAdvanceEmployee())
                throw new InvalidOperationException("Выберите сотрудника.");


            var lines = BuildAdvanceExpenseLineRecords();
            var total = lines.Sum(line => line.Amount);
            if (total <= 0)
                throw new InvalidOperationException("Сумма авансовых платежей должна быть больше нуля.");

            if (ShouldShowAdvanceOrganization())
            {
                var organizationId = GetSelectedReferenceId(OrganizationCombo);
                if (organizationId == Guid.Empty)
                    throw new InvalidOperationException("Выберите организацию для выбранной пары счетов.");
                SetFieldValueIfExists(data, "Организация", organizationId);
            }

            data["Сумма"] = total;
            AmountBox.Text = FormatDecimal(total);
            if (employeeId != Guid.Empty)
                SetFieldValueIfExists(data, "Сотрудник", employeeId);

            // Строки затрат сохраняются в отдельную таблицу (doc_advance_payment_lines)
            // после записи шапки — в OnSaveClick, а не как JSON в шапку.
            _pendingAdvanceLines = lines.Select(ToAdvancePaymentLine).ToList();
            SetFieldValueIfExists(data, "Принято к учету", total);

            var firstLine = lines[0];
            SetFieldValueIfExists(data, "Вид авансового расчета", firstLine.PairId);
            SetFieldValueIfExists(data, "Счет дебета", firstLine.ExpenseAccount);
            SetFieldValueIfExists(data, "Счет кредита", firstLine.CreditAccount);

            var firstCurrencyLine = lines.FirstOrDefault(line => line.CurrencyId != Guid.Empty || line.AmountCurrency > 0 || line.ExchangeRate > 0);
            if (firstCurrencyLine != null)
            {
                SetFieldValueIfExists(data, "Валюта", firstCurrencyLine.CurrencyId);
                SetFieldValueIfExists(data, "Сумма в валюте", firstCurrencyLine.AmountCurrency);
                SetFieldValueIfExists(data, "Курс", firstCurrencyLine.ExchangeRate);
            }
        }
        private void ConfigureAdvanceExpenseGrid()
        {
            // Строки не создаются заранее: новая строка появляется только по
            // кнопке «Добавить строку» или по правому «+» в таблице.
            AdvanceExpenseGrid.ItemsSource = _advanceExpenseLines;
            RefreshAdvanceExpenseLineNumbers();
            RecalculateAdvanceExpenseTotal();
        }

        private async Task LoadAdvanceExpenseLinesAsync(IReadOnlyDictionary<string, object> record, Guid recordId)
        {
            _advanceExpenseLines.Clear();

            // Строки затрат читаются из классической таблицы строк
            // (doc_advance_payment_lines), а не из JSON-поля expense_lines.
            var lines = await _metadataService.GetAdvancePaymentLinesAsync(_document.TableName, recordId);
            foreach (var line in lines)
            {
                var row = new AdvanceExpenseLineRow
                {
                    LineDate = line.LineDate ?? DatePicker.SelectedDate,
                    PairId = line.PairId,
                    ExpenseAccountValue = line.ExpenseAccount,
                    ExpenseAccountDisplay = ResolveAccountDisplay(line.ExpenseAccount, line.ExpenseAccountName),
                    Amount = line.Amount,
                    CurrencyId = line.CurrencyId,
                    AmountCurrency = line.AmountCurrency,
                    ExchangeRate = line.ExchangeRate,
                    Description = line.Description
                };
                ApplyLoadedExpenseAccount(row);
                AttachAdvanceExpenseLine(row);
                _advanceExpenseLines.Add(row);
            }

            if (_advanceExpenseLines.Count == 0)
                AddLegacyAdvanceExpenseLine(record);

            RefreshAdvanceExpenseLineNumbers();
            RecalculateAdvanceExpenseTotal();
        }

        private void AddLegacyAdvanceExpenseLine(IReadOnlyDictionary<string, object> record)
        {
            var amount = GetDecimal(record, "Принято к учету", "accepted_amount", "Сумма", "amount");
            if (!TryGetGuid(GetFirstValue(record, "Вид авансового расчета", "advance_payment_id"), out var pairId) && _advancePayments.Count == 1)
                pairId = _advancePayments[0].Id;

            var expenseAccountValue = GetFirstValue(record, "Счет дебета", "debit_account");
            if (pairId == Guid.Empty && IsEmptyAccountValue(expenseAccountValue) && amount <= 0)
                return;

            var row = new AdvanceExpenseLineRow
            {
                LineDate = DatePicker.SelectedDate,
                PairId = pairId,
                ExpenseAccountValue = expenseAccountValue,
                ExpenseAccountDisplay = ResolveAccountDisplay(expenseAccountValue, null),
                Amount = amount,
                CurrencyId = GetSelectedReferenceId(CurrencyCombo),
                AmountCurrency = GetDecimal(record, "Сумма в валюте", "amount_currency"),
                ExchangeRate = GetDecimal(record, "Курс", "exchange_rate"),
                Description = GetString(record, "Примечание", "description")
            };
            ApplyLoadedExpenseAccount(row);
            AttachAdvanceExpenseLine(row);
            _advanceExpenseLines.Add(row);
        }

        /// <summary>
        /// Восстанавливает код и наименование счёта расхода загруженной строки,
        /// чтобы поле ввода счёта не оказалось пустым при редактировании документа.
        /// </summary>
        private void ApplyLoadedExpenseAccount(AdvanceExpenseLineRow row)
        {
            var account = _accountAnalytics.FindAccount(row.ExpenseAccountValue);
            var code = account?.Code ?? FormatAccountCode(row.ExpenseAccountDisplay);
            row.ExpenseAccountCode = code;
            row.ExpenseAccountTitle = StripAccountCode(
                account?.DisplayName ?? row.ExpenseAccountDisplay,
                code);
            row.ExpenseAccountHint = string.Empty;
        }

        private List<AdvanceExpenseLineRecord> BuildAdvanceExpenseLineRecords()
        {
            var headerPairId = GetSelectedReferenceId(AdvancePaymentCombo);
            var rows = _advanceExpenseLines
                .Where(row => row.PairId != Guid.Empty || !IsEmptyAccountValue(row.ExpenseAccountValue) ||
                              row.Amount != 0 || row.AmountCurrency != 0 || row.ExchangeRate != 0 ||
                              !string.IsNullOrWhiteSpace(row.Description))
                .ToList();
            if (rows.Count == 0)
                throw new InvalidOperationException("Добавьте хотя бы одну строку затрат.");

            var result = new List<AdvanceExpenseLineRecord>();
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var rowNumber = index + 1;
                if (row.PairId == Guid.Empty && headerPairId != Guid.Empty)
                    row.PairId = headerPairId;
                if (row.PairId == Guid.Empty)
                    throw new InvalidOperationException($"Выберите пару счетов в шапке документа.");
                if (IsEmptyAccountValue(row.ExpenseAccountValue))
                    throw new InvalidOperationException($"В строке {rowNumber} введите или выберите счет расхода.");

                var amount = row.Amount;
                if (amount <= 0 && row.AmountCurrency > 0 && row.ExchangeRate > 0)
                {
                    amount = Math.Round(row.AmountCurrency * row.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                    row.Amount = amount;
                }
                if (amount <= 0)
                    throw new InvalidOperationException($"В строке {rowNumber} сумма должна быть больше нуля.");

                if ((row.AmountCurrency > 0 || row.ExchangeRate > 0) && row.CurrencyId == Guid.Empty)
                    throw new InvalidOperationException($"В строке {rowNumber} выберите валюту.");

                var pair = FindAdvancePaymentRow(row.PairId) ??
                    throw new InvalidOperationException($"Пара счетов из шапки документа не найдена в справочнике.");
                var creditAccount = GetString(pair, "credit_account", "Кредит");
                if (string.IsNullOrWhiteSpace(creditAccount))
                    creditAccount = GetString(pair, "debit_account", "Дебет");
                if (string.IsNullOrWhiteSpace(creditAccount))
                    throw new InvalidOperationException($"В выбранной паре счетов не указан расчетный счет.");

                var pairItem = _advancePayments.FirstOrDefault(item => item.Id == row.PairId);
                var pairCode = GetString(pair, "code", "Код");
                var pairName = GetString(pair, "name", "Вид расчета", "Наименование");
                result.Add(new AdvanceExpenseLineRecord
                {
                    LineDate = row.LineDate ?? DatePicker.SelectedDate ?? DateTime.Today,
                    PairId = row.PairId,
                    PairCode = pairCode,
                    PairName = !string.IsNullOrWhiteSpace(pairName) ? pairName : pairItem?.DisplayName ?? string.Empty,
                    DebitAccount = GetString(pair, "debit_account", "Дебет"),
                    CreditAccount = creditAccount,
                    ExpenseAccount = GetAccountValueForSave(row.ExpenseAccountValue).ToString() ?? string.Empty,
                    ExpenseAccountName = string.IsNullOrWhiteSpace(row.ExpenseAccountTitle)
                        ? row.ExpenseAccountDisplay
                        : row.ExpenseAccountTitle,
                    CurrencyId = row.CurrencyId,
                    CurrencyName = ResolveCurrencyDisplay(row.CurrencyId),
                    AmountCurrency = row.AmountCurrency,
                    ExchangeRate = row.ExchangeRate,
                    Amount = amount,
                    Description = row.Description?.Trim() ?? string.Empty
                });
            }

            return result;
        }
        private Dictionary<string, object>? FindAdvancePaymentRow(Guid pairId)
        {
            return _advancePaymentRows.FirstOrDefault(row =>
                TryGetGuid(row.GetValueOrDefault("Id"), out var id) && id == pairId);
        }

        private static bool ReadBool(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                return value switch
                {
                    bool boolValue => boolValue,
                    int intValue => intValue != 0,
                    long longValue => longValue != 0,
                    decimal decimalValue => decimalValue != 0,
                    string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                   text.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                                   text.Equals("+", StringComparison.OrdinalIgnoreCase) ||
                                   text.Equals("1", StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }

            return false;
        }

        private void AddAdvanceExpenseLine_Click(object sender, RoutedEventArgs e)
        {
            AddAdvanceExpenseLine();
        }

        private void AddAdvanceExpenseLine()
        {
            TryCommitAdvanceExpenseGridEdit();
            var row = CreateAdvanceExpenseLineRow();
            _advanceExpenseLines.Add(row);
            RefreshAdvanceExpenseLineNumbers();
            AdvanceExpenseGrid.SelectedItem = row;
            AdvanceExpenseGrid.ScrollIntoView(row);
            FocusAdvanceExpenseDateCell(row);
            RecalculateAdvanceExpenseTotal();
        }

        private void DeleteAdvanceExpenseLine_Click(object sender, RoutedEventArgs e)
        {
            if (AdvanceExpenseGrid.SelectedItem is AdvanceExpenseLineRow row)
                _advanceExpenseLines.Remove(row);
            RefreshAdvanceExpenseLineNumbers();
            RecalculateAdvanceExpenseTotal();
        }

        private async void SelectExpenseAccount_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AdvanceExpenseLineRow row)
                return;

            TryCommitAdvanceExpenseGridEdit();

            await SelectPlanAccountAsync((id, displayName) =>
                ApplyExpenseAccount(row, _accountAnalytics.FindAccount(id), id, displayName));
        }

        /// <summary>
        /// Поле «Счет расхода» в строке затрат принимает только цифры кода счёта,
        /// пробел и вставку любых символов блокирует. Валидация — как в счёт-фактуре.
        /// </summary>
        private void OnExpenseAccountPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsAsciiDigits(e.Text);
        }

        private void OnExpenseAccountPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private void OnExpenseAccountPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsAsciiDigits(text))
                e.CancelCommand();
        }

        /// <summary>
        /// Проверка кода счёта прямо во время набора: как только набранный код
        /// совпал со счётом плана счетов, в ячейку подставляется наименование,
        /// а если таких счетов нет — подсказка сообщает об этом до ухода из ячейки.
        /// </summary>
        private void OnExpenseAccountTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingExpenseAccount)
                return;

            if (sender is not TextBox { DataContext: AdvanceExpenseLineRow row } textBox)
                return;

            ApplyTypedExpenseAccountCode(row, textBox, showMessage: false);
            KeepExpenseAccountFocus(textBox);
        }

        /// <summary>
        /// Возвращает клавиатурный фокус в поле кода счёта, если он был потерян
        /// при обновлении строки. Без этого Enter после набора уходил из таблицы
        /// на первый элемент формы, потому что обработчик перехода по колонкам
        /// живёт на самой таблице и без фокуса в ней не срабатывает.
        /// </summary>
        private static void KeepExpenseAccountFocus(TextBox textBox)
        {
            if (textBox.IsFocused || ReferenceEquals(Keyboard.FocusedElement, textBox))
                return;

            textBox.Focus();
            Keyboard.Focus(textBox);
        }

        /// <summary>
        /// Уход из ячейки с кодом счёта: не найденный счёт подсвечивается сообщением
        /// и очищается, чтобы строка не сохранилась с несуществующим счётом.
        /// </summary>
        private void OnExpenseAccountLostFocus(object sender, RoutedEventArgs e)
        {
            if (_isApplyingExpenseAccount)
                return;

            if (sender is not TextBox { DataContext: AdvanceExpenseLineRow row } textBox)
                return;

            ApplyTypedExpenseAccountCode(row, textBox, showMessage: true);
        }

        private void ApplyTypedExpenseAccountCode(AdvanceExpenseLineRow row, TextBox? textBox, bool showMessage)
        {
            var code = (textBox?.Text ?? string.Empty).Trim();
            if (code.Length == 0)
            {
                ClearExpenseAccount(row);
                return;
            }

            var account = FindAccountByCode(code);
            if (account != null)
            {
                ApplyExpenseAccount(row, account, account.Id, account.DisplayName);
                return;
            }

            row.ExpenseAccountHint = FindAccountsByCodePrefix(code) == 0
                ? $"Счёт с кодом «{code}» не найден в плане счетов."
                : $"Нет счетов, начинающихся с «{code}». Проверьте код.";
            if (!showMessage)
                return;

            MessageBox.Show(row.ExpenseAccountHint, "Проверка",
                MessageBoxButton.OK, MessageBoxImage.Warning);

            _isApplyingExpenseAccount = true;
            try
            {
                row.ExpenseAccountCode = string.Empty;
                if (textBox != null)
                {
                    textBox.Text = string.Empty;
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }
            finally
            {
                _isApplyingExpenseAccount = false;
            }

            ClearExpenseAccount(row);
        }

        /// <summary>
        /// Заполняет все поля счёта расхода строки. Код и наименование расходятся
        /// при ручном наборе, поэтому заполняются независимо друг от друга.
        ///
        /// Здесь намеренно не вызывается UpdateAdvanceAccountDependentVisibility():
        /// видимость колонок зависит от пары счетов в шапке, а не от счёта строки,
        /// и любое переключение Visible/Collapsed заставляет DataGrid перестроить
        /// колонки вместе с находящимся в фокусе полем ввода — после этого Enter
        /// уходил бы из таблицы на первый элемент формы.
        /// </summary>
        private void ApplyExpenseAccount(
            AdvanceExpenseLineRow row,
            AccountReferenceItem? account,
            object? accountValue,
            string? displayName)
        {
            var code = account?.Code ?? FormatAccountCode(displayName ?? string.Empty);
            var title = StripAccountCode(account?.DisplayName ?? displayName, code);
            var display = !string.IsNullOrWhiteSpace(displayName)
                ? displayName!
                : account?.DisplayName ?? code;

            row.ExpenseAccountValue = accountValue;
            row.ExpenseAccountDisplay = display;
            row.ExpenseAccountCode = code;
            row.ExpenseAccountTitle = title;
            row.ExpenseAccountHint = string.Empty;
        }

        private static void ClearExpenseAccount(AdvanceExpenseLineRow row)
        {
            row.ExpenseAccountValue = null;
            row.ExpenseAccountDisplay = string.Empty;
            row.ExpenseAccountTitle = string.Empty;
            row.ExpenseAccountHint = string.Empty;
        }

        private AccountReferenceItem? FindAccountByCode(string code)
        {
            return _accountAnalytics.Accounts.FirstOrDefault(account =>
                string.Equals(account.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        private int FindAccountsByCodePrefix(string code)
        {
            return _accountAnalytics.Accounts.Count(account =>
                account.Code.StartsWith(code, StringComparison.OrdinalIgnoreCase));
        }

        private static string StripAccountCode(string? displayName, string code)
        {
            var text = (displayName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code) || text.Length <= code.Length)
                return text;

            return text.StartsWith(code, StringComparison.OrdinalIgnoreCase)
                ? text[code.Length..].TrimStart(' ', '-')
                : text;
        }

        private static bool IsAsciiDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                if (character is < '0' or > '9')
                    return false;
            }

            return true;
        }

        private void AdvanceExpenseGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var row = e.Row?.Item as AdvanceExpenseLineRow;
            var isAmountColumn = ReferenceEquals(e.Column, AdvanceAmountColumn);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (isAmountColumn)
                    ValidateAdvanceExpenseAmount(row);
                RecalculateAdvanceExpenseTotal();
            }));
        }

        private void AdvanceExpenseGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            RecalculateAdvanceExpenseTotal();
        }

        /// <summary>
        /// Получение фокуса таблицей «Строки затрат»: курсор ставится в колонку «Дата»
        /// первой строки, дальше ввод идёт клавишей Enter по колонкам.
        /// </summary>
        private void AdvanceExpenseGrid_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (AdvanceExpenseGrid.Items.Count == 0)
                return;

            // Реагируем только на вход фокуса в сам грид или его строку, а не на
            // переключение фокуса между редакторами соседних ячеек.
            if (e.OriginalSource is not DataGrid && e.OriginalSource is not DataGridRow)
                return;

            var current = AdvanceExpenseGrid.CurrentCell;
            var rowIndex = current.Item == null ? -1 : AdvanceExpenseGrid.Items.IndexOf(current.Item);
            if (rowIndex == 0 && current.Column == AdvanceExpenseDateColumn)
                return;

            var row = current.Item as object ?? AdvanceExpenseGrid.Items[rowIndex >= 0 ? rowIndex : 0];
            FocusAdvanceExpenseCell(row, 0);
        }

        /// <summary>
        /// Клавиши в таблице «Строки затрат» — как в счёт-фактуре:
        /// Enter переводит на следующую редактируемую ячейку (с последней — на первую
        /// ячейку следующей строки), «+» (в т.ч. правый плюс клавиатуры) добавляет строку.
        /// PreviewKeyDown срабатывает и когда фокус в редакторе активной ячейки,
        /// поэтому ввод работает одинаково в любой колонке.
        /// </summary>
        private void AdvanceExpenseGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (e.Key is Key.Add or Key.OemPlus)
            {
                e.Handled = true;
                AddAdvanceExpenseLine();
                return;
            }

            if (e.Key != Key.Enter)
                return;

            // Открытые списки (календарь даты, выпадающий список валюты) обрабатывают
            // Enter сами — иначе невозможно будет выбрать значение.
            if (IsOpenDropDownInGrid())
                return;

            e.Handled = true;
            TryCommitAdvanceExpenseGridEdit();
            MoveToNextAdvanceExpenseCell();
        }

        private bool IsOpenDropDownInGrid()
        {
            if (Keyboard.FocusedElement is not DependencyObject focused)
                return false;

            return FindAncestorOrSelf<DatePicker>(focused) is { IsDropDownOpen: true } ||
                   FindAncestorOrSelf<ComboBox>(focused) is { IsDropDownOpen: true };
        }

        /// <summary>
        /// Переводит фокус на следующую редактируемую ячейку строки, а с последней —
        /// на первую ячейку следующей строки.
        /// </summary>
        private void MoveToNextAdvanceExpenseCell()
        {
            var currentItem = AdvanceExpenseGrid.CurrentItem ?? AdvanceExpenseGrid.SelectedItem;
            var rowIndex = AdvanceExpenseGrid.Items.IndexOf(currentItem);
            if (rowIndex < 0)
                return;

            var columnIndex = AdvanceExpenseGrid.CurrentCell.Column?.DisplayIndex ?? 0;
            for (var step = columnIndex + 1; step < AdvanceExpenseGrid.Columns.Count; step++)
            {
                if (!IsEditableAdvanceExpenseColumn(step))
                    continue;

                FocusAdvanceExpenseCell(AdvanceExpenseGrid.Items[rowIndex], step);
                return;
            }

            // С последней колонки переходим на первую редактируемую следующей строки.
            var firstEditableColumn = Enumerable.Range(0, AdvanceExpenseGrid.Columns.Count)
                .FirstOrDefault(IsEditableAdvanceExpenseColumn, -1);
            if (firstEditableColumn < 0)
                return;

            var nextRowIndex = rowIndex + 1;
            if (nextRowIndex < AdvanceExpenseGrid.Items.Count)
                FocusAdvanceExpenseCell(AdvanceExpenseGrid.Items[nextRowIndex], firstEditableColumn);
        }

        /// <summary>
        /// Колонка доступна для ввода: видима и не помечена как «только чтение».
        /// </summary>
        private bool IsEditableAdvanceExpenseColumn(int displayColumnIndex)
        {
            var column = AdvanceExpenseGrid.Columns
                .FirstOrDefault(current => current.DisplayIndex == displayColumnIndex);

            return column is { IsReadOnly: false, Visibility: Visibility.Visible };
        }

        /// <summary>
        /// Ставит фокус ячейки, а затем — фокус ввода (редактор) внутри неё.
        /// Одного BeginEdit недостаточно: клавиатурный фокус остаётся на DataGrid.
        /// </summary>
        private void FocusAdvanceExpenseCell(object? item, int displayColumnIndex, bool defer = false)
        {
            if (item == null)
                return;

            var column = AdvanceExpenseGrid.Columns
                .FirstOrDefault(current => current.DisplayIndex == displayColumnIndex);
            if (column == null)
                return;

            void Apply()
            {
                AdvanceExpenseGrid.SelectedItem = item;
                AdvanceExpenseGrid.CurrentCell = new DataGridCellInfo(item, column);
                AdvanceExpenseGrid.ScrollIntoView(item, column);
                AdvanceExpenseGrid.BeginEdit();
            }

            // Для новой строки ждём окончания текущей операции ввода,
            // при переходе по Enter переключаем ячейку сразу.
            if (defer)
                Dispatcher.BeginInvoke(new Action(Apply), System.Windows.Threading.DispatcherPriority.Background);
            else
                Apply();

            RequestAdvanceExpenseEditorFocus(item, column);
        }

        /// <summary>
        /// Запрашивает фокус редактора ячейки. Контейнер ячейки и сам редактор
        /// создаются асинхронно, а DataGrid перехватывает фокус обратно при входе
        /// в режим правки, поэтому делаем несколько попыток с проверкой результата.
        /// </summary>
        private void RequestAdvanceExpenseEditorFocus(object item, DataGridColumn column, int attempt = 0)
        {
            if (attempt > 12)
                return;

            Dispatcher.BeginInvoke(
                new Action(() => TryFocusAdvanceExpenseEditor(item, column, attempt)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TryFocusAdvanceExpenseEditor(object item, DataGridColumn column, int attempt)
        {
            if (AdvanceExpenseGrid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
            {
                RequestAdvanceExpenseEditorFocus(item, column, attempt + 1);
                return;
            }

            var cell = FindAdvanceExpenseCell(row, column);
            if (cell == null)
            {
                RequestAdvanceExpenseEditorFocus(item, column, attempt + 1);
                return;
            }

            // Редактор может появиться позже (DataGrid откладывает вход в режим
            // правки), поэтому вешаемся на загрузку ячейки и ставим фокус сразу,
            // как только поле ввода появится в её дереве.
            if (attempt < 4)
                cell.Loaded += OnFocusedAdvanceExpenseCellLoaded;

            if (!FocusFirstAdvanceExpenseInput(cell))
            {
                AdvanceExpenseGrid.BeginEdit();
                RequestAdvanceExpenseEditorFocus(item, column, attempt + 1);
                return;
            }

            if (!IsKeyboardFocusInAdvanceExpenseCell(cell))
                RequestAdvanceExpenseEditorFocus(item, column, attempt + 1);
        }

        /// <summary>Поле ввода создано — передаём ему клавиатурный фокус.</summary>
        private void OnFocusedAdvanceExpenseCellLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGridCell cell)
            {
                cell.Loaded -= OnFocusedAdvanceExpenseCellLoaded;
                FocusFirstAdvanceExpenseInput(cell);
            }
        }

        /// <summary>
        /// Ставит клавиатурный фокус на первый редактируемый элемент ячейки:
        /// поле ввода счёта или суммы, выпадающий список валюты, календарь даты.
        /// </summary>
        private static bool FocusFirstAdvanceExpenseInput(DependencyObject parent)
        {
            if (parent is not Visual visual)
                return false;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);

                switch (child)
                {
                    case TextBox { IsReadOnly: false, IsEnabled: true } textBox:
                        textBox.Focus();
                        Keyboard.Focus(textBox);
                        textBox.SelectAll();
                        return true;
                    case ComboBox { IsEnabled: true } comboBox:
                        comboBox.Focus();
                        Keyboard.Focus(comboBox);
                        return true;
                    case DatePicker { IsEnabled: true } datePicker:
                        datePicker.Focus();
                        Keyboard.Focus(datePicker);
                        return true;
                }

                if (FocusFirstAdvanceExpenseInput(child))
                    return true;
            }

            return false;
        }

        private static bool IsKeyboardFocusInAdvanceExpenseCell(DataGridCell cell)
        {
            if (cell.IsKeyboardFocusWithin)
                return true;

            return Keyboard.FocusedElement is DependencyObject focused &&
                   (ReferenceEquals(focused, cell) || IsAdvanceExpenseVisualDescendant(cell, focused));
        }

        private static bool IsAdvanceExpenseVisualDescendant(DependencyObject parent, DependencyObject candidate)
        {
            if (parent is not Visual visual)
                return false;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);
                if (ReferenceEquals(child, candidate) || IsAdvanceExpenseVisualDescendant(child, candidate))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Контейнер ячейки ищется обходом визуального дерева строки: ячейки в нём
        /// расположены слева направо в порядке видимых колонок.
        /// </summary>
        private DataGridCell? FindAdvanceExpenseCell(DataGridRow row, DataGridColumn column)
        {
            var targetIndex = GetAdvanceExpenseVisibleColumnIndex(column);
            return targetIndex < 0 ? null : FindAdvanceExpenseCells(row).ElementAtOrDefault(targetIndex);
        }

        private int GetAdvanceExpenseVisibleColumnIndex(DataGridColumn column)
        {
            var visibleIndex = 0;
            foreach (var current in AdvanceExpenseGrid.Columns)
            {
                if (current.Visibility != Visibility.Visible)
                    continue;

                if (ReferenceEquals(current, column))
                    return visibleIndex;

                visibleIndex++;
            }

            return -1;
        }

        /// <summary>Все контейнеры ячеек строки в порядке следования колонок.</summary>
        private static IEnumerable<DataGridCell> FindAdvanceExpenseCells(DependencyObject parent)
        {
            if (parent is not Visual visual)
                yield break;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);
                if (child is DataGridCell cell)
                    yield return cell;

                foreach (var nested in FindAdvanceExpenseCells(child))
                    yield return nested;
            }
        }

        private static T? FindAncestorOrSelf<T>(DependencyObject? element)
            where T : DependencyObject
        {
            var current = element;
            while (current != null)
            {
                if (current is T target)
                    return target;

                current = GetAdvanceExpenseParent(current);
            }

            return null;
        }

        private static DependencyObject? GetAdvanceExpenseParent(DependencyObject element)
        {
            return element switch
            {
                Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        private void FocusAdvanceExpenseDateCell(AdvanceExpenseLineRow row)
        {
            var columnIndex = AdvanceExpenseGrid.Columns
                .FirstOrDefault(column => column == AdvanceExpenseDateColumn)?.DisplayIndex ?? 0;
            FocusAdvanceExpenseCell(row, columnIndex, defer: true);
        }

        private void RefreshAdvanceExpenseLineNumbers()
        {
            for (var index = 0; index < _advanceExpenseLines.Count; index++)
                _advanceExpenseLines[index].RowNumber = index + 1;
        }

        private void RecalculateAdvanceExpenseTotal()
        {
            var total = _advanceExpenseLines.Sum(row => row.Amount);
            if (AdvanceExpenseTotalText != null)
                AdvanceExpenseTotalText.Text = $"Итого: {BIS.ERP.Converters.DecimalInputConverter.Format(total, "0.##")}";
            if (_documentKind == FinanceDocumentKind.AdvanceReport && AmountBox != null)
                AmountBox.Text = FormatDecimal(total);
            if (_documentKind == FinanceDocumentKind.AdvanceReport && _isInitialized)
                QueueAdvanceBalancesUpdate();
        }

        private void TryCommitAdvanceExpenseGridEdit()
        {
            try
            {
                AdvanceExpenseGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                AdvanceExpenseGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (InvalidOperationException)
            {
                // DataGrid can be between edit transitions while the user presses + or ?. The row is still kept in memory.
            }
        }

        private void ValidateAdvanceExpenseAmount(AdvanceExpenseLineRow? row)
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport || row == null || row.Amount > 0 || IsAdvanceExpenseRowEmpty(row))
                return;

            MessageBox.Show("Сумма строки должна быть больше нуля.", "Проверка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool IsAdvanceExpenseRowEmpty(AdvanceExpenseLineRow row)
        {
            return row.PairId == Guid.Empty &&
                   IsEmptyAccountValue(row.ExpenseAccountValue) &&
                   row.Amount == 0m &&
                   row.AmountCurrency == 0m &&
                   row.ExchangeRate == 0m &&
                   string.IsNullOrWhiteSpace(row.Description);
        }

        private static void SetPostingDetail(Dictionary<string, object> detail, string field, string? value) => 
            PostingDetailRowFactory.Set(detail, field, value);

        private static string FormatAccountCode(string accountValue)
        {
            if (string.IsNullOrWhiteSpace(accountValue))
                return string.Empty;

            var separatorIndex = accountValue.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? accountValue[..separatorIndex].Trim()
                : accountValue.Trim();
        }
        private void ShowDescriptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DescriptionBox == null || ShowDescriptionCheckBox == null)
                return;

            DescriptionBox.Visibility = ShowDescriptionCheckBox.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (ShowDescriptionCheckBox.IsChecked != true)
                DescriptionBox.Text = string.Empty;
        }

        /// <summary>
        /// Новая строка затрат создаётся пустой: дату пользователь вводит сам,
        /// пара счетов и счёт расхода — по необходимости.
        /// </summary>
        private AdvanceExpenseLineRow CreateAdvanceExpenseLineRow()
        {
            var row = new AdvanceExpenseLineRow
            {
                PairId = GetSelectedReferenceId(AdvancePaymentCombo)
            };
            AttachAdvanceExpenseLine(row);
            return row;
        }

        private void SelectAdvancePairFromLines()
        {
            if (AdvancePaymentCombo.SelectedItem != null)
                return;

            var pairId = _advanceExpenseLines.FirstOrDefault(row => row.PairId != Guid.Empty)?.PairId ?? Guid.Empty;
            if (pairId == Guid.Empty)
                return;

            AdvancePaymentCombo.SelectedItem = AdvancePaymentCombo.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == pairId);
        }

        private void UpdateAdvanceAccountDependentVisibility()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport)
                return;

            var pairRows = GetSelectedAdvancePairRows().ToList();
            var settings = _advanceExpenseLines
                .Select(row => _accountAnalytics.GetSettingsFromValue(row.ExpenseAccountValue))
                .Concat(pairRows.Select(row => _accountAnalytics.GetSettingsFromValue(GetFirstValue(row, "debit_account", "Дебет"))))
                .Concat(pairRows.Select(row => _accountAnalytics.GetSettingsFromValue(GetFirstValue(row, "credit_account", "Кредит"))))
                .ToList();

            // «Сотрудник» и «Организация» видны только когда в выбранной паре счетов
            // стоят соответствующие пометки справочника «Пары счетов».
            var showEmployee = pairRows.Any(row => ReadBool(row, "use_personnel", "Сотрудники"));
            var showOrganization = pairRows.Any(row => ReadBool(row, "use_organizations", "Организации"));
            var showCurrency = ShouldShowAdvanceCurrency(pairRows, settings);

            // Валютные колонки показываются только для валютных операций:
            // пара счетов с валютным учётом, валюта в строке либо аналитика счёта.
            FinanceEmployeePanel.Visibility = showEmployee ? Visibility.Visible : Visibility.Collapsed;
            OrganizationPanel.Visibility = showOrganization ? Visibility.Visible : Visibility.Collapsed;

            AdvanceCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            AdvanceAmountCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            AdvanceExchangeRateColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            CurrencyPanel.Visibility = Visibility.Collapsed;
        }

        private IEnumerable<Dictionary<string, object>> GetSelectedAdvancePairRows()
        {
            var ids = _advanceExpenseLines
                .Select(row => row.PairId)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            var headerId = GetSelectedReferenceId(AdvancePaymentCombo);
            if (headerId != Guid.Empty)
                ids.Add(headerId);

            foreach (var id in ids)
            {
                var row = FindAdvancePaymentRow(id);
                if (row != null)
                    yield return row;
            }
        }

        private bool ShouldShowAdvanceOrganization()
        {
            return OrganizationPanel.Visibility == Visibility.Visible;
        }

        private bool ShouldShowAdvanceEmployee()
        {
            return FinanceEmployeePanel.Visibility == Visibility.Visible;
        }

        private bool ShouldShowAdvanceCurrency(
            IReadOnlyCollection<Dictionary<string, object>> pairRows,
            IReadOnlyCollection<AccountAnalyticsSettings?> settings)
        {
            return _advanceExpenseLines.Any(row => row.CurrencyId != Guid.Empty || row.AmountCurrency > 0 || row.ExchangeRate > 0) ||
                   pairRows.Any(row => ReadBool(row, "use_currency", "Валютный")) ||
                   AccountAnalyticsRules.ShouldShowField(
                       "Валюта",
                       settings,
                       _accountAnalytics.Definitions,
                       "Справочник валют",
                       showWhenNoAccountSelected: false,
                       showUnmappedFields: false);
        }

        /// <summary>
        /// Реакция строки затрат на изменение валюты и валютных сумм:
        /// при выборе валюты подтягивается курс на дату строки, а введённые
        /// «Сумма в валюте» и «Курс» пересчитывают сумму строки.
        /// </summary>
        private async void OnAdvanceExpenseLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not AdvanceExpenseLineRow row)
                return;

            switch (e.PropertyName)
            {
                case nameof(AdvanceExpenseLineRow.CurrencyId):
                    await ApplyLineExchangeRateAsync(row);
                    // Показ колонок «Сумма в валюте»/«Курс» откладываем: колонки нельзя
                    // менять, пока активна ячейка редактирования валюты.
                    Dispatcher.BeginInvoke(new Action(UpdateAdvanceAccountDependentVisibility));
                    RecalculateAdvanceExpenseTotal();
                    break;
                case nameof(AdvanceExpenseLineRow.AmountCurrency):
                case nameof(AdvanceExpenseLineRow.ExchangeRate):
                    RecalculateAdvanceExpenseLineCurrency(row);
                    RecalculateAdvanceExpenseTotal();
                    break;
            }
        }

        private void AttachAdvanceExpenseLine(AdvanceExpenseLineRow row)
        {
            row.PropertyChanged -= OnAdvanceExpenseLinePropertyChanged;
            row.PropertyChanged += OnAdvanceExpenseLinePropertyChanged;
        }

        /// <summary>
        /// Подтягивает курс выбранной валюты на дату строки (дата документа,
        /// если дата строки ещё не введена). Для базовой валюты курс равен единице.
        /// При interactive=true курс запрашивается по кнопке «?»: если в справочнике
        /// его нет, предлагается загрузить актуальные курсы НБКР.
        /// </summary>
        private async Task ApplyLineExchangeRateAsync(AdvanceExpenseLineRow row, bool interactive = false)
        {
            if (row.CurrencyId == Guid.Empty)
            {
                if (row.AmountCurrency == 0 && row.ExchangeRate == 0)
                    return;

                row.ExchangeRate = 0;
                row.AmountCurrency = 0;
                row.ExchangeRateSource = string.Empty;
                return;
            }

            var currency = _currencies.FirstOrDefault(item => item.Id == row.CurrencyId);
            var rateDate = LineDateOrDocumentDate(row).Date;

            if (IsBaseCurrency(currency))
            {
                row.ExchangeRate = 1m;
                row.ExchangeRateSource = "Базовая валюта";
                RecalculateAdvanceExpenseLineCurrency(row);
                return;
            }

            var rate = await _metadataService.GetCurrencyRateForDateAsync(row.CurrencyId, rateDate);
            if (rate == null && interactive)
                rate = await RequestNewLineExchangeRateAsync(row.CurrencyId, rateDate);

            if (rate == null)
            {
                if (interactive)
                {
                    MessageBox.Show(
                        $"Курс валюты на {rateDate:dd/MM/yyyy} не найден. Введите курс вручную.",
                        "Курс валюты",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            row.ExchangeRate = rate.Rate;
            row.ExchangeRateSource = string.IsNullOrWhiteSpace(rate.Source)
                ? rate.RateDate.ToString("dd/MM/yyyy")
                : $"{rate.Source} на {rate.RateDate:dd/MM/yyyy}";
            RecalculateAdvanceExpenseLineCurrency(row);
        }

        /// <summary>Кнопка «?» в колонке «Курс»: запрос курса из справочника.</summary>
        private async void RequestLineExchangeRate_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AdvanceExpenseLineRow row)
                return;

            TryCommitAdvanceExpenseGridEdit();

            if (row.CurrencyId == Guid.Empty)
            {
                MessageBox.Show("Сначала выберите валюту в строке затрат.", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ApplyLineExchangeRateAsync(row, interactive: true);
            RecalculateAdvanceExpenseTotal();
        }

        /// <summary>
        /// Предлагает загрузить актуальные курсы НБКР и повторно ищет курс
        /// на запрошенную дату. Возвращает null, если курс так и не найден —
        /// тогда пользователь вводит курс вручную.
        /// </summary>
        private async Task<CurrencyRateLookupResult?> RequestNewLineExchangeRateAsync(Guid currencyId, DateTime rateDate)
        {
            var answer = MessageBox.Show(
                $"Курс на {rateDate:dd/MM/yyyy} не найден в справочнике курсов валют. Загрузить актуальные курсы НБКР?",
                "Курс валюты",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return null;

            try
            {
                Cursor = Cursors.Wait;
                var results = await _metadataService.ImportOfficialCurrencyRatesAsync(rateDate.AddDays(-7), rateDate);
                var refreshed = await _metadataService.GetCurrencyRateForDateAsync(currencyId, rateDate);
                if (refreshed != null)
                    return refreshed;

                MessageBox.Show(
                    $"Курсы загружены (добавлено: {results.Sum(item => item.Imported)}, " +
                    $"пропущено: {results.Sum(item => item.Skipped)}), но курс на {rateDate:dd/MM/yyyy} отсутствует. " +
                    "Введите курс вручную.",
                    "Курс валюты",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить курсы НБКР: {ex.Message}", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            finally
            {
                Cursor = null;
            }
        }

        private DateTime LineDateOrDocumentDate(AdvanceExpenseLineRow row)
        {
            return row.LineDate ?? DatePicker.SelectedDate ?? DateTime.Today;
        }

        private static bool IsBaseCurrency(ReferenceItem? currency)
        {
            if (currency == null)
                return false;

            return currency.LookupKeys.Contains("KGS") ||
                   currency.LookupKeys.Contains("417") ||
                   currency.DisplayName.Contains("KGS", StringComparison.OrdinalIgnoreCase) ||
                   currency.DisplayName.Contains("сом", StringComparison.OrdinalIgnoreCase) ||
                   currency.DisplayName.Contains("КГС", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Пересчёт валютных величин строки. «Сумма в валюте» — исходное значение:
        /// если оно введено, сумма в национальной валюте считается по курсу;
        /// иначе, если введена сумма, курс применяется в обратную сторону.
        /// Ручной ввод суммы не перезаписывается, пока валютная сумма не задана.
        /// </summary>
        private static void RecalculateAdvanceExpenseLineCurrency(AdvanceExpenseLineRow row)
        {
            if (row.CurrencyId == Guid.Empty || row.ExchangeRate <= 0)
                return;

            if (row.AmountCurrency > 0)
            {
                row.Amount = Math.Round(row.AmountCurrency * row.ExchangeRate, 2, MidpointRounding.AwayFromZero);
                return;
            }

            if (row.Amount > 0)
                row.AmountCurrency = Math.Round(row.Amount / row.ExchangeRate, 2, MidpointRounding.AwayFromZero);
        }

        private string ResolveCurrencyDisplay(Guid currencyId)
        {
            return currencyId == Guid.Empty
                ? string.Empty
                : _currencies.FirstOrDefault(item => item.Id == currencyId)?.DisplayName ?? currencyId.ToString();
        }

        private string ResolveAccountCodeForCalculation(object? value)
        {
            var account = _accountAnalytics.FindAccount(value);
            if (!string.IsNullOrWhiteSpace(account?.Code))
                return account.Code;

            return FormatAccountCode(value?.ToString() ?? string.Empty);
        }

        private static bool AccountMatches(string accountValue, string expectedCode)
        {
            if (string.IsNullOrWhiteSpace(accountValue) || string.IsNullOrWhiteSpace(expectedCode))
                return false;

            return string.Equals(FormatAccountCode(accountValue), expectedCode, StringComparison.OrdinalIgnoreCase);
        }

        private async Task UpdateAdvanceBalancesAsync()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport || _isUpdatingAdvanceBalances || _isLoading || !_isInitialized)
                return;

            var total = _advanceExpenseLines.Sum(row => row.Amount);
            SetAdvanceBalanceTexts(0m, 0m, total, -total);

            var employee = FinanceEmployeeCombo.SelectedItem as ReferenceItem;
            var pair = GetSelectedAdvancePairRows().FirstOrDefault();
            if (employee == null || pair == null || DatePicker.SelectedDate == null)
                return;

            var debitAccount = ResolveAccountCodeForCalculation(GetFirstValue(pair, "debit_account", "Дебет"));
            var creditAccount = ResolveAccountCodeForCalculation(GetFirstValue(pair, "credit_account", "Кредит"));
            if (string.IsNullOrWhiteSpace(debitAccount) && string.IsNullOrWhiteSpace(creditAccount))
                return;

            try
            {
                _isUpdatingAdvanceBalances = true;
                var documentDate = DatePicker.SelectedDate.Value.Date;
                var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text);

                var previousBalance = 0m;
                var received = 0m;

                await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                await context.Database.OpenConnectionAsync();
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = @"
                    SELECT posting_date,
                           COALESCE(doc_number, '') AS doc_number,
                           COALESCE(document_type, '') AS document_type,
                           COALESCE(debit_account, '') AS debit_account,
                           COALESCE(credit_account, '') AS credit_account,
                           COALESCE(amount_kgs, 0) AS amount_kgs
                    FROM doc_postings
                    WHERE is_active = true
                      AND employee_id = @employeeId
                      AND DATE(posting_date) <= @documentDate
                      AND ((@debitAccount IS NOT NULL AND debit_account = @debitAccount)
                           OR (@creditAccount IS NOT NULL AND credit_account = @creditAccount))
                    ORDER BY posting_date, doc_number;";
                command.Parameters.Add(new NpgsqlParameter("@employeeId", employee.Id));
                command.Parameters.Add(new NpgsqlParameter("@documentDate", documentDate));
                command.Parameters.Add(new NpgsqlParameter("@debitAccount", string.IsNullOrWhiteSpace(debitAccount) ? DBNull.Value : debitAccount));
                command.Parameters.Add(new NpgsqlParameter("@creditAccount", string.IsNullOrWhiteSpace(creditAccount) ? DBNull.Value : creditAccount));

                await using var reader = await command.ExecuteReaderAsync();
                var dateOrdinal = reader.GetOrdinal("posting_date");
                var numberOrdinal = reader.GetOrdinal("doc_number");
                var typeOrdinal = reader.GetOrdinal("document_type");
                var debitOrdinal = reader.GetOrdinal("debit_account");
                var creditOrdinal = reader.GetOrdinal("credit_account");
                var amountOrdinal = reader.GetOrdinal("amount_kgs");

                while (await reader.ReadAsync())
                {
                    var postingNumber = MetadataService.NormalizeLegacyDocumentNumber(reader.GetString(numberOrdinal));
                    var postingType = reader.GetString(typeOrdinal);
                    if (string.Equals(postingNumber, documentNumber, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(postingType, _document.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var postingDate = reader.GetDateTime(dateOrdinal).Date;
                    var postingDebit = reader.GetString(debitOrdinal);
                    var postingCredit = reader.GetString(creditOrdinal);
                    var amount = reader.GetDecimal(amountOrdinal);

                    if (postingDate < documentDate)
                    {
                        if (AccountMatches(postingDebit, debitAccount))
                            previousBalance += amount;
                        if (AccountMatches(postingCredit, creditAccount))
                            previousBalance -= amount;
                    }
                    else if (postingDate == documentDate && AccountMatches(postingDebit, debitAccount))
                    {
                        received += amount;
                    }
                }

                SetAdvanceBalanceTexts(previousBalance, received, total, previousBalance + received - total);
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка пересчета остатков авансового платежа.", "FinanceDocumentDialog.UpdateAdvanceBalancesAsync", ex);
                SetAdvanceBalanceTexts(0m, 0m, total, -total);
            }
            finally
            {
                _isUpdatingAdvanceBalances = false;
            }
        }

        private void SetAdvanceBalanceTexts(decimal previous, decimal received, decimal amount, decimal closing)
        {
            if (PreviousAdvanceBalanceText == null)
                return;

            PreviousAdvanceBalanceText.Text = previous.ToString("N2", CultureInfo.CurrentCulture);
            ReceivedAdvanceText.Text = received.ToString("N2", CultureInfo.CurrentCulture);
            AdvanceAmountText.Text = amount.ToString("N2", CultureInfo.CurrentCulture);
            ClosingAdvanceBalanceText.Text = closing.ToString("N2", CultureInfo.CurrentCulture);
        }
        private void ApplyPayrollStatementData(Dictionary<string, object> data)
        {
            var employeeId = GetSelectedReferenceId(PayrollEmployeeCombo);
            if (employeeId == Guid.Empty)
                throw new InvalidOperationException("Выберите сотрудника.");
            if (!PeriodStartDatePicker.SelectedDate.HasValue || !PeriodEndDatePicker.SelectedDate.HasValue)
                throw new InvalidOperationException("Укажите период платежной ведомости.");
            if (IsEmptyAccountValue(_debitAccountValue) || IsEmptyAccountValue(_paymentAccountValue))
                throw new InvalidOperationException("Для платежной ведомости укажите счет дебета и счет выплаты.");

            var payableAmount = ReadDecimal(PayableAmountBox.Text);
            if (ReadDecimal(AmountBox.Text) <= 0 && payableAmount > 0)
                data["Сумма"] = payableAmount;

            var paymentAccountValue = GetAccountValueForSave(_paymentAccountValue);
            SetFieldValueIfExists(data, "Сотрудник", employeeId);
            SetFieldValueIfExists(data, "Дата начала периода", PeriodStartDatePicker.SelectedDate.Value);
            SetFieldValueIfExists(data, "Дата окончания периода", PeriodEndDatePicker.SelectedDate.Value);
            SetFieldValueIfExists(data, "Счет выплаты", paymentAccountValue);
            SetFieldValueIfExists(data, "Счет кредита", paymentAccountValue);
            SetFieldValueIfExists(data, "Начислено", ReadDecimal(AccruedAmountBox.Text));
            SetFieldValueIfExists(data, "Удержано", ReadDecimal(WithheldAmountBox.Text));
            SetFieldValueIfExists(data, "К выплате", payableAmount);
        }

        private void SetCurrencyValues(Dictionary<string, object> data)
        {
            // Валюта шапки используется только когда панель валюты действительно
            // показана. У авансовых платежей валюта задаётся в каждой строке
            // затрат, и запись в поля шапки из скрытой панели затирала бы их.
            if (CurrencyPanel.Visibility != Visibility.Visible)
                return;

            SetFieldValueIfExists(data, "Валюта", GetSelectedReferenceId(CurrencyCombo));
            SetFieldValueIfExists(data, "Сумма в валюте", ReadDecimal(AmountCurrencyBox.Text));
            SetFieldValueIfExists(data, "Курс", ReadDecimal(ExchangeRateBox.Text));
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        private void ApplyDebitAccountValue(object? value, string? displayName = null)
        {
            _debitAccountValue = value;
            DebitAccountBox.Text = ResolveAccountDisplay(value, displayName);
            UpdateAccountControlledFieldsVisibility();
        }

        private void ApplyCreditAccountValue(object? value, string? displayName = null)
        {
            _creditAccountValue = value;
            CreditAccountBox.Text = ResolveAccountDisplay(value, displayName);
            UpdateAccountControlledFieldsVisibility();
        }

        private void ApplyPaymentAccountValue(object? value, string? displayName = null)
        {
            _paymentAccountValue = value;
            PaymentAccountBox.Text = ResolveAccountDisplay(value, displayName);
            UpdateAccountControlledFieldsVisibility();
        }

        private string ResolveAccountDisplay(object? value, string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            var account = _accountAnalytics.FindAccount(value);
            return account?.DisplayName ?? value?.ToString() ?? string.Empty;
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            if (_documentKind == FinanceDocumentKind.AdvanceReport)
            {
                UpdateAdvanceAccountDependentVisibility();
                return;
            }

            var accountSettings = new[]
            {
                _accountAnalytics.GetSettingsFromValue(_debitAccountValue),
                _accountAnalytics.GetSettingsFromValue(_creditAccountValue),
                _accountAnalytics.GetSettingsFromValue(_paymentAccountValue)
            };

            var hasCurrencyValues =
                CurrencyCombo.SelectedItem != null ||
                ReadDecimal(AmountCurrencyBox.Text) > 0 ||
                ReadDecimal(ExchangeRateBox.Text) > 0;
            var showCurrency = hasCurrencyValues || AccountAnalyticsRules.ShouldShowField(
                "Валюта",
                accountSettings,
                _accountAnalytics.Definitions,
                "Справочник валют",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);

            CurrencyPanel.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            if (!showCurrency)
            {
                CurrencyCombo.SelectedItem = null;
                AmountCurrencyBox.Text = "0";
                ExchangeRateBox.Text = "0";
            }
        }

        private void SetFieldValueIfExists(Dictionary<string, object> data, string fieldName, object? value)
        {
            if (_document.Fields.Any(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                data[fieldName] = value ?? string.Empty;
        }

        private static object GetAccountValueForSave(object? value)
        {
            return value switch
            {
                Guid id when id != Guid.Empty => id,
                string text when !string.IsNullOrWhiteSpace(text) => text,
                _ => string.Empty
            };
        }

        private static bool IsEmptyAccountValue(object? value)
        {
            return value switch
            {
                Guid id => id == Guid.Empty,
                string text => string.IsNullOrWhiteSpace(text),
                _ => value == null
            };
        }

        private static Guid GetSelectedReferenceId(ComboBox comboBox)
        {
            return comboBox.SelectedItem is ReferenceItem selected ? selected.Id : Guid.Empty;
        }

        private static void SelectComboByRecordValue(
            ComboBox comboBox,
            IReadOnlyDictionary<string, object> record,
            params string[] keys)
        {
            var value = GetFirstValue(record, keys);
            if (!TryGetGuid(value, out var id))
                return;

            comboBox.SelectedItem = comboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == id);
        }

        private async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            List<MetadataObject> catalogs,
            string catalogName,
            string firstDisplayField,
            string secondDisplayField)
        {
            var catalog = catalogs.FirstOrDefault(item => item.Name.Equals(catalogName, StringComparison.OrdinalIgnoreCase));
            if (catalog == null)
                return new List<ReferenceItem>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                .Select(row => CreateReferenceItem(row, firstDisplayField, secondDisplayField))
                .ToList();
        }

        private static ReferenceItem CreateReferenceItem(
            Dictionary<string, object> row,
            string firstDisplayField,
            string secondDisplayField)
        {
            var item = new ReferenceItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = BuildDisplayName(row, firstDisplayField, secondDisplayField)
            };

            foreach (var value in row.Values)
            {
                var text = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(text))
                    item.LookupKeys.Add(text);
            }

            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                item.LookupKeys.Add(item.DisplayName);

            return item;
        }

        private static string BuildDisplayName(Dictionary<string, object> row, string firstField, string secondField)
        {
            var first = GetString(row, firstField, firstField.Replace("_", " "));
            var second = GetString(row, secondField, secondField.Replace("_", " "));

            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return $"{first} - {second}";

            return !string.IsNullOrWhiteSpace(first)
                ? first
                : !string.IsNullOrWhiteSpace(second)
                    ? second
                    : GetString(row, "Наименование", "name", "ФИО", "Код", "code", "Id");
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? normalized[..separatorIndex].Trim() : normalized;
        }

        private static object? GetFirstValue(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value;
            }

            return null;
        }

        private static string GetString(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            return GetFirstValue(row, keys)?.ToString() ?? string.Empty;
        }

        private static decimal GetDecimal(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            return ReadDecimal(GetFirstValue(row, keys)?.ToString());
        }

        private static DateTime? GetDate(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            var value = GetFirstValue(row, keys);
            if (value is DateTime dateValue)
                return dateValue;

            return DateTime.TryParse(value?.ToString(), out var parsedDate) ? parsedDate : null;
        }

        private static decimal ReadDecimal(string? text)
        {
            return NumericInputHelper.TryParseDecimal(text, CultureInfo.CurrentCulture, out var value)
                ? value
                : 0m;
        }

        private static string FormatDecimal(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private static bool TryGetGuid(object? value, out Guid id)
        {
            if (value is Guid guid)
            {
                id = guid;
                return true;
            }

            return Guid.TryParse(value?.ToString(), out id);
        }
    }
    public sealed class AdvanceExpenseLineRow : INotifyPropertyChanged
    {
        private int _rowNumber;
        private DateTime? _lineDate;
        private Guid _pairId;
        private object? _expenseAccountValue;
        private string _expenseAccountCode = string.Empty;
        private string _expenseAccountDisplay = string.Empty;
        private string _expenseAccountTitle = string.Empty;
        private string _expenseAccountHint = string.Empty;
        private decimal _amount;
        private Guid _currencyId;
        private decimal _amountCurrency;
        private decimal _exchangeRate;
        private string _exchangeRateSource = string.Empty;
        private string _description = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int RowNumber
        {
            get => _rowNumber;
            set
            {
                if (_rowNumber == value)
                    return;
                _rowNumber = value;
                OnPropertyChanged(nameof(RowNumber));
            }
        }

        public DateTime? LineDate
        {
            get => _lineDate;
            set
            {
                if (_lineDate == value)
                    return;
                _lineDate = value;
                OnPropertyChanged(nameof(LineDate));
            }
        }

        public Guid PairId
        {
            get => _pairId;
            set
            {
                if (_pairId == value)
                    return;
                _pairId = value;
                OnPropertyChanged(nameof(PairId));
            }
        }

        public object? ExpenseAccountValue
        {
            get => _expenseAccountValue;
            set
            {
                if (Equals(_expenseAccountValue, value))
                    return;
                _expenseAccountValue = value;
                OnPropertyChanged(nameof(ExpenseAccountValue));
            }
        }

        public string ExpenseAccountDisplay
        {
            get => _expenseAccountDisplay;
            set
            {
                var normalized = value ?? string.Empty;
                if (_expenseAccountDisplay == normalized)
                    return;
                _expenseAccountDisplay = normalized;
                OnPropertyChanged(nameof(ExpenseAccountDisplay));
            }
        }

        /// <summary>Код счёта расхода: вводится пользователем вручную.</summary>
        public string ExpenseAccountCode
        {
            get => _expenseAccountCode;
            set
            {
                var normalized = value ?? string.Empty;
                if (_expenseAccountCode == normalized)
                    return;
                _expenseAccountCode = normalized;
                OnPropertyChanged(nameof(ExpenseAccountCode));
            }
        }

        /// <summary>Наименование счёта расхода без кода — показывается рядом с полем ввода.</summary>
        public string ExpenseAccountTitle
        {
            get => _expenseAccountTitle;
            set
            {
                var normalized = value ?? string.Empty;
                if (_expenseAccountTitle == normalized)
                    return;
                _expenseAccountTitle = normalized;
                OnPropertyChanged(nameof(ExpenseAccountTitle));
            }
        }

        /// <summary>Подсказка по проверке набранного кода счёта (показывается во всплывающей подсказке).</summary>
        public string ExpenseAccountHint
        {
            get => _expenseAccountHint;
            set
            {
                var normalized = value ?? string.Empty;
                if (_expenseAccountHint == normalized)
                    return;
                _expenseAccountHint = normalized;
                OnPropertyChanged(nameof(ExpenseAccountHint));
                OnPropertyChanged(nameof(ExpenseAccountHasHint));
            }
        }

        /// <summary>Набранный код счёта не совпал со счётом плана счетов.</summary>
        public bool ExpenseAccountHasHint => !string.IsNullOrWhiteSpace(_expenseAccountHint);

        public decimal Amount
        {
            get => _amount;
            set
            {
                if (_amount == value)
                    return;
                _amount = value;
                OnPropertyChanged(nameof(Amount));
            }
        }

        public Guid CurrencyId
        {
            get => _currencyId;
            set
            {
                if (_currencyId == value)
                    return;
                _currencyId = value;
                OnPropertyChanged(nameof(CurrencyId));
            }
        }

        public decimal AmountCurrency
        {
            get => _amountCurrency;
            set
            {
                if (_amountCurrency == value)
                    return;
                _amountCurrency = value;
                OnPropertyChanged(nameof(AmountCurrency));
            }
        }

        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set
            {
                if (_exchangeRate == value)
                    return;
                _exchangeRate = value;
                OnPropertyChanged(nameof(ExchangeRate));
            }
        }

        /// <summary>Источник курса («НБКР на 15/10/2026») — для подсказки в ячейке.</summary>
        public string ExchangeRateSource
        {
            get => _exchangeRateSource;
            set
            {
                var normalized = value ?? string.Empty;
                if (_exchangeRateSource == normalized)
                    return;
                _exchangeRateSource = normalized;
                OnPropertyChanged(nameof(ExchangeRateSource));
                OnPropertyChanged(nameof(ExchangeRateHint));
            }
        }

        private const string ManualExchangeRateHint =
            "Введите курс вручную или нажмите «?» для запроса курса из справочника курсов валют";

        /// <summary>Подсказка ячейки курса: источник курса либо подсказка о ручном вводе.</summary>
        public string ExchangeRateHint => !string.IsNullOrWhiteSpace(_exchangeRateSource)
            ? $"{_exchangeRateSource}. {ManualExchangeRateHint}."
            : ManualExchangeRateHint;

        public string Description
        {
            get => _description;
            set
            {
                var normalized = value ?? string.Empty;
                if (_description == normalized)
                    return;
                _description = normalized;
                OnPropertyChanged(nameof(Description));
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class AdvanceExpenseLineRecord
    {
        public DateTime? LineDate { get; set; }
        public Guid PairId { get; set; }
        public string PairCode { get; set; } = string.Empty;
        public string PairName { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public string ExpenseAccount { get; set; } = string.Empty;
        public string ExpenseAccountName { get; set; } = string.Empty;
        public Guid CurrencyId { get; set; }
        public string CurrencyName { get; set; } = string.Empty;
        public decimal AmountCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}

