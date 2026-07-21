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

    public static void Attach(ComboBox comboBox, IEnumerable items)
    {
        if (comboBox.GetValue(SearchHandlerProperty) is TextChangedEventHandler previousHandler)
            comboBox.RemoveHandler(TextBox.TextChangedEvent, previousHandler);

        var sourceItems = items.Cast<object>().ToList();
        var view = CollectionViewSource.GetDefaultView(sourceItems);

        comboBox.IsEditable = true;
        comboBox.IsTextSearchEnabled = false;
        comboBox.StaysOpenOnEdit = true;
        comboBox.ItemsSource = view;

        view.Filter = item => Matches(item, comboBox.Text);

        TextChangedEventHandler handler = (_, _) =>
        {
            view.Refresh();
            if (comboBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(comboBox.Text))
                comboBox.IsDropDownOpen = comboBox.Items.Count > 0;
        };

        comboBox.SetValue(SearchHandlerProperty, handler);
        comboBox.AddHandler(TextBox.TextChangedEvent, handler);
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
}


