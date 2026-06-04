using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BIS.ERP.Views
{
    public partial class DbfImportWindow : Window
    {
        private readonly DbfParserService _parser;
        private DbfParseResult _parsedData;

        public DbfImportWindow()
        {
            InitializeComponent();
            _parser = new DbfParserService();
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите DBF файл",
                Filter = "DBF файлы (*.dbf)|*.dbf|Все файлы (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePathBox.Text = dialog.FileName;
                await LoadFile(dialog.FileName);
            }
        }

        private async System.Threading.Tasks.Task LoadFile(string filePath)
        {
            try
            {
                StatusTextBlock.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Загрузка файла...";

                _parsedData = await _parser.ParseDbfFileAsync(filePath);

                if (_parsedData.HasError)
                {
                    MessageBox.Show($"Ошибка: {_parsedData.ErrorMessage}", "Ошибка");
                    return;
                }

                FileInfoText.Text = $"Файл: {_parsedData.TableName}";
                RecordInfoText.Text = $"Записей: {_parsedData.RecordCount}, Полей: {_parsedData.Fields.Count}";

                // Показываем первые 5 полей для информации
                var firstFields = _parsedData.Fields.Take(10).Select(f => f.Name);
                FieldInfoText.Text = $"Поля: {string.Join(", ", firstFields)}" + (_parsedData.Fields.Count > 10 ? "..." : "");

                DataGrid.ItemsSource = _parsedData.Rows;

                ImportButton.IsEnabled = true;
                StatusTextBlock.Text = $"Загружено {_parsedData.RecordCount} записей.";

                // Выводим все поля в консоль для отладки
                System.Diagnostics.Debug.WriteLine($"=== ВСЕ ПОЛЯ ФАЙЛА {_parsedData.TableName} ===");
                foreach (var field in _parsedData.Fields)
                {
                    System.Diagnostics.Debug.WriteLine($"  {field.Name} (Тип: {field.Type}, Длина: {field.Length})");
                }
                System.Diagnostics.Debug.WriteLine($"==========================================");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка");
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ImportButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                StatusTextBlock.Text = "Импорт данных...";

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var documentService = new DocumentService(context);

                // ДИАГНОСТИКА: Проверяем существование таблиц
                try
                {
                    System.Diagnostics.Debug.WriteLine("=== CHECKING DATABASE TABLES ===");
                    var canConnect = await context.Database.CanConnectAsync();
                    System.Diagnostics.Debug.WriteLine($"Can connect: {canConnect}");

                    // Проверяем таблицы через SQL
                    using (var cmd = context.Database.GetDbConnection().CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM \"DynamicDocuments\"";
                        context.Database.OpenConnection();
                        var count = await cmd.ExecuteScalarAsync();
                        System.Diagnostics.Debug.WriteLine($"DynamicDocuments count: {count}");
                        context.Database.CloseConnection();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Table check error: {ex.Message}");
                }

                // Создаем документ
                var documentNumber = $"IMP_{DateTime.Now:yyyyMMdd_HHmmss}";
                System.Diagnostics.Debug.WriteLine($"Creating document with number: {documentNumber}");
                System.Diagnostics.Debug.WriteLine($"Rows to import: {_parsedData.Rows.Count}");

                var document = await documentService.CreateDocumentFromDbfAsync(_parsedData, documentNumber);

                StatusTextBlock.Text = $"Импорт завершен! Документ №{document.Number} с {document.TotalRows} строками.";

                MessageBox.Show($"✅ Импорт завершен!\n\n" +
                               $"📄 Документ: {document.Number}\n" +
                               $"📊 Строк: {document.TotalRows}",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== IMPORT ERROR ===");
                System.Diagnostics.Debug.WriteLine($"Error Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"Error Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Inner Error: {ex.InnerException.Message}");
                    System.Diagnostics.Debug.WriteLine($"Inner Stack: {ex.InnerException.StackTrace}");
                }

                MessageBox.Show($"Ошибка импорта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private string GetFieldValue(Dictionary<string, object> row, string fieldName)
        {
            if (row.ContainsKey(fieldName) && row[fieldName] != null)
            {
                var value = row[fieldName].ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    value = CleanStringForDatabase(value);
                    if (value.Length > 500)
                        value = value.Substring(0, 500);
                    return value;
                }
            }
            return "";
        }

        private string GetFirstStringValue(Dictionary<string, object> row)
        {
            foreach (var value in row.Values)
            {
                if (value != null && !string.IsNullOrEmpty(value.ToString()))
                {
                    var str = value.ToString();
                    str = CleanStringForDatabase(str);
                    if (str.Length > 500)
                        str = str.Substring(0, 500);
                    if (!string.IsNullOrEmpty(str))
                        return str;
                }
            }
            return "";
        }

        private decimal GetDecimalValue(Dictionary<string, object> row, string fieldName)
        {
            if (row.ContainsKey(fieldName) && row[fieldName] != null)
            {
                var str = row[fieldName].ToString();
                if (!string.IsNullOrEmpty(str))
                {
                    str = str.Replace('.', ',');
                    if (decimal.TryParse(str, out decimal result))
                        return result;
                }
            }
            return 0;
        }

        private string CleanStringForDatabase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (c != '\0' && c != '\u001A' && c != '\uFFFD')
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}