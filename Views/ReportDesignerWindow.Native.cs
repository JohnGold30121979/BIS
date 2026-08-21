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
        private const string NativeDesignerPageAreaBandType = "__DesignerPageArea";
        private double _nativeZoomMultiplier = 1.0;
        private double NativeDesignerScale => NativeDesignerBaseScale * _nativeZoomMultiplier;
        private readonly ObservableCollection<NativeReportElementViewModel> _nativeElements = new();
        private PrintFormTemplate _nativeTemplate = PrintFormService.CreateBlankNativeTemplate();
        private NativeReportElementViewModel? _selectedNativeElement;
        private bool _suspendNativePropertyUpdates;
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
            FrXFieldsTab.AddHandler(Selector.SelectedEvent, new RoutedEventHandler(OnFrxFieldsTabSelected));
            AttachNativePropertyChangeHandlers();
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


        private async void OnReportTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            // Тип отчета не должен менять визуальный макет автоматически:
            // дизайнер является универсальным движком компоновки.
            if (DataSourceCombo?.SelectedItem is ComboBoxItem selected && selected.Tag is MetadataObject catalog)
            {
                await LoadAvailableFields(catalog);
                RefreshFrxMappingsForCurrentSource(clearMissingFields: GetSelectedReportType() == "FoxProLayout");
            }
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

        private void OnFrxFieldsTabSelected(object sender, RoutedEventArgs e)
        {
            CommitDesignerGridEdits();
            SyncNativeTemplateToTemplateBoxIfNeeded();
            SyncFrxElementMappingsFromNativeTemplate();
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
            EnsureNativeTemplateBands();

            _suspendNativeRender = true;
            NativeElementsGrid.ItemsSource = null;
            try
            {
                _nativeElements.Clear();
                foreach (var element in _nativeTemplate.Elements.OrderBy(item => item.Order))
                {
                    var viewModel = NativeReportElementViewModel.FromElement(element);
                    EnsureNativeElementBand(viewModel);
                    _nativeElements.Add(viewModel);
                }
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
            if (!ShouldSyncNativeTemplate())
                return;
            if (!string.IsNullOrWhiteSpace(_deferredNativeTemplateJson))
                return;

            _nativeTemplate.SourceFormat = GetSelectedReportType() == "FoxProLayout"
                ? "FoxProFRX"
                : "Native";
            EnsureNativeTemplateBands();
            foreach (var element in _nativeElements)
                EnsureNativeElementBand(element);
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

        private bool ShouldSyncNativeTemplate() =>
            IsPrintFormCheck.IsChecked == true ||
            string.Equals(GetSelectedReportType(), "FoxProLayout", StringComparison.OrdinalIgnoreCase);

        private void SyncFrxElementMappingsFromNativeTemplate()
        {
            if (!string.Equals(GetSelectedReportType(), "FoxProLayout", StringComparison.OrdinalIgnoreCase) &&
                _frxElementMappings.Count == 0)
                return;

            if (_nativeElements.Count == 0)
            {
                FrXFieldsTab.IsEnabled = true;
                UpdateMappingPreview();
                return;
            }

            var savedMappings = _frxElementMappings.ToList();
            var savedByOrder = savedMappings
                .GroupBy(item => item.ElementOrder)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First());
            var savedBySource = savedMappings
                .GroupBy(GetMappingIdentity, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First(), StringComparer.OrdinalIgnoreCase);

            ElementMappingGrid.ItemsSource = null;
            try
            {
                _frxElementMappings.Clear();
                var order = 0;
                foreach (var element in _nativeElements.OrderBy(item => item.Order))
                {
                    if (!IsMappableNativeElement(element))
                        continue;

                    var elementText = GetNativeMappingText(element);
                    var sourceKey = GetMappingIdentity(element.Type, elementText, element.Expression);
                    savedByOrder.TryGetValue(element.Order, out var savedByExactOrder);
                    savedBySource.TryGetValue(sourceKey, out var savedBySameSource);
                    var saved = savedByExactOrder ?? savedBySameSource;

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

            ApplyFoxProRulesToEmptyMappings();
            UpdateMappingPreview();
            FrXFieldsTab.IsEnabled = true;
        }

        private static bool IsMappableNativeElement(NativeReportElementViewModel element)
        {
            if (element.Type is "Line" or "Box" or "Picture")
                return false;

            var text = GetNativeMappingText(element);
            return !string.IsNullOrWhiteSpace(text) && text != "+" && text != "-";
        }

        private static string GetNativeMappingText(NativeReportElementViewModel element) =>
            string.IsNullOrWhiteSpace(element.Expression)
                ? PrintFormService.CleanFoxText(element.Text)
                : element.Expression;

        private static string GetMappingIdentity(FrXElementMappingViewModel mapping) =>
            GetMappingIdentity(mapping.ElementType, mapping.ElementText, mapping.ElementExpression);

        private static string GetMappingIdentity(string elementType, string elementText, string elementExpression) =>
            $"{elementType}|{(string.IsNullOrWhiteSpace(elementExpression) ? elementText : elementExpression)}".Trim().ToUpperInvariant();
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
            EnsureNativeTemplateBands();
            foreach (var element in _nativeElements)
                EnsureNativeElementBand(element);

            NativeDesignerCanvas.Width = Math.Max(600, _nativeTemplate.PageWidth * NativeDesignerScale);
            NativeDesignerCanvas.Height = Math.Max(800, GetNativeDesignerPageHeight() * NativeDesignerScale);

            RenderNativeBands();
            foreach (var element in _nativeElements.OrderBy(item => item.Order))
                RenderNativeElement(element);
        }

        private void RenderNativeBands()
        {
            foreach (var band in GetDesignerNativeBands())
            {
                var isPageArea = IsDesignerNativePageAreaBand(band);
                var border = new Border
                {
                    BorderBrush = isPageArea ? Brushes.Gainsboro : Brushes.LightSteelBlue,
                    BorderThickness = new Thickness(0, 0, 0, isPageArea ? 0.5 : 1),
                    Background = isPageArea
                        ? new SolidColorBrush(Color.FromArgb(10, 180, 190, 200))
                        : new SolidColorBrush(Color.FromArgb(24, 52, 152, 219)),
                    Width = NativeDesignerCanvas.Width,
                    Height = Math.Max(18, band.Height * NativeDesignerScale),
                    Child = new TextBlock
                    {
                        Text = isPageArea ? "Свободная область страницы" : band.Type,
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
            var band = GetNativeLayoutBand(element, element.Top);
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
            var band = GetNativeLayoutBand(element, top);
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
            var band = GetNativeLayoutBand(element, element.Top);
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

        private void EnsureNativeTemplateBands()
        {
            _nativeTemplate.Bands ??= new List<PrintFormBand>();
            _nativeTemplate.Elements ??= new List<PrintFormElement>();
            if (_nativeTemplate.PageWidth <= 0)
                _nativeTemplate.PageWidth = 2100;
            if (_nativeTemplate.PageHeight <= 0)
                _nativeTemplate.PageHeight = 2970;

            if (_nativeTemplate.Bands.Count == 0)
            {
                var top = _nativeTemplate.Elements.Count == 0 ? 0 : _nativeTemplate.Elements.Min(element => element.Top);
                var bottom = _nativeTemplate.Elements.Count == 0
                    ? _nativeTemplate.PageHeight
                    : _nativeTemplate.Elements.Max(element => element.Top + Math.Max(element.Height, 1));
                _nativeTemplate.Bands = new List<PrintFormBand>
                {
                    new() { Type = "Detail", Top = top, Height = Math.Max(1, bottom - top), Order = 0 }
                };
            }

            foreach (var band in _nativeTemplate.Bands)
            {
                if (string.IsNullOrWhiteSpace(band.Type))
                    band.Type = band.Order switch
                    {
                        0 => "Header",
                        1 => "Body",
                        _ => "Footer"
                    };
                if (band.Height <= 0)
                    band.Height = 40;
            }
        }

        private IReadOnlyList<PrintFormBand> GetEffectiveNativeBands()
        {
            EnsureNativeTemplateBands();

            var bands = _nativeTemplate.Bands
                .OrderBy(band => band.Top)
                .ThenBy(band => band.Order)
                .Select(CloneNativeBand)
                .ToList();
            if (bands.Count == 0)
                return Array.Empty<PrintFormBand>();

            if (bands.All(band => Math.Abs(band.Top) < 0.001))
            {
                double top = 0;
                foreach (var band in bands.OrderBy(band => band.Order))
                {
                    band.Top = top;
                    top += Math.Max(1, band.Height);
                }
            }

            return bands.OrderBy(band => band.Top).ThenBy(band => band.Order).ToList();
        }

        private IReadOnlyList<PrintFormBand> GetDesignerNativeBands()
        {
            var sourceBands = GetEffectiveNativeBands();
            var pageHeight = GetNativeDesignerPageHeight();
            var result = new List<PrintFormBand>();
            var cursor = 0d;
            var gapOrder = -100000;

            foreach (var band in sourceBands.OrderBy(item => item.Top).ThenBy(item => item.Order))
            {
                var bandTop = Math.Max(0, band.Top);
                if (bandTop > cursor + 2)
                    AddDesignerNativeGapBand(result, cursor, bandTop - cursor, gapOrder++);

                result.Add(band);
                cursor = Math.Max(cursor, bandTop + Math.Max(band.Height, 1));
            }

            if (pageHeight > cursor + 2)
                AddDesignerNativeGapBand(result, cursor, pageHeight - cursor, gapOrder++);

            if (result.Count == 0)
                AddDesignerNativeGapBand(result, 0, pageHeight, gapOrder);

            return result.OrderBy(band => band.Top).ThenBy(band => band.Order).ToList();
        }

        private static void AddDesignerNativeGapBand(List<PrintFormBand> bands, double top, double height, int order)
        {
            bands.Add(new PrintFormBand
            {
                Type = NativeDesignerPageAreaBandType,
                Top = Math.Max(0, top),
                Height = Math.Max(1, height),
                Order = order
            });
        }

        private double GetNativeDesignerPageHeight()
        {
            var pageHeight = Math.Max(1000, _nativeTemplate.PageHeight);
            if (_nativeTemplate.Bands.Count > 0)
                pageHeight = Math.Max(pageHeight, _nativeTemplate.Bands.Max(band => band.Top + Math.Max(band.Height, 1)));
            if (_nativeTemplate.Elements.Count > 0)
                pageHeight = Math.Max(pageHeight, _nativeTemplate.Elements.Max(element => element.Top + Math.Max(element.Height, 1)) + 80);
            if (_nativeElements.Count > 0)
                pageHeight = Math.Max(pageHeight, _nativeElements.Max(element => element.Top + Math.Max(element.Height, 1)) + 80);

            return pageHeight;
        }

        private PrintFormBand GetNativeLayoutBand(NativeReportElementViewModel element, double top) =>
            FindNativeBandForElement(top, element.BandType, GetDesignerNativeBands());

        private static bool IsDesignerNativePageAreaBand(PrintFormBand band) =>
            string.Equals(band.Type, NativeDesignerPageAreaBandType, StringComparison.OrdinalIgnoreCase);
        private void EnsureNativeElementBand(NativeReportElementViewModel element)
        {
            var exactBand = FindNativeBandByExactType(element.BandType, _nativeTemplate.Bands);
            if (exactBand != null)
                return;

            var band = GetNativeBand(element);
            if (!string.Equals(element.BandType, band.Type, StringComparison.OrdinalIgnoreCase))
                element.BandType = band.Type;
        }

        private PrintFormBand GetNativeBand(NativeReportElementViewModel element) =>
            FindNativeBandForElement(element.Top, element.BandType, GetEffectiveNativeBands());

        private PrintFormBand GetNativeBand(string? bandType, double? elementTop = null)
        {
            var bands = GetEffectiveNativeBands();
            if (elementTop.HasValue)
                return FindNativeBandForElement(elementTop.Value, bandType, bands);

            return FindNativeBandByExactType(bandType, bands)
                ?? FindNativeBandByAlias(bandType, bands)
                ?? FindNativeBandByExactType("Body", bands)
                ?? FindNativeBandByExactType("Detail", bands)
                ?? bands.FirstOrDefault()
                ?? new PrintFormBand { Type = "Body", Top = 0, Height = _nativeTemplate.PageHeight, Order = 0 };
        }

        private PrintFormBand FindNativeBandForElement(double top, string? bandType, IReadOnlyList<PrintFormBand> bands)
        {
            const double tolerance = 2d;
            if (bands.Count == 0)
                return new PrintFormBand { Type = "Body", Top = 0, Height = _nativeTemplate.PageHeight, Order = 0 };

            var byCoordinate = bands
                .Where(band => top >= band.Top - tolerance && top <= band.Top + Math.Max(band.Height, 1) + tolerance)
                .OrderBy(band => Math.Abs(top - band.Top))
                .FirstOrDefault();
            if (byCoordinate != null)
                return byCoordinate;

            var previous = bands.LastOrDefault(band => top >= band.Top - tolerance);
            if (previous != null)
                return previous;

            return FindNativeBandByExactType(bandType, bands)
                ?? FindNativeBandByAlias(bandType, bands)
                ?? bands[0];
        }

        private PrintFormBand? FindNativeBandByExactType(string? bandType, IEnumerable<PrintFormBand>? bands = null)
        {
            if (string.IsNullOrWhiteSpace(bandType))
                return null;

            return (bands ?? _nativeTemplate.Bands).FirstOrDefault(item =>
                item.Type.Equals(bandType, StringComparison.OrdinalIgnoreCase));
        }

        private PrintFormBand? FindNativeBandByAlias(string? bandType, IEnumerable<PrintFormBand>? bands = null)
        {
            var aliases = GetNativeBandAliases(bandType);
            foreach (var alias in aliases)
            {
                var band = FindNativeBandByExactType(alias, bands);
                if (band != null)
                    return band;
            }

            return null;
        }

        private static IEnumerable<string> GetNativeBandAliases(string? bandType)
        {
            var normalized = NormalizeNativeBandType(bandType);
            return normalized switch
            {
                "Header" => new[] { "Header", "PageHeader", "Title", "ReportHeader" },
                "Body" => new[] { "Body", "Detail", "GroupHeader", "GroupFooter", "ColumnHeader", "ColumnFooter" },
                "Footer" => new[] { "Footer", "Summary", "PageFooter", "ReportFooter" },
                _ => Array.Empty<string>()
            };
        }

        private static string NormalizeNativeBandType(string? bandType)
        {
            var value = (bandType ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0)
                return string.Empty;
            if (value.Contains("PAGEHEADER") || value is "HEADER" or "TITLE" or "REPORTHEADER")
                return "Header";
            if (value.Contains("PAGEFOOTER") || value.Contains("SUMMARY") || value is "FOOTER" or "REPORTFOOTER")
                return "Footer";
            if (value.Contains("DETAIL") || value.Contains("GROUP") || value.Contains("COLUMN") || value is "BODY")
                return "Body";
            return string.Empty;
        }

        private static PrintFormBand CloneNativeBand(PrintFormBand band) => new()
        {
            Type = band.Type,
            Top = band.Top,
            Height = band.Height,
            Order = band.Order
        };

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
            _suspendNativePropertyUpdates = true;
            try
            {
                if (element == null)
                {
                    NativeTextBox.Text = string.Empty;
                    NativeFieldCombo.SelectedValue = null;
                    NativeExpressionBox.Text = string.Empty;
                    NativeSubstringFieldCombo.SelectedValue = null;
                    NativeSubstringStartBox.Text = "1";
                    NativeSubstringLengthBox.Text = "1";
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
                NativeExpressionBox.Text = element.Expression;
                FillNativeSubstringControls(element.Expression);
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
            finally
            {
                _suspendNativePropertyUpdates = false;
            }
        }

        private void AttachNativePropertyChangeHandlers()
        {
            NativeTextBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeFieldCombo.SelectionChanged += (_, _) => OnNativeFieldComboSelectionChanged();
            NativeExpressionBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeLeftBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeTopBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeWidthBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeHeightBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeFontBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeFontSizeBox.TextChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeAlignmentCombo.SelectionChanged += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeBoldCheck.Checked += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeBoldCheck.Unchecked += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeItalicCheck.Checked += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
            NativeItalicCheck.Unchecked += (_, _) => UpdateSelectedNativeElementFromPropertyControls();
        }

        private void UpdateSelectedNativeElementFromPropertyControls()
        {
            if (_suspendNativePropertyUpdates || _selectedNativeElement == null)
                return;

            _selectedNativeElement.Text = NativeTextBox.Text;
            _selectedNativeElement.Expression = GetNativeExpressionFromPropertyControls();
            _selectedNativeElement.Left = ParseNumber(NativeLeftBox.Text, _selectedNativeElement.Left);
            _selectedNativeElement.Top = ParseNumber(NativeTopBox.Text, _selectedNativeElement.Top);
            _selectedNativeElement.Width = Math.Max(1, ParseNumber(NativeWidthBox.Text, _selectedNativeElement.Width));
            _selectedNativeElement.Height = Math.Max(1, ParseNumber(NativeHeightBox.Text, _selectedNativeElement.Height));
            _selectedNativeElement.FontName = string.IsNullOrWhiteSpace(NativeFontBox.Text) ? "Arial" : NativeFontBox.Text;
            _selectedNativeElement.FontSize = Math.Max(1, ParseNumber(NativeFontSizeBox.Text, _selectedNativeElement.FontSize));
            _selectedNativeElement.Alignment = (NativeAlignmentCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
            _selectedNativeElement.Bold = NativeBoldCheck.IsChecked == true;
            _selectedNativeElement.Italic = NativeItalicCheck.IsChecked == true;
            EnsureNativeElementBand(_selectedNativeElement);
            NativeElementsGrid.Items.Refresh();
        }

        private string GetNativeExpressionFromPropertyControls()
        {
            var expressionText = NativeExpressionBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expressionText))
                return expressionText;

            return NativeFieldCombo.SelectedValue?.ToString() ?? string.Empty;
        }

        private string BuildNativeSubstringExpression(string fieldName)
        {
            var sourceField = string.IsNullOrWhiteSpace(fieldName) ? "amount" : fieldName.Trim();
            var start = ParsePositiveInt(NativeSubstringStartBox.Text, 1);
            var length = ParsePositiveInt(NativeSubstringLengthBox.Text, 1);
            NativeSubstringStartBox.Text = start.ToString(CultureInfo.InvariantCulture);
            NativeSubstringLengthBox.Text = length.ToString(CultureInfo.InvariantCulture);
            return $"SUBSTR(ALLTRIM({sourceField}),{start},{length})";
        }

        private void FillNativeSubstringControls(string expression)
        {
            var parsed = ParseNativeSubstringExpression(expression);
            if (parsed != null)
            {
                NativeSubstringFieldCombo.SelectedValue = parsed.Value.FieldName;
                NativeSubstringStartBox.Text = parsed.Value.Start.ToString(CultureInfo.InvariantCulture);
                NativeSubstringLengthBox.Text = parsed.Value.Length.ToString(CultureInfo.InvariantCulture);
                return;
            }

            NativeSubstringFieldCombo.SelectedValue = NativeFieldCombo.SelectedValue;
            NativeSubstringStartBox.Text = "1";
            NativeSubstringLengthBox.Text = "1";
        }

        private static (string FieldName, int Start, int Length)? ParseNativeSubstringExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                expression.Trim(),
                @"(?i)^SUBSTR\(\s*(?:ALLTRIM\(|ALLTR\(|TRIM\()?\s*([^,\)]+)\s*\)?\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$");
            if (!match.Success)
                return null;

            return (
                match.Groups[1].Value.Trim(),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
        }

        private static int ParsePositiveInt(string textValue, int fallback)
        {
            return int.TryParse(textValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;
        }
        private void OnNativeFieldComboSelectionChanged()
        {
            if (_suspendNativePropertyUpdates || _selectedNativeElement == null)
                return;

            var selectedField = NativeFieldCombo.SelectedValue?.ToString();
            if (!string.IsNullOrWhiteSpace(selectedField) &&
                ShouldReplaceNativeExpressionFromField(_selectedNativeElement.Expression, NativeExpressionBox.Text))
            {
                NativeExpressionBox.Text = selectedField;
            }

            UpdateSelectedNativeElementFromPropertyControls();
        }

        private static bool ShouldReplaceNativeExpressionFromField(string? currentExpression, string? propertyExpression)
        {
            var expression = string.IsNullOrWhiteSpace(propertyExpression)
                ? currentExpression
                : propertyExpression;

            if (string.IsNullOrWhiteSpace(expression))
                return true;

            return expression.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.');
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
            _selectedNativeElement.Expression = GetNativeExpressionFromPropertyControls();
            _selectedNativeElement.Left = ParseNumber(NativeLeftBox.Text, _selectedNativeElement.Left);
            _selectedNativeElement.Top = ParseNumber(NativeTopBox.Text, _selectedNativeElement.Top);
            _selectedNativeElement.Width = Math.Max(1, ParseNumber(NativeWidthBox.Text, _selectedNativeElement.Width));
            _selectedNativeElement.Height = Math.Max(1, ParseNumber(NativeHeightBox.Text, _selectedNativeElement.Height));
            _selectedNativeElement.FontName = string.IsNullOrWhiteSpace(NativeFontBox.Text) ? "Arial" : NativeFontBox.Text;
            _selectedNativeElement.FontSize = Math.Max(1, ParseNumber(NativeFontSizeBox.Text, _selectedNativeElement.FontSize));
            _selectedNativeElement.Alignment = (NativeAlignmentCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
            _selectedNativeElement.Bold = NativeBoldCheck.IsChecked == true;
            _selectedNativeElement.Italic = NativeItalicCheck.IsChecked == true;
            EnsureNativeElementBand(_selectedNativeElement);
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

        private void OnAddNativeSubstringClick(object sender, RoutedEventArgs e)
        {
            var selectedField = NativeSubstringFieldCombo.SelectedItem as FieldDef
                ?? NativeFieldCombo.SelectedItem as FieldDef
                ?? AvailableFields.SelectedItem as FieldDef
                ?? AvailableDataFields.FirstOrDefault();

            var expression = BuildNativeSubstringExpression(selectedField?.DbColumnName ?? "amount");
            AddNativeElement("SUBSTR", string.Empty, expression, 260, 70);
        }

        private void OnBuildNativeSubstringClick(object sender, RoutedEventArgs e)
        {
            if (_selectedNativeElement == null)
                return;

            var selectedField = NativeSubstringFieldCombo.SelectedItem as FieldDef
                ?? NativeFieldCombo.SelectedItem as FieldDef
                ?? AvailableDataFields.FirstOrDefault(field =>
                    string.Equals(field.DbColumnName, NativeFieldCombo.SelectedValue?.ToString(), StringComparison.OrdinalIgnoreCase))
                ?? AvailableDataFields.FirstOrDefault();

            _selectedNativeElement.Type = "SUBSTR";
            _selectedNativeElement.Text = string.Empty;
            _selectedNativeElement.Expression = BuildNativeSubstringExpression(selectedField?.DbColumnName ?? _selectedNativeElement.Expression);
            FillNativeElementProperties(_selectedNativeElement);
            RenderNativeDesigner();
            NativeElementsGrid.Items.Refresh();
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
            var selectedBand = (NativeBandCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Body";
            var band = GetNativeBand(selectedBand);
            var top = band.Top;
            var element = new NativeReportElementViewModel
            {
                Type = type,
                Text = text,
                Expression = expression,
                BandType = band.Type,
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
            SyncFrxElementMappingsFromNativeTemplate();
        }

        private void OnNativeElementCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is NativeReportElementViewModel element)
                {
                    EnsureNativeElementBand(element);
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
            "Expression" or "SUBSTR" => string.IsNullOrWhiteSpace(Expression) ? "{поле}" : $"{{{Expression}}}",
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
            Type = Type == "SUBSTR" ? "Expression" : Type,
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
