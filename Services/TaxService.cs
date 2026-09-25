using System.Globalization;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Вид налога для каталога «Налоги» (колонка tax_kind).
    /// </summary>
    public enum TaxKind
    {
        /// <summary>Налог на добавленную стоимость.</summary>
        Vat,

        /// <summary>Налог с продаж (НСП).</summary>
        Sales
    }

    /// <summary>
    /// Запись каталога «Налоги», приведённая к типизированному виду.
    /// </summary>
    public sealed record TaxOption(
        string Code,
        string Name,
        decimal Rate,
        string TaxKindCode,
        DateTime? ValidFrom,
        DateTime? ValidTo,
        int SortOrder,
        bool IsActive,
        bool IsSystem,
        bool IsDefaultVat,
        bool IsDefaultSalesTax,
        string EsfVatCode,
        string EsfSalesTaxCode);

    /// <summary>
    /// Единая точка работы с каталогом налогов: вместо строковых констант вида «НДС12»
    /// и эвристик по наименованию налоги выбираются по виду (tax_kind) и дате документа.
    /// </summary>
    public sealed class TaxService
    {
        /// <summary>Имя каталога налогов.</summary>
        public const string CatalogName = "Налоги";

        private const string KindVat = "VAT";
        private const string KindSales = "SALES";
        private const string KindBoth = "BOTH";

        private readonly MetadataService _metadataService;

        public TaxService(MetadataService metadataService)
        {
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        }

        /// <summary>
        /// Возвращает все записи каталога «Налоги» без учёта вида и даты.
        /// </summary>
        public async Task<IReadOnlyList<TaxOption>> GetAllAsync()
        {
            var catalogs = await _metadataService.GetCatalogsAsync();
            var catalog = catalogs.FirstOrDefault(item =>
                item.Name.Equals(CatalogName, StringComparison.OrdinalIgnoreCase));

            if (catalog == null)
                return Array.Empty<TaxOption>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);

            return rows
                .Select(ToOption)
                .Where(option => !string.IsNullOrWhiteSpace(option.Code))
                .OrderBy(option => option.SortOrder)
                .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Возвращает активные налоги указанного вида, действующие на дату документа.
        /// </summary>
        /// <remarks>
        /// Если на дату документа не действует ни одна запись (например, документ старше
        /// первой введённой ставки), возвращаются все активные налоги вида — иначе нельзя
        /// было бы редактировать ранее введённые документы.
        /// </remarks>
        public async Task<IReadOnlyList<TaxOption>> GetTaxesAsync(TaxKind kind, DateTime documentDate)
        {
            var all = await GetAllAsync();
            var byKind = all
                .Where(option => option.IsActive && AppliesToKind(option, kind))
                .ToList();

            var effective = byKind
                .Where(option => IsEffective(option, documentDate))
                .ToList();

            return effective.Count > 0 ? effective : byKind;
        }

        /// <summary>
        /// Налог по умолчанию для указанного вида: по флагу «По умолчанию», иначе первый в списке.
        /// </summary>
        public async Task<TaxOption?> GetDefaultAsync(TaxKind kind, DateTime documentDate)
        {
            var taxes = await GetTaxesAsync(kind, documentDate);
            var flagged = kind == TaxKind.Vat
                ? taxes.FirstOrDefault(option => option.IsDefaultVat)
                : taxes.FirstOrDefault(option => option.IsDefaultSalesTax);

            return flagged ?? taxes.FirstOrDefault();
        }

        /// <summary>
        /// Сопоставляет код налога записи каталога. Дата и активность учитываются только для
        /// выбора подходящей записи: уже сохранённый в документе налог находится даже тогда,
        /// когда его срок действия на дату документа истёк.
        /// </summary>
        public async Task<TaxOption?> ResolveAsync(string? code, TaxKind kind, DateTime documentDate)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var all = await GetAllAsync();
            var normalized = code.Trim();
            var matches = all
                .Where(option => option.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return null;

            return matches.FirstOrDefault(option =>
                       option.IsActive && AppliesToKind(option, kind) && IsEffective(option, documentDate))
                   ?? matches.FirstOrDefault(option => AppliesToKind(option, kind))
                   ?? matches[0];
        }

        /// <summary>
        /// Проверяет, относится ли налог к указанному виду.
        /// Вид налога (tax_kind) может быть введён пользователем вручную, поэтому кроме
        /// значений VAT / SALES / BOTH принимаются слова «НДС», «НСП», «налог с продаж» и т. п.
        /// Запись, вид которой определить не удалось, показывается в обоих списках —
        /// иначе введённый пользователем налог просто исчез бы из формы счёта-фактуры.
        /// </summary>
        public static bool AppliesToKind(TaxOption option, TaxKind kind)
        {
            var rawKind = (option.TaxKindCode ?? string.Empty).Trim().ToUpperInvariant();

            switch (rawKind)
            {
                case KindVat:
                    return kind == TaxKind.Vat;
                case KindSales:
                    return kind == TaxKind.Sales;
                case KindBoth:
                    return true;
                case "ОБА":
                case "ВСЕ":
                case "ALL":
                case "ANY":
                    return true;
            }

            var namesVat = StartsWithAny(rawKind, "VAT", "NDS", "НДС");
            var namesSales = StartsWithAny(rawKind, "SALES", "НСП", "ПРОДАЖ") ||
                             rawKind.Contains("НСП", StringComparison.Ordinal);

            if (namesVat || namesSales)
                return namesVat && kind == TaxKind.Vat || namesSales && kind == TaxKind.Sales;

            // Вид налога не задан — определяем по коду и наименованию (записи прежних версий).
            if (string.IsNullOrWhiteSpace(rawKind) &&
                TryMatchLegacyKind(option, out var legacyVat, out var legacySales))
            {
                return kind == TaxKind.Vat ? legacyVat : legacySales;
            }

            return true;
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Прежнее правило разделения налогов: по префиксу кода и наименованию.
        /// Возвращает false, если по коду и наименованию вид определить нельзя.
        /// </summary>
        private static bool TryMatchLegacyKind(TaxOption option, out bool appliesVat, out bool appliesSales)
        {
            appliesVat = false;
            appliesSales = false;

            var code = option.Code ?? string.Empty;
            var name = option.Name ?? string.Empty;

            // «Без НДС / освобождено» присутствует и в списке НДС, и в списке НСП.
            if (code.Equals("WITHOUT_TAX", StringComparison.OrdinalIgnoreCase))
            {
                appliesVat = true;
                appliesSales = true;
                return true;
            }

            var normalizedCode = code.ToUpperInvariant();

            appliesVat = StartsWithAny(normalizedCode, "НДС", "VAT", "NDS") ||
                         name.Contains("НДС", StringComparison.OrdinalIgnoreCase);

            appliesSales = StartsWithAny(normalizedCode, "SALES", "НСП") ||
                           name.Contains("продаж", StringComparison.OrdinalIgnoreCase) ||
                           name.Contains("НСП", StringComparison.OrdinalIgnoreCase);

            return appliesVat || appliesSales;
        }

        /// <summary>
        /// Действует ли ставка на указанную дату (колонки valid_from / valid_to).
        /// </summary>
        public static bool IsEffective(TaxOption option, DateTime documentDate)
        {
            var date = documentDate.Date;

            if (option.ValidFrom.HasValue && option.ValidFrom.Value.Date > date)
                return false;

            if (option.ValidTo.HasValue && option.ValidTo.Value.Date < date)
                return false;

            return true;
        }

        private static TaxOption ToOption(Dictionary<string, object> row)
        {
            return new TaxOption(
                Code: GetRowValue(row, "Код", "code"),
                Name: GetRowValue(row, "Наименование", "name"),
                Rate: GetDecimal(row, "Ставка", "rate"),
                TaxKindCode: GetRowValue(row, "Вид налога", "tax_kind"),
                ValidFrom: GetDate(row, "Действует с", "valid_from"),
                ValidTo: GetDate(row, "Действует по", "valid_to"),
                SortOrder: GetInt(row, "Порядок", "sort_order") ?? int.MaxValue,
                IsActive: IsActiveRow(row),
                IsSystem: GetBool(row, "Служебная запись", "is_system"),
                IsDefaultVat: GetBool(row, "По умолчанию для НДС", "is_default_vat"),
                IsDefaultSalesTax: GetBool(row, "По умолчанию для налога с продаж", "is_default_sales_tax"),
                EsfVatCode: GetRowValue(row, "Код ЭСФ НДС", "esf_vat_code"),
                EsfSalesTaxCode: GetRowValue(row, "Код ЭСФ НСП", "esf_sales_tax_code"));
        }

        private static string GetRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static decimal GetDecimal(Dictionary<string, object> row, params string[] keys)
        {
            var text = GetRowValue(row, keys);

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
                return value;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                ? value
                : 0m;
        }

        private static int? GetInt(Dictionary<string, object> row, params string[] keys)
        {
            return int.TryParse(GetRowValue(row, keys), out var value) ? value : null;
        }

        private static DateTime? GetDate(Dictionary<string, object> row, params string[] keys)
        {
            var text = GetRowValue(row, keys);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var value))
                return value;

            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
                ? value
                : null;
        }

        private static bool GetBool(Dictionary<string, object> row, params string[] keys)
        {
            var value = GetRowValue(row, keys);
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsActiveRow(Dictionary<string, object> row)
        {
            var value = GetRowValue(row, "Активен", "is_active");
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
