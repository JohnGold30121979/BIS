using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Views
{
    public partial class AccountingReportsView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly BalanceService _balanceService;
        private readonly ReportService _reportService;
        private readonly AccountingPeriodService _periodService;
        private readonly PostingService _postingService;
        private DataTable? _currentData;
        private Report? _currentReport;
        private AccountingPeriod? _currentPeriod;

        public AccountingReportsView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _balanceService = new BalanceService(context);
            _reportService = new ReportService(context);
            _periodService = new AccountingPeriodService(context);
            _postingService = new PostingService(context);
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            EndDatePicker.SelectedDate = DateTime.Today;
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Формирование отчета...";
                var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                var end = EndDatePicker.SelectedDate ?? DateTime.Today;
                if (end < start)
                    throw new Exception("Дата окончания периода не может быть раньше даты начала");

                await _periodService.EnsureSchemaAsync();

                (_currentData, _currentReport) = reportType switch
                {
                    "GeneralLedger" => await BuildGeneralLedgerAsync(start.Year),
                    "FinancialPosition" => await BuildFinancialPositionAsync(end),
                    "FinancialResults" => await BuildFinancialResultsAsync(start, end),
                    "PurchaseSalesJournal" => await BuildPurchaseSalesJournalAsync(start, end),
                    "PeriodCollection" => await BuildPeriodCollectionAsync(start, end),
                    "CashBook" => await BuildCashBookAsync(start, end),
                    _ => await BuildTrialBalanceAsync(start, end)
                };

                ReportGrid.ItemsSource = _currentData.DefaultView;
                SearchBox.Clear();
                ExportPdfButton.IsEnabled = _currentData.Columns.Count > 0;
                await UpdatePeriodStateAsync(start, end);
                StatusText.Text = $"Сформировано строк: {_currentData.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка формирования";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnCollectPeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.CollectAsync(start, end);
                await UpdatePeriodStateAsync(start, end);
                StatusText.Text = "Информация за период собрана и контрольные остатки сохранены";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Сбор периода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnTogglePeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.FindAsync(start, end);
                if (_currentPeriod == null)
                    throw new InvalidOperationException("Сначала выполните сбор информации за период.");

                if (_currentPeriod.IsLocked)
                {
                    if (MessageBox.Show("Открыть период для изменения документов?", "Учетный период",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    await _periodService.ReopenAsync(_currentPeriod.Id);
                }
                else
                {
                    if (MessageBox.Show("Закрыть период? Документы с датами этого периода нельзя будет изменять.",
                            "Учетный период", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    await _periodService.CloseAsync(_currentPeriod.Id);
                }
                await UpdatePeriodStateAsync(start, end);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Учетный период", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task UpdatePeriodStateAsync(DateTime start, DateTime end)
        {
            _currentPeriod = await _periodService.FindAsync(start, end);
            PeriodStatusText.Text = _currentPeriod == null
                ? "Период не собран"
                : $"Статус: {LocalizationService.DisplayValue(_currentPeriod.Status)}";
            PeriodStateButton.Content = _currentPeriod?.IsLocked == true ? "Открыть период" : "Закрыть период";
        }

        private (DateTime Start, DateTime End) GetPeriod()
        {
            var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            var end = EndDatePicker.SelectedDate ?? DateTime.Today;
            if (end < start)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала");
            return (start, end);
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentData == null)
                return;
            var search = SearchBox.Text.Trim().Replace("'", "''").Replace("[", "[[]");
            _currentData.DefaultView.RowFilter = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : string.Join(" OR ", _currentData.Columns.Cast<DataColumn>()
                    .Where(column => column.DataType == typeof(string))
                    .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '%{search}%'"));
        }

        private void OnReportGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ReportGrid.SelectedItem is not DataRowView row || !row.Row.Table.Columns.Contains("Счет"))
                return;
            var code = row["Счет"]?.ToString();
            if (string.IsNullOrWhiteSpace(code) || code == "ИТОГО")
                return;
            var name = row.Row.Table.Columns.Contains("Наименование") ? row["Наименование"]?.ToString() ?? code : code;
            var (start, end) = GetPeriod();
            new TurnoversDialog(code, name, start, end, _postingService) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private async Task<(DataTable, Report)> BuildTrialBalanceAsync(DateTime start, DateTime end)
        {
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Оборотно-сальдовая ведомость");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сальдо нач. Дт", typeof(decimal));
            table.Columns.Add("Сальдо нач. Кт", typeof(decimal));
            foreach (var name in new[] { "Сальдо нач. Дт", "Сальдо нач. Кт", "Оборот Дт", "Оборот Кт", "Сальдо кон. Дт", "Сальдо кон. Кт" })
                table.Columns.Add(name, typeof(decimal));

            foreach (var item in balances)
                table.Rows.Add(item.AccountCode, item.AccountName, item.OpeningDebit, item.OpeningCredit,
                    item.TurnoverDebit, item.TurnoverCredit, item.ClosingDebit, item.ClosingCredit);

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Оборотно-сальдовая ведомость за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true));
        }

        private async Task<(DataTable, Report)> BuildCashBookAsync(DateTime start, DateTime end)
        {
            var receiptType = "Приходный кассовый ордер";
            var paymentType = "Расходный кассовый ордер";
            var allPostings = await _postingService.GetAllPostingsAsync(null, end);
            var cashPostings = allPostings
                .Where(posting => posting.DocumentType == receiptType || posting.DocumentType == paymentType)
                .ToList();
            var openingRows = cashPostings.Where(posting => posting.Date < start).ToList();
            var balance = openingRows.Sum(posting => posting.DocumentType == receiptType ? posting.Amount : -posting.Amount);
            var rows = cashPostings.Where(posting => posting.Date >= start && posting.Date <= end.Date.AddDays(1).AddTicks(-1))
                .OrderBy(posting => posting.Date).ThenBy(posting => posting.DocumentNumber).ToList();

            var table = new DataTable("Кассовая книга");
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Содержание", typeof(string));
            table.Columns.Add("Приход", typeof(decimal));
            table.Columns.Add("Расход", typeof(decimal));
            table.Columns.Add("Остаток", typeof(decimal));
            table.Rows.Add(start.Date, string.Empty, "Остаток на начало", string.Empty, 0m, 0m, balance);

            decimal totalReceipt = 0;
            decimal totalPayment = 0;
            foreach (var posting in rows)
            {
                var receipt = posting.DocumentType == receiptType ? posting.Amount : 0m;
                var payment = posting.DocumentType == paymentType ? posting.Amount : 0m;
                totalReceipt += receipt;
                totalPayment += payment;
                balance += receipt - payment;
                table.Rows.Add(posting.Date, posting.DocumentNumber, posting.DocumentType, posting.Note,
                    receipt, payment, balance);
            }
            table.Rows.Add(end.Date, string.Empty, "ИТОГО ЗА ПЕРИОД", string.Empty,
                totalReceipt, totalPayment, balance);
            return (table, CreateReport(table, $"Кассовая книга за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true));
        }

        private async Task<(DataTable, Report)> BuildGeneralLedgerAsync(int year)
        {
            var ledger = await _balanceService.GetGeneralLedgerAsync(year);
            var table = new DataTable("Главная книга");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            for (var month = 1; month <= 12; month++)
            {
                table.Columns.Add($"{month:00} Дт", typeof(decimal));
                table.Columns.Add($"{month:00} Кт", typeof(decimal));
            }
            table.Columns.Add("Год Дт", typeof(decimal));
            table.Columns.Add("Год Кт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Дт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Кт", typeof(decimal));

            foreach (var item in ledger)
            {
                var values = new object[32];
                values[0] = item.AccountCode;
                values[1] = item.AccountName;
                values[2] = item.OpeningDebit;
                values[3] = item.OpeningCredit;
                for (var month = 1; month <= 12; month++)
                {
                    values[2 + month * 2] = item.MonthlyTurnoverDebit[month];
                    values[3 + month * 2] = item.MonthlyTurnoverCredit[month];
                }
                values[28] = item.YearTurnoverDebit;
                values[29] = item.YearTurnoverCredit;
                values[30] = item.ClosingDebit;
                values[31] = item.ClosingCredit;
                table.Rows.Add(values);
            }

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Главная книга за {year} год", true));
        }

        private async Task<(DataTable, Report)> BuildFinancialPositionAsync(DateTime date)
        {
            var balance = await _balanceService.GetEnterpriseBalanceAsync(date);
            var table = new DataTable("Баланс предприятия");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in balance.Assets)
                table.Rows.Add("Активы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Активы", string.Empty, "Итого активы", balance.TotalAssets);
            foreach (var item in balance.Liabilities)
                table.Rows.Add("Обязательства", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Обязательства", string.Empty, "Итого обязательства", balance.TotalLiabilities);
            foreach (var item in balance.Equity)
                table.Rows.Add("Капитал", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Капитал", string.Empty, "Итого капитал", balance.TotalEquity);
            table.Rows.Add("Контроль", string.Empty, "Расхождение баланса", balance.Difference);

            var report = CreateReport(table, $"Баланс предприятия на {date:dd.MM.yyyy}", false);
            report.SummaryText = balance.IsBalanced
                ? "Контроль пройден: активы равны обязательствам и капиталу."
                : $"Контроль не пройден. Расхождение: {balance.Difference:N2}";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildFinancialResultsAsync(DateTime start, DateTime end)
        {
            var results = await _balanceService.GetFinancialResultsAsync(start, end);
            var table = new DataTable("Финансовые результаты");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in results.Income)
                table.Rows.Add("Доходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Доходы", string.Empty, "Итого доходы", results.TotalIncome);
            foreach (var item in results.Expenses)
                table.Rows.Add("Расходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Расходы", string.Empty, "Итого расходы", results.TotalExpenses);
            table.Rows.Add("Результат", string.Empty,
                results.ProfitOrLoss >= 0 ? "Прибыль" : "Убыток", results.ProfitOrLoss);

            return (table, CreateReport(table,
                $"Финансовые результаты за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", false));
        }

        private async Task<(DataTable, Report)> BuildPurchaseSalesJournalAsync(DateTime start, DateTime end)
        {
            var entries = await _balanceService.GetPurchaseSalesJournalAsync(start, end);
            var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
            var utcEnd = DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc);
            var savedEntries = await _context.TaxJournalRecords.AsNoTracking()
                .Where(record => record.Date >= utcStart && record.Date < utcEnd)
                .OrderBy(record => record.Date).ThenBy(record => record.DocumentNumber).ToListAsync();
            var table = new DataTable("Журнал закупок и продаж");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Ставка налога", typeof(string));
            table.Columns.Add("Сумма без налога", typeof(decimal));
            table.Columns.Add("Налог", typeof(decimal));
            table.Columns.Add("Всего", typeof(decimal));
            table.Columns.Add("Проведен", typeof(string));
            table.Columns.Add("Примечание", typeof(string));

            if (savedEntries.Count > 0)
            {
                foreach (var entry in savedEntries)
                    table.Rows.Add(LocalizationService.DisplayValue(entry.JournalType), entry.Date,
                        entry.DocumentNumber, entry.DocumentType, entry.Organization, entry.TaxType,
                        entry.AmountWithoutTax, entry.TaxAmount, entry.TotalAmount, LocalizationService.DisplayValue(true), string.Empty);
            }
            else
            {
                foreach (var entry in entries)
                    table.Rows.Add(entry.Section, entry.Date, entry.DocumentNumber, entry.DocumentType,
                        entry.Organization, entry.TaxType, entry.AmountWithoutTax, entry.TaxAmount, entry.Amount,
                        LocalizationService.DisplayValue(entry.IsPosted), entry.Note);
            }

            var report = CreateReport(table,
                $"Журнал закупок и продаж за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true);
            report.ShowGrandTotal = true;
            var amountField = report.Fields.First(field => field.FieldName == "Всего");
            amountField.AggregateType = "Sum";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildPeriodCollectionAsync(DateTime start, DateTime end)
        {
            var collection = await _balanceService.CollectPeriodInformationAsync(start, end);
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Сбор информации за период");
            table.Columns.Add("Показатель", typeof(string));
            table.Columns.Add("Всего", typeof(int));
            table.Columns.Add("Проведено", typeof(int));
            table.Columns.Add("Не проведено", typeof(int));
            table.Columns.Add("Сумма документов", typeof(decimal));
            table.Columns.Add("Дебет", typeof(decimal));
            table.Columns.Add("Кредит", typeof(decimal));
            table.Columns.Add("Разница", typeof(decimal));

            foreach (var item in collection.Documents)
                table.Rows.Add(item.DocumentType, item.DocumentCount, item.PostedCount, item.UnpostedCount, item.Amount, 0m, 0m, 0m);
            var openingDebit = balances.Sum(item => item.OpeningDebit);
            var openingCredit = balances.Sum(item => item.OpeningCredit);
            var closingDebit = balances.Sum(item => item.ClosingDebit);
            var closingCredit = balances.Sum(item => item.ClosingCredit);
            table.Rows.Add("КОНТРОЛЬ НАЧАЛЬНОГО САЛЬДО", 0, 0, 0, 0m,
                openingDebit, openingCredit, openingDebit - openingCredit);
            table.Rows.Add("БУХГАЛТЕРСКИЕ ПРОВОДКИ", collection.PostingCount,
                collection.PostingCount, 0, collection.DebitTurnover,
                collection.DebitTurnover, collection.CreditTurnover, collection.Difference);
            table.Rows.Add("КОНТРОЛЬ КОНЕЧНОГО САЛЬДО", 0, 0, 0, 0m,
                closingDebit, closingCredit, closingDebit - closingCredit);

            var report = CreateReport(table,
                $"Сбор информации за период {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", false);
            var isBalanced = collection.IsBalanced && Math.Abs(openingDebit - openingCredit) < 0.01m &&
                             Math.Abs(closingDebit - closingCredit) < 0.01m;
            report.SummaryText = isBalanced
                ? "Контроль пройден: начальное сальдо, обороты и конечное сальдо сбалансированы."
                : "Контроль не пройден: обнаружено расхождение дебета и кредита.";
            return (table, report);
        }

        private static Report CreateReport(DataTable table, string title, bool landscape)
        {
            var report = new Report
            {
                Name = title,
                TitleText = title,
                PageOrientation = landscape ? "Landscape" : "Portrait",
                FontName = "Arial",
                FontSize = table.Columns.Count > 12 ? 7 : 9,
                ShowGrandTotal = false,
                ShowGridLines = true,
                AlternateRowColors = true
            };

            var order = 1;
            foreach (DataColumn column in table.Columns)
            {
                report.Fields.Add(new ReportField
                {
                    FieldName = column.ColumnName,
                    DisplayName = column.ColumnName,
                    Order = order++,
                    Width = column.DataType == typeof(string) ? 160 : 85,
                    IsVisible = true
                });
            }
            return report;
        }

        private static void AddTotalsRow(DataTable table, int firstNumericColumn)
        {
            var row = table.NewRow();
            row[0] = "ИТОГО";
            for (var index = firstNumericColumn; index < table.Columns.Count; index++)
                row[index] = table.Rows.Cast<DataRow>().Sum(item => Convert.ToDecimal(item[index]));
            table.Rows.Add(row);
        }

        private void OnExportPdfClick(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentReport == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить бухгалтерский отчет",
                Filter = "PDF файлы (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = $"{_currentData.TableName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllBytes(dialog.FileName, _reportService.ExportToPdf(_currentData, _currentReport));
                MessageBox.Show("PDF-отчет сформирован.", "Отчет",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта PDF: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
