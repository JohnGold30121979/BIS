using BIS.ERP.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class PrintFormSelectionDialog : Window
    {
        public Report? SelectedReport { get; private set; }

        public PrintFormSelectionDialog(IEnumerable<Report> reports)
        {
            InitializeComponent();
            var items = reports.ToList();
            FormsGrid.ItemsSource = items;
            FormsGrid.SelectedItem = items.FirstOrDefault(item => item.IsDefault && item.IsActive)
                                     ?? items.FirstOrDefault(item => item.IsActive)
                                     ?? items.FirstOrDefault();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = FormsGrid.SelectedItem is Report { IsActive: true };
        }

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FormsGrid.SelectedItem is Report { IsActive: true })
                SelectCurrent();
        }

        private void OnSelectClick(object sender, RoutedEventArgs e) => SelectCurrent();

        private void SelectCurrent()
        {
            if (FormsGrid.SelectedItem is not Report { IsActive: true } report)
                return;
            SelectedReport = report;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
