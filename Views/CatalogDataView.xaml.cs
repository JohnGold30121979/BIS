using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CatalogDataView : UserControl
    {
        private readonly MetadataObject _catalog;
        private readonly MetadataService _metadataService;
        private DataTable _dataTable;

        public CatalogDataView(MetadataObject catalog, MetadataService metadataService)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;

            TitleText.Text = $"{catalog.Icon} {catalog.Name}";
            DescriptionText.Text = catalog.Description;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка данных...";

                var data = await _metadataService.GetCatalogDataAsync(_catalog.Id);

                _dataTable = new DataTable();
                _dataTable.TableName = _catalog.Name;

                // Добавляем колонки с понятными названиями (русскими)
                _dataTable.Columns.Add("Id", typeof(Guid));
                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    var columnType = GetColumnType(field.FieldType);
                    _dataTable.Columns.Add(field.Name, columnType); // field.Name - русское имя
                }
                _dataTable.Columns.Add("Дата создания", typeof(DateTime));
                _dataTable.Columns.Add("Дата изменения", typeof(DateTime));

                // Добавляем строки
                foreach (var row in data)
                {
                    var dataRow = _dataTable.NewRow();
                    dataRow["Id"] = row.ContainsKey("Id") ? row["Id"] : Guid.NewGuid();

                    foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                    {
                        // row содержит ключи с русскими именами (благодаря маппингу в GetCatalogDataAsync)
                        dataRow[field.Name] = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;
                    }

                    dataRow["Дата создания"] = row.ContainsKey("CreatedAt") ? row["CreatedAt"] : DateTime.Now;
                    dataRow["Дата изменения"] = row.ContainsKey("UpdatedAt") ? row["UpdatedAt"] : DateTime.Now;

                    _dataTable.Rows.Add(dataRow);
                }

                DataGrid.ItemsSource = _dataTable.DefaultView;

                // Настройка колонок
                foreach (DataGridColumn column in DataGrid.Columns)
                {
                    if (column.Header?.ToString() == "Id")
                    {
                        column.Visibility = Visibility.Collapsed;
                    }
                    else if (column.Header?.ToString() == "Дата создания" || column.Header?.ToString() == "Дата изменения")
                    {
                        column.Width = 150;
                    }
                    else
                    {
                        column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    }
                }

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "❌ Ошибка загрузки";
            }
        }

        private Type GetColumnType(string fieldType)
        {
            return fieldType switch
            {
                "Int" => typeof(int),
                "Decimal" => typeof(decimal),
                "DateTime" => typeof(DateTime),
                "Bool" => typeof(bool),
                _ => typeof(string)
            };
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CatalogItemDialog(_catalog);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "💾 Сохранение...";

                    await _metadataService.AddCatalogItemAsync(_catalog.Id, dialog.ItemData);
                    await LoadData();

                    MessageBox.Show("Запись успешно добавлена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataTable == null || _dataTable.Rows.Count == 0)
                {
                    MessageBox.Show("Нет данных для экспорта", "Информация",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Title = "Сохранить Excel файл",
                    Filter = "Excel файлы (*.xlsx)|*.xlsx",
                    DefaultExt = "xlsx",
                    FileName = $"{_catalog.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ProgressText.Text = "⏳ Экспорт...";
                    StatusText.Text = "Подготовка данных...";

                    await Task.Run(() => ExportToExcel(saveDialog.FileName));

                    ProgressText.Text = "";
                    StatusText.Text = $"✅ Экспорт завершен! Сохранено: {saveDialog.FileName}";

                    var result = MessageBox.Show($"Данные успешно экспортированы!\n\nФайл: {saveDialog.FileName}\n\nОткрыть файл?",
                        "Экспорт завершен", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = saveDialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressText.Text = "";
                StatusText.Text = "❌ Ошибка экспорта";
            }
        }

        private void ExportToExcel(string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(_catalog.Name);

            worksheet.Cell(1, 1).InsertTable(_dataTable);

            var headerRange = worksheet.Range(1, 1, 1, _dataTable.Columns.Count);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2C3E50");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            worksheet.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        }
    }
}