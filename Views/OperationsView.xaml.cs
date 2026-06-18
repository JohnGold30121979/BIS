using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System.Linq;
using System.Collections.Generic;

namespace BIS.ERP.Views
{
    public partial class OperationsView : UserControl
    {
        public OperationsView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadOperations();
        }

        private async System.Threading.Tasks.Task LoadOperations()
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var documentService = new DocumentService(context);
            var documents = await documentService.GetDocumentsAsync();

            // Создаем список для отображения
            var allRows = new List<DisplayRow>();

            foreach (var doc in documents)
            {
                // Проверяем, что Rows не null
                if (doc.Rows != null)
                {
                    foreach (var row in doc.Rows)
                    {
                        // Получаем данные из JSON
                        var rowData = documentService.GetRowData(row);

                        // Извлекаем основные поля
                        var displayRow = new DisplayRow
                        {
                            DocumentNumber = doc.Number,
                            DocumentDate = doc.Date,
                            RowNumber = row.RowNumber,
                            OperationCode = GetValueFromData(rowData, "KOD_STR"),
                            OperationDescription = GetValueFromData(rowData, "NAME_STR"),
                            Amount = GetDecimalFromData(rowData, "SUMMA")
                        };

                        allRows.Add(displayRow);
                    }
                }
            }

            // Сортируем по дате
            allRows = allRows.OrderByDescending(r => r.DocumentDate).ThenBy(r => r.RowNumber).ToList();

            OperationsGrid.ItemsSource = allRows;
        }

        private string GetValueFromData(Dictionary<string, object> data, string key)
        {
            if (data.ContainsKey(key) && data[key] != null)
                return data[key].ToString();
            return "";
        }

        private decimal GetDecimalFromData(Dictionary<string, object> data, string key)
        {
            if (data.ContainsKey(key) && data[key] != null)
            {
                if (decimal.TryParse(data[key].ToString(), out decimal result))
                    return result;
            }
            return 0;
        }
    }

    // Класс для отображения в DataGrid
    public class DisplayRow
    {
        public string DocumentNumber { get; set; } = string.Empty;
        public System.DateTime DocumentDate { get; set; }
        public int RowNumber { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public string OperationDescription { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
