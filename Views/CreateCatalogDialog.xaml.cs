using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class CreateCatalogDialog : Window
    {
        public ObservableCollection<FieldInfo> Fields { get; set; }

        public string CatalogName => NameBox.Text;
        public string CatalogDescription => DescriptionBox.Text;
        public string CatalogIcon => (IconCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "📄";

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
    }
}