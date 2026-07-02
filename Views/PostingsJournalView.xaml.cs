using System;
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
    public partial class PostingsJournalView : UserControl
    {
        private readonly PostingService _postingService;
        private ObservableCollection<PostingViewModel> _allPostings = new();
        private ObservableCollection<PostingViewModel> _filteredPostings = new();

        public PostingsJournalView(PostingService postingService)
        {
            InitializeComponent();
            _postingService = postingService;

            var today = DateTime.Now;
            dpStartDate.SelectedDate = new DateTime(today.Year, today.Month, 1);
            dpEndDate.SelectedDate = today;

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadPostingsAsync();
        }

        private async Task LoadPostingsAsync()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка проводок...";

                var startDate = dpStartDate.SelectedDate ?? DateTime.Now.AddMonths(-1);
                var endDate = dpEndDate.SelectedDate ?? DateTime.Now;

                var postings = await _postingService.GetAllPostingsAsync(startDate, endDate);

                _allPostings.Clear();
                foreach (var posting in postings)
                {
                    _allPostings.Add(posting);
                }

                ApplyFilters();

                StatusText.Text = $"Загружено {_filteredPostings.Count} проводок";
                TotalInfo.Text = $"Общая сумма: {_filteredPostings.Sum(p => p.Amount):N2} сом";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ApplyFilters()
        {
            var query = _allPostings.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(txtDebetFilter.Text))
                query = query.Where(p => p.DebitAccount.Contains(txtDebetFilter.Text));

            if (!string.IsNullOrWhiteSpace(txtCreditFilter.Text))
                query = query.Where(p => p.CreditAccount.Contains(txtCreditFilter.Text));

            if (!string.IsNullOrWhiteSpace(txtOrganizationFilter.Text))
                query = query.Where(p => p.Organization != null && p.Organization.Contains(txtOrganizationFilter.Text));

            if (!string.IsNullOrWhiteSpace(txtEmployeeFilter.Text))
                query = query.Where(p => p.Employee != null && p.Employee.Contains(txtEmployeeFilter.Text));

            if (!string.IsNullOrWhiteSpace(txtDocumentFilter.Text))
                query = query.Where(p => p.DocumentNumber.Contains(txtDocumentFilter.Text));

            if (!string.IsNullOrWhiteSpace(txtQuickSearch.Text))
            {
                var searchText = txtQuickSearch.Text.ToLower();
                query = query.Where(p =>
                    p.DebitAccount.ToLower().Contains(searchText) ||
                    p.CreditAccount.ToLower().Contains(searchText) ||
                    p.DocumentNumber.ToLower().Contains(searchText) ||
                    (p.Note?.ToLower().Contains(searchText) ?? false) ||
                    (p.Organization?.ToLower().Contains(searchText) ?? false) ||
                    (p.Employee?.ToLower().Contains(searchText) ?? false));
            }

            _filteredPostings.Clear();
            foreach (var item in query)
            {
                _filteredPostings.Add(item);
            }

            PostingsGrid.ItemsSource = _filteredPostings;


            TotalInfo.Text = $"Общая сумма: {_filteredPostings.Sum(p => p.Amount):N2} сом";
        }

        private void OnQuickSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadPostingsAsync();

        private void OnClearFiltersClick(object sender, RoutedEventArgs e)
        {
            txtDebetFilter.Text = "";
            txtCreditFilter.Text = "";
            txtOrganizationFilter.Text = "";
            txtEmployeeFilter.Text = "";
            txtDocumentFilter.Text = "";
            txtQuickSearch.Text = "";
            ApplyFilters();
        }

        private void OnTurnoversClick(object sender, RoutedEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as PostingViewModel;
            if (selected == null)
            {
                MessageBox.Show("Выберите проводку для просмотра оборотов по счету", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // TODO: Открыть диалог оборотов
            MessageBox.Show($"Обороты по счету {selected.DebitAccount}", "Информация");
        }

        private void OnSummaryClick(object sender, RoutedEventArgs e)
        {
            // TODO: Открыть диалог сводных оборотов
            MessageBox.Show("Сводные обороты", "Информация");
        }

        private void PostingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PostingsGrid.SelectedItem is PostingViewModel selected)
                StatusText.Text = selected.DetailHint;
        }

        private async void PostingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as PostingViewModel;
            if (selected == null)
                return;

            if (await TryOpenInvoiceFromPostingAsync(selected))
                return;

            var dialog = new PostingDetailsDialog(selected);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }

        private async Task<bool> TryOpenInvoiceFromPostingAsync(PostingViewModel posting)
        {
            if (!InvoiceDocumentTypes.IsSales(posting.DocumentType) &&
                !InvoiceDocumentTypes.IsPurchase(posting.DocumentType))
                return false;

            var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(posting.DocumentNumber);
            if (string.IsNullOrWhiteSpace(documentNumber))
            {
                MessageBox.Show("В проводке не указан номер счет-фактуры.", "Счет-фактура",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var metadataService = new MetadataService(context);
                var documents = await metadataService.GetDocumentsAsync();
                var invoiceMetadata = documents.FirstOrDefault(document =>
                    document.Name.Equals(posting.DocumentType, StringComparison.OrdinalIgnoreCase));

                if (invoiceMetadata == null)
                {
                    MessageBox.Show($"Метаданные документа «{posting.DocumentType}» не найдены.", "Счет-фактура",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return true;
                }

                var invoiceService = new InvoiceService(context);
                invoiceService.Configure(invoiceMetadata);
                await invoiceService.EnsureSchemaAsync();

                var invoiceId = await invoiceService.FindInvoiceIdAsync(documentNumber, posting.Date);
                if (!invoiceId.HasValue)
                    invoiceId = await invoiceService.FindInvoiceIdAsync(documentNumber);

                if (!invoiceId.HasValue)
                {
                    MessageBox.Show($"Счет-фактура №{documentNumber} не найдена.", "Счет-фактура",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return true;
                }

                var dialog = new InvoiceEditDialog(
                    invoiceMetadata,
                    metadataService,
                    invoiceService,
                    invoiceId.Value,
                    isReadOnly: true);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия счет-фактуры: {ex.Message}", "Счет-фактура",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }
    }
}
