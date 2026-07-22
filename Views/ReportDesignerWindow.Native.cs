using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BIS.ERP.Views
{
    public partial class ReportDesignerWindow
    {
        private const double NativeDesignerBaseScale = 0.28;
        private double _nativeZoomMultiplier = 1.0;
        private double NativeDesignerScale => NativeDesignerBaseScale * _nativeZoomMultiplier;
        private readonly ObservableCollection<NativeReportElementViewModel> _nativeElements = new();
        private PrintFormTemplate _nativeTemplate = PrintFormService.CreateBlankNativeTemplate();
        private NativeReportElementViewModel? _selectedNativeElement;
        private NativeReportElementViewModel? _draggedNativeElement;
        private NativeReportElementViewModel? _rotatingNativeLine;
        private NativeReportElementViewModel? _resizedNativeElement;
        private Point _nativeDragStart;
        private Point _nativeResizeStart;
        private double _nativeDragStartLeft;
        private double _nativeDragStartTop;
        private double _nativeResizeStartWidth;
        private double _nativeResizeStartHeight;
        private bool _suspendNativeRender;
        private string? _deferredNativeTemplateJson;
        private bool _deferredNativeTemplateWarning;

        private void InitializeNativeDesignerState()
        {
            NativeElementsGrid.ItemsSource = _nativeElements;
            _nativeElements.CollectionChanged += (_, _) =>
            {
                if (!_suspendNativeRender)
                    RenderNativeDesigner();
            };
            NativeAlignmentCombo.SelectedIndex = 0;
            NativeDesignerTab.AddHandler(Selector.SelectedEvent, new RoutedEventHandler(OnNativeDesignerTabSelected));
            LoadNativeTemplate(PrintFormService.CreateBlankNativeTemplate());
            NativeZoomSlider.Value = 5;
            NativeZoomLabel.Text = "5%";
        }

        private void OnNativeZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (NativeDesignerCanvas == null || NativeZoomLabel == null || NativeZoomSlider == null)
                return;

            _nativeZoomMultiplier = e.NewValue / 100.0;
            NativeZoomLabel.Text = $"{e.NewValue:F0}%";
            RenderNativeDesigner();
        }

        private void OnNativeDesignerMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (NativeDesignerCanvas == null || NativeZoomSlider == null || NativeZoomLabel == null)
                return;

            e.Handled = true;

            var delta = e.Delta > 0 ? 5 : -5;
            var newValue = Math.Clamp(NativeZoomSlider.Value + delta, NativeZoomSlider.Minimum, NativeZoomSlider.Maximum);
            NativeZoomSlider.Value = newValue;
        }

        private IEnumerable<FieldDef> GetPrintFormComputedFields()
        {
            return new[]
            {
                new FieldDef { Name = "Номер документа", DbColumnName = "number", Type = "Computed" },
                new FieldDef { Name = "Дата", DbColumnName = "date", Type = "Computed" },
                new FieldDef { Name = "Организация", DbColumnName = "organization", Type = "Computed" },
                new FieldDef { Name = "ИНН организации", DbColumnName = "inn", Type = "Computed" },
                new FieldDef { Name = "ОКПО организации", DbColumnName = "okpo", Type = "Computed" },
                new FieldDef { Name = "Сотрудник / получатель", DbColumnName = "person", Type = "Computed" },
                new FieldDef { Name = "Касса", DbColumnName = "cash_desk", Type = "Computed" },
                new FieldDef { Name = "Корр. счет", DbColumnName = "correspondent_account", Type = "Computed" },
                new FieldDef { Name = "Дебет", DbColumnName = "debit_account", Type = "Computed" },
                new FieldDef { Name = "Кредит", DbColumnName = "credit_account", Type = "Computed" },
                new FieldDef { Name = "Код дебета", DbColumnName = "debit_code", Type = "Computed" },
                new FieldDef { Name = "Код кредита", DbColumnName = "credit_code", Type = "Computed" },
                new FieldDef { Name = "Сумма", DbColumnName = "amount", Type = "Computed" },
                new FieldDef { Name = "Сумма в валюте", DbColumnName = "amount_in_currency", Type = "Computed" },
                new FieldDef { Name = "Сумма прописью", DbColumnName = "amount_in_words", Type = "Computed" },
                new FieldDef { Name = "Основание", DbColumnName = "basis", Type = "Computed" },
                new FieldDef { Name = "Примечание", DbColumnName = "note", Type = "Computed" },
                new FieldDef { Name = "Валюта", DbColumnName = "currency", Type = "Computed" },
                new FieldDef { Name = "Счет-фактура - номер", DbColumnName = "fact.NOM_BL", Type = "Computed" },
                new FieldDef { Name = "Счет-фактура - серия/ЭСФ", DbColumnName = "fact.SER_BL", Type = "Computed" },
                new FieldDef { Name = "Счет-фактура - дата", DbColumnName = "fact.D_SALE", Type = "Computed" },
                new FieldDef { Name = "Счет-фактура - основание", DbColumnName = "fact.TXT_KOR", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - наименование операции", DbColumnName = "operation_name", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - дебет", DbColumnName = "debit_account", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - кредит", DbColumnName = "credit_account", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - сумма Дт", DbColumnName = "debit_amount", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - сумма Кт", DbColumnName = "credit_amount", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - номер документа", DbColumnName = "document_number", Type = "Computed" },
                new FieldDef { Name = "Акт сверки - дата операции", DbColumnName = "document_date", Type = "Computed" },
                new FieldDef { Name = "Модуль", DbColumnName = "module", Type = "Computed" },
                new FieldDef { Name = "Период с", DbColumnName = "period_start", Type = "Computed" },
                new FieldDef { Name = "Период по", DbColumnName = "period_end", Type = "Computed" },
                new FieldDef { Name = "Заголовок отчета", DbColumnName = "report_title", Type = "Computed" },
                new FieldDef { Name = "Итоговое описание", DbColumnName = "report_summary", Type = "Computed" },
                new FieldDef { Name = "Организация А - наименование", DbColumnName = "fact.C_NAME_ORG", Type = "Computed" },
                new FieldDef { Name = "Организация А - ИНН", DbColumnName = "fact.C_INN", Type = "Computed" },
                new FieldDef { Name = "Организация А - ОКПО", DbColumnName = "fact.C_OKPO", Type = "Computed" },
                new FieldDef { Name = "Организация А - адрес", DbColumnName = "fact.C_ADR", Type = "Computed" },
                new FieldDef { Name = "Организация А - телефон", DbColumnName = "fact.C_PHONE", Type = "Computed" },
                new FieldDef { Name = "Организация А - банк", DbColumnName = "fact.C_BANK", Type = "Computed" },
                new FieldDef { Name = "Организация А - расчетный счет", DbColumnName = "fact.C_RS", Type = "Computed" },
                new FieldDef { Name = "Организация А - БИК", DbColumnName = "fact.C_BIK", Type = "Computed" },
                new FieldDef { Name = "Организация А - руководитель", DbColumnName = "fact.C_DIR", Type = "Computed" },
                new FieldDef { Name = "Организация А - главный бухгалтер", DbColumnName = "fact.C_BUH", Type = "Computed" },
                new FieldDef { Name = "Организация Б - наименование", DbColumnName = "fact.D_NAME_ORG", Type = "Computed" },
                new FieldDef { Name = "Организация Б - ИНН", DbColumnName = "fact.D_INN", Type = "Computed" },
                new FieldDef { Name = "Организация Б - ОКПО", DbColumnName = "fact.D_OKPO", Type = "Computed" },
                new FieldDef { Name = "Организация Б - адрес", DbColumnName = "fact.D_ADR", Type = "Computed" },
                new FieldDef { Name = "Организация Б - телефон", DbColumnName = "fact.D_PHONE", Type = "Computed" },
                new FieldDef { Name = "Организация Б - банк", DbColumnName = "fact.D_BANK", Type = "Computed" },
                new FieldDef { Name = "Организация Б - расчетный счет", DbColumnName = "fact.D_RS", Type = "Computed" },
                new FieldDef { Name = "Организация Б - БИК", DbColumnName = "fact.D_BIK", Type = "Computed" },
                new FieldDef { Name = "Организация Б - руководитель", DbColumnName = "fact.D_DIR", Type = "Computed" },
                new FieldDef { Name = "Организация Б - главный бухгалтер", DbColumnName = "fact.D_BUH", Type = "Computed" }
            };
        }

        private void OnReportTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Тип отчета не должен менять визуальный макет автоматически:
            // дизайнер является универсальным движком компоновки.
        }

        private void LoadNativeTemplateFromReport(Report report)
        {
            if (!string.IsNullOrWhiteSpace(report.Template))
            {
                DeferNativeTemplate(report.Template, showWarning: false);
                return;
            }

            LoadNativeTemplate(PrintFormService.CreateBlankNativeTemplate());
        }

        private void DeferNativeTemplate(string templateJson, bool showWarning)
        {
            _deferredNativeTemplateJson = templateJson;
            _deferredNativeTemplateWarning = showWarning;
            _nativeElements.Clear();
            NativeDesignerCanvas?.Children.Clear();
        }

        private void OnNativeDesignerTabSelected(object sender, RoutedEventArgs e)
        {
            LoadDeferredNativeTemplateIfNeeded();
        }

        private void LoadDeferredNativeTemplateIfNeeded()
        {
            if (string.IsNullOrWhiteSpace(_deferredNativeTemplateJson))
                return;

            try
            {
                var template = PrintFormService.DeserializePrintTemplate(_deferredNativeTemplateJson);
                _deferredNativeTemplateJson = null;
                LoadNativeTemplate(template);
                WarnIfTemplateHasOnlyGeometry(template, _deferredNativeTemplateWarning);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка загрузки нативного макета: {ex.Message}";
            }
        }

        private void LoadNativeTemplate(PrintFormTemplate template)
        {
            _deferredNativeTemplateJson = null;
            _nativeTemplate = template;
            if (string.IsNullOrWhiteSpace(_nativeTemplate.SourceFormat))
                _nativeTemplate.SourceFormat = "Native";

            _suspendNativeRender = true;
            NativeElementsGrid.ItemsSource = null;
            try
            {
                _nativeElements.Clear();
                foreach (var element in _nativeTemplate.Elements.OrderBy(item => item.Order))
                    _nativeElements.Add(NativeReportElementViewModel.FromElement(element));
            }
            finally
            {
                NativeElementsGrid.ItemsSource = _nativeElements;
                _suspendNativeRender = false;
            }

            RenderNativeDesigner();
            SelectNativeElement(_nativeElements.FirstOrDefault());
        }

        private void WarnIfTemplateHasOnlyGeometry(PrintFormTemplate template, bool showDialog)
        {
            if (!TemplateHasOnlyGeometry(template))
                return;

            const string message =
                "Макет содержит геометрию, но в нем нет текстов и привязанных полей. " +
                "Повторно импортируйте исходный .frx, рядом с которым лежит одноименный .FRT.";

            StatusText.Text = message;

            if (showDialog)
            {
                MessageBox.Show(
                    message,
                    "Макет без текстов и полей",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static bool TemplateHasOnlyGeometry(PrintFormTemplate template)
        {
            return template.Elements.Count > 0 &&
                   template.Elements.All(element =>
                       string.IsNullOrWhiteSpace(element.Text) &&
                       string.IsNullOrWhiteSpace(element.Expression));
        }

        private void SyncNativeTemplateToTemplateBoxIfNeeded()
        {
            if (IsPrintFormCheck.IsChecked != true)
                return;
            if (!string.IsNullOrWhiteSpace(_deferredNativeTemplateJson))
                return;

            _nativeTemplate.SourceFormat = GetSelectedReportType() == "FoxProLayout"
                ? "FoxProFRX"
                : "Native";
            _nativeTemplate.Elements = _nativeElements
                .OrderBy(item => item.Order)
                .Select(item => item.ToElement())
                .ToList();

            if (_nativeTemplate.Bands.Count == 0)
            {
                _nativeTemplate.Bands = new List<PrintFormBand>
                {
                    new() { Type = "Header", Top = 0, Height = _nativeTemplate.PageHeight * 0.18, Order = 0 },
                    new() { Type = "Body", Top = _nativeTemplate.PageHeight * 0.18, Height = _nativeTemplate.PageHeight * 0.52, Order = 1 },
                    new() { Type = "Footer", Top = _nativeTemplate.PageHeight * 0.70, Height = _nativeTemplate.PageHeight * 0.25, Order = 2 }
                };
            }

            TemplateTextBox.Text = JsonSerializer.Serialize(_nativeTemplate, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        private List<ReportElementMapping> BuildReportElementMappings(Guid reportId)
        {
            if (GetSelectedReportType() != "FoxProLayout")
                return new List<ReportElementMapping>();

            return _frxElementMappings
                .OrderBy(item => item.Order)
                .Select(item =>
                {
                    var displayName = AvailableDataFields
                        .FirstOrDefault(field => field.DbColumnName == item.MappedFieldName)?.Name ?? item.MappedDisplayName;
                    return new ReportElementMapping
                    {
                        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
                        ReportId = reportId,
                        ElementOrder = item.ElementOrder,
                        ElementType = item.ElementType,
                        ElementText = item.ElementText,
                        ElementExpression = item.ElementExpression,
                        BandType = item.BandType,
                        Left = item.Left,
                        Top = item.Top,
                        Width = item.Width,
                        Height = item.Height,
                        FontName = item.FontName,
                        FontSize = item.FontSize,
                        Bold = item.Bold,
                        Italic = item.Italic,
                        Alignment = item.Alignment,
                        Order = item.Order,
                        MappedFieldName = item.MappedFieldName,
                        MappedDisplayName = displayName,
                        DataSource = item.DataSource,
                        FormatString = item.FormatString,
                        IsVisible = item.IsVisible,
                        CustomText = item.CustomText
                    };
                })
                .ToList();
        }

        private void RenderNativeDesigner()
        {
            if (NativeDesignerCanvas == null)
                return;

            NativeDesignerCanvas.Children.Clear();
            NativeDesignerCanvas.Width = Math.Max(600, _nativeTemplate.PageWidth * NativeDesignerScale);
            NativeDesignerCanvas.Height = Math.Max(800, _nativeTemplate.PageHeight * NativeDesignerScale);

            RenderNativeBands();
            foreach (var element in _nativeElements.OrderBy(item => item.Order))
                RenderNativeElement(element);
        }

        private void RenderNativeBands()
        {
            foreach (var band in _nativeTemplate.Bands.OrderBy(item => item.Order))
            {
                var border = new Border
                {
                    BorderBrush = Brushes.LightSteelBlue,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = new SolidColorBrush(Color.FromArgb(24, 52, 152, 219)),
                    Width = NativeDesignerCanvas.Width,
                    Height = Math.Max(18, band.Height * NativeDesignerScale),
                    Child = new TextBlock
                    {
                        Text = band.Type,
                        Foreground = Brushes.SlateGray,
                        FontSize = 11,
                        Margin = new Thickness(6, 2, 0, 0)
                    }
                };
                Canvas.SetLeft(border, 0);
                Canvas.SetTop(border, band.Top * NativeDesignerScale);
                NativeDesignerCanvas.Children.Add(border);
            }
        }

        private void RenderNativeElement(NativeReportElementViewModel element)
        {
            FrameworkElement visual;
            var isSelected = ReferenceEquals(element, _selectedNativeElement);

            if (element.Type == "Line")
            {
                var line = new Line
                {
                    X1 = 0,
                    Y1 = 0,
                    X2 = Math.Max(1, element.Width * NativeDesignerScale),
                    Y2 = element.Height * NativeDesignerScale,
                    Stroke = isSelected ? Brushes.DodgerBlue : Brushes.Black,
                    StrokeThickness = isSelected ? 2.5 : 1.4,
                    Cursor = Cursors.SizeAll
                };
                visual = line;
            }
            else if (element.Type == "Box")
            {
                visual = new Border
                {
                    Width = Math.Max(8, element.Width * NativeDesignerScale),
                    Height = Math.Max(8, element.Height * NativeDesignerScale),
                    BorderBrush = isSelected ? Brushes.DodgerBlue : Brushes.Black,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeAll
                };
            }
            else
            {
                var text = string.IsNullOrWhiteSpace(element.Expression)
                    ? element.Text
                    : $"{{{element.Expression}}}";
                visual = new Border
                {
                    Width = Math.Max(24, element.Width * NativeDesignerScale),
                    Height = Math.Max(18, element.Height * NativeDesignerScale),
                    BorderBrush = isSelected ? Brushes.DodgerBlue : Brushes.Transparent,
                    BorderThickness = new Thickness(isSelected ? 1.5 : 0),
                    Background = isSelected
                        ? new SolidColorBrush(Color.FromArgb(32, 30, 144, 255))
                        : Brushes.Transparent,
                    Cursor = Cursors.SizeAll,
                    Child = new TextBlock
                    {
                        Text = text,
                        FontFamily = new FontFamily(element.FontName),
                        FontSize = Math.Max(8, element.FontSize),
                        FontWeight = element.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = element.Italic ? FontStyles.Italic : FontStyles.Normal,
                        TextAlignment = element.Alignment == "Right"
                            ? TextAlignment.Right
                            : element.Alignment == "Center" ? TextAlignment.Center : TextAlignment.Left,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.Black
                    }
                };
            }

            visual.Tag = element;
            visual.MouseLeftButtonDown += OnNativeElementMouseLeftButtonDown;
            Canvas.SetLeft(visual, element.Left * NativeDesignerScale);
            Canvas.SetTop(visual, element.Top * NativeDesignerScale);
            NativeDesignerCanvas.Children.Add(visual);

            if (isSelected && element.Type == "Line")
                RenderNativeLineRotateHandle(element);
            else if (isSelected)
                RenderNativeResizeHandle(element);
        }

        private void RenderNativeLineRotateHandle(NativeReportElementViewModel element)
        {
            var handle = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                Cursor = Cursors.Cross,
                Tag = element,
                ToolTip = "Повернуть линию"
            };
            handle.MouseLeftButtonDown += OnNativeLineRotateHandleMouseLeftButtonDown;

            Canvas.SetLeft(handle, (element.Left + element.Width) * NativeDesignerScale - handle.Width / 2);
            Canvas.SetTop(handle, (element.Top + element.Height) * NativeDesignerScale - handle.Height / 2);
            NativeDesignerCanvas.Children.Add(handle);
        }

        private void RenderNativeResizeHandle(NativeReportElementViewModel element)
        {
            var handle = new Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                Cursor = Cursors.SizeNWSE,
                Tag = element,
                ToolTip = "Изменить размер элемента"
            };
            handle.MouseLeftButtonDown += OnNativeResizeHandleMouseLeftButtonDown;

            Canvas.SetLeft(handle, (element.Left + element.Width) * NativeDesignerScale - handle.Width / 2);
            Canvas.SetTop(handle, (element.Top + element.Height) * NativeDesignerScale - handle.Height / 2);
            NativeDesignerCanvas.Children.Add(handle);
        }

        private void OnNativeElementMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement control || control.Tag is not NativeReportElementViewModel element)
                return;

            SelectNativeElement(element);
            NativeDesignerCanvas.Focus();
            _draggedNativeElement = element;
            _rotatingNativeLine = null;
            _resizedNativeElement = null;
            _nativeDragStart = e.GetPosition(NativeDesignerCanvas);
            _nativeDragStartLeft = element.Left;
            _nativeDragStartTop = element.Top;
            NativeDesignerCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnNativeLineRotateHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement control || control.Tag is not NativeReportElementViewModel element)
                return;

            SelectNativeElement(element);
            NativeDesignerCanvas.Focus();
            _draggedNativeElement = null;
            _rotatingNativeLine = element;
            _resizedNativeElement = null;
            NativeDesignerCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnNativeResizeHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement control || control.Tag is not NativeReportElementViewModel element)
                return;

            SelectNativeElement(element);
            NativeDesignerCanvas.Focus();
            _draggedNativeElement = null;
            _rotatingNativeLine = null;
            _resizedNativeElement = element;
            _nativeResizeStart = e.GetPosition(NativeDesignerCanvas);
            _nativeResizeStartWidth = element.Width;
            _nativeResizeStartHeight = element.Height;
            NativeDesignerCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnNativeDesignerMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            var current = e.GetPosition(NativeDesignerCanvas);
            if (_resizedNativeElement != null)
            {
                ResizeNativeElementTo(_resizedNativeElement, current);
                return;
            }

            if (_rotatingNativeLine != null)
            {
                RotateNativeLineTo(_rotatingNativeLine, current);
                return;
            }

            if (_draggedNativeElement == null)
                return;

            var nextLeft = _nativeDragStartLeft + (current.X - _nativeDragStart.X) / NativeDesignerScale;
            var nextTop = _nativeDragStartTop + (current.Y - _nativeDragStart.Y) / NativeDesignerScale;
            SetNativeElementPosition(_draggedNativeElement, nextLeft, nextTop);
        }

        private void OnNativeDesignerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            NativeDesignerCanvas.ReleaseMouseCapture();
            _draggedNativeElement = null;
            _rotatingNativeLine = null;
            _resizedNativeElement = null;
            NativeElementsGrid.Items.Refresh();
        }

        private void OnNativeDesignerPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_selectedNativeElement == null || IsNativeEditorInput(e.OriginalSource as DependencyObject))
                return;

            if (e.Key == Key.Delete)
            {
                DeleteSelectedNativeElement();
                e.Handled = true;
                return;
            }

            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? 50d
                : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1d : 10d;

            var handled = e.Key switch
            {
                Key.Left => MoveSelectedNativeElement(-step, 0),
                Key.Right => MoveSelectedNativeElement(step, 0),
                Key.Up => MoveSelectedNativeElement(0, -step),
                Key.Down => MoveSelectedNativeElement(0, step),
                _ => false
            };

            if (handled)
                e.Handled = true;
        }

        private bool MoveSelectedNativeElement(double dx, double dy)
        {
            if (_selectedNativeElement == null)
                return false;

            SetNativeElementPosition(
                _selectedNativeElement,
                _selectedNativeElement.Left + dx,
                _selectedNativeElement.Top + dy);
            return true;
        }

        private void SetNativeElementPosition(NativeReportElementViewModel element, double left, double top)
        {
            var (clampedLeft, clampedTop) = ClampNativeElementPosition(element, left, top);
            element.Left = clampedLeft;
            element.Top = clampedTop;
            FillNativeElementProperties(element);
            RenderNativeDesigner();
        }

        private void ResizeNativeElementTo(NativeReportElementViewModel element, Point canvasPoint)
        {
            var width = _nativeResizeStartWidth + (canvasPoint.X - _nativeResizeStart.X) / NativeDesignerScale;
            var height = _nativeResizeStartHeight + (canvasPoint.Y - _nativeResizeStart.Y) / NativeDesignerScale;
            SetNativeElementSize(element, width, height);
        }

        private void SetNativeElementSize(NativeReportElementViewModel element, double width, double height)
        {
            var (clampedWidth, clampedHeight) = ClampNativeElementSize(element, width, height);
            element.Width = clampedWidth;
            element.Height = clampedHeight;
            ClampNativeElement(element);
            FillNativeElementProperties(element);
            RenderNativeDesigner();
        }

        private void RotateNativeLineTo(NativeReportElementViewModel element, Point canvasPoint)
        {
            var endpointX = canvasPoint.X / NativeDesignerScale;
            var endpointY = canvasPoint.Y / NativeDesignerScale;
            var band = GetNativeBand(element.BandType);
            endpointX = Clamp(endpointX, 0, _nativeTemplate.PageWidth);
            endpointY = Clamp(endpointY, band.Top, band.Top + band.Height);

            element.Width = endpointX - element.Left;
            element.Height = endpointY - element.Top;
            FillNativeElementProperties(element);
            RenderNativeDesigner();
        }

        private (double Left, double Top) ClampNativeElementPosition(
            NativeReportElementViewModel element,
            double left,
            double top)
        {
            var band = GetNativeBand(element.BandType);
            var bandTop = band.Top;
            var bandBottom = band.Top + band.Height;

            var minRelativeX = element.Type == "Line" ? Math.Min(0, element.Width) : 0;
            var maxRelativeX = element.Type == "Line" ? Math.Max(0, element.Width) : Math.Max(1, element.Width);
            var minRelativeY = element.Type == "Line" ? Math.Min(0, element.Height) : 0;
            var maxRelativeY = element.Type == "Line" ? Math.Max(0, element.Height) : Math.Max(1, element.Height);

            var minLeft = -minRelativeX;
            var maxLeft = _nativeTemplate.PageWidth - maxRelativeX;
            var minTop = bandTop - minRelativeY;
            var maxTop = bandBottom - maxRelativeY;

            return (Clamp(left, minLeft, maxLeft), Clamp(top, minTop, maxTop));
        }

        private (double Width, double Height) ClampNativeElementSize(
            NativeReportElementViewModel element,
            double width,
            double height)
        {
            var band = GetNativeBand(element.BandType);
            var maxWidth = Math.Max(1, _nativeTemplate.PageWidth - element.Left);
            var maxHeight = Math.Max(1, band.Top + band.Height - element.Top);
            return (Clamp(width, 10, maxWidth), Clamp(height, 8, maxHeight));
        }

        private void ClampNativeElement(NativeReportElementViewModel element)
        {
            if (element.Type != "Line")
            {
                var (width, height) = ClampNativeElementSize(element, element.Width, element.Height);
                element.Width = width;
                element.Height = height;
            }

            var (left, top) = ClampNativeElementPosition(element, element.Left, element.Top);
            element.Left = left;
            element.Top = top;
        }

        private PrintFormBand GetNativeBand(string bandType)
        {
            var band = _nativeTemplate.Bands.FirstOrDefault(item =>
                item.Type.Equals(bandType, StringComparison.OrdinalIgnoreCase));
            if (band != null)
                return band;

            var fallback = _nativeTemplate.Bands.FirstOrDefault(item =>
                item.Type.Equals("Body", StringComparison.OrdinalIgnoreCase));
            if (fallback != null)
                return fallback;

            return new PrintFormBand { Type = "Body", Top = 0, Height = _nativeTemplate.PageHeight, Order = 0 };
        }

        private static double Clamp(double value, double min, double max)
        {
            if (min > max)
                return min;
            return Math.Min(Math.Max(value, min), max);
        }

        private static bool IsNativeEditorInput(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is TextBoxBase or PasswordBox or ComboBox)
                    return true;
                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private void OnNativeElementGridSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NativeElementsGrid.SelectedItem is NativeReportElementViewModel element)
                SelectNativeElement(element);
        }

        private void SelectNativeElement(NativeReportElementViewModel? element)
        {
            _selectedNativeElement = element;
            if (element != null && !ReferenceEquals(NativeElementsGrid.SelectedItem, element))
                NativeElementsGrid.SelectedItem = element;
            FillNativeElementProperties(element);
            RenderNativeDesigner();
        }

        private void FillNativeElementProperties(NativeReportElementViewModel? element)
        {
            if (element == null)
            {
                NativeTextBox.Text = string.Empty;
                NativeFieldCombo.SelectedValue = null;
                NativeLeftBox.Text = string.Empty;
                NativeTopBox.Text = string.Empty;
                NativeWidthBox.Text = string.Empty;
                NativeHeightBox.Text = string.Empty;
                NativeFontBox.Text = string.Empty;
                NativeFontSizeBox.Text = string.Empty;
                NativeBoldCheck.IsChecked = false;
                NativeItalicCheck.IsChecked = false;
                return;
            }

            NativeTextBox.Text = element.Text;
            NativeFieldCombo.SelectedValue = element.Expression;
            NativeLeftBox.Text = FormatNumber(element.Left);
            NativeTopBox.Text = FormatNumber(element.Top);
            NativeWidthBox.Text = FormatNumber(element.Width);
            NativeHeightBox.Text = FormatNumber(element.Height);
            NativeFontBox.Text = element.FontName;
            NativeFontSizeBox.Text = FormatNumber(element.FontSize);
            NativeAlignmentCombo.SelectedItem = NativeAlignmentCombo.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(item => item.Content?.ToString() == element.Alignment) ?? NativeAlignmentCombo.Items[0];
            NativeBoldCheck.IsChecked = element.Bold;
            NativeItalicCheck.IsChecked = element.Italic;
        }

        private void OnApplyNativeElementPropertiesClick(object sender, RoutedEventArgs e)
        {
            ApplyNativeElementProperties();
        }

        private void OnNativePropertiesPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnterKey(e.Key))
                return;

            ApplyNativeElementProperties();
            NativeDesignerCanvas.Focus();
            e.Handled = true;
        }

        private bool ApplyNativeElementProperties()
        {
            if (_selectedNativeElement == null)
                return false;

            _selectedNativeElement.Text = NativeTextBox.Text;
            _selectedNativeElement.Expression = NativeFieldCombo.SelectedValue?.ToString() ?? string.Empty;
            _selectedNativeElement.Left = ParseNumber(NativeLeftBox.Text, _selectedNativeElement.Left);
            _selectedNativeElement.Top = ParseNumber(NativeTopBox.Text, _selectedNativeElement.Top);
            _selectedNativeElement.Width = Math.Max(1, ParseNumber(NativeWidthBox.Text, _selectedNativeElement.Width));
            _selectedNativeElement.Height = Math.Max(1, ParseNumber(NativeHeightBox.Text, _selectedNativeElement.Height));
            _selectedNativeElement.FontName = string.IsNullOrWhiteSpace(NativeFontBox.Text) ? "Arial" : NativeFontBox.Text;
            _selectedNativeElement.FontSize = Math.Max(1, ParseNumber(NativeFontSizeBox.Text, _selectedNativeElement.FontSize));
            _selectedNativeElement.Alignment = (NativeAlignmentCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
            _selectedNativeElement.Bold = NativeBoldCheck.IsChecked == true;
            _selectedNativeElement.Italic = NativeItalicCheck.IsChecked == true;
            ClampNativeElement(_selectedNativeElement);
            FillNativeElementProperties(_selectedNativeElement);
            RenderNativeDesigner();
            NativeElementsGrid.Items.Refresh();
            return true;
        }

        private void OnNativeGeometrySpinClick(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeElement == null || sender is not Button { Tag: string tag })
                return;

            var parts = tag.Split(':');
            if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var direction))
                return;

            var step = GetNativeGeometryStep() * direction;
            switch (parts[0])
            {
                case "Left":
                    SetNativeElementPosition(_selectedNativeElement, _selectedNativeElement.Left + step, _selectedNativeElement.Top);
                    break;
                case "Top":
                    SetNativeElementPosition(_selectedNativeElement, _selectedNativeElement.Left, _selectedNativeElement.Top + step);
                    break;
                case "Width":
                    SetNativeElementSize(_selectedNativeElement, _selectedNativeElement.Width + step, _selectedNativeElement.Height);
                    break;
                case "Height":
                    SetNativeElementSize(_selectedNativeElement, _selectedNativeElement.Width, _selectedNativeElement.Height + step);
                    break;
            }

            NativeElementsGrid.Items.Refresh();
            NativeDesignerCanvas.Focus();
            e.Handled = true;
        }

        private static double GetNativeGeometryStep()
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                return 100;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                return 10;
            return 1;
        }

        private void OnAddNativeElementClick(object sender, RoutedEventArgs e)
        {
            AddNativeElement("Text", "Новый элемент", string.Empty, 500, 70);
        }

        private void OnAddNativeTextClick(object sender, RoutedEventArgs e)
        {
            AddNativeElement("Text", "Новый текст", string.Empty, 500, 70);
        }

        private void OnAddNativeFieldClick(object sender, RoutedEventArgs e)
        {
            var selectedField = NativeFieldCombo.SelectedItem as FieldDef
                ?? AvailableFields.SelectedItem as FieldDef
                ?? AvailableDataFields.FirstOrDefault();
            AddNativeElement("Expression", string.Empty, selectedField?.DbColumnName ?? "amount", 520, 70);
        }

        private void OnAddNativeLineClick(object sender, RoutedEventArgs e)
        {
            AddNativeElement("Line", string.Empty, string.Empty, 450, 0, 320, 0);
        }

        private void OnAddNativeBoxClick(object sender, RoutedEventArgs e)
        {
            AddNativeElement("Box", string.Empty, string.Empty, 450, 0, 420, 180);
        }

        private void AddNativeElement(
            string type,
            string text,
            string expression,
            double width,
            double height,
            double? customWidth = null,
            double? customHeight = null)
        {
            var band = (NativeBandCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Body";
            var top = _nativeTemplate.Bands.FirstOrDefault(item => item.Type == band)?.Top ?? 260;
            var element = new NativeReportElementViewModel
            {
                Type = type,
                Text = text,
                Expression = expression,
                BandType = band,
                Left = 120,
                Top = top + 50,
                Width = customWidth ?? width,
                Height = customHeight ?? height,
                FontName = "Arial",
                FontSize = 10,
                Alignment = "Left",
                Order = GetNextNativeElementOrder()
            };
            _nativeElements.Add(element);
            SelectNativeElement(element);
        }

        private void OnNewBlankNativeTemplateClick(object sender, RoutedEventArgs e)
        {
            LoadNativeTemplate(PrintFormService.CreateBlankNativeTemplate());
            SyncNativeTemplateToTemplateBoxIfNeeded();
        }

        private void OnNativeElementCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is NativeReportElementViewModel element)
                {
                    ClampNativeElement(element);
                    if (ReferenceEquals(element, _selectedNativeElement))
                        FillNativeElementProperties(element);
                }

                RenderNativeDesigner();
            }));
        }

        private void OnDeleteNativeElementClick(object sender, RoutedEventArgs e)
        {
            DeleteSelectedNativeElement();
        }

        private void DeleteSelectedNativeElement()
        {
            if (_selectedNativeElement == null)
                return;

            _nativeElements.Remove(_selectedNativeElement);
            ReorderNativeElements();
            SelectNativeElement(_nativeElements.FirstOrDefault());
        }

        private void ReorderNativeElements()
        {
            var order = 0;
            foreach (var element in _nativeElements)
                element.Order = order++;
            NativeElementsGrid.Items.Refresh();
        }

        private int GetNextNativeElementOrder() =>
            _nativeElements.Count == 0 ? 0 : _nativeElements.Max(item => item.Order) + 1;

        private static string FormatNumber(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariantValue))
                return invariantValue;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentValue))
                return currentValue;
            return fallback;
        }

        private static bool IsEnterKey(Key key) => key is Key.Enter or Key.Return;
    }

    public class NativeReportElementViewModel : INotifyPropertyChanged
    {
        private string _type = "Text";
        private string _text = string.Empty;
        private string _expression = string.Empty;
        private string _bandType = "Body";
        private double _left;
        private double _top;
        private double _width = 500;
        private double _height = 70;
        private string _fontName = "Arial";
        private double _fontSize = 10;
        private bool _bold;
        private bool _italic;
        private string _alignment = "Left";
        private int _order;

        public string Type { get => _type; set { _type = value; OnChanged(nameof(Type)); OnChanged(nameof(DisplayText)); } }
        public string Text { get => _text; set { _text = value; OnChanged(nameof(Text)); OnChanged(nameof(DisplayText)); } }
        public string Expression { get => _expression; set { _expression = value; OnChanged(nameof(Expression)); OnChanged(nameof(DisplayText)); } }
        public string BandType { get => _bandType; set { _bandType = value; OnChanged(nameof(BandType)); } }
        public double Left { get => _left; set { _left = value; OnChanged(nameof(Left)); } }
        public double Top { get => _top; set { _top = value; OnChanged(nameof(Top)); } }
        public double Width { get => _width; set { _width = value; OnChanged(nameof(Width)); } }
        public double Height { get => _height; set { _height = value; OnChanged(nameof(Height)); } }
        public string FontName { get => _fontName; set { _fontName = value; OnChanged(nameof(FontName)); } }
        public double FontSize { get => _fontSize; set { _fontSize = value; OnChanged(nameof(FontSize)); } }
        public bool Bold { get => _bold; set { _bold = value; OnChanged(nameof(Bold)); } }
        public bool Italic { get => _italic; set { _italic = value; OnChanged(nameof(Italic)); } }
        public string Alignment { get => _alignment; set { _alignment = value; OnChanged(nameof(Alignment)); } }
        public int Order { get => _order; set { _order = value; OnChanged(nameof(Order)); } }
        public string BorderStyle { get; set; } = "None";
        public string DisplayText => Type switch
        {
            "Expression" => string.IsNullOrWhiteSpace(Expression) ? "{поле}" : $"{{{Expression}}}",
            "Line" => "Линия",
            "Box" => "Рамка",
            _ => string.IsNullOrWhiteSpace(Text) ? "Текст" : Text
        };

        public static NativeReportElementViewModel FromElement(PrintFormElement element) => new()
        {
            Type = element.Type,
            Text = element.Text,
            Expression = element.Expression,
            BandType = element.BandType,
            Left = element.Left,
            Top = element.Top,
            Width = element.Width,
            Height = element.Height,
            FontName = element.FontName,
            FontSize = element.FontSize,
            Bold = element.Bold,
            Italic = element.Italic,
            Alignment = string.IsNullOrWhiteSpace(element.Alignment) ? "Left" : element.Alignment,
            BorderStyle = element.BorderStyle,
            Order = element.Order
        };

        public PrintFormElement ToElement() => new()
        {
            Type = Type,
            Text = Text,
            Expression = Expression,
            BandType = BandType,
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height,
            FontName = FontName,
            FontSize = FontSize,
            Bold = Bold,
            Italic = Italic,
            Alignment = Alignment,
            BorderStyle = Type == "Box" ? "Solid" : BorderStyle,
            Order = Order
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
