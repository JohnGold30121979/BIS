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
        private Dictionary<string, Dictionary<Guid, string>> _referenceCache;
        private Dictionary<string, MetadataObject> _catalogsDict;

        public CatalogDataView(MetadataObject catalog, MetadataService metadataService)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _referenceCache = new Dictionary<string, Dictionary<Guid, string>>();

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
                ProgressText.Text = "⏳ Загрузка...";

                var data = await _metadataService.GetCatalogDataAsync(_catalog.Id);

                // Загружаем все справочники один раз
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                _catalogsDict = allCatalogs.ToDictionary(c => c.Name, c => c);

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

                // Загружаем данные справочников для подстановки имен (универсально)
                await LoadReferenceDataAsync();

                // Добавляем строки
                foreach (var row in data)
                {
                    var dataRow = _dataTable.NewRow();
                    dataRow["Id"] = row.ContainsKey("Id") ? row["Id"] : Guid.NewGuid();

                    foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
                    {
                        var rawValue = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;

                        // Если поле ссылается на справочник - подставляем DisplayName
                        if (!string.IsNullOrEmpty(field.ReferenceCatalog) && _referenceCache.TryGetValue(field.Name, out var dict))
                        {
                            if (rawValue != DBNull.Value && rawValue != null && Guid.TryParse(rawValue.ToString(), out var guid))
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

                // Настраиваем DataGrid
                DataGrid.Columns.Clear();

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
                ProgressText.Text = "";
                EditButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                ProgressText.Text = "";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Универсальная загрузка данных для всех Reference полей
        /// </summary>
        private async Task LoadReferenceDataAsync()
        {
            _referenceCache.Clear();

            foreach (var field in _catalog.Fields.Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (!_catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    continue;

                var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                var dict = new Dictionary<Guid, string>();

                foreach (var row in refData)
                {
                    if (!row.ContainsKey("Id") || row["Id"] == null)
                        continue;

                    var id = Guid.Parse(row["Id"].ToString());

                    // Универсальное форматирование через шаблон из метаданных
                    var displayValue = GetDisplayValueFromRow(row, field, refCatalog.Name);
                    dict[id] = displayValue;
                }

                _referenceCache[field.Name] = dict;
            }
        }

        /// <summary>
        /// Универсальное получение отображаемого значения по шаблону из метаданных
        /// </summary>
        private string GetDisplayValueFromRow(Dictionary<string, object> row, MetadataField field, string catalogName)
        {
            // Если есть шаблон - используем его
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var fld in fieldNames)
                {
                    var fieldKey = fld.Trim();
                    var value = "";

                    if (row.ContainsKey(fieldKey))
                        value = row[fieldKey]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.Replace(" ", "_")))
                        value = row[fieldKey.Replace(" ", "_")]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.ToLower()))
                        value = row[fieldKey.ToLower()]?.ToString() ?? "";

                    result = result.Replace($"{{{fieldKey}}}", value);
                    result = result.Replace($"{{{fieldKey.Replace(" ", "_")}}}", value);
                }

                return result;
            }

            // === БЕЗ ШАБЛОНА - автоопределение ===

            // Для поля "Табельный номер" в справочнике МОЛ
            if (field.Name == "Табельный номер" && catalogName == "Сотрудники (Списочный состав)")
            {
                if (row.ContainsKey("Табельный номер"))
                    return row["Табельный номер"]?.ToString() ?? "";
                if (row.ContainsKey("personnel_number"))
                    return row["personnel_number"]?.ToString() ?? "";
                if (row.ContainsKey("Код"))
                    return row["Код"]?.ToString() ?? "";
                if (row.ContainsKey("code"))
                    return row["code"]?.ToString() ?? "";
            }

            // Приоритеты полей для отображения
            var priorityFields = new[]
            {
                "Наименование", "name", "Name",
                "ФИО", "full_name", "FullName",
                "Наименование вида", "Наименование категории",
                "site_name", "Наименование участка",
                "Код", "code", "Code"
            };

            foreach (var priority in priorityFields)
            {
                if (row.ContainsKey(priority) && row[priority] != null)
                    return row[priority].ToString();
            }

            // Ищем любое строковое поле
            foreach (var key in row.Keys)
            {
                if (key != "Id" && key != "CreatedAt" && key != "UpdatedAt" && row[key] is string strVal && !string.IsNullOrEmpty(strVal))
                    return strVal;
            }

            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
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
                    ProgressText.Text = "⏳ Сохранение...";
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
                    ProgressText.Text = "";
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
                if (value != DBNull.Value && column.ColumnName != "Id" &&
                    column.ColumnName != "Дата создания" && column.ColumnName != "Дата изменения")
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
                    ProgressText.Text = "⏳ Обновление...";
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
                    ProgressText.Text = "";
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null) return;

            var id = (Guid)selectedRow["Id"];
            var firstField = _catalog.Fields.FirstOrDefault();
            var name = firstField != null && selectedRow[firstField.Name] != null
                ? selectedRow[firstField.Name].ToString()
                : id.ToString();

            var result = MessageBox.Show($"Удалить запись '{name}'?\nВосстановление будет невозможно!",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = "🗑️ Удаление...";
                    ProgressText.Text = "⏳ Удаление...";
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
                    ProgressText.Text = "";
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