using System.Windows;

namespace BIS.ERP.Behaviors
{
    public static class ResponsiveWindowBehavior
    {
        private const double ScreenMargin = 24;
        private static bool _isRegistered;

        public static void Initialize()
        {
            if (_isRegistered)
                return;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));

            _isRegistered = true;
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window || window.WindowState == WindowState.Maximized)
                return;

            var workArea = SystemParameters.WorkArea;
            var maxWidth = Math.Max(320, workArea.Width - ScreenMargin);
            var maxHeight = Math.Max(260, workArea.Height - ScreenMargin);

            if (window.MinWidth > maxWidth)
                window.MinWidth = maxWidth;
            if (window.MinHeight > maxHeight)
                window.MinHeight = maxHeight;

            window.MaxWidth = Math.Min(window.MaxWidth, maxWidth);
            window.MaxHeight = Math.Min(window.MaxHeight, maxHeight);

            if (!double.IsNaN(window.Width) && window.Width > maxWidth)
                window.Width = maxWidth;
            if (!double.IsNaN(window.Height) && window.Height > maxHeight)
                window.Height = maxHeight;

            if (window.ActualWidth > maxWidth || window.ActualHeight > maxHeight)
            {
                window.SizeToContent = SizeToContent.Manual;
                if (window.ActualWidth > maxWidth)
                    window.Width = maxWidth;
                if (window.ActualHeight > maxHeight)
                    window.Height = maxHeight;
            }

            KeepInsideWorkArea(window, workArea);
        }

        private static void KeepInsideWorkArea(Window window, Rect workArea)
        {
            if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
                return;

            if (window.Left + window.Width > workArea.Right)
                window.Left = Math.Max(workArea.Left, workArea.Right - window.Width);
            if (window.Top + window.Height > workArea.Bottom)
                window.Top = Math.Max(workArea.Top, workArea.Bottom - window.Height);
            if (window.Left < workArea.Left)
                window.Left = workArea.Left;
            if (window.Top < workArea.Top)
                window.Top = workArea.Top;
        }
    }
}
