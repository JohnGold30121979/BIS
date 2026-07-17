using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
        private Dictionary<string, Dictionary<string, string>> _referenceCache;
        private Dictionary<string, MetadataObject> _catalogsDict;
        private static readonly IValueConverter AccountTypeConverter = new AccountTypeDisplayConverter();
        private static readonly IValueConverter YesNoConverter = new BooleanYesNoDisplayConverter();
        private static readonly IValueConverter LinkFlagConverter = new BooleanPlusDisplayConverter();
        private static readonly IValueConverter ClosingModuleConverter = new ClosingModuleDisplayConverter();
        private static readonly IValueConverter PrintModeConverter = new ChartOfAccountsModeDisplayConverter("Признак печати");
        private static readonly IValueConverter BalanceModeConverter = new ChartOfAccountsModeDisplayConverter("Сохранять остатки");

        public CatalogDataView(MetadataObject catalog, MetadataService metadataService)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _referenceCache = new Dictionary<string, Dictionary<string, string>>();

            TitleText.Text = $"{catalog.Icon} {catalog.Name}";
            DescriptionText.Text = catalog.Description;
            SearchPanel.Visibility = Visibility.Visible;
            SearchBox.ToolTip = IsChartOfAccountsCatalog
                ? "Поиск по коду и наименованию счета"
                : "Поиск по всем видимым колонкам справочника";
            ImportDbfButton.Visibility = CanImportDbf ? Visibility.Visible : Visibility.Collapsed;
            ImportDbfButton.Content = IsPaymentClassificationCatalog
                ? "📥 Загрузить классификацию"
                : "📥 Загрузить DBF";
        }

        private bool IsChartOfAccountsCatalog =>
            string.Equals(_catalog.Name, "План счетов", StringComparison.OrdinalIgnoreCase);

        private bool IsPaymentClassificationCatalog =>
            string.Equals(_catalog.Name, "Классификация платежей", StringComparison.OrdinalIgnoreCase);

        private bool CanImportDbf => IsChartOfAccountsCatalog || IsPaymentClassificationCatalog;

        private bool IsAdvancePaymentsCatalog =>
            string.Equals(_catalog.Name, "Авансовые платежи", StringComparison.OrdinalIgnoreCase);

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
                var visibleFields = GetUniqueCatalogFields();

                // Добавляем колонки
                _dataTable.Columns.Add("Id", typeof(Guid));
                foreach (var field in visibleFields)
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

                    foreach (var field in visibleFields)
                    {
                        var rawValue = row.ContainsKey(field.Name) ? row[field.Name] : DBNull.Value;

                        // Если поле ссылается на справочник - подставляем DisplayName
                        if (!string.IsNullOrEmpty(field.ReferenceCatalog) && _referenceCache.TryGetValue(field.Name, out var dict))
                        {
                            var key = NormalizeReferenceKey(rawValue);
                            dataRow[field.Name] = !string.IsNullOrWhiteSpace(key) && dict.TryGetValue(key, out var displayValue)
                                ? displayValue
                                : rawValue;
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

                foreach (var field in visibleFields)
                {
                    DataGrid.Columns.Add(CreateDataGridColumn(field));
                }

                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = CreateColumnHeader("Дата создания"),
                    Binding = new System.Windows.Data.Binding("Дата создания"),
                    Width = IsChartOfAccountsCatalog || IsAdvancePaymentsCatalog ? 125 : 150,
                    ElementStyle = CreateCellTextStyle()
                });

                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = CreateColumnHeader("Дата изменения"),
                    Binding = new System.Windows.Data.Binding("Дата изменения"),
                    Width = IsChartOfAccountsCatalog || IsAdvancePaymentsCatalog ? 125 : 150,
                    ElementStyle = CreateCellTextStyle()
                });

                DataGrid.ItemsSource = _dataTable.DefaultView;

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
                ProgressText.Text = "";
                EditButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                ApplySearchFilter();
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

            foreach (var field in GetUniqueCatalogFields().Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog)))
            {
                if (!_catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
                    continue;

                var refData = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in refData)
                {
                    // Универсальное форматирование через шаблон из метаданных
                    var displayValue = GetDisplayValueFromRow(row, field, refCatalog.Name);
                    foreach (var key in GetReferenceLookupKeys(row))
                    {
                        if (!dict.ContainsKey(key))
                            dict[key] = displayValue;
                    }
                }

                _referenceCache[field.Name] = dict;
            }
        }

        private List<MetadataField> GetUniqueCatalogFields()
        {
            var allowedColumns = GetAllowedCatalogColumns();
            var result = new List<MetadataField>();
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
            {
                if (allowedColumns != null &&
                    (string.IsNullOrWhiteSpace(field.DbColumnName) || !allowedColumns.Contains(field.DbColumnName)))
                {
                    continue;
                }

                var hasDuplicateColumn = !string.IsNullOrWhiteSpace(field.DbColumnName) &&
                                         !usedColumns.Add(field.DbColumnName);
                var hasDuplicateName = !string.IsNullOrWhiteSpace(field.Name) &&
                                       !usedNames.Add(field.Name);

                if (hasDuplicateColumn || hasDuplicateName)
                    continue;

                result.Add(field);
            }

            return result;
        }

        private HashSet<string>? GetAllowedCatalogColumns()
        {
            if (IsChartOfAccountsCatalog)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "account_type",
                    "description",
                    "level",
                    "is_active",
                    "closing_module_code",
                    "analytic_group",
                    "print_mode",
                    "balance_mode",
                    "link_organizations",
                    "link_employees",
                    "link_currencies",
                    "link_personal_accounts",
                    "link_materials",
                    "link_construction_objects",
                    "link_sites",
                    "tax_code",
                    "account_currency_id"
                };
            }

            if (IsAdvancePaymentsCatalog)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "use_organizations",
                    "use_personnel",
                    "use_currency",
                    "module_code",
                    "debit_account",
                    "credit_account",
                    "use_settlements",
                    "generate_postings",
                    "use_internal_settlements",
                    "is_active",
                    "description"
                };
            }

            return null;
        }

        private static IEnumerable<string> GetReferenceLookupKeys(Dictionary<string, object> row)
        {
            foreach (var keyName in new[] { "Id", "Код", "code", "Code", "Счет", "account_code" })
            {
                var key = NormalizeReferenceKey(row.GetValueOrDefault(keyName));
                if (!string.IsNullOrWhiteSpace(key))
                    yield return key;
            }
        }

        private static string NormalizeReferenceKey(object value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            var text = value.ToString()?.Trim() ?? string.Empty;
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
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

        private DataGridTextColumn CreateDataGridColumn(MetadataField field)
        {
            return new DataGridTextColumn
            {
                Header = CreateColumnHeader(field.Name),
                Binding = CreateColumnBinding(field),
                Width = GetColumnWidth(field),
                MinWidth = GetColumnMinWidth(field),
                ElementStyle = CreateCellTextStyle(GetColumnTextAlignment(field))
            };
        }

        private object CreateColumnHeader(string fieldName)
        {
            if (!IsChartOfAccountsCatalog && !IsAdvancePaymentsCatalog)
                return fieldName;

            return new TextBlock
            {
                Text = IsAdvancePaymentsCatalog
                    ? GetAdvancePaymentsColumnHeader(fieldName)
                    : GetChartOfAccountsColumnHeader(fieldName),
                ToolTip = fieldName,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 14,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(2, 2, 2, 2)
            };
        }

        private DataGridLength GetColumnWidth(MetadataField field)
        {
            if (IsAdvancePaymentsCatalog)
            {
                var advanceWidth = field.Name switch
                {
                    "Код" => 70,
                    "Орг" => 55,
                    "Таб №" => 60,
                    "Валюта" => 70,
                    "Остаток брать из модуля" => 90,
                    "Дебет" => 130,
                    "Кредит" => 130,
                    "Участвует во взаиморасчетах" => 85,
                    "Формировать проводки авансовых платежей" => 85,
                    "Участвует во внутренних взаиморасчетах" => 90,
                    "Вид расчета" => 260,
                    "Активен" => 70,
                    _ => field.FieldType == "Bool" ? 65 : 120
                };

                return new DataGridLength(advanceWidth, DataGridLengthUnitType.Pixel);
            }

            if (!IsChartOfAccountsCatalog)
                return new DataGridLength(1, DataGridLengthUnitType.Star);

            var width = field.Name switch
            {
                "Код" => 78,
                "Наименование" => 185,
                "Тип счета" => 96,
                "Описание" => 150,
                "Уровень" => 48,
                "Активен" => 58,
                "Закрывает модуль" => 78,
                "Группа аналитических статей" => 92,
                "Признак печати" => 72,
                "Сохранять остатки" => 82,
                "Связь с организациями" => 48,
                "Связь со списочным составом" => 48,
                "Связь с валютами" => 48,
                "Связь с лицевыми счетами" => 56,
                "Связь с материалами" => 56,
                "Связь с объектами строительства" => 62,
                "Связь с участками" => 52,
                "Код налога" => 58,
                "Валюта счета" => 92,
                _ => field.FieldType == "Bool" ? 48 : 96
            };

            return new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }

        private double GetColumnMinWidth(MetadataField field)
        {
            if (IsAdvancePaymentsCatalog)
                return field.FieldType == "Bool" ? 45 : 70;

            if (!IsChartOfAccountsCatalog)
                return 100;

            return field.Name switch
            {
                "Код" => 62,
                "Наименование" => 135,
                "Тип счета" => 80,
                "Описание" => 105,
                "Уровень" => 40,
                "Активен" => 50,
                "Закрывает модуль" => 68,
                "Группа аналитических статей" => 80,
                "Признак печати" => 62,
                "Сохранять остатки" => 68,
                "Код налога" => 52,
                "Валюта счета" => 78,
                _ => field.FieldType == "Bool" ? 40 : 56
            };
        }

        private TextAlignment GetColumnTextAlignment(MetadataField field)
        {
            if (IsAdvancePaymentsCatalog)
                return field.FieldType == "Bool" || field.Name == "Код"
                    ? TextAlignment.Center
                    : TextAlignment.Left;

            if (!IsChartOfAccountsCatalog)
                return TextAlignment.Left;

            return field.FieldType == "Bool" || field.Name == "Уровень"
                ? TextAlignment.Center
                : TextAlignment.Left;
        }

        private Style CreateCellTextStyle(TextAlignment textAlignment = TextAlignment.Left)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0)));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, textAlignment));
            return style;
        }

        private string GetChartOfAccountsColumnHeader(string fieldName)
        {
            return fieldName switch
            {
                "Тип счета" => "Тип\nсчета",
                "Закрывает модуль" => "Закрывает\nмодуль",
                "Группа аналитических статей" => "Группа\nаналитики",
                "Признак печати" => "Признак\nпечати",
                "Сохранять остатки" => "Сохр.\nостатки",
                "Связь с организациями" => "Связь\nс орг.",
                "Связь со списочным составом" => "Таб.\n№",
                "Связь с валютами" => "Связь\nс вал.",
                "Связь с лицевыми счетами" => "Лиц.\nсчета",
                "Связь с материалами" => "Матер.\nучет",
                "Связь с объектами строительства" => "Объекты\nстр-ва",
                "Связь с участками" => "Участки",
                "Код налога" => "Код\nналога",
                "Валюта счета" => "Валюта\nсчета",
                "Дата создания" => "Дата\nсоздания",
                "Дата изменения" => "Дата\nизменения",
                _ => fieldName
            };
        }

        private string GetAdvancePaymentsColumnHeader(string fieldName)
        {
            return fieldName switch
            {
                "Остаток брать из модуля" => "Модуль",
                "Участвует во взаиморасчетах" => "Разн. расч.",
                "Формировать проводки авансовых платежей" => "Анал. плат.",
                "Участвует во внутренних взаиморасчетах" => "Внут. расч.",
                "Дата создания" => "Создан",
                "Дата изменения" => "Изменен",
                _ => fieldName
            };
        }

        private Binding CreateColumnBinding(MetadataField field)
        {
            var binding = new Binding(field.Name);

            if (IsChartOfAccountsCatalog && field.Name == "Тип счета")
            {
                binding.Converter = AccountTypeConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Закрывает модуль")
            {
                binding.Converter = ClosingModuleConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Признак печати")
            {
                binding.Converter = PrintModeConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Сохранять остатки")
            {
                binding.Converter = BalanceModeConverter;
            }
            else if (IsAdvancePaymentsCatalog && field.FieldType == "Bool" && field.Name != "Активен")
            {
                binding.Converter = LinkFlagConverter;
            }
            else if (field.FieldType == "Bool" && (!IsChartOfAccountsCatalog || field.Name == "Активен"))
            {
                binding.Converter = YesNoConverter;
            }
            else if (IsChartOfAccountsCatalog && field.FieldType == "Bool")
            {
                binding.Converter = LinkFlagConverter;
            }

            return binding;
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (_dataTable?.DefaultView == null)
                return;

            var searchText = SearchBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _dataTable.DefaultView.RowFilter = string.Empty;
                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
                return;
            }

            var escapedValue = EscapeRowFilterValue(searchText);
            var filterParts = _dataTable.Columns
                .Cast<DataColumn>()
                .Where(column => !column.ColumnName.Equals("Id", StringComparison.OrdinalIgnoreCase))
                .Select(column => $"CONVERT([{EscapeColumnName(column.ColumnName)}], 'System.String') LIKE '%{escapedValue}%'")
                .ToList();

            _dataTable.DefaultView.RowFilter = string.Join(" OR ", filterParts);

            StatusText.Text = IsChartOfAccountsCatalog
                ? $"🔍 Найдено счетов: {_dataTable.DefaultView.Count}"
                : $"🔍 Найдено записей: {_dataTable.DefaultView.Count}";
        }

        private static string EscapeColumnName(string value) =>
            value.Replace("]", "]]");

        private static string EscapeRowFilterValue(string value)
        {
            return value
                .Replace("'", "''")
                .Replace("[", "[[]")
                .Replace("]", "[]]")
                .Replace("%", "[%]")
                .Replace("*", "[*]");
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

        private async void OnImportDbfClick(object sender, RoutedEventArgs e)
        {
            if (!CanImportDbf)
                return;

            var openDialog = CreateDbfOpenDialog();

            if (openDialog.ShowDialog() != true)
                return;

            try
            {
                ProgressText.Text = "⏳ Импорт DBF...";
                StatusText.Text = IsPaymentClassificationCatalog
                    ? "Загрузка классификации платежей из Fox DBF..."
                    : "Загрузка плана счетов из Fox DBF...";

                if (IsPaymentClassificationCatalog)
                {
                    var result = await _metadataService.ImportPaymentClassificationsFromDbfAsync(openDialog.FileName);
                    await LoadData();

                    MessageBox.Show(
                        BuildPaymentClassificationImportMessage(result),
                        "Импорт классификации платежей",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    var result = await _metadataService.ImportChartOfAccountsFromDbfAsync(openDialog.FileName);
                    await LoadData();

                    MessageBox.Show(
                        BuildChartOfAccountsImportMessage(result),
                        "Импорт плана счетов",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка загрузки DBF: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ProgressText.Text = string.Empty;
                StatusText.Text = "✅ Готово";
            }
        }

        private OpenFileDialog CreateDbfOpenDialog()
        {
            if (IsPaymentClassificationCatalog)
            {
                return new OpenFileDialog
                {
                    Title = "Выберите DBF файл классификации платежей",
                    Filter = "DBF файлы (*.dbf)|*.dbf|Все файлы (*.*)|*.*",
                    FileName = "VID_PL.DBF"
                };
            }

            return new OpenFileDialog
            {
                Title = "Выберите DBF файл плана счетов",
                Filter = "Файл плана счетов (BUXSCH.DBF)|BUXSCH.DBF|DBF файлы (*.dbf)|*.dbf|Все файлы (*.*)|*.*",
                FileName = "BUXSCH.DBF"
            };
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

        private static string BuildChartOfAccountsImportMessage(ChartOfAccountsDbfImportResult result)
        {
            var mappedFields = string.Join(Environment.NewLine,
                result.FieldMappings.Select(item => $"• {item.SourceField} -> {item.TargetField}"));

            var ignoredFields = result.IgnoredSourceFields.Count == 0
                ? "нет"
                : string.Join(", ", result.IgnoredSourceFields);

            return
                $"Файл: {result.SourcePath}{Environment.NewLine}{Environment.NewLine}" +
                $"Обработано записей: {result.SourceRecordCount}{Environment.NewLine}" +
                $"Уникальных счетов: {result.LoadedAccountsCount}{Environment.NewLine}" +
                $"Добавлено: {result.InsertedCount}{Environment.NewLine}" +
                $"Обновлено: {result.UpdatedCount}{Environment.NewLine}" +
                $"Переведено в неактивные: {result.DeactivatedCount}{Environment.NewLine}" +
                $"Повторов кодов в источнике: {result.DuplicateSourceCodesCount}{Environment.NewLine}{Environment.NewLine}" +
                $"Разобранные поля DBF:{Environment.NewLine}{mappedFields}{Environment.NewLine}{Environment.NewLine}" +
                $"Поля Fox без прямой загрузки в текущий набор реквизитов:{Environment.NewLine}{ignoredFields}";
        }

        private static string BuildPaymentClassificationImportMessage(PaymentClassificationDbfImportResult result)
        {
            var mappedFields = string.Join(Environment.NewLine,
                result.FieldMappings.Select(item => $"• {item.SourceField} -> {item.TargetField}"));

            var ignoredFields = result.IgnoredSourceFields.Count == 0
                ? "нет"
                : string.Join(", ", result.IgnoredSourceFields);

            return
                $"Файл: {result.SourcePath}{Environment.NewLine}{Environment.NewLine}" +
                $"Обработано записей: {result.SourceRecordCount}{Environment.NewLine}" +
                $"Загружено кодов платежей: {result.LoadedItemsCount}{Environment.NewLine}" +
                $"Добавлено: {result.InsertedCount}{Environment.NewLine}" +
                $"Обновлено: {result.UpdatedCount}{Environment.NewLine}" +
                $"Переведено в неактивные: {result.DeactivatedCount}{Environment.NewLine}" +
                $"Повторов кодов в источнике: {result.DuplicateSourceCodesCount}{Environment.NewLine}{Environment.NewLine}" +
                $"Разобранные поля DBF:{Environment.NewLine}{mappedFields}{Environment.NewLine}{Environment.NewLine}" +
                $"Поля Fox без прямой загрузки в текущий набор реквизитов:{Environment.NewLine}{ignoredFields}";
        }

        private sealed class AccountTypeDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return LocalizationService.DisplayValue(value);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class BooleanPlusDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value switch
                {
                    true => "+",
                    false => string.Empty,
                    _ => string.Empty
                };
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class BooleanYesNoDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return LocalizationService.DisplayValue(value);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ClosingModuleDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName(value?.ToString());
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ChartOfAccountsModeDisplayConverter : IValueConverter
        {
            private readonly string _fieldName;

            public ChartOfAccountsModeDisplayConverter(string fieldName)
            {
                _fieldName = fieldName;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return ChartOfAccountsSelectionMetadata.GetModeDisplay(_fieldName, value?.ToString());
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
