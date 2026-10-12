using System.Data;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    /// <summary>Сторона счёта, по которой отбираются проводки оборотной ведомости.</summary>
    public enum TrialBalanceSide
    {
        Debit,
        Credit
    }

    /// <summary>
    /// Ограничение по организации для оборотной ведомости.
    /// Нужно, чтобы сервис не зависел от элементов интерфейса.
    /// </summary>
    public sealed record TrialBalanceOrganizationScope(
        Guid? Id,
        string? Name = null,
        string? DisplayName = null,
        string? FullName = null,
        string? Code = null)
    {
        /// <summary>Признак «все организации» — без ограничения.</summary>
        public bool IsAll =>
            (!Id.HasValue || Id.Value == Guid.Empty) &&
            string.IsNullOrWhiteSpace(Name) &&
            string.IsNullOrWhiteSpace(DisplayName);
    }

    /// <summary>
    /// Запрос на построение оборотной ведомости.
    /// </summary>
    public sealed record TrialBalanceRequest(
        string AccountCode,
        DateTime Start,
        DateTime End,
        TrialBalanceSide Side,
        TrialBalanceOrganizationScope? Organization = null);

    /// <summary>
    /// Результат построения: таблица и краткое описание для шапки отчёта.
    /// </summary>
    public sealed record TrialBalanceTable(
        DataTable Table,
        string Summary,
        IReadOnlyList<TurnoverBalance> Balances);

    /// <summary>
    /// Построение оборотно-сальдовой ведомости по счёту.
    ///
    /// Логика вынесена из AccountingReportsView в отдельный сервис — так же,
    /// как построение акта сверки в <see cref="ReconciliationActBuilder"/>.
    /// Это позволяет проверять ведомость диагностическим инструментом
    /// без запуска окна и использовать один код во всех местах вывода.
    ///
    /// Ведомость строится по выбранной стороне счёта: если выбрана сторона
    /// дебета, в ведомость попадают проводки, где выбранный счёт стоит в дебете,
    /// и далее раскрываются все счета, затронутые этими проводками.
    /// </summary>
    public class TrialBalanceBuilder
    {
        private readonly AppDbContext _context;
        private readonly PostingService _postingService;

        /// <summary>Кэш имён счетов: план счетов меняется редко.</summary>
        private Dictionary<string, string>? _accountNames;

        public TrialBalanceBuilder(AppDbContext context)
        {
            _context = context;
            _postingService = new PostingService(context);
        }

        /// <summary>
        /// Колонки, которые нужны только механизму подстановки макета.
        /// В экранной таблице они скрываются при генерации колонок сетки.
        /// </summary>
        public static readonly HashSet<string> ServiceColumnNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "account_code", "account_name",
            "opening_debit", "opening_credit",
            "turnover_debit", "turnover_credit",
            "closing_debit", "closing_credit",
            "report_title", "report_summary", "period_start", "period_end"
        };

        /// <summary>
        /// Колонки ведомости: счёт, наименование, сальдо на начало,
        /// обороты и сальдо на конец.
        /// </summary>
        public static readonly string[] NumericColumnNames =
        {
            "Сальдо на начало Дт",
            "Сальдо на начало Кт",
            "Оборот Дт",
            "Оборот Кт",
            "Сальдо на конец Дт",
            "Сальдо на конец Кт"
        };

        /// <summary>Создаёт таблицу ведомости с колонками отчёта и служебными колонками макета.</summary>
        public static DataTable CreateTable()
        {
            var table = new DataTable("Оборотно-сальдовая ведомость");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            foreach (var name in NumericColumnNames)
                table.Columns.Add(name, typeof(decimal));

            // Колонки под канонические имена макета.
            AddAliasColumn(table, "account_code", typeof(string));
            AddAliasColumn(table, "account_name", typeof(string));
            AddAliasColumn(table, "opening_debit", typeof(decimal));
            AddAliasColumn(table, "opening_credit", typeof(decimal));
            AddAliasColumn(table, "turnover_debit", typeof(decimal));
            AddAliasColumn(table, "turnover_credit", typeof(decimal));
            AddAliasColumn(table, "closing_debit", typeof(decimal));
            AddAliasColumn(table, "closing_credit", typeof(decimal));
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
        /// Загружает проводки за период и рассчитывает обороты по счетам.
        ///
        /// Сальдо на начало считается по всем проводкам до начала периода,
        /// обороты — только по проводкам периода, где выбранный счёт стоит
        /// на выбранной стороне.
        /// </summary>
        public async Task<List<TurnoverBalance>> LoadBalancesAsync(TrialBalanceRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var accountCode = request.AccountCode?.Trim() ?? string.Empty;
            if (accountCode.Length == 0)
                return new List<TurnoverBalance>();

            var periodStart = request.Start.Date;
            var periodEndExclusive = request.End.Date.AddDays(1);

            var postings = (await _postingService.GetAllPostingsAsync(null, periodEndExclusive.AddTicks(-1)))
                .Where(posting => posting.Date < periodEndExclusive)
                .Where(posting => MatchesOrganization(posting, request.Organization))
                .ToList();

            var periodPostings = postings
                .Where(posting => posting.Date >= periodStart && AccountMatchesSide(posting, accountCode, request.Side))
                .ToList();

            var accountNames = await LoadAccountNamesAsync();
            var accountCodes = periodPostings
                .SelectMany(posting => new[] { posting.DebitAccount, posting.CreditAccount })
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return accountCodes.Select(code =>
            {
                // Сальдо на начало — по всем проводкам до периода.
                var openingDebit = postings
                    .Where(posting => posting.Date < periodStart && SameAccount(posting.DebitAccount, code))
                    .Sum(posting => posting.Amount);
                var openingCredit = postings
                    .Where(posting => posting.Date < periodStart && SameAccount(posting.CreditAccount, code))
                    .Sum(posting => posting.Amount);

                var turnoverDebit = periodPostings
                    .Where(posting => SameAccount(posting.DebitAccount, code))
                    .Sum(posting => posting.Amount);
                var turnoverCredit = periodPostings
                    .Where(posting => SameAccount(posting.CreditAccount, code))
                    .Sum(posting => posting.Amount);

                // Сальдо сворачивается: в одной колонке дебет, в другой кредит.
                var openingNet = openingDebit - openingCredit;
                var closingNet = openingNet + turnoverDebit - turnoverCredit;

                return new TurnoverBalance
                {
                    AccountCode = code,
                    AccountName = accountNames.TryGetValue(code, out var accountName) ? accountName : code,
                    OpeningDebit = Math.Max(openingNet, 0),
                    OpeningCredit = Math.Max(-openingNet, 0),
                    TurnoverDebit = turnoverDebit,
                    TurnoverCredit = turnoverCredit,
                    ClosingDebit = Math.Max(closingNet, 0),
                    ClosingCredit = Math.Max(-closingNet, 0)
                };
            }).ToList();
        }

        /// <summary>
        /// Заполняет заголовочные колонки отчёта: наименование и период.
        ///
        /// Значения ставятся только в первые две строки. Остальные строки — данные
        /// ведомости, и если заполнить заголовок во всех строках, первая колонка
        /// таблицы будет повторять его вместо кода счёта.
        /// </summary>
        private static void FillReportServiceColumns(DataTable table, TrialBalanceRequest request)
        {
            if (table.Rows.Count == 0)
                return;

            var periodStart = request.Start.ToString("dd.MM.yyyy");
            var periodEnd = request.End.ToString("dd.MM.yyyy");
            var sideTitle = request.Side == TrialBalanceSide.Credit ? "кредиту" : "дебету";
            var title = $"Оборотно-сальдовая ведомость по {sideTitle} счета {request.AccountCode} за {periodStart} - {periodEnd}";

            // Заголовок ведомости — строка 1 набора, период — строка 2.
            var titleRow = table.Rows[0];
            titleRow["report_title"] = title;
            titleRow["period_start"] = periodStart;
            titleRow["period_end"] = periodEnd;

            if (table.Rows.Count > 1)
                table.Rows[1]["report_title"] = $"Период: {periodStart} - {periodEnd}";
        }

        /// <summary>
        /// Строит таблицу ведомости: строки со счетами и строку итогов.
        /// В ведомость попадают только счета с оборотами.
        /// </summary>
        public async Task<TrialBalanceTable> BuildTableAsync(TrialBalanceRequest request)
        {
            var balances = (await LoadBalancesAsync(request))
                .Where(HasTurnover)
                .OrderBy(item => item.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var table = CreateTable();
            foreach (var item in balances)
            {
                var row = table.NewRow();
                row["Счет"] = item.AccountCode;
                row["Наименование"] = item.AccountName;
                row["Сальдо на начало Дт"] = item.OpeningDebit;
                row["Сальдо на начало Кт"] = item.OpeningCredit;
                row["Оборот Дт"] = item.TurnoverDebit;
                row["Оборот Кт"] = item.TurnoverCredit;
                row["Сальдо на конец Дт"] = item.ClosingDebit;
                row["Сальдо на конец Кт"] = item.ClosingCredit;

                // Служебные колонки: механизм подстановки макета ищет значения
                // именно в них, иначе в макете печатается текст выражения.
                row["account_code"] = item.AccountCode;
                row["account_name"] = item.AccountName;
                row["opening_debit"] = item.OpeningDebit;
                row["opening_credit"] = item.OpeningCredit;
                row["turnover_debit"] = item.TurnoverDebit;
                row["turnover_credit"] = item.TurnoverCredit;
                row["closing_debit"] = item.ClosingDebit;
                row["closing_credit"] = item.ClosingCredit;

                table.Rows.Add(row);
            }

            AddTotalsRow(table, 2);
            FillReportServiceColumns(table, request);

            var summary = balances.Count == 0
                ? "Оборотов по выбранному счёту за период нет."
                : $"Счетов в ведомости: {balances.Count}. " +
                  $"Оборот Дт: {balances.Sum(item => item.TurnoverDebit):N2}, " +
                  $"Оборот Кт: {balances.Sum(item => item.TurnoverCredit):N2}.";

            foreach (DataRow row in table.Rows)
                row["report_summary"] = summary;

            return new TrialBalanceTable(table, summary, balances);
        }

        /// <summary>
        /// Загружает имена счетов из плана счетов.
        /// При ошибке возвращает пустой словарь: ведомость построится по кодам.
        /// </summary>
        public async Task<Dictionary<string, string>> LoadAccountNamesAsync()
        {
            if (_accountNames != null)
                return _accountNames;

            try
            {
                var rows = await LoadAccountSelectionDataAsync();
                _accountNames = rows
                    .Select(row => new
                    {
                        Code = ReadString(row, "Код", "code", "Code", "Счет", "account_code"),
                        Name = ReadString(row, "Наименование", "name", "Name")
                    })
                    .Where(account => !string.IsNullOrWhiteSpace(account.Code))
                    .GroupBy(account => account.Code, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => string.IsNullOrWhiteSpace(group.First().Name) ? group.Key : group.First().Name,
                        StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки плана счетов для ОСВ: {ex.Message}");
                _accountNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return _accountNames;
        }

        /// <summary>Строки плана счетов — для выбора счёта в интерфейсе.</summary>
        public async Task<List<Dictionary<string, object>>> LoadAccountSelectionDataAsync()
        {
            var metadata = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));

            return metadata == null
                ? new List<Dictionary<string, object>>()
                : await new MetadataService(_context).GetCatalogDataAsync(metadata.Id);
        }

        /// <summary>Проводка относится к выбранной стороне счёта.</summary>
        public static bool AccountMatchesSide(PostingViewModel posting, string accountCode, TrialBalanceSide side) =>
            side == TrialBalanceSide.Credit
                ? SameAccount(posting.CreditAccount, accountCode)
                : SameAccount(posting.DebitAccount, accountCode);

        /// <summary>В ведомость попадают только счета с оборотами.</summary>
        public static bool HasTurnover(TurnoverBalance balance) =>
            balance.TurnoverDebit != 0m || balance.TurnoverCredit != 0m;

        /// <summary>Сравнение счетов без учёта регистра и пробелов по краям.</summary>
        public static bool SameAccount(string left, string right) =>
            (left ?? string.Empty).Trim().Equals((right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Проводка относится к выбранной организации.</summary>
        public static bool MatchesOrganization(
            PostingViewModel posting,
            TrialBalanceOrganizationScope? organization)
        {
            if (organization == null || organization.IsAll)
                return true;

            var postingOrganization = posting.Organization?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(postingOrganization))
                return false;

            if (organization.Id.HasValue && organization.Id.Value != Guid.Empty &&
                postingOrganization.Equals(organization.Id.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var postingName = NormalizeLookupValue(postingOrganization);
            var candidates = new[] { organization.DisplayName, organization.Name, organization.FullName, organization.Code }
                .Select(NormalizeLookupValue)
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return candidates.Any(candidate =>
                postingOrganization.Equals(candidate, StringComparison.CurrentCultureIgnoreCase) ||
                postingName.Equals(candidate, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Значение организации для сравнения: без пометки неактивной базы.</summary>
        private static string NormalizeLookupValue(string? value) =>
            NormalizeOrganizationName(value)
                .Replace(" (неактивна)", string.Empty, StringComparison.CurrentCultureIgnoreCase)
                .Trim();

        /// <summary>
        /// Снимает префикс базы из названия организации вида «База - Название».
        /// </summary>
        public static string NormalizeOrganizationName(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex >= 0 ? text.Substring(separatorIndex + 3).Trim() : text;
        }

        /// <summary>
        /// Добавляет строку «ИТОГО» с суммами по числовым колонкам ведомости.
        ///
        /// Границы заданы явно: служебные колонки макета тоже имеют числовой тип,
        /// и их суммировать нельзя.
        /// </summary>
        private static void AddTotalsRow(DataTable table, int firstNumericColumn)
        {
            if (table.Rows.Count == 0)
                return;

            // Последняя колонка отчёта — числовые колонки идут подряд.
            var lastNumericColumn = firstNumericColumn + NumericColumnNames.Length - 1;
            if (lastNumericColumn >= table.Columns.Count)
                lastNumericColumn = table.Columns.Count - 1;

            var totals = table.NewRow();
            totals[0] = "ИТОГО";
            totals[1] = string.Empty;

            for (var index = firstNumericColumn; index <= lastNumericColumn; index++)
            {
                var column = table.Columns[index];
                if (column.DataType != typeof(decimal))
                    continue;

                var sum = table.Rows.Cast<DataRow>()
                    .Where(row => row.RowState != DataRowState.Deleted)
                    .Sum(row => row[column] == DBNull.Value ? 0m : Convert.ToDecimal(row[column]));

                totals[column] = sum;
            }

            table.Rows.Add(totals);
        }

        private static string ReadString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null)
                    continue;

                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                    return text.Trim();
            }

            return string.Empty;
        }
    }
}
