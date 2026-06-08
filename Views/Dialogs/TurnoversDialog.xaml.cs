using System;
using System.Data;
using System.Windows;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class TurnoversDialog : Window
    {
        private readonly DataTable _data;

        public TurnoversDialog(string accountCode, string accountName, DateTime startDate, DateTime endDate, PostingService service)
        {
            InitializeComponent();
            TitleText.Text = $"Обороты по счету {accountCode} за {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";

            _data = service.GetTurnoversByAccountAsync(accountCode, startDate, endDate).Result;
            TurnoversGrid.ItemsSource = _data.DefaultView;
        }

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            // TODO: Реализовать печать
            MessageBox.Show("Печать в разработке", "Информация");
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}