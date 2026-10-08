using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Dialogs
{
    /// <summary>
    /// Ввод значений параметров отчёта перед формированием.
    /// Диалог ничего не знает про SQL: он отдаёт словарь значений, который
    /// ReportService подставляет в фильтры отчёта.
    /// </summary>
    public partial class ReportParametersDialog : Window
    {
        private readonly Report _report;
        private readonly AppDbContext? _context;
        private readonly List<ParameterRow> _rows = new();

        public ReportParametersDialog(
            Report report,
            IReadOnlyDictionary<string, string>? savedValues = null,
            AppDbContext? context = null)
        {
            InitializeComponent();
            _report = report;
            _context = context;

            ReportNameText.Text = string.IsNullOrWhiteSpace(report.Name)
                ? "Отчет"
                : report.Name;

            BuildRows(savedValues);
        }

        /// <summary>Значения, введённые пользователем.</summary>
        public Dictionary<string, object> Values { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Просил ли пользователь запомнить введённые значения.</summary>
        public bool RememberValues => RememberCheckBox.IsChecked == true;

        /// <summary>Пользователь запросил сброс сохранённых значений.</summary>
        public bool ResetRequested { get; private set; }

        private void BuildRows(IReadOnlyDictionary<string, string>? savedValues)
        {
            ParametersHost.RowDefinitions.Clear();
            _rows.Clear();

            var parameters = (_report.Parameters ?? new List<ReportParameter>())
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                .OrderBy(parameter => parameter.Order)
                .ThenBy(parameter => parameter.Title)
                .ToList();

            if (parameters.Count == 0)
            {
                HintText.Text = "У отчета нет параметров.";
                ApplyButton.IsEnabled = false;
                return;
            }

            var rowIndex = 0;
            foreach (var parameter in parameters)
            {
                ParametersHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = parameter.DisplayTitle,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 12, 6),
                    TextWrapping = TextWrapping.Wrap
                };
                if (parameter.IsRequired)
                    label.FontWeight = FontWeights.SemiBold;

                Grid.SetRow(label, rowIndex);
                Grid.SetColumn(label, 0);
                ParametersHost.Children.Add(label);

                var editor = CreateEditor(parameter, savedValues);
                Grid.SetRow(editor.Container, rowIndex);
                Grid.SetColumn(editor.Container, 1);
                ParametersHost.Children.Add(editor.Container);

                _rows.Add(new ParameterRow(parameter, editor));
                rowIndex++;
            }

            if (_rows.Any(row => row.Parameter.IsRequired))
                HintText.Text = "Поля, выделенные полужирным, обязательны.";
        }

        private EditorHandle CreateEditor(
            ReportParameter parameter,
            IReadOnlyDictionary<string, string>? savedValues)
        {
            var initial = ResolveInitialValue(parameter, savedValues);
            var type = string.IsNullOrWhiteSpace(parameter.ParameterType)
                ? ReportParameterTypes.String
                : parameter.ParameterType;

            var container = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Firebrick,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(hint, 1);
            inner.Children.Add(hint);
            container.Children.Add(inner);

            FrameworkElement editor;
            Func<object> readValue;

            switch (type)
            {
                case ReportParameterTypes.Date:
                {
                    var picker = new DatePicker
                    {
                        SelectedDate = initial is DateTime date ? date : DateTime.Today,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    // Маска DD/MM/YYYY подключается глобально:
                    // Behaviors.DatePickerInputMaskBehavior.Initialize()
                    editor = picker;
                    readValue = () => picker.SelectedDate ?? DateTime.Today;
                    break;
                }

                case ReportParameterTypes.Bool:
                {
                    var check = new CheckBox
                    {
                        Content = "Да",
                        IsChecked = initial is bool flag && flag,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    editor = check;
                    readValue = () => check.IsChecked == true;
                    break;
                }

                case ReportParameterTypes.Reference:
                {
                    var pickerHost = new Grid();
                    pickerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    pickerHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var box = new TextBox
                    {
                        Text = ReferenceLookupItem.BuildDisplayText(initial?.ToString()),
                        Height = 28,
                        IsReadOnly = true,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        ToolTip = "Нажмите «Выбрать», чтобы указать значение из справочника"
                    };
                    Grid.SetColumn(box, 0);
                    pickerHost.Children.Add(box);

                    var selectButton = new Button
                    {
                        Content = "Выбрать",
                        Width = 90,
                        Height = 28,
                        Margin = new Thickness(6, 0, 0, 0)
                    };
                    Grid.SetColumn(selectButton, 1);
                    pickerHost.Children.Add(selectButton);

                    selectButton.Click += async (_, _) =>
                    {
                        var selected = await PickReferenceAsync(parameter);
                        if (selected == null)
                            return;

                        box.Text = ReferenceLookupItem.BuildDisplayText(selected);
                    };

                    editor = pickerHost;

                    // В фильтр уходит только GUID, а не «Название|GUID».
                    readValue = () => ReferenceLookupItem.ExtractId(box.Text);
                    break;
                }

                default:
                {
                    var box = new TextBox
                    {
                        Text = ReportParameterResolver.ToEditableText(initial),
                        Height = 28,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    if (type is ReportParameterTypes.Int or ReportParameterTypes.Decimal)
                        box.ToolTip = type == ReportParameterTypes.Int
                            ? "Целое число, например 150"
                            : "Число, дробная часть через точку, например 1250.50";

                    editor = box;
                    readValue = () => box.Text;
                    break;
                }
            }

            Grid.SetRow(editor, 0);
            inner.Children.Add(editor);

            // Для ссылок подсказка не нужна: значение и так показано названием.
            if (!string.IsNullOrWhiteSpace(parameter.DefaultValue) &&
                type != ReportParameterTypes.Reference)
            {
                var defaultHint = new TextBlock
                {
                    Text = $"по умолчанию: {parameter.DefaultValue}",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(defaultHint, 1);
                container.Children.Add(defaultHint);
            }

            return new EditorHandle(container, readValue, hint, editor);
        }

        private static object ResolveInitialValue(
            ReportParameter parameter,
            IReadOnlyDictionary<string, string>? savedValues)
        {
            var type = string.IsNullOrWhiteSpace(parameter.ParameterType)
                ? ReportParameterTypes.String
                : parameter.ParameterType;

            if (savedValues != null &&
                savedValues.TryGetValue(parameter.Name, out var saved) &&
                !string.IsNullOrWhiteSpace(saved))
            {
                return ReportParameterResolver.Coerce(saved, type);
            }

            return ReportParameterResolver.ResolveDefaultValue(parameter.DefaultValue, type);
        }

        private async Task<string?> PickReferenceAsync(ReportParameter parameter)
        {
            if (_context == null)
            {
                MessageBox.Show(this,
                    "Выбор из справочника недоступен: нет соединения с базой.",
                    "Параметры отчета", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var catalogName = parameter.ReferenceSource;
            if (string.IsNullOrWhiteSpace(catalogName))
            {
                MessageBox.Show(this,
                    $"Для параметра «{parameter.DisplayTitle}» не определён справочник.\n\n" +
                    "Он определяется автоматически, если параметр используется в фильтре " +
                    "по ссылочному полю. Проверьте вкладку «Параметры» в конструкторе.",
                    "Параметры отчета", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            try
            {
                var service = new ReportService(_context);
                var items = await service.LoadReferenceItemsAsync(catalogName);
                if (items.Count == 0)
                {
                    MessageBox.Show(this,
                        $"В справочнике «{catalogName}» нет записей.",
                        "Параметры отчета", MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }

                var picker = new ReferencePickerDialog(parameter.DisplayTitle, catalogName, items)
                {
                    Owner = this
                };

                return picker.ShowDialog() == true ? picker.SelectedStoredValue : null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Не удалось загрузить справочник «{catalogName}»: {ex.Message}",
                    "Параметры отчета", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var hasErrors = false;

            foreach (var row in _rows)
            {
                var type = string.IsNullOrWhiteSpace(row.Parameter.ParameterType)
                    ? ReportParameterTypes.String
                    : row.Parameter.ParameterType;

                var raw = row.Editor.ReadValue();
                var text = raw as string;
                var isBlank = text != null && string.IsNullOrWhiteSpace(text);

                if (row.Parameter.IsRequired && isBlank)
                {
                    row.Editor.ShowError("Укажите значение.");
                    hasErrors = true;
                    continue;
                }

                if (!isBlank &&
                    !TryValidate(text, row.Parameter, type, out var validationError))
                {
                    row.Editor.ShowError(validationError);
                    hasErrors = true;
                    continue;
                }

                row.Editor.ClearError();
                values[row.Parameter.Name] = raw is string stringValue
                    ? ReportParameterResolver.Coerce(stringValue, type)
                    : raw;
            }

            if (hasErrors)
            {
                MessageBox.Show(this, "Проверьте выделенные параметры.", "Параметры отчета",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Values = values;
            DialogResult = true;
        }

        private static bool TryValidate(string? text, ReportParameter parameter, string type, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            switch (type)
            {
                case ReportParameterTypes.Int when !int.TryParse(text.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out _):
                    error = "Ожидается целое число.";
                    return false;

                case ReportParameterTypes.Decimal when !decimal.TryParse(text.Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _):
                    error = "Ожидается число (дробная часть через точку).";
                    return false;

                case ReportParameterTypes.Date when !DateTime.TryParse(text.Trim(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.DateTimeStyles.None, out _):
                    error = "Ожидается дата.";
                    return false;

                default:
                    return true;
            }
        }

        private async void OnResetClick(object sender, RoutedEventArgs e)
        {
            ResetRequested = true;

            if (_context != null)
            {
                try
                {
                    await new ReportUserSettingService(_context).ResetAsync(
                        _report.Id, ServiceLocator.AuthService.CurrentUser?.Login);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Не удалось сбросить сохраненные значения: {ex.Message}",
                        "Параметры отчета", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            foreach (var row in _rows)
            {
                var type = string.IsNullOrWhiteSpace(row.Parameter.ParameterType)
                    ? ReportParameterTypes.String
                    : row.Parameter.ParameterType;
                row.Editor.ApplyValue(
                    ReportParameterResolver.ResolveDefaultValue(row.Parameter.DefaultValue, type));
                row.Editor.ClearError();
            }

            RememberCheckBox.IsChecked = false;
            HintText.Text = "Значения сброшены к значениям по умолчанию.";
        }

        private sealed record ParameterRow(ReportParameter Parameter, EditorHandle Editor);

        private sealed class EditorHandle
        {
            private readonly Func<object> _readValue;
            private readonly TextBlock _error;
            private readonly FrameworkElement _editor;

            public EditorHandle(
                Grid container,
                Func<object> readValue,
                TextBlock error,
                FrameworkElement editor)
            {
                Container = container;
                _readValue = readValue;
                _error = error;
                _editor = editor;
            }

            public Grid Container { get; }

            public object ReadValue() => _readValue();

            public void ShowError(string message)
            {
                _error.Text = message;
                _error.Visibility = Visibility.Visible;
            }

            public void ClearError()
            {
                _error.Text = string.Empty;
                _error.Visibility = Visibility.Collapsed;
            }

            public void ApplyValue(object? value)
            {
                switch (_editor)
                {
                    case DatePicker picker:
                        picker.SelectedDate = value as DateTime? ?? DateTime.Today;
                        break;
                    case CheckBox check:
                        check.IsChecked = value is bool flag && flag;
                        break;
                    case TextBox box:
                        box.Text = ReportParameterResolver.ToEditableText(value);
                        break;
                    case Grid referenceHost:
                        // Тип «Ссылка»: первый TextBox в контейнере — поле отображения.
                        var displayBox = referenceHost.Children
                            .OfType<TextBox>()
                            .FirstOrDefault();
                        if (displayBox != null)
                            displayBox.Text = ReferenceLookupItem.BuildDisplayText(value?.ToString());
                        break;
                }
            }
        }
    }
}
