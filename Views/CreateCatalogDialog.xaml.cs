using BIS.ERP.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class CreateCatalogDialog : Window
    {
        public ObservableCollection<FieldInfo> Fields { get; set; }

        public string CatalogName => NameBox.Text;
        public string CatalogDescription => DescriptionBox.Text;
        public string CatalogIcon => (IconCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "📚";

        public CreateCatalogDialog()
        {
            InitializeComponent();
            Fields = new ObservableCollection<FieldInfo>();
            FieldsList.ItemsSource = Fields;

            // Добавляем стандартные поля
            Fields.Add(new FieldInfo { Name = "Код", Type = "String", IsRequired = true });
            Fields.Add(new FieldInfo { Name = "Наименование", Type = "String", IsRequired = true });
        }

        private void OnAddFieldClick(object sender, RoutedEventArgs e)
        {
            Fields.Add(new FieldInfo { Name = "Новое поле", Type = "String", IsRequired = false });
        }

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var field = button?.DataContext as FieldInfo;
            if (field != null && Fields.Contains(field))
            {
                Fields.Remove(field);
            }
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CatalogName))
            {
                MessageBox.Show("Введите наименование справочника", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnLoadFromJsonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите JSON файл со структурой справочника",
                Filter = "JSON файлы (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(dialog.FileName);

                    // Используем анонимный тип для десериализации
                    var schema = JsonSerializer.Deserialize<CatalogSchema>(json);

                    if (schema != null)
                    {
                        NameBox.Text = schema.Name;
                        DescriptionBox.Text = schema.Description ?? "";

                        // Устанавливаем иконку
                        if (!string.IsNullOrEmpty(schema.Icon))
                        {
                            for (int i = 0; i < IconCombo.Items.Count; i++)
                            {
                                var item = IconCombo.Items[i] as ComboBoxItem;
                                if (item?.Tag?.ToString() == schema.Icon)
                                {
                                    IconCombo.SelectedIndex = i;
                                    break;
                                }
                            }
                        }

                        // Заполняем поля
                        Fields.Clear();
                        foreach (var field in schema.Fields)
                        {
                            Fields.Add(new FieldInfo
                            {
                                Name = field.Name,
                                Type = field.FieldType,
                                IsRequired = field.IsRequired
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка загрузки JSON: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    // Схема JSON для импорта
    public class CatalogSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = "📚";
        public string Description { get; set; } = string.Empty;
        public List<FieldSchema> Fields { get; set; } = new();
    }

    public class FieldSchema
    {
        public string Name { get; set; } = string.Empty;
        public string FieldType { get; set; } = "String";
        public bool IsRequired { get; set; } = false;
    }
}