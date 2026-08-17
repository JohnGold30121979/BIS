using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BIS.ERP.Services;

public static class ReferenceComboBoxSearchHelper
{
    private static readonly DependencyProperty SearchHandlerProperty =
        DependencyProperty.RegisterAttached(
            "SearchHandler",
            typeof(TextChangedEventHandler),
            typeof(ReferenceComboBoxSearchHelper),
            new PropertyMetadata(null));

    private static readonly DependencyProperty DropDownOpenedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "DropDownOpenedHandler",
            typeof(EventHandler),
            typeof(ReferenceComboBoxSearchHelper),
            new PropertyMetadata(null));

    private static readonly DependencyProperty SelectionChangedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "SelectionChangedHandler",
            typeof(SelectionChangedEventHandler),
            typeof(ReferenceComboBoxSearchHelper),
            new PropertyMetadata(null));

    public static void Attach(ComboBox comboBox, IEnumerable items)
    {
        if (comboBox.GetValue(SearchHandlerProperty) is TextChangedEventHandler previousHandler)
            comboBox.RemoveHandler(TextBox.TextChangedEvent, previousHandler);
        if (comboBox.GetValue(DropDownOpenedHandlerProperty) is EventHandler previousDropDownHandler)
            comboBox.DropDownOpened -= previousDropDownHandler;
        if (comboBox.GetValue(SelectionChangedHandlerProperty) is SelectionChangedEventHandler previousSelectionHandler)
            comboBox.SelectionChanged -= previousSelectionHandler;

        var sourceItems = items.Cast<object>().ToList();
        var view = CollectionViewSource.GetDefaultView(sourceItems);
        var filterByText = false;

        comboBox.IsEditable = true;
        comboBox.IsTextSearchEnabled = false;
        comboBox.StaysOpenOnEdit = true;
        comboBox.ItemsSource = view;

        view.Filter = item => !filterByText || Matches(item, comboBox.Text);

        TextChangedEventHandler handler = (_, _) =>
        {
            var selectedText = GetDisplayText(comboBox, comboBox.SelectedItem);
            filterByText = comboBox.IsKeyboardFocusWithin &&
                           !string.IsNullOrWhiteSpace(comboBox.Text) &&
                           !comboBox.Text.Trim().Equals(selectedText.Trim(), StringComparison.OrdinalIgnoreCase);

            view.Refresh();
            if (filterByText)
                comboBox.IsDropDownOpen = comboBox.Items.Count > 0;
        };

        EventHandler dropDownOpenedHandler = (_, _) =>
        {
            var selectedText = GetDisplayText(comboBox, comboBox.SelectedItem);
            if (!string.IsNullOrWhiteSpace(selectedText) &&
                comboBox.Text.Trim().Equals(selectedText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                filterByText = false;
                view.Refresh();
            }
        };

        SelectionChangedEventHandler selectionHandler = (_, _) =>
        {
            filterByText = false;
            comboBox.Dispatcher.BeginInvoke((Action)view.Refresh);
        };

        comboBox.SetValue(SearchHandlerProperty, handler);
        comboBox.SetValue(DropDownOpenedHandlerProperty, dropDownOpenedHandler);
        comboBox.SetValue(SelectionChangedHandlerProperty, selectionHandler);
        comboBox.AddHandler(TextBox.TextChangedEvent, handler);
        comboBox.DropDownOpened += dropDownOpenedHandler;
        comboBox.SelectionChanged += selectionHandler;
    }

    private static bool Matches(object? item, string? searchText)
    {
        if (item == null || string.IsNullOrWhiteSpace(searchText))
            return true;

        var search = searchText.Trim();
        return GetSearchValues(item).Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetSearchValues(object item)
    {
        yield return item.ToString() ?? string.Empty;

        foreach (var propertyName in new[] { "DisplayName", "Name", "Code", "Value", "Id" })
        {
            var value = GetPropertyValue(item, propertyName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        if (GetPropertyValue(item, "LookupKeys") is IEnumerable lookupKeys)
        {
            foreach (var key in lookupKeys)
            {
                var value = key?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    private static object? GetPropertyValue(object item, string propertyName) =>
        item.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(item);

    private static string GetDisplayText(ComboBox comboBox, object? item)
    {
        if (item == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(comboBox.DisplayMemberPath))
            return GetPropertyValue(item, comboBox.DisplayMemberPath)?.ToString() ?? string.Empty;

        return item.ToString() ?? string.Empty;
    }
}
