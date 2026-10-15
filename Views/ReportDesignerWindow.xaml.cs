using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class ReportDesignerWindow : Window
    {
        private ReportService _reportService;
        private PrintFormService _printFormService;
        private MetadataService _metadataService;
        private Report _currentReport;
        private List<MetadataObject> _availableCatalogs;
        private ObservableCollection<ReportField> _reportFields;
        private ObservableCollection<ReportFilter> _reportFilters;
        private ObservableCollection<ReportParameter> _reportParameters;
        public ObservableCollection<FieldDef> AvailableDataFields { get; } = new();
        public ObservableCollection<FieldDef> AvailableFilterFields { get; } = new();
        public ObservableCollection<FieldDef> AvailableSourceFields { get; } = new();
        public ObservableCollection<FieldDef> AvailableComputedFields { get; } = new();
        public ObservableCollection<FieldDef> AvailableLayoutFields { get; } = new();

        public ReportDesignerWindow(Report report = null)
        {
            InitializeComponent();
            DataContext = this;

            _reportFields = new ObservableCollection<ReportField>();
            _reportFilters = new ObservableCollection<ReportFilter>();
            _reportParameters = new ObservableCollection<ReportParameter>();

            ReportFieldsGrid.ItemsSource = _reportFields;
            FiltersList.ItemsSource = _reportFilters;
            ReportParametersGrid.ItemsSource = _reportParameters;
            ConfigureFieldColumns();
            ConfigureParameterColumns();
            InitializeClosedXmlDesignerState();
            ApplyLayoutKindVisibility();

            _ = InitializeAsync(report);
        }

        private async Task InitializeAsync(Report report = null)
        {
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                _reportService = new ReportService(context);
                _printFormService = new PrintFormService(context);
                _metadataService = new MetadataService(context);

                await LoadDataSources();

                if (report != null)
                {
                    await LoadReportAsync(report);
                }

                DataSourceCombo.SelectionChanged += OnDataSourceChanged;
                AddFilterButton.Click += OnAddFilterClick;

                InfoText.Text = "Готов к работе";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task LoadDataSources()
        {
            _availableCatalogs = await _metadataService.GetCatalogsAsync();
            _availableCatalogs.AddRange(await _metadataService.GetDocumentsAsync());

            var reportSources = (await _metadataService.GetAllMetadataObjectsAsync())
                .Where(item => item.ObjectType == "ReportSource")
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Name)
                .ToList();
            foreach (var source in reportSources)
            {
                if (_availableCatalogs.All(item => item.Id != source.Id))
                    _availableCatalogs.Add(source);
            }

            DataSourceCombo.Items.Clear();
            DataSourceCombo.Items.Add(new ComboBoxItem { Tag = null, Content = "-- Выберите источник данных --" });

            foreach (var catalog in _availableCatalogs)
            {
                DataSourceCombo.Items.Add(new ComboBoxItem
                {
                    Tag = catalog,
                    Content = $"{GetDataSourceIcon(catalog)} {catalog.Name}"
                });
            }

            DataSourceCombo.SelectedIndex = 0;
        }

        private static string GetDataSourceIcon(MetadataObject catalog) => catalog.ObjectType switch
        {
            "Document" => "📄",
            "ReportSource" => "📌",
            _ => "📚"
        };
        private async void OnDataSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
            if (selected?.Tag is MetadataObject catalog)
            {
                SyncReportFieldsWithSourceIfStale(
                    _currentReport ?? new Report { ReportType = GetSelectedReportType() },
                    catalog);
                await LoadAvailableFields(catalog);
            }
            else
            {
                _reportFields.Clear();
                AvailableSourceFields.Clear();
                AvailableComputedFields.Clear();
                AvailableDataFields.Clear();
                AvailableFilterFields.Clear();
                AvailableLayoutFields.Clear();
            }

            UpdateClosedXmlSourceText();
        }

        private async void OnCreateReportDataSetClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedSourceId = (DataSourceCombo.SelectedItem as ComboBoxItem)?.Tag is MetadataObject selectedSource
                    ? selectedSource.Id
                    : (Guid?)null;

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var window = new Window
                {
                    Title = "Наборы данных отчетов",
                    Owner = this,
                    Width = 1100,
                    Height = 720,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new ReportDataSetManagementView(context)
                };

                window.ShowDialog();

                await LoadDataSources();
                RestoreSelectedDataSource(selectedSourceId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия наборов данных отчетов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnReportTypeHelpClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Тип отчета определяет способ построения печатной формы.\n\n" +
                "Выберите источник данных в поле «Источник».\n" +
                "Чтобы добавить SQL-набор данных, нажмите «+» рядом с источником.",
                "Тип отчета",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void RestoreSelectedDataSource(Guid? selectedSourceId)
        {
            if (!selectedSourceId.HasValue)
                return;

            SelectDataSourceById(selectedSourceId.Value);
        }

        private void SelectDataSourceById(Guid sourceId)
        {
            var item = DataSourceCombo.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(comboItem => (comboItem.Tag as MetadataObject)?.Id == sourceId);

            if (item != null)
                DataSourceCombo.SelectedItem = item;
        }

        private async void OnSelectDataSourceClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_availableCatalogs == null || _availableCatalogs.Count == 0)
                    await LoadDataSources();

                var rows = _availableCatalogs
                    .OrderBy(item => item.ObjectType == "ReportSource" ? 0 : item.ObjectType == "Document" ? 1 : 2)
                    .ThenBy(item => item.Name)
                    .Select(item => new Dictionary<string, object>
                    {
                        ["Id"] = item.Id,
                        ["Наименование"] = item.Name,
                        ["Тип"] = GetDataSourceTypeDisplay(item),
                        ["Таблица"] = item.TableName,
                        ["Описание"] = item.Description ?? string.Empty
                    })
                    .ToList();

                var dialog = new ReferenceSelectionDialog(rows, "Наименование", "Тип")
                {
                    Owner = this,
                    Title = "Выбор источника данных отчета"
                };

                if (dialog.ShowDialog() == true &&
                    dialog.SelectedItem != null &&
                    dialog.SelectedItem.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var sourceId))
                {
                    SelectDataSourceById(sourceId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выбора источника данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetDataSourceTypeDisplay(MetadataObject source) => source.ObjectType switch
        {
            "ReportSource" => "Набор данных отчета",
            "Document" => "Документ",
            "Catalog" => "Справочник",
            _ => source.ObjectType
        };

        private async Task LoadAvailableFields(MetadataObject catalog)
        {
            CommitDesignerGridEdits();

            var addedFieldNames = _reportFields
                .SelectMany(field => new[] { field.FieldName, field.DisplayName })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allFields = catalog.Fields
                .OrderBy(f => f.Order)
                .Select(field => new FieldDef
                {
                    Name = field.Name,
                    DbColumnName = string.IsNullOrWhiteSpace(field.DbColumnName) ? field.Name : field.DbColumnName,
                    Type = field.FieldType
                })
                .Where(field => !string.IsNullOrWhiteSpace(field.Name) || !string.IsNullOrWhiteSpace(field.DbColumnName))
                .ToList();

            AvailableSourceFields.Clear();
            AvailableComputedFields.Clear();
            AvailableDataFields.Clear();
            AvailableFilterFields.Clear();
            AvailableLayoutFields.Clear();

            foreach (var field in allFields)
            {
                if (!IsReportFieldAlreadyAdded(field, addedFieldNames))
                    AvailableSourceFields.Add(field);

                AvailableDataFields.Add(field);
                AvailableFilterFields.Add(field);
                AvailableLayoutFields.Add(field);
            }

            foreach (var field in GetComputedFieldDefinitions())
            {
                if (IsReportFieldAlreadyAdded(field, addedFieldNames))
                {
                    AddUniqueField(AvailableDataFields, field);
                    AddUniqueField(AvailableLayoutFields, field);
                }
                else
                {
                    AvailableComputedFields.Add(field);
                }
            }

            await Task.CompletedTask;
        }

        private static bool IsReportFieldAlreadyAdded(FieldDef field, HashSet<string> addedFieldNames)
        {
            return addedFieldNames.Contains(field.Name) ||
                   addedFieldNames.Contains(field.DbColumnName);
        }

        private static void AddUniqueField(ObservableCollection<FieldDef> fields, FieldDef field)
        {
            if (fields.Any(item =>
                    string.Equals(item.DbColumnName, field.DbColumnName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, field.Name, StringComparison.OrdinalIgnoreCase)))
                return;

            fields.Add(field);
        }

        private static IEnumerable<FieldDef> GetComputedFieldDefinitions()
        {
            return ReportComputedFieldCatalog.GetFields()
                .Select(field => new FieldDef
                {
                    Name = field.Name,
                    DbColumnName = field.FieldName,
                    Type = $"Вычисляемое {field.Type}"
                });
        }



        private void CommitDesignerGridEdits()
        {
            TryCommitGrid(ReportFieldsGrid);
        }

        private static void TryCommitGrid(DataGrid grid)
        {
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch (InvalidOperationException)
            {
                // Смена источника данных не должна падать из-за промежуточного режима редактирования грида.
            }
        }

        private void ConfigureFieldColumns()
        {
            var comboColumns = ReportFieldsGrid.Columns.OfType<DataGridComboBoxColumn>().ToList();
            if (comboColumns.Count < 2)
                return;

            comboColumns[0].ItemsSource = new[] { "", "Sum", "Average", "Count", "Min", "Max" };
            comboColumns[0].SelectedValueBinding = new System.Windows.Data.Binding(nameof(ReportField.AggregateType))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay
            };
            comboColumns[1].ItemsSource = new[] { "Left", "Center", "Right" };
            comboColumns[1].SelectedValueBinding = new System.Windows.Data.Binding(nameof(ReportField.Alignment))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay
            };
        }

        private async void OnFieldDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await AddSelectedFieldAsync();
        }
        // ==================== Параметры отчёта ====================

        private void ConfigureParameterColumns()
        {
            ParameterTypeColumn.ItemsSource = ReportParameterTypes.All;
            ParameterTypeColumn.SelectedValueBinding = new System.Windows.Data.Binding(
                nameof(ReportParameter.ParameterType))
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            };
        }

        /// <summary>
        /// Удаляет выбранный параметр отчёта.
        ///
        /// Параметр, нужный подстановкам макета, вернётся при следующем открытии
        /// отчёта: заполнение из макета добавит его снова.
        /// </summary>
        private void OnDeleteParameterClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();

            if (ReportParametersGrid.SelectedItem is not ReportParameter parameter)
            {
                ParameterIssuesText.Text = "Выберите параметр в списке.";
                return;
            }

            var name = string.IsNullOrWhiteSpace(parameter.Name) ? "(без имени)" : parameter.Name;
            var answer = MessageBox.Show(
                $"Удалить параметр «{name}»?",
                "Удаление параметра",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            _reportParameters.Remove(parameter);
            ParameterIssuesText.Text = $"Параметр «{name}» удалён.";
        }

        private void OnAddParameterClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            _reportParameters.Add(new ReportParameter
            {
                Id = Guid.NewGuid(),
                Name = string.Empty,
                Title = string.Empty,
                ParameterType = ReportParameterTypes.Date,
                DefaultValue = "начала месяца",
                IsRequired = true,
                Order = _reportParameters.Count + 1
            });
            ParameterIssuesText.Text = string.Empty;
        }

        private void OnSyncParametersFromFiltersClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            var before = _reportParameters.Count;
            var report = BuildParameterValidationReport();
            ReportService.EnsureParametersCoverFilters(report);

            _reportParameters.Clear();
            foreach (var parameter in report.Parameters.OrderBy(item => item.Order))
                _reportParameters.Add(parameter);

            var added = _reportParameters.Count - before;
            ParameterIssuesText.Text = added > 0
                ? $"Добавлено параметров: {added}."
                : "Новых параметров в фильтрах не найдено.";
        }

        private void OnCheckParametersClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            var issues = ReportService.ValidateParameters(BuildParameterValidationReport());
            ParameterIssuesText.Text = issues.Count == 0
                ? "✅ Замечаний нет."
                : "⚠ " + string.Join("  •  ", issues);
        }

        /// <summary>
        /// Выбор элемента справочника для параметра типа «Ссылка»:
        /// выбранный GUID записывается в значение по умолчанию.
        /// </summary>
        private async void OnPickReferenceValueClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();

            if (ReportParametersGrid.SelectedItem is not ReportParameter parameter)
            {
                MessageBox.Show("Выберите строку параметра в таблице.", "Параметры",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.Equals(parameter.ParameterType, ReportParameterTypes.Reference,
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    $"Параметр «{parameter.DisplayTitle}» имеет тип " +
                    $"«{ReportParameterTypes.GetDisplayName(parameter.ParameterType)}».\n\n" +
                    "Выбор из справочника доступен только для типа «Ссылка».",
                    "Параметры", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Справочник определяется автоматически по ссылочному полю фильтра.
                if (string.IsNullOrWhiteSpace(parameter.ReferenceSource))
                {
                    var draft = BuildParameterValidationReport();
                    await _reportService.FillReferenceSourcesAsync(draft);
                    var resolved = draft.Parameters
                        .FirstOrDefault(item => string.Equals(item.Name, parameter.Name,
                            StringComparison.OrdinalIgnoreCase));

                    if (resolved != null)
                        parameter.ReferenceSource = resolved.ReferenceSource;
                }

                if (string.IsNullOrWhiteSpace(parameter.ReferenceSource))
                {
                    MessageBox.Show(
                        "Не удалось определить справочник.\n\n" +
                        "Заполните поле «Справочник» вручную или убедитесь, что параметр " +
                        "используется в фильтре по ссылочному полю.",
                        "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var items = await _reportService.LoadReferenceItemsAsync(parameter.ReferenceSource);
                if (items.Count == 0)
                {
                    MessageBox.Show($"В справочнике «{parameter.ReferenceSource}» нет записей.",
                        "Параметры", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var picker = new ReferencePickerDialog(
                    parameter.DisplayTitle, parameter.ReferenceSource, items)
                {
                    Owner = this
                };

                if (picker.ShowDialog() != true)
                    return;

                // В значение по умолчанию пишем «Название|GUID» — так разработчик видит,
                // что выбрано, а в фильтр уходит только GUID.
                parameter.DefaultValue = picker.SelectedStoredValue;
                ReportParametersGrid.Items.Refresh();
                ParameterIssuesText.Text = $"Выбрано: {picker.SelectedDisplay}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выбора значения: {ex.Message}", "Параметры",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Черновик отчёта только для проверок параметров.</summary>
        private Report BuildParameterValidationReport() => new()
        {
            Id = _currentReport?.Id ?? Guid.Empty,
            Name = ReportNameBox.Text,
            Filters = _reportFilters.ToList(),
            Parameters = _reportParameters.ToList()
        };

        private void LoadReportParameters(Report report)
        {
            _reportParameters.Clear();
            foreach (var parameter in (report.Parameters ?? new List<ReportParameter>())
                         .OrderBy(item => item.Order))
            {
                _reportParameters.Add(parameter);
            }

            ParameterIssuesText.Text = string.Empty;
        }

        /// <summary>Подстановки вида {Имя} и {Имя:формат} в тексте.</summary>
        private static readonly System.Text.RegularExpressions.Regex LayoutSubstitutionRegex = new(
            @"\{(?:param\.)?([A-Za-z_][A-Za-z0-9_]*)(?::[^}]+)?\}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Описания параметров, которые встречаются в макете ClosedXML.
        ///
        /// Значения подставляет окно отчёта, а описания нужны администратору:
        /// без них вкладка «Параметры» пуста и непонятно, что требует отчёт.
        /// </summary>
        private static readonly Dictionary<string, (string Title, string Type, string Default)> KnownLayoutParameters =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PeriodStart"] = ("Начало периода", ReportParameterTypes.Date, "начала года"),
                ["PeriodEnd"] = ("Конец периода", ReportParameterTypes.Date, "конца года"),
                ["AccountCode"] = ("Счёт", ReportParameterTypes.String, string.Empty),
                ["OrganizationId"] = ("Организация", ReportParameterTypes.String, string.Empty),
                ["CashDeskId"] = ("Касса", ReportParameterTypes.String, string.Empty),
                ["EmployeeId"] = ("Сотрудник", ReportParameterTypes.String, string.Empty),
                ["UserName"] = ("Пользователь", ReportParameterTypes.String, string.Empty)
            };

        /// <summary>
        /// Дополняет вкладку «Параметры» теми параметрами, которые использует макет.
        ///
        /// Уже описанные параметры не трогаются: правки администратора сохраняются.
        /// </summary>
        private void SeedParametersFromClosedXmlLayout(Report report)
        {
            if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout))
                return;

            var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out _);
            if (layout == null)
                return;

            var names = CollectLayoutSubstitutionNames(layout);
            if (names.Count == 0)
                return;

            var existing = _reportParameters
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                .Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = new List<string>();

            foreach (var name in names)
            {
                if (existing.Contains(name))
                    continue;

                var known = KnownLayoutParameters.TryGetValue(name, out var preset)
                    ? preset
                    : (Title: name, Type: ReportParameterTypes.String, Default: string.Empty);

                _reportParameters.Add(new ReportParameter
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    Name = name,
                    Title = known.Title,
                    ParameterType = known.Type,
                    DefaultValue = known.Default,
                    IsRequired = false,
                    Order = _reportParameters.Count + 1
                });

                existing.Add(name);
                added.Add(name);
            }

            if (added.Count > 0)
            {
                ParameterIssuesText.Text =
                    $"Из макета добавлены параметры: {string.Join(", ", added)}.";
            }
        }

        /// <summary>
        /// Имена параметров, встречающихся в макете: в заголовках, свободных строках
        /// и подписях колонок.
        /// </summary>
        private static List<string> CollectLayoutSubstitutionNames(ClosedXmlReportLayout layout)
        {
            var names = new List<string>();

            void Scan(string? text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                foreach (System.Text.RegularExpressions.Match match in
                         LayoutSubstitutionRegex.Matches(text))
                {
                    var name = match.Groups[1].Value;

                    // Служебные подстановки колонтитула в параметрах не нуждаются.
                    if (string.Equals(name, "Organization", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Дата", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        names.Add(name);
                }
            }

            foreach (var sheet in layout.Sheets)
            {
                foreach (var block in sheet.Blocks)
                {
                    foreach (var row in block.Rows)
                    {
                        foreach (var cell in row.Cells)
                            Scan(cell.Text);
                    }

                    if (block.Table == null)
                        continue;

                    foreach (var column in block.Table.Columns)
                        Scan(column.Header);

                    foreach (var level in block.Table.HeaderLevels)
                    {
                        foreach (var cell in level.Cells)
                            Scan(cell.Text);
                    }
                }
            }

            return names;
        }


        private async void OnComputedFieldDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await AddReportFieldAsync(ComputedFieldsList.SelectedItem as FieldDef);
        }

        private async Task AddSelectedFieldAsync()
        {
            await AddReportFieldAsync(AvailableFields.SelectedItem as FieldDef);
        }

        private async Task AddReportFieldAsync(FieldDef? field)
        {
            if (field != null && !_reportFields.Any(f => string.Equals(f.FieldName, field.DbColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                _reportFields.Add(new ReportField
                {
                    Id = Guid.NewGuid(),
                    FieldName = field.DbColumnName,
                    DisplayName = field.Name,
                    Order = _reportFields.Count + 1,
                    IsVisible = true,
                    Width = GetDefaultReportFieldWidth(field.Type),
                    Alignment = GetDefaultReportFieldAlignment(field.Type)
                });

                if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                {
                    await LoadAvailableFields(catalog);
                }
            }
        }

        private async void OnRemoveFieldClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var field = button?.DataContext as ReportField;
            if (field != null)
            {
                _reportFields.Remove(field);

                if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                {
                    await LoadAvailableFields(catalog);
                }
            }
        }

        private void OnMoveUpClick(object sender, RoutedEventArgs e)
        {
            if (ReportFieldsGrid.SelectedItem is ReportField selected)
            {
                var index = _reportFields.IndexOf(selected);
                if (index > 0)
                {
                    _reportFields.Move(index, index - 1);
                    ReorderFields();
                }
            }
        }

        private void OnMoveDownClick(object sender, RoutedEventArgs e)
        {
            if (ReportFieldsGrid.SelectedItem is ReportField selected)
            {
                var index = _reportFields.IndexOf(selected);
                if (index < _reportFields.Count - 1)
                {
                    _reportFields.Move(index, index + 1);
                    ReorderFields();
                }
            }
        }

        private void ReorderFields()
        {
            int order = 1;
            foreach (var field in _reportFields)
            {
                field.Order = order++;
            }
        }

        private void SyncReportFieldsWithSourceIfStale(Report report, MetadataObject catalog)
        {
            if (catalog.Fields.Count == 0)
            {
                _reportFields.Clear();
                return;
            }

            var sourceFieldNames = catalog.Fields
                .SelectMany(field => new[] { field.Name, field.DbColumnName })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var computedFieldNames = GetComputedFieldDefinitions()
                .SelectMany(field => new[] { field.Name, field.DbColumnName })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var mirrorSourceSchema = string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase);
            var computedFields = _reportFields
                .Where(field => computedFieldNames.Contains(field.FieldName) || computedFieldNames.Contains(field.DisplayName))
                .OrderBy(field => field.Order)
                .ToList();
            var hasStaleFields = false;
            for (var i = _reportFields.Count - 1; i >= 0; i--)
            {
                var field = _reportFields[i];
                if (sourceFieldNames.Contains(field.FieldName) ||
                    sourceFieldNames.Contains(field.DisplayName) ||
                    computedFieldNames.Contains(field.FieldName) ||
                    computedFieldNames.Contains(field.DisplayName))
                    continue;

                _reportFields.RemoveAt(i);
                hasStaleFields = true;
            }

            if (_reportFields.Count > 0 && !mirrorSourceSchema)
            {
                if (hasStaleFields)
                    ReorderFields();
                return;
            }

            var sourceReportFieldCount = _reportFields.Count(field =>
                !computedFieldNames.Contains(field.FieldName) &&
                !computedFieldNames.Contains(field.DisplayName));
            if (mirrorSourceSchema && sourceReportFieldCount == catalog.Fields.Count && !hasStaleFields)
                return;

            _reportFields.Clear();
            var order = 1;
            foreach (var field in catalog.Fields.OrderBy(field => field.Order))
            {
                var fieldName = string.IsNullOrWhiteSpace(field.DbColumnName)
                    ? field.Name
                    : field.DbColumnName;

                _reportFields.Add(new ReportField
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    FieldName = fieldName,
                    DisplayName = field.Name,
                    Order = order++,
                    Width = GetDefaultReportFieldWidth(field.FieldType),
                    IsVisible = true,
                    Alignment = GetDefaultReportFieldAlignment(field.FieldType)
                });
            }

            foreach (var field in computedFields)
            {
                if (_reportFields.Any(existing => string.Equals(existing.FieldName, field.FieldName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                field.Order = order++;
                _reportFields.Add(field);
            }
        }

        private static int GetDefaultReportFieldWidth(string fieldType)
        {
            return fieldType switch
            {
                "Decimal" => 100,
                "Money" => 100,
                "Int" => 80,
                "Integer" => 80,
                "Date" => 90,
                "DateTime" => 100,
                _ => 140
            };
        }

        private static string GetDefaultReportFieldAlignment(string fieldType)
        {
            return fieldType switch
            {
                "Decimal" => "Right",
                "Money" => "Right",
                "Int" => "Right",
                "Integer" => "Right",
                _ => "Left"
            };
        }

        private async Task LoadReportAsync(Report report)
        {
            _currentReport = report;

            // Способ макета один — ClosedXML: координатные макеты FRX/native удалены.
            SetLayoutKind(ClosedXmlLayoutKind);

            ReportNameBox.Text = report.Name;
            ReportDescBox.Text = report.Description;

            TitleTextBox.Text = report.TitleText ?? "";
            SubtitleTextBox.Text = report.SubtitleText ?? "";
            HeaderTextBox.Text = report.HeaderText ?? "";
            FooterTextBox.Text = report.FooterText ?? "";
            SummaryTextBox.Text = report.SummaryText ?? "";

            // Ориентация
            OrientationCombo.SelectedIndex = report.PageOrientation == "Landscape" ? 1 : 0;
            FontCombo.Text = report.FontName ?? "Segoe UI";
            FontSizeCombo.Text = (report.FontSize > 0 ? report.FontSize : 10).ToString();
            SelectColorInCombo(HeaderColorCombo, report.HeaderColor ?? "#2C3E50");

            AlternateRowColorsCheck.IsChecked = report.AlternateRowColors;
            ShowGridLinesCheck.IsChecked = report.ShowGridLines;
            ShowGrandTotalCheck.IsChecked = report.ShowGrandTotal;
            ShowPageNumbersCheck.IsChecked = report.ShowPageNumbers;
            IsActiveCheck.IsChecked = report.IsActive;
            IsPrintFormCheck.IsChecked = report.IsPrintForm;
            IsDefaultCheck.IsChecked = report.IsDefault;
            ShowInAccountingReportsCheck.IsChecked = report.ShowInAccountingReports;
            ReportTypeCombo.SelectedItem = ReportTypeCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == report.ReportType) ?? ReportTypeCombo.Items[0];

            // Выбор источника
            if (report.DataSourceId.HasValue)
            {
                var catalog = _availableCatalogs.FirstOrDefault(c => c.Id == report.DataSourceId);
                if (catalog != null)
                {
                    var item = DataSourceCombo.Items
                        .Cast<ComboBoxItem>()
                        .FirstOrDefault(i => (i.Tag as MetadataObject)?.Id == catalog.Id);

                    if (item != null)
                        DataSourceCombo.SelectedItem = item;
                }
            }

            // Загрузка полей
            _reportFields.Clear();
            if (report.Fields != null && report.Fields.Count > 0)
            {
                foreach (var field in report.Fields.OrderBy(f => f.Order))
                {
                    _reportFields.Add(field);
                }
            }

            if (report.DataSourceId.HasValue)
            {
                var catalog = _availableCatalogs.FirstOrDefault(c => c.Id == report.DataSourceId);
                if (catalog != null)
                {
                    SyncReportFieldsWithSourceIfStale(report, catalog);
                    await LoadAvailableFields(catalog);
                }
            }
            else
            {
                _reportFields.Clear();
                AvailableSourceFields.Clear();
                AvailableComputedFields.Clear();
                AvailableDataFields.Clear();
                AvailableFilterFields.Clear();
                AvailableLayoutFields.Clear();
            }
            // Загрузка фильтров
            _reportFilters.Clear();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                _reportFilters.Add(filter);
            }

            LoadReportParameters(report);

            LoadClosedXmlLayoutFromReport(report);

            // Параметры дополняются подстановками из макета: администратор должен
            // видеть на вкладке «Параметры», что требует отчёт.
            SeedParametersFromClosedXmlLayout(report);
            UpdateClosedXmlSourceText();
        }

        private void OnAddFilterClick(object sender, RoutedEventArgs e)
        {
            _reportFilters.Add(new ReportFilter
            {
                Id = Guid.NewGuid(),
                FieldName = "",
                Operation = "=",
                Value = "",
                Order = _reportFilters.Count + 1
            });
        }

        private void RemoveFilter_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var filter = button?.DataContext as ReportFilter;
            if (filter != null)
            {
                _reportFilters.Remove(filter);
            }
        }

        private async void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                if (IsPrintFormCheck.IsChecked == true)
                {
                    var report = BuildReportFromForm();
                    if (report == null)
                        return;

                    // Параметры подставляются в макет как {ИмяПараметра}.
                    var printFormParameters = await _reportService.ResolveParametersForPreviewAsync(report);

                    var pdfBytes = _printFormService.ExportTemplatePreview(report, printFormParameters);
                    var previewWindow = new PdfPreviewWindow(pdfBytes, report.Name) { Owner = this };
                    previewWindow.ShowDialog();

                    StatusText.Text = "✅ Предпросмотр открыт";
                }
                else
                {
                    await GenerateAndShowReport();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предпросмотра: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            await SaveReport();
        }

        private async void OnExportPdfClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var report = BuildReportFromForm();
                if (report == null)
                    return;

                if (report.IsPrintForm)
                {
                    await ExportPrintFormPreviewAsync(true, report);
                    return;
                }

                var data = await _reportService.GetReportDataAsync(
                    report, await _reportService.ResolveParametersForPreviewAsync(report));
                var dialog = new SaveFileDialog
                {
                    Title = "Сохранить отчет в PDF",
                    Filter = "PDF файлы (*.pdf)|*.pdf",
                    DefaultExt = "pdf",
                    FileName = $"{GetSafeFileName(report.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
                };

                if (dialog.ShowDialog() != true)
                    return;

                File.WriteAllBytes(dialog.FileName, _reportService.ExportToPdf(data, report));
                StatusText.Text = $"PDF сформирован: {dialog.FileName}";
                MessageBox.Show("PDF-отчет успешно сформирован.", "Отчет",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования PDF: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportPrintFormPreviewAsync(bool showConfirmation, Report? report = null)
        {
            report ??= BuildReportFromForm();
            if (report == null)
                return;

            var printFormParameters = await _reportService.ResolveParametersForPreviewAsync(report);
            var pdfBytes = _printFormService.ExportTemplatePreview(report, printFormParameters);

            // Показываем документ прямо в окне: внешняя программа не запускается.
            var previewWindow = new PdfPreviewWindow(pdfBytes, report.Name) { Owner = this };
            previewWindow.ShowDialog();

            StatusText.Text = "✅ Предпросмотр открыт";
        }

        private async Task GenerateAndShowReport()
        {
            try
            {
                var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
                if (selected?.Tag is not MetadataObject catalog)
                {
                    MessageBox.Show("Выберите источник данных", "Внимание",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_reportFields.Any())
                {
                    MessageBox.Show("Добавьте хотя бы одно поле в отчет", "Внимание",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var tempReport = BuildReportFromForm(catalog);
                if (tempReport == null)
                    return;

                // В конструкторе параметры не спрашиваем: берём значения по умолчанию
                // с учётом сохранённых значений текущего пользователя.
                var previewParameters = await _reportService.ResolveParametersForPreviewAsync(tempReport);

                var data = await _reportService.GetReportDataAsync(tempReport, previewParameters);

                var pdfBytes = _reportService.ExportToPdf(data, tempReport);
                var previewWindow = new PdfPreviewWindow(pdfBytes, tempReport.Name) { Owner = this };
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveReport()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ReportNameBox.Text))
                {
                    MessageBox.Show("Введите название отчета", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
                if (selected?.Tag is not MetadataObject catalog)
                {
                    MessageBox.Show("Выберите источник данных", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_currentReport == null)
                {
                    _currentReport = new Report
                    {
                        Id = Guid.NewGuid(),
                        CreatedAt = DateTime.UtcNow
                    };
                }

                CommitDesignerGridEdits();

                // Заполняем данные
                _currentReport.Name = ReportNameBox.Text;
                _currentReport.Description = ReportDescBox.Text;
                _currentReport.TitleText = TitleTextBox.Text;
                _currentReport.SubtitleText = SubtitleTextBox.Text;
                _currentReport.HeaderText = HeaderTextBox.Text;
                _currentReport.FooterText = FooterTextBox.Text;
                _currentReport.SummaryText = SummaryTextBox.Text;
                _currentReport.DataSourceType = catalog.ObjectType;
                _currentReport.DataSourceId = catalog.Id;
                _currentReport.ReportType = GetSelectedReportType();
                _currentReport.IsActive = IsActiveCheck.IsChecked ?? true;
                _currentReport.ShowInAccountingReports = ShowInAccountingReportsCheck.IsChecked ?? false;
                _currentReport.IsPrintForm = IsPrintFormCheck.IsChecked ?? false;
                _currentReport.IsDefault = IsDefaultCheck.IsChecked ?? false;
                _currentReport.SourceFormat = GetSelectedLayoutKind() == FoxProLayoutKind
                    ? (_currentReport.ReportType == "FoxProLayout" ? "FoxProFRX" : "Native")
                    : ClosedXmlSourceFormat;
                // Шаблон печатной формы в конструкторе не редактируется: вкладка
                // FRX/native удалена, а отчёты ClosedXML хранят макет в ClosedXmlLayout.
                _currentReport.TemplateVersion = Math.Max(_currentReport.TemplateVersion, 2);
                _currentReport.ClosedXmlLayout = BuildClosedXmlLayoutJson();
                _currentReport.Icon = "📊";
                _currentReport.UpdatedAt = DateTime.UtcNow;

                if (_currentReport.CreatedAt == DateTime.MinValue)
                    _currentReport.CreatedAt = DateTime.UtcNow;

                // Настройки оформления
                _currentReport.PageOrientation = OrientationCombo.SelectedIndex == 1 ? "Landscape" : "Portrait";
                _currentReport.FontName = FontCombo.Text;
                _currentReport.FontSize = int.TryParse(FontSizeCombo.Text, out var fontSize) ? fontSize : 10;
                _currentReport.HeaderColor = GetSelectedColor(HeaderColorCombo);
                _currentReport.AlternateRowColors = AlternateRowColorsCheck.IsChecked ?? true;
                _currentReport.ShowGridLines = ShowGridLinesCheck.IsChecked ?? true;
                _currentReport.ShowGrandTotal = ShowGrandTotalCheck.IsChecked ?? true;
                _currentReport.ShowPageNumbers = ShowPageNumbersCheck.IsChecked ?? true;

                var fieldsToAdd = _reportFields.Select(f => new ReportField
                {
                    Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
                    ReportId = _currentReport.Id,
                    FieldName = f.FieldName,
                    DisplayName = f.DisplayName,
                    Order = f.Order,
                    Width = f.Width,
                    IsVisible = f.IsVisible,
                    Alignment = f.Alignment,
                    AggregateType = f.AggregateType,
                    Format = f.Format
                }).ToList();

                var filtersToAdd = _reportFilters
                    .Where(f => !string.IsNullOrWhiteSpace(f.FieldName))
                    .Select(f => new ReportFilter
                    {
                        Id = f.Id == Guid.Empty ? Guid.NewGuid() : f.Id,
                        ReportId = _currentReport.Id,
                        FieldName = f.FieldName,
                        Operation = f.Operation,
                        Value = f.Value,
                        Value2 = f.Value2,
                        IsOptional = f.IsOptional,
                        Order = f.Order
                    }).ToList();

                // Привязки элементов берутся из открытого отчёта: вкладки FRX-полей,
                // которая их собирала, больше нет.
                _currentReport.Fields = fieldsToAdd;
                _currentReport.Filters = filtersToAdd;
                _currentReport.Parameters = _reportParameters
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                    .OrderBy(parameter => parameter.Order)
                    .ToList();

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var reportService = new ReportService(context);
                _currentReport = await reportService.SaveReportAsync(_currentReport);
                // Отчёт сохранён и окно закрывается: модальное подтверждение только отвлекает.

                DialogResult = true;
                Close();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                MessageBox.Show(
                    "Ошибка конкурентности при сохранении. Попробуйте еще раз.\n\n" +
                    $"Детали: {ex.Message}",
                    "Ошибка сохранения",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetSelectedColor(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string colorTag)
            {
                return colorTag;
            }
            return "#2C3E50";
        }

        /// <summary>
        /// Готовит набор данных для предпросмотра ClosedXML.
        ///
        /// Для акта сверки данные строятся тем же сервисом, что и в рабочем режиме:
        /// макет акта рассчитан на таблицу акта — шапку, пары счетов, сальдо и итоги.
        /// Общий генератор по метаданным отдаёт сырые поля проводок, и макет
        /// не находит в них свои колонки.
        ///
        /// Для остальных отчётов используется обычный источник данных.
        /// </summary>
        private async Task<System.Data.DataTable> BuildClosedXmlPreviewDataAsync(
            Report report,
            Dictionary<string, object> parameters)
        {
            if (report == null)
                return new System.Data.DataTable();

            var isReconciliationAct =
                string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase) ||
                (report.Code?.Contains("reconciliation", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (report.Name?.Contains("акт свер", StringComparison.OrdinalIgnoreCase) ?? false);

            if (!isReconciliationAct)
                return await _reportService.GetReportDataAsync(report, parameters);

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var (start, end) = ResolvePreviewPeriod(parameters);

            var calculation = await new OrganizationBalanceService(context).CalculateAsync(start, end);
            var pairs = calculation.Rows.Where(row => !row.IsOrganizationTotal).ToList();
            var totals = calculation.Rows.Where(row => row.IsOrganizationTotal).ToList();

            var builder = new ReconciliationActBuilder(context);
            var movements = await builder.LoadMovementsAsync(null, start, end);
            var act = builder.BuildTable(pairs, totals, movements, start, end, false, "Все организации");

            // Макет обращается к дате как posting_date, к модулю — как module_code.
            foreach (System.Data.DataRow row in act.Table.Rows)
            {
                var dateText = row.Table.Columns.Contains("Дата") ? row["Дата"]?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(dateText) && DateTime.TryParse(dateText, out var parsed))
                {
                    ReconciliationActBuilder.SetServiceValue(row, "posting_date", text: parsed.ToString("dd.MM.yyyy"));
                    ReconciliationActBuilder.SetServiceValue(row, "document_date", text: parsed.ToString("dd.MM.yyyy"));
                }

                var moduleText = row.Table.Columns.Contains("Модуль") ? row["Модуль"]?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(moduleText))
                {
                    ReconciliationActBuilder.SetServiceValue(row, "module_code", text: moduleText);
                    ReconciliationActBuilder.SetServiceValue(row, "module", text: moduleText);
                }
            }

            return act.Table;
        }

        /// <summary>
        /// Определяет период предпросмотра: из параметров отчёта, иначе текущий год.
        /// </summary>
        private static (DateTime Start, DateTime End) ResolvePreviewPeriod(
            IReadOnlyDictionary<string, object> parameters)
        {
            var start = new DateTime(DateTime.Today.Year, 1, 1);
            var end = DateTime.Today;

            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    var name = pair.Key?.ToLowerInvariant() ?? string.Empty;

                    if (pair.Value is DateTime dateValue)
                    {
                        if (name.Contains("начал") || name.Contains("start"))
                            start = dateValue;
                        else
                            end = dateValue;
                    }
                    else if (pair.Value is string text && DateTime.TryParse(text, out var parsed))
                    {
                        if (name.Contains("начал") || name.Contains("start"))
                            start = parsed;
                        else if (name.Contains("конец") || name.Contains("end"))
                            end = parsed;
                    }
                }
            }

            if (end < start)
                end = start;

            return (start, end);
        }

        private Report? BuildReportFromForm(MetadataObject? selectedCatalog = null)
        {
            selectedCatalog ??= (DataSourceCombo.SelectedItem as ComboBoxItem)?.Tag as MetadataObject;
            if (selectedCatalog == null)
            {
                MessageBox.Show("Выберите источник данных", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (!_reportFields.Any() && IsPrintFormCheck.IsChecked != true)
            {
                MessageBox.Show("Добавьте хотя бы одно поле в отчет", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            CommitDesignerGridEdits();

            var report = new Report
            {
                Name = string.IsNullOrWhiteSpace(ReportNameBox.Text) ? "Новый отчет" : ReportNameBox.Text.Trim(),
                Description = ReportDescBox.Text,
                TitleText = TitleTextBox.Text,
                SubtitleText = SubtitleTextBox.Text,
                HeaderText = HeaderTextBox.Text,
                FooterText = FooterTextBox.Text,
                SummaryText = SummaryTextBox.Text,
                DataSourceType = selectedCatalog.ObjectType,
                DataSourceId = selectedCatalog.Id,
                ReportType = GetSelectedReportType(),
                IsActive = IsActiveCheck.IsChecked ?? true,
                ShowInAccountingReports = ShowInAccountingReportsCheck.IsChecked ?? false,
                IsPrintForm = IsPrintFormCheck.IsChecked ?? false,
                IsDefault = IsDefaultCheck.IsChecked ?? false,
                SourceFormat = GetSelectedLayoutKind() == FoxProLayoutKind
                    ? (GetSelectedReportType() == "FoxProLayout" ? "FoxProFRX" : "Native")
                    : ClosedXmlSourceFormat,
                //Template = _currentReport?.Template ?? "",
                Fields = _reportFields.OrderBy(field => field.Order).ToList(),
                Filters = _reportFilters.Where(filter => !string.IsNullOrWhiteSpace(filter.FieldName)).ToList(),
                Parameters = _reportParameters
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                    .OrderBy(parameter => parameter.Order)
                    .ToList(),
                PageOrientation = OrientationCombo.SelectedIndex == 1 ? "Landscape" : "Portrait",
                FontName = FontCombo.Text,
                FontSize = int.TryParse(FontSizeCombo.Text, out var fontSize) ? fontSize : 10,
                HeaderColor = GetSelectedColor(HeaderColorCombo),
                AlternateRowColors = AlternateRowColorsCheck.IsChecked ?? true,
                ShowGridLines = ShowGridLinesCheck.IsChecked ?? true,
                ShowGrandTotal = ShowGrandTotalCheck.IsChecked ?? true,
                ShowPageNumbers = ShowPageNumbersCheck.IsChecked ?? true,
                Template = _currentReport?.Template ?? string.Empty,
                ClosedXmlLayout = BuildClosedXmlLayoutJson()
            };

            report.ElementMappings = _currentReport?.ElementMappings ?? new List<ReportElementMapping>();
            return report;
        }

        private static string GetSafeFileName(string name)
        {
            return string.Concat(name.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        }

        private string GetSelectedReportType()
        {
            return (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Table";
        }

        // ===================================================================
        // Способ макета: FRX/native (координатный) или ClosedXML (визуальный).
        // Выбранный способ скрывает всё, что относится к другому.
        // ===================================================================

        private const string FoxProLayoutKind = "FoxProFRX";
        private const string ClosedXmlLayoutKind = "ClosedXml";

        /// <summary>Признак «макет этого отчёта — ClosedXML» в поле Report.SourceFormat.</summary>
        private const string ClosedXmlSourceFormat = "ClosedXml";

        private void OnLayoutKindChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyLayoutKindVisibility();
        }

        private string GetSelectedLayoutKind() =>
            (LayoutKindCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ClosedXmlLayoutKind;

        private void SetLayoutKind(string kind)
        {
            if (LayoutKindCombo == null)
                return;

            // Координатного способа макета (FRX/native) больше нет, остался ClosedXML.
            if (string.Equals(kind, FoxProLayoutKind, StringComparison.OrdinalIgnoreCase))
                kind = ClosedXmlLayoutKind;

            foreach (var item in LayoutKindCombo.Items.OfType<ComboBoxItem>())
            {
                if (!string.Equals(item.Tag?.ToString(), kind, StringComparison.OrdinalIgnoreCase))
                    continue;

                LayoutKindCombo.SelectedItem = item;
                break;
            }

            ApplyLayoutKindVisibility();
        }

        private void ApplyLayoutKindVisibility()
        {
            // Способ макета один — ClosedXML: вкладки FRX/native, блок FRX-шаблона
            // и тип отчёта «Макет Visual FoxPro FRX» удалены вместе с подсистемой FRX.
            if (ClosedXmlDesignerTab != null)
                ClosedXmlDesignerTab.Visibility = Visibility.Visible;

            ApplyInactiveTabsVisibility(isFrx: false);

            if (LayoutKindHint != null)
            {
                LayoutKindHint.Text = "ClosedXML: макет собирается визуально на вкладке «📊 ClosedXML макет». Вкладки «Оформление» и «Фильтры» скрыты — оформление и отбор задаются в самом макете.";
            }
        }

        /// <summary>
        /// Скрывает вкладки, настройки которых не влияют на вывод ClosedXML.
        ///
        /// «Оформление» — ориентация, шрифт, цвет шапки, чередование строк: всё это
        /// задаётся в параметрах листа и стилях колонок самого макета, поэтому
        /// значения вкладки в вывод не попадают.
        ///
        /// «Фильтры» — условия отбора применяются только при выборке из справочника
        /// напрямую. Отчёты ClosedXML берут данные из набора данных, и отбор
        /// выполняется внутри запроса или расчёта.
        /// </summary>
        private void ApplyInactiveTabsVisibility(bool isFrx)
        {
            var inactiveVisibility = isFrx ? Visibility.Visible : Visibility.Collapsed;

            if (AppearanceTab != null)
                AppearanceTab.Visibility = inactiveVisibility;
            if (FiltersTab != null)
                FiltersTab.Visibility = inactiveVisibility;
        }

        /// <summary>
        /// Переключение вида макета: простой или форматный.
        ///
        /// Вид определяет, что показывать в конструкторе: у простого макета
        /// шапка из одной строки, у форматного — уровни шапки и поворот подписей.
        /// </summary>
        private void OnLayoutShapeChanged(object sender, RoutedEventArgs e)
        {
            if (_closedXmlSheet == null)
                return;

            _closedXmlSheet.LayoutKind = FormattedLayoutRadio?.IsChecked == true
                ? ClosedXmlLayoutKinds.Formatted
                : ClosedXmlLayoutKinds.Simple;

            ApplyLayoutShapeVisibility();
            RefreshClosedXmlJson();
        }

        /// <summary>
        /// Показывает средства, относящиеся к выбранному виду макета.
        ///
        /// Раздел «Уровни шапки» от вида не зависит: рендер рисует многоуровневую
        /// шапку всегда, когда уровни заполнены. Скрытое поле всё равно меняло бы
        /// вывод, поэтому вид макета задаёт только подсказки конструктора.
        /// </summary>
        private void ApplyLayoutShapeVisibility()
        {
            var isFormatted = string.Equals(
                _closedXmlSheet?.LayoutKind,
                ClosedXmlLayoutKinds.Formatted,
                StringComparison.OrdinalIgnoreCase);

            if (FormattedLayoutRadio != null)
                FormattedLayoutRadio.IsChecked = isFormatted;
            if (SimpleLayoutRadio != null)
                SimpleLayoutRadio.IsChecked = !isFormatted;

            if (ClosedXmlHeaderLevelsExpander != null)
            {
                // Раздел живёт в блоке «Таблица» и показывается развёрнутым:
                // свёрнутый блок прячет уровни, и они выглядят пропавшими.
                ClosedXmlHeaderLevelsExpander.Visibility = Visibility.Visible;
                ClosedXmlHeaderLevelsExpander.IsExpanded = true;
            }

            // Форматный макет — про уровни шапки, а их показывает редактор
            // таблицы: открываем блок «Таблица», иначе раздел скрыт.
            if (isFormatted)
                SelectTableBlockIfNeeded();

            // Панель выбора вида: ClosedXML — единственный способ макета.
            if (LayoutShapePanel != null)
                LayoutShapePanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Выбирает в списке блок «Таблица», если сейчас выбран другой блок.
        ///
        /// «Уровни шапки» показывает редактор таблицы, поэтому в форматном макете
        /// нужен именно этот блок: иначе при открытии отчёта, где первым идёт
        /// заголовок, раздел остаётся за кадром.
        /// </summary>
        private void SelectTableBlockIfNeeded()
        {
            if (_closedXmlSyncing || _closedXmlSheet == null)
                return;

            if (string.Equals(_closedXmlEditingBlock?.Type, "TabularSection", StringComparison.Ordinal))
                return;

            var tableBlock = _closedXmlBlocks.FirstOrDefault(block =>
                string.Equals(block?.Type, "TabularSection", StringComparison.Ordinal));
            if (tableBlock == null)
                return;

            ClosedXmlBlocksList.SelectedItem = tableBlock;
        }

        private void SelectColorInCombo(ComboBox combo, string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex)) return;

            for (int i = 0; i < combo.Items.Count; i++)
            {
                var item = combo.Items[i] as ComboBoxItem;
                if (item != null && item.Tag is string tag && tag == colorHex)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
       
        // ===================================================================
        // Вкладка «📊 ClosedXML макет»: визуальный конструктор Excel-макета.
        // Модель — ClosedXmlReportLayout, интерпретатор — ClosedXmlReportRenderer.
        // ===================================================================

        private readonly ObservableCollection<ClosedXmlBlock> _closedXmlBlocks = new();
        private readonly ObservableCollection<ClosedXmlCellEdit> _closedXmlCells = new();
        private readonly ObservableCollection<ClosedXmlColumn> _closedXmlColumns = new();
        private readonly ObservableCollection<ClosedXmlSignature> _closedXmlSignatures = new();

        /// <summary>
        /// Ячейки уровней шапки. Плоский список: уровень задаётся номером,
        /// так его удобнее править в таблице.
        /// </summary>
        private readonly ObservableCollection<ClosedXmlHeaderCellEdit> _closedXmlHeaderCells = new();

        /// <summary>Текущий лист макета (вкладка редактирует один лист).</summary>
        private ClosedXmlSheet? _closedXmlSheet;

        /// <summary>Блок, значения которого сейчас показаны в редакторах (для отложенной записи).</summary>
        private ClosedXmlBlock? _closedXmlEditingBlock;

        /// <summary>
        /// Блок, для которого редакторы реально заполнены. Пока это не он, содержимое блока
        /// не переписывается — иначе смена выделения стирала бы строки и колонки.
        /// </summary>
        private ClosedXmlBlock? _closedXmlEditorsBlock;

        /// <summary>Защита от рекурсии при программном заполнении полей.</summary>
        private bool _closedXmlSyncing;

        private void InitializeClosedXmlDesignerState()
        {
            ClosedXmlBlocksList.ItemsSource = _closedXmlBlocks;
            ClosedXmlCellsGrid.ItemsSource = _closedXmlCells;
            ClosedXmlColumnsGrid.ItemsSource = _closedXmlColumns;
            ClosedXmlSignaturesGrid.ItemsSource = _closedXmlSignatures;

            // Меню правой кнопки и подсказки про строки/колонки — кодом,
            // чтобы все четыре таблицы вели себя одинаково.
            AttachClosedXmlGridMenus();
            AttachClosedXmlGridHints();
            ClosedXmlHeaderCellsGrid.ItemsSource = _closedXmlHeaderCells;
            ClosedXmlFieldsList.ItemsSource = _reportFields;
            LoadClosedXmlLayout(ClosedXmlLayoutTemplates.CreateBlank());
            UpdateClosedXmlSourceText();
        }

        /// <summary>Показывает, из какого набора данных строится макет.</summary>
        private void UpdateClosedXmlSourceText()
        {
            var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
            var sourceName = (selected?.Tag as MetadataObject)?.Name;

            ClosedXmlSourceText.Text = string.IsNullOrWhiteSpace(sourceName)
                ? "источник не выбран — задайте его на вкладке «📋 Общие»"
                : $"{sourceName}   ·   полей в отчёте: {_reportFields.Count}";

            ClosedXmlFieldsList.ItemsSource = _reportFields;
            ClosedXmlFieldsList.Items.Refresh();
        }

        private void OnClosedXmlGoToSourceClick(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedItem = GeneralTab;
        }

        private async void OnClosedXmlRefreshFieldsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
                if (selected?.Tag is MetadataObject catalog)
                {
                    CommitDesignerGridEdits();
                    await LoadAvailableFields(catalog);
                    StatusText.Text = $"Поля источника «{catalog.Name}» обновлены";
                }
                else
                {
                    StatusText.Text = "Источник не выбран — задайте его на вкладке «📋 Общие»";
                }

                UpdateClosedXmlSourceText();
                RefreshClosedXmlJson();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось обновить поля: {ex.Message}", "ClosedXML",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Двойной щелчок по полю набора подставляет его в выбранную строку колонок.</summary>
        private void OnClosedXmlFieldDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClosedXmlFieldsList.SelectedItem is not ReportField field || string.IsNullOrWhiteSpace(field.FieldName))
                return;

            if (ClosedXmlColumnsGrid.SelectedItem is not ClosedXmlColumn column)
            {
                StatusText.Text = "Сначала выберите строку в списке «Колонки таблицы»";
                return;
            }

            column.Field = field.FieldName;
            if (string.IsNullOrWhiteSpace(column.Header))
                column.Header = string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldName : field.DisplayName;

            // ClosedXmlColumn — простая модель без INotifyPropertyChanged,
            // поэтому грид обновляем вручную.
            ClosedXmlColumnsGrid.Items.Refresh();
            RefreshClosedXmlJson();
            StatusText.Text = $"Поле «{field.FieldName}» подставлено в колонку";
        }

        /// <summary>
        /// Заполняет колонки новой таблицы полями отчёта — это и есть набор данных
        /// со вкладок «Общие»/«Поля», поэтому имена полей не нужно угадывать.
        /// </summary>
        private void FillClosedXmlTableColumnsFromReportFields(ClosedXmlBlock block)
        {
            if (block.Table == null || _reportFields.Count == 0)
                return;

            var columns = _reportFields
                .Where(field => field.IsVisible && !string.IsNullOrWhiteSpace(field.FieldName))
                .OrderBy(field => field.Order)
                .Select(field => new ClosedXmlColumn
                {
                    Field = field.FieldName,
                    Header = string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldName : field.DisplayName,
                    Width = field.Width > 0 ? Math.Clamp(field.Width / 7d, 8, 40) : 14,
                    NumberFormat = field.Format ?? string.Empty,
                    Align = field.Alignment is "Right" or "Center" or "Left" ? field.Alignment : "Left"
                })
                .ToList();

            if (columns.Count > 0)
                block.Table.Columns = columns;
        }

        private void LoadClosedXmlLayoutFromReport(Report report)
        {
            // Источник макета — только сам отчёт: код ничего не подставляет.
            // У стандартных отчётов макет уже посеян в Reports."ClosedXmlLayout".
            if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout))
                return;

            var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out var error);
            if (layout == null)
            {
                InfoText.Text = $"ClosedXML-макет не прочитан: {error}";
                return;
            }

            LoadClosedXmlLayout(layout);
        }

        /// <summary>Новая ячейка получает следующую колонку, а не колонку 1.</summary>
        private void OnClosedXmlCellAddingNewItem(object sender, AddingNewItemEventArgs e)
        {
            var lastRow = _closedXmlCells.Count == 0
                ? 1
                : _closedXmlCells.Max(cell => cell.Row > 0 ? cell.Row : 1);
            var nextColumn = _closedXmlCells.Count == 0
                ? 1
                : _closedXmlCells.Max(cell => cell.Column) + 1;

            e.NewItem = new ClosedXmlCellEdit
            {
                Row = lastRow,
                Column = nextColumn,
                Align = "Left",
                Border = "None"
            };
        }

        private void LoadClosedXmlLayout(ClosedXmlReportLayout? layout)
        {
            _closedXmlSyncing = true;
            try
            {
                layout ??= ClosedXmlLayoutTemplates.CreateBlank();
                if (layout.Sheets.Count == 0)
                    layout.Sheets.Add(new ClosedXmlSheet());

                _closedXmlSheet = layout.Sheets[0];
                _closedXmlSheet.PageSetup ??= new ClosedXmlPageSetup();

                _closedXmlBlocks.Clear();
                foreach (var block in _closedXmlSheet.Blocks)
                    _closedXmlBlocks.Add(block);

                ClosedXmlSheetNameBox.Text = _closedXmlSheet.Name;
                ClosedXmlGridColumnsBox.Text = _closedXmlSheet.GridColumns > 0
                    ? _closedXmlSheet.GridColumns.ToString()
                    : string.Empty;
                ClosedXmlGridColumnWidthBox.Text = _closedXmlSheet.GridColumnWidth > 0
                    ? _closedXmlSheet.GridColumnWidth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty;
                ClosedXmlOrientationCombo.SelectedIndex =
                    string.Equals(_closedXmlSheet.PageSetup.Orientation, "Portrait", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                ClosedXmlFreezeRowsBox.Text = _closedXmlSheet.PageSetup.FreezeRows.ToString();
                ClosedXmlPageHeaderBox.Text = _closedXmlSheet.PageSetup.PageHeaderText;
                ClosedXmlPageFooterBox.Text = _closedXmlSheet.PageSetup.PageFooterText;
                ClosedXmlShowPageHeaderCheck.IsChecked = _closedXmlSheet.PageSetup.ShowPageHeader;
                ClosedXmlFontCombo.Text = _closedXmlSheet.PageSetup.FontName;
                ClosedXmlFontSizeBox.Text = _closedXmlSheet.PageSetup.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ClosedXmlGridLinesCheck.IsChecked = _closedXmlSheet.PageSetup.ShowGridLines;

                // Форматный макет открываем на блоке «Таблица»: в нём уровни шапки,
                // ради которых этот вид и выбирают.
                var initialBlock = string.Equals(
                    _closedXmlSheet.LayoutKind,
                    ClosedXmlLayoutKinds.Formatted,
                    StringComparison.OrdinalIgnoreCase)
                    ? _closedXmlBlocks.FirstOrDefault(block =>
                        string.Equals(block?.Type, "TabularSection", StringComparison.Ordinal))
                    : null;
                initialBlock ??= _closedXmlBlocks.FirstOrDefault();

                ClosedXmlBlocksList.SelectedItem = initialBlock;
                LoadClosedXmlBlockEditors(initialBlock);

                // Вид макета хранится в листе: показываем его состояние.
                ApplyLayoutShapeVisibility();
            }
            finally
            {
                _closedXmlSyncing = false;
            }

            RefreshClosedXmlJson();
        }

        /// <summary>Показывает в редакторах выбранный блок.</summary>
        private void LoadClosedXmlBlockEditors(ClosedXmlBlock? block)
        {
            _closedXmlCells.Clear();
            _closedXmlColumns.Clear();
            _closedXmlSignatures.Clear();
            _closedXmlHeaderCells.Clear();
            _closedXmlEditingBlock = block;
            _closedXmlEditorsBlock = null;

            var isTable = string.Equals(block?.Type, "TabularSection", StringComparison.Ordinal);
            var isSignatures = string.Equals(block?.Type, "SignaturesRow", StringComparison.Ordinal);

            ClosedXmlCellsGrid.Visibility = !isTable && !isSignatures ? Visibility.Visible : Visibility.Collapsed;
            ClosedXmlTableEditor.Visibility = isTable ? Visibility.Visible : Visibility.Collapsed;
            ClosedXmlSignaturesGrid.Visibility = isSignatures ? Visibility.Visible : Visibility.Collapsed;

            // Подпись над таблицами: что правится сейчас и что добавит кнопка.
            ClosedXmlEditorTargetText.Text = isTable
                ? "Правятся: колонки таблицы, уровни шапки и итоги"
                : isSignatures
                    ? "Правятся: строки подписей"
                    : "Правятся: ячейки блока — строка, колонка, объединение, текст";

            if (ClosedXmlAddCellButton != null)
            {
                ClosedXmlAddCellButton.Content = isTable
                    ? "+ Добавить колонку"
                    : isSignatures
                        ? "+ Добавить подпись"
                        : "+ Добавить ячейку";
            }

            ClosedXmlBlockNameBox.Text = block?.Name ?? string.Empty;
            if (block == null)
                return;

            if (isTable)
            {
                block.Table ??= new ClosedXmlTableSection();
                foreach (var column in block.Table.Columns)
                    _closedXmlColumns.Add(column);

                ClosedXmlHeaderFillBox.Text = block.Table.HeaderFill;
                ClosedXmlAlternateFillBox.Text = block.Table.AlternateFill;
                SelectComboByContent(ClosedXmlRowBorderCombo, block.Table.RowBorder);

                // Уровни шапки: плоский список с номером уровня.
                for (var levelIndex = 0; levelIndex < block.Table.HeaderLevels.Count; levelIndex++)
                {
                    foreach (var headerCell in block.Table.HeaderLevels[levelIndex].Cells)
                        _closedXmlHeaderCells.Add(ClosedXmlHeaderCellEdit.FromModel(levelIndex, headerCell));
                }

                ClosedXmlHeaderHeightBox.Text = block.Table.HeaderRowHeight > 0
                    ? block.Table.HeaderRowHeight.ToString("0.#")
                    : string.Empty;

                var group = block.Table.Groups.FirstOrDefault();
                ClosedXmlGroupFieldsBox.Text = group == null ? string.Empty : string.Join(", ", group.ByFields);
                ClosedXmlGroupHeaderBox.Text = group?.HeaderFormat ?? "{value}";
                ClosedXmlSubtotalLabelBox.Text = group?.Subtotal?.Label ?? string.Empty;
                ClosedXmlSubtotalFieldsBox.Text = group?.Subtotal == null ? string.Empty : string.Join(", ", group.Subtotal.SumFields);
                ClosedXmlGrandTotalLabelBox.Text = block.Table.GrandTotal?.Label ?? string.Empty;
                ClosedXmlGrandTotalFieldsBox.Text = block.Table.GrandTotal == null ? string.Empty : string.Join(", ", block.Table.GrandTotal.SumFields);
                ClosedXmlExtraTotalsBox.Text = FormatExtraTotals(block.Table.ExtraTotals);
            }
            else if (isSignatures)
            {
                foreach (var signature in block.Signatures)
                    _closedXmlSignatures.Add(signature);
            }
            else
            {
                var index = 0;
                foreach (var row in block.Rows)
                {
                    index++;
                    var rowNumber = row.Row > 0 ? row.Row : index;
                    foreach (var cell in row.Cells)
                        _closedXmlCells.Add(ClosedXmlCellEdit.FromModel(rowNumber, cell));
                }
            }

            // Редакторы считаются загруженными только для этого блока: фиксация правок
            // переписывает содержимое блока лишь тогда, когда его редакторы на экране.
            _closedXmlEditorsBlock = block;
        }

        /// <summary>Переносит значения редакторов обратно в модель текущего блока и листа.</summary>
        private void CommitClosedXmlBlockEditors() => CommitClosedXmlBlockEditors(_closedXmlEditingBlock);

        /// <summary>
        /// Разворачивает плоский список ячеек шапки в уровни.
        ///
        /// Уровни идут по возрастанию номера: 0 — верхний. Пустые ячейки
        /// отбрасываются, чтобы лишняя строка в таблице не портила шапку.
        /// </summary>
        private List<ClosedXmlHeaderLevel> BuildHeaderLevelsFromEditor()
        {
            var filled = _closedXmlHeaderCells
                .Where(cell => cell.Column > 0 && (cell.MergeToColumn > 0 || !string.IsNullOrWhiteSpace(cell.Text)))
                .ToList();

            if (filled.Count == 0)
                return new List<ClosedXmlHeaderLevel>();

            return filled
                .GroupBy(cell => Math.Max(0, cell.LevelIndex))
                .OrderBy(group => group.Key)
                .Select(group => new ClosedXmlHeaderLevel
                {
                    Name = $"Уровень {group.Key + 1}",
                    Cells = group
                        .OrderBy(cell => cell.Column)
                        .Select(cell => cell.ToModel())
                        .ToList()
                })
                .ToList();
        }

        /// <summary>
        /// Разбирает дополнительные итоговые строки бланка.
        ///
        /// Формат строки: «Метка | поля сумм через запятую». Метка вида @поле берёт
        /// текст из набора данных — так печатается «Налог с продаж по ставке 0.00 %».
        /// </summary>
        private static List<ClosedXmlTotal> ParseExtraTotals(string? text)
        {
            var totals = new List<ClosedXmlTotal>();
            if (string.IsNullOrWhiteSpace(text))
                return totals;

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                var label = parts[0].Trim();
                var fields = parts.Length > 1 ? SplitClosedXmlList(parts[1]) : new List<string>();

                if (string.IsNullOrWhiteSpace(label) && fields.Count == 0)
                    continue;

                var total = new ClosedXmlTotal { SumFields = fields };
                if (label.StartsWith('@'))
                {
                    // Метка собирается в наборе данных: в настройке храним только имя поля.
                    total.LabelField = label[1..].Trim();
                    total.Label = total.LabelField;
                }
                else
                {
                    total.Label = label;
                }

                totals.Add(total);
            }

            return totals;
        }

        /// <summary>Показывает дополнительные итоги в редакторе: по строке на итог.</summary>
        private static string FormatExtraTotals(IEnumerable<ClosedXmlTotal>? totals)
        {
            if (totals == null)
                return string.Empty;

            return string.Join(Environment.NewLine, totals
                .Where(total => total != null)
                .Select(total =>
                {
                    var label = string.IsNullOrWhiteSpace(total.LabelField)
                        ? total.Label
                        : $"@{total.LabelField}";

                    return $"{label} | {string.Join(", ", total.SumFields)}";
                }));
        }

        /// <summary>Фиксирует незавершённое редактирование гридов макета перед чтением модели.</summary>
        private void CommitClosedXmlGridEdits()
        {
            TryCommitGrid(ClosedXmlCellsGrid);
            TryCommitGrid(ClosedXmlColumnsGrid);
            TryCommitGrid(ClosedXmlSignaturesGrid);
            TryCommitGrid(ClosedXmlHeaderCellsGrid);
        }

        private void CommitClosedXmlBlockEditors(ClosedXmlBlock? block)
        {
            if (_closedXmlSyncing || _closedXmlSheet == null)
                return;

            CommitClosedXmlGridEdits();

            // Содержимое блока переписывается только тогда, когда в редакторах загружен
            // именно этот блок. Иначе любая смена выделения стирала бы строки и колонки.
            if (block != null && ReferenceEquals(block, _closedXmlEditorsBlock))
            {
                block.Name = ClosedXmlBlockNameBox.Text ?? string.Empty;

                if (string.Equals(block.Type, "TabularSection", StringComparison.Ordinal))
                {
                    block.Table ??= new ClosedXmlTableSection();
                    block.Table.Columns = _closedXmlColumns.ToList();
                    block.Table.HeaderFill = ClosedXmlHeaderFillBox.Text ?? string.Empty;
                    block.Table.AlternateFill = ClosedXmlAlternateFillBox.Text ?? string.Empty;
                    block.Table.RowBorder = (ClosedXmlRowBorderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

                    var groupFields = SplitClosedXmlList(ClosedXmlGroupFieldsBox.Text);
                    if (groupFields.Count > 0)
                    {
                        var group = block.Table.Groups.FirstOrDefault() ?? new ClosedXmlGroup();
                        group.ByFields = groupFields;
                        group.HeaderFormat = string.IsNullOrWhiteSpace(ClosedXmlGroupHeaderBox.Text)
                            ? "{value}"
                            : ClosedXmlGroupHeaderBox.Text;
                        group.ShowHeader = true;

                        var subtotalLabel = ClosedXmlSubtotalLabelBox.Text?.Trim() ?? string.Empty;
                        group.Subtotal = string.IsNullOrWhiteSpace(subtotalLabel)
                            ? null
                            : new ClosedXmlTotal
                            {
                                Label = subtotalLabel,
                                SumFields = SplitClosedXmlList(ClosedXmlSubtotalFieldsBox.Text)
                            };

                        block.Table.Groups = new List<ClosedXmlGroup> { group };
                    }
                    else
                    {
                        block.Table.Groups = new List<ClosedXmlGroup>();
                    }

                    var grandLabel = ClosedXmlGrandTotalLabelBox.Text?.Trim() ?? string.Empty;
                    block.Table.GrandTotal = string.IsNullOrWhiteSpace(grandLabel)
                        ? null
                        : new ClosedXmlTotal
                        {
                            Label = grandLabel,
                            SumFields = SplitClosedXmlList(ClosedXmlGrandTotalFieldsBox.Text)
                        };

                    // Дополнительные итоги: «Налог с продаж по ставке … %», «ВСЕГО».
                    block.Table.ExtraTotals = ParseExtraTotals(ClosedXmlExtraTotalsBox.Text);

                    // Уровни шапки: плоский список разворачивается в список уровней.
                    block.Table.HeaderLevels = BuildHeaderLevelsFromEditor();

                    block.Table.HeaderRowHeight = double.TryParse(
                        ClosedXmlHeaderHeightBox.Text, out var headerHeight) && headerHeight > 0
                        ? headerHeight
                        : 0;
                }
                else if (string.Equals(block.Type, "SignaturesRow", StringComparison.Ordinal))
                {
                    block.Signatures = _closedXmlSignatures
                        .Where(signature => !string.IsNullOrWhiteSpace(signature.Text))
                        .ToList();
                }
                else
                {
                    // Плоский список ячеек собираем обратно в строки по номеру строки.
                    var rows = new List<ClosedXmlTextRow>();
                    foreach (var group in _closedXmlCells
                                 .Where(cell => !string.IsNullOrWhiteSpace(cell.Text)
                                                || cell.MergeToColumn > 0
                                                || cell.TakeFromRow != 0)
                                 .GroupBy(cell => cell.Row > 0 ? cell.Row : 1)
                                 .OrderBy(group => group.Key))
                    {
                        var row = new ClosedXmlTextRow { Row = group.Key };
                        foreach (var cell in group.OrderBy(cell => cell.Column))
                            row.Cells.Add(cell.ToModel());
                        rows.Add(row);
                    }

                    block.Rows = rows;
                }
            }

            _closedXmlSheet.Name = string.IsNullOrWhiteSpace(ClosedXmlSheetNameBox.Text)
                ? "Лист1"
                : ClosedXmlSheetNameBox.Text.Trim();
            // Узкая сетка листа: 0 — сетки нет, колонок столько же, сколько графов таблицы.
            _closedXmlSheet.GridColumns = Math.Max(0, ParseIntSafe(ClosedXmlGridColumnsBox.Text));
            _closedXmlSheet.GridColumnWidth = ParseDoubleSafe(ClosedXmlGridColumnWidthBox.Text, 0);

            _closedXmlSheet.PageSetup ??= new ClosedXmlPageSetup();
            _closedXmlSheet.PageSetup.Orientation = ClosedXmlOrientationCombo.SelectedIndex == 1 ? "Portrait" : "Landscape";
            _closedXmlSheet.PageSetup.FreezeRows = ParseIntSafe(ClosedXmlFreezeRowsBox.Text);
            _closedXmlSheet.PageSetup.FontName = string.IsNullOrWhiteSpace(ClosedXmlFontCombo.Text)
                ? "Times New Roman"
                : ClosedXmlFontCombo.Text;
            _closedXmlSheet.PageSetup.FontSize = ParseDoubleSafe(ClosedXmlFontSizeBox.Text, 10);
            _closedXmlSheet.PageSetup.ShowGridLines = ClosedXmlGridLinesCheck.IsChecked ?? false;
            _closedXmlSheet.PageSetup.PageHeaderText = ClosedXmlPageHeaderBox.Text?.Trim() ?? string.Empty;
            _closedXmlSheet.PageSetup.PageFooterText = ClosedXmlPageFooterBox.Text?.Trim() ?? string.Empty;
            _closedXmlSheet.PageSetup.ShowPageHeader = ClosedXmlShowPageHeaderCheck.IsChecked ?? true;
            _closedXmlSheet.Blocks = _closedXmlBlocks.ToList();
        }

        /// <summary>Собирает модель макета из текущего состояния редактора.</summary>
        private ClosedXmlReportLayout? BuildClosedXmlLayout()
        {
            if (_closedXmlSheet == null)
                return null;

            CommitClosedXmlBlockEditors();
            return new ClosedXmlReportLayout
            {
                Version = 1,
                Sheets = new List<ClosedXmlSheet> { _closedXmlSheet }
            };
        }

        private string BuildClosedXmlLayoutJson()
        {
            var layout = BuildClosedXmlLayout();
            return layout == null ? string.Empty : ClosedXmlReportRenderer.SerializeLayout(layout);
        }

        private void RefreshClosedXmlJson()
        {
            var layout = BuildClosedXmlLayout();
            ClosedXmlJsonBox.Text = layout == null ? string.Empty : ClosedXmlReportRenderer.SerializeLayout(layout);

            // Табличный вид показывает тот же макет листом — обновляем его вместе с JSON.
            RefreshClosedXmlSheetViewAllBlocks();
        }

        /// <summary>
        /// Добавляет ячейку блока «Заголовок»/«Строки» через диалог.
        ///
        /// Таблица-список остаётся для просмотра, а ввод идёт словами:
        /// «строка сверху вниз», «колонка слева направо», «объединить по колонку».
        /// Остальное (кегль, поворот, рамка, заливка) правится в таблице после добавления.
        /// </summary>
        private void OnAddClosedXmlCellClick(object sender, RoutedEventArgs e)
        {
            if (_closedXmlSheet == null)
                return;

            var nextRow = _closedXmlCells.Count == 0 ? 1 : _closedXmlCells.Max(cell => Math.Max(1, cell.Row));
            var nextColumn = _closedXmlCells.Count == 0
                ? 1
                : _closedXmlCells.Max(cell => Math.Max(1, cell.Column)) + 1;

            var fields = _reportFields
                .Select(field => field.FieldName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var blockName = string.IsNullOrWhiteSpace(_closedXmlEditingBlock?.Name)
                ? _closedXmlEditingBlock?.TypeLabel ?? "блок"
                : _closedXmlEditingBlock!.Name;

            // У таблицы добавляется колонка таблицы, а не ячейка заголовка.
            if (string.Equals(_closedXmlEditingBlock?.Type, "TabularSection", StringComparison.Ordinal))
            {
                var columnDialog = new ClosedXmlColumnDialog(blockName, fields) { Owner = this };
                if (columnDialog.ShowDialog() == true && columnDialog.Result != null)
                {
                    _closedXmlColumns.Add(columnDialog.Result);
                    StatusText.Text = $"Колонка добавлена в таблицу «{blockName}».";
                }

                return;
            }

            var dialog = new ClosedXmlCellDialog(_closedXmlSheet.GridColumns, fields, nextRow, nextColumn, blockName)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.Result == null)
                return;

            _closedXmlCells.Add(dialog.Result);
            StatusText.Text = "Ячейка добавлена.";
        }

        /// <summary>Удаляет выбранную строку таблицы ячеек.</summary>
        private void OnDeleteClosedXmlCellClick(object sender, RoutedEventArgs e)
        {
            if (ClosedXmlCellsGrid.SelectedItem is not ClosedXmlCellEdit cell)
            {
                StatusText.Text = "Выберите строку в таблице ячеек.";
                return;
            }

            _closedXmlCells.Remove(cell);
            StatusText.Text = "Строка удалена.";
        }

        /// <summary>
        /// Добавляет строку в ту таблицу, по которой щёлкнули правой кнопкой.
        ///
        /// Контекстное меню есть у каждой сетки: строка добавляется там,
        /// где стоит курсор, и не приходится гадать, к чему относится кнопка.
        /// </summary>
        private void OnGridAddRowClick(object sender, RoutedEventArgs e)
        {
            var grid = GetContextMenuGrid(sender);
            if (grid == null)
                return;

            switch (grid.Name)
            {
                case "ClosedXmlCellsGrid":
                    OnAddClosedXmlCellClick(sender, e);
                    break;

                case "ClosedXmlColumnsGrid":
                    _closedXmlColumns.Add(new ClosedXmlColumn
                    {
                        Field = string.Empty,
                        Header = "Колонка",
                        Width = 14,
                        Align = "Left"
                    });
                    StatusText.Text = "Колонка добавлена: заполните поле набора и заголовок.";
                    break;

                case "ClosedXmlHeaderCellsGrid":
                    _closedXmlHeaderCells.Add(new ClosedXmlHeaderCellEdit
                    {
                        LevelIndex = 0,
                        Column = 1,
                        Align = "Center"
                    });
                    StatusText.Text = "Строка шапки добавлена: уровень 0 — верхний.";
                    break;

                case "ClosedXmlSignaturesGrid":
                    _closedXmlSignatures.Add(new ClosedXmlSignature
                    {
                        Text = "Подпись",
                        Column = 1,
                        MergeToColumn = 4
                    });
                    StatusText.Text = "Строка подписей добавлена.";
                    break;
            }
        }

        /// <summary>Удаляет выбранную строку таблицы — с подтверждением.</summary>
        private void OnGridDeleteRowClick(object sender, RoutedEventArgs e)
        {
            var grid = GetContextMenuGrid(sender);
            if (grid?.SelectedItem == null)
            {
                StatusText.Text = "Выберите строку в таблице.";
                return;
            }

            var answer = MessageBox.Show(
                "Удалить выбранную строку?",
                "Удаление строки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            switch (grid.Name)
            {
                case "ClosedXmlCellsGrid" when grid.SelectedItem is ClosedXmlCellEdit cell:
                    _closedXmlCells.Remove(cell);
                    break;

                case "ClosedXmlColumnsGrid" when grid.SelectedItem is ClosedXmlColumn column:
                    _closedXmlColumns.Remove(column);
                    break;

                case "ClosedXmlHeaderCellsGrid" when grid.SelectedItem is ClosedXmlHeaderCellEdit headerCell:
                    _closedXmlHeaderCells.Remove(headerCell);
                    break;

                case "ClosedXmlSignaturesGrid" when grid.SelectedItem is ClosedXmlSignature signature:
                    _closedXmlSignatures.Remove(signature);
                    break;

                default:
                    return;
            }

            StatusText.Text = "Строка удалена.";
        }

        /// <summary>Таблица, по которой вызвали контекстное меню.</summary>
        private static DataGrid? GetContextMenuGrid(object sender) =>
            ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget as DataGrid;

        /// <summary>
        /// Меню правой кнопки для таблицы подписей: у остальных таблиц меню
        /// задано в разметке, здесь оно вешается кодом — так одинаково
        /// для всех четырёх.
        /// </summary>
        private void AttachClosedXmlGridMenus()
        {
            if (ClosedXmlSignaturesGrid == null || ClosedXmlSignaturesGrid.ContextMenu != null)
                return;

            var menu = new ContextMenu();

            var addItem = new MenuItem { Header = "Добавить строку" };
            addItem.Click += OnGridAddRowClick;
            menu.Items.Add(addItem);

            var deleteItem = new MenuItem { Header = "Удалить строку" };
            deleteItem.Click += OnGridDeleteRowClick;
            menu.Items.Add(deleteItem);

            ClosedXmlSignaturesGrid.ContextMenu = menu;
        }

        /// <summary>
        /// Подсказки в шапках таблиц: что означают строка и колонка.
        /// Наводите мышь на таблицу — видно правило чтения координат.
        /// </summary>
        private void AttachClosedXmlGridHints()
        {
            ToolTipService.SetToolTip(ClosedXmlCellsGrid,
                "Ячейки блока: Строка — горизонтальная полоса сверху вниз (1 — верх листа), " +
                "Колонка — вертикальная полоса слева направо (1 — левый край), «До кол.» — объединение.");

            ToolTipService.SetToolTip(ClosedXmlTableEditor,
                "Таблица: «Колонки таблицы» — графы бланка (поле набора, заголовок, ширина, формат), " +
                "«Уровни шапки» — подписи графов (Уровень 0 — верхний, Строк — вертикальное объединение).");

            ToolTipService.SetToolTip(ClosedXmlSignaturesGrid,
                "Подписи: Текст подписи, Колонка — слева направо, До кол. — объединение.");
        }

        /// <summary>
        /// Масштаб табличного вида: по лзунку или по Ctrl+колесу.
        ///
        /// Увеличивается сам лист (LayoutTransform), поэтому полосы прокрутки
        /// считают уже увеличенный размер — ничего не обрезается.
        /// </summary>
        private void ApplyClosedXmlSheetZoom(double zoomPercent)
        {
            if (ClosedXmlSheetGridHost == null)
                return;

            var clamped = Math.Clamp(zoomPercent, 50, 200);
            var scale = clamped / 100d;

            ClosedXmlSheetGridHost.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);

            if (ClosedXmlSheetZoomText != null)
                ClosedXmlSheetZoomText.Text = $"{Math.Round(clamped)}%";

            if (ClosedXmlSheetZoomSlider != null && Math.Abs(ClosedXmlSheetZoomSlider.Value - clamped) > 0.5)
                ClosedXmlSheetZoomSlider.Value = clamped;
        }

        private void OnClosedXmlSheetZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            ApplyClosedXmlSheetZoom(e.NewValue);

        /// <summary>
        /// Колесо мыши: с зажатым Ctrl — масштаб, без Ctrl — обычная прокрутка листа.
        /// Так же ведёт себя Excel, поэтому привычка не ломается.
        /// </summary>
        private void OnClosedXmlSheetPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control)
                return;

            ApplyClosedXmlSheetZoom((ClosedXmlSheetZoomSlider?.Value ?? 100) + (e.Delta > 0 ? 10 : -10));
            e.Handled = true;
        }

        private void OnRefreshClosedXmlTableClick(object sender, RoutedEventArgs e) => RefreshClosedXmlSheetViewAllBlocks();

        /// <summary>
        /// Рисует ВЕСЬ лист как таблицу: все блоки сверху вниз, как при печати.
        ///
        /// Раньше вид строился по текущему блоку и зависел от выбора в списке блоков —
        /// теперь рисуется лист целиком: заголовки и строки, шапка таблицы с уровнями и
        /// графы, итоги, подписи. Строки считаются сверху вниз, колонки слева направо.
        /// </summary>
        private void RefreshClosedXmlSheetViewAllBlocks()
        {
            if (_closedXmlSheet == null || ClosedXmlSheetGridHost == null)
                return;

            ClosedXmlSheetGridHost.Children.Clear();
            ClosedXmlSheetGridHost.ColumnDefinitions.Clear();
            ClosedXmlSheetGridHost.RowDefinitions.Clear();

            var columns = _closedXmlSheet.GridColumns > 0 ? _closedXmlSheet.GridColumns : 40;
            var rows = new List<List<(int Column, int Span, string Text, string Info, string Fill,
                ClosedXmlSheetTarget? Target)>>();

            foreach (var block in _closedXmlSheet.Blocks)
            {
                if (block == null)
                    continue;

                if (string.Equals(block.Type, "TabularSection", StringComparison.Ordinal) && block.Table != null)
                {
                    var levelIndex = 0;
                    foreach (var level in block.Table.HeaderLevels)
                    {
                        var levelCells = new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>();
                        foreach (var headerCell in level.Cells)
                        {
                            var from = Math.Max(1, headerCell.Column);
                            var to = headerCell.MergeToColumn > from ? headerCell.MergeToColumn : from;
                            levelCells.Add((from, Math.Max(1, to - from + 1), headerCell.Text ?? string.Empty,
                                $"Шапка таблицы: уровень {levelIndex}, колонки {from}–{to}", headerCell.Fill ?? string.Empty,
                                new ClosedXmlSheetTarget
                                {
                                    Block = block,
                                    HeaderCell = headerCell,
                                    HeaderLevelIndex = levelIndex,
                                    Info = $"Ячейка шапки: уровень {levelIndex + 1}, колонки {from}–{to}"
                                }));
                        }

                        rows.Add(levelCells);
                        levelIndex++;
                    }

                    // Строка данных: каждая графа на своих узких колонках.
                    var columnCells = new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>();
                    var position = 1;
                    foreach (var column in block.Table.Columns)
                    {
                        if (column == null)
                            continue;

                        var span = column.Span > 0 ? column.Span : 1;
                        columnCells.Add((position, span, column.Header ?? column.Field ?? string.Empty,
                            $"Графа: поле «{column.Field}», ширина {column.Width:0.#}, формат «{column.NumberFormat}»",
                            string.Empty,
                            new ClosedXmlSheetTarget
                            {
                                Block = block,
                                Column = column,
                                Info = $"Графа таблицы: поле «{column.Field}»"
                            }));
                        position += span;
                    }

                    rows.Add(columnCells);

                    if (block.Table.GrandTotal != null)
                        rows.Add(new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>
                        {
                            (1, columns, block.Table.GrandTotal.Label, "Общий итог таблицы", "#EEF2F6",
                                new ClosedXmlSheetTarget
                                {
                                    Block = block, Total = block.Table.GrandTotal, Info = "Общий итог таблицы"
                                })
                        });

                    foreach (var total in block.Table.ExtraTotals)
                        rows.Add(new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>
                        {
                            (1, columns, total.Label, "Дополнительный итог", "#EEF2F6",
                                new ClosedXmlSheetTarget
                                {
                                    Block = block, Total = total, Info = "Дополнительный итог"
                                })
                        });

                    continue;
                }

                if (string.Equals(block.Type, "SignaturesRow", StringComparison.Ordinal))
                {
                    var signatureCells = new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>();
                    foreach (var signature in block.Signatures)
                    {
                        if (signature == null)
                            continue;

                        var from = Math.Max(1, signature.Column);
                        var to = signature.MergeToColumn > from ? signature.MergeToColumn : from;
                        signatureCells.Add((from, Math.Max(1, to - from + 1), signature.Text ?? string.Empty,
                            $"Подпись: колонки {from}–{to}", string.Empty,
                            new ClosedXmlSheetTarget
                            {
                                Block = block, Signature = signature, Info = $"Подпись: колонки {from}–{to}"
                            }));
                    }

                    rows.Add(signatureCells);
                    continue;
                }

                // Текстовые блоки: каждая строка блока — строка листа.
                foreach (var textRow in block.Rows)
                {
                    if (textRow == null)
                        continue;

                    var textCells = new List<(int, int, string, string, string, ClosedXmlSheetTarget?)>();
                    foreach (var cellLayout in textRow.Cells.OrderBy(item => item.Column))
                    {
                        if (cellLayout == null)
                            continue;

                        var from = Math.Max(1, cellLayout.Column);
                        var span = cellLayout.MergeToColumn > from ? cellLayout.MergeToColumn - from + 1 : 1;
                        var hasField = !string.IsNullOrWhiteSpace(cellLayout.TakeFromField);
                        var text = hasField ? cellLayout.TakeFromField! : cellLayout.Text ?? string.Empty;

                        // Клетка хранится моделью: диалог откроется и тогда, когда списки
                        // показывают другой блок — их переключит OpenTextCellEditor.
                        var rowTarget = new ClosedXmlSheetTarget
                        {
                            Block = block,
                            TextCell = cellLayout,
                            TextRow = textRow.Row,
                            Info = hasField
                                ? $"Строка {textRow.Row}, колонка {from}: значение из поля «{cellLayout.TakeFromField}» (строка набора {cellLayout.TakeFromRow})"
                                : $"Строка {textRow.Row}, колонка {from}: текст «{cellLayout.Text}»"
                        };

                        textCells.Add((from, span, text, rowTarget.Info,
                            cellLayout.Fill ?? string.Empty, rowTarget));
                    }

                    rows.Add(textCells);
                }
            }

            var rowCount = Math.Max(1, rows.Count);

            // Первая колонка — номера строк, первая строка — номера колонок, как в Excel.
            ClosedXmlSheetGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            for (var index = 0; index < columns; index++)
                ClosedXmlSheetGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

            ClosedXmlSheetGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            for (var index = 0; index < rowCount; index++)
                ClosedXmlSheetGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

            // Угол и нумерация: сверху — номера колонок, слева — номера строк.
            ClosedXmlSheetGridHost.Children.Add(CreateGridHeaderCell(0, 0, string.Empty, "#E8EDF2"));
            for (var index = 0; index < columns; index++)
                ClosedXmlSheetGridHost.Children.Add(CreateGridHeaderCell(index + 1, 0, (index + 1).ToString(), "#E8EDF2"));

            for (var index = 0; index < rowCount; index++)
                ClosedXmlSheetGridHost.Children.Add(CreateGridHeaderCell(0, index + 1, (index + 1).ToString(), "#F3F5F7"));

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                foreach (var item in rows[rowIndex])
                {
                    var column = Math.Clamp(item.Column - 1, 0, columns - 1);
                    var span = Math.Clamp(item.Span, 1, columns - column);

                    var blockCell = new Border
                    {
                        BorderBrush = System.Windows.Media.Brushes.LightGray,
                        BorderThickness = new Thickness(0.5),
                        Background = ParseGridCellBrush(item.Fill),
                        Padding = new Thickness(2, 0, 2, 0),
                        ToolTip = item.Info,
                        Child = new TextBlock
                        {
                            Text = item.Text,
                            FontSize = 9,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };

                    var target = item.Target;
                    blockCell.MouseLeftButtonUp += (_, _) => SelectSheetTarget(target, item.Info);

                    if (target != null && target.HasEditor)
                    {
                        // Двойной щелчок и повторный щелчок по уже выбранной клетке
                        // открывают диалог того элемента, который стоит на этом месте.
                        blockCell.MouseLeftButtonDown += (_, args) =>
                        {
                            if (args.ClickCount == 2 || ReferenceEquals(_sheetSelectedTarget, target))
                                OpenSheetTargetEditor(target);
                        };
                        blockCell.MouseRightButtonUp += (_, _) => ShowSheetTargetMenu(blockCell, target);
                    }

                    // +1 — сдвиг под шапку с нумерацией: колонка 1 листа — вторая на экране.
                    Grid.SetColumn(blockCell, column + 1);
                    Grid.SetRow(blockCell, rowIndex + 1);
                    Grid.SetColumnSpan(blockCell, span);
                    ClosedXmlSheetGridHost.Children.Add(blockCell);
                }
            }

            ClosedXmlTableCellInfo.Text = $"Лист целиком: блоков {_closedXmlSheet.Blocks.Count}, строк {rowCount}, колонок {columns}. Щёлкните по клетке.";
        }

        /// <summary>
        /// Рисует лист как таблицу: строки × колонки, клетки с текстом или полем,
        /// объединения и заливка как в бланке. Это второй вид на тот же макет
        /// (данные те же — общая модель), поэтому правки в списках видны и здесь.
        ///
        /// Щелчок по клетке показывает её координаты и содержимое — новичку
        /// так понятнее, что такое «строка» и «колонка».
        /// </summary>
        private void RefreshClosedXmlSheetView()
        {
            if (_closedXmlSheet == null || ClosedXmlSheetGridHost == null)
                return;

            ClosedXmlSheetGridHost.Children.Clear();
            ClosedXmlSheetGridHost.ColumnDefinitions.Clear();
            ClosedXmlSheetGridHost.RowDefinitions.Clear();

            var lastColumn = _closedXmlCells.Count == 0
                ? 1
                : _closedXmlCells.Max(cell => Math.Max(1,
                    cell.MergeToColumn > cell.Column ? cell.MergeToColumn : cell.Column));
            var lastRow = _closedXmlCells.Count == 0 ? 1 : _closedXmlCells.Max(cell => Math.Max(1, cell.Row));

            // Колонок столько же, сколько у листа: в узкой сетке — её число.
            var columns = _closedXmlSheet.GridColumns > 0
                ? _closedXmlSheet.GridColumns
                : Math.Min(lastColumn, 40);
            var rows = Math.Min(lastRow, 200);

            for (var index = 0; index < columns; index++)
                ClosedXmlSheetGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

            for (var index = 0; index < rows; index++)
                ClosedXmlSheetGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });

            foreach (var cell in _closedXmlCells)
            {
                var column = Math.Clamp(cell.Column - 1, 0, columns - 1);
                var row = Math.Clamp(cell.Row - 1, 0, rows - 1);
                var span = cell.MergeToColumn > cell.Column
                    ? Math.Min(columns - column, cell.MergeToColumn - cell.Column + 1)
                    : 1;

                var hasField = !string.IsNullOrWhiteSpace(cell.TakeFromField);
                var text = hasField ? cell.TakeFromField! : cell.Text ?? string.Empty;

                var info = $"Клетка: строка {cell.Row}, колонка {cell.Column}" +
                           (span > 1 ? $", объединение до {cell.Column + span - 1}" : string.Empty) +
                           (hasField
                               ? $", значение из поля «{cell.TakeFromField}» (строка набора {cell.TakeFromRow})"
                               : $", текст «{cell.Text}»");

                var block = new Border
                {
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new Thickness(0.5),
                    Background = ParseGridCellBrush(cell.Fill),
                    Padding = new Thickness(2, 0, 2, 0),
                    ToolTip = info,
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 9,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                block.MouseLeftButtonUp += (_, _) => ClosedXmlTableCellInfo.Text = info;
                // Этап 2: правка прямо в сетке — двойной щелчок и правая кнопка.
                block.MouseLeftButtonDown += (_, args) =>
                {
                    if (args.ClickCount == 2)
                        EditSheetCell(cell);
                };
                block.MouseRightButtonUp += (_, _) => ShowSheetCellMenu(block, cell);

                Grid.SetColumn(block, column);
                Grid.SetRow(block, row);
                Grid.SetColumnSpan(block, span);
                ClosedXmlSheetGridHost.Children.Add(block);
            }

            ClosedXmlTableCellInfo.Text = $"Клеток: {_closedXmlCells.Count}; строк видно: {rows}, колонок: {columns}. Щёлкните по клетке.";
        }

        /// <summary>
        /// Ячейка нумерации: номера колонок сверху и строк слева, как в Excel.
        /// </summary>
        private Border CreateGridHeaderCell(int column, int row, string text, string fill)
        {
            var cell = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.Silver,
                BorderThickness = new Thickness(0.5),
                Background = ParseGridCellBrush(fill),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.DimGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = column == 0 && row == 0
                    ? "Лист: сверху номера колонок, слева номера строк"
                    : column == 0
                        ? $"Строка {row} листа"
                        : $"Колонка {column} листа"
            };

            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, row);
            return cell;
        }

        /// <summary>Заливка клетки для табличного вида.</summary>
        private static System.Windows.Media.Brush ParseGridCellBrush(string? fill)
        {
            if (string.IsNullOrWhiteSpace(fill))
                return System.Windows.Media.Brushes.Transparent;

            try
            {
                return (System.Windows.Media.Brush)new BrushConverter().ConvertFrom(fill)!;
            }
            catch
            {
                return System.Windows.Media.Brushes.Transparent;
            }
        }

        /// <summary>
        /// Правка клетки прямо из табличного вида: тот же диалог, что при
        /// добавлении, но заполненный текущими значениями. Оформление, которого
        /// нет в диалоге (кегль, поворот, рамка), сохраняется как было.
        /// </summary>
        private void EditSheetCell(ClosedXmlCellEdit cell)
        {
            if (_closedXmlSheet == null)
                return;

            var blockName = string.IsNullOrWhiteSpace(_closedXmlEditingBlock?.Name)
                ? _closedXmlEditingBlock?.TypeLabel ?? "блок"
                : _closedXmlEditingBlock!.Name;

            var fields = _reportFields
                .Select(field => field.FieldName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dialog = new ClosedXmlCellDialog(_closedXmlSheet.GridColumns, fields, cell.Row, cell.Column, blockName)
            {
                Owner = this
            };
            dialog.LoadForEdit(cell, blockName);

            if (dialog.ShowDialog() != true || dialog.Result == null)
                return;

            var result = dialog.Result;
            cell.Row = result.Row;
            cell.Column = result.Column;
            cell.MergeToColumn = result.MergeToColumn;
            cell.Text = result.Text;
            cell.TakeFromRow = result.TakeFromRow;
            cell.TakeFromField = result.TakeFromField;
            cell.Bold = result.Bold;
            cell.WrapText = result.WrapText;

            RefreshClosedXmlJson();
            ClosedXmlTableCellInfo.Text = $"Клетка изменена: строка {cell.Row}, колонка {cell.Column}.";
        }

        /// <summary>Клетка, выбранная щелчком в табличном виде (повторный щелчок открывает правку).</summary>
        private ClosedXmlSheetTarget? _sheetSelectedTarget;

        /// <summary>
        /// Один щелчок по клетке выбирает её и показывает, что в ней стоит; если у клетки
        /// есть диалог, подсказка говорит, что он откроется.
        /// </summary>
        private void SelectSheetTarget(ClosedXmlSheetTarget? target, string info)
        {
            _sheetSelectedTarget = target;
            ClosedXmlTableCellInfo.Text = target != null && target.HasEditor
                ? $"{info} — двойной щелчок откроет правку"
                : info;
        }

        /// <summary>
        /// Открывает диалог правки по клетке макета. У каждого элемента он свой:
        /// у шапки — диалог шапки, у графы — графы, у клетки — клетки, у итога — итога.
        /// </summary>
        private void OpenSheetTargetEditor(ClosedXmlSheetTarget? target)
        {
            if (_closedXmlSheet == null || target == null)
                return;

            if (target.TextCell != null)
            {
                OpenTextCellEditor(target);
                return;
            }

            if (target.HeaderCell != null)
            {
                var headerDialog = new ClosedXmlHeaderCellDialog(
                    target.HeaderCell,
                    target.HeaderLevelIndex,
                    _closedXmlSheet.GridColumns)
                {
                    Owner = this
                };

                if (headerDialog.ShowDialog() == true)
                    RefreshSheetAfterElementEdit(target.Block, "Ячейка шапки изменена.");
                return;
            }

            if (target.Column != null)
            {
                var columnDialog = new ClosedXmlColumnDialog(
                    target.Block?.Name ?? "таблица",
                    _reportFields
                        .Select(field => field.FieldName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList())
                {
                    Owner = this
                };

                columnDialog.LoadForEdit(target.Column, target.Block?.Name ?? "таблица");
                if (columnDialog.ShowDialog() == true)
                    RefreshSheetAfterElementEdit(target.Block, $"Графа «{target.Column.Field}» изменена.");
                return;
            }

            if (target.Total != null)
            {
                var totalDialog = new ClosedXmlTotalDialog(target.Total, "строка итога") { Owner = this };
                if (totalDialog.ShowDialog() == true)
                    RefreshSheetAfterElementEdit(target.Block, "Итоговая строка изменена.");
                return;
            }

            if (target.Signature != null)
            {
                var signatureDialog = new ClosedXmlSignatureDialog(target.Signature, _closedXmlSheet.GridColumns)
                {
                    Owner = this
                };

                if (signatureDialog.ShowDialog() == true)
                    RefreshSheetAfterElementEdit(target.Block, "Строка подписей изменена.");
            }
        }

        /// <summary>
        /// Клетка текстового блока: правка идёт через списки блока, поэтому сначала
        /// показываем списки того блока, которому клетка принадлежит.
        /// </summary>
        private void OpenTextCellEditor(ClosedXmlSheetTarget target)
        {
            if (target.TextCell == null)
                return;

            if (!ReferenceEquals(_closedXmlEditingBlock, target.Block))
            {
                if (target.Block != null && ClosedXmlBlocksList != null &&
                    ClosedXmlBlocksList.Items.Contains(target.Block))
                {
                    ClosedXmlBlocksList.SelectedItem = target.Block;
                }
                else
                {
                    LoadClosedXmlBlockEditors(target.Block);
                }
            }

            var cell = _closedXmlCells.FirstOrDefault(item =>
                item.Row == target.TextRow && item.Column == target.TextCell.Column);
            if (cell == null)
            {
                ClosedXmlTableCellInfo.Text =
                    "Клетка не найдена в списке блока — выберите блок на вкладке «ClosedXML макет».";
                return;
            }

            EditSheetCell(cell);
        }

        /// <summary>
        /// Правая кнопка по клетке макета: изменить элемент (пункт называет его) или
        /// удалить клетку текстового блока.
        /// </summary>
        private void ShowSheetTargetMenu(Border block, ClosedXmlSheetTarget target)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem
            {
                Header = target.HeaderCell != null ? "Изменить ячейку шапки"
                    : target.Column != null ? "Изменить графу"
                    : target.Total != null ? "Изменить итог"
                    : target.Signature != null ? "Изменить подпись"
                    : "Изменить клетку"
            };
            editItem.Click += (_, _) => OpenSheetTargetEditor(target);
            menu.Items.Add(editItem);

            if (target.TextCell != null)
            {
                var cell = _closedXmlCells.FirstOrDefault(item =>
                    item.Row == target.TextRow && item.Column == target.TextCell.Column);
                if (cell != null)
                {
                    var deleteItem = new MenuItem
                    {
                        Header = "Удалить клетку",
                        Foreground = System.Windows.Media.Brushes.Firebrick
                    };
                    deleteItem.Click += (_, _) =>
                    {
                        _closedXmlCells.Remove(cell);
                        RefreshClosedXmlJson();
                        ClosedXmlTableCellInfo.Text = "Клетка удалена.";
                    };
                    menu.Items.Add(deleteItem);
                }
            }

            block.ContextMenu = menu;
            menu.IsOpen = true;
        }

        /// <summary>
        /// После правки элемента макета: списки блока (они держат те же модели),
        /// JSON и табличный вид листа.
        /// </summary>
        private void RefreshSheetAfterElementEdit(ClosedXmlBlock? block, string message)
        {
            if (block != null)
                LoadClosedXmlBlockEditors(block);

            RefreshClosedXmlJson();
            ClosedXmlTableCellInfo.Text = message;
        }

        /// <summary>Правая кнопка по клетке в табличном виде: изменить или удалить.</summary>
        private void ShowSheetCellMenu(Border block, ClosedXmlCellEdit cell)
        {
            var menu = new ContextMenu();

            var editItem = new MenuItem { Header = "Изменить клетку" };
            editItem.Click += (_, _) => EditSheetCell(cell);
            menu.Items.Add(editItem);

            var deleteItem = new MenuItem
            {
                Header = "Удалить клетку",
                Foreground = System.Windows.Media.Brushes.Firebrick
            };
            deleteItem.Click += (_, _) => DeleteSheetCell(cell);
            menu.Items.Add(deleteItem);

            menu.PlacementTarget = block;
            menu.IsOpen = true;
        }

        /// <summary>Удаляет клетку из табличного вида — с подтверждением.</summary>
        private void DeleteSheetCell(ClosedXmlCellEdit cell)
        {
            var answer = MessageBox.Show(
                $"Удалить клетку (строка {cell.Row}, колонка {cell.Column})?",
                "Удаление клетки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            _closedXmlCells.Remove(cell);
            RefreshClosedXmlJson();
            ClosedXmlTableCellInfo.Text = "Клетка удалена.";
        }

        private void OnNewBlankClosedXmlLayoutClick(object sender, RoutedEventArgs e)
        {
            LoadClosedXmlLayout(ClosedXmlLayoutTemplates.CreateBlank());
            StatusText.Text = "Создан пустой ClosedXML-макет";
        }

        private void OnAddClosedXmlBlockClick(object sender, RoutedEventArgs e)
        {
            if (_closedXmlSheet == null)
                return;

            CommitClosedXmlBlockEditors();
            var type = (sender as Button)?.Tag?.ToString() ?? "TitleRows";
            var block = ClosedXmlLayoutTemplates.CreateBlock(type, _closedXmlBlocks.Count + 1);
            if (string.Equals(type, "TabularSection", StringComparison.Ordinal))
                FillClosedXmlTableColumnsFromReportFields(block);
            _closedXmlBlocks.Add(block);
            ClosedXmlBlocksList.SelectedItem = block;
            LoadClosedXmlBlockEditors(block);
            RefreshClosedXmlJson();
            StatusText.Text = $"Добавлен блок: {block.TypeLabel}";
        }

        private void OnDeleteClosedXmlBlockClick(object sender, RoutedEventArgs e)
        {
            if (ClosedXmlBlocksList.SelectedItem is not ClosedXmlBlock block || _closedXmlSheet == null)
                return;

            var index = _closedXmlBlocks.IndexOf(block);
            _closedXmlEditingBlock = null;
            _closedXmlBlocks.Remove(block);
            _closedXmlSheet.Blocks = _closedXmlBlocks.ToList();

            ClosedXmlBlocksList.SelectedIndex = _closedXmlBlocks.Count == 0
                ? -1
                : Math.Min(index, _closedXmlBlocks.Count - 1);
            LoadClosedXmlBlockEditors(ClosedXmlBlocksList.SelectedItem as ClosedXmlBlock);
            RefreshClosedXmlJson();
        }

        private void OnMoveClosedXmlBlockUpClick(object sender, RoutedEventArgs e)
        {
            MoveClosedXmlBlock(-1);
        }

        private void OnMoveClosedXmlBlockDownClick(object sender, RoutedEventArgs e)
        {
            MoveClosedXmlBlock(1);
        }

        private void MoveClosedXmlBlock(int delta)
        {
            if (ClosedXmlBlocksList.SelectedItem is not ClosedXmlBlock block)
                return;

            var index = _closedXmlBlocks.IndexOf(block);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= _closedXmlBlocks.Count)
                return;

            CommitClosedXmlBlockEditors();
            _closedXmlBlocks.Move(index, target);
            ClosedXmlBlocksList.SelectedItem = block;
            RefreshClosedXmlJson();
        }

        private void OnClosedXmlBlockSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_closedXmlSyncing)
                return;

            // Записываем правки в тот блок, который был выбран до смены выделения.
            var previous = e.RemovedItems.Count > 0
                ? e.RemovedItems[0] as ClosedXmlBlock
                : _closedXmlEditingBlock;

            CommitClosedXmlBlockEditors(previous);
            LoadClosedXmlBlockEditors(ClosedXmlBlocksList.SelectedItem as ClosedXmlBlock);
            RefreshClosedXmlJson();
        }

        private void OnClosedXmlBlockNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_closedXmlSyncing || _closedXmlEditingBlock == null)
                return;

            _closedXmlEditingBlock.Name = ClosedXmlBlockNameBox.Text ?? string.Empty;
            RefreshClosedXmlJson();
        }

        /// <summary>
        /// Предпросмотр макета ClosedXML в PDF.
        ///
        /// Вывод идёт по той же модели макета, что и Excel, поэтому документ
        /// совпадает по составу и оформлению.
        /// </summary>
        private async void OnClosedXmlPreviewPdfClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var layout = BuildClosedXmlLayout();
                if (layout == null)
                {
                    MessageBox.Show("Макет не задан.", "ClosedXML PDF",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                RefreshClosedXmlJson();

                var report = BuildReportFromForm();
                if (report == null)
                    return;

                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Формирование PDF по макету ClosedXML...";

                var parameters = await _reportService.ResolveParametersForPreviewAsync(report);
                var data = await BuildClosedXmlPreviewDataAsync(report, parameters);
                var previewData = ClosedXmlReportRenderer.PrepareData(data, report);

                var typedParameters = parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
                var bytes = ClosedXmlReportRenderer.RenderToPdfBytes(layout, previewData, typedParameters);

                var previewWindow = new PdfPreviewWindow(bytes, report.Name) { Owner = this };
                previewWindow.ShowDialog();

                StatusText.Text = "✅ Предпросмотр PDF открыт";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предпросмотра PDF: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnClosedXmlPreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var layout = BuildClosedXmlLayout();
                if (layout == null)
                {
                    MessageBox.Show("Макет не задан.", "ClosedXML",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                RefreshClosedXmlJson();

                var report = BuildReportFromForm();
                if (report == null)
                    return;

                if (report.Fields == null || report.Fields.Count == 0)
                {
                    MessageBox.Show("Добавьте поля отчёта — без них в макет нечего выводить.", "ClosedXML",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Формирование ClosedXML-предпросмотра...";

                var parameters = await _reportService.ResolveParametersForPreviewAsync(report);

                // Для акта сверки набор данных строится тем же сервисом, что и в рабочем
                // режиме. Общий генератор по метаданным отдаёт сырые поля проводок,
                // и макет акта не находит в них свои колонки: он рассчитан на таблицу
                // акта — с шапкой, парами счетов, сальдо и итогами.
                var data = await BuildClosedXmlPreviewDataAsync(report, parameters);
                var previewData = ClosedXmlReportRenderer.PrepareData(data, report);

                var bytes = ClosedXmlReportRenderer.RenderToBytes(
                    layout,
                    previewData,
                    parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
                var path = Path.Combine(Path.GetTempPath(), $"closedxml_preview_{Guid.NewGuid():N}.xlsx");
                File.WriteAllBytes(path, bytes);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });

                StatusText.Text = $"✅ Предпросмотр ClosedXML: {path}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка предпросмотра ClosedXML: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private static void SelectComboByContent(ComboBox combo, string? value)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = -1;
        }

        private static List<string> SplitClosedXmlList(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? new List<string>()
                : text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0)
                    .ToList();

        private static int ParseIntSafe(string? text) =>
            int.TryParse(text, out var value) ? value : 0;

        private static double ParseDoubleSafe(string? text, double fallback) =>
            double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    /// <summary>Строка редактора ячеек блока (Row + ClosedXmlTextCell).</summary>
    public class ClosedXmlCellEdit
    {
        public int Row { get; set; }

        public int Column { get; set; } = 1;

        public int MergeToColumn { get; set; }

        public string Text { get; set; } = string.Empty;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public double FontSize { get; set; }

        public string Align { get; set; } = "Left";

        public string Fill { get; set; } = string.Empty;

        public string Border { get; set; } = "None";

        /// <summary>Взять значение из строки набора (1-based; отрицательное — с конца).</summary>
        public int TakeFromRow { get; set; }

        /// <summary>Имя колонки набора для привязки.</summary>
        public string TakeFromField { get; set; } = string.Empty;

        /// <summary>Поворот текста в градусах: 0, 45, 90, 270.</summary>
        public int TextRotation { get; set; }

        /// <summary>Переносить длинный текст по словам.</summary>
        public bool WrapText { get; set; }

        public static ClosedXmlCellEdit FromModel(int row, ClosedXmlTextCell cell) => new()
        {
            Row = row,
            Column = cell.Column,
            MergeToColumn = cell.MergeToColumn,
            Text = cell.Text,
            Bold = cell.Bold,
            Italic = cell.Italic,
            FontSize = cell.FontSize,
            Align = cell.Align,
            Fill = cell.Fill,
            Border = cell.Border,
            TakeFromRow = cell.TakeFromRow,
            TakeFromField = cell.TakeFromField,
            TextRotation = cell.TextRotation,
            WrapText = cell.WrapText
        };

        public ClosedXmlTextCell ToModel() => new()
        {
            Column = Column <= 0 ? 1 : Column,
            MergeToColumn = MergeToColumn,
            Text = Text ?? string.Empty,
            Bold = Bold,
            Italic = Italic,
            FontSize = FontSize,
            Align = string.IsNullOrWhiteSpace(Align) ? "Left" : Align,
            Fill = Fill ?? string.Empty,
            Border = string.IsNullOrWhiteSpace(Border) ? "None" : Border,
            TakeFromRow = TakeFromRow,
            TakeFromField = TakeFromField ?? string.Empty,
            TextRotation = TextRotation,
            WrapText = WrapText
        };
    }

    /// <summary>
    /// Диалог «Добавление колонки таблицы»: у таблицы добавляется графа,
    /// а не ячейка заголовка, поэтому и поля другие.
    /// </summary>
    public class ClosedXmlColumnDialog : Window
    {
        private readonly ComboBox _fieldCombo = new() { Width = 380 };
        private readonly TextBox _headerBox = new() { Width = 380, Text = "Заголовок" };
        private readonly TextBox _widthBox = new() { Width = 70, Text = "14" };
        private readonly TextBox _formatBox = new() { Width = 140, Text = "#,##0.00" };
        private readonly TextBox _spanBox = new() { Width = 70, Text = "0" };
        private readonly ComboBox _alignCombo = ClosedXmlDialogParts.CreateCombo("Left", "Center", "Right");
        private readonly ComboBox _rotationCombo = ClosedXmlDialogParts.CreateCombo("0", "45", "90", "270");
        private readonly CheckBox _boldCheck = new() { Content = "Жирный", VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _suppressZeroCheck = new()
        {
            Content = "Не выводить нули", VerticalAlignment = VerticalAlignment.Center
        };
        private readonly TextBlock _titleBlock = new() { FontWeight = FontWeights.Bold, FontSize = 14 };
        private readonly Button _acceptButton = new() { Width = 120, Height = 30, IsDefault = true };
        private ClosedXmlColumn? _editTarget;

        public ClosedXmlColumn? Result { get; private set; }

        public ClosedXmlColumnDialog(string blockName, List<string> fields)
        {
            var target = string.IsNullOrWhiteSpace(blockName) ? "таблица" : blockName;
            Title = $"Добавление колонки — {target}";
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            _titleBlock.Text = $"Добавление колонки таблицы — {target}";
            _titleBlock.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(_titleBlock);
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10),
                Text = "Колонка — графа бланка. «Поле набора» — откуда берётся значение, «Заголовок» печатается в шапке таблицы. Выравнивание, жирный, поворот и подавление нулей правятся здесь же."
            });

            foreach (var field in fields)
                _fieldCombo.Items.Add(field);
            if (_fieldCombo.Items.Count > 0)
                _fieldCombo.SelectedIndex = 0;
            ClosedXmlDialogParts.SelectCombo(_alignCombo, "Right");

            AddRow(panel, "Поле набора (значение):", _fieldCombo);
            AddRow(panel, "Заголовок графы:", _headerBox);
            AddRow(panel, "Ширина:", _widthBox);
            AddRow(panel, "Формат числа:", _formatBox);
            AddRow(panel, "Колонок листа (0 — одна):", _spanBox);
            AddRow(panel, "Выравнивание:", _alignCombo);
            AddRow(panel, "Поворот подписи:", _rotationCombo);
            AddRow(panel, string.Empty, _boldCheck);
            AddRow(panel, string.Empty, _suppressZeroCheck);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            _acceptButton.Content = "Добавить";
            _acceptButton.Click += (_, _) => Accept();
            var cancel = new Button
            {
                Content = "Отмена", Width = 100, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true
            };
            buttons.Children.Add(_acceptButton);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
        }

        /// <summary>
        /// Правка существующей графы: те же поля, что при добавлении, заполненные
        /// значениями. Результат пишется прямо в графу — вторая модель не нужна.
        /// </summary>
        public void LoadForEdit(ClosedXmlColumn column, string blockName)
        {
            if (column == null)
                throw new ArgumentNullException(nameof(column));

            _editTarget = column;
            var target = string.IsNullOrWhiteSpace(blockName) ? "таблица" : blockName;
            Title = $"Правка графы — {target}";
            _titleBlock.Text = $"Графа таблицы — {target}";
            _acceptButton.Content = "Сохранить";

            ClosedXmlDialogParts.SelectCombo(_fieldCombo, column.Field);
            if (_fieldCombo.SelectedItem == null && !string.IsNullOrWhiteSpace(column.Field))
                _fieldCombo.Items.Add(column.Field);
            if (_fieldCombo.SelectedItem == null && _fieldCombo.Items.Count > 0)
                _fieldCombo.SelectedIndex = 0;

            _headerBox.Text = column.Header ?? string.Empty;
            _widthBox.Text = (column.Width > 0 ? column.Width : 14).ToString("0.#");
            _formatBox.Text = column.NumberFormat ?? string.Empty;
            _spanBox.Text = Math.Max(0, column.Span).ToString();
            ClosedXmlDialogParts.SelectCombo(_alignCombo, column.Align);
            ClosedXmlDialogParts.SelectCombo(_rotationCombo, column.TextRotation.ToString());
            _boldCheck.IsChecked = column.Bold;
            _suppressZeroCheck.IsChecked = column.SuppressZero;
        }

        private static void AddRow(Panel panel, string label, FrameworkElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            row.Children.Add(control);
            panel.Children.Add(row);
        }

        private static int ParseInt(string? text, int fallback) =>
            int.TryParse(text, out var value) ? value : fallback;

        private void Accept()
        {
            if (_fieldCombo.SelectedItem is not string field || string.IsNullOrWhiteSpace(field))
            {
                MessageBox.Show(
                    "Выберите поле набора — по нему колонка берёт значение.",
                    "Колонка таблицы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var header = string.IsNullOrWhiteSpace(_headerBox.Text) ? field : _headerBox.Text.Trim();
            var width = Math.Max(4, ParseInt(_widthBox.Text, 14));
            var format = _formatBox.Text?.Trim() ?? string.Empty;
            var span = Math.Max(0, ParseInt(_spanBox.Text, 0));
            var align = _alignCombo.SelectedItem?.ToString() ?? "Right";
            var rotation = ParseInt(_rotationCombo.SelectedItem?.ToString(), 0);
            var bold = _boldCheck.IsChecked == true;
            var suppressZero = _suppressZeroCheck.IsChecked == true;

            if (_editTarget != null)
            {
                _editTarget.Field = field;
                _editTarget.Header = header;
                _editTarget.Width = width;
                _editTarget.NumberFormat = format;
                _editTarget.Span = span;
                _editTarget.Align = align;
                _editTarget.TextRotation = rotation;
                _editTarget.Bold = bold;
                _editTarget.SuppressZero = suppressZero;
                DialogResult = true;
                return;
            }

            Result = new ClosedXmlColumn
            {
                Field = field,
                Header = header,
                Width = width,
                NumberFormat = format,
                Span = span,
                Align = align,
                TextRotation = rotation,
                Bold = bold,
                SuppressZero = suppressZero
            };

            DialogResult = true;
        }
    }

    /// <summary>
    /// Диалог «Новая ячейка»: те же поля, что в таблице, но словами —
    /// понятно тому, кто вкладку ClosedXML открыл впервые.
    /// </summary>
    public class ClosedXmlCellDialog : Window
    {
        private readonly TextBox _rowBox = new() { Width = 70, Text = "1" };
        private readonly TextBox _columnBox = new() { Width = 70 };
        private readonly TextBox _mergeBox = new() { Width = 70 };
        private readonly TextBox _textBox = new() { Width = 380 };
        private readonly ComboBox _fieldCombo = new() { Width = 380 };
        private readonly TextBox _takeFromRowBox = new() { Width = 70, Text = "0" };
        private readonly CheckBox _boldCheck = new() { Content = "Жирный" };
        private readonly CheckBox _wrapCheck = new() { Content = "Переносить длинный текст" };

        public ClosedXmlCellEdit? Result { get; private set; }

        public ClosedXmlCellDialog(int gridColumns, List<string> fields, int nextRow, int nextColumn,
            string blockName)
        {
            var target = string.IsNullOrWhiteSpace(blockName) ? "блок" : blockName;
            Title = $"Добавление ячейки — {target}";
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _rowBox.Text = Math.Max(1, nextRow).ToString();
            _columnBox.Text = Math.Max(1, nextColumn).ToString();

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = $"Добавление ячейки — {target}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Text = (gridColumns > 0 ? $"Сетка листа: {gridColumns} колонок. " : string.Empty) +
                       "Колонка — вертикальная полоса листа слева направо (1 — левый край). " +
                       "Строка — горизонтальная полоса сверху вниз (1 — самая верхняя, где заголовок бланка)."
            });

            AddRow(panel, "Строка (сверху вниз):", _rowBox);
            AddRow(panel, "Колонка (слева направо):", _columnBox);
            AddRow(panel, "Объединить по колонку:", _mergeBox);
            AddHint(panel, "Пусто или то же число — ячейка в одной колонке. 3 и 5 — ячейка займёт колонки 3–5.");
            AddRow(panel, "Текст:", _textBox);
            AddHint(panel, "Можно вписать {Параметр} — он появится на вкладке «Параметры».");

            _fieldCombo.Items.Add("(не подставлять)");
            foreach (var field in fields)
                _fieldCombo.Items.Add(field);
            _fieldCombo.SelectedIndex = 0;
            AddRow(panel, "Поле набора:", _fieldCombo);
            AddRow(panel, "Из строки набора:", _takeFromRowBox);
            AddHint(panel, "1 — первая строка данных, -1 — последняя, 0 — брать текст из поля «Текст».");

            var checks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _wrapCheck.Margin = new Thickness(18, 0, 0, 0);
            checks.Children.Add(_boldCheck);
            checks.Children.Add(_wrapCheck);
            panel.Children.Add(checks);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "Добавить", Width = 120, Height = 30, IsDefault = true };
            ok.Click += (_, _) => Accept();
            var cancel = new Button
            {
                Content = "Отмена", Width = 100, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
        }

        private static void AddRow(Panel panel, string label, FrameworkElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            row.Children.Add(control);
            panel.Children.Add(row);
        }

        private static void AddHint(Panel panel, string text) =>
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(180, 2, 0, 0)
            });

        private static int ParseInt(string? text, int fallback) =>
            int.TryParse(text, out var value) ? value : fallback;

        /// <summary>
        /// Заполняет диалог текущими значениями клетки — для правки
        /// прямо из табличного вида.
        /// </summary>
        public void LoadForEdit(ClosedXmlCellEdit cell, string blockName)
        {
            var target = string.IsNullOrWhiteSpace(blockName) ? "блок" : blockName;
            Title = $"Изменение клетки — {target}";

            _rowBox.Text = Math.Max(1, cell.Row).ToString();
            _columnBox.Text = Math.Max(1, cell.Column).ToString();
            _mergeBox.Text = cell.MergeToColumn > cell.Column ? cell.MergeToColumn.ToString() : string.Empty;
            _textBox.Text = cell.Text ?? string.Empty;
            _takeFromRowBox.Text = cell.TakeFromRow.ToString();
            _boldCheck.IsChecked = cell.Bold;
            _wrapCheck.IsChecked = cell.WrapText;

            var fieldIndex = string.IsNullOrWhiteSpace(cell.TakeFromField)
                ? 0
                : _fieldCombo.Items.IndexOf(cell.TakeFromField);
            _fieldCombo.SelectedIndex = fieldIndex >= 0 ? fieldIndex : 0;
        }

        private void Accept()
        {
            Result = new ClosedXmlCellEdit
            {
                Row = ParseInt(_rowBox.Text, 1),
                Column = ParseInt(_columnBox.Text, 1),
                MergeToColumn = ParseInt(_mergeBox.Text, 0),
                Text = _textBox.Text ?? string.Empty,
                TakeFromRow = ParseInt(_takeFromRowBox.Text, 0),
                TakeFromField = _fieldCombo.SelectedIndex <= 0
                    ? string.Empty
                    : _fieldCombo.SelectedItem?.ToString() ?? string.Empty,
                Bold = _boldCheck.IsChecked == true,
                WrapText = _wrapCheck.IsChecked == true,
                Align = "Left",
                Border = "None"
            };

            DialogResult = true;
        }
    }

    /// <summary>
    /// Ячейка уровня шапки для таблицы конструктора.
    /// Плоский вид: уровень задаётся номером, иначе вложенный список
    /// было бы неудобно править.
    /// </summary>
    public class ClosedXmlHeaderCellEdit
    {
        /// <summary>Номер уровня шапки, начиная с 0 (верхний).</summary>
        public int LevelIndex { get; set; }

        /// <summary>Имя уровня: подписывает уровень в списке.</summary>
        public string LevelName { get; set; } = string.Empty;

        public int Column { get; set; } = 1;

        public int MergeToColumn { get; set; }

        /// <summary>
        /// Сколько строк шапки подпись занимает по вертикали: 1 — только свой
        /// уровень; 2 — подпись стоит сразу над двумя уровнями (граф бланка,
        /// под которым идёт второй уровень).
        /// </summary>
        public int RowSpan { get; set; } = 1;

        /// <summary>Подпись. Пусто в нижнем уровне — подставится заголовок колонки.</summary>
        public string Text { get; set; } = string.Empty;

        public bool Bold { get; set; } = true;

        public string Align { get; set; } = "Center";

        public int TextRotation { get; set; }

        public string Fill { get; set; } = string.Empty;

        public static ClosedXmlHeaderCellEdit FromModel(int levelIndex, ClosedXmlHeaderCell cell) => new()
        {
            LevelIndex = levelIndex,
            Column = cell.Column,
            MergeToColumn = cell.MergeToColumn,
            RowSpan = cell.RowSpan <= 0 ? 1 : cell.RowSpan,
            Text = cell.Text,
            Bold = cell.Bold,
            Align = cell.Align,
            TextRotation = cell.TextRotation,
            Fill = cell.Fill
        };

        public ClosedXmlHeaderCell ToModel() => new()
        {
            Column = Column <= 0 ? 1 : Column,
            MergeToColumn = MergeToColumn,
            RowSpan = RowSpan <= 0 ? 1 : RowSpan,
            Text = Text ?? string.Empty,
            Bold = Bold,
            Align = string.IsNullOrWhiteSpace(Align) ? "Center" : Align,
            TextRotation = TextRotation,
            Fill = Fill ?? string.Empty
        };
    }

    public class FieldDef
    {
        public string Name { get; set; } = string.Empty;
        public string DbColumnName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
