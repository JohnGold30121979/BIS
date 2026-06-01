using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BIS.ERP.Models;

namespace BIS.ERP.Views
{
    public partial class InfoBaseSelectionDialog : Window
    {
        public InfoBase SelectedInfoBase { get; private set; }

        public InfoBaseSelectionDialog(List<InfoBase> infoBases)
        {
            InitializeComponent();
            InfoBasesList.ItemsSource = infoBases;

            // Если есть активная база, выделяем её
            var activeBase = infoBases.FirstOrDefault(b => b.IsActive);
            if (activeBase != null)
            {
                InfoBasesList.SelectedItem = activeBase;
            }
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = InfoBasesList.SelectedItem != null;
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            SelectedInfoBase = InfoBasesList.SelectedItem as InfoBase;
            if (SelectedInfoBase != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();

            var dialog = new CreateInfoBaseDialog();
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}