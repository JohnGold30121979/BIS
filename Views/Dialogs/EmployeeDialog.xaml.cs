using BIS.ERP.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EmployeeDialog : Window
    {
        public Employee Employee { get; private set; }
        public bool IsEditMode { get; private set; }

        public EmployeeDialog(Employee employee = null)
        {
            InitializeComponent();

            if (employee != null)
            {
                IsEditMode = true;
                Title = $"✏️ Редактирование: {employee.FullName}";
                LoadEmployee(employee);
            }
            else
            {
                IsEditMode = false;
                Title = "➕ Добавление сотрудника";
                Employee = new Employee();
            }
        }

        private void LoadEmployee(Employee employee)
        {
            Employee = employee;
            txtPersonnelNumber.Text = employee.PersonnelNumber;
            txtFullName.Text = employee.FullName;
            txtPosition.Text = employee.Position;
            txtDepartment.Text = employee.Department;
            dpHireDate.SelectedDate = employee.HireDate;
            dpTerminationDate.SelectedDate = employee.TerminationDate;

            // Устанавливаем статус
            for (int i = 0; i < cmbStatus.Items.Count; i++)
            {
                if ((cmbStatus.Items[i] as ComboBoxItem)?.Content?.ToString() == employee.Status)
                {
                    cmbStatus.SelectedIndex = i;
                    break;
                }
            }

            txtPhone.Text = employee.Phone;
            txtEmail.Text = employee.Email;
            txtTaxId.Text = employee.TaxId;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Employee.PersonnelNumber = txtPersonnelNumber.Text;
                Employee.FullName = txtFullName.Text;
                Employee.Position = txtPosition.Text;
                Employee.Department = txtDepartment.Text;
                Employee.HireDate = dpHireDate.SelectedDate;
                Employee.TerminationDate = dpTerminationDate.SelectedDate;
                Employee.Status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Активен";
                Employee.Phone = txtPhone.Text;
                Employee.Email = txtEmail.Text;
                Employee.TaxId = txtTaxId.Text;
                Employee.IsActive = Employee.Status == "Активен";
                Employee.UpdatedAt = DateTime.UtcNow;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}