using BIS.ERP.Models;
using System;
using System.Windows;

namespace BIS.ERP.Views
{
    public partial class PostingDetailsDialog : Window
    {
        public PostingDetailsDialog(PostingViewModel posting)
        {
            InitializeComponent();

            DocumentNumberText.Text = posting.DocumentNumber;
            DateText.Text = posting.Date.ToString("dd.MM.yyyy HH:mm");
            DocumentTypeText.Text = posting.DocumentType;
            DebitText.Text = posting.DebitAccount;
            CreditText.Text = posting.CreditAccount;
            AmountText.Text = posting.Amount.ToString("N2");
            AmountCurrencyText.Text = posting.AmountCurrency.ToString("N2");
            CurrencyText.Text = posting.Currency;
            OrganizationText.Text = posting.Organization;
            EmployeeText.Text = posting.Employee;
            NoteText.Text = posting.Note;

            Title = $"Детали проводки №{posting.DocumentNumber}";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}