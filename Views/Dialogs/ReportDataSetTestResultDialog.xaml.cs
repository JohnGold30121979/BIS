using System.Data;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class ReportDataSetTestResultDialog : Window
    {
        public ReportDataSetTestResultDialog(string dataSetName, DataTable table, int limit)
        {
            InitializeComponent();

            TitleText.Text = string.IsNullOrWhiteSpace(dataSetName)
                ? "Результат выполнения запроса"
                : $"Результат выполнения запроса: {dataSetName}";

            ColumnCountText.Text = table.Columns.Count.ToString();
            RowCountText.Text = table.Rows.Count.ToString();
            LimitText.Text = limit.ToString();
            StatusText.Text = table.Rows.Count == 0
                ? "Запрос выполнен успешно, строк нет."
                : $"Показаны строки тестового выполнения запроса.";

            ResultGrid.ItemsSource = table.DefaultView;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
