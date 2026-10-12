using System.Data;
using System.Globalization;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>Параметр системного расчёта — для показа в интерфейсе.</summary>
    public sealed record SystemCalculatorParameter(
        string Name,
        string Title,
        string ParameterType = "String",
        string DefaultValue = "",
        bool IsRequired = true);

    /// <summary>Готовая колонка результата системного расчёта.</summary>
    public sealed record SystemCalculatorField(string DbColumnName, string Name, string FieldType = "String");

    /// <summary>
    /// Готовый системный расчёт: превращает расчёт системы в набор данных отчёта.
    /// </summary>
    public sealed record SystemReportCalculator(
        string Code,
        string Name,
        string Description,
        IReadOnlyList<SystemCalculatorParameter> Parameters,
        IReadOnlyList<SystemCalculatorField> Fields,
        Func<AppDbContext, IReadOnlyDictionary<string, object>?, CancellationToken, Task<DataTable>> Build);

    /// <summary>
    /// Справочник системных расчётов.
    ///
    /// Идея: расчёт, уже написанный в системе, публикуется как источник данных.
    /// Администратор выбирает расчёт, задаёт параметры и собирает вывод мышью
    /// в конструкторе — без разработки.
    ///
    /// Этим системные расчёты отличаются от SQL-наборов: там администратор сам
    /// описывает запрос, а здесь пользуется готовой логикой учёта.
    /// </summary>
    public static class SystemReportCalculators
    {
        public const string MovementsCode = "system.movements";
        public const string TurnoverCode = "system.turnover";
        public const string BalancesCode = "system.balances";
        public const string ReconciliationActCode = "system.reconciliation-act";

        /// <summary>Общие параметры периода — совпадают с параметрами SQL-наборов.</summary>
        private static readonly SystemCalculatorParameter[] PeriodParameters =
        {
            new("PeriodStart", "Начало периода", "Date"),
            new("PeriodEnd", "Конец периода", "Date")
        };

        private static readonly SystemCalculatorParameter[] PeriodAndOrganizationParameters =
        {
            new("PeriodStart", "Начало периода", "Date"),
            new("PeriodEnd", "Конец периода", "Date"),
            new("OrganizationId", "Организация", "Reference", "", false)
        };

        /// <summary>Все доступные расчёты, в порядке показа.</summary>
        public static IReadOnlyList<SystemReportCalculator> All { get; } = new List<SystemReportCalculator>
        {
            new(
                MovementsCode,
                "Движения по проводкам",
                "Проводки за период: дата, документ, счета, сумма, организация. Основа для журналов и реестров.",
                PeriodAndOrganizationParameters,
                new[]
                {
                    new SystemCalculatorField("Дата", "Дата", "Date"),
                    new SystemCalculatorField("Организация", "Организация"),
                    new SystemCalculatorField("N докум", "Номер документа"),
                    new SystemCalculatorField("Вид документа", "Вид документа"),
                    new SystemCalculatorField("Дебет", "Дебет"),
                    new SystemCalculatorField("Кредит", "Кредит"),
                    new SystemCalculatorField("Сумма", "Сумма", "Number"),
                    new SystemCalculatorField("Содержание", "Содержание"),
                    new SystemCalculatorField("Модуль", "Модуль"),
                    new SystemCalculatorField("posting_date", "Дата (макет)", "Date"),
                    new SystemCalculatorField("organization_id", "Организация (макет)"),
                    new SystemCalculatorField("doc_number", "Номер документа (макет)"),
                    new SystemCalculatorField("document_type", "Вид документа (макет)"),
                    new SystemCalculatorField("debit_account", "Дебет (макет)"),
                    new SystemCalculatorField("credit_account", "Кредит (макет)"),
                    new SystemCalculatorField("amount_kgs", "Сумма (макет)", "Number"),
                    new SystemCalculatorField("description", "Содержание (макет)"),
                    new SystemCalculatorField("module_code", "Модуль (макет)")
                },
                BuildMovementsAsync),

            new(
                TurnoverCode,
                "Обороты по счёту",
                "Оборотно-сальдовая ведомость по выбранному счёту: сальдо на начало, обороты, сальдо на конец.",
                new[]
                {
                    new SystemCalculatorParameter("AccountCode", "Счёт", "Reference"),
                    new SystemCalculatorParameter("PeriodStart", "Начало периода", "Date"),
                    new SystemCalculatorParameter("PeriodEnd", "Конец периода", "Date"),
                    new SystemCalculatorParameter("Side", "Сторона отбора", "String", "Debit", false)
                },
                new[]
                {
                    new SystemCalculatorField("Счет", "Счёт"),
                    new SystemCalculatorField("Наименование", "Наименование"),
                    new SystemCalculatorField("Сальдо на начало Дт", "Сальдо на начало Дт", "Number"),
                    new SystemCalculatorField("Сальдо на начало Кт", "Сальдо на начало Кт", "Number"),
                    new SystemCalculatorField("Оборот Дт", "Оборот Дт", "Number"),
                    new SystemCalculatorField("Оборот Кт", "Оборот Кт", "Number"),
                    new SystemCalculatorField("Сальдо на конец Дт", "Сальдо на конец Дт", "Number"),
                    new SystemCalculatorField("Сальдо на конец Кт", "Сальдо на конец Кт", "Number"),
                    new SystemCalculatorField("account_code", "Счёт (макет)"),
                    new SystemCalculatorField("account_name", "Наименование (макет)"),
                    new SystemCalculatorField("opening_debit", "Сальдо нач. Дт (макет)", "Number"),
                    new SystemCalculatorField("opening_credit", "Сальдо нач. Кт (макет)", "Number"),
                    new SystemCalculatorField("turnover_debit", "Оборот Дт (макет)", "Number"),
                    new SystemCalculatorField("turnover_credit", "Оборот Кт (макет)", "Number"),
                    new SystemCalculatorField("closing_debit", "Сальдо кон. Дт (макет)", "Number"),
                    new SystemCalculatorField("closing_credit", "Сальдо кон. Кт (макет)", "Number")
                },
                BuildTurnoverAsync),

            new(
                BalancesCode,
                "Сальдо по организациям",
                "Расчёт сальдо по парам счетов и организациям: начальное, обороты, конечное — в сомах и валюте.",
                new[]
                {
                    new SystemCalculatorParameter("PeriodStart", "Начало периода", "Date"),
                    new SystemCalculatorParameter("PeriodEnd", "Конец периода", "Date"),
                    new SystemCalculatorParameter("Mode", "Что выводить", "String", "Pairs", false)
                },
                new[]
                {
                    new SystemCalculatorField("Организация", "Организация"),
                    new SystemCalculatorField("Пара счетов", "Пара счетов"),
                    new SystemCalculatorField("Счет", "Счёт"),
                    new SystemCalculatorField("Корр. счет", "Корреспондентский счёт"),
                    new SystemCalculatorField("Модуль", "Модуль"),
                    new SystemCalculatorField("Сальдо на начало Дт", "Сальдо на начало Дт", "Number"),
                    new SystemCalculatorField("Сальдо на начало Кт", "Сальдо на начало Кт", "Number"),
                    new SystemCalculatorField("Оборот Дт", "Оборот Дт", "Number"),
                    new SystemCalculatorField("Оборот Кт", "Оборот Кт", "Number"),
                    new SystemCalculatorField("Сальдо на конец Дт", "Сальдо на конец Дт", "Number"),
                    new SystemCalculatorField("Сальдо на конец Кт", "Сальдо на конец Кт", "Number"),
                    new SystemCalculatorField("Сальдо на конец", "Сальдо на конец", "Number"),
                    new SystemCalculatorField("organization_name", "Организация (макет)"),
                    new SystemCalculatorField("account_code", "Счёт (макет)"),
                    new SystemCalculatorField("counter_account_code", "Корр. счёт (макет)"),
                    new SystemCalculatorField("opening_debit", "Сальдо нач. Дт (макет)", "Number"),
                    new SystemCalculatorField("opening_credit", "Сальдо нач. Кт (макет)", "Number"),
                    new SystemCalculatorField("turnover_debit", "Оборот Дт (макет)", "Number"),
                    new SystemCalculatorField("turnover_credit", "Оборот Кт (макет)", "Number"),
                    new SystemCalculatorField("closing_debit", "Сальдо кон. Дт (макет)", "Number"),
                    new SystemCalculatorField("closing_credit", "Сальдо кон. Кт (макет)", "Number")
                },
                BuildBalancesAsync),

            new(
                ReconciliationActCode,
                "Акт сверки",
                "Таблица акта сверки: шапка, пары счетов, сальдо, обороты и итоги по организациям.",
                PeriodAndOrganizationParameters,
                new[]
                {
                    new SystemCalculatorField("Наименование материала, вид операции", "Наименование материала, вид операции"),
                    new SystemCalculatorField("Дебет", "Дебет"),
                    new SystemCalculatorField("Кредит", "Кредит"),
                    new SystemCalculatorField("Сумма Дт", "Сумма Дт", "Number"),
                    new SystemCalculatorField("Сумма Кт", "Сумма Кт", "Number"),
                    new SystemCalculatorField("N докум", "Номер документа"),
                    new SystemCalculatorField("Дата", "Дата", "Date"),
                    new SystemCalculatorField("Модуль", "Модуль"),
                    new SystemCalculatorField("posting_date", "Дата (макет)", "Date"),
                    new SystemCalculatorField("document_date", "Дата документа (макет)"),
                    new SystemCalculatorField("module_code", "Модуль (макет)"),
                    new SystemCalculatorField("module", "Модуль документа (макет)"),
                    new SystemCalculatorField("debit_account", "Дебет (макет)"),
                    new SystemCalculatorField("credit_account", "Кредит (макет)"),
                    new SystemCalculatorField("debit_amount", "Сумма Дт (макет)", "Number"),
                    new SystemCalculatorField("credit_amount", "Сумма Кт (макет)", "Number"),
                    new SystemCalculatorField("operation_name", "Наименование (макет)"),
                    new SystemCalculatorField("document_number", "Номер документа (макет)"),
                    new SystemCalculatorField("report_summary", "Сводка отчёта"),
                    new SystemCalculatorField("period_start", "Начало периода"),
                    new SystemCalculatorField("period_end", "Конец периода")
                },
                BuildReconciliationActAsync)
        };

        /// <summary>Находит расчёт по коду.</summary>
        public static SystemReportCalculator? Find(string? code) =>
            string.IsNullOrWhiteSpace(code)
                ? null
                : All.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>Список для выпадающего списка: код и название.</summary>
        public static IReadOnlyList<(string Code, string Name)> Options =>
            All.Select(item => (item.Code, item.Name)).ToList();

        /// <summary>Поля расчёта в виде описаний для сохранения в наборе данных.</summary>
        public static IEnumerable<ReportDataSetField> BuildFieldRows(SystemReportCalculator calculator, Guid dataSetId) =>
            calculator.Fields.Select((field, index) => new ReportDataSetField
            {
                Id = Guid.NewGuid(),
                ReportDataSetId = dataSetId,
                DbColumnName = field.DbColumnName,
                Name = field.Name,
                FieldType = field.FieldType,
                Order = index + 1
            });

        // =============================================================
        // Исполнители расчётов
        // =============================================================

        /// <summary>Движения по проводкам за период.</summary>
        private static async Task<DataTable> BuildMovementsAsync(
            AppDbContext context,
            IReadOnlyDictionary<string, object>? parameters,
            CancellationToken cancellationToken)
        {
            var (start, end) = ReadPeriod(parameters);
            var organizationId = ReadGuid(parameters, "OrganizationId");

            var rows = await new ReconciliationActBuilder(context)
                .LoadMovementsAsync(organizationId, start, end);

            var table = new DataTable("Движения по проводкам");
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("N докум", typeof(string));
            table.Columns.Add("Вид документа", typeof(string));
            table.Columns.Add("Дебет", typeof(string));
            table.Columns.Add("Кредит", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));
            table.Columns.Add("Содержание", typeof(string));
            table.Columns.Add("Модуль", typeof(string));
            table.Columns.Add("posting_date", typeof(DateTime));
            table.Columns.Add("organization_id", typeof(string));
            table.Columns.Add("doc_number", typeof(string));
            table.Columns.Add("document_type", typeof(string));
            table.Columns.Add("debit_account", typeof(string));
            table.Columns.Add("credit_account", typeof(string));
            table.Columns.Add("amount_kgs", typeof(decimal));
            table.Columns.Add("description", typeof(string));
            table.Columns.Add("module_code", typeof(string));

            foreach (var movement in rows)
            {
                var row = table.NewRow();
                row["Дата"] = movement.Date;
                row["Организация"] = movement.OrganizationName;
                row["N докум"] = movement.DocumentNumber;
                row["Вид документа"] = movement.DocumentType;
                row["Дебет"] = movement.DebitAccount;
                row["Кредит"] = movement.CreditAccount;
                row["Сумма"] = movement.Amount;
                row["Содержание"] = movement.Description;
                row["Модуль"] = movement.ModuleCode;

                // Те же значения под именами, которые ищет макет.
                row["posting_date"] = movement.Date;
                row["organization_id"] = movement.OrganizationName;
                row["doc_number"] = movement.DocumentNumber;
                row["document_type"] = movement.DocumentType;
                row["debit_account"] = movement.DebitAccount;
                row["credit_account"] = movement.CreditAccount;
                row["amount_kgs"] = movement.Amount;
                row["description"] = movement.Description;
                row["module_code"] = movement.ModuleCode;

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>Оборотно-сальдовая ведомость по счёту.</summary>
        private static async Task<DataTable> BuildTurnoverAsync(
            AppDbContext context,
            IReadOnlyDictionary<string, object>? parameters,
            CancellationToken cancellationToken)
        {
            var (start, end) = ReadPeriod(parameters);
            var accountCode = ReadText(parameters, "AccountCode");
            var side = string.Equals(ReadText(parameters, "Side"), "Credit", StringComparison.OrdinalIgnoreCase)
                ? TrialBalanceSide.Credit
                : TrialBalanceSide.Debit;

            if (string.IsNullOrWhiteSpace(accountCode))
                return new DataTable("Обороты по счёту");

            var balance = await new TrialBalanceBuilder(context)
                .BuildTableAsync(new TrialBalanceRequest(accountCode, start, end, side));

            return balance.Table;
        }

        /// <summary>Сальдо по организациям и парам счетов.</summary>
        private static async Task<DataTable> BuildBalancesAsync(
            AppDbContext context,
            IReadOnlyDictionary<string, object>? parameters,
            CancellationToken cancellationToken)
        {
            var (start, end) = ReadPeriod(parameters);
            var mode = ReadText(parameters, "Mode");

            var calculation = await new OrganizationBalanceService(context).CalculateAsync(start, end);
            var rows = calculation.Rows;
            if (string.Equals(mode, "Pairs", StringComparison.OrdinalIgnoreCase))
                rows = rows.Where(row => !row.IsOrganizationTotal).ToList();
            else if (string.Equals(mode, "Totals", StringComparison.OrdinalIgnoreCase))
                rows = rows.Where(row => row.IsOrganizationTotal).ToList();

            var table = new DataTable("Сальдо по организациям");
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Пара счетов", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Корр. счет", typeof(string));
            table.Columns.Add("Модуль", typeof(string));
            table.Columns.Add("Сальдо на начало Дт", typeof(decimal));
            table.Columns.Add("Сальдо на начало Кт", typeof(decimal));
            table.Columns.Add("Оборот Дт", typeof(decimal));
            table.Columns.Add("Оборот Кт", typeof(decimal));
            table.Columns.Add("Сальдо на конец Дт", typeof(decimal));
            table.Columns.Add("Сальдо на конец Кт", typeof(decimal));
            table.Columns.Add("Сальдо на конец", typeof(decimal));
            table.Columns.Add("organization_name", typeof(string));
            table.Columns.Add("account_code", typeof(string));
            table.Columns.Add("counter_account_code", typeof(string));
            table.Columns.Add("opening_debit", typeof(decimal));
            table.Columns.Add("opening_credit", typeof(decimal));
            table.Columns.Add("turnover_debit", typeof(decimal));
            table.Columns.Add("turnover_credit", typeof(decimal));
            table.Columns.Add("closing_debit", typeof(decimal));
            table.Columns.Add("closing_credit", typeof(decimal));

            foreach (var item in rows)
            {
                var row = table.NewRow();
                row["Организация"] = item.OrganizationName;
                row["Пара счетов"] = item.IsOrganizationTotal ? "ИТОГО" : item.AccountPairName;
                row["Счет"] = item.AccountCode;
                row["Корр. счет"] = item.CounterAccountCode;
                row["Модуль"] = item.ModuleCode;
                row["Сальдо на начало Дт"] = item.OpeningDebit;
                row["Сальдо на начало Кт"] = item.OpeningCredit;
                row["Оборот Дт"] = item.TurnoverDebit;
                row["Оборот Кт"] = item.TurnoverCredit;
                row["Сальдо на конец Дт"] = item.ClosingDebit;
                row["Сальдо на конец Кт"] = item.ClosingCredit;
                row["Сальдо на конец"] = item.Balance;

                row["organization_name"] = item.OrganizationName;
                row["account_code"] = item.AccountCode;
                row["counter_account_code"] = item.CounterAccountCode;
                row["opening_debit"] = item.OpeningDebit;
                row["opening_credit"] = item.OpeningCredit;
                row["turnover_debit"] = item.TurnoverDebit;
                row["turnover_credit"] = item.TurnoverCredit;
                row["closing_debit"] = item.ClosingDebit;
                row["closing_credit"] = item.ClosingCredit;

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>Таблица акта сверки.</summary>
        private static async Task<DataTable> BuildReconciliationActAsync(
            AppDbContext context,
            IReadOnlyDictionary<string, object>? parameters,
            CancellationToken cancellationToken)
        {
            var (start, end) = ReadPeriod(parameters);
            var organizationId = ReadGuid(parameters, "OrganizationId");

            var calculation = await new OrganizationBalanceService(context).CalculateAsync(start, end);
            var pairs = calculation.Rows
                .Where(row => !row.IsOrganizationTotal)
                .Where(row => MatchesOrganization(row, organizationId))
                .ToList();
            var totals = calculation.Rows
                .Where(row => row.IsOrganizationTotal)
                .Where(row => MatchesOrganization(row, organizationId))
                .ToList();

            var builder = new ReconciliationActBuilder(context);
            var movements = await builder.LoadMovementsAsync(organizationId, start, end);
            var act = builder.BuildTable(pairs, totals, movements, start, end, organizationId.HasValue, "Все организации");

            FillReportColumns(act.Table, start, end, act.Summary);

            // Макет обращается к дате как posting_date, к модулю — как module_code.
            // Сам построитель акта эти колонки не заполняет: это делает вызывающая сторона.
            foreach (DataRow row in act.Table.Rows)
            {
                var dateText = act.Table.Columns.Contains("Дата") ? row["Дата"]?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(dateText))
                {
                    if (DateTime.TryParse(dateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
                    {
                        row["posting_date"] = parsed.ToString("dd.MM.yyyy");
                        row["document_date"] = parsed.ToString("dd.MM.yyyy");
                    }
                    else
                    {
                        row["posting_date"] = dateText;
                        row["document_date"] = dateText;
                    }
                }

                var moduleText = act.Table.Columns.Contains("Модуль") ? row["Модуль"]?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(moduleText))
                {
                    if (act.Table.Columns.Contains("module_code"))
                        row["module_code"] = moduleText;
                    if (act.Table.Columns.Contains("module"))
                        row["module"] = moduleText;
                }
            }

            return act.Table;
        }

        /// <summary>
        /// Заполняет служебные колонки отчёта: период и сводку.
        /// Их читает макет, поэтому без них часть шапки выйдет пустой.
        /// </summary>
        private static void FillReportColumns(DataTable table, DateTime start, DateTime end, string summary)
        {
            if (table.Rows.Count == 0)
                return;

            var periodStart = start.ToString("dd.MM.yyyy");
            var periodEnd = end.ToString("dd.MM.yyyy");

            foreach (DataRow row in table.Rows)
            {
                if (table.Columns.Contains("report_summary"))
                    row["report_summary"] = summary;
                if (table.Columns.Contains("period_start"))
                    row["period_start"] = periodStart;
                if (table.Columns.Contains("period_end"))
                    row["period_end"] = periodEnd;
            }
        }

        /// <summary>
        /// Проверяет принадлежность строки расчёта выбранной организации.
        /// При пустом отборе проходят все строки.
        /// </summary>
        private static bool MatchesOrganization(OrganizationBalanceRow row, Guid? organizationId)
        {
            if (!organizationId.HasValue || organizationId.Value == Guid.Empty)
                return true;

            return row.OrganizationId == organizationId;
        }

        // =============================================================
        // Чтение параметров
        // =============================================================

        /// <summary>
        /// Читает период из параметров. Если значений нет, берётся текущий месяц:
        /// так расчёт не падает при первом открытии.
        /// </summary>
        private static (DateTime Start, DateTime End) ReadPeriod(IReadOnlyDictionary<string, object>? parameters)
        {
            var today = DateTime.Today;
            var start = parameters != null ? ReadDate(parameters, "PeriodStart") : null;
            var end = parameters != null ? ReadDate(parameters, "PeriodEnd") : null;

            var resolvedStart = start ?? new DateTime(today.Year, today.Month, 1);
            var resolvedEnd = end ?? today;

            if (resolvedEnd < resolvedStart)
                resolvedEnd = resolvedStart;

            return (resolvedStart.Date, resolvedEnd.Date);
        }

        private static DateTime? ReadDate(IReadOnlyDictionary<string, object> parameters, string name)
        {
            if (!TryGetValue(parameters, name, out var value) || value == null)
                return null;

            return value switch
            {
                DateTime date => date,
                DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
                string text when DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) => parsed,
                _ => null
            };
        }

        private static string ReadText(IReadOnlyDictionary<string, object>? parameters, string name)
        {
            if (parameters == null || !TryGetValue(parameters, name, out var value) || value == null)
                return string.Empty;

            return value.ToString()?.Trim() ?? string.Empty;
        }

        private static Guid? ReadGuid(IReadOnlyDictionary<string, object>? parameters, string name)
        {
            var text = ReadText(parameters, name);
            return Guid.TryParse(text, out var value) && value != Guid.Empty ? value : null;
        }

        /// <summary>Ищет параметр без учёта регистра.</summary>
        private static bool TryGetValue(
            IReadOnlyDictionary<string, object> parameters,
            string name,
            out object? value)
        {
            foreach (var pair in parameters)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
