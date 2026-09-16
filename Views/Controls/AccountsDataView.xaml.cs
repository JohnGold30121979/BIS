using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace BIS.ERP.Views.Controls
{
    public partial class AccountsDataView : UserControl
    {
        private DataTable _accountsTable = new();
        private DataView _dataView = null!;

        public event SelectionChangedEventHandler? SelectionChanged;
        public event MouseButtonEventHandler? RowDoubleClick;

        public AccountsDataView()
        {
            InitializeComponent();
        }

        public void SetData(List<Dictionary<string, object>> accounts)
        {
            // Сохраняем текущий оффсет прокрутки
            var sv = FindScrollViewer(AccountsGrid);
            double? offset = null;
            if (sv != null)
                offset = sv.VerticalOffset;

            _accountsTable = ConvertToDataTable(accounts);
            _dataView = _accountsTable.DefaultView;
            AccountsGrid.ItemsSource = _dataView;

            // Восстанавливаем оффсет прокрутки асинхронно после обновления ItemsSource
            if (sv != null && offset.HasValue)
            {
                AccountsGrid.Dispatcher.BeginInvoke(new Action(() =>
                {
                    sv.ScrollToVerticalOffset(offset.Value);
                }));
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject d)
        {
            if (d == null) return null;
            if (d is ScrollViewer sv) return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        public DataRowView? GetSelectedRow()
        {
            return AccountsGrid.SelectedItem as DataRowView;
        }

        public string? GetSelectedId()
        {
            var drv = GetSelectedRow();
            return drv == null ? null : drv["Id"]?.ToString();
        }

        public void SelectRowById(string id)
        {
            if (string.IsNullOrEmpty(id) || _dataView == null) return;

            for (int i = 0; i < _dataView.Count; i++)
            {
                var row = _dataView[i];
                if (string.Equals(row["Id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase))
                {
                    AccountsGrid.SelectedIndex = i;
                    AccountsGrid.ScrollIntoView(AccountsGrid.SelectedItem);
                    break;
                }
            }
        }

        private DataTable ConvertToDataTable(List<Dictionary<string, object>> data)
        {
            if (data == null || data.Count == 0)
                return new DataTable();

            var dataTable = new DataTable();

            dataTable.Columns.Add("Код", typeof(string));
            dataTable.Columns.Add("Наименование", typeof(string));
            dataTable.Columns.Add("Тип счета", typeof(string));
            dataTable.Columns.Add("Активен", typeof(string));
            dataTable.Columns.Add("Id", typeof(string));

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

        private static string FormatAccountType(string val)
        {
            return val ?? string.Empty;
        }

        private static string FormatActiveValue(string val)
        {
            return val ?? string.Empty;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_dataView == null)
                return;

            var text = SearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                _dataView.RowFilter = string.Empty;
                return;
            }

            // Экранируем одинарные кавычки
            var esc = text.Replace("'", "''");
            // Фильтруем по коду или наименованию
            _dataView.RowFilter = $"[Код] LIKE '%{esc}%' OR [Наименование] LIKE '%{esc}%'";
        }

        private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(this, e);
        }

        private void AccountsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            RowDoubleClick?.Invoke(this, e);
        }
    }
}
