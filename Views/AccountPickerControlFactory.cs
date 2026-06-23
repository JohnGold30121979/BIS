using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public static class AccountPickerControlFactory
    {
        public static UserControl Create(
            AccountAnalyticsRegistry accountAnalytics,
            object? currentValue,
            Window owner,
            Action? selectionChanged = null)
        {
            var selectedAccount = accountAnalytics.FindAccount(currentValue);
            var textBox = new TextBox
            {
                Height = 30,
                IsReadOnly = true,
                Background = Brushes.LightGray,
                Text = selectedAccount?.DisplayName ?? currentValue?.ToString() ?? string.Empty
            };

            var button = new Button
            {
                Content = "?",
                Width = 34,
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0)
            };

            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(textBox, 0);
            Grid.SetColumn(button, 1);
            panel.Children.Add(textBox);
            panel.Children.Add(button);

            var picker = new UserControl
            {
                Content = panel,
                Tag = selectedAccount,
                MinWidth = 200
            };

            button.Click += (_, _) =>
            {
                var dialog = new AccountSelectionDialog(BuildAccountRows(accountAnalytics.Accounts))
                {
                    Owner = owner
                };

                if (dialog.ShowDialog() != true || dialog.SelectedAccount == null)
                    return;

                var selected = accountAnalytics.FindAccount(dialog.SelectedAccount.GetValueOrDefault("Id"));
                if (selected == null)
                    return;

                picker.Tag = selected;
                textBox.Text = selected.DisplayName;
                selectionChanged?.Invoke();
            };

            return picker;
        }

        public static AccountReferenceItem? GetSelectedAccount(Control control)
        {
            return control is UserControl { Tag: AccountReferenceItem account } ? account : null;
        }

        public static object GetSelectedAccountValue(MetadataField field, Control control)
        {
            var account = GetSelectedAccount(control);
            return account == null ? string.Empty : AccountAnalyticsRules.GetAccountValueForField(field, account);
        }

        private static List<Dictionary<string, object>> BuildAccountRows(IEnumerable<AccountReferenceItem> accounts)
        {
            return accounts.Select(account => new Dictionary<string, object>
            {
                ["Id"] = account.Id,
                ["Код"] = account.Code,
                ["Наименование"] = GetAccountName(account),
                ["Тип счета"] = string.Empty,
                ["Активен"] = true
            }).ToList();
        }

        private static string GetAccountName(AccountReferenceItem account)
        {
            var prefix = $"{account.Code} - ";
            return account.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? account.DisplayName[prefix.Length..]
                : account.DisplayName;
        }
    }
}
