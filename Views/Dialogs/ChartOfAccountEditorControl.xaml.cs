using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    public partial class ChartOfAccountEditorControl : UserControl
    {
        public Dictionary<string, object> EditedAccount { get; private set; }
        private bool _isNew;

        public event Action<Dictionary<string, object>>? Saved;
        public event Action? Cancelled;

        public ChartOfAccountEditorControl(Dictionary<string, object> account, bool isNew)
        {
            InitializeComponent();
            _isNew = isNew;
            EditedAccount = new Dictionary<string, object>(account ?? new Dictionary<string, object>());

            // Инициализация полей
            CodeBox.Text = EditedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
            NameBox.Text = EditedAccount.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty;
            var type = EditedAccount.GetValueOrDefault("Тип счета")?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(type))
                TypeCombo.SelectedItem = null; // оставляем выбор по умолчанию, пользователь может выбрать
            ActiveCheck.IsChecked = EditedAccount.GetValueOrDefault("Активен") is bool b ? b : true;

            // Можно задать описание/заголовок в родителе
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CodeBox.Text))
            {
                MessageBox.Show("Код не может быть пустым", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EditedAccount["Код"] = CodeBox.Text.Trim();
            EditedAccount["Наименование"] = NameBox.Text.Trim();
            EditedAccount["Тип счета"] = (TypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            EditedAccount["Активен"] = ActiveCheck.IsChecked == true;
            if (!EditedAccount.ContainsKey("Id"))
                EditedAccount["Id"] = Guid.NewGuid().ToString();

            Saved?.Invoke(EditedAccount);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Cancelled?.Invoke();
        }
    }
}
