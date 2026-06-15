using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class AccountSelectionDialog : Window
    {
        public Dictionary<string, object> SelectedAccount { get; private set; }
        private List<Dictionary<string, object>> _accounts;

        public AccountSelectionDialog(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts;

            // Преобразуем в DataTable для лучшего отображения
            var dataTable = ConvertToDataTable(accounts);
            AccountsGrid.ItemsSource = dataTable.DefaultView;
        }

        private DataTable ConvertToDataTable(List<Dictionary<string, object>> data)
        {
            if (data == null || data.Count == 0)
                return new DataTable();

            var dataTable = new DataTable();

            // Добавляем колонки
            dataTable.Columns.Add("Код", typeof(string));
            dataTable.Columns.Add("Наименование", typeof(string));
            dataTable.Columns.Add("Тип счета", typeof(string));
            dataTable.Columns.Add("Активен", typeof(bool));
            dataTable.Columns.Add("Id", typeof(string)); // скрытая колонка

            // Заполняем строки
            foreach (var row in data)
            {
                dataTable.Rows.Add(
                    row.ContainsKey("Код") ? row["Код"].ToString() : "",
                    row.ContainsKey("Наименование") ? row["Наименование"].ToString() : "",
                    row.ContainsKey("Тип счета") ? row["Тип счета"].ToString() : "",
                    row.ContainsKey("Активен") && row["Активен"] != null ? (bool)row["Активен"] : false,
                    row.ContainsKey("Id") ? row["Id"].ToString() : ""
                );
            }

            return dataTable;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AccountsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void SelectCurrentAccount()
        {
            var selected = AccountsGrid.SelectedItem as DataRowView;
            if (selected != null)
            {
                // Восстанавливаем Dictionary из выбранной строки
                SelectedAccount = new Dictionary<string, object>
                {
                    ["Код"] = selected["Код"].ToString(),
                    ["Наименование"] = selected["Наименование"].ToString(),
                    ["Тип счета"] = selected["Тип счета"].ToString(),
                    ["Активен"] = selected["Активен"],
                    ["Id"] = selected["Id"].ToString()
                };
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Выберите счет из списка!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}