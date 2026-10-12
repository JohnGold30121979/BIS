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
        private FoxProReportFieldRuleService _foxProRuleService;
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
        public ObservableCollection<FoxProReportFieldRule> FoxProRules { get; } = new();
        private ObservableCollection<FrXElementMappingViewModel> _frxElementMappings = new();

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
            ElementMappingGrid.ItemsSource = _frxElementMappings;
            ConfigureFieldColumns();
            ConfigureParameterColumns();
            InitializeNativeDesignerState();
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
                _foxProRuleService = new FoxProReportFieldRuleService(context);
                await _foxProRuleService.SeedDefaultRulesAsync();
                await LoadFoxProRulesAsync();

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
                RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
            }
            else
            {
                _reportFields.Clear();
                AvailableSourceFields.Clear();
                AvailableComputedFields.Clear();
                AvailableDataFields.Clear();
                AvailableFilterFields.Clear();
                AvailableLayoutFields.Clear();
                RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
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
                "Для FRX выберите «Макет Visual FoxPro FRX» и укажите источник данных в поле «Источник».\n" +
                "Поля во вкладках «Поля» и «FRX» берутся из одного выбранного источника.\n\n" +
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
            TryCommitGrid(ElementMappingGrid);
            TryCommitGrid(FoxProRulesGrid);
            TryCommitGrid(NativeElementsGrid);
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
                    RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
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
                    RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
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

            // Сначала способ макета: от него зависит, какие вкладки и поля видны.
            SelectLayoutKindFromReport(report);

            ReportNameBox.Text = report.Name;
            ReportDescBox.Text = report.Description;

            TitleTextBox.Text = report.TitleText ?? "";
            SubtitleTextBox.Text = report.SubtitleText ?? "";
            HeaderTextBox.Text = report.HeaderText ?? "";
            FooterTextBox.Text = report.FooterText ?? "";
            SummaryTextBox.Text = report.SummaryText ?? "";
            TemplateTextBox.Text = report.Template ?? "";

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
                    RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
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
                RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
            }
            // Загрузка фильтров
            _reportFilters.Clear();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                _reportFilters.Add(filter);
            }

            LoadReportParameters(report);

            if (!string.IsNullOrWhiteSpace(report.Template))
            {
                await LoadFrxElementMappings(report.Template, report.ElementMappings);
                RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
                LoadNativeTemplateFromReport(report);
            }
            else
            {
                LoadNativeTemplateFromReport(report);
            }

            LoadClosedXmlLayoutFromReport(report);
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

                SyncNativeTemplateToTemplateBoxIfNeeded();

                SyncFrxElementMappingsFromNativeTemplate();
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
                _currentReport.IsPrintForm = IsPrintFormCheck.IsChecked ?? false;
                _currentReport.IsDefault = IsDefaultCheck.IsChecked ?? false;
                _currentReport.SourceFormat = GetSelectedLayoutKind() == FoxProLayoutKind
                    ? (_currentReport.ReportType == "FoxProLayout" ? "FoxProFRX" : "Native")
                    : ClosedXmlSourceFormat;
                _currentReport.Template = TemplateTextBox?.Text ?? "";
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

                var mappingsToAdd = BuildReportElementMappings(_currentReport.Id);

                _currentReport.Fields = fieldsToAdd;
                _currentReport.Filters = filtersToAdd;
                _currentReport.Parameters = _reportParameters
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
                    .OrderBy(parameter => parameter.Order)
                    .ToList();
                _currentReport.ElementMappings = mappingsToAdd;

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

            SyncNativeTemplateToTemplateBoxIfNeeded();

            SyncFrxElementMappingsFromNativeTemplate();
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
                Template = TemplateTextBox.Text,
                ClosedXmlLayout = BuildClosedXmlLayoutJson()
            };

            report.ElementMappings = BuildReportElementMappings(report.Id);
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

        private ComboBoxItem? _foxProReportTypeItem;

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

            // При отключённом FRX способ макета всегда ClosedXML.
            if (!ReportingFeatures.FrxEnabled &&
                string.Equals(kind, FoxProLayoutKind, StringComparison.OrdinalIgnoreCase))
            {
                kind = ClosedXmlLayoutKind;
            }

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
            // При отключённом FRX способ макета всегда ClosedXML: вкладки FRX/native,
            // блок FRX-шаблона и тип отчёта «Макет Visual FoxPro FRX» скрываются.
            var isFrx = ReportingFeatures.FrxEnabled &&
                        string.Equals(GetSelectedLayoutKind(), FoxProLayoutKind, StringComparison.OrdinalIgnoreCase);

            // Пункт выбора способа FRX скрывается, если механизм отключён.
            HideFrxLayoutKindItem();

            // Вкладки: FRX-ветка и ветка ClosedXML никогда не показываются вместе.
            if (FrXFieldsTab != null)
                FrXFieldsTab.Visibility = isFrx ? Visibility.Visible : Visibility.Collapsed;
            if (NativeDesignerTab != null)
                NativeDesignerTab.Visibility = isFrx ? Visibility.Visible : Visibility.Collapsed;
            if (ClosedXmlDesignerTab != null)
                ClosedXmlDesignerTab.Visibility = isFrx ? Visibility.Collapsed : Visibility.Visible;

            // Блок FRX-шаблона на вкладке «Общие».
            if (FrxTemplateLabel != null)
                FrxTemplateLabel.Visibility = isFrx ? Visibility.Visible : Visibility.Collapsed;
            if (FrxTemplatePanel != null)
                FrxTemplatePanel.Visibility = isFrx ? Visibility.Visible : Visibility.Collapsed;

            ApplyInactiveTabsVisibility(isFrx);

            SyncFoxProReportTypeItem(isFrx);

            if (LayoutKindHint != null)
            {
                LayoutKindHint.Text = isFrx
                    ? "FRX/native: макет задаётся координатами — работают вкладки «🔗 FRX поля» и «🧱 Нативный макет», вкладка ClosedXML скрыта."
                    : "ClosedXML: макет собирается визуально на вкладке «📊 ClosedXML макет». Вкладки FRX/native, «Оформление» и «Фильтры» скрыты — оформление и отбор задаются в самом макете.";
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
        /// Уровни шапки и высота её строки нужны только форматному макету:
        /// в простом шапка состоит из одной строки подписей колонок.
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
                ClosedXmlHeaderLevelsExpander.Visibility = isFormatted ? Visibility.Visible : Visibility.Collapsed;

            // Панель выбора вида нужна только для ClosedXML: у FRX своя механика.
            if (LayoutShapePanel != null)
                LayoutShapePanel.Visibility = ReportingFeatures.FrxEnabled
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        /// <summary>
        /// Скрывает пункт «FRX — макет FoxPro» в списке способа макета,
        /// когда механизм FRX отключён.
        /// </summary>
        private void HideFrxLayoutKindItem()
        {
            if (LayoutKindCombo == null)
                return;

            foreach (var item in LayoutKindCombo.Items.OfType<ComboBoxItem>())
            {
                if (!string.Equals(item.Tag?.ToString(), FoxProLayoutKind, StringComparison.OrdinalIgnoreCase))
                    continue;

                item.Visibility = ReportingFeatures.FrxEnabled ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
        }

        /// <summary>Тип отчёта «Макет Visual FoxPro FRX» доступен только в способе FRX.</summary>
        private void SyncFoxProReportTypeItem(bool show)
        {
            if (ReportTypeCombo == null)
                return;

            _foxProReportTypeItem ??= ReportTypeCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "FoxProLayout", StringComparison.OrdinalIgnoreCase));

            if (_foxProReportTypeItem == null)
                return;

            var isPresent = ReportTypeCombo.Items.Contains(_foxProReportTypeItem);
            if (show && !isPresent)
            {
                ReportTypeCombo.Items.Add(_foxProReportTypeItem);
            }
            else if (!show && isPresent)
            {
                if (ReferenceEquals(ReportTypeCombo.SelectedItem, _foxProReportTypeItem))
                    ReportTypeCombo.SelectedIndex = 0;

                ReportTypeCombo.Items.Remove(_foxProReportTypeItem);
            }
        }

        private void SelectLayoutKindFromReport(Report report)
        {
            // FRX/native-режим — только если у отчёта действительно координатный макет:
            // помечен как FRX, загружен FRX-шаблон, это печатная форма или задан макет
            // конструктора. Стандартные отчёты без макета открываются в ClosedXML.
            var looksLikeFrx = string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase)
                               || report.IsPrintForm
                               || !string.IsNullOrWhiteSpace(report.Template);

            var isFrx = looksLikeFrx && string.IsNullOrWhiteSpace(report.ClosedXmlLayout);
            SetLayoutKind(isFrx ? FoxProLayoutKind : ClosedXmlLayoutKind);
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
       
        // Загрузка FRX-файла в поле шаблона
        private async void OnLoadFrxClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Title = "Выберите файл макета FoxPro (.frx)",
                    Filter = "FoxPro макеты (*.frx)|*.frx|Все файлы (*.*)|*.*",
                    DefaultExt = ".frx",
                    CheckFileExists = true
                };

                if (openDialog.ShowDialog() != true)
                    return;

                StatusText.Text = $"Загрузка: {Path.GetFileName(openDialog.FileName)}...";

                // Читаем файл
                var fileBytes = File.ReadAllBytes(openDialog.FileName);

                // Парсим FRX
                var templateJson = await PrintFormService.ParseFrxFileTemplateAsync(fileBytes, openDialog.FileName);

                if (!string.IsNullOrEmpty(templateJson))
                {
                    CommitDesignerGridEdits();

                    // ЗАПОЛНЯЕМ ПОЛЕ
                    TemplateTextBox.Text = templateJson;

                    // Автоматически выбираем тип "Макет Visual FoxPro FRX"
                    foreach (ComboBoxItem item in ReportTypeCombo.Items)
                    {
                        if (item.Tag?.ToString() == "FoxProLayout")
                        {
                            ReportTypeCombo.SelectedItem = item;
                            break;
                        }
                    }

                    // Устанавливаем флаг печатной формы
                    IsPrintFormCheck.IsChecked = true;

                    // ЗАПОЛНЯЕМ ТАБЛИЦУ СООТВЕТСТВИЙ ЭЛЕМЕНТОВ МАКЕТА
                    await LoadFrxElementMappings(templateJson);
                    DeferNativeTemplate(templateJson, showWarning: true);

                    if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                    {
                        await LoadAvailableFields(catalog);
                        RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
                    }
                    else
                    {
                        RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
                    }

                    StatusText.Text = $"✅ Загружено: {Path.GetFileName(openDialog.FileName)}, полей источника: {AvailableDataFields.Count}";

                    // Макет уже загружен в конструктор: модальное подтверждение только отвлекает.
                }
                else
                {
                    StatusText.Text = "❌ Ошибка парсинга FRX";
                    MessageBox.Show("Не удалось распарсить FRX-файл.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadFrxElementMappings(
            string templateJson,
            IEnumerable<ReportElementMapping>? savedMappings = null)
        {
            try
            {
                var template = PrintFormService.DeserializePrintTemplate(templateJson);
                if (template == null || template.Elements == null)
                {
                    MappingPreviewText.Text = "Нет элементов для отображения.";
                    return;
                }

                try
                {
                    ElementMappingGrid.ItemsSource = null;
                    _frxElementMappings.Clear();
                    var savedByElementOrder = (savedMappings ?? Array.Empty<ReportElementMapping>())
                        .GroupBy(item => item.ElementOrder)
                        .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());
                    int order = 0;
                    foreach (var element in template.Elements.OrderBy(e => e.Order))
                    {
                        // Пропускаем линии, рамки и пустые тексты
                        if (element.Type is "Line" or "Box" or "Picture")
                            continue;

                        var elementText = string.IsNullOrWhiteSpace(element.Expression)
                            ? PrintFormService.CleanFoxText(element.Text)
                            : element.Expression;

                        if (string.IsNullOrWhiteSpace(elementText) || elementText == "+" || elementText == "-")
                            continue;

                        savedByElementOrder.TryGetValue(element.Order, out var saved);
                        _frxElementMappings.Add(new FrXElementMappingViewModel
                        {
                            Id = saved?.Id ?? Guid.NewGuid(),
                            ElementOrder = element.Order,
                            ElementType = element.Type,
                            ElementText = elementText,
                            ElementExpression = element.Expression,
                            BandType = element.BandType,
                            Left = element.Left,
                            Top = element.Top,
                            Width = element.Width,
                            Height = element.Height,
                            FontName = element.FontName,
                            FontSize = element.FontSize,
                            Bold = element.Bold,
                            Italic = element.Italic,
                            Alignment = element.Alignment,
                            Order = order++,
                            MappedFieldName = saved?.MappedFieldName ?? string.Empty,
                            MappedDisplayName = saved?.MappedDisplayName ?? string.Empty,
                            DataSource = saved?.DataSource ?? string.Empty,
                            FormatString = saved?.FormatString ?? string.Empty,
                            IsVisible = saved?.IsVisible ?? true,
                            CustomText = saved?.CustomText ?? string.Empty
                        });
                    }
                }
                finally
                {
                    ElementMappingGrid.ItemsSource = _frxElementMappings;
                }

                var autoApplied = ApplyFoxProRulesToEmptyMappings();
                UpdateMappingPreview();

                if (autoApplied > 0)
                    StatusText.Text = $"FRX поля: автоматически применены правила: {autoApplied}.";

                if (_frxElementMappings.Count > 0)
                    FrXFieldsTab.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MappingPreviewText.Text = $"Ошибка загрузки элементов: {ex.Message}";
            }
        }

        private async Task LoadFoxProRulesAsync()
        {
            if (_foxProRuleService == null)
                return;

            FoxProRules.Clear();
            foreach (var rule in await _foxProRuleService.GetRulesAsync())
                FoxProRules.Add(rule);
        }

        private void OnApplyFoxProRulesClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            var applied = ApplyFoxProRulesToEmptyMappings();
            UpdateMappingPreview();
            StatusText.Text = applied == 0
                ? "Общие правила не нашли новых соответствий."
                : $"Общие правила применены: {applied}.";
        }

        private int ApplyFoxProRulesToEmptyMappings()
        {
            var applied = 0;
            foreach (var mapping in _frxElementMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.MappedFieldName))
                    continue;

                var source = GetMappingSource(mapping);
                var target = FindRuleTargetField(source);
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                mapping.MappedFieldName = target;
                mapping.MappedDisplayName = ResolveFieldDisplayName(target);
                applied++;
            }

            return applied;
        }

        private async void OnSaveMappedFoxProRulesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitDesignerGridEdits();
                var profileCode = GetCurrentRecognitionProfileCode();
                var added = 0;
                foreach (var mapping in _frxElementMappings.Where(item => !string.IsNullOrWhiteSpace(item.MappedFieldName)))
                {
                    var source = FoxProReportKnowledgeBase.NormalizeRuleSource(GetMappingSource(mapping));
                    if (string.IsNullOrWhiteSpace(source))
                        continue;

                    var existing = FoxProRules.FirstOrDefault(rule =>
                        !rule.IsRegex &&
                        FoxProReportKnowledgeBase.RuleMatches(rule, source));
                    if (existing == null)
                    {
                        existing = new FoxProReportFieldRule
                        {
                            Id = Guid.NewGuid(),
                            SourcePattern = source,
                            Priority = 100,
                            IsActive = true
                        };
                        FoxProRules.Add(existing);
                        added++;
                    }

                    existing.ProfileCode = profileCode;
                    existing.SourcePattern = source;
                    existing.CanonicalField = FoxProReportKnowledgeBase.GetCanonicalFieldForSource(source);
                    existing.TargetFieldName = mapping.MappedFieldName;
                    existing.TargetDisplayName = ResolveFieldDisplayName(mapping.MappedFieldName);
                    existing.Description = string.IsNullOrWhiteSpace(existing.Description)
                        ? "Создано из визуального конструктора отчетов."
                        : existing.Description;
                    existing.IsActive = true;
                }

                await SaveFoxProRulesAsync();
                StatusText.Text = added == 0
                    ? "Общие правила обновлены."
                    : $"Общие правила сохранены. Новых: {added}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения правил FoxPro: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAddFoxProRuleClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            var mapping = ElementMappingGrid.SelectedItem as FrXElementMappingViewModel;
            var source = mapping == null ? string.Empty : FoxProReportKnowledgeBase.NormalizeRuleSource(GetMappingSource(mapping));
            var target = mapping?.MappedFieldName ?? string.Empty;
            FoxProRules.Add(new FoxProReportFieldRule
            {
                Id = Guid.NewGuid(),
                ProfileCode = GetCurrentRecognitionProfileCode(),
                SourcePattern = source,
                CanonicalField = FoxProReportKnowledgeBase.GetCanonicalFieldForSource(source),
                TargetFieldName = target,
                TargetDisplayName = ResolveFieldDisplayName(target),
                Priority = 100,
                IsActive = true,
                Description = "Пользовательское правило распознавания."
            });
        }

        private void OnDeleteFoxProRuleClick(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            if (FoxProRulesGrid.SelectedItem is FoxProReportFieldRule rule)
                FoxProRules.Remove(rule);
        }

        private async void OnSaveFoxProRulesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                CommitDesignerGridEdits();
                await SaveFoxProRulesAsync();
                StatusText.Text = "Список общих правил FoxPro сохранен.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения списка правил FoxPro: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveFoxProRulesAsync()
        {
            if (_foxProRuleService == null)
                return;

            foreach (var rule in FoxProRules)
            {
                rule.SourcePattern = FoxProReportKnowledgeBase.NormalizeRuleSource(rule.SourcePattern);
                if (string.IsNullOrWhiteSpace(rule.CanonicalField))
                    rule.CanonicalField = FoxProReportKnowledgeBase.GetCanonicalFieldForSource(rule.SourcePattern);
                rule.TargetDisplayName = ResolveFieldDisplayName(rule.TargetFieldName);
                rule.UpdatedAt = DateTime.UtcNow;
            }

            await _foxProRuleService.SaveRulesAsync(FoxProRules);
            await LoadFoxProRulesAsync();
        }

        private string FindRuleTargetField(string source)
        {
            var savedRule = FoxProRules
                .Where(rule => rule.IsActive)
                .OrderBy(rule => rule.Priority)
                .FirstOrDefault(rule => FoxProReportKnowledgeBase.RuleMatches(rule, source));
            if (!string.IsNullOrWhiteSpace(savedRule?.TargetFieldName))
            {
                var savedField = FindAvailableDataField(savedRule.TargetFieldName);
                if (savedField != null)
                    return savedField.DbColumnName;
            }

            var canonical = FoxProReportKnowledgeBase.GetCanonicalFieldForSource(source);
            foreach (var candidate in FoxProReportKnowledgeBase.GetTargetFieldCandidates(canonical))
            {
                var field = FindAvailableDataField(candidate);
                if (field != null)
                    return field.DbColumnName;
            }

            return string.Empty;
        }

        private FieldDef? FindAvailableDataField(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            var normalized = NormalizeDesignerFieldName(candidate);
            return AvailableDataFields.FirstOrDefault(field =>
                field.DbColumnName.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                NormalizeDesignerFieldName(field.DbColumnName) == normalized ||
                NormalizeDesignerFieldName(field.Name) == normalized);
        }

        private string ResolveFieldDisplayName(string fieldName)
        {
            return FindAvailableDataField(fieldName)?.Name ?? string.Empty;
        }

        private void RefreshFrxMappingsForCurrentSource(bool clearMissingFields)
        {
            if (_frxElementMappings.Count == 0)
                return;

            var availableFieldNames = AvailableDataFields
                .SelectMany(field => new[] { field.Name, field.DbColumnName })
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in _frxElementMappings)
            {
                if (clearMissingFields &&
                    !string.IsNullOrWhiteSpace(mapping.MappedFieldName) &&
                    !availableFieldNames.Contains(mapping.MappedFieldName))
                {
                    mapping.MappedFieldName = string.Empty;
                }

                mapping.MappedDisplayName = ResolveFieldDisplayName(mapping.MappedFieldName);
            }

            ApplyFoxProRulesToEmptyMappings();
            UpdateMappingPreview();
        }

        private string GetCurrentRecognitionProfileCode()
        {
            try
            {
                var template = PrintFormService.DeserializePrintTemplate(TemplateTextBox.Text);
                return template.RecognitionProfileCode;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetMappingSource(FrXElementMappingViewModel mapping)
        {
            return string.IsNullOrWhiteSpace(mapping.ElementExpression)
                ? mapping.ElementText
                : mapping.ElementExpression;
        }

        private static string NormalizeDesignerFieldName(string value)
        {
            return System.Text.RegularExpressions.Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"[\s\.\-]+", "_");
        }
        private void UpdateMappingPreview()
        {
            if (_frxElementMappings.Count == 0)
            {
                MappingPreviewText.Text = "Нет элементов для отображения.";
                return;
            }

            var preview = new System.Text.StringBuilder();
            preview.AppendLine("=== Соответствия элементов макета ===");
            preview.AppendLine();

            foreach (var mapping in _frxElementMappings.OrderBy(m => m.Order))
            {
                var status = string.IsNullOrWhiteSpace(mapping.MappedFieldName)
                    ? "⏳ не назначено"
                    : $"✅ {mapping.MappedFieldName}";
                preview.AppendLine($"{mapping.Order + 1}. [{mapping.ElementType}] \"{mapping.ElementText}\" → {status}");
            }

            MappingPreviewText.Text = preview.ToString();
        }

        private void OnElementMappingChanged(object sender, EventArgs e)
        {
            UpdateMappingPreview();
        }

        private void OnElementMappingCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateMappingPreview));
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
                    RefreshFrxMappingsForCurrentSource(clearMissingFields: true);
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
                ClosedXmlOrientationCombo.SelectedIndex =
                    string.Equals(_closedXmlSheet.PageSetup.Orientation, "Portrait", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                ClosedXmlFreezeRowsBox.Text = _closedXmlSheet.PageSetup.FreezeRows.ToString();
                ClosedXmlFontCombo.Text = _closedXmlSheet.PageSetup.FontName;
                ClosedXmlFontSizeBox.Text = _closedXmlSheet.PageSetup.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ClosedXmlGridLinesCheck.IsChecked = _closedXmlSheet.PageSetup.ShowGridLines;

                ClosedXmlBlocksList.SelectedIndex = _closedXmlBlocks.Count > 0 ? 0 : -1;
                LoadClosedXmlBlockEditors(_closedXmlBlocks.FirstOrDefault());

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
            _closedXmlSheet.PageSetup ??= new ClosedXmlPageSetup();
            _closedXmlSheet.PageSetup.Orientation = ClosedXmlOrientationCombo.SelectedIndex == 1 ? "Portrait" : "Landscape";
            _closedXmlSheet.PageSetup.FreezeRows = ParseIntSafe(ClosedXmlFreezeRowsBox.Text);
            _closedXmlSheet.PageSetup.FontName = string.IsNullOrWhiteSpace(ClosedXmlFontCombo.Text)
                ? "Times New Roman"
                : ClosedXmlFontCombo.Text;
            _closedXmlSheet.PageSetup.FontSize = ParseDoubleSafe(ClosedXmlFontSizeBox.Text, 10);
            _closedXmlSheet.PageSetup.ShowGridLines = ClosedXmlGridLinesCheck.IsChecked ?? false;
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

    public class FrXElementMappingViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public Guid Id { get; set; }
        public int ElementOrder { get; set; }
        public string ElementType { get; set; } = "Text";
        public string ElementText { get; set; } = string.Empty;
        public string ElementExpression { get; set; } = string.Empty;
        public string BandType { get; set; } = "Detail";
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string FontName { get; set; } = "Arial";
        public double FontSize { get; set; } = 9;
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string Alignment { get; set; } = "Left";
        public int Order { get; set; }

        private string _mappedFieldName = string.Empty;
        public string MappedFieldName
        {
            get => _mappedFieldName;
            set { _mappedFieldName = value; OnPropertyChanged(nameof(MappedFieldName)); }
        }

        public string MappedDisplayName { get; set; } = string.Empty;
        public string DataSource { get; set; } = string.Empty;

        private string _formatString = string.Empty;
        public string FormatString
        {
            get => _formatString;
            set { _formatString = value; OnPropertyChanged(nameof(FormatString)); }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        private string _customText = string.Empty;
        public string CustomText
        {
            get => _customText;
            set { _customText = value; OnPropertyChanged(nameof(CustomText)); }
        }
        public string LeftTop => $"{(int)Left}/{(int)Top}";

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}









