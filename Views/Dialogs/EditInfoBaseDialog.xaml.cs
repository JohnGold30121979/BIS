using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EditInfoBaseDialog : Window
    {
        public string InfoBaseName => NameBox.Text.Trim();

        public EditInfoBaseDialog(string currentName)
        {
            InitializeComponent();
            NameBox.Text = currentName;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InfoBaseName))
            {
                ErrorText.Text = "Введите наименование информационной базы.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
