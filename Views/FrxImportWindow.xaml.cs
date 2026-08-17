using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                Title = "Выберите макеты FoxPro",
                Filter = "FoxPro reports (*.frx;*.fpx;*.json)|*.frx;*.fpx;*.json|FRX (*.frx)|*.frx|JSON (*.json)|*.json|Все файлы (*.*)|*.*",
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
                try
                {
                    var parser = new FrxParser();
                    var parsed = parser.ParseFrxFile(file.FullPath);
                    var template = parser.GetPrintTemplate(parsed);
                    var memoPath = Path.ChangeExtension(file.FullPath, ".FRT");

                    var preview = new StringBuilder();
                    preview.AppendLine($"📄 Файл: {file.FileName}");
                    preview.AppendLine($"📦 FRT: {(File.Exists(memoPath) ? "найден" : "не найден")}");
                    preview.AppendLine($"📊 Полос: {parsed.Bands.Count}");
                    preview.AppendLine($"🏷 Поля/элементы: {parsed.Fields.Count}");
                    preview.AppendLine($"📐 Размер: {template.PageWidth:F0}x{template.PageHeight:F0}");

                    // Детальная информация по бандам
                    if (parsed.Bands.Count > 0)
                    {
                        preview.AppendLine();
                        preview.AppendLine("── Полосы отчета ──");
                        foreach (var band in parsed.Bands.OrderBy(b => b.Order))
                        {
                            var bandFields = parsed.Fields.Where(f => f.BandId == band.Id).ToList();
                            preview.AppendLine($"  [{band.BandType}] top={band.Top} h={band.Height} fields={bandFields.Count}");
                            foreach (var field in bandFields.OrderBy(f => f.Order))
                            {
                                var expr = !string.IsNullOrEmpty(field.Expression) ? $" = {Truncate(field.Expression, 40)}" : "";
                                preview.AppendLine($"    · {field.FieldName} ({field.FieldType}) [{field.Left},{field.Top} {field.Width}x{field.Height}]{expr}");
                            }
                        }
                    }

                    preview.AppendLine();
                    preview.AppendLine($"🔄 Источник: будет определен автоматически по имени формы.");

                    PreviewText.Text = preview.ToString();
                }
                catch (Exception ex)
                {
                    PreviewText.Text = $"❌ Не удалось разобрать макет: {ex.Message}\n{ex.StackTrace}";
                }
            }
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLen ? value : value[..maxLen] + "...";
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

                    var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                    var reportService = new ReportService(context);
                    var metadataService = new MetadataService(context);
                    var documents = await metadataService.GetDocumentsAsync();
                    var parser = new FrxParser();
                    var parsed = parser.ParseFrxFile(file.FullPath);
                    var template = parser.GetPrintTemplate(parsed);
                    var target = DetectTargetDocument(file.FileName, documents);
                    var code = $"foxpro.{Path.GetFileNameWithoutExtension(file.FileName).ToLowerInvariant()}";
                    var report = (await reportService.GetReportsAsync()).FirstOrDefault(item => item.Code == code) ?? new Report();
                    report.Code = code;
                    report.Name = target == null
                        ? Path.GetFileNameWithoutExtension(file.FileName)
                        : $"{target.Name} ({Path.GetFileNameWithoutExtension(file.FileName)})";
                    report.Description = $"Импортирован из Visual FoxPro: {file.FileName}";
                    report.Icon = "🖨";
                    report.ReportType = "FoxProLayout";
                    report.DataSourceType = "Document";
                    report.DataSourceId = target?.Id;
                    report.IsPrintForm = true;
                    report.IsActive = target != null;
                    report.IsDefault = false;
                    report.SourceFormat = "FoxProFRX";
                    report.TemplateVersion = 1;
                    report.Template = parsed.FrxXml;

                    // ИЗВЛЕКАЕМ ПОЛЯ ИЗ FRX-ШАБЛОНА, чтобы они не пропадали
                    var extractedFields = PrintFormService.ExtractReportFieldsFromTemplate(report.Template);
                    report.Fields.Clear();
                    foreach (var field in extractedFields)
                    {
                        field.ReportId = report.Id;
                        report.Fields.Add(field);
                    }

                    report.PageOrientation = template.PageWidth >= template.PageHeight ? "Landscape" : "Portrait";
                    report.CreatedAt = report.CreatedAt == default ? DateTime.UtcNow : report.CreatedAt;
                    report.UpdatedAt = DateTime.UtcNow;
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

        private static MetadataObject? DetectTargetDocument(string fileName, IEnumerable<MetadataObject> documents)
        {
            var normalized = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            if (normalized.Contains("pri") || normalized.Contains("pko") || normalized.Contains("при"))
                return documents.FirstOrDefault(item => item.Name == "Расходный/Приходный КО" || item.TableName == "doc_cash_orders");
            if (normalized.Contains("ras") || normalized.Contains("rko") || normalized.Contains("рас"))
                return documents.FirstOrDefault(item => item.Name == "Расходный/Приходный КО" || item.TableName == "doc_cash_orders");
            return null;
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

