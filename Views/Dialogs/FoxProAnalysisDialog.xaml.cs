using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views.Dialogs
{
    public partial class FoxProAnalysisDialog : Window
    {
        private const string DefaultFoxProPath = @"E:\C#\Новая папка\Артур\Артур\rab (исходники модулей)\fin_vpf9";
        private readonly FoxProProjectAnalyzer _analyzer = new();
        private FoxProAnalysisResult? _result;

        public FoxProAnalysisDialog()
        {
            InitializeComponent();
            if (Directory.Exists(DefaultFoxProPath))
                ProjectPathBox.Text = DefaultFoxProPath;
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите папку FoxPro-проекта",
                InitialDirectory = Directory.Exists(ProjectPathBox.Text)
                    ? ProjectPathBox.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog(this) == true)
                ProjectPathBox.Text = dialog.FolderName;
        }

        private async void OnAnalyzeClick(object sender, RoutedEventArgs e)
        {
            await AnalyzeAsync();
        }

        private async Task AnalyzeAsync()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                SaveJsonButton.IsEnabled = false;
                StatusText.Text = "Анализ FoxPro-проекта...";

                _result = await _analyzer.AnalyzeAsync(ProjectPathBox.Text);
                BindResult(_result);

                SaveJsonButton.IsEnabled = true;
                OpenCodeButton.IsEnabled = true;
                OpenTextButton.IsEnabled = true;
                StatusText.Text = $"Анализ завершен: {_result.Summary.TotalFiles} файлов, {_result.Summary.TableUsages} обращений к таблицам.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка анализа";
                MessageBox.Show($"Ошибка анализа FoxPro-проекта: {ex.Message}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BindResult(FoxProAnalysisResult result)
        {
            SummaryText.Text =
                $"Папка: {result.RootPath}\n" +
                $"Дата анализа: {result.AnalyzedAt:dd.MM.yyyy HH:mm}\n\n" +
                $"Файлов всего: {result.Summary.TotalFiles}\n" +
                $"PRG: {result.Summary.PrgFiles}, формы SCX: {result.Summary.FormFiles}, отчеты FRX: {result.Summary.ReportFiles}, DBF-подобные: {result.Summary.DbfLikeFiles}\n" +
                $"Определений процедур/классов: {result.Summary.ProcedureDefinitions}\n" +
                $"Вызовов форм: {result.Summary.FormCalls}, отчетов: {result.Summary.ReportCalls}\n" +
                $"Использований таблиц в коде: {result.Summary.TableUsages}\n" +
                $"Файлов правил проводок f_prov*.prg: {result.Summary.PostingRuleFiles}";

            ExtensionGrid.ItemsSource = result.ExtensionStatistics;
            FilesGrid.ItemsSource = result.Files;
            ReferencesGrid.ItemsSource = result.References
                .OrderBy(reference => reference.ReferenceType)
                .ThenBy(reference => reference.Target)
                .ToList();
            DefinitionsGrid.ItemsSource = result.Definitions
                .OrderBy(definition => definition.Kind)
                .ThenBy(definition => definition.Name)
                .ToList();
            DbfGrid.ItemsSource = result.DbfTables
                .OrderByDescending(table => table.RecordCount)
                .ThenBy(table => table.FileName)
                .ToList();
            TableUsagesGrid.ItemsSource = result.TableUsages
                .OrderBy(usage => usage.TableName)
                .ThenBy(usage => usage.SourceFile)
                .ToList();
            WarningsList.ItemsSource = result.Warnings;
        }

        private async void OnSaveJsonClick(object sender, RoutedEventArgs e)
        {
            if (_result == null)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить анализ FoxPro",
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = $"foxpro-analysis-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Сохранение JSON...";
                await _analyzer.SaveJsonAsync(_result, dialog.FileName);
                StatusText.Text = $"JSON сохранен: {dialog.FileName}";
                MessageBox.Show($"Анализ сохранен:\n{dialog.FileName}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка сохранения JSON";
                MessageBox.Show($"Ошибка сохранения JSON: {ex.Message}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void OnOpenInVsCodeClick(object sender, RoutedEventArgs e)
        {
            OpenSelectedFile(preferVsCode: true);
        }

        private void OnOpenAsTextClick(object sender, RoutedEventArgs e)
        {
            OpenSelectedFile(preferVsCode: false);
        }

        private void OnOpenSelectedInVsCodeClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedFile(preferVsCode: true, sender);
        }

        private void OpenSelectedFile(bool preferVsCode, object? source = null)
        {
            if (_result == null)
            {
                MessageBox.Show("Сначала выполните анализ FoxPro-проекта.", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = ResolveSelectedFile(source);
            if (selected.RelativePath == null)
            {
                MessageBox.Show("Выберите строку с файлом или ссылкой на код.", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fullPath = Path.GetFullPath(Path.Combine(_result.RootPath, selected.RelativePath));
            if (!File.Exists(fullPath))
            {
                MessageBox.Show($"Файл не найден:\n{fullPath}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (preferVsCode)
                {
                    if (TryOpenWithVsCode(fullPath, selected.LineNumber))
                    {
                        StatusText.Text = selected.LineNumber.HasValue
                            ? $"Открыто в VS Code: {selected.RelativePath}:{selected.LineNumber}"
                            : $"Открыто в VS Code: {selected.RelativePath}";
                        return;
                    }

                    var fallback = MessageBox.Show(
                        "VS Code не найден через стандартные пути и команду code.\nОткрыть файл в Блокноте?",
                        "Анализ FoxPro",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (fallback != MessageBoxResult.Yes)
                        return;
                }

                OpenWithNotepad(fullPath);
                StatusText.Text = $"Открыто как TXT: {selected.RelativePath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть файл: {ex.Message}", "Анализ FoxPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (string? RelativePath, int? LineNumber) ResolveSelectedFile(object? source = null)
        {
            if (source is DataGrid grid)
                return ResolveSelectedFileFromGrid(grid);

            if (AnalysisTabs.SelectedItem is TabItem tabItem)
            {
                var header = tabItem.Header?.ToString() ?? string.Empty;
                return header switch
                {
                    "Файлы" => ResolveSelectedFileFromGrid(FilesGrid),
                    "Вызовы" => ResolveSelectedFileFromGrid(ReferencesGrid),
                    "Определения" => ResolveSelectedFileFromGrid(DefinitionsGrid),
                    "DBF/SCX/FRX" => ResolveSelectedFileFromGrid(DbfGrid),
                    "Таблицы в коде" => ResolveSelectedFileFromGrid(TableUsagesGrid),
                    _ => (null, null)
                };
            }

            return ResolveSelectedFileFromGrid(FilesGrid);
        }

        private static (string? RelativePath, int? LineNumber) ResolveSelectedFileFromGrid(DataGrid grid)
        {
            return grid.SelectedItem switch
            {
                FoxProFileSummary file => (file.RelativePath, null),
                FoxProCodeReference reference => (reference.SourceFile, reference.LineNumber),
                FoxProSymbolDefinition definition => (definition.SourceFile, definition.LineNumber),
                FoxProDbfTableInfo table => (table.RelativePath, null),
                FoxProTableUsage usage => (usage.SourceFile, usage.LineNumber),
                _ => (null, null)
            };
        }

        private static bool TryOpenWithVsCode(string filePath, int? lineNumber)
        {
            var argumentPath = lineNumber.HasValue
                ? $"{filePath}:{lineNumber.Value}"
                : filePath;
            var arguments = $"-g \"{argumentPath}\"";

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe")
            };

            foreach (var candidate in candidates.Where(File.Exists))
            {
                if (TryStartProcess(candidate, arguments, useShellExecute: false))
                    return true;
            }

            return TryStartProcess("code", arguments, useShellExecute: true)
                || TryStartProcess("code.cmd", arguments, useShellExecute: true);
        }

        private static void OpenWithNotepad(string filePath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false
            });
        }

        private static bool TryStartProcess(string fileName, string arguments, bool useShellExecute)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = useShellExecute
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
