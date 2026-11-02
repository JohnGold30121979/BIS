using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class CashDayTurnoverDialog : Window
    {
        public CashDayTurnoverDialog(string title, string subtitle, CashDayTurnoverRow row)
        {
            InitializeComponent();
            TitleText.Text = title;
            SubtitleText.Text = subtitle;
            OpeningDebitText.Text = FormatAmount(row.OpeningDebit);
            OpeningCreditText.Text = FormatAmount(row.OpeningCredit);
            DebitTurnoverText.Text = FormatAmount(row.DebitTurnover);
            CreditTurnoverText.Text = FormatAmount(row.CreditTurnover);
            ClosingDebitText.Text = FormatAmount(row.ClosingDebit);
            ClosingCreditText.Text = FormatAmount(row.ClosingCredit);
        }

        private static string FormatAmount(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        }
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


