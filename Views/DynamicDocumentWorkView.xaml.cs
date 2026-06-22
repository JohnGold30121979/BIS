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
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

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
                    var catalog = allCatalogs.FirstOrDefault(c => c.Name == field.ReferenceCatalog);
                    if (catalog != null)
                    {
                        var catalogData = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        var dict = new Dictionary<Guid, string>();
                        foreach (var item in catalogData)
                        {
                            var id = item.ContainsKey("Id") ? Guid.Parse(item["Id"].ToString()) : Guid.Empty;
                            if (id != Guid.Empty)
                                dict[id] = ReferenceDisplayHelper.BuildDisplayValue(item, field);
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
                            if (Guid.TryParse(rawValue?.ToString(), out var guid))
                                dataRow[field.Name] = dict.ContainsKey(guid) ? dict[guid] : rawValue.ToString();
                            else
                                dataRow[field.Name] = string.Empty;
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

                UpdateAnalyticColumns(data, accountAnalytics);

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = _documentMetadata.Fields
                .Where(AccountAnalyticsRules.IsAccountSelectorField)
                .Select(field => field.Name)
                .ToList();

            foreach (var field in _documentMetadata.Fields.Where(field =>
                         AccountAnalyticsRules.IsAccountControlledField(field, accountAnalytics.Definitions)))
            {
                var isVisible = AccountAnalyticsRules.ShouldShowFieldForRows(
                    field.Name,
                    rows,
                    accountFields,
                    accountAnalytics,
                    field.ReferenceCatalog);

                var column = DataGrid.Columns.FirstOrDefault(dataGridColumn =>
                    string.Equals(dataGridColumn.Header?.ToString(), field.Name, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                    column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
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
            // Для документа "Проводки" используем кастомный диалог
            if (_documentMetadata.Name == "Проводки")
            {
                var dialog = new PostingEditDialog(_documentMetadata, _metadataService);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                    MessageBox.Show("Проводка добавлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    await _metadataService.CreateDynamicRecordAsync(_documentMetadata.Id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись добавлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            UpdateButtonsState();
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

            // Для документа "Проводки" используем кастомный диалог
            if (_documentMetadata.Name == "Проводки")
            {
                var dialog = new PostingEditDialog(_documentMetadata, _metadataService, id);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                    MessageBox.Show("Проводка обновлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService, id);
                dialog.Owner = Window.GetWindow(this);
                if (dialog.ShowDialog() == true)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_documentMetadata.Id, id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись обновлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            UpdateButtonsState();
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

        private async void OnPostClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null)
            {
                MessageBox.Show("Выберите документ для проведения!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var id = (Guid)selectedRow["Id"];
            var result = MessageBox.Show("Провести выбранный документ?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = "🔄 Проведение...";
                    await _metadataService.PostDocumentAsync(_documentMetadata.Id, id);
                    await LoadData();
                    MessageBox.Show("Документ проведён!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка проведения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
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
