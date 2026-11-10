using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BIS.ERP.Services;

namespace BIS.ERP.Views.Controls;

public sealed class MdiDialogHostControl : UserControl
{
    private readonly Action? _closeRequested;
    private readonly Action? _documentClosed;
    private bool _isClosing;

    public MdiDialogHostControl(
        Window sourceWindow,
        Action? closeRequested = null,
        Action? documentClosed = null,
        bool fillWorkspace = false)
    {
        _closeRequested = closeRequested;
        _documentClosed = documentClosed;
        Content = BuildContent(sourceWindow, fillWorkspace);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isClosing)
                sourceWindow.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, sourceWindow));
        }), DispatcherPriority.Loaded);
    }

    public void NotifyDocumentClosed()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        _documentClosed?.Invoke();
    }

    private UIElement BuildContent(Window sourceWindow, bool fillWorkspace)
    {
        var windowContent = sourceWindow.Content;
        sourceWindow.Content = null;

        UIElement body = windowContent as UIElement ?? new TextBlock
        {
            Text = windowContent?.ToString() ?? string.Empty,
            TextWrapping = TextWrapping.Wrap
        };

        PreserveWindowDataContext(sourceWindow, body);

        HookCloseButtons(body);
        sourceWindow.Closed += (_, _) => RequestClose();

        // Окно, объявленное maximized в XAML, должно занимать всё пространство
        // вкладки: в MDI-режиме свойства WindowState/WindowStartupLocation сами
        // по себе не действуют, поэтому учитываем их здесь явно.
        if (fillWorkspace || sourceWindow.WindowState == WindowState.Maximized)
            return BuildFullWorkspaceContent(sourceWindow, body, ManagesOwnScroll(sourceWindow));

        var targetWidth = NormalizeDimension(sourceWindow.Width, 760, 1500);
        var targetHeight = NormalizeDimension(sourceWindow.Height, 420, 920);

        var card = new Border
        {
            MaxWidth = targetWidth,
            MaxHeight = targetHeight,
            MinWidth = Math.Min(520, targetWidth),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(196, 214, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.16,
                Color = Color.FromRgb(35, 56, 77)
            },
            Child = Scrollable(body, ManagesOwnScroll(sourceWindow))
        };

        ApplyDialogBackground(card, sourceWindow);

        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 253))
        };
        root.Children.Add(card);
        return root;
    }

    private static void PreserveWindowDataContext(Window sourceWindow, UIElement body)
    {
        if (sourceWindow.DataContext == null || body is not FrameworkElement element)
            return;

        var localDataContext = element.ReadLocalValue(FrameworkElement.DataContextProperty);
        if (localDataContext == DependencyProperty.UnsetValue || element.DataContext == null)
            element.DataContext = sourceWindow.DataContext;
    }

    private static UIElement BuildFullWorkspaceContent(
        Window sourceWindow, UIElement body, bool managesOwnScroll)
    {
        if (body is FrameworkElement element)
        {
            element.Width = double.NaN;
            element.Height = double.NaN;
            element.MinWidth = 0;
            element.MinHeight = 0;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = VerticalAlignment.Stretch;
        }

        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 253))
        };

        var panel = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(214, 226, 238)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = Scrollable(body, managesOwnScroll)
        };

        ApplyDialogBackground(panel, sourceWindow);

        root.Children.Add(panel);
        return root;
    }

    /// <summary>
    /// Подложка под содержимым окна: если диалог сам задал фон окна (например, документы ТМЗ
    /// с тёмной темой), красим рамку вкладки тем же фоном — иначе вокруг диалога
    /// остаётся белая кайма и гамма выглядит развалившейся.
    /// </summary>
    private static void ApplyDialogBackground(Border panel, Window sourceWindow)
    {
        if (Equals(sourceWindow.ReadLocalValue(Window.BackgroundProperty), DependencyProperty.UnsetValue))
            return;

        panel.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Window.Background))
        {
            Source = sourceWindow,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
    }

    /// <summary>
    /// Оборачивает содержимое окна в ScrollViewer — кроме окон, которые сами управляют
    /// прокруткой (таблица с закреплёнными поиском и кнопками). Внешний ScrollViewer
    /// в этом случае только мешает: он даёт содержимому бесконечную высоту, таблица
    /// растёт на все строки и прокручивается вместе с поиском.
    /// </summary>
    private static UIElement Scrollable(UIElement body, bool managesOwnScroll)
    {
        if (managesOwnScroll)
            return body;

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = body
        };
    }

    private static bool ManagesOwnScroll(Window sourceWindow)
    {
        return sourceWindow.Tag is string tag &&
               string.Equals(tag, MdiDialogService.ManageOwnScrollTag, StringComparison.OrdinalIgnoreCase);
    }

    private static double NormalizeDimension(double value, double fallback, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;

        return Math.Min(value, max);
    }

    private void HookCloseButtons(DependencyObject root)
    {
        if (root is Button button && IsCloseButton(button))
        {
            button.Click += (_, _) => Dispatcher.BeginInvoke(RequestClose, DispatcherPriority.Background);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            HookCloseButtons(VisualTreeHelper.GetChild(root, index));
        }
    }

    private static bool IsCloseButton(Button button)
    {
        var text = button.Content switch
        {
            string value => value,
            TextBlock textBlock => textBlock.Text,
            _ => button.Content?.ToString() ?? string.Empty
        };

        text = text.Trim();
        return text.Equals("Закрыть", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Отмена", StringComparison.OrdinalIgnoreCase);
    }

    private void RequestClose()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        _closeRequested?.Invoke();
    }
}