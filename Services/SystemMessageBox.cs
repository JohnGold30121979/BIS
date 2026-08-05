using System;
using System.Windows;

namespace BIS.ERP.Services
{
    public static class SystemMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText)
        {
            LogIfNeeded(messageBoxText, null, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(messageBoxText);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            LogIfNeeded(messageBoxText, caption, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(messageBoxText, caption);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            LogIfNeeded(messageBoxText, caption, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult, options);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText)
        {
            LogIfNeeded(messageBoxText, null, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(owner, messageBoxText);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
        {
            LogIfNeeded(messageBoxText, caption, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
        {
            LogIfNeeded(messageBoxText, caption, MessageBoxImage.None);
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult);
        }

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options)
        {
            LogIfNeeded(messageBoxText, caption, icon);
            return System.Windows.MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult, options);
        }

        private static void LogIfNeeded(string? messageBoxText, string? caption, MessageBoxImage icon)
        {
            var message = messageBoxText ?? string.Empty;
            var title = caption ?? string.Empty;
            var fullMessage = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";

            if (IsError(title, message, icon))
            {
                SystemLogService.Error(fullMessage, "MessageBox");
                return;
            }

            if (IsWarning(title, message, icon))
            {
                SystemLogService.Warning(fullMessage, "MessageBox");
            }
        }

        private static bool IsError(string caption, string message, MessageBoxImage icon)
        {
            return (int)icon == (int)MessageBoxImage.Error ||
                   ContainsAny(caption, "ошибка", "error", "exception") ||
                   ContainsAny(message, "ошибка", "error", "exception", "исключение");
        }

        private static bool IsWarning(string caption, string message, MessageBoxImage icon)
        {
            return (int)icon == (int)MessageBoxImage.Warning ||
                   ContainsAny(caption, "предупреждение", "проверка", "внимание", "warning", "validation") ||
                   ContainsAny(message, "предупреждение", "проверка", "внимание", "warning", "validation");
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var marker in markers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
