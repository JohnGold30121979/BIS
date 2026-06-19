using System.Collections.Generic;
using System.Windows;
using BIS.ERP.Models.Configurator;

namespace BIS.ERP.Views.Dialogs
{
    public partial class CreateDynamicObjectDialog : Window
    {
        public string ObjectName => txtName.Text;
        public string ObjectIcon => txtIcon.Text;
        public string Description => txtDescription.Text;
        public bool UsePostings => chkPostings.IsChecked ?? false;
        public bool UseBalances => chkBalances.IsChecked ?? false;
        public bool UseMovements => false;
        public List<DynamicFieldViewModel> Fields { get; set; } = new();

        public CreateDynamicObjectDialog(string objectType)
        {
            InitializeComponent();
            Title = $"Создание {(objectType == "Catalog" ? "справочника" : "документа")}";
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Введите наименование!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
