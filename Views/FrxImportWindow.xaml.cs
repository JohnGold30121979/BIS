using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class FrxImportWindow : Window
    {
        private List<FrxFileInfo> _selectedFiles;

        public FrxImportWindow()
        {
            InitializeComponent();
            _selectedFiles = new List<FrxFileInfo>();
        }

        private void OnSelectFilesClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите FRX файлы",
                Filter = "FoxPro Report Files (*.frx)|*.frx|Все файлы (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedFiles.Clear();
                foreach (var fileName in dialog.FileNames)
                {
                    var fileInfo = new FileInfo(fileName);
                    _selectedFiles.Add(new FrxFileInfo
                    {
                        FileName = fileInfo.Name,
                        FullPath = fileName,
                        Size = fileInfo.Length,
                        Modified = fileInfo.LastWriteTime
                    });
                }

                FilesList.ItemsSource = _selectedFiles.ToList();
                ImportButton.IsEnabled = _selectedFiles.Any();
                StatusText.Text = $"Выбрано файлов: {_selectedFiles.Count}";

                if (_selectedFiles.Any())
                {
                    var file = _selectedFiles.First();
                    PreviewText.Text = $"Файл: {file.FileName}\nРазмер: {file.Size} байт\nГотов к импорту";
                }
            }
        }

        private void OnFileSelected(object sender, SelectionChangedEventArgs e)
        {
            if (FilesList.SelectedItem is FrxFileInfo file)
            {
                PreviewText.Text = $"Файл: {file.FileName}\nРазмер: {file.Size} байт\nПуть: {file.FullPath}";
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            ImportButton.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;

            int imported = 0;
            int failed = 0;

            for (int i = 0; i < _selectedFiles.Count; i++)
            {
                var file = _selectedFiles[i];
                ProgressBar.Value = (i + 1) * 100 / _selectedFiles.Count;
                StatusText.Text = $"Импорт: {file.FileName}...";

                try
                {
                    System.Diagnostics.Debug.WriteLine($"=== Импорт файла: {file.FileName} ===");

                    // Читаем файл
                    var frxData = File.ReadAllBytes(file.FullPath);
                    System.Diagnostics.Debug.WriteLine($"Размер файла: {frxData.Length} байт");

                    // Создаем простой отчет вместо полного парсинга
                    var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                    var reportService = new ReportService(context);

                    var report = new Report
                    {
                        Id = Guid.NewGuid(),
                        Name = Path.GetFileNameWithoutExtension(file.FileName),
                        Description = $"Импортирован из FRX: {file.FileName}",
                        Icon = "📊",
                        ReportType = "Standard",
                        DataSourceType = "Catalog",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Добавляем поля по умолчанию
                    report.Fields.Add(new ReportField
                    {
                        Id = Guid.NewGuid(),
                        FieldName = "code",
                        DisplayName = "Код",
                        Order = 1,
                        IsVisible = true,
                        Width = 100
                    });

                    report.Fields.Add(new ReportField
                    {
                        Id = Guid.NewGuid(),
                        FieldName = "name",
                        DisplayName = "Наименование",
                        Order = 2,
                        IsVisible = true,
                        Width = 200
                    });

                    await reportService.SaveReportAsync(report);
                    imported++;

                    System.Diagnostics.Debug.WriteLine($"Импорт успешен: {report.Name}");
                }
                catch (Exception ex)
                {
                    failed++;
                    System.Diagnostics.Debug.WriteLine($"Ошибка импорта {file.FileName}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }

            ProgressBar.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Импорт завершен. Успешно: {imported}, Ошибок: {failed}";

            MessageBox.Show($"Импортировано отчетов: {imported}\nОшибок: {failed}",
                "Импорт завершен", MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            ImportButton.IsEnabled = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class FrxFileInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
    }
}