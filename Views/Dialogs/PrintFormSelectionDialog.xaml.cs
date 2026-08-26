using BIS.ERP.Models;
using BIS.ERP.Services;
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
        public PrintFormOutputFormat SelectedFormat { get; private set; } = PrintFormOutputFormat.Pdf;

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
            var canPrint = FormsGrid.SelectedItem is Report { IsActive: true };
            PdfButton.IsEnabled = canPrint;
            ExcelButton.IsEnabled = canPrint;
        }

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FormsGrid.SelectedItem is Report { IsActive: true })
                SelectCurrent(PrintFormOutputFormat.Pdf);
        }

        private void OnPdfClick(object sender, RoutedEventArgs e) => SelectCurrent(PrintFormOutputFormat.Pdf);

        private void OnExcelClick(object sender, RoutedEventArgs e) => SelectCurrent(PrintFormOutputFormat.Excel);

        private void SelectCurrent(PrintFormOutputFormat format)
        {
            if (FormsGrid.SelectedItem is not Report { IsActive: true } report)
                return;

            SelectedFormat = format;
            SelectedReport = report;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

