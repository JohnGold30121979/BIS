using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views.Dialogs
{
    public partial class DynamicDocumentItemDialog : Window
    {
        private readonly MetadataObject _metadata;
        private readonly Guid? _editId;
        private readonly Dictionary<string, System.Windows.Controls.Control> _fieldControls = new();
        private readonly MetadataService _metadataService;

        public Dictionary<string, object> ItemData { get; private set; } = new();

        public DynamicDocumentItemDialog(MetadataObject metadata, MetadataService metadataService, Guid? editId = null)
        {
            InitializeComponent();
            _metadata = metadata;
            _metadataService = metadataService;
            _editId = editId;
            Title = $"{(editId.HasValue ? "Редактирование" : "Добавление")}: {metadata.Name}";

            Loaded += async (s, e) => await BuildFormAsync();
        }

        private async Task BuildFormAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

            foreach (var field in _metadata.Fields.OrderBy(f => f.Order))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

                panel.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                System.Windows.Controls.Control inputControl;
                var safeName = GetSafeControlName(field.Name);

                // Если поле ссылается на справочник
                if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                {
                    var comboBox = new ComboBox
                    {
                        Height = 30,
                        Name = safeName,
                        DisplayMemberPath = "DisplayName",
                        SelectedValuePath = "Id",
                        MinWidth = 200
                    };

                    // ... остальной код для ComboBox
                    inputControl = comboBox;
                }
                // Вычисляемое поле
                else if (!string.IsNullOrEmpty(field.Formula))
                {
                    inputControl = new TextBox
                    {
                        Height = 30,
                        Name = safeName,
                        IsReadOnly = true,
                        Background = Brushes.LightGray
                    };
                }
                // Обычное поле
                else
                {
                    inputControl = field.FieldType switch
                    {
                        "String" => new TextBox { Height = 30, Name = safeName },
                        "Int" => new TextBox { Height = 30, Name = safeName },
                        "Decimal" => new TextBox { Height = 30, Name = safeName },
                        "DateTime" => new DatePicker { Height = 30, Name = safeName },
                        "Bool" => new CheckBox { Content = "Да", Name = safeName, VerticalAlignment = VerticalAlignment.Center },
                        _ => new TextBox { Height = 30, Name = safeName }
                    };
                }

                if (_metadata.ObjectType == "Document" &&
                    MetadataService.IsDocumentNumberFieldName(field.Name) &&
                    inputControl is TextBox numberTextBox &&
                    !_editId.HasValue)
                {
                    numberTextBox.IsReadOnly = true;
                    numberTextBox.Background = Brushes.LightGray;

                    try
                    {
                        numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata.Name);
                    }
                    catch
                    {
                        numberTextBox.Text = MetadataService.GenerateFallbackDocumentNumber();
                    }
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
            }
        }

        private string GetSafeControlName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "control";
            // Заменяем все не-буквенно-цифровые символы на подчёркивания
            return new string(fieldName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        // Маленький класс для отображения
        public class ReferenceItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
      

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var field in _metadata.Fields)
                {
                    var control = _fieldControls[field.Name];
                    object value;

                    if (control is ComboBox comboBox)
                    {
                        // Сохраняем SelectedValue (GUID)
                        value = comboBox.SelectedValue?.ToString() ?? "";
                        System.Diagnostics.Debug.WriteLine($"Поле {field.Name}: сохранен GUID = {value}");
                    }
                    else
                    {
                        value = field.FieldType switch
                        {
                            "String" => (control as TextBox)?.Text ?? "",
                            "Int" => int.TryParse((control as TextBox)?.Text, out var intVal) ? intVal : 0,
                            "Decimal" => decimal.TryParse((control as TextBox)?.Text, out var decVal) ? decVal : 0,
                            "DateTime" => (control as DatePicker)?.SelectedDate ?? DateTime.Now,
                            "Bool" => (control as CheckBox)?.IsChecked ?? false,
                            _ => (control as TextBox)?.Text ?? ""
                        };

                        if (_metadata.ObjectType == "Document" &&
                            MetadataService.IsDocumentNumberFieldName(field.Name))
                        {
                            var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(value?.ToString());
                            if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(c => !char.IsDigit(c)))
                            {
                                MessageBox.Show("Номер документа должен содержать только цифры.", "Проверка",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            value = documentNumber;
                        }
                    }

                    ItemData[field.Name] = value;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
