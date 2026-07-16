using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EditInfoBaseDialog : Window
    {
        public string InfoBaseName => NameBox.Text.Trim();
        public string InfoBaseIcon => string.IsNullOrWhiteSpace(IconBox.Text)
            ? InfoBase.DefaultIcon
            : IconBox.Text.Trim();

        public EditInfoBaseDialog(string currentName, string? currentIcon)
        {
            InitializeComponent();
            NameBox.Text = currentName;
            IconBox.Text = string.IsNullOrWhiteSpace(currentIcon) ? InfoBase.DefaultIcon : currentIcon.Trim();
            SelectIconComboItem(IconBox.Text);
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void OnIconSelected(object sender, SelectionChangedEventArgs e)
        {
            if (IconBox == null)
                return;

            var selectedIcon = (IconCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedIcon))
                IconBox.Text = selectedIcon;
        }

        private void SelectIconComboItem(string icon)
        {
            for (var index = 0; index < IconCombo.Items.Count; index++)
            {
                if (IconCombo.Items[index] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), icon, System.StringComparison.Ordinal))
                {
                    IconCombo.SelectedIndex = index;
                    return;
                }
            }

            IconCombo.SelectedIndex = -1;
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
