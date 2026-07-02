using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class MutualSettlementsView : UserControl
    {
        private readonly MetadataService _metadataService;
        private string _currentViewMode = "All";
        private string _filterDebitAccount;
        private string _filterCreditAccount;
        private List<MutualSettlementItem> _allData;

        public MutualSettlementsView(MetadataService metadataService)
        {
            InitializeComponent();
            _metadataService = metadataService;
            _allData = new List<MutualSettlementItem>();

            dpStartDate.SelectedDate = new DateTime(DateTime.Now.Year, 1, 1);
            dpEndDate.SelectedDate = DateTime.Now;

            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await LoadAdvancePaymentPairs();
                    await LoadData();
                }
            };
        }

        private async System.Threading.Tasks.Task LoadAdvancePaymentPairs()
        {
            try
            {
                var pairs = await _metadataService.GetAdvancePaymentPairsAsync();
                cmbAccountPair.ItemsSource = pairs;
                cmbAccountPair.DisplayMemberPath = "name";
                cmbAccountPair.SelectedValuePath = "Id";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки пар счетов: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadData()
        {
            try
            {
                var startDate = dpStartDate.SelectedDate ?? DateTime.Now.AddMonths(-1);
                var endDate = dpEndDate.SelectedDate ?? DateTime.Now;

                var rawData = await _metadataService.GetMutualSettlementsAsync(startDate, endDate);

                // Группируем по организациям и парам счетов
                var grouped = rawData
                    .GroupBy(r => new
                    {
                        org = r.GetValueOrDefault("organization_name")?.ToString() ?? "Без организации",
                        debit = r.GetValueOrDefault("debit_account")?.ToString() ?? "",
                        credit = r.GetValueOrDefault("credit_account")?.ToString() ?? ""
                    })
                    .Select(g =>
                    {
                        var debitSum = g.Sum(r =>
                        {
                            var amt = r.GetValueOrDefault("amount_kgs");
                            return Convert.ToDecimal(amt ?? 0);
                        });
                        // For simplicity, treat all amounts as debit if debit account matches filter
                        var creditSum = 0m;
                        return new MutualSettlementItem
                        {
                            organization_name = g.Key.org,
                            debit_account = g.Key.debit,
                            credit_account = g.Key.credit,
                            debit_total = debitSum,
                            credit_total = creditSum,
                            balance = debitSum - creditSum
                        };
                    })
                    .ToList();

                _allData = grouped;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilters()
        {
            var data = _allData.AsEnumerable();

            // Фильтр по паре счетов
            if (!string.IsNullOrEmpty(_filterDebitAccount) || !string.IsNullOrEmpty(_filterCreditAccount))
            {
                data = data.Where(d =>
                    (string.IsNullOrEmpty(_filterDebitAccount) || d.debit_account == _filterDebitAccount) &&
                    (string.IsNullOrEmpty(_filterCreditAccount) || d.credit_account == _filterCreditAccount));
            }

            // Режим просмотра
            switch (_currentViewMode)
            {
                case "Debtors":
                    data = data.Where(d => d.balance > 0);
                    break;
                case "Creditors":
                    data = data.Where(d => d.balance < 0);
                    break;
            }

            dgSettlements.ItemsSource = data.ToList();
        }

        private async void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private void btnViewMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _currentViewMode = btn.Tag?.ToString() ?? "All";
                ApplyFilters();
            }
        }

        private void btnExpand_Click(object sender, RoutedEventArgs e)
        {
            // Show all columns - already expanded by default
        }

        private void btnCollapse_Click(object sender, RoutedEventArgs e)
        {
            // Show only total per organization
            var collapsed = _allData
                .GroupBy(d => d.organization_name)
                .Select(g => new MutualSettlementItem
                {
                    organization_name = g.Key,
                    debit_total = g.Sum(d => d.debit_total),
                    credit_total = g.Sum(d => d.credit_total),
                    balance = g.Sum(d => d.balance)
                })
                .ToList();

            dgSettlements.ItemsSource = collapsed;
        }

        private async void btnReconciliation_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgSettlements.SelectedItem as MutualSettlementItem;
            if (selected == null)
            {
                MessageBox.Show("Выберите организацию для акта сверки", "Информация",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Find organization ID by name
            var startDate = dpStartDate.SelectedDate ?? DateTime.Now.AddMonths(-1);
            var endDate = dpEndDate.SelectedDate ?? DateTime.Now;

            // For now, show the data in a message
            var data = await _metadataService.GetReconciliationStatementAsync(
                Guid.Empty, startDate, endDate);

            var details = string.Join("\n", data.Select(d =>
                $"{d.GetValueOrDefault("Дата"):yyyy-MM-dd} | {d.GetValueOrDefault("Номер документа")} | {d.GetValueOrDefault("Дебет")} -> {d.GetValueOrDefault("Кредит")} | {d.GetValueOrDefault("Сумма"):N2}"));

            var report = $"Акт сверки: {selected.organization_name}\n" +
                        $"Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}\n" +
                        $"Сальдо: {selected.balance:N2}\n\n" +
                        details;

            MessageBox.Show(report, "Акт сверки", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnFilter_Click(object sender, RoutedEventArgs e)
        {
            grpFilter.Visibility = grpFilter.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void btnSetFilter_Click(object sender, RoutedEventArgs e)
        {
            var selected = cmbAccountPair.SelectedItem as Dictionary<string, object>;
            if (selected != null)
            {
                _filterDebitAccount = selected.GetValueOrDefault("debit_account")?.ToString();
                _filterCreditAccount = selected.GetValueOrDefault("credit_account")?.ToString();
                ApplyFilters();
            }
        }

        private void btnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            _filterDebitAccount = null;
            _filterCreditAccount = null;
            cmbAccountPair.SelectedItem = null;
            ApplyFilters();
        }

        private async void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Simple print using PrintDialog
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintVisual(dgSettlements, "Взаиморасчеты с организациями");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class MutualSettlementItem
    {
        public string organization_name { get; set; } = string.Empty;
        public string debit_account { get; set; } = string.Empty;
        public string credit_account { get; set; } = string.Empty;
        public decimal debit_total { get; set; }
        public decimal credit_total { get; set; }
        public decimal balance { get; set; }
    }
}