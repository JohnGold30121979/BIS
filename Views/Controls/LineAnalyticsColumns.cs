using BIS.ERP.Models;
using BIS.ERP.Services;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views.Controls
{
    /// <summary>Колонки справочников, привязанных к счёту каждой строки документа.</summary>
    public sealed class LineAnalyticsColumns
    {
        private readonly DataGrid _grid;
        private readonly MetadataService _metadata;
        private readonly AccountAnalyticsRegistry _registry;
        private readonly Func<ILineAnalyticsRow, object?> _account;
        private readonly IEnumerable _rows;
        private readonly bool _readOnly;
        private readonly Dictionary<string, DataGridColumn> _columns = new();
        private readonly Dictionary<string, Task<List<Dictionary<string, object>>>> _lookups = new();
        private readonly HashSet<INotifyPropertyChanged> _subscribed = new();
        private readonly List<MetadataObject> _catalogs;
        private readonly string _accountProperty;
        private readonly HashSet<string> _nativeLinks;
        private (ILineAnalyticsRow Row, AccountAnalyticsLinkDefinition Definition)? _pendingPicker;
        private bool _pickerOpen;

        public LineAnalyticsColumns(DataGrid grid, MetadataService metadata, AccountAnalyticsRegistry registry,
            List<MetadataObject> catalogs, IEnumerable rows, Func<ILineAnalyticsRow, object?> account,
            string accountProperty, bool readOnly = false, IEnumerable<string>? nativeLinks = null,
            DataGridColumn? afterColumn = null, IReadOnlyDictionary<string, DataGridColumn>? nativeColumns = null)
        {
            _grid = grid;
            _metadata = metadata;
            _registry = registry;
            _catalogs = catalogs;
            _rows = rows;
            _account = account;
            _accountProperty = accountProperty;
            _readOnly = readOnly;
            _nativeLinks = new HashSet<string>(nativeLinks ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (nativeColumns != null)
                foreach (var (code, column) in nativeColumns)
                    _columns[code] = column;
            int? nextDisplayIndex = afterColumn == null ? null :
                (afterColumn.DisplayIndex >= 0 ? afterColumn.DisplayIndex : grid.Columns.IndexOf(afterColumn)) + 1;
            foreach (var definition in registry.Definitions.Where(item => item.IsActive &&
                         !string.IsNullOrWhiteSpace(item.ReferenceCatalog) && !_nativeLinks.Contains(item.Code) &&
                         !_columns.ContainsKey(item.Code)).DistinctBy(item => item.Code))
            {
                var column = new DataGridTemplateColumn
                {
                    Header = definition.ReferenceCatalog,
                    Width = 220,
                    CanUserSort = false,
                    CellTemplate = CreateTemplate(definition),
                    Visibility = Visibility.Collapsed,
                    IsReadOnly = readOnly
                };
                _columns[definition.Code] = column;
                grid.Columns.Add(column);
                if (nextDisplayIndex.HasValue)
                {
                    column.DisplayIndex = nextDisplayIndex.Value;
                    nextDisplayIndex++;
                }
            }
            if (rows is INotifyCollectionChanged collection)
                collection.CollectionChanged += (_, _) => Refresh();
            grid.PreviewMouseLeftButtonDown += OnPickerMouseDown;
            grid.PreviewMouseLeftButtonUp += OnPickerMouseUp;
            Refresh();
        }

        private void OnPickerMouseDown(object sender, MouseButtonEventArgs e)
        {
            _pendingPicker = null;
            var element = e.OriginalSource as DependencyObject;
            while (element != null && element is not Button)
                element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
            if (!_readOnly && element is Button { Tag: AccountAnalyticsLinkDefinition definition,
                    DataContext: ILineAnalyticsRow row })
            {
                _pendingPicker = (row, definition);
                e.Handled = true;
            }
        }

        private async void OnPickerMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_pendingPicker is not { } pending) return;
            _pendingPicker = null;
            e.Handled = true;
            try { await PickAsync(pending.Row, pending.Definition); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Выбор аналитики", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        public void Refresh()
        {
            var rows = _rows.OfType<ILineAnalyticsRow>().ToList();
            foreach (var old in _subscribed.Where(item => !rows.Any(row => ReferenceEquals(row, item))).ToList())
            {
                old.PropertyChanged -= OnRowChanged;
                _subscribed.Remove(old);
            }
            foreach (var row in rows)
            {
                if (row is INotifyPropertyChanged changed && _subscribed.Add(changed))
                    changed.PropertyChanged += OnRowChanged;
                var settings = _registry.GetSettingsFromValue(_account(row));
                foreach (var definition in _registry.Definitions.Where(item => _columns.ContainsKey(item.Code)))
                    row.Analytics[definition.Code].IsApplicable = settings?.Allows(definition) == true;
            }
            foreach (var (code, column) in _columns)
                column.Visibility = rows.Any(row => row.Analytics[code].IsApplicable)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _accountProperty || string.IsNullOrEmpty(e.PropertyName))
                Refresh();
        }

        public bool IsAvailable(object? row, DataGridColumn column)
        {
            var code = _columns.FirstOrDefault(pair => ReferenceEquals(pair.Value, column)).Key;
            return code == null || row is ILineAnalyticsRow analyticRow && analyticRow.Analytics[code].IsApplicable;
        }

        private DataTemplate CreateTemplate(AccountAnalyticsLinkDefinition definition)
        {
            var path = $"Analytics[{definition.Code}]";
            var panel = new FrameworkElementFactory(typeof(DockPanel));
            panel.SetBinding(UIElement.VisibilityProperty, new Binding(path + ".IsApplicable")
            {
                Converter = new BooleanToVisibilityConverter()
            });
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(ContentControl.ContentProperty, "?");
            button.SetValue(FrameworkElement.WidthProperty, 24d);
            button.SetValue(FrameworkElement.TagProperty, definition);
            button.SetValue(DockPanel.DockProperty, Dock.Right);
            button.SetValue(UIElement.IsEnabledProperty, !_readOnly);
            button.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (sender, _) =>
            {
                if (sender is not FrameworkElement { DataContext: ILineAnalyticsRow row } || _readOnly)
                    return;
                try { await PickAsync(row, definition); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Выбор аналитики", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }));
            panel.AppendChild(button);
            var text = new FrameworkElementFactory(typeof(TextBox));
            text.SetValue(TextBox.IsReadOnlyProperty, _readOnly);
            text.SetValue(FrameworkElement.ToolTipProperty, "Введите номер/код из справочника или нажмите «?»");
            text.SetBinding(TextBox.TextProperty, new Binding(path + ".Text")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            // Enter обрабатывается таблицей; разрешение кода выполняется также перед сохранением.
            text.AddHandler(UIElement.LostFocusEvent, new RoutedEventHandler(async (sender, _) =>
            {
                if (sender is not FrameworkElement { DataContext: ILineAnalyticsRow row } || _readOnly)
                    return;
                var value = row.Analytics[definition.Code];
                var input = value.Text;
                try { await ResolveAsync(value, definition, input); }
                catch { /* Ошибка справочника будет показана при сохранении. Введённый текст остаётся. */ }
            }));
            panel.AppendChild(text);
            return new DataTemplate { VisualTree = panel };
        }

        private MetadataObject Catalog(AccountAnalyticsLinkDefinition definition) =>
            _catalogs.FirstOrDefault(item => item.Name.Equals(definition.ReferenceCatalog, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Справочник «{definition.ReferenceCatalog}» не найден.");

        private Task<List<Dictionary<string, object>>> Lookup(AccountAnalyticsLinkDefinition definition)
        {
            if (!_lookups.TryGetValue(definition.ReferenceCatalog, out var task) || task.IsFaulted)
                _lookups[definition.ReferenceCatalog] = task = _metadata.GetCatalogDataAsync(Catalog(definition).Id);
            return task;
        }

        private static string Read(Dictionary<string, object> row, params string[] keys) =>
            keys.Select(key => row.GetValueOrDefault(key)?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

        private string FieldText(Dictionary<string, object> row, AccountAnalyticsLinkDefinition definition, params string[] columns)
        {
            var fields = _catalogs.FirstOrDefault(item => item.Name.Equals(definition.ReferenceCatalog, StringComparison.OrdinalIgnoreCase))?.Fields;
            foreach (var column in columns)
            {
                var field = fields?.FirstOrDefault(item => item.DbColumnName.Equals(column, StringComparison.OrdinalIgnoreCase));
                if (field != null && !string.IsNullOrWhiteSpace(Read(row, field.Name, field.DbColumnName)))
                    return Read(row, field.Name, field.DbColumnName);
            }
            return string.Empty;
        }

        private string Code(Dictionary<string, object> row, AccountAnalyticsLinkDefinition definition)
        {
            var code = FieldText(row, definition, "personnel_number", "code", "number", "account_number");
            return string.IsNullOrWhiteSpace(code) ? Read(row, "Табельный номер", "personnel_number", "Код", "code", "Номер", "number", "Номер лицевого счета", "Лицевой счет") : code;
        }
        private string Name(Dictionary<string, object> row, AccountAnalyticsLinkDefinition definition)
        {
            var name = FieldText(row, definition, "full_name", "name", "description");
            return string.IsNullOrWhiteSpace(name) ? Read(row, "Наименование", "name", "ФИО", "full_name", "Наименование материала", "Наименование участка") : name;
        }

        private void Select(LineAnalyticValue value, Dictionary<string, object> row, AccountAnalyticsLinkDefinition definition)
        {
            if (!Guid.TryParse(Read(row, "Id", "id"), out var id))
                throw new InvalidOperationException("У выбранной записи справочника отсутствует идентификатор.");
            value.Select(id, Code(row, definition), Name(row, definition));
        }

        private async Task<bool> ResolveAsync(LineAnalyticValue value, AccountAnalyticsLinkDefinition definition, string input)
        {
            if (!value.IsApplicable || string.IsNullOrWhiteSpace(input)) return true;
            var rows = await Lookup(definition);
            // Пользователь мог продолжить ввод или выбрать другой счёт, пока загружался справочник.
            if (!value.IsApplicable) return true;
            if (value.Text != input)
                return value.Id.HasValue || string.IsNullOrWhiteSpace(value.Text);
            var matches = rows.Where(row => value.Id.HasValue
                ? Read(row, "Id", "id").Equals(value.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                : Code(row, definition).Equals(input.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  Read(row, "Id", "id").Equals(input.Trim(), StringComparison.OrdinalIgnoreCase) ||
                  $"{Code(row, definition)} - {Name(row, definition)}".Equals(input.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count != 1) return false;
            Select(value, matches[0], definition);
            return true;
        }

        private async Task PickAsync(ILineAnalyticsRow row, AccountAnalyticsLinkDefinition definition)
        {
            var value = row.Analytics[definition.Code];
            if (!value.IsApplicable || _pickerOpen) return;
            _pickerOpen = true;
            try
            {
                var rows = await Lookup(definition);
                var picker = new ReferenceSelectionDialog(rows);
                picker.ConfigureCatalogEditing(Catalog(definition), _metadata);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(Window.GetWindow(_grid), picker,
                        $"Выбор: {definition.ReferenceCatalog}") == true && picker.SelectedItem != null && value.IsApplicable)
                {
                    Select(value, picker.SelectedItem, definition);
                    _lookups.Remove(definition.ReferenceCatalog);
                }
            }
            finally { _pickerOpen = false; }
        }

        public async Task ValidateAsync()
        {
            Refresh();
            var index = 0;
            foreach (var row in _rows.OfType<ILineAnalyticsRow>())
            {
                index++;
                foreach (var definition in _registry.Definitions.Where(item => _columns.ContainsKey(item.Code)))
                {
                    var value = row.Analytics[definition.Code];
                    if (!await ResolveAsync(value, definition, value.Text))
                        throw new InvalidOperationException($"Строка {index}, «{definition.ReferenceCatalog}»: укажите точный номер/код или выберите запись через «?».");
                }
            }
        }
    }
}
