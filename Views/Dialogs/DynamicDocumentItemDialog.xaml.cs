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
            // Предзагружаем все справочники для быстрого доступа
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

                // Если поле ссылается на справочник
                if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                {
                    var comboBox = new ComboBox
                    {
                        Height = 30,
                        Name = field.Name,
                        DisplayMemberPath = "DisplayName",
                        SelectedValuePath = "Id",
                        MinWidth = 200
                    };

                    System.Diagnostics.Debug.WriteLine($"=== Поле: {field.Name}, Справочник: {field.ReferenceCatalog}");

                    if (catalogsDict.TryGetValue(field.ReferenceCatalog, out var catalog))
                    {
                        System.Diagnostics.Debug.WriteLine($"Справочник найден: {catalog.Name}, TableName: {catalog.TableName}");

                        var data = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        System.Diagnostics.Debug.WriteLine($"Получено записей: {data.Count}");

                        var items = new List<ReferenceItem>();

                        foreach (var d in data)
                        {
                            var item = new ReferenceItem();

                            if (d.ContainsKey("Id"))
                                item.Id = Guid.Parse(d["Id"].ToString());

                            // ВЫВОДИМ ВСЕ КЛЮЧИ ДЛЯ ОТЛАДКИ
                            System.Diagnostics.Debug.WriteLine($"Ключи записи: {string.Join(", ", d.Keys)}");

                            // Ищем название поля с наименованием
                            if (d.ContainsKey("site_name"))
                                item.Name = d["site_name"].ToString();
                            else if (d.ContainsKey("Наименование"))
                                item.Name = d["Наименование"].ToString();
                            else if (d.ContainsKey("name"))
                                item.Name = d["name"].ToString();
                            else if (d.ContainsKey("Name"))
                                item.Name = d["Name"].ToString();
                            else if (d.ContainsKey("Код"))
                                item.Name = d["Код"].ToString();
                            else
                            {
                                // Если ничего не нашли, берем первое значение
                                var firstValue = d.Values.FirstOrDefault();
                                item.Name = firstValue?.ToString() ?? "Без имени";
                            }

                            items.Add(item);
                        }

                        comboBox.ItemsSource = items;
                        System.Diagnostics.Debug.WriteLine($"Загружено {items.Count} записей из {field.ReferenceCatalog}");
                        if (items.Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"Первая запись: Id={items[0].Id}, Name={items[0].Name}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Справочник '{field.ReferenceCatalog}' НЕ НАЙДЕН!");
                        comboBox.ItemsSource = new List<ReferenceItem> { new ReferenceItem { Name = $"Справочник '{field.ReferenceCatalog}' не найден" } };
                    }

                    inputControl = comboBox;
                }
                // Вычисляемое поле
                else if (!string.IsNullOrEmpty(field.Formula))
                {
                    var textBox = new TextBox { Height = 30, Name = field.Name, IsReadOnly = true, Background = Brushes.LightGray };
                    inputControl = textBox;
                }
                // Обычное поле
                else
                {
                    inputControl = field.FieldType switch
                    {
                        "String" => new TextBox { Height = 30, Name = field.Name },
                        "Int" => new TextBox { Height = 30, Name = field.Name },
                        "Decimal" => new TextBox { Height = 30, Name = field.Name },
                        "DateTime" => new DatePicker { Height = 30, Name = field.Name },
                        "Bool" => new CheckBox { Content = "Да", Name = field.Name, VerticalAlignment = VerticalAlignment.Center },
                        _ => new TextBox { Height = 30, Name = field.Name }
                    };
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
            }
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