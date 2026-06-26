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
        public ObservableCollection<FieldDef> AvailableFilterFields { get; } = new();

        public ReportDesignerWindow(Report report = null)
        {
            InitializeComponent();
            DataContext = this;

            _reportFields = new ObservableCollection<ReportField>();
            _reportFilters = new ObservableCollection<ReportFilter>();

            ReportFieldsGrid.ItemsSource = _reportFields;
            FiltersList.ItemsSource = _reportFilters;
            ConfigureFieldColumns();

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
            if (field != null && !_reportFields.Any(f => f.FieldName == field.Name))
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
            foreach (var field in report.Fields.OrderBy(f => f.Order))
            {
                _reportFields.Add(field);
            }

            // Загрузка фильтров
            _reportFilters.Clear();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                _reportFilters.Add(filter);
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
            if (IsPrintFormCheck.IsChecked == true)
                await ExportPrintFormPreviewAsync(false);
            else
                await GenerateAndShowReport();
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

            var dialog = new SaveFileDialog
            {
                Title = "Сохранить предпросмотр печатной формы",
                Filter = "PDF файлы (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = $"{GetSafeFileName(report.Name)}_предпросмотр.pdf"
            };
            if (dialog.ShowDialog() != true)
                return Task.CompletedTask;

            File.WriteAllBytes(dialog.FileName, _printFormService.ExportTemplatePreview(report));
            StatusText.Text = $"Предпросмотр сформирован: {dialog.FileName}";
            if (showConfirmation)
                MessageBox.Show("PDF печатной формы успешно сформирован.", "Печатная форма",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();

                if (_currentReport.Id == Guid.Empty)
                {
                    _currentReport.Id = Guid.NewGuid();
                    _currentReport.Fields = fieldsToAdd;
                    _currentReport.Filters = filtersToAdd;
                    context.Reports.Add(_currentReport);
                }
                else
                {
                    var existingReport = await context.Reports
                        .Include(r => r.Fields)
                        .Include(r => r.Filters)
                        .FirstOrDefaultAsync(r => r.Id == _currentReport.Id);

                    if (existingReport == null)
                    {
                        _currentReport.Id = Guid.NewGuid();
                        _currentReport.Fields = fieldsToAdd;
                        _currentReport.Filters = filtersToAdd;
                        context.Reports.Add(_currentReport);
                    }
                    else
                    {
                        // ✅ УДАЛЯЕМ СТАРЫЙ И СОЗДАЕМ НОВЫЙ
                        var reportId = existingReport.Id;
                        var createdAt = existingReport.CreatedAt;

                        context.ReportFields.RemoveRange(existingReport.Fields);
                        context.ReportFilters.RemoveRange(existingReport.Filters);
                        context.Reports.Remove(existingReport);
                        await context.SaveChangesAsync();

                        _currentReport.Id = reportId;
                        _currentReport.CreatedAt = createdAt;
                        _currentReport.Fields = fieldsToAdd;
                        _currentReport.Filters = filtersToAdd;
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

            return new Report
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

                    StatusText.Text = $"✅ Загружено: {Path.GetFileName(openDialog.FileName)}";

                    MessageBox.Show($"FRX-макет успешно загружен!\n\n" +
                                   $"Файл: {Path.GetFileName(openDialog.FileName)}\n" +
                                   $"Размер: {templateJson.Length} символов",
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
    }



    public class FieldDef
    {
        public string Name { get; set; } = string.Empty;
        public string DbColumnName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
