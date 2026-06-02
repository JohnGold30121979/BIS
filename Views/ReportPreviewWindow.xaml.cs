using BIS.ERP.Models;
using BIS.ERP.Services;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class ReportPreviewWindow : Window
    {
        private readonly DataTable _data;
        private readonly Report _report;
        private readonly ReportService _reportService;

        public ReportPreviewWindow(DataTable data, Report report)
        {
            System.Diagnostics.Debug.WriteLine("ReportPreviewWindow constructor START");

            InitializeComponent();
            _data = data;
            _report = report;

            System.Diagnostics.Debug.WriteLine($"Data rows: {data?.Rows.Count}, Report: {report?.Name}");

            try
            {
                var context = ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync().Result;
                _reportService = new ReportService(context);
                System.Diagnostics.Debug.WriteLine("ReportService created");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating service: {ex.Message}");
            }

            this.Loaded += OnLoaded;
            System.Diagnostics.Debug.WriteLine("ReportPreviewWindow constructor END");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("ReportPreviewWindow OnLoaded START");

            try
            {
                if (_data == null)
                {
                    System.Diagnostics.Debug.WriteLine("Data is null");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Setting ItemsSource, rows: {_data.Rows.Count}");
                PreviewGrid.ItemsSource = _data.DefaultView;
                Title = $"Предпросмотр: {_report.Name}";
                System.Diagnostics.Debug.WriteLine("ItemsSource set");

                // Настройка колонок
                foreach (var column in PreviewGrid.Columns)
                {
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                System.Diagnostics.Debug.WriteLine("Columns configured");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnLoaded: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Ошибка отображения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            System.Diagnostics.Debug.WriteLine("ReportPreviewWindow OnLoaded END");
        }

        private void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить Excel файл",
                Filter = "Excel файлы (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = $"{_report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var data = _reportService.ExportToExcel(_data, _report);
                    File.WriteAllBytes(saveDialog.FileName, data);

                    MessageBox.Show($"Отчет сохранен: {saveDialog.FileName}", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnExportHtmlClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить HTML файл",
                Filter = "HTML файлы (*.html)|*.html",
                DefaultExt = "html",
                FileName = $"{_report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var html = _reportService.ExportToHtml(_data, _report);
                    File.WriteAllText(saveDialog.FileName, html);

                    MessageBox.Show($"Отчет сохранен: {saveDialog.FileName}", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        // Печать отчета
        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    // Создаем FixedDocument для печати
                    var fixedDoc = new System.Windows.Documents.FixedDocument();
                    var pageContent = new System.Windows.Documents.PageContent();
                    var fixedPage = new System.Windows.Documents.FixedPage();

                    // Клонируем содержимое для печати
                    var container = ReportContainer;
                    fixedPage.Children.Add(container);

                    pageContent.Child = fixedPage;
                    fixedDoc.Pages.Add(pageContent);

                    printDialog.PrintDocument(fixedDoc.DocumentPaginator, _report.Name);
                }
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
    }
}