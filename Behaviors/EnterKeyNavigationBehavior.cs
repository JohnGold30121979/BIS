using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BIS.ERP.Behaviors
{
    public static class EnterKeyNavigationBehavior
    {
        private static bool _isInitialized;

        public static void Initialize()
        {
            if (_isInitialized)
                return;

            EventManager.RegisterClassHandler(
                typeof(Window),
                Keyboard.PreviewKeyDownEvent,
                new KeyEventHandler(OnPreviewKeyDown));

            _isInitialized = true;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
                return;

            if (ShouldIgnoreEnter(focusedElement))
                return;

            var scope = FindNavigationScope(focusedElement) ?? sender as DependencyObject;
            if (scope == null)
                return;

            if (FindAncestorOrSelf<Button>(focusedElement, scope) is Button focusedButton)
            {
                if (!focusedButton.IsEnabled)
                    return;

                e.Handled = true;
                focusedButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                return;
            }

            if (NormalizeFocusableElement(focusedElement, scope) is not DependencyObject currentElement)
                return;

            e.Handled = MoveToNextElement(currentElement, scope);
        }

        private static bool ShouldIgnoreEnter(DependencyObject focusedElement)
        {
            if (HasAncestor<DataGrid>(focusedElement) || HasAncestor<DataGridCell>(focusedElement))
                return true;

            if (FindAncestorOrSelf<ComboBox>(focusedElement) is ComboBox comboBox && comboBox.IsDropDownOpen)
                return true;

            if (FindAncestorOrSelf<DatePicker>(focusedElement) is DatePicker datePicker && datePicker.IsDropDownOpen)
                return true;

            return false;
        }

        private static DependencyObject? FindNavigationScope(DependencyObject element)
        {
            var current = element;

            while (current != null)
            {
                if (current is UserControl or Window)
                    return current;

                current = GetParent(current);
            }

            return null;
        }

        private static DependencyObject? NormalizeFocusableElement(DependencyObject element, DependencyObject scope)
        {
            if (FindAncestorOrSelf<TextBox>(element, scope) is TextBox textBox)
                return textBox;

            if (FindAncestorOrSelf<PasswordBox>(element, scope) is PasswordBox passwordBox)
                return passwordBox;

            if (FindAncestorOrSelf<ComboBox>(element, scope) is ComboBox comboBox)
                return comboBox;

            if (FindAncestorOrSelf<DatePicker>(element, scope) is DatePicker datePicker)
                return datePicker;

            if (FindAncestorOrSelf<CheckBox>(element, scope) is CheckBox checkBox)
                return checkBox;

            if (FindAncestorOrSelf<RadioButton>(element, scope) is RadioButton radioButton)
                return radioButton;

            if (FindAncestorOrSelf<Button>(element, scope) is Button button)
                return button;

            return null;
        }

        private static bool FocusPrimaryActionButton(DependencyObject scope)
        {
            var primaryButton = FindPrimaryActionButton(scope);
            if (primaryButton != null)
            {
                FocusElement(primaryButton);
                return true;
            }

            return false;
        }

        private static Button? FindPrimaryActionButton(DependencyObject scope)
        {
            Button? fallbackButton = null;

            Traverse(scope, child =>
            {
                if (child is not Button button || !button.IsEnabled || !button.IsVisible || !button.IsTabStop)
                    return false;

                if (fallbackButton == null && button.IsDefault)
                {
                    fallbackButton = button;
                }

                if (IsPrimaryActionButton(button))
                {
                    fallbackButton = button;
                    return true;
                }

                return false;
            });

            return fallbackButton;
        }

        private static bool IsPrimaryActionButton(Button button)
        {
            var buttonName = button.Name ?? string.Empty;
            var buttonText = button.Content?.ToString() ?? string.Empty;
            var normalized = $"{buttonName} {buttonText}".ToLowerInvariant();

            return normalized.Contains("save") ||
                   normalized.Contains("create") ||
                   normalized.Contains("login") ||
                   normalized.Contains("import") ||
                   normalized.Contains("apply") ||
                   normalized.Contains("ok") ||
                   normalized.Contains("сохран") ||
                   normalized.Contains("создат") ||
                   normalized.Contains("войти") ||
                   normalized.Contains("импорт") ||
                   normalized.Contains("примен") ||
                   normalized.Contains("ок");
        }

        private static bool IsSecondaryActionButton(Button button)
        {
            var buttonName = button.Name ?? string.Empty;
            var buttonText = button.Content?.ToString() ?? string.Empty;
            var normalized = $"{buttonName} {buttonText}".ToLowerInvariant();

            return normalized.Contains("cancel") ||
                   normalized.Contains("close") ||
                   normalized.Contains("exit") ||
                   normalized.Contains("отмена") ||
                   normalized.Contains("закры") ||
                   normalized.Contains("выход");
        }

        private static bool MoveToNextElement(DependencyObject currentElement, DependencyObject scope)
        {
            var current = currentElement;

            for (var i = 0; i < 50; i++)
            {
                if (!TryMoveFocusNext(current))
                    return FocusPrimaryActionButton(scope);

                if (Keyboard.FocusedElement is not DependencyObject movedFocus)
                    return FocusPrimaryActionButton(scope);

                if (!IsDescendantOrSelf(movedFocus, scope))
                    return FocusPrimaryActionButton(scope);

                var normalized = NormalizeFocusableElement(movedFocus, scope);
                if (normalized == null || normalized == current)
                    return FocusPrimaryActionButton(scope);

                if (IsInputControl(normalized))
                    return true;

                if (normalized is Button button)
                {
                    if (IsSecondaryActionButton(button))
                        return FocusPrimaryActionButton(scope);

                    if (IsPrimaryActionButton(button) || button.IsDefault)
                        return true;
                }

                current = normalized;
            }

            return FocusPrimaryActionButton(scope);
        }

        private static bool TryMoveFocusNext(DependencyObject currentElement)
        {
            var request = new TraversalRequest(FocusNavigationDirection.Next);

            return currentElement switch
            {
                UIElement uiElement => uiElement.MoveFocus(request),
                ContentElement contentElement => contentElement.MoveFocus(request),
                _ => false
            };
        }

        private static bool IsInputControl(DependencyObject element)
        {
            return element is TextBox ||
                   element is PasswordBox ||
                   element is ComboBox ||
                   element is DatePicker ||
                   element is CheckBox ||
                   element is RadioButton;
        }

        private static void FocusElement(DependencyObject element)
        {
            switch (element)
            {
                case UIElement uiElement:
                    uiElement.Focus();
                    break;
                case ContentElement contentElement:
                    contentElement.Focus();
                    break;
            }
        }

        private static T? FindAncestorOrSelf<T>(DependencyObject? element, DependencyObject? scope = null)
            where T : DependencyObject
        {
            var current = element;

            while (current != null)
            {
                if (current is T target)
                    return target;

                if (scope != null && current == scope)
                    break;

                current = GetParent(current);
            }

            return null;
        }

        private static bool HasAncestor<T>(DependencyObject? element)
            where T : DependencyObject
        {
            return FindAncestorOrSelf<T>(element) != null;
        }

        private static bool IsDescendantOrSelf(DependencyObject element, DependencyObject scope)
        {
            var current = element;

            while (current != null)
            {
                if (current == scope)
                    return true;

                current = GetParent(current);
            }

            return false;
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

        private static bool Traverse(DependencyObject root, Func<DependencyObject, bool> visitor)
        {
            var childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (visitor(child))
                    return true;

                if (Traverse(child, visitor))
                    return true;
            }

            return false;
        }
    }
}
