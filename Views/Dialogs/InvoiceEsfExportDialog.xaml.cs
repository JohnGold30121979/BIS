using BIS.ERP.Models;
using System;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class InvoiceEsfExportDialog : Window
    {
        private readonly InvoiceListRow? _selectedInvoice;

        public InvoiceEsfExportDialog(InvoiceListRow? selectedInvoice)
        {
            InitializeComponent();
            _selectedInvoice = selectedInvoice;

            var currentMonthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            StartDatePicker.SelectedDate = currentMonthStart;
            EndDatePicker.SelectedDate = currentMonthStart.AddMonths(1).AddDays(-1);

            if (_selectedInvoice == null)
            {
                SelectedInvoiceRadio.IsEnabled = false;
                SelectedInvoiceText.Text = "Сейчас в списке ничего не выбрано. Доступен только пакетный режим.";
                PeriodRadio.IsChecked = true;
            }
            else
            {
                SelectedInvoiceText.Text =
                    $"{_selectedInvoice.DocDate:dd.MM.yyyy} / {_selectedInvoice.DocNumber} / {_selectedInvoice.OrganizationName}";
                SelectedInvoiceRadio.IsChecked = true;
            }

            UpdateModeState();
        }

        public InvoiceEsfExportMode Mode =>
            SelectedInvoiceRadio.IsChecked == true
                ? InvoiceEsfExportMode.SelectedInvoice
                : InvoiceEsfExportMode.Period;

        public DateTime StartDate => StartDatePicker.SelectedDate ?? DateTime.Today;
        public DateTime EndDate => EndDatePicker.SelectedDate ?? DateTime.Today;
        public bool OnlyNotExported => OnlyNotExportedCheckBox.IsChecked == true;

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateModeState();
        }

        private void UpdateModeState()
        {
            var isPeriodMode = PeriodRadio.IsChecked == true;
            StartDatePicker.IsEnabled = isPeriodMode;
            EndDatePicker.IsEnabled = isPeriodMode;
            OnlyNotExportedCheckBox.IsEnabled = isPeriodMode;
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (Mode == InvoiceEsfExportMode.SelectedInvoice && _selectedInvoice == null)
            {
                MessageBox.Show("Выберите счет-фактуру в списке или переключитесь на пакетную выгрузку.",
                    "Выгрузка ЭСФ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (Mode == InvoiceEsfExportMode.Period && StartDate.Date > EndDate.Date)
            {
                MessageBox.Show("Дата начала периода не может быть позже даты окончания.",
                    "Выгрузка ЭСФ", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    public enum InvoiceEsfExportMode
    {
        SelectedInvoice,
        Period
    }
}
