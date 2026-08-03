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

namespace BIS.ERP.Views
{
    public partial class FinanceDocumentDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private readonly FinanceDocumentKind _documentKind;

        private AccountAnalyticsRegistry _accountAnalytics = new();
        private List<ReferenceItem> _organizations = new();
        private List<ReferenceItem> _employees = new();
        private List<ReferenceItem> _currencies = new();
        private List<ReferenceItem> _advancePayments = new();
        private List<Dictionary<string, object>> _advancePaymentRows = new();
        private readonly ObservableCollection<AdvanceExpenseLineRow> _advanceExpenseLines = new();
        private readonly ObservableCollection<Dictionary<string, object>> _advancePostingDetails = new();
        private List<Dictionary<string, object>>? _accountSelectionData;

        private object? _debitAccountValue;
        private object? _creditAccountValue;
        private object? _paymentAccountValue;
        private bool _isLoading;
        private bool _isInitialized;
        private bool _isApplyingCurrencyRate;
        private bool _isCalculatingPayroll;

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
            var personnelAdvanceRows = _advancePaymentRows.Where(IsPersonnelAdvancePair).ToList();
            if (personnelAdvanceRows.Count == 0)
                personnelAdvanceRows = _advancePaymentRows;
            _advancePayments = personnelAdvanceRows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                .Select(row => CreateReferenceItem(row, "code", "name"))
                .ToList();
            AdvancePaymentPairColumn.ItemsSource = _advancePayments;

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
                        _advancePayments = items;
                        AdvancePaymentPairColumn.ItemsSource = _advancePayments;
                    },
                    "code",
                    "name");
            }
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

            SelectComboByRecordValue(OrganizationCombo, record, "Организация", "organization_id");
            ApplyDebitAccountValue(GetFirstValue(record, "Счет дебета", "debit_account"));
            ApplyCreditAccountValue(GetFirstValue(record, "Счет кредита", "credit_account"));
            SelectComboByRecordValue(CurrencyCombo, record, "Валюта", "currency_id");
            AmountCurrencyBox.Text = FormatDecimal(GetDecimal(record, "Сумма в валюте", "amount_currency"));
            ExchangeRateBox.Text = FormatDecimal(GetDecimal(record, "Курс", "exchange_rate"));

            LoadModeSpecificRecord(record);
        }

        private void LoadModeSpecificRecord(Dictionary<string, object> record)
        {
            if (_documentKind == FinanceDocumentKind.AdvanceReport)
            {
                SelectComboByRecordValue(FinanceEmployeeCombo, record, "Сотрудник", "employee_id");
                SelectComboByRecordValue(AdvanceEmployeeCombo, record, "Сотрудник", "employee_id");
                SelectComboByRecordValue(AdvancePaymentCombo, record, "Вид авансового расчета", "advance_payment_id");
                LoadAdvanceExpenseLines(record);
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

            OrganizationPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            FinanceEmployeePanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvanceExpensePanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvancePostingDetailsPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            AdvanceReportPanel.Visibility = Visibility.Collapsed;
            PayrollStatementPanel.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            CommonAmountPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            DebitAccountPanel.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            CreditAccountPanel.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Collapsed : Visibility.Visible;

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

                var dialog = new AccountSelectionDialog(accountsData)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    var accountCode = dialog.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
                    var accountName = dialog.SelectedAccount.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty;
                    var displayName = string.IsNullOrWhiteSpace(accountName) ? accountCode : $"{accountCode} - {accountName}";
                    if (Guid.TryParse(dialog.SelectedAccount.GetValueOrDefault("Id")?.ToString(), out var accountId))
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
            if (_isLoading || AdvancePaymentCombo.SelectedItem is not ReferenceItem selected)
                return;

            var row = _advancePaymentRows.FirstOrDefault(item =>
                TryGetGuid(item.GetValueOrDefault("Id"), out var id) && id == selected.Id);
            if (row == null)
                return;

            ApplyDebitAccountValue(GetFirstValue(row, "debit_account", "Дебет"));
            ApplyCreditAccountValue(GetFirstValue(row, "credit_account", "Кредит"));
        }

        private async void OnRateInputChanged(object sender, EventArgs e)
        {
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
                var data = BuildRecordData();

                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, data);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, data);

                DialogResult = true;
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
                ["Примечание"] = DescriptionBox.Text
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
            if (employeeId == Guid.Empty)
                throw new InvalidOperationException("Выберите сотрудника.");

            var lines = BuildAdvanceExpenseLineRecords();
            var total = lines.Sum(line => line.Amount);
            if (total <= 0)
                throw new InvalidOperationException("Сумма авансовых платежей должна быть больше нуля.");

            data["Сумма"] = total;
            AmountBox.Text = FormatDecimal(total);
            SetFieldValueIfExists(data, "Сотрудник", employeeId);
            SetFieldValueIfExists(data, "Строки затрат", JsonSerializer.Serialize(lines));
            SetFieldValueIfExists(data, "Принято к учету", total);

            var firstLine = lines[0];
            SetFieldValueIfExists(data, "Вид авансового расчета", firstLine.PairId);
            SetFieldValueIfExists(data, "Счет дебета", firstLine.ExpenseAccount);
            SetFieldValueIfExists(data, "Счет кредита", firstLine.CreditAccount);
        }


        private void ConfigureAdvanceExpenseGrid()
        {
            AdvanceExpenseGrid.ItemsSource = _advanceExpenseLines;
            AdvancePostingDetailsGrid.ItemsSource = _advancePostingDetails;
            if (_advanceExpenseLines.Count == 0)
                _advanceExpenseLines.Add(new AdvanceExpenseLineRow());
            RecalculateAdvanceExpenseTotal();
        }

        private void LoadAdvanceExpenseLines(IReadOnlyDictionary<string, object> record)
        {
            _advanceExpenseLines.Clear();
            var json = GetString(record, "Строки затрат", "expense_lines");
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<List<AdvanceExpenseLineRecord>>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (loaded != null)
                    {
                        foreach (var line in loaded)
                        {
                            _advanceExpenseLines.Add(new AdvanceExpenseLineRow
                            {
                                PairId = line.PairId,
                                ExpenseAccountValue = line.ExpenseAccount,
                                ExpenseAccountDisplay = ResolveAccountDisplay(line.ExpenseAccount, line.ExpenseAccountName),
                                Amount = line.Amount,
                                Description = line.Description
                            });
                        }
                    }
                }
                catch
                {
                    _advanceExpenseLines.Clear();
                }
            }

            if (_advanceExpenseLines.Count == 0)
                AddLegacyAdvanceExpenseLine(record);
            if (_advanceExpenseLines.Count == 0)
                _advanceExpenseLines.Add(new AdvanceExpenseLineRow());

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

            _advanceExpenseLines.Add(new AdvanceExpenseLineRow
            {
                PairId = pairId,
                ExpenseAccountValue = expenseAccountValue,
                ExpenseAccountDisplay = ResolveAccountDisplay(expenseAccountValue, null),
                Amount = amount,
                Description = GetString(record, "Примечание", "description")
            });
        }

        private List<AdvanceExpenseLineRecord> BuildAdvanceExpenseLineRecords()
        {
            var rows = _advanceExpenseLines
                .Where(row => row.PairId != Guid.Empty || !IsEmptyAccountValue(row.ExpenseAccountValue) ||
                              row.Amount != 0 || !string.IsNullOrWhiteSpace(row.Description))
                .ToList();
            if (rows.Count == 0)
                throw new InvalidOperationException("Добавьте хотя бы одну строку затрат.");

            var result = new List<AdvanceExpenseLineRecord>();
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var rowNumber = index + 1;
                if (row.PairId == Guid.Empty)
                    throw new InvalidOperationException($"В строке {rowNumber} выберите пару счетов.");
                if (IsEmptyAccountValue(row.ExpenseAccountValue))
                    throw new InvalidOperationException($"В строке {rowNumber} выберите счет расхода.");
                if (row.Amount <= 0)
                    throw new InvalidOperationException($"В строке {rowNumber} сумма должна быть больше нуля.");

                var pair = FindAdvancePaymentRow(row.PairId) ??
                    throw new InvalidOperationException($"В строке {rowNumber} пара счетов не найдена в справочнике.");
                var creditAccount = GetString(pair, "credit_account", "Кредит");
                if (string.IsNullOrWhiteSpace(creditAccount))
                    creditAccount = GetString(pair, "debit_account", "Дебет");
                if (string.IsNullOrWhiteSpace(creditAccount))
                    throw new InvalidOperationException($"В строке {rowNumber} в паре счетов не указан расчетный счет.");

                var pairItem = _advancePayments.FirstOrDefault(item => item.Id == row.PairId);
                var pairCode = GetString(pair, "code", "Код");
                var pairName = GetString(pair, "name", "Вид расчета", "Наименование");
                result.Add(new AdvanceExpenseLineRecord
                {
                    PairId = row.PairId,
                    PairCode = pairCode,
                    PairName = !string.IsNullOrWhiteSpace(pairName) ? pairName : pairItem?.DisplayName ?? string.Empty,
                    DebitAccount = GetString(pair, "debit_account", "Дебет"),
                    CreditAccount = creditAccount,
                    ExpenseAccount = GetAccountValueForSave(row.ExpenseAccountValue).ToString() ?? string.Empty,
                    ExpenseAccountName = row.ExpenseAccountDisplay,
                    Amount = row.Amount,
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

        private static bool IsPersonnelAdvancePair(IReadOnlyDictionary<string, object> row)
        {
            return ReadBool(row, "use_personnel", "Сотрудники") ||
                   GetString(row, "name", "Вид расчета", "Наименование")
                       .Contains("подотчет", StringComparison.OrdinalIgnoreCase);
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
            TryCommitAdvanceExpenseGridEdit();
            var row = new AdvanceExpenseLineRow();
            _advanceExpenseLines.Add(row);
            AdvanceExpenseGrid.SelectedItem = row;
            AdvanceExpenseGrid.ScrollIntoView(row);
            RecalculateAdvanceExpenseTotal();
        }

        private void DeleteAdvanceExpenseLine_Click(object sender, RoutedEventArgs e)
        {
            if (AdvanceExpenseGrid.SelectedItem is AdvanceExpenseLineRow row)
                _advanceExpenseLines.Remove(row);
            if (_advanceExpenseLines.Count == 0)
                _advanceExpenseLines.Add(new AdvanceExpenseLineRow());
            RecalculateAdvanceExpenseTotal();
        }

        private async void SelectExpenseAccount_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not AdvanceExpenseLineRow row)
                return;

            TryCommitAdvanceExpenseGridEdit();

            await SelectPlanAccountAsync((id, displayName) =>
            {
                row.ExpenseAccountValue = id;
                row.ExpenseAccountDisplay = displayName;
                UpdateAdvancePostingDetails();
            });
        }

        private void AdvanceExpenseGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            var row = e.Row?.Item as AdvanceExpenseLineRow;
            var isAmountColumn = string.Equals(e.Column.Header?.ToString(), "Сумма", StringComparison.OrdinalIgnoreCase);
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

        private void AdvanceExpenseGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAdvancePostingDetails();
        }

        private void AdvanceExpenseGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedAdvancePostingDetails();
        }

        private void AdvancePostingDetailsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedAdvancePostingDetails();
        }

        private void RecalculateAdvanceExpenseTotal()
        {
            var total = _advanceExpenseLines.Sum(row => row.Amount);
            if (AdvanceExpenseTotalText != null)
                AdvanceExpenseTotalText.Text = $"Итого: {FormatDecimal(total)}";
            if (_documentKind == FinanceDocumentKind.AdvanceReport && AmountBox != null)
                AmountBox.Text = FormatDecimal(total);
            UpdateAdvancePostingDetails();
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

        private void UpdateAdvancePostingDetails()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport || AdvancePostingDetailsGrid == null)
                return;

            _advancePostingDetails.Clear();
            var selectedRow = AdvanceExpenseGrid?.SelectedItem as AdvanceExpenseLineRow
                ?? _advanceExpenseLines.FirstOrDefault(row => !IsAdvanceExpenseRowEmpty(row));
            var posting = BuildAdvancePostingPreview(selectedRow);
            if (posting == null)
            {
                _advancePostingDetails.Add(new Dictionary<string, object>
                {
                    ["Документ"] = "Выберите строку затрат выше"
                });
                return;
            }

            var detail = new Dictionary<string, object>();
            SetPostingDetail(detail, "Документ", posting.DocumentNumber);
            SetPostingDetail(detail, "Тип документа", posting.DocumentType);
            SetPostingDetail(detail, "Дата", posting.Date.ToString("dd.MM.yyyy"));
            SetPostingDetail(detail, "Модуль", posting.ModuleName);
            SetPostingDetail(detail, "Дебет", FormatAccountCode(posting.DebitAccount));
            SetPostingDetail(detail, "Кредит", FormatAccountCode(posting.CreditAccount));
            SetPostingDetail(detail, "Сумма", posting.Amount > 0 ? posting.Amount.ToString("N2") : null);
            SetPostingDetail(detail, "Сотрудник", posting.Employee);
            SetPostingDetail(detail, "Примечание", posting.Note);
            _advancePostingDetails.Add(detail);
        }

        private PostingViewModel? BuildAdvancePostingPreview(AdvanceExpenseLineRow? row)
        {
            if (row == null || IsAdvanceExpenseRowEmpty(row))
                return null;

            var pair = row.PairId == Guid.Empty ? null : FindAdvancePaymentRow(row.PairId);
            var creditAccount = pair == null ? string.Empty : GetString(pair, "credit_account", "Кредит");
            if (string.IsNullOrWhiteSpace(creditAccount) && pair != null)
                creditAccount = GetString(pair, "debit_account", "Дебет");

            var debitAccount = !string.IsNullOrWhiteSpace(row.ExpenseAccountDisplay)
                ? row.ExpenseAccountDisplay
                : GetAccountValueForSave(row.ExpenseAccountValue).ToString() ?? string.Empty;

            var employee = FinanceEmployeeCombo?.SelectedItem is ReferenceItem selectedEmployee
                ? selectedEmployee.DisplayName
                : string.Empty;

            return new PostingViewModel
            {
                Date = DatePicker.SelectedDate ?? DateTime.Today,
                DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text),
                DocumentType = _document.Name,
                ModuleCode = "finance",
                ModuleName = "Финансы",
                DebitAccount = debitAccount,
                CreditAccount = creditAccount,
                Direction = "Бухгалтерская проводка",
                Amount = row.Amount,
                Employee = employee,
                Note = row.Description
            };
        }

        private void OpenSelectedAdvancePostingDetails()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport)
                return;

            var posting = BuildAdvancePostingPreview(AdvanceExpenseGrid?.SelectedItem as AdvanceExpenseLineRow);
            if (posting == null)
            {
                MessageBox.Show("Выберите заполненную строку затрат.", "Детали проводки",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new PostingDetailsDialog(posting)
            {
                Owner = this
            };
            dialog.ShowDialog();
        }

        private static bool IsAdvanceExpenseRowEmpty(AdvanceExpenseLineRow row)
        {
            return row.PairId == Guid.Empty &&
                   IsEmptyAccountValue(row.ExpenseAccountValue) &&
                   row.Amount == 0m &&
                   string.IsNullOrWhiteSpace(row.Description);
        }

        private static void SetPostingDetail(Dictionary<string, object> detail, string field, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            detail[field] = value;
        }

        private static string FormatAccountCode(string accountValue)
        {
            if (string.IsNullOrWhiteSpace(accountValue))
                return string.Empty;

            var separatorIndex = accountValue.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? accountValue[..separatorIndex].Trim()
                : accountValue.Trim();
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
            if (CurrencyPanel.Visibility != Visibility.Visible && CurrencyCombo.SelectedItem is null)
                return;

            SetFieldValueIfExists(data, "Валюта", GetSelectedReferenceId(CurrencyCombo));
            SetFieldValueIfExists(data, "Сумма в валюте", ReadDecimal(AmountCurrencyBox.Text));
            SetFieldValueIfExists(data, "Курс", ReadDecimal(ExchangeRateBox.Text));
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue))
                return currentValue;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue)
                ? invariantValue
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
        private Guid _pairId;
        private object? _expenseAccountValue;
        private string _expenseAccountDisplay = string.Empty;
        private decimal _amount;
        private string _description = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

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
        public Guid PairId { get; set; }
        public string PairCode { get; set; } = string.Empty;
        public string PairName { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public string ExpenseAccount { get; set; } = string.Empty;
        public string ExpenseAccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}


