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
            var row = DataGrid.SelectedItem as DataRowView;
            var hasSelection = row != null;
            var isPosted = row != null && IsPosted(row);
            EditButton.IsEnabled = hasSelection && !isPosted;
            DeleteButton.IsEnabled = hasSelection && !isPosted;
            PostButton.IsEnabled = hasSelection && !isPosted;
        }

        private static bool IsPosted(DataRowView row)
        {
            foreach (var columnName in new[] { "Проведен", "Проведён", "is_posted" })
            {
                if (row.DataView.Table.Columns.Contains(columnName) && row[columnName] is bool value)
                    return value;
            }
            return false;
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
                var referenceCatalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in _documentMetadata.Fields.Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog)))
                {
                    var catalog = allCatalogs.FirstOrDefault(c => c.Name == field.ReferenceCatalog);
                    if (catalog != null)
                    {
                        var catalogData = await _metadataService.GetCatalogDataAsync(catalog.Id);
                        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in catalogData)
                        {
                            var displayValue = ReferenceDisplayHelper.BuildDisplayValue(item, field);
                            foreach (var key in GetReferenceLookupKeys(item, displayValue))
                            {
                                if (!dict.ContainsKey(key))
                                    dict[key] = displayValue;
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

                    foreach (var field in _documentMetadata.Fields.OrderBy(f => f.Order))
                    {
                        var rawValue = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;

                        // Если поле ссылается на справочник - подставляем Name вместо GUID
                        if (referenceCatalogs.TryGetValue(field.Name, out var dict) && rawValue != DBNull.Value)
                        {
                            var rawText = rawValue?.ToString() ?? string.Empty;
                            var normalized = NormalizeReferenceLookupKey(rawText);
                            dataRow[field.Name] = dict.TryGetValue(rawText.Trim(), out var displayValue)
                                ? displayValue
                                : dict.TryGetValue(normalized, out displayValue)
                                    ? displayValue
                                    : rawText;
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
                ApplyDocumentGridProfile();

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

        private static IEnumerable<string> GetReferenceLookupKeys(
            Dictionary<string, object> row,
            string displayName)
        {
            foreach (var keyName in new[]
                     {
                         "Id", "Код", "code", "Code", "Счет", "account_code",
                         "Наименование", "name", "ФИО", "full_name"
                     })
            {
                if (!row.TryGetValue(keyName, out var value))
                    continue;

                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                yield return displayName.Trim();

            var displayKey = NormalizeReferenceLookupKey(displayName);
            if (!string.IsNullOrWhiteSpace(displayKey))
                yield return displayKey;
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? normalized[..separatorIndex].Trim()
                : normalized;
        }


        private void ApplyDocumentGridProfile()
        {
            if (!IsFixedAssetMovementDocument())
                return;

            DataGrid.IsReadOnly = true;

            var profiles = new[]
            {
                CreateGridColumnProfile("Дата", 175, "Дата", "doc_date"),
                CreateGridColumnProfile("№ документа", 115, "Номер", "№ документа", "doc_number"),
                CreateGridColumnProfile("№ счет-фактуры", 145, "№ счет-фактуры", "invoice_number"),
                CreateGridColumnProfile("Сумма", 120, "Сумма", "amount"),
                CreateGridColumnProfile("Сумма в валюте", 135, "Сумма в валюте", "amount_currency"),
                CreateGridColumnProfile("Организация", 220, "Организация", "organization_id"),
                CreateGridColumnProfile("Примечание", 1, "Примечание", "description")
            };

            var visibleColumns = new List<(DataGridColumn Column, GridColumnProfile Profile)>();
            foreach (var column in DataGrid.Columns)
            {
                var header = column.Header?.ToString() ?? string.Empty;
                var profile = profiles.FirstOrDefault(item => item.Matches(header));
                if (profile == null)
                {
                    column.Visibility = Visibility.Collapsed;
                    continue;
                }

                column.Header = profile.Header;
                column.Visibility = Visibility.Visible;
                column.Width = profile.Header == "Примечание"
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(profile.Width);

                ApplyFixedAssetMovementColumnFormat(column, profile.Header);

                visibleColumns.Add((column, profile));
            }

            foreach (var item in visibleColumns
                         .OrderBy(item => Array.IndexOf(profiles, item.Profile))
                         .Select((item, index) => new { item.Column, Index = index }))
            {
                item.Column.DisplayIndex = item.Index;
            }
        }

        private static void ApplyFixedAssetMovementColumnFormat(DataGridColumn column, string header)
        {
            if (column is not DataGridTextColumn textColumn)
                return;

            if (string.Equals(header, "Дата", StringComparison.OrdinalIgnoreCase))
                ApplyBindingStringFormat(textColumn, "{0:dd.MM.yyyy HH:mm:ss}");
            else if (IsAmountGridColumn(header))
                ApplyBindingStringFormat(textColumn, "{0:N2}");

            if (IsAmountGridColumn(header))
                textColumn.ElementStyle = CreateRightAlignedTextBlockStyle();
        }

        private static void ApplyBindingStringFormat(DataGridTextColumn textColumn, string stringFormat)
        {
            if (textColumn.Binding is not System.Windows.Data.Binding binding)
                return;

            textColumn.Binding = new System.Windows.Data.Binding
            {
                Path = binding.Path,
                XPath = binding.XPath,
                Mode = binding.Mode,
                UpdateSourceTrigger = binding.UpdateSourceTrigger,
                Converter = binding.Converter,
                ConverterCulture = binding.ConverterCulture,
                ConverterParameter = binding.ConverterParameter,
                FallbackValue = binding.FallbackValue,
                TargetNullValue = binding.TargetNullValue,
                StringFormat = stringFormat
            };
        }

        private static bool IsAmountGridColumn(string header)
            => string.Equals(header, "Сумма", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Сумма в валюте", StringComparison.OrdinalIgnoreCase);

        private static Style CreateRightAlignedTextBlockStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0)));
            return style;
        }
        private static GridColumnProfile CreateGridColumnProfile(
            string header,
            double width,
            params string[] sourceNames)
        {
            return new GridColumnProfile(header, width, sourceNames);
        }

        private bool IsFixedAssetMovementDocument()
            => string.Equals(_documentMetadata.Name, "Учет движения ОС", StringComparison.OrdinalIgnoreCase);

        private sealed class GridColumnProfile
        {
            public GridColumnProfile(string header, double width, string[] sourceNames)
            {
                Header = header;
                Width = width;
                SourceNames = sourceNames
                    .Append(header)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            public string Header { get; }
            public double Width { get; }
            private string[] SourceNames { get; }

            public bool Matches(string header)
                => SourceNames.Any(source => string.Equals(source, header, StringComparison.OrdinalIgnoreCase));
        }
        private async Task<Dictionary<string, object>?> SelectFixedAssetMovementTypeAsync()
        {
            var metadataItems = await _metadataService.GetCatalogsAsync();
            var entryCatalog = metadataItems.FirstOrDefault(item =>
                item.ObjectType == "Catalog" &&
                string.Equals(item.Name, "Ввод нового документа ОС", StringComparison.OrdinalIgnoreCase));

            if (entryCatalog == null)
            {
                MessageBox.Show("Справочник 'Ввод нового документа ОС' не найден. Обновите метаданные конфигурации.",
                    "Ввод документа ОС", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var entries = await _metadataService.GetCatalogDataAsync(entryCatalog.Id);
            var activeEntries = entries
                .Where(IsActiveCatalogRow)
                .OrderBy(ReadSortOrder)
                .ThenBy(row => ReadString(row, "Код", "code"))
                .ToList();

            if (activeEntries.Count == 0)
            {
                MessageBox.Show("В справочнике 'Ввод нового документа ОС' нет активных записей.",
                    "Ввод документа ОС", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var owner = Window.GetWindow(this);
            var dialog = new ReferenceSelectionDialog(activeEntries, "Код", "Наименование")
            {
                Title = "Ввод нового документа ОС"
            };

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(owner, dialog, dialog.Title) != true || dialog.SelectedItem == null)
                return null;

            if (!dialog.SelectedItem.TryGetValue("Id", out var id) ||
                !Guid.TryParse(id?.ToString(), out var entryId))
            {
                MessageBox.Show("Не удалось определить выбранный вид документа ОС.",
                    "Ввод документа ОС", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var entryCode = ReadString(dialog.SelectedItem, "Код", "code");
            var entryName = ReadString(dialog.SelectedItem, "Наименование", "name");
            var entryCaption = string.IsNullOrWhiteSpace(entryCode)
                ? entryName
                : string.IsNullOrWhiteSpace(entryName)
                    ? entryCode
                    : $"{entryCode} - {entryName}";

            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Вид документа ОС"] = entryId.ToString(),
                ["_FixedAssetMovementTypeTitle"] = entryCaption
            };
        }

        private static bool IsActiveCatalogRow(Dictionary<string, object> row)
        {
            var value = row.GetValueOrDefault("Активен") ?? row.GetValueOrDefault("is_active");
            if (value == null || value == DBNull.Value)
                return true;
            if (value is bool boolValue)
                return boolValue;

            var text = value.ToString()?.Trim();
            return string.IsNullOrEmpty(text) ||
                   text.Equals("Да", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("Активен", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text == "1";
        }

        private static int ReadSortOrder(Dictionary<string, object> row)
        {
            var text = ReadString(row, "Порядок", "sort_order", "Order");
            return int.TryParse(text, out var order) ? order : int.MaxValue;
        }

        private static string ReadString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return string.Empty;
        }
        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            // Для документа "Проводки" используем кастомный диалог
            if (_documentMetadata.Name == "Проводки")
            {
                var dialog = new PostingEditDialog(_documentMetadata, _metadataService);
                dialog.Owner = Window.GetWindow(this);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        dialog,
                        "Добавление проводки") == true)
                {
                    await LoadData();
                    MessageBox.Show("Проводка добавлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else if (_documentMetadata.Name == "Учет движения ОС")
            {
                var initialData = await SelectFixedAssetMovementTypeAsync();
                if (initialData == null)
                {
                    UpdateButtonsState();
                    return;
                }

                var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService, initialData: initialData);
                dialog.Owner = Window.GetWindow(this);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        dialog,
                        $"Добавление: {_documentMetadata.Name}") == true)
                {
                    await _metadataService.CreateDynamicRecordAsync(_documentMetadata.Id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись добавлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService);
                dialog.Owner = Window.GetWindow(this);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        dialog,
                        $"Добавление: {_documentMetadata.Name}") == true)
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
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        dialog,
                        "Редактирование проводки") == true)
                {
                    await LoadData();
                    MessageBox.Show("Проводка обновлена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var dialog = new DynamicDocumentItemDialog(_documentMetadata, _metadataService, id);
                dialog.Owner = Window.GetWindow(this);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        dialog,
                        $"Редактирование: {_documentMetadata.Name}") == true)
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


