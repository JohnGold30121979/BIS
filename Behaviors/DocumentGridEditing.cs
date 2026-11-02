using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Behaviors
{
    /// <summary>Фиксация текста редактора до навигации по таблице документа.</summary>
    public static class DocumentGridEditing
    {
        public static bool Commit(DataGrid grid)
        {
            // В шаблонной колонке CommitEdit не обновляет LostFocus-binding:
            // TextBox остаётся в фокусе, особенно в последней ячейке таблицы.
            if (grid.IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox box)
            {
                if (!CommitText(box))
                    return false;
            }

            return grid.CommitEdit(DataGridEditingUnit.Cell, true) &&
                   grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        internal static bool CommitText(TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            return !Validation.GetHasError(box);
        }
    }
}
