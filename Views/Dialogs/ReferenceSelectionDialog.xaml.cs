using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BIS.ERP.Views
{
    public partial class ReferenceSelectionDialog : Window
    {
        private readonly List<Dictionary<string, object>> _items;
        private readonly string _firstField;
        private readonly string _secondField;
        private List<Dictionary<string, object>> _filteredItems;

        public Dictionary<string, object> SelectedItem { get; private set; }

        public ReferenceSelectionDialog(
            List<Dictionary<string, object>> items,
            string firstField = null,
            string secondField = null)
        {
            InitializeComponent();
            _items = items;
            _firstField = firstField;
            _secondField = secondField;
            _filteredItems = items;

            // Динамически создаем колонки
            if (items != null && items.Count > 0)
            {
                // Определяем все возможные ключи (поля)
                var allKeys = items.SelectMany(d => d.Keys).Distinct().ToList();
                // Исключаем служебные поля
                var excludeKeys = new[] { "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "IsDeleted" };
                var displayKeys = allKeys.Where(k => !excludeKeys.Contains(k) && k != "Id").ToList();

                // Если заданы поля для отображения, используем их в первую очередь
                if (!string.IsNullOrEmpty(firstField) && displayKeys.Contains(firstField))
                {
                    // Помещаем firstField на первое место
                    displayKeys.Remove(firstField);
                    displayKeys.Insert(0, firstField);
                }
                if (!string.IsNullOrEmpty(secondField) && displayKeys.Contains(secondField))
                {
                    // Помещаем secondField на второе место, если оно не firstField
                    if (displayKeys.Contains(secondField) && secondField != firstField)
                    {
                        displayKeys.Remove(secondField);
                        if (displayKeys.Count > 0 && displayKeys[0] == firstField)
                            displayKeys.Insert(1, secondField);
                        else
                            displayKeys.Insert(0, secondField);
                    }
                }

                // Добавляем колонки в DataGrid
                ItemsGrid.AutoGenerateColumns = false;
                ItemsGrid.Columns.Clear();

                foreach (var key in displayKeys)
                {
                    var column = new DataGridTextColumn
                    {
                        Header = key,
                        Binding = new Binding($"[{key}]") // Индексатор словаря
                    };
                    ItemsGrid.Columns.Add(column);
                }

                // Если нет колонок, добавляем колонку "Id"
                if (ItemsGrid.Columns.Count == 0)
                {
                    var column = new DataGridTextColumn
                    {
                        Header = "Id",
                        Binding = new Binding("[Id]")
                    };
                    ItemsGrid.Columns.Add(column);
                }
            }
            else
            {
                ItemsGrid.AutoGenerateColumns = true;
            }

            ItemsGrid.ItemsSource = _filteredItems;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(search))
            {
                _filteredItems = _items;
            }
            else
            {
                _filteredItems = _items
                    .Where(item => item.Values.Any(v => v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                    .ToList();
            }
            ItemsGrid.ItemsSource = _filteredItems;
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var button = FindName("SelectButton") as Button;
            if (button != null)
                button.IsEnabled = ItemsGrid.SelectedItem != null;
        }

        private void ItemsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectItem();
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            SelectItem();
        }

        private void SelectItem()
        {
            SelectedItem = ItemsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedItem != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}