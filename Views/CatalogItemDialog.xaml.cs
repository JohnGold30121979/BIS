using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;

namespace BIS.ERP.Views
{
    public partial class CatalogItemDialog : Window
    {
        private readonly MetadataObject _catalog;
        private readonly Dictionary<string, object> _itemData;
        private readonly Dictionary<string, Control> _controls;

        public Dictionary<string, object> ItemData => _itemData;

        public CatalogItemDialog(MetadataObject catalog, Dictionary<string, object> existingData = null)
        {
            InitializeComponent();
            _catalog = catalog;
            _itemData = new Dictionary<string, object>();
            _controls = new Dictionary<string, Control>();

            DialogTitle.Text = $"Добавление в справочник: {catalog.Name}";

            BuildFields(existingData);
        }

        private void BuildFields(Dictionary<string, object> existingData)
        {
            foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

                var label = new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                panel.Children.Add(label);

                Control inputControl;

                switch (field.FieldType)
                {
                    case "Int":
                        var intBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                        if (existingData != null && existingData.ContainsKey(field.Name))
                            intBox.Text = existingData[field.Name]?.ToString();
                        inputControl = intBox;
                        break;

                    case "Decimal":
                        var decimalBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                        if (existingData != null && existingData.ContainsKey(field.Name))
                            decimalBox.Text = existingData[field.Name]?.ToString();
                        inputControl = decimalBox;
                        break;

                    case "DateTime":
                        var datePicker = new DatePicker { Height = 35 };
                        if (existingData != null && existingData.ContainsKey(field.Name))
                            datePicker.SelectedDate = existingData[field.Name] as DateTime?;
                        inputControl = datePicker;
                        break;

                    case "Bool":
                        var checkBox = new CheckBox { Content = "Да", Margin = new Thickness(0, 5, 0, 0) };
                        if (existingData != null && existingData.ContainsKey(field.Name))
                            checkBox.IsChecked = (bool?)existingData[field.Name];
                        inputControl = checkBox;
                        break;

                    default: // String
                        var textBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                        if (existingData != null && existingData.ContainsKey(field.Name))
                            textBox.Text = existingData[field.Name]?.ToString();
                        inputControl = textBox;
                        break;
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _controls[field.Name] = inputControl;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    var control = _controls[field.Name];
                    object value = null;

                    switch (field.FieldType)
                    {
                        case "Int":
                            if (control is TextBox tbInt && int.TryParse(tbInt.Text, out int intVal))
                                value = intVal;
                            break;

                        case "Decimal":
                            if (control is TextBox tbDec && decimal.TryParse(tbDec.Text, out decimal decVal))
                                value = decVal;
                            break;

                        case "DateTime":
                            if (control is DatePicker dp)
                                value = dp.SelectedDate;
                            break;

                        case "Bool":
                            if (control is CheckBox cb)
                                value = cb.IsChecked ?? false;
                            break;

                        default:
                            if (control is TextBox tb)
                                value = tb.Text;
                            break;
                    }

                    _itemData[field.Name] = value ?? DBNull.Value;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}