using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Детектор дрейфа схемы (план §2.2, п. 5).
    ///
    /// Раньше сервис молча правил старые базы через ALTER TABLE. Теперь он только
    /// сравнивает ожидаемые колонки с фактическими в information_schema и пишет
    /// расхождения в лог: чинить схему должна миграция, а не старт приложения.
    /// </summary>
    public sealed class RuntimeSchemaFixService
    {
        private readonly AppDbContext _context;
        private static readonly object SyncLock = new();
        private static readonly HashSet<string> ReportedConnections = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSchemaFixService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Ожидаемые колонки: перенесены из прежнего DDL один в один.
        /// </summary>
        private static readonly (string Table, string Column)[] ExpectedColumns =
        {
            ("Reports", "Code"),
            ("Reports", "IsActive"),
            ("Reports", "IsPrintForm"),
            ("Reports", "IsDefault"),
            ("Reports", "IsSystem"),
            ("Reports", "SourceFormat"),
            ("Reports", "TemplateVersion"),
            ("Reports", "ClosedXmlLayout"),
            ("Reports", "ShowInAccountingReports"),
            ("LocalizationEntries", "Category"),
            ("LocalizationEntries", "IsActive"),
            ("LocalizationEntries", "UpdatedAt"),
            ("MetadataObjects", "UsePostings"),
            ("MetadataObjects", "UseBalances"),
            ("MetadataObjects", "UseMovements"),
            ("MetadataObjects", "BalanceTable"),
            ("MetadataObjects", "MovementTable"),
            ("MetadataObjects", "ReferenceFields"),
            ("MetadataFields", "ReferenceCatalog"),
            ("MetadataFields", "Formula"),
            ("MetadataFields", "DisplayPattern"),
            ("MetadataFields", "DisplayFields"),
            ("catalog_taxes", "is_active"),
            ("catalog_taxes", "sort_order"),
            ("catalog_taxes", "esf_vat_code"),
            ("catalog_taxes", "esf_sales_tax_code"),
            ("catalog_taxes", "is_default_vat"),
            ("catalog_taxes", "is_default_sales_tax"),
            ("catalog_taxes", "vat_payable_account"),
            ("catalog_taxes", "vat_recoverable_account"),
            ("catalog_taxes", "sales_tax_account"),
            ("catalog_taxes", "tax_kind"),
            ("catalog_taxes", "valid_from"),
            ("catalog_taxes", "valid_to"),
            ("catalog_taxes", "is_system"),
            ("catalog_payment_kinds", "is_active"),
            ("catalog_payment_kinds", "rate"),
            ("catalog_payment_kinds", "esf_code"),
            ("catalog_payment_kinds", "is_default"),
            ("catalog_supply_kinds", "is_active"),
            ("catalog_supply_kinds", "sort_order"),
            ("catalog_supply_kinds", "esf_code"),
            ("catalog_supply_kinds", "is_default"),
            ("catalog_delivery_types", "is_active"),
            ("catalog_delivery_types", "sort_order"),
            ("catalog_delivery_types", "esf_code"),
            ("catalog_delivery_types", "is_default"),
        };

        private const int MinFreeTextLength = 80;

        public async Task EnsureAsync()
        {
            var connectionKey = _context.Database.GetConnectionString() ?? "default";
            lock (SyncLock)
            {
                if (ReportedConnections.Contains(connectionKey))
                    return;
            }

            // Единый механизм миграций: журнал schema_migrations и шаги
            // (колонки EF, колонки метаданных, схемы касс/наборов, разовые чистки).
            try
            {
                await new SchemaMigrationService(_context).EnsureAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[migration] миграции не выполнены: {ex.Message}");
            }


            lock (SyncLock)
            {
                ReportedConnections.Add(connectionKey);
            }
        }

        /// <summary>
        /// Сверяет ожидаемые колонки с information_schema. Ничего не меняет.
        /// </summary>
        private async Task DetectDriftAsync()
        {
            var actual = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var shortColumns = new List<string>();

            var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT table_name, column_name, character_maximum_length
                    FROM information_schema.columns
                    WHERE table_schema = 'public'";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var tableName = reader.GetString(0);
                    var columnName = reader.GetString(1);

                    if (!actual.TryGetValue(tableName, out var columns))
                    {
                        columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        actual[tableName] = columns;
                    }

                    columns.Add(columnName);

                    if (!reader.IsDBNull(2) && reader.GetInt32(2) <= 10)
                        shortColumns.Add($"{tableName}.{columnName} ({reader.GetInt32(2)})");
                }
            }

            var missing = new List<string>();
            foreach (var (table, column) in ExpectedColumns)
            {
                // Отсутствие самой таблицы — не дрейф: её может ещё не быть в этой базе.
                if (!actual.TryGetValue(table, out var columns))
                    continue;

                if (!columns.Contains(column))
                    missing.Add($"{table}.{column}");
            }

            if (missing.Count == 0 && shortColumns.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[schema] дрейфа не обнаружено");
                return;
            }

            foreach (var item in missing)
                System.Diagnostics.Debug.WriteLine($"[schema drift] нет колонки: {item}");

            foreach (var item in shortColumns)
                System.Diagnostics.Debug.WriteLine($"[schema drift] короткая строка (<= 10): {item}");

            System.Diagnostics.Debug.WriteLine(
                $"[schema drift] всего расхождений: {missing.Count + shortColumns.Count} (чинится миграцией, не в рантайме)");
        }

        /// <summary>
        /// Отчёты, которые сразу показываются в окне «Бухгалтерская отчётность».
        ///
        /// Это отчёты с готовым визуальным макетом ClosedXML.
        /// </summary>
    }
}
