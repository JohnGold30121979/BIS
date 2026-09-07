using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BIS.ERP.Behaviors
{
    public static class MouseWheelScrollBehavior
    {
        private static bool _isInitialized;

        public static void Initialize()
        {
            if (_isInitialized)
                return;

            EventManager.RegisterClassHandler(
                typeof(ScrollViewer),
                UIElement.PreviewMouseWheelEvent,
                new MouseWheelEventHandler(OnPreviewMouseWheel),
                true);

            _isInitialized = true;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer || e.OriginalSource is not DependencyObject source)
                return;

            var targetScrollViewer = FindScrollableScrollViewer(source, e.Delta);
            if (!ReferenceEquals(targetScrollViewer, scrollViewer))
                return;

            var nextOffset = scrollViewer.VerticalOffset - e.Delta;
            scrollViewer.ScrollToVerticalOffset(Clamp(nextOffset, 0, scrollViewer.ScrollableHeight));
            e.Handled = true;
        }

        private static ScrollViewer? FindScrollableScrollViewer(DependencyObject source, int delta)
        {
            var current = source;

            while (current != null)
            {
                if (current is ScrollViewer scrollViewer && CanScroll(scrollViewer, delta))
                    return scrollViewer;

                current = GetParent(current);
            }

            return null;
        }

        private static bool CanScroll(ScrollViewer scrollViewer, int delta)
        {
            if (scrollViewer.ScrollableHeight <= 0)
                return false;

            return delta < 0
                ? scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight
                : scrollViewer.VerticalOffset > 0;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            return element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }
    }
}
