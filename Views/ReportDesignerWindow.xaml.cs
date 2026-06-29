using BIS.ERP.Models;
using BIS.ERP.Services;
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
        public ObservableCollection<FieldDef> AvailableDataFields { get; } = new();
        public ObservableCollection<FieldDef> AvailableFilterFields { get; } = new();
        private ObservableCollection<FrXElementMappingViewModel> _frxElementMappings = new();

        public ReportDesignerWindow(Report report = null)
        {
            InitializeComponent();
            DataContext = this;

            _reportFields = new ObservableCollection<ReportField>();
            _reportFilters = new ObservableCollection<ReportFilter>();

            ReportFieldsGrid.ItemsSource = _reportFields;
            FiltersList.ItemsSource = _reportFilters;
            ElementMappingGrid.ItemsSource = _frxElementMappings;
            ConfigureFieldColumns();
            InitializeNativeDesignerState();

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
                    LoadReport(report);
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

            DataSourceCombo.Items.Clear();
            DataSourceCombo.Items.Add(new ComboBoxItem { Tag = null, Content = "-- Выберите источник данных --" });

            foreach (var catalog in _availableCatalogs)
            {
                DataSourceCombo.Items.Add(new ComboBoxItem
                {
                    Tag = catalog,
                    Content = $"{(catalog.ObjectType == "Document" ? "📄" : "📚")} {catalog.Name}"
                });
            }

            DataSourceCombo.SelectedIndex = 0;
        }

        private async void OnDataSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DataSourceCombo.SelectedItem as ComboBoxItem;
            if (selected?.Tag is MetadataObject catalog)
            {
                await LoadAvailableFields(catalog);

                // ПРИНУДИТЕЛЬНО ОБНОВЛЯЕМ UI
                AvailableFields.Items.Refresh();
            }
            else
            {
                AvailableFields.ItemsSource = null;
                AvailableDataFields.Clear();
            }
        }

        private async Task LoadAvailableFields(MetadataObject catalog)
        {
            var addedFieldNames = _reportFields.Select(f => f.DisplayName).ToHashSet();

            var fields = new List<FieldDef>();

            foreach (var field in catalog.Fields.OrderBy(f => f.Order))
            {
                if (!addedFieldNames.Contains(field.Name))
                {
                    fields.Add(new FieldDef { Name = field.Name, DbColumnName = field.DbColumnName, Type = field.FieldType });
                }
            }

            // ОБНОВЛЯЕМ ЧЕРЕЗ DISPATCHER
            await Dispatcher.InvokeAsync(() =>
            {
                AvailableDataFields.Clear();
                foreach (var field in fields)
                    AvailableDataFields.Add(field);

                foreach (var field in GetPrintFormComputedFields())
                    AvailableDataFields.Add(field);

                AvailableFields.ItemsSource = fields;
                AvailableFilterFields.Clear();

                foreach (var field in catalog.Fields.OrderBy(field => field.Order))
                {
                    AvailableFilterFields.Add(new FieldDef
                    {
                        Name = field.Name,
                        DbColumnName = field.DbColumnName,
                        Type = field.FieldType
                    });
                }

                // Принудительное обновление
                AvailableFields.Items.Refresh();
                ElementMappingGrid.Items.Refresh();
            });
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

        private void OnFieldDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedField();
        }

        private void AddSelectedField()
        {
            var field = AvailableFields.SelectedItem as FieldDef;
            if (field != null && !_reportFields.Any(f => f.FieldName == field.DbColumnName))
            {
                _reportFields.Add(new ReportField
                {
                    Id = Guid.NewGuid(),
                    FieldName = field.DbColumnName,
                    DisplayName = field.Name,
                    Order = _reportFields.Count + 1,
                    IsVisible = true,
                    Width = 120,
                    Alignment = "Left"
                });

                if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                {
                    _ = LoadAvailableFields(catalog);
                }
            }
        }

        private void OnRemoveFieldClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var field = button?.DataContext as ReportField;
            if (field != null)
            {
                _reportFields.Remove(field);

                if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                {
                    _ = LoadAvailableFields(catalog);
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

        private void LoadReport(Report report)
        {
            _currentReport = report;
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
            else if (report.ReportType == "FoxProLayout" && !string.IsNullOrWhiteSpace(report.Template))
            {
                // Если поля не сохранились, но есть FRX-шаблон — извлекаем поля из него
                var extractedFields = PrintFormService.ExtractReportFieldsFromTemplate(report.Template);
                int fieldOrder = 1;
                foreach (var field in extractedFields)
                {
                    field.Order = fieldOrder++;
                    _reportFields.Add(field);
                }
            }

            // Обновляем доступные поля для источника данных
            if (report.DataSourceId.HasValue)
            {
                var catalog = _availableCatalogs.FirstOrDefault(c => c.Id == report.DataSourceId);
                if (catalog != null)
                {
                    _ = LoadAvailableFields(catalog);
                }
            }

            // Загрузка фильтров
            _reportFilters.Clear();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                _reportFilters.Add(filter);
            }

            if (!string.IsNullOrWhiteSpace(report.Template))
            {
                _ = LoadFrxElementMappings(report.Template, report.ElementMappings);
                LoadNativeTemplateFromReport(report);
            }
            else
            {
                LoadNativeTemplateFromReport(report);
            }
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

                    var pdfBytes = _printFormService.ExportTemplatePreview(report);
                    var previewWindow = new PdfPreviewWindow(pdfBytes) { Owner = this };
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

                var data = await _reportService.GetReportDataAsync(report);
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

        private Task ExportPrintFormPreviewAsync(bool showConfirmation, Report? report = null)
        {
            report ??= BuildReportFromForm();
            if (report == null)
                return Task.CompletedTask;

            var pdfBytes = _printFormService.ExportTemplatePreview(report);
            var tempPdf = Path.Combine(Path.GetTempPath(), $"preview_{Guid.NewGuid():N}.pdf");
            File.WriteAllBytes(tempPdf, pdfBytes);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempPdf,
                UseShellExecute = true,
                Verb = "open"
            };
            System.Diagnostics.Process.Start(psi);

            StatusText.Text = "✅ Предпросмотр открыт";
            return Task.CompletedTask;
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

                var data = await _reportService.GetReportDataAsync(tempReport);

                var previewWindow = new ReportPreviewWindow(data, tempReport, _reportService);
                previewWindow.Owner = this;
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

                SyncNativeTemplateToTemplateBoxIfNeeded();

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
                _currentReport.SourceFormat = _currentReport.ReportType == "FoxProLayout" ? "FoxProFRX" : "Native";
                _currentReport.Template = TemplateTextBox?.Text ?? "";
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
                        Order = f.Order
                    }).ToList();

                var mappingsToAdd = BuildReportElementMappings(_currentReport.Id);

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();

                if (_currentReport.Id == Guid.Empty)
                {
                    _currentReport.Id = Guid.NewGuid();
                    _currentReport.Fields = fieldsToAdd;
                    _currentReport.Filters = filtersToAdd;
                    foreach (var mapping in mappingsToAdd)
                        mapping.ReportId = _currentReport.Id;
                    _currentReport.ElementMappings = mappingsToAdd;
                    context.Reports.Add(_currentReport);
                }
                else
                {
                    var existingReport = await context.Reports
                        .Include(r => r.Fields)
                        .Include(r => r.Filters)
                        .Include(r => r.ElementMappings)
                        .FirstOrDefaultAsync(r => r.Id == _currentReport.Id);

                    if (existingReport == null)
                    {
                        _currentReport.Id = Guid.NewGuid();
                        _currentReport.Fields = fieldsToAdd;
                        _currentReport.Filters = filtersToAdd;
                        foreach (var mapping in mappingsToAdd)
                            mapping.ReportId = _currentReport.Id;
                        _currentReport.ElementMappings = mappingsToAdd;
                        context.Reports.Add(_currentReport);
                    }
                    else
                    {
                        // ✅ УДАЛЯЕМ СТАРЫЙ И СОЗДАЕМ НОВЫЙ
                        var reportId = existingReport.Id;
                        var createdAt = existingReport.CreatedAt;

                        context.ReportFields.RemoveRange(existingReport.Fields);
                        context.ReportFilters.RemoveRange(existingReport.Filters);
                        context.ReportElementMappings.RemoveRange(existingReport.ElementMappings);
                        context.Reports.Remove(existingReport);
                        await context.SaveChangesAsync();

                        _currentReport.Id = reportId;
                        _currentReport.CreatedAt = createdAt;
                        _currentReport.Fields = fieldsToAdd;
                        _currentReport.Filters = filtersToAdd;
                        foreach (var mapping in mappingsToAdd)
                            mapping.ReportId = reportId;
                        _currentReport.ElementMappings = mappingsToAdd;
                        context.Reports.Add(_currentReport);
                    }
                }

                await context.SaveChangesAsync();

                MessageBox.Show($"Отчет \"{_currentReport.Name}\" сохранен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

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

            SyncNativeTemplateToTemplateBoxIfNeeded();

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
                SourceFormat = GetSelectedReportType() == "FoxProLayout" ? "FoxProFRX" : "Native",
                //Template = _currentReport?.Template ?? "",
                Fields = _reportFields.OrderBy(field => field.Order).ToList(),
                Filters = _reportFilters.Where(filter => !string.IsNullOrWhiteSpace(filter.FieldName)).ToList(),
                PageOrientation = OrientationCombo.SelectedIndex == 1 ? "Landscape" : "Portrait",
                FontName = FontCombo.Text,
                FontSize = int.TryParse(FontSizeCombo.Text, out var fontSize) ? fontSize : 10,
                HeaderColor = GetSelectedColor(HeaderColorCombo),
                AlternateRowColors = AlternateRowColorsCheck.IsChecked ?? true,
                ShowGridLines = ShowGridLinesCheck.IsChecked ?? true,
                ShowGrandTotal = ShowGrandTotalCheck.IsChecked ?? true,
                ShowPageNumbers = ShowPageNumbersCheck.IsChecked ?? true,
                Template = TemplateTextBox.Text
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
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var printFormService = new PrintFormService(context);
                var templateJson = await printFormService.ParseFrxFileAsync(fileBytes, openDialog.FileName);

                if (!string.IsNullOrEmpty(templateJson))
                {
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

                    // ИЗВЛЕКАЕМ ПОЛЯ ИЗ ШАБЛОНА И ЗАПОЛНЯЕМ ИМИ ТАБЛИЦУ ПОЛЕЙ
                    var extractedFields = PrintFormService.ExtractReportFieldsFromTemplate(templateJson);
                    _reportFields.Clear();
                    int fieldOrder = 1;
                    foreach (var field in extractedFields)
                    {
                        field.Order = fieldOrder++;
                        _reportFields.Add(field);
                    }
                    ReportFieldsGrid.Items.Refresh();

                    var nativeTemplate = PrintFormService.DeserializePrintTemplate(templateJson);

                    // ЗАПОЛНЯЕМ ТАБЛИЦУ СООТВЕТСТВИЙ ЭЛЕМЕНТОВ МАКЕТА
                    await LoadFrxElementMappings(templateJson);
                    LoadNativeTemplate(nativeTemplate);
                    WarnIfTemplateHasOnlyGeometry(nativeTemplate, showDialog: true);

                    // Также обновляем список доступных полей, убирая уже добавленные
                    if (DataSourceCombo.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
                    {
                        _ = LoadAvailableFields(catalog);
                    }

                    StatusText.Text = $"✅ Загружено: {Path.GetFileName(openDialog.FileName)}, полей: {_reportFields.Count}";

                    MessageBox.Show($"FRX-макет успешно загружен!\n\n" +
                                   $"Файл: {Path.GetFileName(openDialog.FileName)}\n" +
                                   $"Размер: {templateJson.Length} символов\n" +
                                   $"Извлечено полей: {_reportFields.Count}",
                                   "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
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

                ElementMappingGrid.Items.Refresh();
                UpdateMappingPreview();

                if (_frxElementMappings.Count > 0)
                    FrXFieldsTab.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MappingPreviewText.Text = $"Ошибка загрузки элементов: {ex.Message}";
            }
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
