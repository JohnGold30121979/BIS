using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.Win32;

namespace BIS.ERP.Views
{
    public partial class InvoiceWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private readonly bool _isRegistrationMode;
        private readonly bool _isSalesMode;
        private InvoiceService? _invoiceService;
        private InvoiceEsfExchangeService? _invoiceEsfExchangeService;
        private readonly List<InvoiceListRow> _allInvoices = new();
        private readonly ObservableCollection<InvoiceListRow> _filteredInvoices = new();

        public InvoiceWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            _isRegistrationMode = InvoiceDocumentTypes.IsPurchase(documentMetadata.Name);
            _isSalesMode = InvoiceDocumentTypes.IsSales(documentMetadata.Name);
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;
            ConfigureModeUi();
            Loaded += OnLoaded;
        }

        private async Task<string> ResolveAssignedModuleNameAsync()
        {
            try
            {
                var assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(
                    _documentMetadata.Id,
                    _documentMetadata.ObjectType);
                if (!string.IsNullOrWhiteSpace(assignedModuleName))
                    return NormalizeModuleName(assignedModuleName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка определения модуля документа {_documentMetadata.Name}: {ex.Message}");
            }

            return _isSalesMode || _isRegistrationMode ? "Финансы" : string.Empty;
        }

        private static string NormalizeModuleName(string moduleName)
        {
            var trimmed = moduleName.Trim();
            return trimmed.ToUpperInvariant() switch
            {
                "ФИН" or "ФИНАНСЫ" or "FIN" or "FINANCE" => "Финансы",
                "ОС" or "FIXEDASSETS" => "Основные средства",
                "ТМЦ" or "МАТЕРИАЛЫ" or "INVENTORY" => "Учет материальных ценностей",
                _ => trimmed
            };
        }
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            await new InvoiceMetadataSeedService(context).EnsureAsync();
            _invoiceService = new InvoiceService(context);
            _invoiceService.Configure(_documentMetadata);
            await _invoiceService.EnsureSchemaAsync();
            _invoiceEsfExchangeService = new InvoiceEsfExchangeService(context, _invoiceService);
            await LoadDataAsync();
        }

        private InvoiceListRow? SelectedInvoice => InvoicesGrid.SelectedItem as InvoiceListRow;

        private void UpdateButtonsState()
        {
            var selected = SelectedInvoice;
            var hasSelection = selected != null;
            EditButton.IsEnabled = hasSelection;
            ViewButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection; // && selected?.IsPosted == false;
            AllPostingsButton.IsEnabled = hasSelection;
            PrintButton.IsEnabled = hasSelection;
            ExportEsfButton.IsEnabled = _isSalesMode;
            ImportEsfButton.IsEnabled = _isSalesMode;
        }

        private void ConfigureModeUi()
        {
            if (!_isRegistrationMode)
                return;

            TitleText.Text = $"{_documentMetadata.Icon} Регистрация счет-фактур по НДС";
            DescriptionText.Text =
                "Реестр зарегистрированных счетов-фактур: полный просмотр и редактирование документа, включая номер бланка и модуль.";
            AddButton.Content = "➕ Добавить счет-фактуру";
            AddButton.Width = 165;
            EditButton.Content = "✏ Изменить";
            EditButton.Width = 120;

            TaxBlankColumn.Visibility = Visibility.Visible;
            BasisColumn.Visibility = Visibility.Collapsed;
            EsfNumberColumn.Visibility = Visibility.Collapsed;
            EsfStatusColumn.Visibility = Visibility.Collapsed;
            ExportEsfButton.Visibility = Visibility.Collapsed;
            ImportEsfButton.Visibility = Visibility.Collapsed;

            LineUnitColumn.Visibility = Visibility.Visible;
            LineQuantityColumn.Visibility = Visibility.Visible;
            LineAccountColumn.Visibility = Visibility.Collapsed;
            LineSalesTaxColumn.Visibility = Visibility.Collapsed;
        }

        private async Task LoadDataAsync()
        {
            if (_invoiceService == null)
                return;

            try
            {
                StatusText.Text = "Загрузка...";
                var postedCount = await _invoiceService.EnsureSavedInvoicesPostedAsync();
                var invoices = await _invoiceService.GetInvoicesAsync();
                var defaultModuleName = await ResolveAssignedModuleNameAsync();
                foreach (var invoice in invoices)
                {
                    invoice.ModuleCode = string.IsNullOrWhiteSpace(invoice.ModuleCode)
                        ? defaultModuleName
                        : NormalizeModuleName(invoice.ModuleCode);
                }

                _allInvoices.Clear();
                _allInvoices.AddRange(invoices);
                LinesGrid.ItemsSource = null;
                ApplyInvoiceFilters(updateEmptyLines: true);
                StatusText.Text = postedCount > 0
                    ? $"Загружено документов: {_allInvoices.Count}; показано: {_filteredInvoices.Count}; автоматически проведено: {postedCount}"
                    : $"Загружено документов: {_allInvoices.Count}; показано: {_filteredInvoices.Count}";
                UpdateButtonsState();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyInvoiceFilters(bool updateEmptyLines = false)
        {
            IEnumerable<InvoiceListRow> query = _allInvoices;

            query = ApplyColumnFilter(query, DateFilterBox.Text, invoice => invoice.DocDate.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture));
            query = ApplyColumnFilter(query, DocumentFilterBox.Text, invoice => invoice.DocNumber);
            query = ApplyColumnFilter(query, AmountFilterBox.Text, invoice => FormatAmount(invoice.TotalAmount));
            query = ApplyColumnFilter(query, EsfNumberFilterBox.Text, invoice => invoice.EsfNumber);
            query = ApplyColumnFilter(query, TaxBlankFilterBox.Text, invoice => invoice.TaxBlankNumber);
            query = ApplyColumnFilter(query, OrganizationFilterBox.Text, invoice => invoice.OrganizationName);

            _filteredInvoices.Clear();
            foreach (var invoice in query)
                _filteredInvoices.Add(invoice);

            if (InvoicesGrid.ItemsSource == null)
                InvoicesGrid.ItemsSource = _filteredInvoices;

            if (updateEmptyLines || _filteredInvoices.Count == 0)
                LinesGrid.ItemsSource = null;

            StatusText.Text = $"Показано документов: {_filteredInvoices.Count} из {_allInvoices.Count}";
            UpdateButtonsState();
        }

        private static IEnumerable<InvoiceListRow> ApplyColumnFilter(
            IEnumerable<InvoiceListRow> query,
            string filter,
            Func<InvoiceListRow, string?> valueSelector)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return query;

            var filterText = filter.Trim();
            return query.Where(invoice => MatchesOrderedColumnFilter(valueSelector(invoice), filterText));
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

        private static string FormatAmount(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);

        private void OnColumnFilterChanged(object sender, TextChangedEventArgs e) => ApplyInvoiceFilters(updateEmptyLines: true);

        private void OnInvoiceFilterChanged(object sender, TextChangedEventArgs e) => OnColumnFilterChanged(sender, e);
        private async void OnInvoiceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
            var selected = SelectedInvoice;
            if (selected == null || _invoiceService == null)
            {
                LinesGrid.ItemsSource = null;
                return;
            }

            try
            {
                LinesGrid.ItemsSource = await _invoiceService.GetLinesAsync(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки строк: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (_invoiceService == null)
                return;

            var dialog = new InvoiceEditDialog(_documentMetadata, _metadataService, _invoiceService);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
                await LoadDataAsync();
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selected = SelectedInvoice;
            if (selected != null)
                await OpenInvoiceDialogAsync(selected.Id, isReadOnly: false);
        }

        private async void OnViewClick(object sender, RoutedEventArgs e)
        {
            var selected = SelectedInvoice;
            if (selected != null)
                await OpenInvoiceDialogAsync(selected.Id, isReadOnly: true);
        }

        private async void OnInvoiceDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = SelectedInvoice;
            if (selected != null)
                await OpenInvoiceDialogAsync(selected.Id, isReadOnly: true);
        }

        private async Task OpenInvoiceDialogAsync(Guid invoiceId, bool isReadOnly)
        {
            if (_invoiceService == null)
                return;

            var dialog = new InvoiceEditDialog(_documentMetadata, _metadataService, _invoiceService, invoiceId, isReadOnly);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true && !isReadOnly)
                await LoadDataAsync();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_invoiceService == null)
                return;

            var selected = SelectedInvoice;
            if (selected == null)
                return;

            if (MessageBox.Show("Удалить выбранный счет-фактуру?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _invoiceService.DeleteInvoiceAsync(selected.Id);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnAllPostingsClick(object sender, RoutedEventArgs e)
        {
            var selected = SelectedInvoice;
            if (selected == null)
                return;

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var postingService = new PostingService(context);
                var postings = await postingService.GetPostingsByDocumentAsync(
                    _documentMetadata.Name, selected.DocNumber, selected.DocDate);

                var dialog = new DocumentPostingsDialog(_documentMetadata.Name, selected.DocNumber, postings);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnPrintClick(object sender, RoutedEventArgs e)
        {
            var selected = SelectedInvoice;
            if (selected == null)
            {
                MessageBox.Show("Выберите счет-фактуру для печати.", "Печатная форма",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var printFormService = new PrintFormService(context);
                await printFormService.SeedInvoiceFormsAsync();
                var forms = await printFormService.GetPrintFormsAsync(_documentMetadata.Id, includeInactive: false);
                if (forms.Count == 0)
                {
                    MessageBox.Show("Для счет-фактуры не настроены печатные формы.", "Печатная форма",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                    return;

                var selectedReport = selectionDialog.SelectedReport;
                var selectedFormat = selectionDialog.SelectedFormat;
                StatusText.Text = selectedFormat == PrintFormOutputFormat.Excel
                    ? "Формирование Excel..."
                    : "Формирование PDF...";
                var output = selectedFormat == PrintFormOutputFormat.Excel
                    ? await printFormService.ExportInvoiceDocumentExcelAsync(selectedReport, selected.Id)
                    : await printFormService.ExportInvoiceDocumentAsync(selectedReport, selected.Id);
                var outputPath = await PrintFormOutputFileService.SaveAndOpenAsync(output, selectedReport.Name, selectedFormat);
                StatusText.Text = $"Открыт файл печатной формы: {outputPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка печати";
                MessageBox.Show($"Ошибка формирования печатной формы: {ex.Message}", "Печать",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadDataAsync();

        private async void OnExportEsfClick(object sender, RoutedEventArgs e)
        {
            if (!_isSalesMode || _invoiceEsfExchangeService == null)
                return;

            var modeDialog = new InvoiceEsfExportDialog(SelectedInvoice)
            {
                Owner = Window.GetWindow(this)
            };

            if (modeDialog.ShowDialog() != true)
                return;

            var defaultFileName = modeDialog.Mode == InvoiceEsfExportMode.SelectedInvoice && SelectedInvoice != null
                ? $"esf_{SelectedInvoice.DocNumber}_{SelectedInvoice.DocDate:yyyyMMdd}.xml"
                : $"esf_{modeDialog.StartDate:yyyyMMdd}_{modeDialog.EndDate:yyyyMMdd}.xml";

            var saveDialog = new SaveFileDialog
            {
                Filter = "XML файлы (*.xml)|*.xml",
                FileName = defaultFileName,
                AddExtension = true,
                DefaultExt = ".xml"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            try
            {
                StatusText.Text = "Формирование XML ЭСФ...";
                InvoiceEsfExportResult result;
                if (modeDialog.Mode == InvoiceEsfExportMode.SelectedInvoice)
                {
                    var selected = SelectedInvoice;
                    if (selected == null)
                        throw new InvalidOperationException("Не выбрана счет-фактура для выгрузки.");

                    result = await _invoiceEsfExchangeService.ExportSelectedInvoicesAsync(
                        new[] { selected.Id },
                        saveDialog.FileName);
                }
                else
                {
                    result = await _invoiceEsfExchangeService.ExportPeriodAsync(
                        modeDialog.StartDate,
                        modeDialog.EndDate,
                        modeDialog.OnlyNotExported,
                        saveDialog.FileName);
                }

                await LoadDataAsync();
                StatusText.Text = $"XML выгружен: {result.ExportedCount}";
                MessageBox.Show(
                    $"XML выгружен успешно.\nДокументов: {result.ExportedCount}\nФайл: {result.OutputPath}",
                    "Выгрузка ЭСФ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка выгрузки ЭСФ";
                MessageBox.Show(ex.Message, "Выгрузка ЭСФ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnImportEsfClick(object sender, RoutedEventArgs e)
        {
            if (!_isSalesMode || _invoiceEsfExchangeService == null)
                return;

            var openDialog = new OpenFileDialog
            {
                Filter = "XML файлы (*.xml)|*.xml|Все файлы (*.*)|*.*",
                Multiselect = false
            };

            if (openDialog.ShowDialog() != true)
                return;

            try
            {
                StatusText.Text = "Загрузка ответа налоговой...";
                var result = await _invoiceEsfExchangeService.ImportResponseAsync(openDialog.FileName);
                await LoadDataAsync();
                StatusText.Text = result.SkippedDuplicates == 0
                    ? $"Обновлено ЭСФ: {result.UpdatedCount}"
                    : $"Обновлено ЭСФ: {result.UpdatedCount}, дублей: {result.SkippedDuplicates}";
                var skippedText = result.SkippedDuplicates == 0
                    ? string.Empty
                    : $"\nПропущено дублей: {result.SkippedDuplicates}";
                var unmatchedText = result.UnmatchedReceipts.Count == 0
                    ? "\nВсе записи сопоставлены автоматически."
                    : "\nНе сопоставлено: " + result.UnmatchedReceipts.Count + "\n" +
                      string.Join(Environment.NewLine, result.UnmatchedReceipts.Take(10));

                MessageBox.Show(
                    $"Файл обработан.\nВсего записей: {result.TotalReceipts}\nОбновлено документов: {result.UpdatedCount}{skippedText}{unmatchedText}",
                    "Загрузка ответа",
                    MessageBoxButton.OK,
                    result.UnmatchedReceipts.Count == 0 && result.SkippedDuplicates == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки ответа";
                MessageBox.Show(ex.Message, "Загрузка ответа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
