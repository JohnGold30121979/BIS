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

            query = ApplyColumnFilter(query, DateFilterBox.Text, p => p.Date.ToString("dd.MM.yyyy"));
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
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Обороты по счету");
        }

        private async void OnOpenJournalReportClick(object sender, RoutedEventArgs e)
        {
            if (_filteredPostings.Count == 0)
            {
                MessageBox.Show("В журнале нет проводок для формирования отчета.", "Журнал проводок",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rows = _filteredPostings.ToList();
            var kind = GetSelectedJournalReportKind();
            var startDate = dpStartDate.SelectedDate ?? rows.Min(row => row.Date);
            var endDate = dpEndDate.SelectedDate ?? rows.Max(row => row.Date);

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Формирование Excel-отчета журнала проводок...";

                var path = BuildTemporaryExcelPath($"BIS_Журнал_проводок_{kind}");
                await Task.Run(() => ExportJournalReportExcel(path, kind, rows, startDate, endDate));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

                StatusText.Text = "Excel-отчет журнала проводок открыт";
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка формирования Excel-отчета журнала проводок.", "PostingsJournalView.OnOpenJournalReportClick", ex);
                StatusText.Text = $"Ошибка отчета: {ex.Message}";
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Журнал проводок",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private JournalReportKind GetSelectedJournalReportKind()
        {
            return (JournalReportKindComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Brief" => JournalReportKind.Brief,
                "ByArticle" => JournalReportKind.ByArticle,
                "BySubaccount" => JournalReportKind.BySubaccount,
                _ => JournalReportKind.Full
            };
        }

        private void ExportJournalReportExcel(
            string path,
            JournalReportKind kind,
            IReadOnlyList<PostingViewModel> rows,
            DateTime startDate,
            DateTime endDate)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(SafeSheetName(GetJournalReportKindTitle(kind)));

            var row = 1;
            sheet.Cell(row, 1).Value = "Журнал проводок";
            sheet.Range(row, 1, row, 8).Merge().Style.Font.SetBold().Font.SetFontSize(14);
            row++;
            sheet.Cell(row, 1).Value = $"Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";
            sheet.Range(row, 1, row, 8).Merge();
            row++;
            sheet.Cell(row, 1).Value = $"Вид отчета: {GetJournalReportKindTitle(kind)}";
            sheet.Range(row, 1, row, 8).Merge();
            row += 2;

            row = kind switch
            {
                JournalReportKind.Brief => WriteBriefJournalReport(sheet, row, rows),
                JournalReportKind.ByArticle => WriteJournalByArticleReport(sheet, row, rows),
                JournalReportKind.BySubaccount => WriteJournalBySubaccountReport(sheet, row, rows),
                _ => WriteFullJournalReport(sheet, row, rows)
            };

            row++;
            WriteJournalTotals(sheet, row, kind, rows);

            var usedRange = sheet.RangeUsed();
            if (usedRange != null)
            {
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            sheet.SheetView.FreezeRows(5);
            sheet.Columns().AdjustToContents();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            workbook.SaveAs(path);
        }

        private static int WriteFullJournalReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteJournalHeader(sheet, row,
                "Дата", "Документ", "Тип", "Модуль", "Дебет", "Кредит", "Сумма", "Сумма вал.",
                "Валюта", "Организация", "Сотрудник", "Содержание");
            row++;

            foreach (var posting in rows.OrderBy(item => item.Date).ThenBy(item => item.DocumentNumber))
            {
                sheet.Cell(row, 1).Value = posting.Date;
                sheet.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
                sheet.Cell(row, 2).Value = posting.DocumentNumber;
                sheet.Cell(row, 3).Value = posting.DocumentType;
                sheet.Cell(row, 4).Value = posting.ModuleName;
                sheet.Cell(row, 5).Value = posting.DebitAccount;
                sheet.Cell(row, 6).Value = posting.CreditAccount;
                sheet.Cell(row, 7).Value = posting.Amount;
                sheet.Cell(row, 8).Value = posting.AmountCurrency;
                sheet.Cell(row, 9).Value = posting.Currency;
                sheet.Cell(row, 10).Value = posting.Organization;
                sheet.Cell(row, 11).Value = posting.Employee;
                sheet.Cell(row, 12).Value = posting.Note;
                row++;
            }

            sheet.Range(1, 7, Math.Max(row, 1), 8).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteBriefJournalReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteJournalHeader(sheet, row,
                "Дата", "Документ", "Тип", "Модуль", "Дебет", "Кредит", "Кол-во", "Сумма", "Сумма вал.", "Содержание");
            row++;

            var groups = rows
                .GroupBy(posting => new
                {
                    Date = posting.Date.Date,
                    posting.DocumentNumber,
                    posting.DocumentType,
                    posting.ModuleName,
                    posting.DebitAccount,
                    posting.CreditAccount,
                    Note = posting.Note ?? string.Empty
                })
                .OrderBy(group => group.Key.Date)
                .ThenBy(group => group.Key.DocumentNumber);

            foreach (var group in groups)
            {
                sheet.Cell(row, 1).Value = group.Key.Date;
                sheet.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
                sheet.Cell(row, 2).Value = group.Key.DocumentNumber;
                sheet.Cell(row, 3).Value = group.Key.DocumentType;
                sheet.Cell(row, 4).Value = group.Key.ModuleName;
                sheet.Cell(row, 5).Value = group.Key.DebitAccount;
                sheet.Cell(row, 6).Value = group.Key.CreditAccount;
                sheet.Cell(row, 7).Value = group.Count();
                sheet.Cell(row, 8).Value = group.Sum(item => item.Amount);
                sheet.Cell(row, 9).Value = group.Sum(item => item.AmountCurrency);
                sheet.Cell(row, 10).Value = group.Key.Note;
                row++;
            }

            sheet.Range(1, 8, Math.Max(row, 1), 9).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteJournalByArticleReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteJournalHeader(sheet, row, "Статья / содержание", "Кол-во", "Оборот сом", "Оборот вал.");
            row++;

            var groups = rows
                .GroupBy(posting => string.IsNullOrWhiteSpace(posting.Note) ? "(без статьи)" : posting.Note.Trim())
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                sheet.Cell(row, 1).Value = group.Key;
                sheet.Cell(row, 2).Value = group.Count();
                sheet.Cell(row, 3).Value = group.Sum(item => item.Amount);
                sheet.Cell(row, 4).Value = group.Sum(item => item.AmountCurrency);
                row++;
            }

            sheet.Range(1, 3, Math.Max(row, 1), 4).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteJournalBySubaccountReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteJournalHeader(sheet, row, "Субсчет", "Наименование", "Кол-во", "Дебет оборот", "Кредит оборот");
            row++;

            var debitRows = rows.Select(posting => new
            {
                Code = posting.DebitAccount,
                Name = posting.DebitAccountName,
                DebitAmount = posting.Amount,
                CreditAmount = 0m
            });
            var creditRows = rows.Select(posting => new
            {
                Code = posting.CreditAccount,
                Name = posting.CreditAccountName,
                DebitAmount = 0m,
                CreditAmount = posting.Amount
            });

            var groups = debitRows
                .Concat(creditRows)
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var name = group.Select(item => item.Name).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                sheet.Cell(row, 1).Value = group.Key;
                sheet.Cell(row, 2).Value = name;
                sheet.Cell(row, 3).Value = group.Count();
                sheet.Cell(row, 4).Value = group.Sum(item => item.DebitAmount);
                sheet.Cell(row, 5).Value = group.Sum(item => item.CreditAmount);
                row++;
            }

            sheet.Range(1, 4, Math.Max(row, 1), 5).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static void WriteJournalTotals(IXLWorksheet sheet, int row, JournalReportKind kind, IReadOnlyList<PostingViewModel> rows)
        {
            sheet.Cell(row, 1).Value = "Итого";
            sheet.Cell(row, 1).Style.Font.SetBold();

            switch (kind)
            {
                case JournalReportKind.Brief:
                    sheet.Cell(row, 8).Value = rows.Sum(item => item.Amount);
                    sheet.Cell(row, 9).Value = rows.Sum(item => item.AmountCurrency);
                    sheet.Range(row, 8, row, 9).Style.NumberFormat.Format = "#,##0.00";
                    sheet.Range(row, 1, row, 9).Style.Font.SetBold();
                    break;
                case JournalReportKind.ByArticle:
                    sheet.Cell(row, 3).Value = rows.Sum(item => item.Amount);
                    sheet.Cell(row, 4).Value = rows.Sum(item => item.AmountCurrency);
                    sheet.Range(row, 3, row, 4).Style.NumberFormat.Format = "#,##0.00";
                    sheet.Range(row, 1, row, 4).Style.Font.SetBold();
                    break;
                case JournalReportKind.BySubaccount:
                    sheet.Cell(row, 4).Value = rows.Sum(item => item.Amount);
                    sheet.Cell(row, 5).Value = rows.Sum(item => item.Amount);
                    sheet.Range(row, 4, row, 5).Style.NumberFormat.Format = "#,##0.00";
                    sheet.Range(row, 1, row, 5).Style.Font.SetBold();
                    break;
                default:
                    sheet.Cell(row, 7).Value = rows.Sum(item => item.Amount);
                    sheet.Cell(row, 8).Value = rows.Sum(item => item.AmountCurrency);
                    sheet.Range(row, 7, row, 8).Style.NumberFormat.Format = "#,##0.00";
                    sheet.Range(row, 1, row, 8).Style.Font.SetBold();
                    break;
            }
        }

        private static void WriteJournalHeader(IXLWorksheet sheet, int row, params string[] headers)
        {
            for (var index = 0; index < headers.Length; index++)
            {
                var cell = sheet.Cell(row, index + 1);
                cell.Value = headers[index];
                cell.Style.Font.SetBold();
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEAF6");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        private static string GetJournalReportKindTitle(JournalReportKind kind) => kind switch
        {
            JournalReportKind.Brief => "Краткая",
            JournalReportKind.ByArticle => "Итоги по статьям",
            JournalReportKind.BySubaccount => "Итоги по субсчетам",
            _ => "Полная"
        };

        private static string BuildTemporaryExcelPath(string baseName)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                baseName = baseName.Replace(invalid, '_');

            var directory = Path.Combine(Path.GetTempPath(), "BIS ERP", "Reports");
            return Path.Combine(directory, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        private static string SafeSheetName(string name)
        {
            var safeName = name;
            foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' })
                safeName = safeName.Replace(invalid, ' ');

            safeName = safeName.Trim();
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Журнал";

            return safeName.Length > 31 ? safeName[..31] : safeName;
        }

        private enum JournalReportKind
        {
            Full,
            Brief,
            ByArticle,
            BySubaccount
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
            MdiDialogService.ShowInWorkspaceOrDialog(Window.GetWindow(this), dialog, $"Детали проводки: {selected.DocumentNumber}");
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


