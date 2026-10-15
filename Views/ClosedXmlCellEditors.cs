using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIS.ERP.Models;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Что правится щелчком по клетке макета.
    ///
    /// Пересечение строки и колонки листа знает свой элемент 1:1: у клетки текстового
    /// блока — сам текст или поле набора, у шапки — ячейку уровня, у строки графов —
    /// колонку таблицы, у итоговых строк — итог, у подписей — строку подписи.
    /// Диалог открывается по типу элемента: у шапки — диалог шапки, у графы — графы.
    /// </summary>
    public sealed class ClosedXmlSheetTarget
    {
        public ClosedXmlBlock? Block { get; init; }

        /// <summary>Клетка текстового блока (заголовок, строки, свободные строки).</summary>
        public ClosedXmlTextCell? TextCell { get; init; }

        /// <summary>Строка листа, на которой стоит клетка (1-based).</summary>
        public int TextRow { get; init; }

        /// <summary>Ячейка уровня шапки таблицы.</summary>
        public ClosedXmlHeaderCell? HeaderCell { get; init; }

        /// <summary>Уровень шапки (0 — верхний).</summary>
        public int HeaderLevelIndex { get; init; }

        /// <summary>Графа таблицы (строка графов под шапкой).</summary>
        public ClosedXmlColumn? Column { get; init; }

        /// <summary>Итоговая строка: общий итог или дополнительный итог.</summary>
        public ClosedXmlTotal? Total { get; init; }

        /// <summary>Строка подписей.</summary>
        public ClosedXmlSignature? Signature { get; init; }

        /// <summary>Подсказка для строки состояния и всплывающей подсказки.</summary>
        public string Info { get; init; } = string.Empty;

        /// <summary>Есть ли для этого элемента диалог правки.</summary>
        public bool HasEditor =>
            TextCell != null || HeaderCell != null || Column != null || Total != null || Signature != null;
    }

    /// <summary>
    /// Общие части диалогов правки макета: строка «подпись — поле», подсказка, кнопки.
    /// </summary>
    internal static class ClosedXmlDialogParts
    {
        public static void AddRow(Panel panel, string label, FrameworkElement control)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 180,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            row.Children.Add(control);
            panel.Children.Add(row);
        }

        public static void AddHint(Panel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        public static void AddTitle(Panel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        /// <summary>
        /// Кнопки «Сохранить» и «Отмена». Проверку значений делает <paramref name="accept"/>:
        /// если он бросает исключение, окно остаётся открытым и показывает причину.
        /// </summary>
        public static void AddButtons(Window window, Panel panel, string acceptText, Action accept)
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var ok = new Button { Content = acceptText, Width = 120, Height = 30, IsDefault = true };
            ok.Click += (_, _) =>
            {
                try
                {
                    accept();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Правка макета", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                window.DialogResult = true;
            };

            var cancel = new Button
            {
                Content = "Отмена", Width = 100, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true
            };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
        }

        public static int ParseInt(string? text, int fallback) =>
            int.TryParse((text ?? string.Empty).Trim(), out var value) ? value : fallback;

        public static ComboBox CreateCombo(params string[] items)
        {
            var combo = new ComboBox { Width = 120 };
            foreach (var item in items)
                combo.Items.Add(item);
            combo.SelectedIndex = 0;
            return combo;
        }

        public static void SelectCombo(ComboBox combo, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (var item in combo.Items)
            {
                if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Диалог правки ячейки шапки таблицы: подпись, объединение по колонкам,
    /// сколько уровней занимает по вертикали, выравнивание, поворот и заливка.
    /// </summary>
    public class ClosedXmlHeaderCellDialog : Window
    {
        private readonly ClosedXmlHeaderCell _cell;
        private readonly TextBox _textBox = new()
        {
            Width = 300, Height = 52, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true
        };
        private readonly TextBox _fromBox = new() { Width = 60 };
        private readonly TextBox _toBox = new() { Width = 60 };
        private readonly TextBox _rowSpanBox = new() { Width = 60 };
        private readonly TextBox _fillBox = new() { Width = 120 };
        private readonly ComboBox _alignCombo = ClosedXmlDialogParts.CreateCombo("Left", "Center", "Right");
        private readonly ComboBox _rotationCombo = ClosedXmlDialogParts.CreateCombo("0", "45", "90", "270");
        private readonly CheckBox _boldCheck = new() { Content = "Жирный", VerticalAlignment = VerticalAlignment.Center };

        public ClosedXmlHeaderCellDialog(ClosedXmlHeaderCell cell, int levelIndex, int gridColumns)
        {
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));
            var columns = Math.Max(1, gridColumns);

            Title = $"Правка ячейки шапки — уровень {levelIndex + 1}";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            ClosedXmlDialogParts.AddTitle(panel, $"Ячейка шапки таблицы — уровень {levelIndex + 1}");
            ClosedXmlDialogParts.AddHint(panel,
                "Шапка собирается из уровней: верхний объединяет графы («Сумма» над «Дт» и «Кт»), " +
                "нижний подписывает каждую. Пустая подпись берёт заголовок колонки. " +
                "«Занимает уровней» нужно графам вроде «Количество». " +
                $"Колонок на листе: {columns}.");

            _textBox.Text = cell.Text ?? string.Empty;
            _fromBox.Text = Math.Max(1, cell.Column).ToString();
            _toBox.Text = (cell.MergeToColumn > cell.Column ? cell.MergeToColumn : cell.Column).ToString();
            _rowSpanBox.Text = Math.Max(1, cell.RowSpan).ToString();
            _fillBox.Text = cell.Fill ?? string.Empty;
            _boldCheck.IsChecked = cell.Bold;
            ClosedXmlDialogParts.SelectCombo(_alignCombo, cell.Align);
            ClosedXmlDialogParts.SelectCombo(_rotationCombo, cell.TextRotation.ToString());

            ClosedXmlDialogParts.AddRow(panel, "Подпись (пусто — из колонки):", _textBox);
            ClosedXmlDialogParts.AddRow(panel, "Колонка с:", _fromBox);
            ClosedXmlDialogParts.AddRow(panel, "по колонку:", _toBox);
            ClosedXmlDialogParts.AddRow(panel, "Занимает уровней:", _rowSpanBox);
            ClosedXmlDialogParts.AddRow(panel, "Выравнивание:", _alignCombo);
            ClosedXmlDialogParts.AddRow(panel, "Поворот текста:", _rotationCombo);
            ClosedXmlDialogParts.AddRow(panel, "Заливка (#RRGGBB):", _fillBox);
            ClosedXmlDialogParts.AddRow(panel, string.Empty, _boldCheck);

            ClosedXmlDialogParts.AddButtons(this, panel, "Сохранить", Apply);
            Content = panel;
        }

        private void Apply()
        {
            var from = Math.Max(1, ClosedXmlDialogParts.ParseInt(_fromBox.Text, _cell.Column));
            var to = ClosedXmlDialogParts.ParseInt(_toBox.Text, from);
            if (to < from)
                to = from;

            _cell.Column = from;
            _cell.MergeToColumn = to;
            _cell.RowSpan = Math.Max(1, ClosedXmlDialogParts.ParseInt(_rowSpanBox.Text, 1));
            _cell.Text = (_textBox.Text ?? string.Empty).Trim();
            _cell.Bold = _boldCheck.IsChecked == true;
            _cell.Align = _alignCombo.SelectedItem?.ToString() ?? "Center";
            _cell.TextRotation = ClosedXmlDialogParts.ParseInt(_rotationCombo.SelectedItem?.ToString(), 0);
            _cell.Fill = (_fillBox.Text ?? string.Empty).Trim();
        }
    }

    /// <summary>Диалог правки итоговой строки таблицы: метка, поле метки, поля сумм.</summary>
    public class ClosedXmlTotalDialog : Window
    {
        private readonly ClosedXmlTotal _total;
        private readonly TextBox _labelBox = new() { Width = 300 };
        private readonly TextBox _labelFieldBox = new() { Width = 300 };
        private readonly TextBox _sumFieldsBox = new() { Width = 300 };
        private readonly CheckBox _boldCheck = new() { Content = "Жирный", VerticalAlignment = VerticalAlignment.Center };

        public ClosedXmlTotalDialog(ClosedXmlTotal total, string title)
        {
            _total = total ?? throw new ArgumentNullException(nameof(total));

            Title = $"Правка итога — {title}";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            ClosedXmlDialogParts.AddTitle(panel, $"Итоговая строка — {title}");
            ClosedXmlDialogParts.AddHint(panel,
                "Метка печатается в строке итога. Если задано «Поле метки», метка берётся из данных " +
                "(например, ставка налога). Пустой список полей сумм — суммируются все числовые графы.");

            _labelBox.Text = total.Label ?? string.Empty;
            _labelFieldBox.Text = total.LabelField ?? string.Empty;
            _sumFieldsBox.Text = total.SumFields == null ? string.Empty : string.Join(", ", total.SumFields);
            _boldCheck.IsChecked = total.Bold;

            ClosedXmlDialogParts.AddRow(panel, "Метка:", _labelBox);
            ClosedXmlDialogParts.AddRow(panel, "Поле метки (необязательно):", _labelFieldBox);
            ClosedXmlDialogParts.AddRow(panel, "Поля сумм (через запятую):", _sumFieldsBox);
            ClosedXmlDialogParts.AddRow(panel, string.Empty, _boldCheck);

            ClosedXmlDialogParts.AddButtons(this, panel, "Сохранить", Apply);
            Content = panel;
        }

        private void Apply()
        {
            var label = (_labelBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(label))
                throw new InvalidOperationException("Метка итога не может быть пустой: она печатается в строке итога.");

            _total.Label = label;
            _total.LabelField = (_labelFieldBox.Text ?? string.Empty).Trim();
            _total.Bold = _boldCheck.IsChecked == true;
            _total.SumFields = (_sumFieldsBox.Text ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }
    }

    /// <summary>Диалог правки строки подписей: текст, колонки и жирный.</summary>
    public class ClosedXmlSignatureDialog : Window
    {
        private readonly ClosedXmlSignature _signature;
        private readonly TextBox _textBox = new()
        {
            Width = 300, Height = 52, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true
        };
        private readonly TextBox _fromBox = new() { Width = 60 };
        private readonly TextBox _toBox = new() { Width = 60 };
        private readonly CheckBox _boldCheck = new() { Content = "Жирный", VerticalAlignment = VerticalAlignment.Center };

        public ClosedXmlSignatureDialog(ClosedXmlSignature signature, int gridColumns)
        {
            _signature = signature ?? throw new ArgumentNullException(nameof(signature));
            var columns = Math.Max(1, gridColumns);

            Title = "Правка строки подписей";
            Width = 640;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16) };
            ClosedXmlDialogParts.AddTitle(panel, "Строка подписей");
            ClosedXmlDialogParts.AddHint(panel,
                $"Подпись печатается одной строкой листа. Колонок на листе: {columns}.");

            _textBox.Text = signature.Text ?? string.Empty;
            _fromBox.Text = Math.Max(1, signature.Column).ToString();
            _toBox.Text = (signature.MergeToColumn > signature.Column ? signature.MergeToColumn : signature.Column)
                .ToString();
            _boldCheck.IsChecked = signature.Bold;

            ClosedXmlDialogParts.AddRow(panel, "Текст подписи:", _textBox);
            ClosedXmlDialogParts.AddRow(panel, "Колонка с:", _fromBox);
            ClosedXmlDialogParts.AddRow(panel, "по колонку:", _toBox);
            ClosedXmlDialogParts.AddRow(panel, string.Empty, _boldCheck);

            ClosedXmlDialogParts.AddButtons(this, panel, "Сохранить", Apply);
            Content = panel;
        }

        private void Apply()
        {
            var text = (_textBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Пустую подпись печатать нечего — впишите текст.");

            var from = Math.Max(1, ClosedXmlDialogParts.ParseInt(_fromBox.Text, _signature.Column));
            var to = ClosedXmlDialogParts.ParseInt(_toBox.Text, from);

            _signature.Text = text;
            _signature.Column = from;
            _signature.MergeToColumn = to < from ? from : to;
            _signature.Bold = _boldCheck.IsChecked == true;
        }
    }
}
