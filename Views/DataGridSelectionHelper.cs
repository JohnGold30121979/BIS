using System;
using System.Collections.Generic;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Помогает «остаться» на записи после закрытия диалога: после перезагрузки
    /// списка выделяет строку созданной (или изменённой) записи, прокручивает
    /// к ней таблицу и переводит в таблицу клавиатурный фокус.
    ///
    /// Выделение не просто выставляется один раз, а удерживается короткое время,
    /// потому что сразу после закрытия диалога список перезагружается ещё раз:
    /// MDI-воркспейс заново прикрепляет содержимое вкладки-вызывателя (она повторно
    /// проходит Loaded → перезагрузку данных), а модальные сообщения «Запись
    /// добавлена/обновлена» забирают клавиатурный фокус. Без этого строка
    /// показывается и тут же выглядит невыделенной.
    /// </summary>
    public static class DataGridSelectionHelper
    {
        /// <summary>Как часто перепроверяем, что выделение и фокус не сброшены.</summary>
        private const int RetryIntervalMs = 150;

        /// <summary>Сколько времени удерживаем выделение после закрытия диалога.</summary>
        private const int RetryWindowMs = 5000;

        /// <summary>Сколько проверок подряд должны пройти успешно, чтобы прекратить повторы.</summary>
        private const int StableTicksToStop = 2;

        /// <summary>
        /// Выделяет строку с указанным идентификатором, прокручивает к ней таблицу
        /// и переводит фокус в таблицу. Если идентификатор не задан — ничего не меняет;
        /// если строки ещё нет в списке (список перезагружается) — повторяет попытку.
        /// </summary>
        public static void SelectRowById(this DataGrid grid, Guid? recordId, Func<object, Guid?> idSelector)
        {
            if (grid == null || idSelector == null || recordId is not { } target || target == Guid.Empty)
                return;

            object? FindTargetRow()
            {
                foreach (var item in grid.Items)
                {
                    if (idSelector(item) == target)
                        return item;
                }

                return null;
            }

            KeepRowSelected(grid, FindTargetRow, item => idSelector(item) == target);
        }

        /// <summary>
        /// Выделяет и прокручивает к уже известному объекту строки, переводя
        /// фокус в таблицу; повторяет попытку, пока строка есть в списке.
        /// </summary>
        public static void SelectRow(this DataGrid grid, object? item)
        {
            if (grid == null || item == null)
                return;

            KeepRowSelected(
                grid,
                () => grid.Items.Contains(item) ? item : null,
                candidate => ReferenceEquals(candidate, item));
        }

        /// <summary>
        /// Выделяет нужную строку и удерживает выделение с фокусом, пока список
        /// перезагружается (Loaded вкладки-вызывателя, модальные сообщения и т. п.).
        /// Прекращает работу, как только пользователь сам выбрал другую строку,
        /// начал ввод в текстовом поле или ушёл работать в другую таблицу.
        /// </summary>
        private static void KeepRowSelected(
            DataGrid grid,
            Func<object?> findTargetRow,
            Func<object, bool> isTargetRow)
        {
            if (!grid.IsLoaded)
            {
                // Таблица ещё не в визуальном дереве (вкладка MDI переприкрепляется) —
                // применим выделение, как только она загрузится.
                void OnGridLoaded(object sender, RoutedEventArgs e)
                {
                    grid.Loaded -= OnGridLoaded;
                    KeepRowSelected(grid, findTargetRow, isTargetRow);
                }

                grid.Loaded += OnGridLoaded;
                return;
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(RetryWindowMs);
            var stableTicks = 0;
            DispatcherTimer? timer = null;

            void Stop()
            {
                var activeTimer = timer;
                if (activeTimer == null)
                    return;

                timer = null;
                activeTimer.Stop();
                activeTimer.Tick -= OnTick;
                grid.Unloaded -= OnGridUnloaded;
            }

            void OnGridUnloaded(object sender, RoutedEventArgs e) => Stop();

            void OnTick(object? sender, EventArgs e)
            {
                try
                {
                    var target = grid.SelectedItem is { } selected && isTargetRow(selected)
                        ? selected
                        : findTargetRow();

                    if (target == null)
                    {
                        // Строки пока нет — список ещё перезагружается; ждём, но недолго.
                        if (DateTime.UtcNow >= deadline)
                            Stop();

                        return;
                    }

                    if (grid.SelectedItem is { } current && !isTargetRow(current))
                    {
                        // Пользователь сам переключился на другую строку — не вмешиваемся.
                        Stop();
                        return;
                    }

                    if (IsUserBusyElsewhere(grid))
                    {
                        // Пользователь уже работает с другим элементом — фокус не отбираем.
                        Stop();
                        return;
                    }

                    ApplyRow(grid, target);

                    if (FocusRow(grid, target) && grid.SelectedItem is { } stillTarget && isTargetRow(stillTarget))
                        stableTicks++;
                    else
                        stableTicks = 0;

                    if (stableTicks >= StableTicksToStop || DateTime.UtcNow >= deadline)
                        Stop();
                }
                catch
                {
                    Stop();
                }
            }

            // Первое применение — сразу, чтобы строка появилась без задержки.
            var initialTarget = findTargetRow();
            if (initialTarget != null)
            {
                ApplyRow(grid, initialTarget);
                FocusRow(grid, initialTarget);
            }

            if (!grid.IsVisible)
                return;

            timer = new DispatcherTimer(DispatcherPriority.Background, grid.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(RetryIntervalMs)
            };
            timer.Tick += OnTick;
            grid.Unloaded += OnGridUnloaded;
            timer.Start();
        }

        private static void ApplyRow(DataGrid grid, object row)
        {
            if (!ReferenceEquals(grid.SelectedItem, row))
                grid.SelectedItem = row;

            grid.UpdateLayout();
            grid.ScrollIntoView(row);
        }

        /// <summary>
        /// Переводит клавиатурный фокус в таблицу на выделенную строку. Без этого
        /// выделенная строка отображается «неактивной» (серой), а клавиши не работают.
        /// Фокус не отбирается у другого окна приложения и у полей ввода.
        /// </summary>
        private static bool FocusRow(DataGrid grid, object row)
        {
            if (!grid.IsLoaded || !grid.IsVisible)
                return false;

            if (grid.IsKeyboardFocusWithin)
                return true;

            var window = Window.GetWindow(grid);
            if (window == null || !window.IsActive)
                return false;

            if (IsUserBusyElsewhere(grid))
                return false;

            try
            {
                if (grid.Columns.Count > 0)
                    grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);

                grid.Focus();
            }
            catch
            {
                return false;
            }

            return grid.IsKeyboardFocusWithin;
        }

        /// <summary>
        /// Признак того, что пользователь уже взаимодействует с другим элементом:
        /// печатает в поле ввода, работает с выпадающим списком или с другой таблицей.
        /// </summary>
        private static bool IsUserBusyElsewhere(DataGrid grid)
        {
            if (Keyboard.FocusedElement is not DependencyObject focused)
                return false;

            if (focused is TextBoxBase or PasswordBox or ComboBox or ComboBoxItem)
                return true;

            var focusedGrid = FindAncestor<DataGrid>(focused);
            return focusedGrid != null && !ReferenceEquals(focusedGrid, grid);
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                if (node is T match)
                    return match;

                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }

            return null;
        }

        /// <summary>Идентификатор записи из строки-словаря (Dictionary&lt;string, object&gt;).</summary>
        public static Guid? GetIdFromDictionary(object? item)
        {
            if (item is not Dictionary<string, object> row)
                return null;

            if (!row.TryGetValue("Id", out var value))
                return null;

            return TryConvertToGuid(value);
        }

        /// <summary>Идентификатор записи из строки DataTable/DataView (колонка «Id»).</summary>
        public static Guid? GetIdFromDataRow(object? item)
        {
            if (item is not DataRowView rowView)
                return null;

            try
            {
                if (!rowView.Row.Table.Columns.Contains("Id"))
                    return null;

                return TryConvertToGuid(rowView["Id"]);
            }
            catch
            {
                return null;
            }
        }

        private static Guid? TryConvertToGuid(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (value is Guid guid)
                return guid;

            return Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
        }
    }
}
