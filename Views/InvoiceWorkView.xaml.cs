using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    public partial class InvoiceWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private InvoiceService? _invoiceService;

        public InvoiceWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
            TitleText.Text = $"{documentMetadata.Icon} {documentMetadata.Name}";
            DescriptionText.Text = documentMetadata.Description;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            _invoiceService = new InvoiceService(context);
            _invoiceService.Configure(_documentMetadata);
            await _invoiceService.EnsureSchemaAsync();
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
    }
}
