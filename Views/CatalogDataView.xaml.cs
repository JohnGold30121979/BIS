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

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = DataGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка данных...";

                var data = await _metadataService.GetCatalogDataAsync(_catalog.Id);

                _dataTable = new DataTable();
                _dataTable.TableName = _catalog.Name;

                // Добавляем колонки
                _dataTable.Columns.Add("Id", typeof(Guid));
                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    var columnType = GetColumnType(field.FieldType);
                    _dataTable.Columns.Add(field.Name, columnType);
                }
                _dataTable.Columns.Add("Дата создания", typeof(DateTime));
                _dataTable.Columns.Add("Дата изменения", typeof(DateTime));

                // Загружаем данные справочников для подстановки имен
                var referenceCatalogs = new Dictionary<string, Dictionary<Guid, string>>();

                foreach (var field in _catalog.Fields.Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog)))
                {
                    var catalog = (await _metadataService.GetCatalogsAsync()).FirstOrDefault(c => c.Name == field.ReferenceCatalog);
                    if (catalog != null)
                    {
                        var catalogData = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        var dict = new Dictionary<Guid, string>();

                        foreach (var item in catalogData)
                        {
                            if (item.ContainsKey("Id") && item["Id"] != null)
                            {
                                var id = Guid.Parse(item["Id"].ToString());
                                var displayValue = "";

                                // Для справочника сотрудников - показываем "Табельный номер - ФИО"
                                if (field.ReferenceCatalog == "Сотрудники (Списочный состав)")
                                {
                                    var personnelNumber = "";
                                    var fullName = "";

                                    if (item.ContainsKey("Табельный номер"))
                                        personnelNumber = item["Табельный номер"].ToString();
                                    else if (item.ContainsKey("personnel_number"))
                                        personnelNumber = item["personnel_number"].ToString();
                                    else if (item.ContainsKey("Код"))
                                        personnelNumber = item["Код"].ToString();

                                    if (item.ContainsKey("ФИО"))
                                        fullName = item["ФИО"].ToString();
                                    else if (item.ContainsKey("full_name"))
                                        fullName = item["full_name"].ToString();
                                    else if (item.ContainsKey("Наименование"))
                                        fullName = item["Наименование"].ToString();

                                    // Для поля "Табельный номер" - показываем табельный номер
                                    if (field.Name == "Табельный номер")
                                    {
                                        displayValue = personnelNumber;
                                    }
                                    // Для других полей (если нужно) - показываем ФИО
                                    else
                                    {
                                        displayValue = fullName;
                                    }
                                }
                                // Для справочника участков
                                else if (field.ReferenceCatalog == "Участки")
                                {
                                    if (item.ContainsKey("site_name"))
                                        displayValue = item["site_name"].ToString();
                                    else if (item.ContainsKey("Наименование участка"))
                                        displayValue = item["Наименование участка"].ToString();
                                    else if (item.ContainsKey("Наименование"))
                                        displayValue = item["Наименование"].ToString();
                                    else if (item.ContainsKey("name"))
                                        displayValue = item["name"].ToString();
                                }
                                else
                                {
                                    if (item.ContainsKey("Наименование"))
                                        displayValue = item["Наименование"].ToString();
                                    else if (item.ContainsKey("name"))
                                        displayValue = item["name"].ToString();
                                }

                                if (id != Guid.Empty && !string.IsNullOrEmpty(displayValue))
                                    dict[id] = displayValue;
                            }
                        }
                        referenceCatalogs[field.Name] = dict;
                    }
                }

                // Добавляем строки
                foreach (var row in data)
                {
                    var dataRow = _dataTable.NewRow();
                    dataRow["Id"] = row.ContainsKey("Id") ? row["Id"] : Guid.NewGuid();

                    foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                    {
                        var rawValue = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;

                        // Если поле ссылается на справочник - подставляем DisplayName вместо GUID
                        if (referenceCatalogs.TryGetValue(field.Name, out var dict) && rawValue != DBNull.Value && rawValue.ToString() != "")
                        {
                            if (Guid.TryParse(rawValue.ToString(), out var guid))
                            {
                                dataRow[field.Name] = dict.ContainsKey(guid) ? dict[guid] : rawValue.ToString();
                            }
                            else
                            {
                                dataRow[field.Name] = rawValue;
                            }
                        }
                        else
                        {
                            dataRow[field.Name] = rawValue;
                        }
                    }

                    dataRow["Дата создания"] = row.ContainsKey("CreatedAt") ? row["CreatedAt"] : DateTime.Now;
                    dataRow["Дата изменения"] = row.ContainsKey("UpdatedAt") ? row["UpdatedAt"] : DateTime.Now;

                    _dataTable.Rows.Add(dataRow);
                }

                // Очищаем колонки и добавляем вручную
                DataGrid.Columns.Clear();

                // Добавляем колонки для каждого поля
                foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                {
                    DataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = field.Name,
                        Binding = new System.Windows.Data.Binding(field.Name),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                        MinWidth = 100
                    });
                }

                // Добавляем колонки с датами
                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Дата создания",
                    Binding = new System.Windows.Data.Binding("Дата создания"),
                    Width = 150
                });

                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Дата изменения",
                    Binding = new System.Windows.Data.Binding("Дата изменения"),
                    Width = 150
                });

                DataGrid.ItemsSource = _dataTable.DefaultView;

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
                EditButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            var dialog = new CatalogItemDialog(_catalog, _metadataService);
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

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null) return;

            var id = (Guid)selectedRow["Id"];
            var existingData = new Dictionary<string, object>();

            foreach (DataColumn column in _dataTable.Columns)
            {
                var value = selectedRow[column.ColumnName];
                if (value != DBNull.Value)
                {
                    existingData[column.ColumnName] = value;
                }
            }

            var dialog = new CatalogItemDialog(_catalog, _metadataService, existingData);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "💾 Обновление...";
                    await _metadataService.UpdateDynamicRecordAsync(_catalog.Id, id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись успешно обновлена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null) return;

            var id = (Guid)selectedRow["Id"];
            var name = selectedRow[_catalog.Fields.FirstOrDefault()?.Name ?? "Id"]?.ToString() ?? id.ToString();

            var result = MessageBox.Show($"Удалить запись '{name}'?\nВосстановление будет невозможно!",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = "🗑️ Удаление...";
                    await _metadataService.DeleteDynamicRecordAsync(_catalog.Id, id);
                    await LoadData();
                    MessageBox.Show("Запись успешно удалена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
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