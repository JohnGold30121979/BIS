using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    public partial class DynamicDocumentWorkView : UserControl
    {
        private readonly MetadataObject _documentMetadata;
        private readonly MetadataService _metadataService;
        private DataTable _dataTable;

        public DynamicDocumentWorkView(MetadataObject documentMetadata, MetadataService metadataService)
        {
            InitializeComponent();
            _documentMetadata = documentMetadata;
            _metadataService = metadataService;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
            UpdateButtonsState();
        }

        private void UpdateButtonsState()
        {
            var hasSelection = DataGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка данных...";
                var data = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);

                _dataTable = new DataTable();
                _dataTable.TableName = _documentMetadata.Name;

                // Добавляем колонки
                _dataTable.Columns.Add("Id", typeof(Guid));
                foreach (var field in _documentMetadata.Fields.OrderBy(f => f.Order))
                {
                    var columnType = GetColumnType(field.FieldType);
                    _dataTable.Columns.Add(field.Name, columnType);
                }
                _dataTable.Columns.Add("Дата создания", typeof(DateTime));
                _dataTable.Columns.Add("Дата изменения", typeof(DateTime));

                // Загружаем справочники для подстановки имен
                var referenceCatalogs = new Dictionary<string, Dictionary<Guid, string>>();
                foreach (var field in _documentMetadata.Fields.Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog)))
                {
                    var catalog = (await _metadataService.GetCatalogsAsync()).FirstOrDefault(c => c.Name == field.ReferenceCatalog);
                    if (catalog != null)
                    {
                        var catalogData = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        var dict = new Dictionary<Guid, string>();
                        foreach (var item in catalogData)
                        {
                            var id = item.ContainsKey("Id") ? Guid.Parse(item["Id"].ToString()) : Guid.Empty;
                            var name = item.ContainsKey("Наименование") ? item["Наименование"].ToString() :
                                      (item.ContainsKey("name") ? item["name"].ToString() : "");
                            if (id != Guid.Empty)
                                dict[id] = name;
                        }
                        referenceCatalogs[field.Name] = dict;
                    }
                }

                // Добавляем строки
                foreach (var row in data)
                {
                    var dataRow = _dataTable.NewRow();
                    dataRow["Id"] = row.ContainsKey("Id") ? row["Id"] : Guid.NewGuid();

                    foreach (var field in _documentMetadata.Fields.OrderBy(f => f.Order))
                    {
                        var rawValue = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;

                        // Если поле ссылается на справочник - подставляем Name вместо GUID
                        if (referenceCatalogs.TryGetValue(field.Name, out var dict) && rawValue != DBNull.Value)
                        {
                            var guid = Guid.Parse(rawValue.ToString());
                            dataRow[field.Name] = dict.ContainsKey(guid) ? dict[guid] : rawValue.ToString();
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

                DataGrid.ItemsSource = _dataTable.DefaultView;

                // Настройка колонок
                foreach (DataGridColumn column in DataGrid.Columns)
                {
                    if (column.Header?.ToString() == "Id")
                        column.Visibility = Visibility.Collapsed;
                    else
                        column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
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
            var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "💾 Сохранение...";
                    await _metadataService.AddCatalogItemAsync(_documentMetadata.Id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись добавлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                    UpdateButtonsState();
                }
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите запись для редактирования!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var id = (Guid)selectedRow["Id"];
            var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService, id);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "💾 Обновление...";
                    await _metadataService.UpdateDynamicRecordAsync(_documentMetadata.Id, id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись обновлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                    UpdateButtonsState();
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите запись для удаления!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("Удалить выбранную запись?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var id = (Guid)selectedRow["Id"];
                    StatusText.Text = "🗑️ Удаление...";
                    await _metadataService.DeleteDynamicRecordAsync(_documentMetadata.Id, id);
                    await LoadData();
                    MessageBox.Show("Запись удалена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StatusText.Text = "✅ Готово";
                    UpdateButtonsState();
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
            UpdateButtonsState();
        }
    }
}