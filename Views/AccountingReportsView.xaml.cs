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

namespace BIS.ERP.Views
{
    public partial class AccountingReportsView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly BalanceService _balanceService;
        private readonly ReportService _reportService;
        private DataTable? _currentData;
        private Report? _currentReport;

        public AccountingReportsView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _balanceService = new BalanceService(context);
            _reportService = new ReportService(context);
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

                (_currentData, _currentReport) = reportType switch
                {
                    "GeneralLedger" => await BuildGeneralLedgerAsync(start.Year),
                    "FinancialPosition" => await BuildFinancialPositionAsync(end),
                    "FinancialResults" => await BuildFinancialResultsAsync(start, end),
                    "PurchaseSalesJournal" => await BuildPurchaseSalesJournalAsync(start, end),
                    "PeriodCollection" => await BuildPeriodCollectionAsync(start, end),
                    _ => await BuildTrialBalanceAsync(start, end)
                };

                ReportGrid.ItemsSource = _currentData.DefaultView;
                ExportPdfButton.IsEnabled = _currentData.Columns.Count > 0;
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

        private async Task<(DataTable, Report)> BuildTrialBalanceAsync(DateTime start, DateTime end)
        {
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Оборотно-сальдовая ведомость");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            foreach (var name in new[] { "Сальдо нач. Дт", "Сальдо нач. Кт", "Оборот Дт", "Оборот Кт", "Сальдо кон. Дт", "Сальдо кон. Кт" })
                table.Columns.Add(name, typeof(decimal));

            foreach (var item in balances)
                table.Rows.Add(item.AccountCode, item.AccountName, item.OpeningDebit, item.OpeningCredit,
                    item.TurnoverDebit, item.TurnoverCredit, item.ClosingDebit, item.ClosingCredit);

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Оборотно-сальдовая ведомость за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true));
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

            foreach (var item in ledger)
            {
                var values = new object[28];
                values[0] = item.AccountCode;
                values[1] = item.AccountName;
                for (var month = 1; month <= 12; month++)
                {
                    values[month * 2] = item.MonthlyTurnoverDebit[month];
                    values[month * 2 + 1] = item.MonthlyTurnoverCredit[month];
                }
                values[26] = item.YearTurnoverDebit;
                values[27] = item.YearTurnoverCredit;
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
            var table = new DataTable("Журнал закупок и продаж");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));
            table.Columns.Add("Проведен", typeof(bool));
            table.Columns.Add("Примечание", typeof(string));

            foreach (var entry in entries)
                table.Rows.Add(entry.Section, entry.Date, entry.DocumentNumber, entry.DocumentType,
                    entry.Organization, entry.Amount, entry.IsPosted, entry.Note);

            var report = CreateReport(table,
                $"Журнал закупок и продаж за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true);
            report.ShowGrandTotal = true;
            var amountField = report.Fields.First(field => field.FieldName == "Сумма");
            amountField.AggregateType = "Sum";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildPeriodCollectionAsync(DateTime start, DateTime end)
        {
            var collection = await _balanceService.CollectPeriodInformationAsync(start, end);
            var table = new DataTable("Сбор информации за период");
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Всего", typeof(int));
            table.Columns.Add("Проведено", typeof(int));
            table.Columns.Add("Не проведено", typeof(int));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in collection.Documents)
                table.Rows.Add(item.DocumentType, item.DocumentCount, item.PostedCount, item.UnpostedCount, item.Amount);
            table.Rows.Add("БУХГАЛТЕРСКИЕ ПРОВОДКИ", collection.PostingCount,
                collection.PostingCount, 0, collection.DebitTurnover);
            table.Rows.Add("КОНТРОЛЬ ДЕБЕТ/КРЕДИТ", 0, 0, 0, collection.Difference);

            var report = CreateReport(table,
                $"Сбор информации за период {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", false);
            report.SummaryText = collection.IsBalanced
                ? "Контроль пройден: обороты по дебету и кредиту равны."
                : $"Обнаружено расхождение дебета и кредита: {collection.Difference:N2}";
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
