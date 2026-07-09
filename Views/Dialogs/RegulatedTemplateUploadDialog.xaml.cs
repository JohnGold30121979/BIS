using BIS.ERP.Models;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class RegulatedTemplateUploadDialog : Window
    {
        public RegulatedTemplateUploadDialog(string filePath, RegulatedReportTemplateDraft draft)
        {
            InitializeComponent();
            FilePathTextBox.Text = filePath;
            CodeTextBox.Text = draft.Code;
            NameTextBox.Text = draft.Name;
            VersionTextBox.Text = draft.Version;
            DescriptionTextBox.Text = draft.Description;
            IsActiveCheckBox.IsChecked = draft.IsActive;
        }

        public RegulatedReportTemplateDraft Draft { get; private set; } = new();

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var code = CodeTextBox.Text.Trim();
            var name = NameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Укажите код шаблона.", "Шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
                CodeTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Укажите название шаблона.", "Шаблон", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return;
            }

            Draft = new RegulatedReportTemplateDraft
            {
                Code = code,
                Name = name,
                Version = VersionTextBox.Text.Trim(),
                Description = DescriptionTextBox.Text.Trim(),
                IsActive = IsActiveCheckBox.IsChecked == true
            };

            DialogResult = true;
        }
    }
}
