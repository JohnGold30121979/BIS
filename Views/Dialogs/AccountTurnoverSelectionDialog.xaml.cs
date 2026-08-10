using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using ClosedXML.Excel;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AccountTurnoverSelectionDialog : Window
    {
        private readonly List<PostingViewModel> _postings;
        private readonly DateTime _startDate;
        private readonly DateTime _endDate;
        private readonly ObservableCollection<PostingViewModel> _details = new();
        private List<AccountTurnoverAccountItem> _accounts = new();
        private bool _isReloadingAccounts;

        public AccountTurnoverSelectionDialog(
            IEnumerable<PostingViewModel> postings,
            DateTime startDate,
            DateTime endDate,
            PostingViewModel? selectedPosting = null)
        {
            InitializeComponent();

            _postings = postings.ToList();
            _startDate = startDate.Date;
            _endDate = endDate.Date;
            DetailsGrid.ItemsSource = _details;
            PeriodText.Text = $"Период: {_startDate:dd.MM.yyyy} - {_endDate:dd.MM.yyyy}. Счета берутся из текущего журнала проводок.";

            var initialSide = AccountTurnoverSide.Both;
            SelectSide(initialSide);
            ReloadAccounts(initialSide, selectedPosting);
            Recalculate();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _isReloadingAccounts)
                return;

            if (sender == SideComboBox)
                ReloadAccounts(GetSelectedSide(), null);

            Recalculate();
        }

        private void OnAccountSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _isReloadingAccounts)
                return;

            ApplyAccountSearch();
            Recalculate();
        }

        private void ReloadAccounts(AccountTurnoverSide side, PostingViewModel? selectedPosting)
        {
            var selectedAccount = !string.IsNullOrWhiteSpace(selectedPosting?.DebitAccount)
                ? selectedPosting.DebitAccount
                : selectedPosting?.CreditAccount;

            _accounts = _postings
                .SelectMany(CreateAccountItems)
                .Where(account => !string.IsNullOrWhiteSpace(account.Code))
                .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(account => !string.IsNullOrWhiteSpace(account.Name))
                    .First())
                .OrderBy(account => NormalizeAccountCode(account.Code), StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApplyAccountSearch(selectedAccount);
        }

        private void ApplyAccountSearch(string? selectedAccountCode = null)
        {
            try
            {
                _isReloadingAccounts = true;
                var currentCode = selectedAccountCode ?? (AccountComboBox.SelectedItem as AccountTurnoverAccountItem)?.Code;
                var accounts = _accounts
                    .Where(account => MatchesAccountSearch(account, AccountSearchBox.Text))
                    .ToList();

                AccountComboBox.ItemsSource = accounts;
                AccountComboBox.SelectedItem = accounts.FirstOrDefault(account =>
                    !string.IsNullOrWhiteSpace(currentCode) &&
                    account.Code.Equals(currentCode, StringComparison.OrdinalIgnoreCase));
                AccountComboBox.SelectedItem ??= accounts.FirstOrDefault();
            }
            finally
            {
                _isReloadingAccounts = false;
            }
        }

        private void Recalculate()
        {
            var accountFilter = GetActiveAccountFilter();
            var rows = GetCurrentRows(accountFilter);

            _details.Clear();
            foreach (var row in rows)
                _details.Add(row);

            var debitTotal = accountFilter.IsEmpty
                ? 0m
                : rows.Where(row => AccountCodeMatches(row.DebitAccount, accountFilter)).Sum(row => row.Amount);
            var creditTotal = accountFilter.IsEmpty
                ? 0m
                : rows.Where(row => AccountCodeMatches(row.CreditAccount, accountFilter)).Sum(row => row.Amount);

            CountText.Text = rows.Count.ToString("N0");
            AmountText.Text = debitTotal.ToString("N2");
            CreditAmountText.Text = creditTotal.ToString("N2");
            CurrencyAmountText.Text = rows.Sum(row => row.AmountCurrency).ToString("N2");
        }

        private List<PostingViewModel> GetCurrentRows()
        {
            return GetCurrentRows(GetActiveAccountFilter());
        }

        private List<PostingViewModel> GetCurrentRows(AccountTurnoverAccountFilter accountFilter)
        {
            if (accountFilter.IsEmpty)
                return new List<PostingViewModel>();

            var side = GetSelectedSide();
            return _postings
                .Where(posting => AccountMatches(posting, side, accountFilter))
                .OrderBy(posting => posting.Date)
                .ThenBy(posting => posting.DocumentNumber)
                .ToList();
        }

        private AccountTurnoverAccountFilter GetActiveAccountFilter()
        {
            var searchDigits = NormalizeAccountCode(AccountSearchBox.Text);
            if (!string.IsNullOrWhiteSpace(searchDigits))
            {
                return new AccountTurnoverAccountFilter(searchDigits, true, $"счета с началом {searchDigits}");
            }

            var account = AccountComboBox.SelectedItem as AccountTurnoverAccountItem;
            var accountCode = NormalizeAccountCode(account?.Code);
            return new AccountTurnoverAccountFilter(accountCode, false, account?.DisplayName ?? accountCode);
        }

        private static bool AccountMatches(PostingViewModel posting, AccountTurnoverSide side, AccountTurnoverAccountFilter accountFilter)
        {
            return side switch
            {
                AccountTurnoverSide.Debit => AccountCodeMatches(posting.DebitAccount, accountFilter),
                AccountTurnoverSide.Credit => AccountCodeMatches(posting.CreditAccount, accountFilter),
                _ => AccountCodeMatches(posting.DebitAccount, accountFilter) ||
                     AccountCodeMatches(posting.CreditAccount, accountFilter)
            };
        }

        private static bool AccountCodeMatches(string? accountCode, AccountTurnoverAccountFilter accountFilter)
        {
            var normalizedCode = NormalizeAccountCode(accountCode);
            if (string.IsNullOrWhiteSpace(normalizedCode) || accountFilter.IsEmpty)
                return false;

            return accountFilter.UsePrefix
                ? normalizedCode.StartsWith(accountFilter.Code, StringComparison.Ordinal)
                : normalizedCode.Equals(accountFilter.Code, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AccountCodeEquals(string? left, string? right)
        {
            return NormalizeAccountCode(left).Equals(NormalizeAccountCode(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAccountCode(string? accountCode)
        {
            return ExtractDigits(accountCode ?? string.Empty);
        }

        private static bool MatchesAccountSearch(AccountTurnoverAccountItem account, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            var filterText = filter.Trim();
            var filterDigits = ExtractDigits(filterText);
            if (filterDigits.Length > 0)
                return ExtractDigits(account.Code).StartsWith(filterDigits, StringComparison.Ordinal);

            return account.DisplayName.StartsWith(filterText, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private async void OnOpenExcelClick(object sender, RoutedEventArgs e)
        {
            var accountFilter = GetActiveAccountFilter();
            if (accountFilter.IsEmpty)
            {
                MessageBox.Show("Выберите счет или введите начало счета для формирования отчета.", "Обороты по счету",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rows = GetCurrentRows(accountFilter);
            if (rows.Count == 0)
            {
                MessageBox.Show("По выбранному счету нет проводок для отчета.", "Обороты по счету",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                var side = GetSelectedSide();
                var kind = GetSelectedReportKind();
                var path = BuildTemporaryExcelPath($"BIS_Обороты_{accountFilter.Code}_{kind}");
                await Task.Run(() => ExportExcel(path, accountFilter, side, kind, rows));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SystemLogService.Error("Ошибка формирования Excel отчета оборотов по счету.", "AccountTurnoverSelectionDialog.OnOpenExcelClick", ex);
                MessageBox.Show($"Ошибка формирования Excel: {ex.Message}", "Обороты по счету",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void ExportExcel(
            string path,
            AccountTurnoverAccountFilter accountFilter,
            AccountTurnoverSide side,
            AccountTurnoverReportKind kind,
            IReadOnlyList<PostingViewModel> rows)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(SafeSheetName(GetReportKindTitle(kind)));

            var row = 1;
            sheet.Cell(row, 1).Value = "Обороты по счету";
            sheet.Range(row, 1, row, 8).Merge().Style.Font.SetBold().Font.SetFontSize(14);
            row++;
            sheet.Cell(row, 1).Value = $"Период: {_startDate:dd.MM.yyyy} - {_endDate:dd.MM.yyyy}";
            sheet.Range(row, 1, row, 8).Merge();
            row++;
            sheet.Cell(row, 1).Value = $"Сторона: {GetSideTitle(side)}";
            sheet.Range(row, 1, row, 3).Merge();
            sheet.Cell(row, 4).Value = $"Счет: {accountFilter.DisplayName}";
            sheet.Range(row, 4, row, 8).Merge();
            row += 2;

            row = kind switch
            {
                AccountTurnoverReportKind.Brief => WriteBriefReport(sheet, row, rows),
                AccountTurnoverReportKind.ByArticle => WriteByArticleReport(sheet, row, rows),
                AccountTurnoverReportKind.BySubaccount => WriteBySubaccountReport(sheet, row, side, accountFilter, rows),
                _ => WriteFullReport(sheet, row, rows)
            };

            row++;
            var (amountColumn, currencyAmountColumn) = GetReportAmountColumns(kind);
            sheet.Cell(row, 1).Value = "Итого";
            sheet.Cell(row, 1).Style.Font.SetBold();
            sheet.Cell(row, amountColumn).Value = rows.Sum(item => item.Amount);
            sheet.Cell(row, currencyAmountColumn).Value = rows.Sum(item => item.AmountCurrency);
            sheet.Range(row, amountColumn, row, currencyAmountColumn).Style.NumberFormat.Format = "#,##0.00";
            sheet.Range(row, 1, row, currencyAmountColumn).Style.Font.SetBold();

            var usedRange = sheet.RangeUsed();
            if (usedRange != null)
            {
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            sheet.Columns().AdjustToContents();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            workbook.SaveAs(path);
        }

        private static int WriteFullReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            var headers = new[]
            {
                "Дата", "Документ", "Тип", "Модуль", "Дебет", "Кредит", "Сумма", "Сумма вал.", "Валюта", "Организация", "Сотрудник", "Примечание"
            };
            WriteHeader(sheet, row, headers);
            row++;

            foreach (var posting in rows)
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

            sheet.Range(2, 7, Math.Max(row, 2), 8).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteBriefReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteHeader(sheet, row, "Дата", "Документ", "Тип", "Дебет", "Кредит", "Кол-во", "Сумма", "Сумма вал.", "Содержание");
            row++;

            var groups = rows
                .GroupBy(posting => new
                {
                    Date = posting.Date.Date,
                    posting.DocumentNumber,
                    posting.DocumentType,
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
                sheet.Cell(row, 4).Value = group.Key.DebitAccount;
                sheet.Cell(row, 5).Value = group.Key.CreditAccount;
                sheet.Cell(row, 6).Value = group.Count();
                sheet.Cell(row, 7).Value = group.Sum(item => item.Amount);
                sheet.Cell(row, 8).Value = group.Sum(item => item.AmountCurrency);
                sheet.Cell(row, 9).Value = group.Key.Note;
                row++;
            }

            sheet.Range(2, 7, Math.Max(row, 2), 8).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteByArticleReport(IXLWorksheet sheet, int row, IReadOnlyList<PostingViewModel> rows)
        {
            WriteHeader(sheet, row, "Статья / содержание", "Кол-во", "Оборот сом", "Оборот вал.");
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

            sheet.Range(2, 3, Math.Max(row, 2), 4).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static int WriteBySubaccountReport(IXLWorksheet sheet, int row, AccountTurnoverSide side, AccountTurnoverAccountFilter accountFilter, IReadOnlyList<PostingViewModel> rows)
        {
            WriteHeader(sheet, row, "Субсчет", "Наименование", "Кол-во", "Оборот сом", "Оборот вал.");
            row++;

            var groups = rows
                .Select(posting =>
                {
                    var selectedOnDebit = AccountCodeMatches(posting.DebitAccount, accountFilter);
                    var useCreditCounterpart = side == AccountTurnoverSide.Debit ||
                                               (side == AccountTurnoverSide.Both && selectedOnDebit);
                    return new
                    {
                        Code = useCreditCounterpart ? posting.CreditAccount : posting.DebitAccount,
                        Name = useCreditCounterpart ? posting.CreditAccountName : posting.DebitAccountName,
                        Posting = posting
                    };
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                .GroupBy(item => new { item.Code, item.Name })
                .OrderBy(group => NormalizeAccountCode(group.Key.Code), StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                sheet.Cell(row, 1).Value = group.Key.Code;
                sheet.Cell(row, 2).Value = group.Key.Name;
                sheet.Cell(row, 3).Value = group.Count();
                sheet.Cell(row, 4).Value = group.Sum(item => item.Posting.Amount);
                sheet.Cell(row, 5).Value = group.Sum(item => item.Posting.AmountCurrency);
                row++;
            }

            sheet.Range(2, 4, Math.Max(row, 2), 5).Style.NumberFormat.Format = "#,##0.00";
            return row;
        }

        private static void WriteHeader(IXLWorksheet sheet, int row, params string[] headers)
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

        private static (int AmountColumn, int CurrencyAmountColumn) GetReportAmountColumns(AccountTurnoverReportKind kind)
        {
            return kind switch
            {
                AccountTurnoverReportKind.Full => (7, 8),
                AccountTurnoverReportKind.Brief => (7, 8),
                AccountTurnoverReportKind.ByArticle => (3, 4),
                AccountTurnoverReportKind.BySubaccount => (4, 5),
                _ => (2, 3)
            };
        }
        private static IEnumerable<AccountTurnoverAccountItem> CreateAccountItems(PostingViewModel posting)
        {
            if (!string.IsNullOrWhiteSpace(posting.DebitAccount))
                yield return new AccountTurnoverAccountItem(posting.DebitAccount, posting.DebitAccountName);

            if (!string.IsNullOrWhiteSpace(posting.CreditAccount))
                yield return new AccountTurnoverAccountItem(posting.CreditAccount, posting.CreditAccountName);
        }

        private AccountTurnoverSide GetSelectedSide()
        {
            return (SideComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Debit" => AccountTurnoverSide.Debit,
                "Credit" => AccountTurnoverSide.Credit,
                _ => AccountTurnoverSide.Both
            };
        }

        private AccountTurnoverReportKind GetSelectedReportKind()
        {
            return (ReportKindComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
            {
                "Brief" => AccountTurnoverReportKind.Brief,
                "ByArticle" => AccountTurnoverReportKind.ByArticle,
                "BySubaccount" => AccountTurnoverReportKind.BySubaccount,
                _ => AccountTurnoverReportKind.Full
            };
        }

        private void SelectSide(AccountTurnoverSide side)
        {
            var targetTag = side switch
            {
                AccountTurnoverSide.Debit => "Debit",
                AccountTurnoverSide.Credit => "Credit",
                _ => "Both"
            };

            foreach (var item in SideComboBox.Items.OfType<ComboBoxItem>())
            {
                item.IsSelected = Equals(item.Tag, targetTag);
            }
        }

        private static string GetSideTitle(AccountTurnoverSide side) => side switch { AccountTurnoverSide.Debit => "Дебет", AccountTurnoverSide.Credit => "Кредит", _ => "Дебет+Кредит" };

        private static string GetReportKindTitle(AccountTurnoverReportKind kind) => kind switch
        {
            AccountTurnoverReportKind.Brief => "Краткая",
            AccountTurnoverReportKind.ByArticle => "Итоги по статьям",
            AccountTurnoverReportKind.BySubaccount => "Итоги по субсчетам",
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
                safeName = "Обороты";

            return safeName.Length > 31 ? safeName[..31] : safeName;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private sealed record AccountTurnoverAccountFilter(string Code, bool UsePrefix, string DisplayName)
        {
            public bool IsEmpty => string.IsNullOrWhiteSpace(Code);
        }
        private enum AccountTurnoverSide
        {
            Both,
            Debit,
            Credit
        }

        private enum AccountTurnoverReportKind
        {
            Full,
            Brief,
            ByArticle,
            BySubaccount
        }

        private sealed record AccountTurnoverAccountItem(string Code, string Name)
        {
            public string DisplayName => string.IsNullOrWhiteSpace(Name)
                ? Code
                : $"{Code} - {Name}";
        }
    }
}

