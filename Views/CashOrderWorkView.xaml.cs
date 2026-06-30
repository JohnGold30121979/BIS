using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private bool _isLoading = false;

        public CashOrderWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;

            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;

            // Подписываемся на событие загрузки
            this.Loaded += async (s, e) => await LoadData();
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
                StatusText.Text = selected.IsPosted
                    ? $"Проведен: Дт {selected.DebitAccount} / Кт {selected.CreditAccount}, {selected.Amount:N2} сом. Двойной щелчок откроет проводку."
                    : $"Не проведен: {selected.DocNumber}, {selected.Amount:N2} сом.";
        }

        private async Task LoadData()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                StatusText.Text = "Загрузка данных...";

                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

                // ---- Создаём кэш для Reference полей ----
                var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

                // 1. Собрать все Reference поля документа (из метаданных)
                var refFields = _documentMetadata.Fields
                    .Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog))
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"🔍 Найдено Reference полей: {refFields.Count}");

                // 2. Для каждого поля собрать уникальные ID из данных
                foreach (var field in refFields)
                {
                    var ids = new HashSet<Guid>();
                    foreach (var row in data)
                    {
                        if (row.TryGetValue(field.Name, out var value) && value != null)
                        {
                            if (Guid.TryParse(value.ToString(), out var id))
                                ids.Add(id);
                        }
                    }

                    if (ids.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"⏭️ Поле '{field.Name}' — нет данных, пропускаем");
                        continue;
                    }

                    // 3. Найти справочник по ReferenceCatalog
                    if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var catalog))
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Справочник '{field.ReferenceCatalog}' не найден");
                        continue;
                    }

                    // 4. Загрузить данные справочника
                    var refData = await _metadataService.GetCatalogDataAsync(catalog.Id);
                    var dict = new Dictionary<Guid, string>();

                    foreach (var item in refData)
                    {
                        if (!item.TryGetValue("Id", out var refId) || refId == null)
                            continue;
                        if (!Guid.TryParse(refId.ToString(), out var id))
                            continue;

                        // Построить отображаемое имя
                        var display = ReferenceDisplayHelper.BuildDisplayValue(item, field);
                        if (string.IsNullOrEmpty(display))
                        {
                            var fallback = item.FirstOrDefault(kv => kv.Key != "Id").Value?.ToString();
                            display = !string.IsNullOrEmpty(fallback) ? fallback : id.ToString();
                        }
                        dict[id] = display;
                    }

                    referenceCache[field.Name] = dict;
                    System.Diagnostics.Debug.WriteLine($"✅ Загружено {dict.Count} записей для '{field.Name}' (справочник: {field.ReferenceCatalog})");
                }

                // 5. Специальная загрузка для корреспондирующего счета (план счетов)
                try
                {
                    var chartCatalog = catalogsDict.FirstOrDefault(c => c.Key.StartsWith("План счетов")).Value;
                    if (chartCatalog != null)
                    {
                        var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);
                        var accountDict = new Dictionary<Guid, string>();
                        foreach (var acc in accountsData)
                        {
                            if (acc.TryGetValue("Id", out var accIdObj) && accIdObj != null && Guid.TryParse(accIdObj.ToString(), out var accId))
                            {
                                var code = acc.ContainsKey("Код") ? acc["Код"].ToString() : "";
                                var name = acc.ContainsKey("Наименование") ? acc["Наименование"].ToString() : "";
                                accountDict[accId] = $"{code} - {name}";
                            }
                        }
                        referenceCache["correspondent_account"] = accountDict;
                        System.Diagnostics.Debug.WriteLine($"✅ Загружено счетов: {accountDict.Count}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ План счетов не найден!");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки счетов: {ex.Message}");
                }

                // ---- Создание списка строк ----
                var rows = new List<CashOrderRow>();

                foreach (var row in data)
                {
                    var newRow = new CashOrderRow
                    {
                        Id = row.TryGetValue("Id", out var idObj) && idObj != DBNull.Value && idObj != null
                            ? Guid.Parse(idObj.ToString())
                            : Guid.NewGuid(),
                        DocNumber = row.TryGetValue("Номер", out var numObj) && numObj != DBNull.Value
                            ? numObj?.ToString()
                            : (row.TryGetValue("Номер документа", out var numObj2) && numObj2 != DBNull.Value
                                ? numObj2?.ToString()
                                : ""),
                        DocDate = row.TryGetValue("Дата", out var dateObj) && dateObj != DBNull.Value && dateObj != null
                            ? (DateTime)dateObj
                            : DateTime.Now,
                        Amount = row.TryGetValue("Сумма", out var amtObj) && amtObj != DBNull.Value && amtObj != null
                            ? Convert.ToDecimal(amtObj)
                            : 0,
                        Basis = row.TryGetValue("Основание", out var basisObj) && basisObj != DBNull.Value
                            ? basisObj?.ToString()
                            : "",
                        Description = row.TryGetValue("Примечание", out var descObj) && descObj != DBNull.Value
                            ? descObj?.ToString()
                            : "",
                        IsPosted = row.TryGetValue("Проведён", out var postedObj) && postedObj != DBNull.Value && postedObj != null
                            ? (bool)postedObj
                            : false,
                        CreatedAt = row.TryGetValue("CreatedAt", out var createdObj) && createdObj != DBNull.Value && createdObj != null
                            ? (DateTime)createdObj
                            : DateTime.Now,
                        UpdatedAt = row.TryGetValue("UpdatedAt", out var updatedObj) && updatedObj != DBNull.Value && updatedObj != null
                            ? (DateTime)updatedObj
                            : DateTime.Now,

                        // Новые поля
                        DebitAccount = row.TryGetValue("Дебет", out var debObj) && debObj != DBNull.Value
                            ? debObj?.ToString()
                            : "",
                        CreditAccount = row.TryGetValue("Кредит", out var credObj) && credObj != DBNull.Value
                            ? credObj?.ToString()
                            : "",
                        AmountInCurrency = row.TryGetValue("Сумма в валюте", out var curObj) && curObj != DBNull.Value && curObj != null
                            ? Convert.ToDecimal(curObj)
                            : 0,
                        CashDeskId = row.TryGetValue("Касса", out var cashIdObj) && cashIdObj != DBNull.Value ? cashIdObj?.ToString() : ""
                    };

                    // ---- Загрузка Reference полей через кэш ----
                    // Организация
                    if (row.TryGetValue("Организация", out var orgObj) && orgObj != DBNull.Value && orgObj != null)
                    {
                        if (Guid.TryParse(orgObj.ToString(), out var orgId) &&
                            referenceCache.TryGetValue("Организация", out var orgDict) &&
                            orgDict.TryGetValue(orgId, out var orgName))
                        {
                            newRow.OrganizationName = orgName;
                        }
                        else
                        {
                            newRow.OrganizationName = orgObj.ToString();
                        }
                    }

                    // Валюта
                    if (row.TryGetValue("Валюта", out var curObj2) && curObj2 != DBNull.Value && curObj2 != null)
                    {
                        if (Guid.TryParse(curObj2.ToString(), out var curId) &&
                            referenceCache.TryGetValue("Валюта", out var curDict) &&
                            curDict.TryGetValue(curId, out var curName))
                        {
                            newRow.CurrencyName = curName;
                        }
                        else
                        {
                            newRow.CurrencyName = curObj2.ToString();
                        }
                    }

                    // Касса
                    if (row.TryGetValue("Касса", out var cashObj) && cashObj != DBNull.Value && cashObj != null)
                    {
                        if (Guid.TryParse(cashObj.ToString(), out var cashId) &&
                            referenceCache.TryGetValue("Касса", out var cashDict) &&
                            cashDict.TryGetValue(cashId, out var cashName))
                        {
                            newRow.CashDeskName = cashName;
                        }
                        else
                        {
                            newRow.CashDeskName = cashObj.ToString();
                        }
                    }

                    // Сотрудник
                    if (row.TryGetValue("Сотрудник", out var empObj) && empObj != DBNull.Value && empObj != null)
                    {
                        if (Guid.TryParse(empObj.ToString(), out var empId) &&
                            referenceCache.TryGetValue("Сотрудник", out var empDict) &&
                            empDict.TryGetValue(empId, out var empName))
                        {
                            newRow.EmployeeName = empName;
                        }
                        else
                        {
                            newRow.EmployeeName = empObj.ToString();
                        }
                    }

                    // Материал
                    if (row.TryGetValue("Материал", out var matObj) && matObj != DBNull.Value && matObj != null)
                    {
                        if (Guid.TryParse(matObj.ToString(), out var matId) &&
                            referenceCache.TryGetValue("Материал", out var matDict) &&
                            matDict.TryGetValue(matId, out var matName))
                        {
                            newRow.MaterialName = matName;
                        }
                        else
                        {
                            newRow.MaterialName = matObj.ToString();
                        }
                    }

                    // Корреспондирующий счет
                    if (row.TryGetValue("Корр. счет", out var corrObj) && corrObj != DBNull.Value && corrObj != null)
                    {
                        if (Guid.TryParse(corrObj.ToString(), out var corrId) &&
                            referenceCache.TryGetValue("correspondent_account", out var accDict) &&
                            accDict.TryGetValue(corrId, out var accName))
                        {
                            newRow.CorrespondentAccountName = accName;
                        }
                        else
                        {
                            newRow.CorrespondentAccountName = corrObj.ToString();
                        }
                    }

                    rows.Add(newRow);
                }

                DataGrid.ItemsSource = rows;
                DataGrid.Items.Refresh();
                // UpdateAnalyticColumns(data, accountAnalytics);

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

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new CashOrderDialog(_documentMetadata, _metadataService);
                dialog.Owner = Window.GetWindow(this);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
            var selectedRow = DataGrid.SelectedItem as CashOrderRow;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите документ для редактирования!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new CashOrderDialog(_documentMetadata, _metadataService, selectedRow.Id);
                dialog.Owner = Window.GetWindow(this);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
            var selectedRow = DataGrid.SelectedItem as CashOrderRow;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите документ для удаления!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Удалить выбранный документ?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
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
        }

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as CashOrderRow;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите документ для проведения!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var actionText = selectedRow.IsPosted ? "Отменить проведение выбранного документа?" : "Провести выбранный документ?";
            var result = MessageBox.Show(actionText, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = selectedRow.IsPosted ? "Отмена проведения..." : "Проведение...";

                    // Меняем статус документа
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
                var forms = await printFormService.GetPrintFormsAsync(_documentMetadata.Id);
                if (forms.Count == 0)
                {
                    MessageBox.Show("Для документа не настроены печатные формы.", "Печатная форма",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                    return;

                StatusText.Text = "Формирование PDF...";
                var pdf = await printFormService.ExportDocumentAsync(selectionDialog.SelectedReport, selectedRow.Id);
                StatusText.Text = "PDF сформирован";

                // ✅ Открываем окно предпросмотра
                var previewWindow = new PdfPreviewWindow(pdf);
                previewWindow.Owner = Window.GetWindow(this);
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
            var selected = DataGrid.SelectedItem as CashOrderRow;
            if (selected == null)
                return;

            PostingViewModel? posting = null;
            if (selected.IsPosted)
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var postingService = new PostingService(context);
                var postings = await postingService.GetAllPostingsAsync(selected.DocDate.Date, selected.DocDate.Date);
                posting = postings.FirstOrDefault(item =>
                    item.DocumentNumber == MetadataService.NormalizeLegacyDocumentNumber(selected.DocNumber) &&
                    item.DocumentType == _documentMetadata.Name);
            }

            posting ??= new PostingViewModel
            {
                DocumentNumber = selected.DocNumber,
                Date = selected.DocDate,
                DocumentType = _documentMetadata.Name,
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

            var dialog = new PostingDetailsDialog(posting);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        private string ResolveCorrespondentAccount(CashOrderRow selected)
        {
            if (_documentMetadata.Name.Equals("Приходный кассовый ордер", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(selected.CreditAccount)
                    ? selected.CorrespondentAccountName
                    : selected.CreditAccount;

            if (_documentMetadata.Name.Equals("Расходный кассовый ордер", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(selected.DebitAccount)
                    ? selected.CorrespondentAccountName
                    : selected.DebitAccount;

            return selected.CorrespondentAccountName;
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var row = e.Row.DataContext as CashOrderRow;
            if (row != null && row.IsPosted)
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
            }
        }

    }

    public class CashOrderRow
    {
        public Guid Id { get; set; }
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

        // НОВЫЕ СВОЙСТВА ДЛЯ ПЕЧАТНОЙ ФОРМЫ
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public decimal AmountInCurrency { get; set; }
        public string CashDeskId { get; set; } = string.Empty;
    }
}
