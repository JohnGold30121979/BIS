using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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

        HookCloseButtons(body);
        sourceWindow.Closed += (_, _) => RequestClose();

        if (fillWorkspace)
            return BuildFullWorkspaceContent(body);

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
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            }
        };

        var root = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(247, 250, 253))
        };
        root.Children.Add(card);
        return root;
    }

    private static UIElement BuildFullWorkspaceContent(UIElement body)
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
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            }
        };

        root.Children.Add(panel);
        return root;
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