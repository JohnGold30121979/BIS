using System;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class TurnoversDialog : Window
    {
        private DataTable _data = new();

        public TurnoversDialog(string accountCode, string accountName, DateTime startDate, DateTime endDate, PostingService service)
        {
            InitializeComponent();
            TitleText.Text = $"Обороты по счету {accountCode} - {accountName} за {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}";
            Loaded += async (_, _) =>
            {
                try
                {
                    _data = await service.GetTurnoversByAccountAsync(accountCode, startDate, endDate);
                    TurnoversGrid.ItemsSource = _data.DefaultView;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка получения оборотов: {ex.Message}", "Обороты",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() == true)
                dialog.PrintVisual(TurnoversGrid, TitleText.Text);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
