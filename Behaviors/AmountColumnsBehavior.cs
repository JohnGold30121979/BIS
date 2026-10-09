using BIS.ERP.Converters;
using System.Windows.Controls;
using System.Windows.Data;

namespace BIS.ERP.Behaviors
{
    /// <summary>
    /// Применяет единый формат сумм (DecimalInputConverter: разряды пробелом, дробная часть запятой)
    /// к колонкам, которые DataGrid генерирует автоматически (AutoGenerateColumns="True").
    /// Вручную заданные колонки в XAML не затрагиваются — у них конвертер уже прописан.
    /// Подключение: AmountColumnsBehavior.Attach(Grid) в конструкторе окна после InitializeComponent().
    /// </summary>
    public static class AmountColumnsBehavior
    {
        public static void Attach(DataGrid grid)
        {
            if (grid == null)
                return;

            grid.AutoGeneratingColumn -= OnAutoGeneratingColumns;
            grid.AutoGeneratingColumn += OnAutoGeneratingColumns;
        }

        private static void OnAutoGeneratingColumns(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is not DataGridTextColumn textColumn)
                return;

            // Числовые колонки с дробной частью трактуем как суммы.
            if (e.PropertyType != typeof(decimal) &&
                e.PropertyType != typeof(double) &&
                e.PropertyType != typeof(float))
                return;

            if (textColumn.Binding is not Binding binding)
                return;

            binding.Converter = new DecimalInputConverter();
            binding.ConverterParameter = "N2";
        }
    }
}