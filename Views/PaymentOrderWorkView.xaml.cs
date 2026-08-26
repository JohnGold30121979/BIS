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
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Views
{
    public partial class PaymentOrderWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private const string PaymentOrderPrintReportCode = "standard.frx.finance.payment-order.pr-pl23";
        private List<PaymentOrderRow> _rows = new();
        private readonly ObservableCollection<Dictionary<string, object>> _postingDetails = new();
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private string _moduleName = string.Empty;
        private bool _isLoading;
        private bool _isPrinting;

        public PaymentOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;

            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;
            PostingDetailsGrid.ItemsSource = _postingDetails;

            Loaded += async (s, e) => await LoadData();
        }

        private void UpdateButtonsState()
        {
            var row = DataGrid.SelectedItem as PaymentOrderRow;
            bool hasSelection = row != null;
            bool isPosted = row?.IsPosted == true;
            bool canEdit = hasSelection && !isPosted;

            EditButton.IsEnabled = canEdit;
            DeleteButton.IsEnabled = hasSelection;
            PostButton.IsEnabled = hasSelection;
            PrintButton.IsEnabled = hasSelection && !_isPrinting;
            PostButton.Content = isPosted ? "↩ Отменить проведение" : "✅ Провести";
            PostButton.Background = isPosted ? Brushes.DarkOrange : Brushes.MediumPurple;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            UpdateSelectedPostingDetails();
        }

        private async void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow row)
                return;

            if (!row.IsPosted)
            {
                MessageBox.Show("Документ еще не проведен, проводки для просмотра нет.", "Проводки",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var documentType = ResolvePaymentOrderPostingType(row.OrderType);
            var postings = await _metadataService.GetPostingsByDocumentAsync(documentType, row.DocNumber, row.DocDate);
            if (postings.Count == 0)
            {
                MessageBox.Show("Связанная проводка в журнале не найдена.", "Проводки",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (postings.Count > 1)
            {
                var allPostingsDialog = new DocumentPostingsDialog(documentType, row.DocNumber, postings)
                {
                    Owner = Window.GetWindow(this)
                };
                MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), allPostingsDialog, $"Все проводки: {row.DocNumber}");
                return;
            }

            var details = new PostingDetailsDialog(postings[0])
            {
                Owner = Window.GetWindow(this)
            };
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), details, $"Детали проводки: {row.DocNumber}");
        }

        private void UpdateSelectedPostingDetails()
        {
            _postingDetails.Clear();

            if (DataGrid?.SelectedItem is not PaymentOrderRow row)
            {
                SetDetailColumnsVisibility(false, false, false, false);
                _postingDetails.Add(PostingDetailRowFactory.Create(("Документ", "Выберите платежное поручение в списке выше")));
                return;
            }

            var selectedSettings = new[]
            {
                _accountAnalytics.GetSettingsByCode(row.OurAccountName),
                _accountAnalytics.GetSettingsByCode(row.CorrespondentAccountName)
            };
            var showCurrency = ShouldShowPostingAnalytic("Валюта", "Справочник валют", selectedSettings);
            var showOrganization = ShouldShowPostingAnalytic("Организация", "Организации", selectedSettings);
            var showEmployee = ShouldShowPostingAnalytic("Сотрудник", "Сотрудники (Списочный состав)", selectedSettings);
            var showMaterial = ShouldShowPostingAnalytic("Материал", "Справочник материалов", selectedSettings);
            SetDetailColumnsVisibility(showCurrency, showOrganization, showEmployee, showMaterial);

            var detail = PostingDetailRowFactory.Create();
            SetPostingDetail(detail, "Документ", row.DocNumber);
            SetPostingDetail(detail, "Тип документа", ResolvePaymentOrderPostingType(row.OrderType));
            SetPostingDetail(detail, "Дата", row.DocDate.ToString("dd.MM.yyyy"));
            SetPostingDetail(detail, "Модуль", _moduleName);
            SetPostingDetail(detail, "Дебет", ExtractAccountCode(row.OurAccountName));
            SetPostingDetail(detail, "Кредит", ExtractAccountCode(row.CorrespondentAccountName));
            SetPostingDetail(detail, "Сумма", row.Amount.ToString("N2"));

            if (showCurrency)
            {
                SetPostingDetail(detail, "Сумма вал.", row.AmountCurrency != 0m ? row.AmountCurrency.ToString("N2") : null);
                SetPostingDetail(detail, "Валюта", row.CurrencyName);
            }

            if (showOrganization)
                SetPostingDetail(detail, "Организация", row.OrganizationName);

            if (showEmployee)
                SetPostingDetail(detail, "Сотрудник", row.EmployeeName);

            if (showMaterial)
                SetPostingDetail(detail, "Материал", row.MaterialName);

            var note = string.IsNullOrWhiteSpace(row.Description) ? row.Purpose : row.Description;
            SetPostingDetail(detail, "Статус", row.IsPosted ? "Проведён" : "Не проведён");
            SetPostingDetail(detail, "Примечание", note);
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
        private async Task LoadData()
        {
            if (_isLoading)
                return;

            _isLoading = true;
            try
            {
                StatusText.Text = "Загрузка данных...";
                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                _moduleName = await _metadataService.GetAssignedModuleNameAsync(_documentMetadata.Id, _documentMetadata.ObjectType) ?? string.Empty;
                var referenceCache = await LoadReferenceCacheAsync(catalogsDict);

                await LoadOurSettlementAccountsFallbackAsync(catalogsDict, referenceCache);

                _rows = data.Select(row => BuildRow(row, referenceCache, _accountAnalytics)).ToList();
                DataGrid.ItemsSource = _rows;
                UpdateAnalyticColumns(data, _accountAnalytics);
                StatusText.Text = $"📊 Загружено записей: {_rows.Count}";
                UpdateButtonsState();
                UpdateSelectedPostingDetails();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task<Dictionary<string, Dictionary<Guid, string>>> LoadReferenceCacheAsync(
            Dictionary<string, MetadataObject> catalogsDict)
        {
            var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();
            foreach (var field in _documentMetadata.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    continue;

                var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                var dict = new Dictionary<Guid, string>();
                foreach (var item in refData)
                {
                    if (item.TryGetValue("Id", out var rawId) && Guid.TryParse(rawId?.ToString(), out var id))
                        dict[id] = ReferenceDisplayHelper.BuildDisplayValue(item, field);
                }

                referenceCache[field.Name] = dict;
                if (!string.IsNullOrWhiteSpace(field.DbColumnName))
                    referenceCache[field.DbColumnName] = dict;
            }

            return referenceCache;
        }

        private async Task LoadOurSettlementAccountsFallbackAsync(
            Dictionary<string, MetadataObject> catalogsDict,
            Dictionary<string, Dictionary<Guid, string>> referenceCache)
        {
            if (referenceCache.ContainsKey("Наш счет") || !catalogsDict.TryGetValue("Расчетные счета организаций", out var accountCatalog))
                return;

            try
            {
                var accountsData = await _metadataService.GetCatalogDataAsync(accountCatalog.Id);
                var bankDict = new Dictionary<Guid, string>();
                if (catalogsDict.TryGetValue("Банки", out var bankCatalog))
                {
                    var banksData = await _metadataService.GetCatalogDataAsync(bankCatalog.Id);
                    foreach (var bank in banksData)
                    {
                        if (bank.TryGetValue("Id", out var rawBankId) && Guid.TryParse(rawBankId?.ToString(), out var bankId))
                        {
                            bankDict[bankId] = ReadString(bank, "Наименование банка", "name");
                        }
                    }
                }

                var ourAccountDict = new Dictionary<Guid, string>();
                foreach (var account in accountsData)
                {
                    if (!account.TryGetValue("Id", out var rawId) || !Guid.TryParse(rawId?.ToString(), out var accountId))
                        continue;

                    var accountNumber = ReadString(account, "Счет", "account_number");
                    var bankName = string.Empty;
                    if (Guid.TryParse(ReadString(account, "Банк", "bank_id"), out var bankId))
                        bankDict.TryGetValue(bankId, out bankName);

                    ourAccountDict[accountId] = string.IsNullOrWhiteSpace(bankName)
                        ? accountNumber
                        : $"{accountNumber} - {bankName}";
                }

                referenceCache["Наш счет"] = ourAccountDict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки расчетных счетов: {ex.Message}");
            }
        }

        private PaymentOrderRow BuildRow(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var result = new PaymentOrderRow
            {
                Id = Guid.TryParse(ReadString(row, "Id"), out var id) ? id : Guid.NewGuid(),
                OrganizationId = ReadGuid(row, "Организация", "organization_id"),
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(ReadString(row, "Номер", "doc_number", "number")),
                DocDate = ReadDate(row, DateTime.Now, "Дата", "doc_date", "date"),
                OrderType = ReadString(row, "Тип", "order_type"),
                Amount = ReadDecimal(row, "Сумма", "amount"),
                AmountCurrency = ReadDecimal(row, "Сумма в валюте", "amount_currency"),
                ExchangeRate = ReadDecimal(row, "Курс", "exchange_rate"),
                Purpose = ReadString(row, "Назначение платежа", "purpose"),
                Description = ReadString(row, "Примечание", "description"),
                IsPosted = ReadBool(row, "Проведён", "is_posted"),
                CreatedAt = ReadDate(row, DateTime.Now, "CreatedAt"),
                UpdatedAt = ReadDate(row, DateTime.Now, "UpdatedAt")
            };

            result.OrganizationName = ResolveReferenceOrRaw(row, referenceCache, "Организация", "organization_id");
            result.BankName = ResolveReferenceOrRaw(row, referenceCache, "Банк", "bank_id");
            result.CurrencyName = ResolveReferenceOrRaw(row, referenceCache, "Валюта", "currency_id");
            result.EmployeeName = ResolveReferenceOrRaw(row, referenceCache, "Сотрудник", "employee_id");
            result.MaterialName = ResolveReferenceOrRaw(row, referenceCache, "Материал", "material_id");
            result.PaymentClassificationName = ResolveReferenceOrRaw(row, referenceCache, "Классификация платежа", "payment_classification_id");
            result.OurAccountName = ResolveAccountDisplay(row, referenceCache, accountAnalytics,
                "our_account_id", "Наш счет", "Дебет", "debit_account");
            result.CorrespondentAccountName = ResolveAccountDisplay(row, referenceCache, accountAnalytics,
                "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Кредит", "credit_account");

            return result;
        }

        private static string ResolveReferenceOrRaw(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            params string[] keys)
        {
            var value = GetFirstValue(row, keys);
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            if (Guid.TryParse(value, out var id) && TryResolveReference(referenceCache, id, out var displayName, keys))
                return displayName;

            return value;
        }

        private static string ResolveAccountDisplay(
            Dictionary<string, object> row,
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            AccountAnalyticsRegistry accountAnalytics,
            params string[] keys)
        {
            var value = GetFirstValue(row, keys);
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var account = accountAnalytics.FindAccount(value);
            if (account != null)
                return account.Code;

            if (Guid.TryParse(value, out var id) && TryResolveReference(referenceCache, id, out var displayName, keys))
                return ExtractAccountCode(displayName);

            return ExtractAccountCode(value);
        }

        private static string ExtractAccountCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var separatorIndex = value.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? value[..separatorIndex].Trim()
                : value.Trim();
        }

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = new[]
            {
                "our_account_id", "Наш счет", "Дебет", "debit_account",
                "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Кредит", "credit_account"
            };

            OrganizationColumn.Visibility = GetAnalyticColumnVisibility(
                "Организация", "Организации", rows, accountFields, accountAnalytics);
            var currencyVisibility = GetAnalyticColumnVisibility(
                "Валюта", "Справочник валют", rows, accountFields, accountAnalytics);
            CurrencyColumn.Visibility = currencyVisibility;
            AmountCurrencyColumn.Visibility = currencyVisibility;
            ExchangeRateColumn.Visibility = currencyVisibility;
            EmployeeColumn.Visibility = GetAnalyticColumnVisibility(
                "Сотрудник", "Сотрудники (Списочный состав)", rows, accountFields, accountAnalytics);
            MaterialColumn.Visibility = GetAnalyticColumnVisibility(
                "Материал", "Справочник материалов", rows, accountFields, accountAnalytics);
        }

        private static Visibility GetAnalyticColumnVisibility(
            string fieldName,
            string referenceCatalog,
            List<Dictionary<string, object>> rows,
            IEnumerable<string> accountFields,
            AccountAnalyticsRegistry registry)
        {
            return AccountAnalyticsRules.ShouldShowFieldForRows(
                    fieldName, rows, accountFields, registry, referenceCatalog)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static string ReadString(Dictionary<string, object>? row, params string[] keys)
        {
            return GetFirstValue(row, keys);
        }

        private static decimal ReadDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is decimal decimalValue)
                    return decimalValue;

                if (decimal.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return 0m;
        }

        private static bool ReadBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is bool boolValue)
                    return boolValue;

                if (bool.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return false;
        }

        private static DateTime ReadDate(Dictionary<string, object> row, DateTime fallback, params string[] keys)
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

            return fallback;
        }

        private static string GetFirstValue(Dictionary<string, object>? row, params string[] keys)
        {
            if (row == null)
                return string.Empty;

            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static bool TryResolveReference(
            Dictionary<string, Dictionary<Guid, string>> referenceCache,
            Guid id,
            out string displayName,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (referenceCache.TryGetValue(key, out var dict) && dict.TryGetValue(id, out displayName))
                    return true;
            }

            displayName = string.Empty;
            return false;
        }

        private static string ResolvePaymentOrderPostingType(string orderType)
        {
            return orderType.Contains("Вход", StringComparison.OrdinalIgnoreCase)
                ? "Входящее платежное поручение"
                : "Исходящее платежное поручение";
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService)
            {
                Owner = Window.GetWindow(this)
            };
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    "Платежное поручение") == true)
                await LoadData();
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected || selected.IsPosted)
                return;

            var dialog = new PaymentOrderDialog(_documentMetadata, _metadataService, selected.Id)
            {
                Owner = Window.GetWindow(this)
            };
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    "Редактирование платежного поручения") == true)
                await LoadData();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected)
                return;

            string message = selected.IsPosted
                ? "Удалить документ? Это также удалит связанные проводки из журнала."
                : "Удалить документ?";
            if (MessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, selected.Id);
            await LoadData();
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not PaymentOrderRow selected)
                return;

            if (selected.IsPosted)
            {
                if (MessageBox.Show("Отменить проведение документа? Связанные проводки будут удалены из журнала.",
                        "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                try
                {
                    StatusText.Text = "🔄 Отмена проведения...";
                    await _metadataService.UnpostDocumentAsync(_documentMetadata.Id, selected.Id);
                    await LoadData();
                    MessageBox.Show("Проведение отменено.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка отмены проведения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                }

                return;
            }

            if (selected.Amount <= 0)
            {
                MessageBox.Show("Сумма должна быть больше 0 для проведения.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show("Провести документ?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                StatusText.Text = "🔄 Проведение...";
                await _metadataService.PostDocumentAsync(_documentMetadata.Id, selected.Id);
                await LoadData();
                MessageBox.Show("Документ проведён!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проведения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StatusText.Text = "✅ Готово";
            }
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (_isPrinting || DataGrid.SelectedItem is not PaymentOrderRow selected)
                return;

            _isPrinting = true;
            UpdateButtonsState();
            try
            {
                StatusText.Text = "Подготовка печатных форм платежного поручения...";
                using var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                await new MetadataService(context).EnsurePaymentOrderPrintFormAsync();

                var printFormService = new PrintFormService(context);
                await printFormService.SeedPaymentOrderFormsAsync();
                var forms = await printFormService.GetPrintFormsAsync(_documentMetadata.Id, includeInactive: false);
                if (forms.Count == 0)
                {
                    MessageBox.Show("Для платежного поручения нет доступных печатных форм.",
                        "Платежное поручение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusText.Text = "Печатные формы не найдены";
                    return;
                }

                Report selectedReport;
                var selectedFormat = PrintFormOutputFormat.Pdf;
                if (ChoosePrintFormCheckBox.IsChecked == true)
                {
                    var selectionDialog = new PrintFormSelectionDialog(forms)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                    {
                        StatusText.Text = "Печать отменена";
                        return;
                    }

                    selectedReport = selectionDialog.SelectedReport;
                    selectedFormat = selectionDialog.SelectedFormat;
                }
                else
                {
                    selectedReport = SelectPaymentOrderPrintForm(forms);
                }

                StatusText.Text = selectedFormat == PrintFormOutputFormat.Excel
                    ? "Формирование Excel платежного поручения..."
                    : "Формирование PDF платежного поручения...";

                var output = selectedFormat == PrintFormOutputFormat.Excel
                    ? await printFormService.ExportDocumentExcelAsync(selectedReport, selected.Id)
                    : await printFormService.ExportDocumentAsync(selectedReport, selected.Id);

                var outputPath = await PrintFormOutputFileService.SaveAndOpenAsync(output, selectedReport.Name, selectedFormat);
                StatusText.Text = $"Печатная форма открыта: {outputPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка печати: {ex.Message}";
                MessageBox.Show($"Ошибка предпросмотра платежного поручения: {ex.Message}",
                    "Платежное поручение", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isPrinting = false;
                UpdateButtonsState();
            }
        }

        private static Report SelectPaymentOrderPrintForm(IReadOnlyList<Report> forms)
        {
            return forms.FirstOrDefault(form => string.Equals(form.Code, "payment.order.native", StringComparison.OrdinalIgnoreCase))
                ?? forms.FirstOrDefault(form => form.IsDefault &&
                    !string.Equals(form.Code, PaymentOrderPrintReportCode, StringComparison.OrdinalIgnoreCase))
                ?? forms.FirstOrDefault(form => form.IsDefault)
                ?? forms.First();
        }

        private async Task<DataTable> BuildPaymentOrderPrintTableAsync(PaymentOrderRow row, BIS.ERP.Data.AppDbContext context)
        {
            var table = new DataTable("Платежное поручение");
            void AddColumn(string name, Type? type = null)
            {
                if (!table.Columns.Contains(name))
                    table.Columns.Add(name, type ?? typeof(string));
            }

            var columns = new[]
            {
                "Номер", "Дата", "Тип", "Сторона А", "Сторона Б",
                "side_a_name", "side_a_inn", "side_a_bank", "side_a_bic", "side_a_account",
                "side_b_name", "side_b_inn", "side_b_bank", "side_b_bic", "side_b_account",
                "payer_name", "payer_inn", "payer_bank", "payer_bic", "payer_account",
                "receiver_name", "receiver_inn", "receiver_bank", "receiver_bic", "receiver_account",
                "namep1", "namep2", "inn1", "inn2", "mc_bank", "md_bank", "mc_mfo", "md_mfo",
                "mt_adr1", "mt_adr2", "rschc", "rschd", "rschk", "dok1", "dat1", "KODPL_1",
                "sch1", "sum1", "sum2", "MSUM1", "NA1", "LL", "Назначение платежа", "Примечание",
                "Дебет", "Кредит", "Сумма", "Модуль", "DATE()", "_PAGENO"
            };
            foreach (var column in columns)
                AddColumn(column, column is "Дата" or "DATE()" ? typeof(DateTime) : typeof(string));

            var printMetadataService = new MetadataService(context);
            var (ownParty, counterpartyParty) = await LoadPaymentOrderPartiesAsync(
                printMetadataService,
                row.OrganizationId,
                row.OrganizationName);

            var isIncoming = row.OrderType.Contains("Вход", StringComparison.OrdinalIgnoreCase);
            var payerParty = isIncoming ? counterpartyParty : ownParty;
            var receiverParty = isIncoming ? ownParty : counterpartyParty;
            ApplyPaymentOrderFallbacks(row, payerParty, receiverParty);

            var debit = ExtractAccountCode(row.OurAccountName);
            var credit = ExtractAccountCode(row.CorrespondentAccountName);

            var dataRow = table.NewRow();
            dataRow["Номер"] = row.DocNumber;
            dataRow["Дата"] = row.DocDate;
            dataRow["Тип"] = row.OrderType;
            dataRow["Сторона А"] = payerParty.Name;
            dataRow["Сторона Б"] = receiverParty.Name;
            FillPartyColumns(dataRow, "side_a", payerParty);
            FillPartyColumns(dataRow, "side_b", receiverParty);
            FillPartyColumns(dataRow, "payer", payerParty);
            FillPartyColumns(dataRow, "receiver", receiverParty);

            // FoxPro pr_pl23: namep2/rschc/mc_bank - плательщик, namep1/rschd/rschk/md_bank - получатель.
            dataRow["namep2"] = payerParty.Name;
            dataRow["inn2"] = payerParty.Inn;
            dataRow["rschc"] = payerParty.AccountNumber;
            dataRow["mc_bank"] = payerParty.BankName;
            dataRow["mc_mfo"] = payerParty.BankBic;
            dataRow["mt_adr1"] = payerParty.BankAddress;
            dataRow["namep1"] = receiverParty.Name;
            dataRow["inn1"] = receiverParty.Inn;
            dataRow["rschd"] = receiverParty.AccountNumber;
            dataRow["rschk"] = receiverParty.AccountNumber;
            dataRow["md_bank"] = receiverParty.BankName;
            dataRow["md_mfo"] = receiverParty.BankBic;
            dataRow["mt_adr2"] = receiverParty.BankAddress;
            dataRow["dok1"] = row.DocNumber;
            dataRow["dat1"] = row.DocDate.ToString("dd.MM.yyyy");
            dataRow["KODPL_1"] = string.Empty;
            dataRow["sch1"] = debit;
            dataRow["sum1"] = row.Amount.ToString("0.##");
            dataRow["sum2"] = row.Amount.ToString("0.##");
            dataRow["MSUM1"] = $"{row.Amount:N2} сом";
            dataRow["NA1"] = row.Purpose;
            dataRow["LL"] = row.Description;
            dataRow["Назначение платежа"] = row.Purpose;
            dataRow["Примечание"] = row.Description;
            dataRow["Дебет"] = debit;
            dataRow["Кредит"] = credit;
            dataRow["Сумма"] = row.Amount.ToString("0.##");
            dataRow["Модуль"] = _moduleName;
            dataRow["DATE()"] = DateTime.Today;
            dataRow["_PAGENO"] = "1";
            table.Rows.Add(dataRow);
            return table;
        }

        private async Task<(PaymentOrderPrintParty Own, PaymentOrderPrintParty Counterparty)> LoadPaymentOrderPartiesAsync(
            MetadataService metadataService,
            Guid? counterpartyId,
            string counterpartyFallback)
        {
            var catalogs = (await metadataService.GetCatalogsAsync())
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var organizations = await GetCatalogRowsAsync(metadataService, catalogs, "Организации");
            var bankAccounts = await GetCatalogRowsAsync(metadataService, catalogs, "Расчетные счета организаций");
            var banks = await GetCatalogRowsAsync(metadataService, catalogs, "Банки");

            var ownOrganization = organizations.FirstOrDefault(item => ReadBool(item, "Первичная организация", "is_primary"))
                ?? organizations.FirstOrDefault();
            var counterpartyOrganization = FindOrganization(organizations, counterpartyId, counterpartyFallback);

            return (
                BuildPrintParty(ownOrganization, bankAccounts, banks, "Наша организация"),
                BuildPrintParty(counterpartyOrganization, bankAccounts, banks,
                    string.IsNullOrWhiteSpace(counterpartyFallback) ? "Контрагент" : counterpartyFallback));
        }

        private static async Task<List<Dictionary<string, object>>> GetCatalogRowsAsync(
            MetadataService metadataService,
            Dictionary<string, MetadataObject> catalogs,
            string catalogName)
        {
            return catalogs.TryGetValue(catalogName, out var catalog)
                ? await metadataService.GetCatalogDataAsync(catalog.Id)
                : new List<Dictionary<string, object>>();
        }

        private static Dictionary<string, object>? FindOrganization(
            IEnumerable<Dictionary<string, object>> organizations,
            Guid? organizationId,
            string displayName)
        {
            if (organizationId.HasValue)
            {
                var byId = organizations.FirstOrDefault(item => ReadGuid(item, "Id") == organizationId.Value);
                if (byId != null)
                    return byId;
            }

            var normalized = displayName.Trim();
            var code = normalized.Split(" - ", StringSplitOptions.None).FirstOrDefault()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(code))
            {
                var byCode = organizations.FirstOrDefault(item =>
                    string.Equals(ReadString(item, "Код", "code"), code, StringComparison.OrdinalIgnoreCase));
                if (byCode != null)
                    return byCode;
            }

            return organizations.FirstOrDefault(item =>
                ContainsDisplayPart(normalized, ReadString(item, "Полное наименование", "full_name")) ||
                ContainsDisplayPart(normalized, ReadString(item, "Наименование", "name")));
        }

        private static PaymentOrderPrintParty BuildPrintParty(
            Dictionary<string, object>? organization,
            List<Dictionary<string, object>> bankAccounts,
            List<Dictionary<string, object>> banks,
            string fallbackName)
        {
            var organizationId = ReadGuid(organization, "Id");
            var party = new PaymentOrderPrintParty
            {
                Name = FirstNonEmpty(
                    ReadString(organization, "Полное наименование", "full_name"),
                    ReadString(organization, "Наименование", "name"),
                    fallbackName),
                Inn = ReadString(organization, "ИНН", "inn"),
                BankName = ReadString(organization, "Банк", "bank_name"),
                AccountNumber = ReadString(organization, "Расчетный счет", "bank_account"),
                BankBic = ReadString(organization, "БИК", "bic")
            };

            var settlementAccount = organizationId.HasValue
                ? bankAccounts
                    .Where(item => ReadGuid(item, "Организация", "organization_id") == organizationId.Value)
                    .OrderByDescending(item => ReadBool(item, "Активен", "is_active"))
                    .ThenByDescending(item => ReadBool(item, "Основной счет", "is_main"))
                    .FirstOrDefault()
                : null;

            if (settlementAccount != null)
            {
                party.AccountNumber = FirstNonEmpty(ReadString(settlementAccount, "Счет", "account_number"), party.AccountNumber);
                party.BankBic = FirstNonEmpty(ReadString(settlementAccount, "БИК", "bic"), party.BankBic);

                var bankId = ReadGuid(settlementAccount, "Банк", "bank_id");
                var bank = bankId.HasValue
                    ? banks.FirstOrDefault(item => ReadGuid(item, "Id") == bankId.Value)
                    : null;
                if (bank != null)
                {
                    party.BankName = FirstNonEmpty(ReadString(bank, "Наименование банка", "name"), party.BankName);
                    party.BankBic = FirstNonEmpty(ReadString(bank, "БИК", "bic"), party.BankBic);
                    party.BankAddress = ReadString(bank, "Адрес", "address");
                }
            }

            return party;
        }

        private static void ApplyPaymentOrderFallbacks(
            PaymentOrderRow row,
            PaymentOrderPrintParty payerParty,
            PaymentOrderPrintParty receiverParty)
        {
            if (!string.IsNullOrWhiteSpace(row.BankName) && string.IsNullOrWhiteSpace(payerParty.BankName))
                payerParty.BankName = row.BankName;

            payerParty.AccountNumber = FirstNonEmpty(payerParty.AccountNumber, ExtractAccountCode(row.OurAccountName));
            receiverParty.AccountNumber = FirstNonEmpty(receiverParty.AccountNumber, ExtractAccountCode(row.CorrespondentAccountName));
        }

        private static void FillPartyColumns(DataRow dataRow, string prefix, PaymentOrderPrintParty party)
        {
            dataRow[$"{prefix}_name"] = party.Name;
            dataRow[$"{prefix}_inn"] = party.Inn;
            dataRow[$"{prefix}_bank"] = party.BankName;
            dataRow[$"{prefix}_bic"] = party.BankBic;
            dataRow[$"{prefix}_account"] = party.AccountNumber;
        }

        private static bool ContainsDisplayPart(string displayName, string value)
        {
            return !string.IsNullOrWhiteSpace(displayName) &&
                   !string.IsNullOrWhiteSpace(value) &&
                   displayName.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static Guid? ReadGuid(Dictionary<string, object>? row, params string[] keys)
        {
            if (row == null)
                return null;

            var value = GetFirstValue(row, keys);
            return Guid.TryParse(value, out var id) ? id : null;
        }        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadData();
    }

    internal sealed class PaymentOrderPrintParty
    {
        public string Name { get; set; } = string.Empty;
        public string Inn { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string BankBic { get; set; } = string.Empty;
        public string BankAddress { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
    }

    public class PaymentOrderRow
    {
        public Guid Id { get; set; }
        public Guid? OrganizationId { get; set; }
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string OrderType { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string OurAccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal AmountCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public string CurrencyName { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string CorrespondentAccountName { get; set; } = string.Empty;
        public string PaymentClassificationName { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPosted { get; set; }
        public string IsPostedDisplay => LocalizationService.DisplayValue(IsPosted);
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}


