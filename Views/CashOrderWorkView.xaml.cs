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

                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

                var referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

                // Загружаем Reference поля
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
                    }
                }

                // Загружаем счета из плана счетов
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
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки счетов: {ex.Message}");
                }

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
                        UpdatedAt = row.ContainsKey("UpdatedAt") && row["UpdatedAt"] != null ? (DateTime)row["UpdatedAt"] : DateTime.Now,

                        // НОВЫЕ ПОЛЯ
                        DebitAccount = row.ContainsKey("Дебет") ? row["Дебет"]?.ToString() : "",
                        CreditAccount = row.ContainsKey("Кредит") ? row["Кредит"]?.ToString() : "",
                        AmountInCurrency = row.ContainsKey("Сумма в валюте") && row["Сумма в валюте"] != null ? Convert.ToDecimal(row["Сумма в валюте"]) : 0
                    };                  

                    // Загружаем остальные Reference поля...
                    // (оставьте существующий код для Organizations, Currency, Employee, Material)

                    rows.Add(newRow);
                }

                DataGrid.ItemsSource = rows;
                //UpdateAnalyticColumns(data, accountAnalytics);

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
    }
}
