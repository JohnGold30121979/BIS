using System;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Строка списка выбора значения параметра типа «Ссылка».
    /// Хранит GUID элемента справочника и его подпись.
    /// </summary>
    public sealed class ReferenceLookupItem
    {
        public ReferenceLookupItem(string id, string display, string searchText = "")
        {
            Id = id ?? string.Empty;
            Display = string.IsNullOrWhiteSpace(display) ? Id : display;
            SearchText = searchText ?? string.Empty;
        }

        public string Id { get; }

        public string Display { get; }

        /// <summary>Дополнительный текст для поиска (код, ИНН и т. п.).</summary>
        public string SearchText { get; }

        /// <summary>Формат для сохранения в значении параметра: "Название|GUID".</summary>
        public string ToStoredValue() => string.IsNullOrWhiteSpace(Display)
            ? Id
            : $"{Display}|{Id}";

        /// <summary>
        /// Разбирает сохранённое значение "Название|GUID".
        /// Поддерживает и старый формат — просто GUID.
        /// </summary>
        public static bool TryParseStored(string? stored, out string id, out string display)
        {
            id = string.Empty;
            display = string.Empty;

            if (string.IsNullOrWhiteSpace(stored))
                return false;

            var text = stored.Trim();
            var separatorIndex = text.LastIndexOf('|');

            if (separatorIndex > 0 && separatorIndex < text.Length - 1)
            {
                var candidateId = text[(separatorIndex + 1)..].Trim();
                if (Guid.TryParse(candidateId, out var parsed))
                {
                    id = parsed.ToString();
                    display = text[..separatorIndex].Trim();
                    return true;
                }
            }

            if (Guid.TryParse(text, out var directId))
            {
                id = directId.ToString();
                display = string.Empty;
                return true;
            }

            return false;
        }

        /// <summary>Только GUID — то, что уходит в фильтр отчёта.</summary>
        public static string ExtractId(string? storedValue) =>
            TryParseStored(storedValue, out var id, out _) ? id : (storedValue ?? string.Empty).Trim();

        /// <summary>Текст для поля ввода параметра: название и GUID рядом.</summary>
        public static string BuildDisplayText(string? storedValue)
        {
            if (!TryParseStored(storedValue, out var id, out var display))
                return storedValue ?? string.Empty;

            return string.IsNullOrWhiteSpace(display)
                ? id
                : $"{display}  ({id})";
        }
    }
}
