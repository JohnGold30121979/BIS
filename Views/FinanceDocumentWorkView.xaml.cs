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
        private AccountAnalyticsRegistry _accountRegistry = new();
        private bool _isLoading;

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

                DataGrid.ItemsSource = rows.Select(row => BuildRow(row, referenceMaps, _accountRegistry)).ToList();
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

        private FinanceDocumentRow BuildRow(
            Dictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps,
            AccountAnalyticsRegistry accountRegistry)
        {
            return new FinanceDocumentRow
            {
                Id = ReadGuid(row, "Id"),
                DocumentNumber = ReadString(row, "Номер", "doc_number"),
                DocumentDate = ReadDate(row, "Дата", "doc_date") ?? DateTime.Today,
                EmployeeName = ResolveReference(row, referenceMaps, "Сотрудник", "employee_id"),
                RepresentativeName = ResolveReference(row, referenceMaps, "Представитель", "representative_id"),
                CounterpartyName = ResolveReference(row, referenceMaps, "Поставщик", "counterparty_id", "Организация", "organization_id"),
                AdvancePaymentName = _documentKind == FinanceDocumentKind.AdvanceReport
                    ? ResolveAdvancePaymentDisplay(row, referenceMaps)
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
                await LoadDataAsync();
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
                await LoadDataAsync();
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
                var postings = await _metadataService.GetPostingsByDocumentAsync(
                    _documentMetadata.Name,
                    selected.DocumentNumber,
                    selected.DocumentDate);

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
            SetPostingDetail(detail, "Дата", posting.Date.ToString("dd.MM.yyyy"));
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
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Детали проводки: {posting.DocumentNumber}");
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

        private static string ResolveAdvancePaymentDisplay(
            IReadOnlyDictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps)
        {
            var fromLines = ResolveAdvancePaymentFromExpenseLines(ReadString(row, "Строки затрат", "expense_lines"));
            if (!string.IsNullOrWhiteSpace(fromLines))
                return fromLines;

            return ResolveReference(row, referenceMaps, "Вид авансового расчета", "advance_payment_id");
        }

        private static string ResolveAdvancePaymentFromExpenseLines(string json)
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

                    var code = item.TryGetProperty("PairCode", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty;
                    var name = item.TryGetProperty("PairName", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                    var display = FormatAdvancePairDisplay(code, name);
                    if (string.IsNullOrWhiteSpace(display) || values.Any(value => string.Equals(value, display, StringComparison.CurrentCultureIgnoreCase)))
                        continue;

                    values.Add(display);
                }

                return string.Join("; ", values);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatAdvancePairDisplay(string code, string name)
        {
            code = code.Trim();
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return name;
            if (string.IsNullOrWhiteSpace(name))
                return code;
            if (name.StartsWith(code + " -", StringComparison.CurrentCultureIgnoreCase))
                return name;

            return $"{code} - {name}";
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
                ({ } startDate, { } endDate) => $"{startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}",
                ({ } startDate, null) => $"с {startDate:dd.MM.yyyy}",
                (null, { } endDate) => $"по {endDate:dd.MM.yyyy}",
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


