using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class AccountSelectionDialog : Window
    {
        public Dictionary<string, object> SelectedAccount { get; private set; }
        private List<Dictionary<string, object>> _accounts;

        public AccountSelectionDialog(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts;
            AccountsGrid.ItemsSource = accounts;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SelectedAccount = AccountsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedAccount != null)
            {
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Выберите счет!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AccountsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectedAccount = AccountsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedAccount != null)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}