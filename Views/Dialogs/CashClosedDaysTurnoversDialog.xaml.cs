using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class CashClosedDaysTurnoversDialog : Window
    {
        public CashClosedDaysTurnoversDialog(
            string title,
            string subtitle,
            IEnumerable<CashClosedDayTurnoverRow> rows)
        {
            InitializeComponent();
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            TurnoversGrid.ItemsSource = rows.OrderBy(row => row.Date).ToList();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }

    public sealed class CashClosedDayTurnoverRow
    {
        public DateTime Date { get; set; }
        public decimal OpeningBalance { get; set; }
        public decimal DebitTurnover { get; set; }
        public decimal CreditTurnover { get; set; }
        public decimal ClosingBalance { get; set; }
    }
}
