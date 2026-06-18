using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class DynamicDocumentsView : UserControl
    {
        private readonly DocumentService _documentService;
        private List<DynamicDocument> _documents = new();
        private bool _isLoading = false;

        public DynamicDocumentsView(DocumentService documentService)
        {
            InitializeComponent();
            _documentService = documentService;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadDocuments();
        }

        private async Task LoadDocuments()
        {
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка списка документов...";

                _documents = await _documentService.GetDocumentsAsync();
                DocumentsList.ItemsSource = _documents;

                UpdateButtonsState();

                if (_documents.Any())
                {
                    DocumentsList.SelectedIndex = 0;
                }

                StatusText.Text = $"Загружено {_documents.Count} документов";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки документов: {ex.Message}", "Ошибка");
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private void UpdateButtonsState()
        {
            var hasSelection = DocumentsList.SelectedItem != null;
            DeleteButton.IsEnabled = hasSelection;
            ExportButton.IsEnabled = hasSelection;
        }

        private async void OnDocumentSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();

            if (DocumentsList.SelectedItem is DynamicDocument document)
            {
                await LoadDocumentDetails(document);
            }
        }

        private async Task LoadDocumentDetails(DynamicDocument document)
        {
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = $"Загрузка документа {document.Number}...";

                await Dispatcher.InvokeAsync(() => {
                    DataGrid.ItemsSource = null;
                    DocumentTitle.Text = $"📄 Загрузка...";
                    DetailsInfo.Text = "Загрузка данных...";
                });

                var fullDocument = await _documentService.GetDocumentByIdAsync(document.Id);

                if (fullDocument == null) return;

                // Получаем все поля
                var allFields = await _documentService.GetAllFieldNamesAsync(fullDocument);

                // СОЗДАЕМ DATATABLE
                var dataTable = new DataTable();

                // Добавляем колонку для номера строки
                dataTable.Columns.Add("№", typeof(int));

                // Добавляем колонки для всех полей
                foreach (var field in allFields)
                {
                    dataTable.Columns.Add(field, typeof(string));
                }

                var rows = fullDocument.Rows.OrderBy(r => r.RowNumber).ToList();
                int rowNumber = 1;

                foreach (var row in rows)
                {
                    var rowData = await _documentService.GetRowDataAsync(row);

                    // Создаем новую строку DataTable
                    var dataRow = dataTable.NewRow();

                    // Устанавливаем номер строки
                    dataRow["№"] = rowNumber++;

                    // Заполняем все поля
                    foreach (var field in allFields)
                    {
                        if (rowData.ContainsKey(field) && rowData[field] != null)
                        {
                            var value = rowData[field].ToString() ?? string.Empty;
                            // Ограничиваем длину для отображения
                            dataRow[field] = value.Length > 200 ? value.Substring(0, 200) + "..." : value;
                        }
                        else
                        {
                            dataRow[field] = "";
                        }
                    }

                    dataTable.Rows.Add(dataRow);
                }

                await Dispatcher.InvokeAsync(() => {
                    DocumentTitle.Text = $"📄 {fullDocument.DisplayName}";

                    // Устанавливаем DataTable как источник
                    DataGrid.ItemsSource = dataTable.DefaultView;

                    // Настройка ширины колонок
                    DataGrid.AutoGenerateColumns = true;
                    DataGrid.ColumnWidth = DataGridLength.Auto;

                    DetailsInfo.Text = $"📁 Файл: {fullDocument.SourceFile}\n" +
                                      $"📋 Тип: {fullDocument.DocumentType}\n" +
                                      $"📅 Создан: {fullDocument.CreatedAt:dd.MM.yyyy HH:mm:ss}\n" +
                                      $"📊 Всего строк: {fullDocument.TotalRows}\n" +
                                      $"🔢 Всего полей: {allFields.Count}\n\n" +
                                      $"🏷️ Список первых 20 полей:\n{string.Join("\n", allFields.Take(20))}" +
                                      $"{(allFields.Count > 20 ? $"\n... и еще {allFields.Count - 20} полей" : "")}";

                    StatusText.Text = $"Документ {fullDocument.Number} загружен ({fullDocument.TotalRows} строк, {allFields.Count} полей)";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => {
                    StatusText.Text = $"Ошибка загрузки: {ex.Message}";
                    MessageBox.Show($"Ошибка загрузки деталей: {ex.Message}", "Ошибка");
                });
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            var importWindow = new DbfImportWindow();
            if (importWindow.ShowDialog() == true)
            {
                await LoadDocuments();
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadDocuments();
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DocumentsList.SelectedItem is DynamicDocument document)
            {
                var result = MessageBox.Show($"Удалить документ {document.Number}?\n" +
                    $"Все данные документа будут безвозвратно удалены.",
                    "Подтверждение удаления",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        _isLoading = true;
                        Mouse.OverrideCursor = Cursors.Wait;
                        StatusText.Text = $"Удаление документа {document.Number}...";

                        await _documentService.DeleteDocumentAsync(document.Id);
                        await LoadDocuments();

                        StatusText.Text = $"Документ {document.Number} удален";
                    }
                    catch (Exception ex)
                    {
                        StatusText.Text = $"Ошибка удаления: {ex.Message}";
                        MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка");
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                        _isLoading = false;
                    }
                }
            }
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (DocumentsList.SelectedItem is DynamicDocument document)
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "JSON файлы (*.json)|*.json",
                    FileName = $"{document.Number}_{DateTime.Now:yyyyMMdd_HHmmss}.json"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    try
                    {
                        _isLoading = true;
                        Mouse.OverrideCursor = Cursors.Wait;
                        StatusText.Text = $"Экспорт документа {document.Number}...";

                        var fullDocument = await _documentService.GetDocumentByIdAsync(document.Id);

                        var exportData = new
                        {
                            Document = new
                            {
                                fullDocument.Number,
                                fullDocument.Date,
                                fullDocument.DocumentType,
                                fullDocument.SourceFile,
                                fullDocument.TotalRows,
                                fullDocument.CreatedAt
                            },
                            Rows = fullDocument.Rows.OrderBy(r => r.RowNumber)
                                .Select(r => _documentService.GetRowData(r))
                                .ToList()
                        };

                        var json = System.Text.Json.JsonSerializer.Serialize(exportData,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                        await System.IO.File.WriteAllTextAsync(saveDialog.FileName, json);

                        StatusText.Text = $"Документ экспортирован: {saveDialog.FileName}";
                        MessageBox.Show($"Документ экспортирован в файл:\n{saveDialog.FileName}",
                            "Экспорт завершен", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        StatusText.Text = $"Ошибка экспорта: {ex.Message}";
                        MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка");
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                        _isLoading = false;
                    }
                }
            }
        }
    }
}
