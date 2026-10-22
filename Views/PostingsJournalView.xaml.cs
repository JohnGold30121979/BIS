using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using ClosedXML.Excel;

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

            query = ApplyColumnFilter(query, DateFilterBox.Text, p => p.Date.ToString("dd/MM/yyyy"));
            query = ApplyColumnFilter(query, DocumentFilterBox.Text, p => p.DocumentNumber);
            query = ApplyColumnFilter(query, DocumentTypeFilterBox.Text, p => p.DocumentType);
            query = ApplyColumnFilter(query, ModuleFilterBox.Text, p => p.ModuleName);
            query = ApplyColumnFilter(query, DebitFilterBox.Text, p => p.DebitAccount);
            query = ApplyColumnFilter(query, CreditFilterBox.Text, p => p.CreditAccount);
            query = ApplyColumnFilter(query, AmountFilterBox.Text, p => FormatAmount(p.Amount));
            query = ApplyColumnFilter(query, AmountCurrencyFilterBox.Text, p => FormatAmount(p.AmountCurrency));
            query = ApplyColumnFilter(query, CurrencyFilterBox.Text, p => p.Currency);
            if (IsOrganizationColumnVisible())
                query = ApplyColumnFilter(query, OrganizationFilterBox.Text, p => p.Organization);
            if (IsEmployeeColumnVisible())
                query = ApplyColumnFilter(query, EmployeeFilterBox.Text, p => p.Employee);
            query = ApplyColumnFilter(query, NoteFilterBox.Text, p => p.Note);

            _filteredPostings.Clear();
            foreach (var item in query)
            {
                _filteredPostings.Add(item);
            }

            PostingsGrid.ItemsSource = _filteredPostings;
            StatusText.Text = $"Показано {_filteredPostings.Count} проводок";
            TotalInfo.Text = $"Общая сумма: {_filteredPostings.Sum(p => p.Amount):N2} сом";
        }

        private void OnColumnVisibilityChanged(object sender, RoutedEventArgs e)
        {
            ApplyAnalyticsColumnVisibility();
            ApplyFilters();
        }

        private void ApplyAnalyticsColumnVisibility()
        {
            OrganizationColumn.Visibility = IsOrganizationColumnVisible()
                ? Visibility.Visible
                : Visibility.Collapsed;
            EmployeeColumn.Visibility = IsEmployeeColumnVisible()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private bool IsOrganizationColumnVisible() => ShowOrganizationColumnCheckBox?.IsChecked == true;

        private bool IsEmployeeColumnVisible() => ShowEmployeeColumnCheckBox?.IsChecked == true;
        private static IEnumerable<PostingViewModel> ApplyColumnFilter(
            IEnumerable<PostingViewModel> query,
            string filter,
            Func<PostingViewModel, string?> valueSelector)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return query;

            var filterText = filter.Trim();
            return query.Where(posting => MatchesOrderedColumnFilter(valueSelector(posting), filterText));
        }

        private static bool MatchesOrderedColumnFilter(string? value, string filterText)
        {
            var valueText = (value ?? string.Empty).Trim();
            if (valueText.Length == 0)
                return false;

            var filterDigits = ExtractDigits(filterText);
            if (filterDigits.Length > 0)
                return ExtractDigits(valueText).StartsWith(filterDigits, StringComparison.Ordinal);

            return valueText.StartsWith(filterText, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static string FormatAmount(decimal amount) => amount.ToString("N2");

        private void OnColumnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadPostingsAsync();

        private void OnTurnoversClick(object sender, RoutedEventArgs e)
        {
            if (_filteredPostings.Count == 0)
            {
                MessageBox.Show("В журнале нет проводок для расчета оборотов по счету.", "Обороты по счету",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new AccountTurnoverSelectionDialog(
                _filteredPostings,
                dpStartDate.SelectedDate ?? DateTime.Now,
                dpEndDate.SelectedDate ?? DateTime.Now,
                PostingsGrid.SelectedItem as PostingViewModel)
            {
                Owner = Window.GetWindow(this)
            };
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Обороты по счету", null, fillWorkspace: true);
        }

        private Report? _chosenPrintForm;

        private PostingsJournalVariant GetSelectedVariant() =>
            (JournalReportKindComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Brief" => PostingsJournalVariant.Brief,
                "ByArticle" => PostingsJournalVariant.ByArticle,
                "BySubaccount" => PostingsJournalVariant.BySubaccount,
                _ => PostingsJournalVariant.Full
            };

        /// <summary>
        /// Печать журнала по макету ClosedXML: тот же макет уходит и в Excel, и в PDF.
        /// Кнопки работают без диалога; список форм открывает только флажок «Выбрать форму».
        /// </summary>
        private async void OnPrintExcelClick(object sender, RoutedEventArgs e) => await PrintJournalAsync(toPdf: false);

        private async void OnPrintPdfClick(object sender, RoutedEventArgs e) => await PrintJournalAsync(toPdf: true);

        private async Task PrintJournalAsync(bool toPdf)
        {
            if (_filteredPostings.Count == 0)
            {
                MessageBox.Show("В журнале нет проводок для формирования отчета.", "Журнал проводок",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var rows = _filteredPostings.ToList();
                var startDate = dpStartDate.SelectedDate ?? rows.Min(row => row.Date);
                var endDate = dpEndDate.SelectedDate ?? rows.Max(row => row.Date);

                var variant = _chosenPrintForm != null
                    ? PostingsJournalPrintService.ParseVariant(_chosenPrintForm.Code)
                    : GetSelectedVariant();

                var table = PostingsJournalPrintService.BuildTable(variant, rows, startDate, endDate);

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var service = new PostingsJournalPrintService(context);

                var forms = await service.GetPrintFormsAsync();
                var report = _chosenPrintForm != null
                    ? forms.FirstOrDefault(form => form.Id == _chosenPrintForm.Id) ?? forms.First()
                    : forms.First(form => form.Code == PostingsJournalPrintService.GetCode(variant));

                if (toPdf)
                {
                    StatusText.Text = "Формирование PDF журнала проводок...";
                    var pdf = await Task.Run(() => PostingsJournalPrintService.RenderPdf(report, table, startDate, endDate));
                    var previewWindow = new PdfPreviewWindow(pdf, report.Name) { Owner = Window.GetWindow(this) };
                    previewWindow.ShowDialog();
                    StatusText.Text = "Предпросмотр PDF открыт";
                }
                else
                {
                    StatusText.Text = "Формирование Excel журнала проводок...";
                    var content = await Task.Run(() => PostingsJournalPrintService.RenderExcel(report, table, startDate, endDate));
                    var path = await PrintFormOutputFileService.SaveAndOpenAsync(
                        content, report.Name, PrintFormOutputFormat.Excel);
                    StatusText.Text = $"Excel-отчет открыт: {Path.GetFileName(path)}";
                }
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка формирования отчета журнала проводок (ClosedXML).",
                    "PostingsJournalView.PrintJournalAsync", ex);
                StatusText.Text = $"Ошибка отчета: {ex.Message}";
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Журнал проводок",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnChoosePrintFormChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var service = new PostingsJournalPrintService(context);
                var forms = await service.GetPrintFormsAsync();

                if (forms.Count == 0)
                {
                    ChoosePrintFormCheckBox.IsChecked = false;
                    MessageBox.Show("Печатные формы журнала не найдены.", "Печать",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selectionDialog = new PrintFormSelectionDialog(forms) { Owner = Window.GetWindow(this) };
                if (selectionDialog.ShowDialog() != true || selectionDialog.SelectedReport == null)
                {
                    ChoosePrintFormCheckBox.IsChecked = false;
                    return;
                }

                _chosenPrintForm = selectionDialog.SelectedReport;
                StatusText.Text = $"Печатная форма: {_chosenPrintForm.Name}";
            }
            catch (Exception ex)
            {
                ChoosePrintFormCheckBox.IsChecked = false;
                SystemLogService.Error("Ошибка выбора печатной формы журнала проводок.",
                    "PostingsJournalView.OnChoosePrintFormChecked", ex);
            }
        }

        private void OnChoosePrintFormUnchecked(object sender, RoutedEventArgs e)
        {
            _chosenPrintForm = null;
            StatusText.Text = "Печатная форма по умолчанию";
        }

        private async void OnOpenSourceDocumentClick(object sender, RoutedEventArgs e)
        {
            if (PostingsGrid.SelectedItem is not PostingViewModel selected)
            {
                MessageBox.Show("Выберите проводку, по которой нужно открыть первичный документ.", "Первичный документ",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var owner = Window.GetWindow(this);
                var sourceResult = await PostingSourceDocumentOpener.TryOpenAsync(selected, null, owner, isReadOnly: false);
                if (sourceResult != null)
                {
                    // Исходный документ был открыт: обновляем журнал, если он изменен и сохранен.
                    if (sourceResult.Value)
                    {
                        await LoadPostingsAsync();
                    }

                    return;
                }

                if (selected.Id == Guid.Empty)
                {
                    MessageBox.Show("Первичный документ по выбранной проводке не найден.", "Первичный документ",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var metadataService = new MetadataService(context);
                var postingDocument = (await metadataService.GetDocumentsAsync())
                    .FirstOrDefault(document =>
                        document.Name.Equals("Проводки", StringComparison.OrdinalIgnoreCase) ||
                        document.TableName.Equals("doc_postings", StringComparison.OrdinalIgnoreCase));

                if (postingDocument == null)
                {
                    MessageBox.Show("Метаданные документа «Проводки» не найдены.", "Первичный документ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new PostingEditDialog(postingDocument, metadataService, selected.Id)
                {
                    Owner = owner
                };
                dialog.ShowDialog();
                await LoadPostingsAsync();
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка открытия первичного документа из журнала проводок.", "PostingsJournalView.OnOpenSourceDocumentClick", ex);
                MessageBox.Show($"Ошибка открытия первичного документа: {ex.Message}", "Первичный документ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            if (InvoiceDocumentTypes.IsSales(selected.DocumentType) ||
                InvoiceDocumentTypes.IsPurchase(selected.DocumentType))
            {
                var sourceResult = await PostingSourceDocumentOpener.TryOpenAsync(
                        selected,
                        null,
                        Window.GetWindow(this),
                        isReadOnly: true);
                if (sourceResult != null)
                {
                    return;
                }
            }

            var dialog = new PostingDetailsDialog(selected);
            dialog.Owner = Window.GetWindow(this);
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Детали проводки: {selected.DocumentNumber}", null, fillWorkspace: true);
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

                var invoiceId = await invoiceService.FindInvoiceIdByPostingNumberAsync(posting.DocumentNumber, posting.Date);

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
                    metadataService,
                    invoiceService,
                    invoiceId.Value,
                    isReadOnly: true);
                await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    "Просмотр счет-фактуры");
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



