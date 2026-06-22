using System.Collections.Generic;
using System;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class AccountSelectionDialog : Window
    {
        public Dictionary<string, object> SelectedAccount { get; private set; }
        private List<Dictionary<string, object>> _accounts;
        private DataTable _accountsTable = new();

        public AccountSelectionDialog(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts;

            // Преобразуем в DataTable для лучшего отображения
            _accountsTable = ConvertToDataTable(accounts);
            AccountsGrid.ItemsSource = _accountsTable.DefaultView;
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
            dataTable.Columns.Add("Активен", typeof(string));
            dataTable.Columns.Add("Id", typeof(string)); // скрытая колонка

            // Заполняем строки
            foreach (var row in data)
            {
                dataTable.Rows.Add(
                    row.ContainsKey("Код") ? row["Код"].ToString() : "",
                    row.ContainsKey("Наименование") ? row["Наименование"].ToString() : "",
                    FormatAccountType(row.ContainsKey("Тип счета") ? row["Тип счета"]?.ToString() : ""),
                    FormatActiveValue(row.ContainsKey("Активен") ? row["Активен"] : null),
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var view = _accountsTable.DefaultView;
            var searchText = SearchBox.Text?.Trim().Replace("'", "''") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                view.RowFilter = string.Empty;
                return;
            }

            view.RowFilter =
                $"[Код] LIKE '%{searchText}%' OR [Наименование] LIKE '%{searchText}%' OR [Тип счета] LIKE '%{searchText}%'";

            if (view.Count > 0)
                AccountsGrid.SelectedIndex = 0;
        }

        private void SelectCurrentAccount()
        {
            var selected = AccountsGrid.SelectedItem as DataRowView;
            if (selected != null)
            {
                var selectedId = selected["Id"]?.ToString();
                var originalAccount = _accounts.FirstOrDefault(account =>
                    string.Equals(account.TryGetValue("Id", out var idValue) ? idValue?.ToString() : null,
                        selectedId, StringComparison.OrdinalIgnoreCase));

                if (originalAccount == null)
                {
                    MessageBox.Show("Не удалось определить выбранный счет. Попробуйте выбрать его снова.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                SelectedAccount = new Dictionary<string, object>(originalAccount);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Выберите счет из списка!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string FormatAccountType(string? accountType)
        {
            return LocalizationService.DisplayValue(accountType);
        }

        private static string FormatActiveValue(object? value)
        {
            return LocalizationService.DisplayValue(value);
        }
    }
}
