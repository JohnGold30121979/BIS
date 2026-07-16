using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BIS.ERP.Services
{
    public sealed class InfoBaseLogoFile
    {
        public required byte[] ImageBytes { get; init; }
        public required string ContentType { get; init; }
        public required string FileName { get; init; }
    }

    public static class InfoBaseLogoFileService
    {
        private const int MaxLogoBytes = 2 * 1024 * 1024;

        public static bool TryPickLogo(Window owner, out InfoBaseLogoFile? logo, out string? error)
        {
            logo = null;
            error = null;

            var dialog = new OpenFileDialog
            {
                Title = "Выбор логотипа информационной базы",
                Filter = "Логотип (*.png;*.ico)|*.png;*.ico",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != true)
                return false;

            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var contentType = extension switch
            {
                ".png" => "image/png",
                ".ico" => "image/x-icon",
                _ => null
            };

            if (contentType == null)
            {
                error = "Поддерживаются только файлы PNG и ICO.";
                return false;
            }

            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length > MaxLogoBytes)
            {
                error = "Размер логотипа не должен превышать 2 МБ.";
                return false;
            }

            logo = new InfoBaseLogoFile
            {
                ImageBytes = File.ReadAllBytes(dialog.FileName),
                ContentType = contentType,
                FileName = Path.GetFileName(dialog.FileName)
            };
            return true;
        }

        public static BitmapImage? CreateImageSource(byte[]? imageBytes)
        {
            if (imageBytes is not { Length: > 0 })
                return null;

            try
            {
                using var stream = new MemoryStream(imageBytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
