using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class ReportPreviewWindow : Window
    {
        private readonly DataTable _data;
        private readonly Report _report;
        private readonly ReportService _reportService;

        public ReportPreviewWindow(DataTable data, Report report, ReportService reportService)
        {
            InitializeComponent();
            _data = data;
            _report = report;
            _reportService = reportService;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // ✅ ПРОВЕРКА: Есть ли данные?
                if (_data == null || _data.Rows.Count == 0)
                {
                    // Скрываем таблицу
                    PreviewGrid.Visibility = Visibility.Collapsed;

                    // Скрываем шапку и подвал
                    HeaderPanel.Visibility = Visibility.Collapsed;
                    FooterPanel.Visibility = Visibility.Collapsed;

                    // ✅ ПОКАЗЫВАЕМ СООБЩЕНИЕ ОБ ОТСУТСТВИИ ДАННЫХ
                    // Очищаем контейнер ReportContainer (это Border)
                    ReportContainer.Child = null;

                    // Создаем сообщение
                    var noDataPanel = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(20)
                    };

                    noDataPanel.Children.Add(new TextBlock
                    {
                        Text = "📭",
                        FontSize = 48,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 15)
                    });

                    noDataPanel.Children.Add(new TextBlock
                    {
                        Text = "Нет данных для отображения",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10)
                    });

                    noDataPanel.Children.Add(new TextBlock
                    {
                        Text = "Проверьте:\n• Наличие материалов в базе\n• Правильность фильтров\n• Период отчета",
                        FontSize = 13,
                        Foreground = System.Windows.Media.Brushes.DarkGray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    });

                    // Устанавливаем сообщение в контейнер
                    ReportContainer.Child = noDataPanel;

                    Title = $"Предпросмотр: {_report.Name} (нет данных)";
                    StatusText.Text = "Нет данных";
                    return;
                }

                // ✅ ПРОВЕРКА: Есть ли колонки?
                if (_data.Columns.Count == 0)
                {
                    PreviewGrid.Visibility = Visibility.Collapsed;
                    HeaderPanel.Visibility = Visibility.Collapsed;
                    FooterPanel.Visibility = Visibility.Collapsed;

                    ReportContainer.Child = null;

                    var noColumnsPanel = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    noColumnsPanel.Children.Add(new TextBlock
                    {
                        Text = "⚠️",
                        FontSize = 48,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 15)
                    });

                    noColumnsPanel.Children.Add(new TextBlock
                    {
                        Text = "Нет колонок для отображения",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.Orange,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    ReportContainer.Child = noColumnsPanel;

                    Title = $"Предпросмотр: {_report.Name} (нет колонок)";
                    StatusText.Text = "Нет колонок";
                    return;
                }

                // ✅ ЕСТЬ ДАННЫЕ - отображаем
                PreviewGrid.Visibility = Visibility.Visible;
                HeaderPanel.Visibility = Visibility.Visible;
                FooterPanel.Visibility = Visibility.Visible;

                PreviewGrid.ItemsSource = _data.DefaultView;
                Title = $"Предпросмотр: {_report.Name}";
                ApplyReportAppearance();

                // Настраиваем колонки
                foreach (var column in PreviewGrid.Columns)
                {
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }

                // Обновляем статус
                StatusText.Text = $"Записей: {_data.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка отображения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyReportAppearance()
        {
            HeaderTitle.Text = string.IsNullOrWhiteSpace(_report.TitleText)
                ? _report.Name
                : _report.TitleText;
            HeaderSubtitle.Text = _report.SubtitleText ?? string.Empty;
            HeaderPanel.Visibility = _report.ShowHeader ? Visibility.Visible : Visibility.Collapsed;
            FooterPanel.Visibility = _report.ShowFooter || _report.ShowPageNumbers
                ? Visibility.Visible
                : Visibility.Collapsed;
            FooterText.Text = _report.FooterText ?? string.Empty;
            FooterSignature.Text = _report.FooterSignature ?? string.Empty;
            PageNumber.Text = _report.ShowPageNumbers ? "Страница 1" : string.Empty;

            try
            {
                HeaderPanel.Background = (Brush)new BrushConverter().ConvertFromString(_report.HeaderColor);
                if (_report.AlternateRowColors)
                    PreviewGrid.AlternatingRowBackground =
                        (Brush)new BrushConverter().ConvertFromString(_report.AlternateRowColor);
            }
            catch
            {
                // Некорректный пользовательский цвет не должен мешать формированию отчета.
            }

            PreviewGrid.GridLinesVisibility = _report.ShowGridLines
                ? DataGridGridLinesVisibility.All
                : DataGridGridLinesVisibility.None;
            PreviewGrid.FontFamily = new FontFamily(
                string.IsNullOrWhiteSpace(_report.FontName) ? "Segoe UI" : _report.FontName);
            PreviewGrid.FontSize = _report.FontSize > 0 ? _report.FontSize : 10;
        }

        private async void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputPath = BuildTemporaryExcelPath(_report.Name, "xlsx");
                byte[] data;

                if (HasFoxProTemplate(_report))
                {
                    var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                    var ruleService = new FoxProReportFieldRuleService(context);
                    await ruleService.SeedDefaultRulesAsync();
                    var rules = await ruleService.GetRulesAsync(includeInactive: false);
                    var dataSnapshot = _data.Copy();
                    var printFormService = new PrintFormService(context);
                    data = await Task.Run(() => printFormService.ExportReportTemplateExcel(dataSnapshot, _report, rules));
                }
                else
                {
                    data = await Task.Run(() => _reportService.ExportToExcel(_data, _report));
                }

                File.WriteAllBytes(outputPath, data);
                OpenGeneratedFile(outputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия Excel: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool HasFoxProTemplate(Report report) =>
            !string.IsNullOrWhiteSpace(report.Template) &&
            (string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase));

        private static string BuildTemporaryExcelPath(string baseName, string extension)
        {
            var safeName = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "report";

            var normalizedExtension = (extension ?? "xlsx").Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(normalizedExtension))
                normalizedExtension = "xlsx";

            var directory = Path.Combine(Path.GetTempPath(), "BIS.ERP", "ExcelPreview");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalizedExtension}");
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = (value ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(chars).Trim(' ', '.', '_');
        }

        private static void OpenGeneratedFile(string filePath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
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

        private void OnExportPdfClick(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Сохранить PDF файл",
                Filter = "PDF файлы (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = $"{_report.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var data = _reportService.ExportToPdf(_data, _report);
                    File.WriteAllBytes(saveDialog.FileName, data);

                    MessageBox.Show($"PDF сохранен: {saveDialog.FileName}", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения PDF: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintVisual(ReportContainer, _report.Name);
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
