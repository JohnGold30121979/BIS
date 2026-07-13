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
            ModuleText.Text = posting.ModuleName;
            DebitText.Text = posting.DebitAccount;
            DebitNameText.Text = posting.DebitAccountName;
            CreditText.Text = posting.CreditAccount;
            CreditNameText.Text = posting.CreditAccountName;
            CorrespondentText.Text = posting.CorrespondentAccount;
            DirectionText.Text = posting.Direction;
            AmountText.Text = posting.Amount.ToString("N2");
            AmountCurrencyText.Text = posting.AmountCurrency.ToString("N2");
            CurrencyText.Text = posting.Currency;
            OrganizationText.Text = posting.Organization;
            EmployeeText.Text = posting.Employee;
            CreatedAtText.Text = posting.CreatedAt.HasValue ? posting.CreatedAt.Value.ToString("dd.MM.yyyy HH:mm") : "";
            StatusText.Text = posting.IsActive ? "Активна" : "Отключена";
            NoteText.Text = posting.Note;

            Title = $"Детали проводки №{posting.DocumentNumber}";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
