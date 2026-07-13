using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    public partial class PostingsView : UserControl
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private ObservableCollection<Dictionary<string, object>> _postings;

        public PostingsView(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _postings = new ObservableCollection<Dictionary<string, object>>();
            PostingsGrid.ItemsSource = _postings;

            // Привязываем горячие клавиши
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка...";
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_document, _metadataService);
                var displayData = ReferenceDisplayHelper.ResolveRows(data, referenceMaps);

                _postings.Clear();

                // Для отладки - выводим ключи
                if (displayData.Any())
                {
                    var firstRow = displayData.First();
                    System.Diagnostics.Debug.WriteLine("=== Ключи в данных ===");
                    foreach (var key in firstRow.Keys)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {key}");
                    }
                }

                foreach (var row in displayData.OrderByDescending(r => r.GetValueOrDefault("Дата")))
                {
                    _postings.Add(row);
                }

                UpdateAnalyticColumns(data, accountAnalytics);

                StatusText.Text = $"📊 Загружено проводок: {_postings.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rawRows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = new[] { "Дебет", "Кредит" };
            var showCurrency = AccountAnalyticsRules.ShouldShowFieldForRows(
                "Валюта", rawRows, accountFields, accountAnalytics, "Справочник валют");

            AmountCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            CurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            OrganizationColumn.Visibility = GetAnalyticColumnVisibility(
                "Организация", "Организации", rawRows, accountFields, accountAnalytics);
            EmployeeColumn.Visibility = GetAnalyticColumnVisibility(
                "Сотрудник", "Сотрудники (Списочный состав)", rawRows, accountFields, accountAnalytics);
            MaterialColumn.Visibility = GetAnalyticColumnVisibility(
                "Материал", "Справочник материалов", rawRows, accountFields, accountAnalytics);
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

        private void PostingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PostingsGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PostingEditDialog(_document, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await LoadData();
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as Dictionary<string, object>;
            if (selected == null) return;

            if (await PostingSourceDocumentOpener.TryOpenAsync(
                    BuildPostingViewModel(selected),
                    _metadataService,
                    Window.GetWindow(this),
                    isReadOnly: false))
            {
                return;
            }

            if (selected.ContainsKey("Id") && selected["Id"] != null)
            {
                var id = Guid.Parse(selected["Id"].ToString());
                var dialog = new PostingEditDialog(_document, _metadataService, id);
                dialog.Owner = Window.GetWindow(this);

                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as Dictionary<string, object>;
            if (selected == null) return;

            var result = MessageBox.Show("Удалить проводку?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (selected.ContainsKey("Id") && selected["Id"] != null)
                {
                    var id = Guid.Parse(selected["Id"].ToString());
                    await _metadataService.DeleteDynamicRecordAsync(_document.Id, id);
                    await LoadData();
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            // Открыть окно поиска
            var searchDialog = new PostingSearchDialog(_postings.ToList());
            searchDialog.Owner = Window.GetWindow(this);
            if (searchDialog.ShowDialog() == true && searchDialog.SelectedPosting != null)
            {
                PostingsGrid.SelectedItem = searchDialog.SelectedPosting;
                PostingsGrid.ScrollIntoView(searchDialog.SelectedPosting);
            }
        }

        private async void PostingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as Dictionary<string, object>;
            if (selected == null)
                return;

            if (await PostingSourceDocumentOpener.TryOpenAsync(
                    BuildPostingViewModel(selected),
                    _metadataService,
                    Window.GetWindow(this),
                    isReadOnly: true))
            {
                return;
            }

            var dialog = new PostingDetailsDialog(BuildPostingViewModel(selected));
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        private async Task<bool> TryOpenInvoiceFromPostingAsync(
            Dictionary<string, object> posting,
            bool isReadOnly)
        {
            var documentType = GetRowString(posting, "Тип документа", "document_type");
            if (!InvoiceDocumentTypes.IsSales(documentType) && !InvoiceDocumentTypes.IsPurchase(documentType))
                return false;

            var rawDocumentNumber = GetRowString(posting, "Номер документа", "doc_number");
            var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(rawDocumentNumber);
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                MessageBox.Show("В проводке не указан номер счет-фактуры.", "Счет-фактура",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }

            var documents = await _metadataService.GetDocumentsAsync();
            var invoiceMetadata = documents.FirstOrDefault(document =>
                document.Name.Equals(documentType, StringComparison.OrdinalIgnoreCase));
            if (invoiceMetadata == null)
            {
                MessageBox.Show($"Метаданные документа «{documentType}» не найдены.", "Счет-фактура",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var invoiceService = new InvoiceService(context);
            invoiceService.Configure(invoiceMetadata);
            await invoiceService.EnsureSchemaAsync();

            var documentDate = GetRowDate(posting, "Дата", "posting_date");
            var invoiceId = await invoiceService.FindInvoiceIdByPostingNumberAsync(rawDocumentNumber, documentDate);
            if (!invoiceId.HasValue)
            {
                MessageBox.Show(
                    $"Проводка есть, но исходная счет-фактура №{documentNumber} не найдена. Будут открыты детали проводки.",
                    "Счет-фактура",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dialog = new InvoiceEditDialog(
                invoiceMetadata,
                _metadataService,
                invoiceService,
                invoiceId.Value,
                isReadOnly);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && !isReadOnly)
                await LoadData();

            return true;
        }

        private static PostingViewModel BuildPostingViewModel(Dictionary<string, object> row)
        {
            return new PostingViewModel
            {
                Id = GetRowGuid(row, "Id"),
                Date = GetRowDate(row, "Дата", "posting_date") ?? DateTime.Today,
                DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(
                    GetRowString(row, "Номер документа", "doc_number")),
                DocumentType = GetRowString(row, "Тип документа", "document_type"),
                DebitAccount = GetRowString(row, "Дебет", "debit_account"),
                CreditAccount = GetRowString(row, "Кредит", "credit_account"),
                Amount = GetRowDecimal(row, "Сумма в сом", "amount_kgs"),
                AmountCurrency = GetRowDecimal(row, "Сумма в валюте", "amount_currency"),
                Currency = GetRowString(row, "Валюта", "currency_id"),
                Organization = GetRowString(row, "Организация", "organization_id"),
                Employee = GetRowString(row, "Сотрудник", "employee_id"),
                Note = GetRowString(row, "Примечание", "description"),
                CreatedAt = GetRowDate(row, "CreatedAt"),
                IsActive = GetRowBool(row, "Активен", "is_active", defaultValue: true)
            };
        }

        private static string GetRowString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static DateTime? GetRowDate(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is DateTime date)
                    return date;
                if (DateTime.TryParse(pair.Value?.ToString(), out date))
                    return date;
            }

            return null;
        }

        private static decimal GetRowDecimal(Dictionary<string, object> row, params string[] keys)
        {
            return decimal.TryParse(GetRowString(row, keys), out var value) ? value : 0m;
        }

        private static Guid GetRowGuid(Dictionary<string, object> row, params string[] keys)
        {
            return Guid.TryParse(GetRowString(row, keys), out var value) ? value : Guid.Empty;
        }

        private static bool GetRowBool(Dictionary<string, object> row, string firstKey, string secondKey, bool defaultValue)
        {
            var value = GetRowString(row, firstKey, secondKey);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
