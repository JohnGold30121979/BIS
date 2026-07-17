using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        private object? _debitAccountValue;
        private object? _creditAccountValue;
        private object? _paymentAccountValue;
        private bool _isLoading;
        private bool _isApplyingCurrencyRate;
        private bool _isCalculatingPayroll;

        public FinanceDocumentDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _documentKind = FinanceDocumentKindHelper.FromName(document.Name);
            DialogTitle.Text = $"Добавление: {document.Name}";
            DatePicker.SelectedDate = DateTime.Today;
            SetDefaultDates();
            ConfigureMode();
            ContentRendered += async (_, _) => await InitializeAsync();
        }

        public FinanceDocumentDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            _documentKind = FinanceDocumentKindHelper.FromName(document.Name);
            DialogTitle.Text = $"Редактирование: {document.Name}";
            ConfigureMode();
            ContentRendered += async (_, _) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isLoading)
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
            _advancePayments = _advancePaymentRows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                .Select(row => CreateReferenceItem(row, "code", "name"))
                .ToList();

            ReferenceComboBoxSearchHelper.Attach(OrganizationCombo, _organizations);
            ReferenceComboBoxSearchHelper.Attach(AdvanceEmployeeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(PayrollEmployeeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(RepresentativeCombo, _employees);
            ReferenceComboBoxSearchHelper.Attach(CounterpartyCombo, _organizations);
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
                ReferencePickerControlFactory.AttachEditor(
                    CounterpartyCombo,
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
                ReferencePickerControlFactory.AttachEditor(
                    RepresentativeCombo,
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

            var advancePaymentCatalog = catalogs.FirstOrDefault(catalog => catalog.Name == "Авансовые платежи");
            if (advancePaymentCatalog != null)
            {
                ReferencePickerControlFactory.AttachEditor(
                    AdvancePaymentCombo,
                    _metadataService,
                    advancePaymentCatalog,
                    this,
                    items => _advancePayments = items,
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
                SelectComboByRecordValue(AdvanceEmployeeCombo, record, "Сотрудник", "employee_id");
                SelectComboByRecordValue(AdvancePaymentCombo, record, "Вид авансового расчета", "advance_payment_id");
                ReportStartDatePicker.SelectedDate = GetDate(record, "Дата начала отчета", "report_start_date");
                ReportEndDatePicker.SelectedDate = GetDate(record, "Дата окончания отчета", "report_end_date");
                IssueDocumentBox.Text = GetString(record, "Документ выдачи", "issue_document_number");
                IssueDocumentDatePicker.SelectedDate = GetDate(record, "Дата выдачи", "issue_document_date");
                AcceptedAmountBox.Text = FormatDecimal(GetDecimal(record, "Принято к учету", "accepted_amount"));
                OverrunAmountBox.Text = FormatDecimal(GetDecimal(record, "Перерасход", "overrun_amount"));
                ReturnAmountBox.Text = FormatDecimal(GetDecimal(record, "Остаток к возврату", "return_amount"));
            }
            else if (_documentKind == FinanceDocumentKind.PowerOfAttorney)
            {
                SelectComboByRecordValue(RepresentativeCombo, record, "Представитель", "representative_id");
                SelectComboByRecordValue(CounterpartyCombo, record, "Поставщик", "counterparty_id");
                BankAccountBox.Text = GetString(record, "Расчетный счет", "bank_account");
                BankNameBox.Text = GetString(record, "Банк", "bank_name");
                IdentityDocumentNameBox.Text = GetString(record, "Документ личности", "identity_document_name");
                IdentityDocumentNumberBox.Text = GetString(record, "Номер документа личности", "identity_document_number");
                IdentityDocumentDatePicker.SelectedDate = GetDate(record, "Дата документа личности", "identity_document_date");
                IdentityDocumentIssuerBox.Text = GetString(record, "Кем выдан документ", "identity_document_issuer");
                ValidUntilDatePicker.SelectedDate = GetDate(record, "Срок действия", "valid_until");
                SourceDocumentNumberBox.Text = GetString(record, "Документ-основание", "source_document_number");
                SourceDocumentDatePicker.SelectedDate = GetDate(record, "Дата документа-основания", "source_document_date");
                ItemsDescriptionBox.Text = GetString(record, "Перечень ценностей", "items_description");
                QuantityBox.Text = FormatDecimal(GetDecimal(record, "Количество", "quantity"));
                UnitNameBox.Text = GetString(record, "Единица измерения", "unit_name");
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
            var isPowerOfAttorney = _documentKind == FinanceDocumentKind.PowerOfAttorney;
            var isPayrollStatement = _documentKind == FinanceDocumentKind.PayrollStatement;

            AdvanceReportPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            PowerOfAttorneyPanel.Visibility = isPowerOfAttorney ? Visibility.Visible : Visibility.Collapsed;
            PayrollStatementPanel.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            CommonAmountPanel.Visibility = isPowerOfAttorney ? Visibility.Collapsed : Visibility.Visible;
            DebitAccountPanel.Visibility = isPowerOfAttorney ? Visibility.Collapsed : Visibility.Visible;
            CreditAccountPanel.Visibility = isPowerOfAttorney ? Visibility.Collapsed : Visibility.Visible;

            if (isPayrollStatement)
                CreditAccountPanel.Visibility = Visibility.Collapsed;
        }

        private void SetDefaultDates()
        {
            var today = DateTime.Today;
            ReportStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            ReportEndDatePicker.SelectedDate = today;
            PeriodStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
            PeriodEndDatePicker.SelectedDate = today;
            ValidUntilDatePicker.SelectedDate = today.AddDays(10);
        }

        private async Task<string> GetNextNumberAsync()
        {
            try
            {
                return await _metadataService.GetNextDocumentNumberAsync(_document.Name);
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
                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);
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
            else if (_documentKind == FinanceDocumentKind.PowerOfAttorney)
                ApplyPowerOfAttorneyData(data);
            else if (_documentKind == FinanceDocumentKind.PayrollStatement)
                ApplyPayrollStatementData(data);

            return data;
        }

        private void ApplyAdvanceReportData(Dictionary<string, object> data)
        {
            var employeeId = GetSelectedReferenceId(AdvanceEmployeeCombo);
            var advancePaymentId = GetSelectedReferenceId(AdvancePaymentCombo);
            if (employeeId == Guid.Empty)
                throw new InvalidOperationException("Выберите сотрудника.");
            if (advancePaymentId == Guid.Empty)
                throw new InvalidOperationException("Выберите вид авансового расчета.");
            if (IsEmptyAccountValue(_debitAccountValue) || IsEmptyAccountValue(_creditAccountValue))
                throw new InvalidOperationException("Для авансового отчета укажите счета дебета и кредита.");

            var acceptedAmount = ReadDecimal(AcceptedAmountBox.Text);
            if (ReadDecimal(AmountBox.Text) <= 0 && acceptedAmount > 0)
                data["Сумма"] = acceptedAmount;

            SetFieldValueIfExists(data, "Сотрудник", employeeId);
            SetFieldValueIfExists(data, "Вид авансового расчета", advancePaymentId);
            SetFieldValueIfExists(data, "Дата начала отчета", ReportStartDatePicker.SelectedDate ?? DateTime.Today);
            SetFieldValueIfExists(data, "Дата окончания отчета", ReportEndDatePicker.SelectedDate ?? DateTime.Today);
            SetFieldValueIfExists(data, "Документ выдачи", IssueDocumentBox.Text);
            SetFieldValueIfExists(data, "Дата выдачи", IssueDocumentDatePicker.SelectedDate ?? DateTime.Today);
            SetFieldValueIfExists(data, "Принято к учету", acceptedAmount);
            SetFieldValueIfExists(data, "Перерасход", ReadDecimal(OverrunAmountBox.Text));
            SetFieldValueIfExists(data, "Остаток к возврату", ReadDecimal(ReturnAmountBox.Text));
        }

        private void ApplyPowerOfAttorneyData(Dictionary<string, object> data)
        {
            var representativeId = GetSelectedReferenceId(RepresentativeCombo);
            if (representativeId == Guid.Empty)
                throw new InvalidOperationException("Выберите представителя.");
            if (!ValidUntilDatePicker.SelectedDate.HasValue)
                throw new InvalidOperationException("Укажите срок действия доверенности.");

            SetFieldValueIfExists(data, "Представитель", representativeId);
            SetFieldValueIfExists(data, "Поставщик", GetSelectedReferenceId(CounterpartyCombo));
            SetFieldValueIfExists(data, "Расчетный счет", BankAccountBox.Text);
            SetFieldValueIfExists(data, "Банк", BankNameBox.Text);
            SetFieldValueIfExists(data, "Документ личности", IdentityDocumentNameBox.Text);
            SetFieldValueIfExists(data, "Номер документа личности", IdentityDocumentNumberBox.Text);
            SetFieldValueIfExists(data, "Дата документа личности", IdentityDocumentDatePicker.SelectedDate ?? DateTime.Today);
            SetFieldValueIfExists(data, "Кем выдан документ", IdentityDocumentIssuerBox.Text);
            SetFieldValueIfExists(data, "Срок действия", ValidUntilDatePicker.SelectedDate.Value);
            SetFieldValueIfExists(data, "Документ-основание", SourceDocumentNumberBox.Text);
            SetFieldValueIfExists(data, "Дата документа-основания", SourceDocumentDatePicker.SelectedDate ?? DateTime.Today);
            SetFieldValueIfExists(data, "Перечень ценностей", ItemsDescriptionBox.Text);
            SetFieldValueIfExists(data, "Количество", ReadDecimal(QuantityBox.Text));
            SetFieldValueIfExists(data, "Единица измерения", UnitNameBox.Text);
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
            if (_documentKind == FinanceDocumentKind.PowerOfAttorney)
            {
                CurrencyPanel.Visibility = Visibility.Collapsed;
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
}
