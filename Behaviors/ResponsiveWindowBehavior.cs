using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BIS.ERP.Behaviors
{
    public static class ResponsiveWindowBehavior
    {
        private const double DialogScreenMargin = 24;
        private const double MainScreenMargin = 12;
        private const double DialogWidthRatio = 0.94;
        private const double DialogHeightRatio = 0.90;
        private const double MainWidthRatio = 0.98;
        private const double MainHeightRatio = 0.96;
        private const double MinimumWindowWidth = 320;
        private const double MinimumWindowHeight = 260;
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

            ApplyResponsiveSizing(window);

            // Повторяем после финального layout, когда ActualWidth/ActualHeight уже честные.
            _ = window.Dispatcher.BeginInvoke(
                () => ApplyResponsiveSizing(window),
                DispatcherPriority.ContextIdle);
        }

        private static void ApplyResponsiveSizing(Window window)
        {
            if (window.WindowState == WindowState.Maximized)
                return;

            var isDialog = IsDialogWindow(window);
            var workArea = GetWindowWorkArea(window);
            var horizontalMargin = isDialog ? DialogScreenMargin : MainScreenMargin;
            var verticalMargin = isDialog ? DialogScreenMargin : MainScreenMargin;
            var widthRatio = isDialog ? DialogWidthRatio : MainWidthRatio;
            var heightRatio = isDialog ? DialogHeightRatio : MainHeightRatio;

            var maxWidth = Math.Max(MinimumWindowWidth, Math.Min(workArea.Width - horizontalMargin * 2, workArea.Width * widthRatio));
            var maxHeight = Math.Max(MinimumWindowHeight, Math.Min(workArea.Height - verticalMargin * 2, workArea.Height * heightRatio));

            if (window.MinWidth > maxWidth)
                window.MinWidth = maxWidth;
            if (window.MinHeight > maxHeight)
                window.MinHeight = maxHeight;

            if (double.IsInfinity(window.MaxWidth) || window.MaxWidth > maxWidth)
                window.MaxWidth = maxWidth;
            if (double.IsInfinity(window.MaxHeight) || window.MaxHeight > maxHeight)
                window.MaxHeight = maxHeight;

            var currentWidth = ResolveWindowWidth(window);
            var currentHeight = ResolveWindowHeight(window);
            var needsWidthClamp = currentWidth > maxWidth;
            var needsHeightClamp = currentHeight > maxHeight;

            if (isDialog && window.ResizeMode == ResizeMode.NoResize && (needsWidthClamp || needsHeightClamp))
                window.ResizeMode = ResizeMode.CanResize;

            if (needsWidthClamp)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Width = maxWidth;
            }

            if (needsHeightClamp)
            {
                window.SizeToContent = SizeToContent.Manual;
                window.Height = maxHeight;
            }

            CenterDialogIfNeeded(window, workArea, isDialog);
            KeepInsideWorkArea(window, workArea);
        }

        private static bool IsDialogWindow(Window window)
        {
            var typeName = window.GetType().Name;
            var fullName = window.GetType().FullName ?? string.Empty;

            return window.Owner != null
                   || window.WindowStartupLocation == WindowStartupLocation.CenterOwner
                   || typeName.EndsWith("Dialog", StringComparison.OrdinalIgnoreCase)
                   || fullName.Contains(".Dialogs.", StringComparison.OrdinalIgnoreCase);
        }

        private static Rect GetWindowWorkArea(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero && window.Owner != null)
                handle = new WindowInteropHelper(window.Owner).Handle;

            if (handle == IntPtr.Zero)
                return SystemParameters.WorkArea;

            var screen = Forms.Screen.FromHandle(handle);
            var bounds = screen.WorkingArea;
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget == null)
                return SystemParameters.WorkArea;

            var transform = source.CompositionTarget.TransformFromDevice;
            var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
            var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private static double ResolveWindowWidth(Window window)
        {
            if (!double.IsNaN(window.Width) && window.Width > 0)
                return window.Width;

            return window.ActualWidth > 0 ? window.ActualWidth : window.DesiredSize.Width;
        }

        private static double ResolveWindowHeight(Window window)
        {
            if (!double.IsNaN(window.Height) && window.Height > 0)
                return window.Height;

            return window.ActualHeight > 0 ? window.ActualHeight : window.DesiredSize.Height;
        }

        private static void CenterDialogIfNeeded(Window window, Rect workArea, bool isDialog)
        {
            if (!isDialog)
                return;

            var width = ResolveWindowWidth(window);
            var height = ResolveWindowHeight(window);
            if (width <= 0 || height <= 0)
                return;

            if (window.Owner != null && window.WindowStartupLocation == WindowStartupLocation.CenterOwner)
            {
                var ownerWidth = ResolveWindowWidth(window.Owner);
                var ownerHeight = ResolveWindowHeight(window.Owner);
                if (ownerWidth > 0 && ownerHeight > 0 && !double.IsNaN(window.Owner.Left) && !double.IsNaN(window.Owner.Top))
                {
                    window.Left = window.Owner.Left + (ownerWidth - width) / 2;
                    window.Top = window.Owner.Top + (ownerHeight - height) / 2;
                    return;
                }
            }

            if (window.WindowStartupLocation == WindowStartupLocation.CenterScreen || window.WindowStartupLocation == WindowStartupLocation.CenterOwner)
            {
                window.Left = workArea.Left + (workArea.Width - width) / 2;
                window.Top = workArea.Top + (workArea.Height - height) / 2;
            }
        }

        private static void KeepInsideWorkArea(Window window, Rect workArea)
        {
            var width = ResolveWindowWidth(window);
            var height = ResolveWindowHeight(window);
            if (width <= 0 || height <= 0 || double.IsNaN(window.Left) || double.IsNaN(window.Top))
                return;

            if (window.Left + width > workArea.Right)
                window.Left = Math.Max(workArea.Left, workArea.Right - width);
            if (window.Top + height > workArea.Bottom)
                window.Top = Math.Max(workArea.Top, workArea.Bottom - height);
            if (window.Left < workArea.Left)
                window.Left = workArea.Left;
            if (window.Top < workArea.Top)
                window.Top = workArea.Top;
        }
    }
}
