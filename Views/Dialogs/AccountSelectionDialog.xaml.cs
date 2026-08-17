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
        private DataTable _visibleAccountsTable = new();

        public AccountSelectionDialog(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts;

            // Преобразуем в DataTable для лучшего отображения
            _accountsTable = ConvertToDataTable(accounts);
            _visibleAccountsTable = _accountsTable;
            AccountsGrid.ItemsSource = _visibleAccountsTable.DefaultView;
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
                    GetAccountRowValue(row, "Код", "code", "Code", "Счет", "schet", "account_code", "AccountCode"),
                    GetAccountRowValue(row, "Наименование", "name", "Name"),
                    FormatAccountType(GetAccountRowValue(row, "Тип счета", "account_type", "AccountType")),
                    FormatActiveValue(GetAccountRowValue(row, "Активен", "is_active", "IsActive")),
                    GetAccountRowValue(row, "Id")
                );
            }

            return dataTable;
        }

        private static string GetAccountRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString() ?? string.Empty;
            }

            return string.Empty;
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
            var searchText = SearchBox.Text?.Trim() ?? string.Empty;

            _visibleAccountsTable = string.IsNullOrWhiteSpace(searchText)
                ? _accountsTable
                : BuildFilteredAccountsTable(searchText);

            AccountsGrid.ItemsSource = _visibleAccountsTable.DefaultView;

            if (_visibleAccountsTable.DefaultView.Count > 0)
                AccountsGrid.SelectedIndex = 0;
        }

        private DataTable BuildFilteredAccountsTable(string searchText)
        {
            var result = _accountsTable.Clone();

            foreach (DataRow row in _accountsTable.Rows)
            {
                if (MatchesAccountSearch(row, searchText))
                    result.ImportRow(row);
            }

            return result;
        }

        private static bool MatchesAccountSearch(DataRow row, string searchText)
        {
            var code = row["Код"]?.ToString() ?? string.Empty;
            var name = row["Наименование"]?.ToString() ?? string.Empty;
            var type = row["Тип счета"]?.ToString() ?? string.Empty;
            var digitSearch = ExtractDigits(searchText);

            if (!string.IsNullOrEmpty(digitSearch))
                return ExtractDigits(code).StartsWith(digitSearch, StringComparison.Ordinal);

            return name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   type.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   code.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static bool ContainsDigitsInOrder(string valueDigits, string searchDigits)
        {
            if (string.IsNullOrEmpty(searchDigits))
                return true;

            var searchIndex = 0;
            foreach (var digit in valueDigits)
            {
                if (digit != searchDigits[searchIndex])
                    continue;

                searchIndex++;
                if (searchIndex == searchDigits.Length)
                    return true;
            }

            return false;
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
