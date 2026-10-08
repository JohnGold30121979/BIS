using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    /// <summary>
    /// Выбор элемента справочника для параметра типа «Ссылка».
    /// Возвращает GUID выбранного элемента — именно его подставляет отчёт в фильтр.
    /// </summary>
    public partial class ReferencePickerDialog : Window
    {
        private readonly List<ReferenceLookupItem> _allItems;

        public ReferencePickerDialog(
            string title,
            string catalogName,
            IEnumerable<ReferenceLookupItem> items)
        {
            InitializeComponent();

            _allItems = items?.ToList() ?? new List<ReferenceLookupItem>();

            Title = $"Выбор: {title}";
            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Выбор значения" : title;
            HintText.Text = $"Справочник: {catalogName}. " +
                            "Можно ввести часть названия или GUID для поиска.";

            ApplyFilter(string.Empty);
        }

        /// <summary>Выбранный GUID в виде строки (для подстановки в фильтр).</summary>
        public string SelectedId { get; private set; } = string.Empty;

        /// <summary>Отображаемое название выбранного элемента.</summary>
        public string SelectedDisplay { get; private set; } = string.Empty;

        /// <summary>Значение для сохранения в параметре: "Название|GUID".</summary>
        public string SelectedStoredValue { get; private set; } = string.Empty;

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e) =>
            ApplyFilter(SearchBox.Text);

        private void ApplyFilter(string? query)
        {
            var text = (query ?? string.Empty).Trim();

            var filtered = string.IsNullOrWhiteSpace(text)
                ? _allItems
                : _allItems
                    .Where(item => item.Display.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                   item.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                                   item.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            ItemsList.ItemsSource = filtered;

            if (filtered.Count > 0)
                ItemsList.SelectedIndex = 0;
        }

        private void OnItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
            CommitSelection();

        private void OnSelectClick(object sender, RoutedEventArgs e) => CommitSelection();

        private void CommitSelection()
        {
            if (ItemsList.SelectedItem is not ReferenceLookupItem selected)
            {
                MessageBox.Show(this, "Выберите значение из списка.", "Выбор значения",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedId = selected.Id;
            SelectedDisplay = selected.Display;
            SelectedStoredValue = selected.ToStoredValue();
            DialogResult = true;
        }
    }
}
