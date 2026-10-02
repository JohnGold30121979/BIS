using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class ReferenceSelectionDialog : Window
    {
        private static readonly IValueConverter GridValueConverter = new ReferenceGridValueConverter();
        private readonly List<Dictionary<string, object>> _items;
        private readonly string _firstField;
        private readonly string _secondField;
        private readonly IReadOnlyDictionary<string, Dictionary<Guid, string>>? _referenceMaps;
        private List<Dictionary<string, object>> _filteredItems;
        private Func<Dictionary<string, object>?, Task>? _extraAction;
        private MetadataObject? _catalog;
        private MetadataService? _metadataService;

        public Dictionary<string, object> SelectedItem { get; private set; }

        public ReferenceSelectionDialog(
            List<Dictionary<string, object>> items,
            string firstField = null,
            string secondField = null,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps = null)
        {
            InitializeComponent();
            var sourceItems = items ?? new List<Dictionary<string, object>>();
            _referenceMaps = referenceMaps;
            _items = referenceMaps != null && referenceMaps.Count > 0
                ? ReferenceDisplayHelper.ResolveRows(sourceItems, referenceMaps)
                : sourceItems;
            _firstField = firstField;
            _secondField = secondField;
            _filteredItems = _items;

            RebuildGrid();
        }

        /// <summary>
        /// Включает кнопки «Добавить» и «Изменить», позволяя создавать и
        /// редактировать записи нужного справочника прямо из окна выбора.
        /// </summary>
        public void ConfigureCatalogEditing(MetadataObject catalog, MetadataService metadataService)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));

            AddButton.Visibility = Visibility.Visible;
            EditButton.Visibility = Visibility.Visible;
            AddButton.IsEnabled = true;
            EditButton.IsEnabled = ItemsGrid.SelectedItem != null;
        }

        private void RebuildGrid()
        {
            if (_items.Count > 0)
            {
                // Определяем все возможные ключи (поля)
                var allKeys = _items.SelectMany(d => d.Keys).Distinct().ToList();
                // Исключаем служебные поля
                var excludeKeys = new[] { "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "IsDeleted" };
                var displayKeys = allKeys.Where(k => !excludeKeys.Contains(k) && k != "Id").ToList();

                // Если заданы поля для отображения, используем их в первую очередь
                if (!string.IsNullOrEmpty(_firstField) && displayKeys.Contains(_firstField))
                {
                    // Помещаем firstField на первое место
                    displayKeys.Remove(_firstField);
                    displayKeys.Insert(0, _firstField);
                }
                if (!string.IsNullOrEmpty(_secondField) && displayKeys.Contains(_secondField))
                {
                    // Помещаем secondField на второе место, если оно не firstField
                    if (displayKeys.Contains(_secondField) && _secondField != _firstField)
                    {
                        displayKeys.Remove(_secondField);
                        if (displayKeys.Count > 0 && displayKeys[0] == _firstField)
                            displayKeys.Insert(1, _secondField);
                        else
                            displayKeys.Insert(0, _secondField);
                    }
                }

                // Добавляем колонки в DataGrid
                ItemsGrid.AutoGenerateColumns = false;
                ItemsGrid.Columns.Clear();

                foreach (var key in displayKeys)
                {
                    ItemsGrid.Columns.Add(CreateColumn(
                        key,
                        key == _secondField,
                        IsAccountColumn(key),
                        IsLogicalColumn(_items, key)));
                }

                // Если нет колонок, добавляем колонку "Id"
                if (ItemsGrid.Columns.Count == 0)
                {
                    ItemsGrid.Columns.Add(CreateColumn("Id", isName: true, isAccount: false, isLogical: false));
                }
            }
            else
            {
                ItemsGrid.AutoGenerateColumns = false;
                ItemsGrid.Columns.Clear();

                foreach (var key in new[] { _firstField, _secondField }
                             .Where(key => !string.IsNullOrWhiteSpace(key))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ItemsGrid.Columns.Add(CreateColumn(
                        key,
                        key == _secondField,
                        IsAccountColumn(key),
                        IsLogicalColumn(_items, key)));
                }

                if (ItemsGrid.Columns.Count == 0)
                {
                    ItemsGrid.Columns.Add(CreateColumn("Id", isName: true, isAccount: false, isLogical: false));
                }
            }

            ItemsGrid.ItemsSource = _filteredItems;
        }
public void ConfigureExtraAction(string caption, Func<Dictionary<string, object>?, Task> action, string tooltip = null)
        {
            _extraAction = action ?? throw new ArgumentNullException(nameof(action));
            ExtraActionButton.Content = caption;
            ExtraActionButton.ToolTip = tooltip;
            ExtraActionButton.Visibility = Visibility.Visible;
            ExtraActionButton.IsEnabled = ItemsGrid.SelectedItem != null;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            _filteredItems = FilterItems(search);
            // Обновление источника — только через UI-очередь и с полной
            // заменой коллекции (reset), а не мутацией привязанного списка.
            ItemsGrid.ItemsSource = null;
            ItemsGrid.ItemsSource = _filteredItems;
        }

        private void ApplySort()
        {
            // Сортировка отключена: порядок задаётся уже отфильтрованным списком.
        }

        private List<Dictionary<string, object>> FilterItems(string search)
        {
            if (string.IsNullOrEmpty(search))
                // ВАЖНО: возвращаем НОВЫЙ список, а не ссылку на _items.
                // Мутация общего экземпляра, привязанного к ItemsGrid, без уведомлений
                // коллекции рассинхронизирует генератор DataGrid (лог 09:49).
                return new List<Dictionary<string, object>>(_items);

            return _items
                .Where(item => item.Values.Any(v => v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = ItemsGrid.SelectedItem != null;
            ExtraActionButton.IsEnabled = ExtraActionButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
            EditButton.IsEnabled = EditButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
        }

        private void ItemsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectItem();
        }

        private async void OnExtraActionClick(object sender, RoutedEventArgs e)
        {
            if (_extraAction == null)
                return;

            if (ItemsGrid.SelectedItem is not Dictionary<string, object> selectedItem)
            {
                MessageBox.Show("Сначала выберите запись справочника.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ExtraActionButton.IsEnabled = false;
                await _extraAction(selectedItem);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выполнения действия: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ExtraActionButton.IsEnabled = ExtraActionButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
            }
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (_catalog == null || _metadataService == null)
                return;

            try
            {
                var dialog = new CatalogItemDialog(_catalog, _metadataService);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, $"Добавление: {_catalog.Name}") != true)
                    return;

                Cursor = Cursors.Wait;
                var newId = await _metadataService.CreateDynamicRecordAsync(_catalog.Id, dialog.ItemData);
                await ReloadItemsAsync(newId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления записи: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }
private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (_catalog == null || _metadataService == null)
                return;

            var selectedItem = ItemsGrid.SelectedItem as Dictionary<string, object>;
            if (selectedItem == null)
            {
                MessageBox.Show("Сначала выберите запись для редактирования.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var recordId = GetRecordId(selectedItem);
            if (!recordId.HasValue)
            {
                MessageBox.Show("У выбранной записи не найден идентификатор.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Всегда берём свежие «сырые» данные из БД, чтобы поля-ссылки
                // не оказались подменены отображаемыми значениями при сохранении.
                var rawRows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
                var existingData = rawRows.FirstOrDefault(row => GetRecordId(row) == recordId);
                if (existingData == null)
                {
                    MessageBox.Show("Выбранная запись справочника не найдена.", _catalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(_catalog, _metadataService, existingData);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, $"Редактирование: {_catalog.Name}") != true)
                    return;

                Cursor = Cursors.Wait;
                await _metadataService.UpdateDynamicRecordAsync(_catalog.Id, recordId.Value, dialog.ItemData);
                await ReloadItemsAsync(recordId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления записи: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async Task ReloadItemsAsync(Guid? selectRecordId = null)
        {
            if (_catalog == null || _metadataService == null)
                return;

            var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
            var reloaded = _referenceMaps != null && _referenceMaps.Count > 0
                ? ReferenceDisplayHelper.ResolveRows(rows, _referenceMaps)
                : rows;

            // Безопасное обновление ItemsGrid через одну UI-очередь:
            // сбрасываем привязку, затем заменяем содержимое новым списком.
            // Мутация привязанного списка по месту рассинхронизирует
            // генератор DataGrid (лог 09:49).
            await Dispatcher.InvokeAsync(() =>
            {
                ItemsGrid.ItemsSource = null;
                _items.Clear();
                foreach (var row in reloaded)
                    _items.Add(row);

                var search = SearchBox.Text?.Trim() ?? string.Empty;
                _filteredItems = FilterItems(search);
                ApplySort();
                ItemsGrid.ItemsSource = _filteredItems;

                if (selectRecordId.HasValue)
                {
                    var target = _filteredItems.FirstOrDefault(row => GetRecordId(row) == selectRecordId);
                    if (target != null)
                    {
                        ItemsGrid.SelectedItem = target;
                        ItemsGrid.ScrollIntoView(target);
                    }
                }
            });
        }

        private static Guid? GetRecordId(Dictionary<string, object> row)
        {
            if (row.TryGetValue("Id", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
                return id;

            return null;
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            SelectItem();
        }

        private void SelectItem()
        {
            SelectedItem = ItemsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedItem != null)
            {
                MdiDialogService.CloseWithResult(this, true);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            MdiDialogService.CloseWithResult(this, false);
        }

/// <summary>
        /// Ширина колонки. Логические признаки ужимаются до ширины «Да»/«Нет»,
        /// «Вид расчета» и счета дебета/кредита забирают всю свободную ширину
        /// окна, остальные поля получают умеренный размер по содержимому.
        ///
        /// Ширины подобраны по образцу рабочего справочника «Пары счетов»:
        /// там логические признаки занимают 40–62 px, а «Вид расчета» 220 px.
        /// Раньше все колонки имели ширину Auto по содержимому, из-за чего
        /// заголовок «Формировать проводки авансовых платежей» растягивал
        /// колонку на всю ширину, а сумма ширин не помещалась в окно —
        /// колонки не менялись при растягивании окна мышью.
        /// </summary>
        private static DataGridTextColumn CreateColumn(
            string key,
            bool isName,
            bool isAccount,
            bool isLogical)
        {
            return new DataGridTextColumn
            {
                Header = isLogical ? GetCompactHeader(key) : key,
                Width = isName || isAccount
                    ? new DataGridLength(isName ? 3 : 2, DataGridLengthUnitType.Star)
                    : new DataGridLength(isLogical ? LogicalColumnWidth : GetFixedColumnWidth(key)),
                MinWidth = isName ? 180 : isAccount ? 140 : isLogical ? 40 : 70,
                CanUserSort = true,
                CanUserResize = true,
                CanUserReorder = true,
                Binding = CreateValueBinding(key)
            };
        }

        /// <summary>
        /// Ширина логической колонки. Заголовки сокращены до «Разн. расч.»,
        /// «Анал. плат.», «Внт. рач.», поэтому 72 px хватает и на заголовок,
        /// и на значение «Да»/«Нет».
        /// </summary>
        private const double LogicalColumnWidth = 72;

        private static double GetFixedColumnWidth(string key)
        {
            return key switch
            {
                "Код" or "code" => 56,
                "Остаток брать из модуля" => 78,
                "Табельный номер" => 92,
                _ => 110
            };
        }

        /// <summary>
        /// Короткие заголовки длинных логических полей — те же сокращения,
        /// что и в справочнике, чтобы колонка «Да/Нет» не раздувалась
        /// под длинный заголовок.
        /// </summary>
        private static string GetCompactHeader(string key) => key switch
        {
            "Участвует во взаиморасчетах" => "Разн. расч.",
            "Формировать проводки авансовых платежей" => "Анал. плат.",
            "Участвует во внутренних взаиморасчетах" => "Внт. рач.",
            "Остаток брать из модуля" => "Модуль",
            "Организации" => "Орг",
            "Сотрудники" => "Сотр.",
            "Валютный учет" or "Валютный учёт" => "Вал. учёт",
            _ => key
        };

        private static bool IsAccountColumn(string key) =>
            key is "Дебет" or "Кредит" or "Счет дебета" or "Счет кредита" or "Счёт дебета" or "Счёт кредита" or "Корр счет";

        /// <summary>
        /// Логическое поле — во всех строках только «Да»/«Нет». Такие колонки
        /// не должны занимать ширину по длине заголовка.
        /// </summary>
        private static bool IsLogicalColumn(IEnumerable<Dictionary<string, object>> items, string key)
        {
            var hasValue = false;
            foreach (var item in items)
            {
                if (!item.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
                    continue;

                hasValue = true;
                if (value is bool)
                    continue;

                if (value is string text)
                {
                    if (text.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals("False", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                return false;
            }

            return hasValue;
        }

        private static Binding CreateValueBinding(string key)
        {
            return new Binding($"[{key}]")
            {
                Converter = GridValueConverter,
                ConverterParameter = key
            };
        }

        private sealed class ReferenceGridValueConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var key = parameter as string ?? string.Empty;
                var current = value?.ToString();

                if (current == null)
                    return "—";

                if (current.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    current.Equals("False", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Equals("True", StringComparison.OrdinalIgnoreCase)
                        ? (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase) ? "Активен" : "Да")
                        : (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase) ? "Неактивен" : "Нет");
                }

                return current;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}