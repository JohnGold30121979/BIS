using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Окно предпросмотра печатной формы.
    ///
    /// PDF показывается прямо в окне через встроенный просмотрщик WebView2:
    /// внешняя программа для просмотра не запускается. Печать вызывается
    /// кнопкой и открывает системный диалог печати.
    ///
    /// Если просмотрщик недоступен, окно предлагает открыть документ
    /// во внешней программе — тогда печатает она.
    /// </summary>
    public partial class PdfPreviewWindow : Window
    {
        private readonly byte[] _pdfData;
        private readonly string _documentName;
        private string _tempFilePath = string.Empty;
        private bool _viewerReady;

        public PdfPreviewWindow(byte[] pdfData, string? documentName = null)
        {
            InitializeComponent();
            _pdfData = pdfData ?? Array.Empty<byte>();
            _documentName = string.IsNullOrWhiteSpace(documentName)
                ? "Документ"
                : documentName!.Trim();

            Title = $"Предпросмотр — {_documentName}";
            TitleText.Text = _documentName;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pdfData.Length == 0)
                    throw new InvalidOperationException("Документ не сформирован: данных нет.");

                EnsureTempPdfFile();
                await InitializeViewerAsync();
            }
            catch (Exception ex)
            {
                ShowFallback($"Не удалось показать документ в окне: {ex.Message}");
            }
        }

        /// <summary>
        /// Готовит встроенный просмотрщик и открывает в нём файл документа.
        /// </summary>
        private async System.Threading.Tasks.Task InitializeViewerAsync()
        {
            StatusText.Text = "Подготовка просмотра…";

            // Данные приложения не нужны: документ лежит во временном файле.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BIS.ERP",
                "WebView");

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await PdfView.EnsureCoreWebView2Async(environment);

            var settings = PdfView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;

            // Разрешаем чтение только временного файла документа.
            PdfView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "bis-preview",
                Path.GetDirectoryName(_tempFilePath)!,
                CoreWebView2HostResourceAccessKind.DenyCors);

            _viewerReady = true;
            PdfView.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;

            PdfView.CoreWebView2.Navigate(
                $"https://bis-preview/{Uri.EscapeDataString(Path.GetFileName(_tempFilePath))}");

            StatusText.Text = "Документ открыт. Кнопка «Печать» откроет диалог печати.";
        }

        /// <summary>
        /// Показывает запасной вариант: документ можно сохранить
        /// или открыть во внешней программе.
        /// </summary>
        private void ShowFallback(string message)
        {
            _viewerReady = false;
            PdfView.Visibility = Visibility.Collapsed;
            PlaceholderPanel.Visibility = Visibility.Visible;
            FallbackOpenButton.Visibility = Visibility.Visible;
            PreviewStatusText.Text = message;
            StatusText.Text = "Просмотр в окне недоступен.";
        }

        private void OnOpenClick(object sender, RoutedEventArgs e)
        {
            OpenPdfExternally(showError: true);
        }

        private void OpenPdfExternally(bool showError)
        {
            try
            {
                EnsureTempPdfFile();

                Process.Start(new ProcessStartInfo
                {
                    FileName = _tempFilePath,
                    UseShellExecute = true
                });
                StatusText.Text = "Документ открыт во внешней программе.";
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    MessageBox.Show(
                        $"Не удалось открыть документ: {ex.Message}",
                        "Предпросмотр",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Печать: документ печатает встроенный просмотрщик,
        /// поэтому диалог печати открывается прямо из окна.
        /// </summary>
        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (_viewerReady && PdfView.CoreWebView2 != null)
            {
                try
                {
                    // Открывает системный диалог печати поверх окна.
                    PdfView.CoreWebView2.ShowPrintUI();
                    StatusText.Text = "Открыт диалог печати.";
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Не удалось открыть диалог печати: {ex.Message}\n\n" +
                        "Документ будет открыт во внешней программе — печать доступна из неё.",
                        "Печать",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // Запасной путь: печатает программа, назначенная для PDF.
            try
            {
                EnsureTempPdfFile();
                Process.Start(new ProcessStartInfo
                {
                    FileName = _tempFilePath,
                    UseShellExecute = true,
                    Verb = "print"
                });
                StatusText.Text = "Документ отправлен на печать.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Сохранение документа на диск.</summary>
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var safeName = string.Join("_", _documentName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Документ";

            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить PDF",
                Filter = "PDF файлы (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllBytes(saveDialog.FileName, _pdfData);
                StatusText.Text = $"Документ сохранён: {saveDialog.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void EnsureTempPdfFile()
        {
            if (string.IsNullOrWhiteSpace(_tempFilePath))
            {
                var previewDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BIS.ERP",
                    "Preview");
                Directory.CreateDirectory(previewDirectory);
                _tempFilePath = Path.Combine(previewDirectory, $"BIS_{Guid.NewGuid():N}.pdf");
            }

            if (!File.Exists(_tempFilePath))
                File.WriteAllBytes(_tempFilePath, _pdfData);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Просмотрщик держит файл открытым: сначала отключаем его от документа,
            // иначе удаление файла не удастся.
            try
            {
                if (PdfView?.CoreWebView2 != null)
                {
                    PdfView.CoreWebView2.Navigate("about:blank");
                    PdfView.Dispose();
                }
            }
            catch
            {
                // Просмотрщик мог не запуститься — это не мешает закрытию окна.
            }

            base.OnClosed(e);

            TryDeleteTempFile();
        }

        private void TryDeleteTempFile()
        {
            if (string.IsNullOrEmpty(_tempFilePath))
                return;

            // Файл может оставаться занятым: тогда его заберёт следующая уборка.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(_tempFilePath))
                        return;

                    File.Delete(_tempFilePath);
                    return;
                }
                catch
                {
                    System.Threading.Thread.Sleep(120);
                }
            }
        }
    }
}
