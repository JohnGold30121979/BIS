using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using BIS.ERP.Views.Controls;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Выбор счёта из плана счетов.
    /// UserControl (аналогично CatalogDataView): размещается в MDI как документ
    /// через MdiDialogService.ShowControlInWorkspaceForResultAsync, при отсутствии
    /// MDI — в отдельном модальном окне.
    /// </summary>
    public partial class AccountSelectionView : UserControl
    {
        public Dictionary<string, object> SelectedAccount { get; private set; }
        private List<Dictionary<string, object>> _accounts;

        public AccountSelectionView(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts ?? new List<Dictionary<string, object>>();

            // Используем контрол для поиска и отображения
            AccountsView.SetData(_accounts);
            EditButton.IsEnabled = false;
            AccountsView.SelectionChanged += AccountsView_SelectionChanged;
            AccountsView.RowDoubleClick += AccountsView_RowDoubleClick;
        }

        private void AccountsView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            EditButton.IsEnabled = AccountsView.GetSelectedRow() != null;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var newAccount = new Dictionary<string, object>
            {
                ["Id"] = Guid.NewGuid().ToString(),
                ["Код"] = string.Empty,
                ["Наименование"] = string.Empty,
                ["Тип счета"] = string.Empty,
                ["Активен"] = true
            };

            OpenAccountEditor(newAccount, isNew: true, edited =>
            {
                _accounts.Add(edited);
                ReloadAccounts(_accounts);
                SelectRowById(edited.GetValueOrDefault("Id")?.ToString());
            });
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var drv = AccountsView.GetSelectedRow();
            if (drv == null)
                return;

            var selectedId = drv["Id"]?.ToString();
            var original = _accounts.FirstOrDefault(a => string.Equals(a.GetValueOrDefault("Id")?.ToString(), selectedId, StringComparison.OrdinalIgnoreCase));
            if (original == null)
                return;

            var copy = new Dictionary<string, object>(original);
            OpenAccountEditor(copy, isNew: false, edited =>
            {
                var idx = _accounts.FindIndex(a => string.Equals(a.GetValueOrDefault("Id")?.ToString(), selectedId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    _accounts[idx] = edited;

                ReloadAccounts(_accounts);
                SelectRowById(edited.GetValueOrDefault("Id")?.ToString());
            });
        }

        private void OpenAccountEditor(
            Dictionary<string, object> account,
            bool isNew,
            Action<Dictionary<string, object>> applyResult)
        {
            var control = new ChartOfAccountEditorControl(account, isNew);
            control.Saved += applyResult;

            var owner = Window.GetWindow(this);
            var key = $"{typeof(ChartOfAccountEditorControl).FullName}:{Guid.NewGuid():N}";
            if (!MdiDialogService.TryOpenDocumentInWorkspace(owner, key, "Редактор счёта", control))
            {
                // fallback — открыть в отдельном окне
                var win = new Window
                {
                    Title = "Редактор счёта",
                    Content = control,
                    Owner = owner ?? Application.Current?.MainWindow,
                    Width = 420,
                    Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                control.Saved += _ => win.Close();
                control.Cancelled += () => win.Close();

                win.Show();
            }
        }

        private void ReloadAccounts(List<Dictionary<string, object>> accounts)
        {
            var prevSelectedId = AccountsView.GetSelectedId();
            _accounts = accounts ?? new List<Dictionary<string, object>>();
            AccountsView.SetData(_accounts);
            if (!string.IsNullOrEmpty(prevSelectedId))
                AccountsView.SelectRowById(prevSelectedId);
            else if (_accounts.Count > 0)
                AccountsView.SelectRowById(_accounts[0].GetValueOrDefault("Id")?.ToString());
        }

        private void SelectRowById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            AccountsView.SelectRowById(id);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            MdiDialogService.CloseWithResult(this, false);
        }

        private void AccountsView_RowDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void SelectCurrentAccount()
        {
            var drv = AccountsView.GetSelectedRow();
            if (drv == null)
                return;

            var id = drv["Id"]?.ToString();
            var acc = _accounts.FirstOrDefault(a => string.Equals(a.GetValueOrDefault("Id")?.ToString(), id, StringComparison.OrdinalIgnoreCase));
            if (acc == null)
                return;

            SelectedAccount = acc;
            MdiDialogService.CloseWithResult(this, true);
        }
    }
}
