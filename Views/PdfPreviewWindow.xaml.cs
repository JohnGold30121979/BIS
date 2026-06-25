using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BIS.ERP.Views
{
    public partial class PdfPreviewWindow : Window
    {
        private readonly byte[] _pdfData;
        private string _tempFilePath;

        public PdfPreviewWindow(byte[] pdfData)
        {
            InitializeComponent();
            _pdfData = pdfData;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Сохраняем во временный файл
                _tempFilePath = Path.Combine(Path.GetTempPath(), $"BIS_{Guid.NewGuid()}.pdf");
                File.WriteAllBytes(_tempFilePath, _pdfData);

                // Показываем PDF в WebBrowser
                PdfViewer.Navigate(new Uri(_tempFilePath));

                // Устанавливаем заголовок окна
                Title = $"Предпросмотр - {Path.GetFileName(_tempFilePath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки PDF: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// Сохранение PDF на диск
        /// </summary>
        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить PDF",
                Filter = "PDF файлы (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = $"Документ_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_tempFilePath, saveDialog.FileName, overwrite: true);
                    MessageBox.Show($"PDF сохранён:\n{saveDialog.FileName}", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Печать PDF (открывает диалог печати)
        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _tempFilePath,
                    UseShellExecute = true,
                    Verb = "print",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Удаляем временный файл
            try
            {
                if (!string.IsNullOrEmpty(_tempFilePath) && File.Exists(_tempFilePath))
                    File.Delete(_tempFilePath);
            }
            catch { }
        }
    }
}