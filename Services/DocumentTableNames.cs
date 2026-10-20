using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Канон мнемонических имён таблиц документов модулей «Основные средства» и «Материалы» (ТМЦ).
    ///
    /// Правила:
    /// <list type="bullet">
    /// <item>шапка документа ОС — <c>doc_asset_&lt;смысл&gt;</c>;</item>
    /// <item>шапка документа ТМЦ — <c>doc_inventory_&lt;смысл&gt;</c>;</item>
    /// <item>строки документа — <c>&lt;имя шапки&gt;_lines</c>;</item>
    /// <item>хеш-имена (<c>doc_asset_&lt;12 hex&gt;</c>, <c>doc_inventory_&lt;12 hex&gt;</c>) запрещены
    /// (Docs/План_нормализации_модели_данных.md §0.3).</item>
    /// </list>
    ///
    /// Ключ — имя документа (<c>MetadataObjects.Name</c>), а не таблица: по имени
    /// <see cref="DocumentTableNameNormalizer"/> находит legacy-таблицу и переименовывает её.
    /// </summary>
    public static class DocumentTableNames
    {
        // ---------- Основные средства ----------

        public const string AssetMovement = "doc_asset_movement";
        public const string AssetPurchase = "doc_asset_purchase";
        public const string AssetCommissioning = "doc_asset_commissioning";
        public const string AssetProductionReceipt = "doc_asset_production_receipt";
        public const string AssetRevaluation = "doc_asset_revaluation";
        public const string AssetSale = "doc_asset_sale";
        public const string AssetPartialSale = "doc_asset_partial_sale";
        public const string AssetLiquidation = "doc_asset_liquidation";
        public const string AssetCompletion = "doc_asset_completion";
        public const string AssetDecompletion = "doc_asset_decompletion";
        public const string AssetDepreciation = "doc_asset_depreciation";
        public const string AssetDepreciationWriteOff = "doc_asset_depreciation_writeoff";
        public const string AssetConservation = "doc_asset_conservation";
        public const string AssetReopening = "doc_asset_reopening";
        public const string AssetAssignment = "doc_asset_assignment";
        public const string AssetExpenseAccountChange = "doc_asset_expense_account_change";
        public const string AssetGroupChange = "doc_asset_group_change";

        /// <summary>Табличная часть документа «Учет движения ОС».</summary>
        public const string AssetMovementLines = "doc_asset_movement_lines";

        // ---------- Товарно-материальные запасы (ТМЗ) ----------
        // Три документа вместо таблицы на каждый вид операции (решение 20.10.2026):
        // вид операции — реквизит шапки, строки — в отдельной таблице.

        public const string InventoryIncoming = "doc_inventory_incoming";
        public const string InventoryOutgoing = "doc_inventory_outgoing";
        public const string InventoryMovement = "doc_inventory_movement";

        public const string InventoryIncomingLines = "doc_inventory_incoming_lines";
        public const string InventoryOutgoingLines = "doc_inventory_outgoing_lines";
        public const string InventoryMovementLines = "doc_inventory_movement_lines";

        // ---------- Карты «имя документа -> таблица» ----------

        private static readonly (string DocumentName, string TableName)[] FixedAssetDocumentMap =
        {
            ("Учет движения ОС", AssetMovement),
            ("Покупка ОС", AssetPurchase),
            ("Ввод ОС в эксплуатацию", AssetCommissioning),
            ("Приход из производства ОС", AssetProductionReceipt),
            ("Переоценка ОС", AssetRevaluation),
            ("Реализация ОС", AssetSale),
            ("Частичная реализация ОС", AssetPartialSale),
            ("Ликвидация ОС", AssetLiquidation),
            ("Укомплектация ОС", AssetCompletion),
            ("Разукомплектация ОС", AssetDecompletion),
            ("Начисление амортизации", AssetDepreciation),
            ("Списание амортизации", AssetDepreciationWriteOff),
            ("Консервация ОС", AssetConservation),
            ("Расконсервация ОС", AssetReopening),
            ("Передача ОС в подотчет", AssetAssignment),
            ("Смена затратного счета", AssetExpenseAccountChange),
            ("Смена группы ОС", AssetGroupChange)
        };

        private static readonly (string DocumentName, string TableName)[] InventoryDocumentMap =
        {
            ("Поступление ТМЗ", InventoryIncoming),
            ("Выбытие ТМЗ", InventoryOutgoing),
            ("Внутреннее перемещение ТМЗ", InventoryMovement)
        };

        private static readonly Dictionary<string, string> CanonicalTables = BuildLookup(
            FixedAssetDocumentMap,
            InventoryDocumentMap);

        private static readonly Regex LegacyHashedName = new(
            "^doc_(asset|inventory)_[0-9a-f]{12}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Имена документов ОС в порядке справочника «Ввод нового документа ОС».</summary>
        public static IReadOnlyList<string> FixedAssetDocumentNames { get; } =
            FixedAssetDocumentMap.Select(item => item.DocumentName).ToArray();

        /// <summary>Имена документов ТМЦ.</summary>
        public static IReadOnlyList<string> InventoryDocumentNames { get; } =
            InventoryDocumentMap.Select(item => item.DocumentName).ToArray();

        /// <summary>Все канонические имена таблиц шапок и строк.</summary>
        public static IReadOnlyList<string> AllCanonicalTableNames { get; } =
            FixedAssetDocumentMap.Select(item => item.TableName)
                .Concat(InventoryDocumentMap.Select(item => item.TableName))
                .Append(AssetMovementLines)
                .Append(InventoryIncomingLines)
                .Append(InventoryOutgoingLines)
                .Append(InventoryMovementLines)
                .ToArray();

        /// <summary>Возвращает каноническую таблицу по имени документа.</summary>
        public static bool TryGetCanonicalTableName(string? documentName, out string tableName)
        {
            if (!string.IsNullOrWhiteSpace(documentName) &&
                CanonicalTables.TryGetValue(documentName.Trim(), out var found))
            {
                tableName = found;
                return true;
            }

            tableName = string.Empty;
            return false;
        }

        /// <summary>Каноническая таблица или <c>null</c>, если документ не из ОС/ТМЦ.</summary>
        public static string? GetCanonicalTableName(string? documentName) =>
            TryGetCanonicalTableName(documentName, out var tableName) ? tableName : null;

        /// <summary>Относится ли документ к модулям ОС или ТМЦ.</summary>
        public static bool IsKnownDocument(string? documentName) =>
            TryGetCanonicalTableName(documentName, out _);

        /// <summary>Хеш-имя legacy-таблицы: <c>doc_asset_&lt;12 hex&gt;</c> или <c>doc_inventory_&lt;12 hex&gt;</c>.</summary>
        public static bool IsLegacyHashedTableName(string? tableName) =>
            !string.IsNullOrWhiteSpace(tableName) && LegacyHashedName.IsMatch(tableName.Trim());

        /// <summary>Совпадает ли имя таблицы с одним из канонических.</summary>
        public static bool IsCanonicalTableName(string? tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            var candidate = tableName.Trim();
            return AllCanonicalTableNames.Any(name =>
                string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string> BuildLookup(
            params (string DocumentName, string TableName)[][] maps)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in maps)
            {
                foreach (var item in map)
                {
                    if (!result.ContainsKey(item.DocumentName))
                        result[item.DocumentName] = item.TableName;
                }
            }

            return result;
        }
    }
}
