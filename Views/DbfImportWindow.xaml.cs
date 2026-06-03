using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using BIS.ERP.Services;

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

                DataGrid.ItemsSource = _parsedData.Rows;

                ImportButton.IsEnabled = true;
                StatusTextBlock.Text = $"Загружено {_parsedData.RecordCount} записей.";
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
                var infoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();

                int imported = 0;
                int total = _parsedData.Rows.Count;

                for (int i = 0; i < _parsedData.Rows.Count; i++)
                {
                    var row = _parsedData.Rows[i];

                    // Получаем описание из первого непустого поля
                    var description = GetFirstStringValue(row);

                    var doc = new Models.Document
                    {
                        Id = Guid.NewGuid(),
                        Number = (i + 1).ToString("D6"),
                        Date = DateTime.UtcNow,
                        DocumentType = "Operation",
                        OperationDescription = description,
                        Amount = 0,
                        InfoBaseId = infoBase?.Id,
                        IsPosted = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    await documentService.CreateDocumentAsync(doc);
                    imported++;

                    ProgressBar.Value = (i + 1) * 100 / total;
                }

                StatusTextBlock.Text = $"Импорт завершен! Импортировано: {imported} документов.";
                MessageBox.Show($"Импорт завершен!\nИмпортировано: {imported} записей",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка импорта: {ex.Message}", "Ошибка");
            }
            finally
            {
                ImportButton.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private string GetFirstStringValue(Dictionary<string, object> row)
        {
            foreach (var value in row.Values)
            {
                if (value != null && !string.IsNullOrEmpty(value.ToString()))
                {
                    var str = value.ToString();

                    // Очищаем от недопустимых символов
                    str = CleanStringForDatabase(str);

                    // Ограничиваем длину (максимум 500 символов)
                    if (str.Length > 500)
                        str = str.Substring(0, 500);

                    if (!string.IsNullOrEmpty(str))
                        return str;
                }
            }
            return "";
        }

        private string CleanStringForDatabase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                // Пропускаем нулевые байты и другие недопустимые символы
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