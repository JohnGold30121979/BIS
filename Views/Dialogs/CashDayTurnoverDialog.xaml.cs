using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class CashDayTurnoverDialog : Window
    {
        public CashDayTurnoverDialog(string title, string subtitle, IEnumerable<CashDayTurnoverRow> rows)
        {
            InitializeComponent();
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            TurnoversGrid.ItemsSource = rows.OrderBy(row => row.Date).ToList();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }

    public sealed class CashDayTurnoverRow
    {
        public DateTime Date { get; set; }
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public decimal DebitTurnover { get; set; }
        public decimal CreditTurnover { get; set; }
        public decimal ClosingDebit { get; set; }
        public decimal ClosingCredit { get; set; }
    }
}
