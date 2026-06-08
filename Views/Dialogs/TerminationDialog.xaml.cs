using BIS.ERP.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    public partial class TerminationDialog : Window
    {
        public DateTime TerminationDate { get; private set; }
        public string Reason { get; private set; }

        public TerminationDialog(Employee employee)
        {
            InitializeComponent();
            txtEmployeeName.Text = employee.FullName;
            dpTerminationDate.SelectedDate = DateTime.Now; // Устанавливаем дату в коде
        }

        private void OnTerminateClick(object sender, RoutedEventArgs e)
        {
            TerminationDate = dpTerminationDate.SelectedDate ?? DateTime.Now;
            Reason = (cmbReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Уволен";

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