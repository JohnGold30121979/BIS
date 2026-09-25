using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public static class AccountPickerControlFactory
    {
        public static UserControl Create(
            AccountAnalyticsRegistry accountAnalytics,
            object? currentValue,
            Window owner,
            Action? selectionChanged = null,
            string? moduleCodeOrName = null,
            bool allowManualInput = false)
        {
            var selectedAccount = accountAnalytics.FindAccount(currentValue);
            var textBox = new TextBox
            {
                Height = 30,
                IsReadOnly = !allowManualInput,
                Background = allowManualInput ? Brushes.White : Brushes.LightGray,
                MaxLength = allowManualInput ? 8 : 0,
                ToolTip = allowManualInput ? "Только цифры, до 8 знаков" : null,
                Text = GetAccountText(selectedAccount, currentValue, allowManualInput)
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

            if (allowManualInput)
                AttachManualInputHandlers(textBox, picker, selectionChanged);

            button.Click += async (_, _) =>
            {
                var selection = new AccountSelectionView(
                    BuildAccountRows(accountAnalytics.GetAccountsForModule(moduleCodeOrName)));

                if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(owner, "Выбор счета", selection) != true || selection.SelectedAccount == null)
                    return;

                var selected = accountAnalytics.FindAccount(selection.SelectedAccount.GetValueOrDefault("Id"));
                if (selected == null)
                    return;

                textBox.Text = GetAccountText(selected, null, allowManualInput);
                picker.Tag = selected;
                selectionChanged?.Invoke();
            };

            return picker;
        }

        public static AccountReferenceItem? GetSelectedAccount(Control control)
        {
            return control is UserControl { Tag: AccountReferenceItem account } ? account : null;
        }

        public static string GetAccountCode(Control control)
        {
            return control is UserControl picker
                ? FindDescendant<TextBox>(picker)?.Text.Trim() ?? string.Empty
                : string.Empty;
        }

        public static void SetSelectedAccount(Control control, AccountReferenceItem? account)
        {
            if (control is not UserControl picker)
                return;

            var textBox = FindDescendant<TextBox>(picker);
            if (textBox != null)
                textBox.Text = account == null
                    ? string.Empty
                    : textBox.IsReadOnly
                        ? account.DisplayName
                        : account.Code;

            picker.Tag = account;
        }

        public static object GetSelectedAccountValue(MetadataField field, Control control)
        {
            var account = GetSelectedAccount(control);
            return account == null ? string.Empty : AccountAnalyticsRules.GetAccountValueForField(field, account);
        }

        private static string GetAccountText(
            AccountReferenceItem? account,
            object? fallbackValue,
            bool allowManualInput)
        {
            if (account == null)
                return fallbackValue?.ToString() ?? string.Empty;

            return allowManualInput ? account.Code : account.DisplayName;
        }

        private static void AttachManualInputHandlers(
            TextBox textBox,
            UserControl picker,
            Action? selectionChanged)
        {
            textBox.PreviewTextInput += (_, args) =>
                args.Handled = !IsAsciiDigits(args.Text);

            textBox.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Space)
                    args.Handled = true;
            };

            DataObject.AddPastingHandler(textBox, (_, args) =>
            {
                if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    args.CancelCommand();
                    return;
                }

                var text = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
                if (!IsAsciiDigits(text))
                    args.CancelCommand();
            });

            textBox.TextChanged += (_, _) =>
            {
                picker.Tag = null;
                selectionChanged?.Invoke();
            };
        }

        private static bool IsAsciiDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                if (character is < '0' or > '9')
                    return false;
            }

            return true;
        }

        private static List<Dictionary<string, object>> BuildAccountRows(IEnumerable<AccountReferenceItem> accounts)
        {
            return accounts.Select(account => new Dictionary<string, object>
            {
                ["Id"] = account.Id,
                ["Код"] = account.Code,
                ["Наименование"] = GetAccountName(account),
                ["Тип счета"] = account.AccountType,
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

        private static T? FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                var nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
