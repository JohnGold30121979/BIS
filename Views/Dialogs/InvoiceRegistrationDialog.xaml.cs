using System;
using System.Threading.Tasks;
using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class InvoiceRegistrationDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly InvoiceService _invoiceService;
        private readonly Guid _invoiceId;
        private readonly bool _isReadOnly;
        private InvoiceDocument? _invoice;

        public InvoiceRegistrationDialog(
            MetadataObject document,
            InvoiceService invoiceService,
            Guid invoiceId,
            bool isReadOnly = false)
        {
            InitializeComponent();
            _document = document;
            _invoiceService = invoiceService;
            _invoiceId = invoiceId;
            _isReadOnly = isReadOnly;
            Loaded += async (_, _) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                await _invoiceService.EnsureSchemaAsync();
                _invoice = await _invoiceService.GetInvoiceAsync(_invoiceId);
                if (_invoice == null)
                {
                    MessageBox.Show("Документ не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                DialogTitle.Text = _isReadOnly
                    ? $"Просмотр: {_document.Name}"
                    : $"Регистрация: {_document.Name}";
                DateBox.Text = _invoice.DocDate.ToString("dd.MM.yyyy");
                DocumentNumberBox.Text = _invoice.DocNumber;
                OrganizationBox.Text = _invoice.OrganizationName;
                EsfNumberBox.Text = _invoice.EsfNumber;
                TotalAmountBox.Text = _invoice.TotalAmount.ToString("N2");
                TaxBlankNumberBox.Text = _invoice.TaxBlankNumber;
                ModuleCodeBox.Text = _invoice.ModuleCode;
                LinesGrid.ItemsSource = _invoice.Lines;

                if (_isReadOnly)
                    DisableEditing();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void DisableEditing()
        {
            TaxBlankNumberBox.IsReadOnly = true;
            ModuleCodeBox.IsReadOnly = true;
            SaveButton.Content = "Закрыть";
            CancelButton.Visibility = Visibility.Collapsed;
            ModeHintText.Visibility = Visibility.Visible;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
            {
                Close();
                return;
            }

            try
            {
                await _invoiceService.UpdateRegistrationInfoAsync(
                    _invoiceId,
                    TaxBlankNumberBox.Text,
                    ModuleCodeBox.Text);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
