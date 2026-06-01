using BIS.ERP.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class CreateMetadataObjectDialog : Window
    {
        public string ObjectName => NameBox.Text;
        public string ObjectType => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Catalog";
        public string ParentType { get; private set; } = "Catalog";

        public CreateMetadataObjectDialog()
        {
            InitializeComponent();

            // Загружаем возможных родителей
            var parents = new List<ParentInfo>
            {
                new() { Id = "Catalog", Name = "Справочники" },
                new() { Id = "Document", Name = "Документы" },
                new() { Id = "Report", Name = "Отчеты" }
            };

            ParentCombo.ItemsSource = parents;
            ParentCombo.SelectedIndex = 0;
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ObjectName))
            {
                MessageBox.Show("Введите имя объекта", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selected = ParentCombo.SelectedItem as ParentInfo;
            if (selected != null)
            {
                ParentType = selected.Id;
            }

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    // Вспомогательный класс для выбора родителя
    public class ParentInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}