using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using System;
using System.Collections.Generic;
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

        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private bool _isLoading;

        public CashOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
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
            if (DataGrid.SelectedItem is CashOrderRow selected)
            {
                StatusText.Text = selected.IsPosted
                    ? $"Проведен: {selected.OrderTypeDisplay}; Дт {selected.DebitAccount} / Кт {selected.CreditAccount}, {selected.Amount:N2} сом. Двойной щелчок откроет проводку."
                    : $"Не проведен: {selected.OrderTypeDisplay} {selected.DocNumber}, {selected.Amount:N2} сом.";
            }
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
                var catalogsByName = allCatalogs.ToDictionary(catalog => catalog.Name, catalog => catalog);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                var referenceCache = await BuildReferenceCacheAsync(documentRows, catalogsByName, accountAnalytics);

                var rows = documentRows
                    .Select(item => CreateCashOrderRow(item.Document, item.Row, referenceCache))
                    .OrderByDescending(row => row.DocDate)
                    .ThenByDescending(row => row.CreatedAt)
                    .ToList();

                DataGrid.ItemsSource = rows;
                DataGrid.Items.Refresh();
                StatusText.Text = $"📊 Загружено записей: {rows.Count}";
                UpdateButtonsState();
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

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not CashOrderRow selectedRow)
            {
                MessageBox.Show("Выберите документ для печати.", "Печатная форма",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var printFormService = new PrintFormService(context);
                await printFormService.SeedCashOrderFormsAsync();
                var formPrefix = selectedRow.IsReceipt ? "cash.receipt." : "cash.payment.";
                var forms = (await printFormService.GetPrintFormsAsync(_documentMetadata.Id))
                    .Where(form => form.Code.StartsWith(formPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (forms.Count == 0)
                {
                    MessageBox.Show("Для выбранного типа кассового ордера не настроены печатные формы.", "Печатная форма",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                    return;

                StatusText.Text = "Формирование PDF...";
                var pdf = await printFormService.ExportDocumentAsync(selectionDialog.SelectedReport, selectedRow.Id);
                StatusText.Text = "PDF сформирован";

                var previewWindow = new PdfPreviewWindow(pdf) { Owner = Window.GetWindow(this) };
                previewWindow.ShowDialog();
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка печати";
                MessageBox.Show($"Ошибка формирования печатной формы: {ex.Message}", "Печать",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

