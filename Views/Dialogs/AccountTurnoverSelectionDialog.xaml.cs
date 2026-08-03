using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AccountTurnoverSelectionDialog : Window
    {
        private readonly List<PostingViewModel> _postings;
        private readonly DateTime _startDate;
        private readonly DateTime _endDate;
        private readonly ObservableCollection<PostingViewModel> _details = new();

        public AccountTurnoverSelectionDialog(
            IEnumerable<PostingViewModel> postings,
            DateTime startDate,
            DateTime endDate,
            PostingViewModel? selectedPosting = null)
        {
            InitializeComponent();

            _postings = postings.ToList();
            _startDate = startDate.Date;
            _endDate = endDate.Date;
            DetailsGrid.ItemsSource = _details;
            PeriodText.Text = $"Период: {_startDate:dd.MM.yyyy} - {_endDate:dd.MM.yyyy}. Счета берутся из текущего журнала проводок.";

            var initialSide = ResolveInitialSide(selectedPosting);
            SelectSide(initialSide);
            ReloadAccounts(initialSide, selectedPosting);
            Recalculate();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            if (sender == SideComboBox)
                ReloadAccounts(GetSelectedSide(), null);

            Recalculate();
        }

        private void ReloadAccounts(AccountTurnoverSide side, PostingViewModel? selectedPosting)
        {
            var selectedAccount = side == AccountTurnoverSide.Debit
                ? selectedPosting?.DebitAccount
                : selectedPosting?.CreditAccount;

            var accounts = _postings
                .Select(posting => CreateAccountItem(posting, side))
                .Where(account => !string.IsNullOrWhiteSpace(account.Code))
                .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(account => !string.IsNullOrWhiteSpace(account.Name))
                    .First())
                .OrderBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AccountComboBox.ItemsSource = accounts;
            AccountComboBox.SelectedItem = accounts.FirstOrDefault(account =>
                account.Code.Equals(selectedAccount, StringComparison.OrdinalIgnoreCase));
            AccountComboBox.SelectedItem ??= accounts.FirstOrDefault();
        }

        private void Recalculate()
        {
            var account = AccountComboBox.SelectedItem as AccountTurnoverAccountItem;
            if (account == null)
            {
                CountText.Text = "0";
                AmountText.Text = "0,00";
                CurrencyAmountText.Text = "0,00";
                _details.Clear();
                return;
            }

            var side = GetSelectedSide();
            var rows = _postings
                .Where(posting => AccountMatches(posting, side, account.Code))
                .OrderBy(posting => posting.Date)
                .ThenBy(posting => posting.DocumentNumber)
                .ToList();

            _details.Clear();
            foreach (var row in rows)
                _details.Add(row);

            CountText.Text = rows.Count.ToString("N0");
            AmountText.Text = rows.Sum(row => row.Amount).ToString("N2");
            CurrencyAmountText.Text = rows.Sum(row => row.AmountCurrency).ToString("N2");
        }

        private static bool AccountMatches(PostingViewModel posting, AccountTurnoverSide side, string accountCode)
        {
            var value = side == AccountTurnoverSide.Debit
                ? posting.DebitAccount
                : posting.CreditAccount;
            return value.Equals(accountCode, StringComparison.OrdinalIgnoreCase);
        }

        private static AccountTurnoverAccountItem CreateAccountItem(PostingViewModel posting, AccountTurnoverSide side)
        {
            return side == AccountTurnoverSide.Debit
                ? new AccountTurnoverAccountItem(posting.DebitAccount, posting.DebitAccountName)
                : new AccountTurnoverAccountItem(posting.CreditAccount, posting.CreditAccountName);
        }

        private static AccountTurnoverSide ResolveInitialSide(PostingViewModel? selectedPosting)
        {
            if (selectedPosting == null)
                return AccountTurnoverSide.Debit;

            return string.IsNullOrWhiteSpace(selectedPosting.DebitAccount) &&
                   !string.IsNullOrWhiteSpace(selectedPosting.CreditAccount)
                ? AccountTurnoverSide.Credit
                : AccountTurnoverSide.Debit;
        }

        private AccountTurnoverSide GetSelectedSide()
        {
            return SideComboBox.SelectedItem is ComboBoxItem { Tag: "Credit" }
                ? AccountTurnoverSide.Credit
                : AccountTurnoverSide.Debit;
        }

        private void SelectSide(AccountTurnoverSide side)
        {
            foreach (var item in SideComboBox.Items.OfType<ComboBoxItem>())
            {
                item.IsSelected = side == AccountTurnoverSide.Credit
                    ? Equals(item.Tag, "Credit")
                    : Equals(item.Tag, "Debit");
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private enum AccountTurnoverSide
        {
            Debit,
            Credit
        }

        private sealed record AccountTurnoverAccountItem(string Code, string Name)
        {
            public string DisplayName => string.IsNullOrWhiteSpace(Name)
                ? Code
                : $"{Code} - {Name}";
        }
    }
}
