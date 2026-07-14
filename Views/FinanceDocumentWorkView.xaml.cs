using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private bool _isLoading;

        public FinanceDocumentWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            _documentKind = FinanceDocumentKindHelper.FromName(documentMetadata.Name);

            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;
            ConfigureColumns();

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

                var rows = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_documentMetadata, _metadataService);
                var accountRegistry = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

                DataGrid.ItemsSource = rows.Select(row => BuildRow(row, referenceMaps, accountRegistry)).ToList();
                StatusText.Text = $"Загружено записей: {rows.Count}";
                UpdateButtonsState();
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
                AdvancePaymentName = ResolveReference(row, referenceMaps, "Вид авансового расчета", "advance_payment_id"),
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
            var isPowerOfAttorney = _documentKind == FinanceDocumentKind.PowerOfAttorney;
            var isPayrollStatement = _documentKind == FinanceDocumentKind.PayrollStatement;

            EmployeeColumn.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            RepresentativeColumn.Visibility = isPowerOfAttorney ? Visibility.Visible : Visibility.Collapsed;
            CounterpartyColumn.Visibility = isPowerOfAttorney ? Visibility.Visible : Visibility.Collapsed;
            AdvancePaymentColumn.Visibility = isAdvanceReport ? Visibility.Visible : Visibility.Collapsed;
            PeriodColumn.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            DebitAccountColumn.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            CreditAccountColumn.Visibility = isAdvanceReport || isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            PaymentAccountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            AmountColumn.Visibility = isPowerOfAttorney ? Visibility.Collapsed : Visibility.Visible;
            PayableAmountColumn.Visibility = isPayrollStatement ? Visibility.Visible : Visibility.Collapsed;
            ValidUntilColumn.Visibility = isPowerOfAttorney ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButtonsState()
        {
            var hasSelection = DataGrid.SelectedItem is FinanceDocumentRow;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            PostButton.IsEnabled = hasSelection;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtonsState();

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new FinanceDocumentDialog(_documentMetadata, _metadataService)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
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

            if (dialog.ShowDialog() == true)
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

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadDataAsync();

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
        PowerOfAttorney,
        PayrollStatement,
        Other
    }

    internal static class FinanceDocumentKindHelper
    {
        public static FinanceDocumentKind FromName(string documentName)
        {
            return documentName switch
            {
                "Авансовый отчет" => FinanceDocumentKind.AdvanceReport,
                "Доверенность" => FinanceDocumentKind.PowerOfAttorney,
                "Платежная ведомость" => FinanceDocumentKind.PayrollStatement,
                _ => FinanceDocumentKind.Other
            };
        }
    }
}
