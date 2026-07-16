using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Services
{
    public static class LogoDisplayHelper
    {
        public static void Apply(Image image, TextBlock fallbackText, byte[]? imageBytes, string? fallbackIcon)
        {
            var imageSource = LogoFileService.CreateImageSource(imageBytes);
            image.Source = imageSource;
            image.Visibility = imageSource == null ? Visibility.Collapsed : Visibility.Visible;
            fallbackText.Text = string.IsNullOrWhiteSpace(fallbackIcon) ? "🏢" : fallbackIcon.Trim();
            fallbackText.Visibility = imageSource == null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
