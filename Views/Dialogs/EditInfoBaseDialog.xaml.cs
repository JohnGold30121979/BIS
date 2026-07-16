using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Dialogs
{
    public partial class EditInfoBaseDialog : Window
    {
        public string InfoBaseName => NameBox.Text.Trim();
        public string InfoBaseIcon => string.IsNullOrWhiteSpace(IconBox.Text)
            ? InfoBase.DefaultIcon
            : IconBox.Text.Trim();
        public byte[]? LogoImageBytes => logoImageBytes;
        public string? LogoContentType => logoContentType;
        public string? LogoFileName => logoFileName;

        private byte[]? logoImageBytes;
        private string? logoContentType;
        private string? logoFileName;

        public EditInfoBaseDialog(
            string currentName,
            string? currentIcon,
            byte[]? currentLogoImage,
            string? currentLogoContentType,
            string? currentLogoFileName)
        {
            InitializeComponent();
            NameBox.Text = currentName;
            IconBox.Text = string.IsNullOrWhiteSpace(currentIcon) ? InfoBase.DefaultIcon : currentIcon.Trim();
            logoImageBytes = currentLogoImage;
            logoContentType = currentLogoContentType;
            logoFileName = currentLogoFileName;
            SelectIconComboItem(IconBox.Text);
            UpdateLogoPreview();
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void OnIconSelected(object sender, SelectionChangedEventArgs e)
        {
            if (IconBox == null)
                return;

            var selectedIcon = (IconCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedIcon))
                IconBox.Text = selectedIcon;
        }

        private void SelectIconComboItem(string icon)
        {
            for (var index = 0; index < IconCombo.Items.Count; index++)
            {
                if (IconCombo.Items[index] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), icon, System.StringComparison.Ordinal))
                {
                    IconCombo.SelectedIndex = index;
                    return;
                }
            }

            IconCombo.SelectedIndex = -1;
        }

        private void OnLoadLogoClick(object sender, RoutedEventArgs e)
        {
            if (!InfoBaseLogoFileService.TryPickLogo(this, out var logo, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    ErrorText.Text = error;
                    ErrorText.Visibility = Visibility.Visible;
                }
                return;
            }

            logoImageBytes = logo!.ImageBytes;
            logoContentType = logo.ContentType;
            logoFileName = logo.FileName;
            ErrorText.Visibility = Visibility.Collapsed;
            UpdateLogoPreview();
        }

        private void OnClearLogoClick(object sender, RoutedEventArgs e)
        {
            logoImageBytes = null;
            logoContentType = null;
            logoFileName = null;
            UpdateLogoPreview();
        }

        private void UpdateLogoPreview()
        {
            var imageSource = InfoBaseLogoFileService.CreateImageSource(logoImageBytes);
            LogoPreviewImage.Source = imageSource;
            LogoPreviewImage.Visibility = imageSource == null ? Visibility.Collapsed : Visibility.Visible;
            LogoPlaceholderText.Visibility = imageSource == null ? Visibility.Visible : Visibility.Collapsed;
            LogoNameText.Text = string.IsNullOrWhiteSpace(logoFileName)
                ? "Логотип не загружен"
                : $"Загружен: {logoFileName}";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InfoBaseName))
            {
                ErrorText.Text = "Введите наименование информационной базы.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
