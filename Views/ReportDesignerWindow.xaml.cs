using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.IO;
using BIS.ERP.Models;
using BIS.ERP.Services;

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
                    _currentReport = new Report();
                }

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
                _currentReport.Icon = "📊";
                _currentReport.UpdatedAt = DateTime.UtcNow;

                if (_currentReport.CreatedAt == DateTime.MinValue)
                    _currentReport.CreatedAt = DateTime.UtcNow;

                _currentReport.PageOrientation = OrientationCombo.SelectedIndex == 1 ? "Landscape" : "Portrait";
                _currentReport.FontName = FontCombo.Text;
                _currentReport.FontSize = int.Parse(FontSizeCombo.Text);
                _currentReport.HeaderColor = GetSelectedColor(HeaderColorCombo);
                _currentReport.AlternateRowColors = AlternateRowColorsCheck.IsChecked ?? true;
                _currentReport.ShowGridLines = ShowGridLinesCheck.IsChecked ?? true;
                _currentReport.ShowGrandTotal = ShowGrandTotalCheck.IsChecked ?? true;
                _currentReport.ShowPageNumbers = ShowPageNumbersCheck.IsChecked ?? true;

                _currentReport.Fields.Clear();
                foreach (var field in _reportFields)
                {
                    _currentReport.Fields.Add(new ReportField
                    {
                        Id = field.Id == Guid.Empty ? Guid.NewGuid() : field.Id,
                        FieldName = field.FieldName,
                        DisplayName = field.DisplayName,
                        Order = field.Order,
                        Width = field.Width,
                        IsVisible = field.IsVisible,
                        Alignment = field.Alignment,
                        AggregateType = field.AggregateType,
                        Format = field.Format
                    });
                }

                _currentReport.Filters.Clear();
                foreach (var filter in _reportFilters)
                {
                    if (!string.IsNullOrWhiteSpace(filter.FieldName))
                    {
                        _currentReport.Filters.Add(new ReportFilter
                        {
                            Id = filter.Id == Guid.Empty ? Guid.NewGuid() : filter.Id,
                            FieldName = filter.FieldName,
                            Operation = filter.Operation,
                            Value = filter.Value,
                            Value2 = filter.Value2,
                            Order = filter.Order
                        });
                    }
                }

                await _reportService.SaveReportAsync(_currentReport);

                MessageBox.Show($"Отчет \"{_currentReport.Name}\" сохранен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
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
                Template = _currentReport?.Template ?? "",
                Fields = _reportFields.OrderBy(field => field.Order).ToList(),
                Filters = _reportFilters.Where(filter => !string.IsNullOrWhiteSpace(filter.FieldName)).ToList(),
                PageOrientation = OrientationCombo.SelectedIndex == 1 ? "Landscape" : "Portrait",
                FontName = FontCombo.Text,
                FontSize = int.TryParse(FontSizeCombo.Text, out var fontSize) ? fontSize : 10,
                HeaderColor = GetSelectedColor(HeaderColorCombo),
                AlternateRowColors = AlternateRowColorsCheck.IsChecked ?? true,
                ShowGridLines = ShowGridLinesCheck.IsChecked ?? true,
                ShowGrandTotal = ShowGrandTotalCheck.IsChecked ?? true,
                ShowPageNumbers = ShowPageNumbersCheck.IsChecked ?? true
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
    }

    public class FieldDef
    {
        public string Name { get; set; } = string.Empty;
        public string DbColumnName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
