using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace BIS.ERP.Behaviors
{
    public static class DatePickerInputMaskBehavior
    {
        public const string DisplayFormat = "dd/MM/yyyy";
        public const string InputMask = "DD/MM/YYYY";

        private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;
        private static bool _isInitialized;

        private static readonly DependencyProperty DatePickerConfiguredProperty =
            DependencyProperty.RegisterAttached(
                "DatePickerInputMaskConfigured",
                typeof(bool),
                typeof(DatePickerInputMaskBehavior),
                new PropertyMetadata(false));

        private static readonly DependencyProperty TextBoxConfiguredProperty =
            DependencyProperty.RegisterAttached(
                "DatePickerTextBoxInputMaskConfigured",
                typeof(bool),
                typeof(DatePickerInputMaskBehavior),
                new PropertyMetadata(false));

        private static readonly DependencyProperty OwnerDatePickerProperty =
            DependencyProperty.RegisterAttached(
                "OwnerDatePicker",
                typeof(DatePicker),
                typeof(DatePickerInputMaskBehavior),
                new PropertyMetadata(null));

        private static readonly DependencyProperty ApplyingTextProperty =
            DependencyProperty.RegisterAttached(
                "DatePickerInputMaskApplyingText",
                typeof(bool),
                typeof(DatePickerInputMaskBehavior),
                new PropertyMetadata(false));

        public static void Initialize()
        {
            if (_isInitialized)
                return;

            ApplyApplicationDateCulture();

            EventManager.RegisterClassHandler(
                typeof(DatePicker),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnDatePickerLoaded),
                true);

            _isInitialized = true;
        }

        private static void ApplyApplicationDateCulture()
        {
            var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
            culture.DateTimeFormat.ShortDatePattern = DisplayFormat;
            culture.DateTimeFormat.DateSeparator = "/";

            CultureInfo.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
        }

        private static void OnDatePickerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is DatePicker datePicker)
                ConfigureDatePicker(datePicker);
        }

        private static void ConfigureDatePicker(DatePicker datePicker)
        {
            if ((bool)datePicker.GetValue(DatePickerConfiguredProperty))
            {
                ApplyDisplayText(datePicker);
                return;
            }

            datePicker.SetValue(DatePickerConfiguredProperty, true);
            datePicker.SelectedDateFormat = DatePickerFormat.Short;
            datePicker.ToolTip ??= "Введите дату в формате DD/MM/YYYY";

            datePicker.DateValidationError += OnDateValidationError;
            datePicker.SelectedDateChanged += OnSelectedDateChanged;
            datePicker.CalendarClosed += OnCalendarClosed;
            datePicker.LostKeyboardFocus += OnDatePickerLostKeyboardFocus;

            datePicker.Dispatcher.BeginInvoke(
                () =>
                {
                    ConfigureTextBox(datePicker);
                    ApplyDisplayText(datePicker);
                },
                DispatcherPriority.Loaded);
        }

        private static void OnDateValidationError(object? sender, DatePickerDateValidationErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        private static void OnSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is not DatePicker datePicker || IsApplyingText(datePicker))
                return;

            datePicker.Dispatcher.BeginInvoke(
                () => ApplyDisplayText(datePicker),
                DispatcherPriority.ContextIdle);
        }

        private static void OnCalendarClosed(object? sender, RoutedEventArgs e)
        {
            if (sender is DatePicker datePicker)
                ApplyDisplayText(datePicker);
        }

        private static void OnDatePickerLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is DatePicker datePicker)
                CommitDatePickerText(datePicker);
        }

        private static void ConfigureTextBox(DatePicker datePicker)
        {
            datePicker.ApplyTemplate();

            var textBox = datePicker.Template?.FindName("PART_TextBox", datePicker) as DatePickerTextBox
                          ?? FindDescendant<DatePickerTextBox>(datePicker);
            if (textBox == null || (bool)textBox.GetValue(TextBoxConfiguredProperty))
                return;

            textBox.SetValue(TextBoxConfiguredProperty, true);
            textBox.SetValue(OwnerDatePickerProperty, datePicker);
            textBox.ToolTip = "Введите дату в формате DD/MM/YYYY";
            SetDatePickerWatermark(textBox);

            textBox.PreviewTextInput += OnTextBoxPreviewTextInput;
            textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
            textBox.LostKeyboardFocus += OnTextBoxLostKeyboardFocus;
            DataObject.AddPastingHandler(textBox, OnTextBoxPaste);
        }

        private static void OnTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            e.Handled = true;
            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => !char.IsDigit(ch) && ch != '/'))
                return;

            ReplaceSelectedText(textBox, e.Text);
            CommitIfComplete(textBox);
        }

        private static void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Back && e.Key != Key.Delete)
                return;

            e.Handled = true;
            DeleteDigit(textBox, e.Key == Key.Back);
            CommitIfComplete(textBox);
        }

        private static void OnTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (textBox.GetValue(OwnerDatePickerProperty) is DatePicker datePicker)
                CommitDatePickerText(datePicker);
        }

        private static void OnTextBoxPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox ||
                !e.DataObject.GetDataPresent(DataFormats.Text))
                return;

            e.CancelCommand();
            ReplaceSelectedText(textBox, e.DataObject.GetData(DataFormats.Text)?.ToString() ?? string.Empty);
            CommitIfComplete(textBox);
        }

        private static void CommitDatePickerText(DatePicker datePicker)
        {
            ConfigureTextBox(datePicker);

            var textBox = datePicker.Template?.FindName("PART_TextBox", datePicker) as DatePickerTextBox
                          ?? FindDescendant<DatePickerTextBox>(datePicker);
            var text = NormalizeDateText(textBox?.Text ?? datePicker.Text);

            if (string.IsNullOrWhiteSpace(text))
            {
                SetSelectedDate(datePicker, null);
                return;
            }

            if (TryParseDate(text, out var date))
            {
                SetSelectedDate(datePicker, date.Date);
                return;
            }

            SetDatePickerText(datePicker, text);
        }

        private static void CommitIfComplete(TextBox textBox)
        {
            if (textBox.GetValue(OwnerDatePickerProperty) is not DatePicker datePicker)
                return;

            var text = NormalizeDateText(textBox.Text);
            if (TryParseDate(text, out var date))
                SetSelectedDate(datePicker, date.Date);
        }

        private static void ApplyDisplayText(DatePicker datePicker)
        {
            ConfigureTextBox(datePicker);

            if (datePicker.SelectedDate.HasValue)
                SetDatePickerText(datePicker, datePicker.SelectedDate.Value.ToString(DisplayFormat, DateCulture));
        }

        private static void SetSelectedDate(DatePicker datePicker, DateTime? date)
        {
            datePicker.SetValue(ApplyingTextProperty, true);
            try
            {
                datePicker.SelectedDate = date;
                SetDatePickerText(datePicker, date?.ToString(DisplayFormat, DateCulture) ?? string.Empty);
            }
            finally
            {
                datePicker.SetValue(ApplyingTextProperty, false);
            }
        }

        private static void SetDatePickerText(DatePicker datePicker, string text)
        {
            datePicker.Text = text;

            datePicker.Dispatcher.BeginInvoke(
                () =>
                {
                    var textBox = datePicker.Template?.FindName("PART_TextBox", datePicker) as DatePickerTextBox
                                  ?? FindDescendant<DatePickerTextBox>(datePicker);
                    if (textBox != null && textBox.Text != text)
                    {
                        textBox.Text = text;
                        textBox.CaretIndex = Math.Min(textBox.Text.Length, text.Length);
                    }
                },
                DispatcherPriority.ContextIdle);
        }

        private static bool IsApplyingText(DatePicker datePicker)
        {
            return (bool)datePicker.GetValue(ApplyingTextProperty);
        }

        private static void ReplaceSelectedText(TextBox textBox, string input)
        {
            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = Math.Min(textBox.SelectionStart, currentText.Length);
            var selectionLength = Math.Min(textBox.SelectionLength, currentText.Length - selectionStart);
            var mergedText = currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, input);
            var caretText = mergedText[..Math.Min(selectionStart + input.Length, mergedText.Length)];
            var caretDigitCount = CountDigits(caretText);

            ApplyMaskedDigits(textBox, ExtractDigits(mergedText), caretDigitCount);
        }

        private static void DeleteDigit(TextBox textBox, bool backspace)
        {
            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = Math.Min(textBox.SelectionStart, currentText.Length);
            var selectionLength = Math.Min(textBox.SelectionLength, currentText.Length - selectionStart);

            if (selectionLength > 0)
            {
                var mergedText = currentText.Remove(selectionStart, selectionLength);
                ApplyMaskedDigits(textBox, ExtractDigits(mergedText), CountDigits(mergedText[..selectionStart]));
                return;
            }

            var digits = ExtractDigits(currentText);
            if (digits.Length == 0)
            {
                textBox.Clear();
                return;
            }

            var digitIndex = backspace
                ? CountDigits(currentText[..selectionStart]) - 1
                : CountDigits(currentText[..selectionStart]);

            if (digitIndex < 0 || digitIndex >= digits.Length)
                return;

            var remainingDigits = digits.Remove(digitIndex, 1);
            ApplyMaskedDigits(textBox, remainingDigits, digitIndex);
        }

        private static void ApplyMaskedDigits(TextBox textBox, string digits, int caretDigitCount)
        {
            var maskedText = FormatDigits(digits);
            textBox.Text = maskedText;
            textBox.CaretIndex = FindCaretIndex(maskedText, caretDigitCount);
        }

        private static string NormalizeDateText(string? value)
        {
            var digits = ExtractDigits(value ?? string.Empty);
            return FormatDigits(digits);
        }

        private static string FormatDigits(string digits)
        {
            digits = digits.Length > 8 ? digits[..8] : digits;

            return digits.Length switch
            {
                <= 2 => digits,
                <= 4 => $"{digits[..2]}/{digits[2..]}",
                _ => $"{digits[..2]}/{digits[2..4]}/{digits[4..]}"
            };
        }

        private static string ExtractDigits(string value)
        {
            return new string(value.Where(char.IsDigit).Take(8).ToArray());
        }

        private static int CountDigits(string value)
        {
            return value.Count(char.IsDigit);
        }

        private static int FindCaretIndex(string text, int digitCount)
        {
            if (digitCount <= 0)
                return 0;

            var seenDigits = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i]))
                    continue;

                seenDigits++;
                if (seenDigits == digitCount)
                    return Math.Min(i + 1, text.Length);
            }

            return text.Length;
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(
                text,
                DisplayFormat,
                DateCulture,
                DateTimeStyles.None,
                out date);
        }

        private static void SetDatePickerWatermark(DatePickerTextBox textBox)
        {
            var watermarkProperty = typeof(DatePickerTextBox)
                .GetField("WatermarkProperty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null) as DependencyProperty;

            if (watermarkProperty != null)
                textBox.SetValue(watermarkProperty, InputMask);
        }

        private static T? FindDescendant<T>(DependencyObject source)
            where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
            {
                var child = VisualTreeHelper.GetChild(source, i);
                if (child is T typedChild)
                    return typedChild;

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }
    }
}

