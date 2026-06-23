using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.Win32;
using System.IO;

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
        }

        private async Task LoadData()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                StatusText.Text = "Загрузка данных...";

                // Получаем данные документа
                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);

                // Загружаем все справочники
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

                // Кэш для Reference полей
                var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

                // 1. Загружаем данные для всех Reference полей документа
                foreach (var field in _documentMetadata.Fields.Where(f => f.FieldType == "Reference" && !string.IsNullOrEmpty(f.ReferenceCatalog)))
                {
                    if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    {
                        var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                        var dict = new Dictionary<Guid, string>();

                        foreach (var item in refData)
                        {
                            if (!item.ContainsKey("Id")) continue;

                            if (Guid.TryParse(item["Id"].ToString(), out var id))
                            {
                                dict[id] = ReferenceDisplayHelper.BuildDisplayValue(item, field);
                            }
                        }
                        referenceCache[field.Name] = dict;
                        System.Diagnostics.Debug.WriteLine($"Загружено {dict.Count} записей для поля {field.Name}");
                    }
                }

                // 2. Загружаем счета из плана счетов для correspondent_account
                try
                {
                    var chartCatalog = catalogsDict.FirstOrDefault(c => c.Key.StartsWith("План счетов")).Value;
                    if (chartCatalog != null)
                    {
                        var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);
                        var accountDict = new Dictionary<Guid, string>();
                        foreach (var acc in accountsData)
                        {
                            if (acc.ContainsKey("Id") && Guid.TryParse(acc["Id"].ToString(), out var accId))
                            {
                                var code = acc.ContainsKey("Код") ? acc["Код"].ToString() : "";
                                var name = acc.ContainsKey("Наименование") ? acc["Наименование"].ToString() : "";
                                accountDict[accId] = $"{code} - {name}";
                            }
                        }
                        referenceCache["correspondent_account"] = accountDict;
                        System.Diagnostics.Debug.WriteLine($"Загружено {accountDict.Count} счетов для correspondent_account");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("План счетов не найден!");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки счетов: {ex.Message}");
                }

                // Создаем список для хранения данных
                var rows = new List<CashOrderRow>();

                foreach (var row in data)
                {
                    var newRow = new CashOrderRow
                    {
                        Id = row.ContainsKey("Id") ? Guid.Parse(row["Id"].ToString()) : Guid.NewGuid(),
                        DocNumber = row.ContainsKey("Номер") ? row["Номер"]?.ToString() :
                                   (row.ContainsKey("Номер документа") ? row["Номер документа"]?.ToString() : ""),
                        DocDate = row.ContainsKey("Дата") && row["Дата"] != null ? (DateTime)row["Дата"] : DateTime.Now,
                        Amount = row.ContainsKey("Сумма") && row["Сумма"] != null ? Convert.ToDecimal(row["Сумма"]) : 0,
                        Basis = row.ContainsKey("Основание") ? row["Основание"]?.ToString() : "",
                        Description = row.ContainsKey("Примечание") ? row["Примечание"]?.ToString() : "",
                        IsPosted = row.ContainsKey("Проведён") && row["Проведён"] != null ? (bool)row["Проведён"] : false,
                        CreatedAt = row.ContainsKey("CreatedAt") && row["CreatedAt"] != null ? (DateTime)row["CreatedAt"] : DateTime.Now,
                        UpdatedAt = row.ContainsKey("UpdatedAt") && row["UpdatedAt"] != null ? (DateTime)row["UpdatedAt"] : DateTime.Now
                    };

                    // Касса
                    if (row.ContainsKey("Касса") && row["Касса"] != null && referenceCache.TryGetValue("Касса", out var cashDict))
                    {
                        if (Guid.TryParse(row["Касса"].ToString(), out var cashId))
                            newRow.CashDeskName = cashDict.ContainsKey(cashId) ? cashDict[cashId] : row["Касса"].ToString();
                    }

                    // Организация
                    if (row.ContainsKey("Организация") && row["Организация"] != null && referenceCache.TryGetValue("Организация", out var orgDict))
                    {
                        if (Guid.TryParse(row["Организация"].ToString(), out var orgId))
                            newRow.OrganizationName = orgDict.ContainsKey(orgId) ? orgDict[orgId] : row["Организация"].ToString();
                    }

                    if (row.ContainsKey("Валюта") && row["Валюта"] != null && referenceCache.TryGetValue("Валюта", out var currencyDict))
                    {
                        if (Guid.TryParse(row["Валюта"].ToString(), out var currencyId))
                            newRow.CurrencyName = currencyDict.ContainsKey(currencyId) ? currencyDict[currencyId] : row["Валюта"].ToString();
                    }

                    if (row.ContainsKey("Сотрудник") && row["Сотрудник"] != null && referenceCache.TryGetValue("Сотрудник", out var employeeDict))
                    {
                        if (Guid.TryParse(row["Сотрудник"].ToString(), out var employeeId))
                            newRow.EmployeeName = employeeDict.ContainsKey(employeeId) ? employeeDict[employeeId] : row["Сотрудник"].ToString();
                    }

                    if (row.ContainsKey("Материал") && row["Материал"] != null && referenceCache.TryGetValue("Материал", out var materialDict))
                    {
                        if (Guid.TryParse(row["Материал"].ToString(), out var materialId))
                            newRow.MaterialName = materialDict.ContainsKey(materialId) ? materialDict[materialId] : row["Материал"].ToString();
                    }

                    // Корреспондирующий счёт (пробуем разные возможные имена)
                    string corrValue = null;
                    string[] possibleCorrNames = { "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Счет" };

                    foreach (var name in possibleCorrNames)
                    {
                        if (row.ContainsKey(name) && row[name] != null)
                        {
                            corrValue = row[name].ToString();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(corrValue) && referenceCache.TryGetValue("correspondent_account", out var accDict))
                    {
                        if (Guid.TryParse(corrValue, out var accId))
                        {
                            newRow.CorrespondentAccountName = accDict.ContainsKey(accId) ? accDict[accId] : corrValue;
                        }
                        else
                        {
                            // Если не GUID, возможно уже сохранено имя счёта
                            newRow.CorrespondentAccountName = corrValue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(corrValue))
                    {
                        newRow.CorrespondentAccountName = corrValue;
                    }

                    var corrAccount = accountAnalytics.FindAccount(corrValue);
                    if (corrAccount != null)
                        newRow.CorrespondentAccountName = corrAccount.DisplayName;

                    rows.Add(newRow);
                }

                DataGrid.ItemsSource = rows;
                UpdateAnalyticColumns(data, accountAnalytics);

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

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = new[]
            {
                "correspondent_account", "Корр. счет", "Корр счет", "Коррсчет", "Счет"
            };

            OrganizationColumn.Visibility = GetAnalyticColumnVisibility(
                "Организация", "Организации", rows, accountFields, accountAnalytics);
            CurrencyColumn.Visibility = GetAnalyticColumnVisibility(
                "Валюта", "Справочник валют", rows, accountFields, accountAnalytics);
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

                var saveDialog = new SaveFileDialog
                {
                    Title = "Сохранить печатную форму",
                    Filter = "PDF файлы (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = $"{_documentMetadata.Name}_{selectedRow.DocNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };
                if (saveDialog.ShowDialog() != true)
                    return;

                StatusText.Text = "Формирование PDF...";
                var pdf = await printFormService.ExportDocumentAsync(selectionDialog.SelectedReport, selectedRow.Id);
                File.WriteAllBytes(saveDialog.FileName, pdf);
                StatusText.Text = "PDF сформирован";
                MessageBox.Show("Печатная форма сохранена в PDF.", "Печать",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка печати";
                MessageBox.Show($"Ошибка формирования печатной формы: {ex.Message}", "Печать",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
