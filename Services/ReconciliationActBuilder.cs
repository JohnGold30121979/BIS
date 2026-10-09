using System.Data;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Строка движения по проводке для акта сверки.
    /// Вынесена из AccountingReportsView, чтобы построение таблицы можно было
    /// вызывать и из отчёта, и из диагностического инструмента.
    /// </summary>
    public sealed class ReconciliationMovementRow
    {
        public DateTime Date { get; init; }
        public Guid? OrganizationId { get; init; }
        public string OrganizationName { get; init; } = string.Empty;
        public string DocumentNumber { get; init; } = string.Empty;
        public string DocumentType { get; init; } = string.Empty;
        public string ModuleCode { get; init; } = string.Empty;
        public string DebitAccount { get; init; } = string.Empty;
        public string CreditAccount { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>
    /// Построение таблицы акта сверки: строки данных для макета FRX.
    ///
    /// Логика вынесена из AccountingReportsView в отдельный сервис, чтобы
    /// её можно было проверить диагностическим инструментом без запуска окна.
    /// </summary>
    public class ReconciliationActBuilder
    {
        private readonly AppDbContext _context;

        public ReconciliationActBuilder(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Колонки, которые нужны только механизму подстановки FRX-макета.
        /// В экранной таблице они скрываются при генерации колонок сетки.
        /// </summary>
        public static readonly HashSet<string> ServiceColumnNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "operation_name", "debit_account", "credit_account",
            "debit_amount", "credit_amount", "debit_begin", "credit_begin",
            "document_number", "document_date", "module",
            "report_title", "report_summary", "period_start", "period_end"
        };

        /// <summary>Создаёт таблицу акта сверки с колонками отчёта и служебными колонками макета.</summary>
        public static DataTable CreateTable()
        {
            var table = new DataTable("Акт сверки");
            table.Columns.Add("Наименование материала, вид операции", typeof(string));
            table.Columns.Add("Дебет", typeof(string));
            table.Columns.Add("Кредит", typeof(string));
            table.Columns.Add("Сумма Дт", typeof(decimal));
            table.Columns.Add("Сумма Кт", typeof(decimal));
            table.Columns.Add("N докум", typeof(string));
            table.Columns.Add("Дата", typeof(string));
            table.Columns.Add("Модуль", typeof(string));

            // Колонки под канонические имена макета: механизм подстановки ищет
            // значения именно в них, иначе в макете печатается текст выражения.
            AddAliasColumn(table, "operation_name", typeof(string));
            AddAliasColumn(table, "debit_account", typeof(string));
            AddAliasColumn(table, "credit_account", typeof(string));
            AddAliasColumn(table, "debit_amount", typeof(decimal));
            AddAliasColumn(table, "credit_amount", typeof(decimal));
            AddAliasColumn(table, "debit_begin", typeof(decimal));
            AddAliasColumn(table, "credit_begin", typeof(decimal));
            AddAliasColumn(table, "document_number", typeof(string));
            AddAliasColumn(table, "document_date", typeof(string));
            AddAliasColumn(table, "module", typeof(string));
            AddAliasColumn(table, "report_title", typeof(string));
            AddAliasColumn(table, "report_summary", typeof(string));
            AddAliasColumn(table, "period_start", typeof(string));
            AddAliasColumn(table, "period_end", typeof(string));
            return table;
        }

        private static void AddAliasColumn(DataTable table, string columnName, Type columnType)
        {
            if (!table.Columns.Contains(columnName))
                table.Columns.Add(columnName, columnType);
        }

        /// <summary>
        /// Загружает движения по проводкам за период. Фильтр по организации —
        /// необязательный: при пустом значении возвращаются все организации.
        /// </summary>
        public async Task<List<ReconciliationMovementRow>> LoadMovementsAsync(
            Guid? organizationId,
            DateTime start,
            DateTime end)
        {
            var rows = new List<ReconciliationMovementRow>();
            try
            {
                await _context.Database.ExecuteSqlRawAsync(@"
                    DO $$
                    BEGIN
                        IF to_regclass('public.doc_postings') IS NOT NULL THEN
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2) NOT NULL DEFAULT 0;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS organization_id uuid;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS description text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        END IF;
                    END $$;");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                return rows;
            }

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT p.posting_date, p.doc_number, COALESCE(p.document_type, 'Проводка') AS document_type,
                       COALESCE(p.module_code, '') AS module_code,
                       p.debit_account, p.credit_account, COALESCE(p.amount_kgs, 0) AS amount_kgs,
                       COALESCE(p.description, '') AS description,
                       CASE
                           WHEN COALESCE(p.organization_id::text, '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                           THEN p.organization_id::text
                           ELSE NULL
                       END AS organization_id_text,
                       COALESCE(NULLIF(o.""name"", ''), 'Без организации') AS organization_name
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON p.organization_id::text = o.""Id""::text
                WHERE COALESCE(p.is_active, true) = true
                  AND (COALESCE(@organizationId, '') = '' OR p.organization_id::text = @organizationId)
                  AND p.posting_date >= @startDate
                  AND p.posting_date < @endDateExclusive
                ORDER BY organization_name, p.posting_date, p.doc_number, p.debit_account, p.credit_account";
            command.Parameters.Add(new NpgsqlParameter("@organizationId",
                organizationId.HasValue && organizationId.Value != Guid.Empty ? organizationId.Value.ToString() : string.Empty));
            command.Parameters.Add(new NpgsqlParameter("@startDate", DateTime.SpecifyKind(start.Date, DateTimeKind.Utc)));
            command.Parameters.Add(new NpgsqlParameter("@endDateExclusive", DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc)));

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var postingDate = reader["posting_date"] is DateTime date
                        ? date
                        : Convert.ToDateTime(reader["posting_date"], CultureInfo.InvariantCulture);

                    rows.Add(new ReconciliationMovementRow
                    {
                        Date = postingDate,
                        OrganizationId = Guid.TryParse(reader["organization_id_text"]?.ToString(), out var movementOrganizationId)
                            ? movementOrganizationId
                            : null,
                        OrganizationName = reader["organization_name"]?.ToString() ?? "Без организации",
                        DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(reader["doc_number"]?.ToString()),
                        DocumentType = reader["document_type"]?.ToString() ?? string.Empty,
                        ModuleCode = reader["module_code"]?.ToString() ?? string.Empty,
                        DebitAccount = reader["debit_account"]?.ToString() ?? string.Empty,
                        CreditAccount = reader["credit_account"]?.ToString() ?? string.Empty,
                        Amount = ReadDecimal(reader["amount_kgs"]),
                        Description = reader["description"]?.ToString() ?? string.Empty
                    });
                }
            }
            catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703")
            {
                rows.Clear();
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return rows;
        }

        private static decimal ReadDecimal(object? value) =>
            value == null || value == DBNull.Value
                ? 0m
                : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

        /// <summary>
        /// Строит таблицу акта сверки. Возвращает таблицу и описание того,
        /// что в неё попало — для диагностики.
        /// </summary>
        public ReconciliationActTable BuildTable(
            IReadOnlyList<OrganizationBalanceRow> pairs,
            IReadOnlyList<OrganizationBalanceRow> totals,
            IReadOnlyList<ReconciliationMovementRow> movements,
            DateTime start,
            DateTime end,
            bool scoped,
            string titleName)
        {
            var table = CreateTable();
            var trace = new List<string>();

            AddRow(table, scoped
                ? $"АКТ СВЕРКИ между нашей организацией и \"{titleName}\""
                : "АКТЫ СВЕРКИ ПО ВСЕМ ОРГАНИЗАЦИЯМ");
            AddRow(table, $"Период: {start:dd/MM/yyyy} - {end:dd/MM/yyyy}");
            AddRow(table, string.Empty);

            if (pairs.Count == 0)
            {
                AddRow(table, scoped
                    ? "Данных по выбранной организации и активным парам счетов за период не найдено."
                    : "Данных по организациям и активным парам счетов за период не найдено.");
            }
            else if (scoped)
            {
                var total = totals.FirstOrDefault(row => MatchesSelectedOrganization(row, null, null));
                AddSection(table, titleName, pairs, total, movements, start, end, false);
                trace.Add($"режим: одна организация '{titleName}', пар {pairs.Count}");
            }
            else
            {
                var groups = pairs
                    .GroupBy(row => new { row.OrganizationId, Name = NormalizeOrganizationName(row.OrganizationName) })
                    .OrderBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var group in groups)
                {
                    var groupName = string.IsNullOrWhiteSpace(group.Key.Name) ? "Без организации" : group.Key.Name;
                    var total = totals.FirstOrDefault(row => SameOrganization(row, group.Key.OrganizationId, groupName));
                    var groupMovements = movements
                        .Where(movement => MatchesMovementOrganization(movement, group.Key.OrganizationId, groupName))
                        .ToList();

                    AddSection(table, groupName, group.ToList(), total, groupMovements, start, end, true);
                    AddRow(table, string.Empty);

                    trace.Add($"организация '{groupName}': пар {group.Count()}, " +
                              $"движений подходящих {groupMovements.Count()} из {movements.Count}");
                }

                trace.Insert(0, $"режим: все организации, групп {groups.Count}, пар всего {pairs.Count}, движений всего {movements.Count}");
            }

            var balance = scoped
                ? totals.FirstOrDefault(row => MatchesSelectedOrganization(row, null, null))?.Balance ?? pairs.Sum(row => row.Balance)
                : totals.Sum(row => row.Balance);
            var summary = scoped
                ? BuildDebtSummary(titleName, balance)
                : BuildAllOrganizationsDebtSummary(totals.Count, balance);
            AddRow(table, summary);
            ApplyHeaderFields(table, start, end, summary);

            trace.Add($"строк в таблице: {table.Rows.Count}");
            return new ReconciliationActTable(table, summary, trace);
        }

        private static void AddSection(
            DataTable table,
            string organizationTitle,
            IReadOnlyList<OrganizationBalanceRow> pairs,
            OrganizationBalanceRow? total,
            IReadOnlyList<ReconciliationMovementRow> movements,
            DateTime start,
            DateTime end,
            bool showOrganizationHeader)
        {
            if (showOrganizationHeader)
                AddRow(table, $"Организация: {organizationTitle}");

            var firstPair = true;
            foreach (var pair in pairs)
            {
                if (!firstPair)
                    AddRow(table, string.Empty);
                firstPair = false;

                var pairName = string.IsNullOrWhiteSpace(pair.AccountPairName) ? pair.CounterAccountName : pair.AccountPairName;
                AddRow(table, $"Пара счетов : {pair.AccountCode} - {pair.CounterAccountCode} ({pairName})");
                AddRow(table, $"САЛЬДО НА {start:dd/MM/yyyy}",
                    debitAmount: NonZeroAmount(pair.OpeningDebit),
                    creditAmount: NonZeroAmount(pair.OpeningCredit));

                var pairMovements = movements
                    .Where(movement => AccountInPair(movement.DebitAccount, pair) || AccountInPair(movement.CreditAccount, pair))
                    .OrderBy(movement => movement.Date)
                    .ThenBy(movement => movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (pairMovements.Count == 0 && (pair.TurnoverDebit != 0 || pair.TurnoverCredit != 0))
                    AddRow(table, "Движения найдены в остатках, но детализация проводок недоступна.");

                foreach (var movement in pairMovements)
                {
                    var movementDate = movement.Date == default ? end : movement.Date;
                    AddRow(table,
                        BuildMovementDescription(movement),
                        movement.DebitAccount,
                        movement.CreditAccount,
                        AccountInPair(movement.DebitAccount, pair) ? movement.Amount : null,
                        AccountInPair(movement.CreditAccount, pair) ? movement.Amount : null,
                        movement.DocumentNumber,
                        movementDate.ToString("dd/MM/yyyy"),
                        movement.ModuleCode);
                }

                AddRow(table, "ИТОГО ОБОРОТОВ",
                    debitAmount: NonZeroAmount(pair.TurnoverDebit),
                    creditAmount: NonZeroAmount(pair.TurnoverCredit));
                AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy}",
                    debitAmount: NonZeroAmount(pair.ClosingDebit),
                    creditAmount: NonZeroAmount(pair.ClosingCredit));
            }

            if (total != null && pairs.Count > 1)
            {
                AddRow(table, string.Empty);
                AddRow(table, "ИТОГО ОБОРОТОВ ПО ОРГАНИЗАЦИИ",
                    debitAmount: NonZeroAmount(total.TurnoverDebit),
                    creditAmount: NonZeroAmount(total.TurnoverCredit));
                AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy} ПО ОРГАНИЗАЦИИ",
                    debitAmount: NonZeroAmount(total.ClosingDebit),
                    creditAmount: NonZeroAmount(total.ClosingCredit));
            }
        }

        private static decimal? NonZeroAmount(decimal value) => value == 0m ? null : value;

        private static string BuildMovementDescription(ReconciliationMovementRow movement)
        {
            var description = string.IsNullOrWhiteSpace(movement.Description)
                ? (string.IsNullOrWhiteSpace(movement.DocumentType) ? "Проводка" : movement.DocumentType)
                : movement.Description;

            return ShortenText(description);
        }

        /// <summary>
        /// Сокращает длинное описание операции: убирает перечисление модулей
        /// и ограничивает длину, иначе текст не помещается в колонку.
        /// </summary>
        internal static string ShortenText(string? text, int maxLength = 70)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                return value;

            var separatorIndex = value.LastIndexOf(';');
            if (separatorIndex >= 0 && separatorIndex < value.Length - 1)
            {
                var tail = value[(separatorIndex + 1)..].Trim();
                if (tail.Length > 0)
                    value = tail;
            }

            return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";
        }

        /// <summary>Заголовок документа не должен попадать в колонку операций.</summary>
        internal static bool IsDocumentTitleText(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            return value.StartsWith("АКТ СВЕРКИ", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("АКТЫ СВЕРКИ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Добавляет строку таблицы. Служебные колонки заполняются каноническими
        /// именами: механизм подстановки FRX-макета ищет значения именно в них.
        /// </summary>
        internal static void AddRow(
            DataTable table,
            string operation,
            string debit = "",
            string credit = "",
            decimal? debitAmount = null,
            decimal? creditAmount = null,
            string documentNumber = "",
            string date = "",
            string moduleCode = "")
        {
            var row = table.NewRow();
            row["Наименование материала, вид операции"] = operation;
            row["Дебет"] = debit;
            row["Кредит"] = credit;
            row["Сумма Дт"] = debitAmount.HasValue ? debitAmount.Value : DBNull.Value;
            row["Сумма Кт"] = creditAmount.HasValue ? creditAmount.Value : DBNull.Value;
            row["N докум"] = documentNumber;
            row["Дата"] = date;
            row["Модуль"] = moduleCode;

            SetServiceValue(row, "operation_name", text: IsDocumentTitleText(operation) ? null : operation);
            SetServiceValue(row, "debit_account", text: debit);
            SetServiceValue(row, "credit_account", text: credit);
            SetServiceValue(row, "document_number", text: documentNumber);
            SetServiceValue(row, "document_date", text: date);
            SetServiceValue(row, "module", text: moduleCode);
            SetServiceValue(row, "debit_amount", amount: debitAmount);
            SetServiceValue(row, "credit_amount", amount: creditAmount);

            table.Rows.Add(row);
        }

        /// <summary>Записывает значение в служебную колонку; в числовую — только DBNull.</summary>
        internal static void SetServiceValue(DataRow row, string columnName, string? text = null, decimal? amount = null)
        {
            if (!row.Table.Columns.Contains(columnName))
                return;

            if (amount.HasValue)
            {
                row[columnName] = amount.Value;
                return;
            }

            var column = row.Table.Columns[columnName]!;
            if (IsNumericColumn(column))
            {
                row[columnName] = DBNull.Value;
                return;
            }

            row[columnName] = string.IsNullOrWhiteSpace(text) ? DBNull.Value : text;
        }

        private static bool IsNumericColumn(DataColumn column) =>
            column.DataType == typeof(decimal) ||
            column.DataType == typeof(double) ||
            column.DataType == typeof(float) ||
            column.DataType == typeof(int) ||
            column.DataType == typeof(long) ||
            column.DataType == typeof(short);

        /// <summary>Проставляет поля шапки макета во всех строках: их читает полоса Title.</summary>
        internal static void ApplyHeaderFields(DataTable table, DateTime start, DateTime end, string summary)
        {
            var periodStart = start.ToString("dd/MM/yyyy");
            var periodEnd = end.ToString("dd/MM/yyyy");

            foreach (DataRow row in table.Rows)
            {
                SetServiceValue(row, "report_title", text: "АКТ СВЕРКИ");
                SetServiceValue(row, "report_summary", text: summary);
                SetServiceValue(row, "period_start", text: periodStart);
                SetServiceValue(row, "period_end", text: periodEnd);
            }
        }

        public static bool AccountInPair(string accountCode, OrganizationBalanceRow pair) =>
            SameAccount(accountCode, pair.AccountCode) || SameAccount(accountCode, pair.CounterAccountCode);

        private static bool SameAccount(string left, string right) =>
            left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool SameOrganization(OrganizationBalanceRow row, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return row.OrganizationId.HasValue && row.OrganizationId.Value == organizationId.Value;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return rowName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool MatchesMovementOrganization(ReconciliationMovementRow movement, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return movement.OrganizationId.HasValue && movement.OrganizationId.Value == organizationId.Value;

            var movementName = NormalizeOrganizationName(movement.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return movementName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool MatchesSelectedOrganization(OrganizationBalanceRow row, Guid? selectedId, string? selectedName)
        {
            if (!selectedId.HasValue || selectedId.Value == Guid.Empty)
                return true;
            if (row.OrganizationId.HasValue && row.OrganizationId.Value == selectedId.Value)
                return true;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(selectedName);
            return !string.IsNullOrWhiteSpace(filterName) &&
                   rowName.Equals(filterName, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Отбрасывает префикс кода организации: «123 - Название» → «Название».</summary>
        public static string NormalizeOrganizationName(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex >= 0 ? text[(separatorIndex + 3)..].Trim() : text;
        }

        private static string BuildDebtSummary(string organizationName, decimal balance)
        {
            var direction = balance >= 0 ? "перед нами" : "за нами";
            return $"Задолженность \"{organizationName}\" {direction} составляет {Math.Abs(balance):N2}";
        }

        private static string BuildAllOrganizationsDebtSummary(int organizationCount, decimal balance)
        {
            var direction = balance >= 0 ? "перед нами" : "за нами";
            return $"Задолженность по {organizationCount} организациям {direction} составляет {Math.Abs(balance):N2}";
        }
    }

    /// <summary>Результат построения таблицы акта сверки вместе с диагностическим следом.</summary>
    public sealed record ReconciliationActTable(DataTable Table, string Summary, IReadOnlyList<string> Trace);
}
