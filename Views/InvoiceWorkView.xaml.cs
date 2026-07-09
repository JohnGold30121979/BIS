using System;
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

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
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
            DeleteButton.IsEnabled = hasSelection && selected?.IsPosted == false;
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
                "Реестр зарегистрированных счетов-фактур: просмотр документов и присвоение серии/номера налогового бланка.";
            AddButton.Content = "➕ Добавить счет-фактуру";
            AddButton.Width = 165;
            EditButton.Content = "🧾 Номер бланка";
            EditButton.Width = 135;

            TaxBlankColumn.Visibility = Visibility.Visible;
            ArmColumn.Visibility = Visibility.Visible;
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
                InvoicesGrid.ItemsSource = invoices;
                LinesGrid.ItemsSource = null;
                StatusText.Text = postedCount > 0
                    ? $"Загружено документов: {invoices.Count}; автоматически проведено: {postedCount}"
                    : $"Загружено документов: {invoices.Count}";
                UpdateButtonsState();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

            if (_isRegistrationMode)
            {
                var registrationDialog = new InvoiceRegistrationDialog(
                    _documentMetadata,
                    _invoiceService,
                    invoiceId,
                    isReadOnly);
                registrationDialog.Owner = Window.GetWindow(this);
                if (registrationDialog.ShowDialog() == true && !isReadOnly)
                    await LoadDataAsync();
                return;
            }

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

                StatusText.Text = "Формирование PDF...";
                var pdf = await printFormService.ExportInvoiceDocumentAsync(selectionDialog.SelectedReport, selected.Id);
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

                StatusText.Text = $"Обновлено ЭСФ: {result.UpdatedCount}";
                var unmatchedText = result.UnmatchedReceipts.Count == 0
                    ? "\nВсе записи сопоставлены автоматически."
                    : "\nНе сопоставлено: " + result.UnmatchedReceipts.Count + "\n" +
                      string.Join(Environment.NewLine, result.UnmatchedReceipts.Take(10));

                MessageBox.Show(
                    $"Файл обработан.\nВсего записей: {result.TotalReceipts}\nОбновлено документов: {result.UpdatedCount}{unmatchedText}",
                    "Загрузка ответа",
                    MessageBoxButton.OK,
                    result.UnmatchedReceipts.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки ответа";
                MessageBox.Show(ex.Message, "Загрузка ответа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
