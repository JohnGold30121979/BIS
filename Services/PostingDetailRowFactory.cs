namespace BIS.ERP.Services
{
    public static class PostingDetailRowFactory
    {
        private static readonly string[] StandardFields =
        [
            "Документ",
            "Тип документа",
            "Дата",
            "Модуль",
            "Дебет",
            "Кредит",
            "Сумма",
            "Сумма вал.",
            "Валюта",
            "Организация",
            "Сотрудник",
            "Материал",
            "Статус",
            "Примечание"
        ];

        public static Dictionary<string, object> Create(params (string Field, string? Value)[] values)
        {
            var row = StandardFields.ToDictionary(field => field, _ => (object)string.Empty);
            foreach (var (field, value) in values)
            {
                Set(row, field, value);
            }

            return row;
        }

        public static void Set(Dictionary<string, object> row, string field, string? value)
        {
            row[field] = string.IsNullOrWhiteSpace(value) ? string.Empty : value;
        }
    }
}
