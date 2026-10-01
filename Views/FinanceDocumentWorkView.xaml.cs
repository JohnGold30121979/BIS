using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class FinanceDocumentWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private readonly FinanceDocumentKind _documentKind;
        private readonly ObservableCollection<Dictionary<string, object>> _postingDetails = new();
        private List<FinanceDocumentRow> _allRows = new();
        private List<Dictionary<string, object>> _advancePaymentPairs = new();
        private AccountAnalyticsRegistry _accountRegistry = new();
        private bool _isLoading;

        /// <summary>
        /// Прежнее название модуля авансовых платежей. Проводки, созданные до
        /// переименования, лежат в базе с этим типом документа, поэтому при поиске
        /// проводок учитываются оба названия.
        /// </summary>
        private static readonly string[] LegacyAdvanceReportDocumentNames = { "Авансовый отчет" };

        public FinanceDocumentWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            _documentKind = FinanceDocumentKindHelper.FromName(documentMetadata.Name);

            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;
            PostingDetailsGrid.ItemsSource = _postingDetails;
            ConfigureColumns();
            InitializeReportPeriodDefaults();

            Loaded += async (_, _) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            try
            {
                StatusText.Text = "Загрузка данных...";
                UpdateButtonsState();

                var rows = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_documentMetadata, _metadataService);
                _accountRegistry = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                _advancePaymentPairs = _documentKind == FinanceDocumentKind.AdvanceReport
                    ? await _metadataService.GetAdvancePaymentPairsAsync()
                    : new List<Dictionary<string, object>>();

                _allRows = rows.Select(row => BuildRow(row, referenceMaps, _accountRegistry)).ToList();
                ApplyColumnFilters();
                StatusText.Text = $"Загружено записей: {rows.Count}";
                UpdateButtonsState();
                await UpdateSelectedPostingDetailsAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                UpdateButtonsState();
            }
        }

        private void ApplyColumnFilters()
        {
            IEnumerable<FinanceDocumentRow> query = _allRows;
            query = ApplyColumnFilter(query, NumberFilterBox.Text, row => row.DocumentNumber);
            query = ApplyColumnFilter(query, DateFilterBox.Text, row => row.DocumentDate.ToString("dd/MM/yyyy"));
            query = ApplyColumnFilter(query, AmountFilterBox.Text, row => FormatAmount(row.Amount));
            query = ApplyColumnFilter(query, DebitFilterBox.Text, row => row.DebitAccountDisplay);
            query = ApplyColumnFilter(query, CreditFilterBox.Text, row => row.CreditAccountDisplay);

            var filteredRows = query.ToList();
            DataGrid.ItemsSource = filteredRows;
            StatusText.Text = $"Показано записей: {filteredRows.Count} из {_allRows.Count}";
            UpdateButtonsState();
        }

        private void OnColumnFilterChanged(object sender, TextChangedEventArgs e) => ApplyColumnFilters();

        private static IEnumerable<FinanceDocumentRow> ApplyColumnFilter(
            IEnumerable<FinanceDocumentRow> query,
            string filter,
            Func<FinanceDocumentRow, string?> valueSelector)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return query;

            var filterText = filter.Trim();
            return query.Where(row => MatchesOrderedColumnFilter(valueSelector(row), filterText));
        }

        private static bool MatchesOrderedColumnFilter(string? value, string filterText)
        {
            var valueText = (value ?? string.Empty).Trim();
            if (valueText.Length == 0)
                return false;

            var normalizedFilter = filterText.Trim();
            var filterDigits = ExtractDigits(normalizedFilter);
            if (filterDigits.Length > 0)
                return ExtractDigits(valueText).StartsWith(filterDigits, StringComparison.Ordinal);

            return valueText.StartsWith(normalizedFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static string FormatAmount(decimal amount) => amount.ToString("N2");

        private FinanceDocumentRow BuildRow(
            Dictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps,
            AccountAnalyticsRegistry accountRegistry)
        {
            var documentDate = ReadDate(row, "Дата", "doc_date") ?? DateTime.Today;
            var expenseLinesJson = ReadString(row, "Строки затрат", "expense_lines");
            var lineDates = ReadExpenseLineDates(expenseLinesJson);

            return new FinanceDocumentRow
            {
                Id = ReadGuid(row, "Id"),
                DocumentNumber = ReadString(row, "Номер", "doc_number"),
                DocumentDate = documentDate,
                // Даты проводок берутся из строк затрат: дата проводки может отличаться
                // от даты документа, и поиск строго по дате документа терял проводки.
                PostingDateFrom = lineDates.Count > 0 ? lineDates.Min().AddDays(-1) : documentDate,
                PostingDateTo = lineDates.Count > 0 ? lineDates.Max().AddDays(1) : documentDate,
                EmployeeName = ResolveReference(row, referenceMaps, "Сотрудник", "employee_id"),
                RepresentativeName = ResolveReference(row, referenceMaps, "Представитель", "representative_id"),
                CounterpartyName = ResolveReference(row, referenceMaps, "Поставщик", "counterparty_id", "Организация", "organization_id"),
                AdvancePaymentName = _documentKind == FinanceDocumentKind.AdvanceReport
                    ? ResolveAdvancePaymentDisplay(row, expenseLinesJson)
                    : ResolveReference(row, referenceMaps, "Вид авансового расчета", "advance_payment_id"),
                PeriodDisplay = BuildPeriodDisplay(row),
                DebitAccountDisplay = ResolveAccount(row, accountRegistry, "Счет дебета", "debit_account"),
                CreditAccountDisplay = ResolveAccount(row, accountRegistry, "Счет кредита", "credit_account"),
                PaymentAccountDisplay = ResolveAccount(row, accountRegistry, "Счет выплаты", "payment_account"),
                Amount = ReadDecimal(row, "Сумма", "amount"),
                PayableAmount = ReadDecimal(row, "К выплате", "payable_amount"),
                ValidUntil = ReadDate(row, "Срок действия", "valid_until"),
                Basis = ResolveBasis(row),
                IsPosted = ReadBool(row, "Проведен", "Проведён", "is_posted"),
                CreatedAt = ReadDate(row, "CreatedAt") ?? DateTime.Today
            };
        }

        /// <summary>Даты строк затрат из сохранённого JSON документа.</summary>
        private static List<DateTime> ReadExpenseLineDates(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<DateTime>();

            var dates = new List<DateTime>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return dates;

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    if (item.TryGetProperty("LineDate", out var dateElement) &&
                        dateElement.ValueKind == JsonValueKind.String &&
                        dateElement.TryGetDateTime(out var lineDate))
                    {
                        dates.Add(lineDate.Date);
                    }
                }
            }
            catch
            {
                return dates;
            }

            return dates;
        }

        private void ConfigureColumns()
        {
            var isAdvanceReport = _documentKind == FinanceDocumentKind.AdvanceReport;
            var isPayrollStatement = _documentKind == FinanceDocumentKind.PayrollStatement;

            EmployeeColumn.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            AdvancePaymentColumn.Header = "Пары счетов";
            AdvancePaymentColumn.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            PeriodColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            DebitAccountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            CreditAccountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            PaymentAccountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            AmountColumn.Visibility = Visibility.Visible;
            PayableAmountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            ReportPeriodPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            PostedColumn.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            PostButton.Visibility = isAdvanceReport ? Visibility.Collapsed : Visibility.Visible;
            PostingDetailsPanel.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InitializeReportPeriodDefaults()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport)
                return;

            var today = DateTime.Today;
            ReportStartDatePicker.SelectedDate = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            ReportEndDatePicker.SelectedDate = today;
        }
        private void UpdateButtonsState()
        {
            var hasSelection = DataGrid.SelectedItem is FinanceDocumentRow;
            AddButton.IsEnabled = !_isLoading;
            RefreshButton.IsEnabled = !_isLoading;
            EditButton.IsEnabled = !_isLoading && hasSelection;
            DeleteButton.IsEnabled = !_isLoading && hasSelection;
            PostButton.IsEnabled = !_isLoading && hasSelection;
            ExportTurnoverExcelButton.IsEnabled = !_isLoading;
            ReportStartDatePicker.IsEnabled = !_isLoading;
            ReportEndDatePicker.IsEnabled = !_isLoading;
        }

        private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            await UpdateSelectedPostingDetailsAsync();
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var dialog = new FinanceDocumentDialog(_documentMetadata, _metadataService)
            {
                Owner = Window.GetWindow(this)
            };

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    _documentMetadata.Name) == true)
            {
                await LoadDataAsync();
                // Остаёмся на только что созданном документе.
                DataGrid.SelectRowById(dialog.SavedRecordId, row => (row as FinanceDocumentRow)?.Id);
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e) => await EditSelectedAsync();

        private async void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => await EditSelectedAsync();

        private async Task EditSelectedAsync()
        {
            if (DataGrid.SelectedItem is not FinanceDocumentRow selected)
                return;

            var dialog = new FinanceDocumentDialog(_documentMetadata, _metadataService, selected.Id)
            {
                Owner = Window.GetWindow(this)
            };

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    $"Редактирование: {_documentMetadata.Name}") == true)
            {
                await LoadDataAsync();
                // Возвращаем выделение на отредактированный документ.
                DataGrid.SelectRowById(dialog.SavedRecordId, row => (row as FinanceDocumentRow)?.Id);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not FinanceDocumentRow selected)
                return;

            if (MessageBox.Show("Удалить документ?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selected.Id);
            await LoadDataAsync();
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not FinanceDocumentRow selected)
                return;

            if (MessageBox.Show("Провести документ?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StatusText.Text = "Проведение документа...";
                await _metadataService.PostDocumentAsync(_documentMetadata.Id, selected.Id);
                await LoadDataAsync();
                MessageBox.Show("Документ проведен.", "Проведение",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проведения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnExportTurnoverExcelClick(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var startDate = ReportStartDatePicker.SelectedDate?.Date ?? DateTime.Today.Date;
            var endDate = ReportEndDatePicker.SelectedDate?.Date ?? DateTime.Today.Date;
            if (endDate < startDate)
            {
                MessageBox.Show("Дата окончания отчета не может быть меньше даты начала.", "Оборотка Excel",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isLoading = true;
            try
            {
                StatusText.Text = "Формирование оборотной ведомости Excel...";
                UpdateButtonsState();

                await using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var reportService = new AdvancePaymentsTurnoverReportService(context);
                var filePath = await reportService.ExportExcelAsync(startDate, endDate);

                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                StatusText.Text = $"Открыт отчет Excel: {System.IO.Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка Excel: {ex.Message}";
                MessageBox.Show($"Ошибка формирования оборотной ведомости Excel: {ex.Message}", "Оборотка Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                UpdateButtonsState();
            }
        }
        private async Task UpdateSelectedPostingDetailsAsync()
        {
            if (_documentKind != FinanceDocumentKind.AdvanceReport)
                return;

            _postingDetails.Clear();
            if (DataGrid?.SelectedItem is not FinanceDocumentRow selected)
            {
                SetDetailColumnsVisibility(false, false, false);
                _postingDetails.Add(PostingDetailRowFactory.Create(("Документ", "Выберите авансовый платеж в списке выше")));
                return;
            }

            try
            {
                var selectedId = selected.Id;
                var postings = await _metadataService.GetPostingsByDocumentRangeAsync(
                    _documentMetadata.Name,
                    selected.DocumentNumber,
                    selected.PostingDateFrom,
                    selected.PostingDateTo,
                    LegacyAdvanceReportDocumentNames);

                if (DataGrid?.SelectedItem is not FinanceDocumentRow current || current.Id != selectedId)
                    return;

                if (postings.Count == 0)
                {
                    SetDetailColumnsVisibility(false, false, false);
                    _postingDetails.Add(PostingDetailRowFactory.Create(("Документ", "Проводки по выбранному авансовому платежу не найдены")));
                    return;
                }

                var showCurrency = postings.Any(posting => posting.AmountCurrency != 0m || !string.IsNullOrWhiteSpace(posting.Currency));
                var showOrganization = postings.Any(posting => !string.IsNullOrWhiteSpace(posting.Organization));
                var showEmployee = postings.Any(posting => !string.IsNullOrWhiteSpace(posting.Employee));
                SetDetailColumnsVisibility(showCurrency, showOrganization, showEmployee);

                foreach (var posting in postings)
                    _postingDetails.Add(CreatePostingDetailRow(posting));
            }
            catch (Exception ex)
            {
                SetDetailColumnsVisibility(false, false, false);
                _postingDetails.Add(PostingDetailRowFactory.Create(("Документ", $"Ошибка загрузки проводок: {ex.Message}")));
            }
        }

        private Dictionary<string, object> CreatePostingDetailRow(PostingViewModel posting)
        {
            var detail = PostingDetailRowFactory.Create();
            SetPostingDetail(detail, "Документ", posting.DocumentNumber);
            SetPostingDetail(detail, "Тип документа", posting.DocumentType);
            SetPostingDetail(detail, "Дата", posting.Date.ToString("dd/MM/yyyy"));
            SetPostingDetail(detail, "Модуль", posting.ModuleName);
            SetPostingDetail(detail, "Дебет", ExtractAccountCode(posting.DebitAccount));
            SetPostingDetail(detail, "Кредит", ExtractAccountCode(posting.CreditAccount));
            SetPostingDetail(detail, "Сумма", posting.Amount.ToString("N2"));
            SetPostingDetail(detail, "Сумма вал.", posting.AmountCurrency != 0m ? posting.AmountCurrency.ToString("N2") : null);
            SetPostingDetail(detail, "Валюта", posting.Currency);
            SetPostingDetail(detail, "Организация", posting.Organization);
            SetPostingDetail(detail, "Сотрудник", posting.Employee);
            SetPostingDetail(detail, "Статус", posting.IsActive ? "Активна" : "Неактивна");
            SetPostingDetail(detail, "Примечание", posting.Note);
            detail["__posting"] = posting;
            return detail;
        }

        private void SetDetailColumnsVisibility(bool showCurrency, bool showOrganization, bool showEmployee)
        {
            DetailAmountCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            DetailCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            DetailOrganizationColumn.Visibility = showOrganization ? Visibility.Visible : Visibility.Collapsed;
            DetailEmployeeColumn.Visibility = showEmployee ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PostingDetailsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PostingDetailsGrid.SelectedItem is not Dictionary<string, object> row ||
                !row.TryGetValue("__posting", out var value) ||
                value is not PostingViewModel posting)
            {
                return;
            }

            var dialog = new PostingDetailsDialog(posting)
            {
                Owner = Window.GetWindow(this)
            };
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Детали проводки: {posting.DocumentNumber}", null, fillWorkspace: true);
        }

        private static void SetPostingDetail(Dictionary<string, object> detail, string field, string? value) =>
            PostingDetailRowFactory.Set(detail, field, value);

        private static string ExtractAccountCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            var separatorIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? trimmed[..separatorIndex].Trim() : trimmed;
        }
        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadDataAsync();

        /// <summary>
        /// Колонка «Пары счетов» показывает сами счета пары («Дт 602 / Кт 303»).
        /// Раньше здесь выводилось наименование вида расчёта («3 - Расчеты с
        /// подотчетными лицами»), хотя колонка подписана как пары счетов.
        /// </summary>
        private string ResolveAdvancePaymentDisplay(
            IReadOnlyDictionary<string, object> row,
            string expenseLinesJson)
        {
            var fromLines = ResolveAdvancePairAccountsFromExpenseLines(expenseLinesJson);
            if (!string.IsNullOrWhiteSpace(fromLines))
                return fromLines;

            var fromPair = ResolveAdvancePairAccountsFromPairCatalog(row);
            if (!string.IsNullOrWhiteSpace(fromPair))
                return fromPair;

            return FormatAdvancePairAccounts(
                ReadString(row, "Счет дебета", "debit_account"),
                ReadString(row, "Счет кредита", "credit_account"));
        }

        private string ResolveAdvancePairAccountsFromPairCatalog(IReadOnlyDictionary<string, object> row)
        {
            if (!TryGetGuid(ReadString(row, "Вид авансового расчета", "advance_payment_id"), out var pairId))
                return string.Empty;

            var pair = _advancePaymentPairs.FirstOrDefault(item =>
                TryGetGuid(item.GetValueOrDefault("Id")?.ToString(), out var id) && id == pairId);
            if (pair == null)
                return string.Empty;

            return FormatAdvancePairAccounts(
                ReadString(pair, "debit_account", "Дебет"),
                ReadString(pair, "credit_account", "Кредит"));
        }

        private static string ResolveAdvancePairAccountsFromExpenseLines(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                    return string.Empty;

                var values = new List<string>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var debit = item.TryGetProperty("DebitAccount", out var debitElement) ? debitElement.GetString() ?? string.Empty : string.Empty;
                    var credit = item.TryGetProperty("CreditAccount", out var creditElement) ? creditElement.GetString() ?? string.Empty : string.Empty;
                    var display = FormatAdvancePairAccounts(debit, credit);
                    if (string.IsNullOrWhiteSpace(display) ||
                        values.Any(value => string.Equals(value, display, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        continue;
                    }

                    values.Add(display);
                }

                return string.Join("; ", values);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Пара счетов одной строки: «Дт 602 / Кт 303».</summary>
        private static string FormatAdvancePairAccounts(string? debitAccount, string? creditAccount)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(debitAccount))
                parts.Add($"Дт {debitAccount.Trim()}");
            if (!string.IsNullOrWhiteSpace(creditAccount))
                parts.Add($"Кт {creditAccount.Trim()}");

            return string.Join(" / ", parts);
        }
        private static string ResolveReference(
            IReadOnlyDictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (Guid.TryParse(value.ToString(), out var id))
                {
                    foreach (var mapKey in keys)
                    {
                        if (referenceMaps.TryGetValue(mapKey, out var map) && map.TryGetValue(id, out var displayName))
                            return displayName;
                    }
                }

                return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string ResolveAccount(
            IReadOnlyDictionary<string, object> row,
            AccountAnalyticsRegistry accountRegistry,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                var account = accountRegistry.FindAccount(value);
                return account?.DisplayName ?? value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string BuildPeriodDisplay(IReadOnlyDictionary<string, object> row)
        {
            var start = ReadDate(row, "Дата начала отчета", "report_start_date", "Дата начала периода", "period_start_date");
            var end = ReadDate(row, "Дата окончания отчета", "report_end_date", "Дата окончания периода", "period_end_date");
            return (start, end) switch
            {
                ({ } startDate, { } endDate) => $"{startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}",
                ({ } startDate, null) => $"с {startDate:dd/MM/yyyy}",
                (null, { } endDate) => $"по {endDate:dd/MM/yyyy}",
                _ => string.Empty
            };
        }

        private static string ResolveBasis(IReadOnlyDictionary<string, object> row)
        {
            var basis = ReadString(row, "Основание", "basis");
            if (!string.IsNullOrWhiteSpace(basis))
                return basis;

            basis = ReadString(row, "Документ-основание", "source_document_number");
            if (!string.IsNullOrWhiteSpace(basis))
                return basis;

            return ReadString(row, "Перечень ценностей", "items_description", "Примечание", "description");
        }

        private static Guid ReadGuid(IReadOnlyDictionary<string, object> row, string key)
        {
            return row.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out var id)
                ? id
                : Guid.Empty;
        }

        private static bool TryGetGuid(string? value, out Guid id)
        {
            return Guid.TryParse(value, out id);
        }

        private static string ReadString(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static decimal ReadDecimal(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is decimal decimalValue)
                    return decimalValue;

                if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue))
                    return currentValue;

                if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue))
                    return invariantValue;
            }

            return 0m;
        }

        private static DateTime? ReadDate(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is DateTime dateValue)
                    return dateValue;

                if (DateTime.TryParse(value.ToString(), out var parsedDate))
                    return parsedDate;
            }

            return null;
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
                                   text.Equals("1", StringComparison.OrdinalIgnoreCase),
                    _ => false
                };
            }

            return false;
        }
    }

    public sealed class FinanceDocumentRow
    {
        public Guid Id { get; set; }
        public string DocumentNumber { get; set; } = string.Empty;
        public DateTime DocumentDate { get; set; }

        /// <summary>Начало интервала поиска проводок документа.</summary>
        public DateTime PostingDateFrom { get; set; }

        /// <summary>Конец интервала поиска проводок документа.</summary>
        public DateTime PostingDateTo { get; set; }

        public string EmployeeName { get; set; } = string.Empty;
        public string RepresentativeName { get; set; } = string.Empty;
        public string CounterpartyName { get; set; } = string.Empty;
        public string AdvancePaymentName { get; set; } = string.Empty;
        public string PeriodDisplay { get; set; } = string.Empty;
        public string DebitAccountDisplay { get; set; } = string.Empty;
        public string CreditAccountDisplay { get; set; } = string.Empty;
        public string PaymentAccountDisplay { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal PayableAmount { get; set; }
        public DateTime? ValidUntil { get; set; }
        public string Basis { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string IsPostedDisplay => LocalizationService.DisplayValue(IsPosted);
        public DateTime CreatedAt { get; set; }
    }

    internal enum FinanceDocumentKind
    {
        AdvanceReport,
        PayrollStatement,
        Other
    }

    internal static class FinanceDocumentKindHelper
    {
        public static FinanceDocumentKind FromName(string documentName)
        {
            return documentName switch
            {
                "Авансовый отчет" or "Авансовые платежи" => FinanceDocumentKind.AdvanceReport,
                "Платежная ведомость" => FinanceDocumentKind.PayrollStatement,
                _ => FinanceDocumentKind.Other
            };
        }
    }
}



