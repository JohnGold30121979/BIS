using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BIS.ERP.Services;

public sealed class ReportDataSetService
{
    public const string DataSetReferenceKey = "ReportDataSetCode";
    public const string CashOrderTurnoverDataSetCode = "cash.order.turnover";

    /// <summary>Код набора данных отчёта по счет-фактурам.</summary>
    public const string InvoiceJournalDataSetCode = "finance.invoice-journal";

    /// <summary>Код набора данных счёта-фактуры по форме СТ-1.</summary>
    public const string VatInvoiceSt1DataSetCode = "finance.vat-invoice-st1";

    private static readonly Regex ForbiddenSqlRegex = new(
        @"\b(insert|update|delete|drop|alter|create|truncate|grant|revoke|copy|call|execute|merge|vacuum|analyze|do|begin|commit|rollback)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppDbContext _context;

    public ReportDataSetService(AppDbContext context)
    {
        _context = context;
    }

    private async Task<NpgsqlConnection> OpenStandaloneConnectionAsync()
    {
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Не удалось получить строку подключения к инфобазе.");

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
    public async Task EnsureSchemaAsync()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS ""ReportDataSets"" (
                ""Id"" uuid NOT NULL,
                ""Code"" varchar(160) NOT NULL,
                ""Name"" varchar(300) NOT NULL,
                ""Description"" varchar(1000) NOT NULL DEFAULT '',
                ""Kind"" varchar(20) NOT NULL DEFAULT 'Sql',
                ""CalculatorCode"" varchar(120) NOT NULL DEFAULT '',
                ""ParameterDefinitions"" text NOT NULL DEFAULT '',
                ""SqlText"" text NOT NULL DEFAULT '',
                ""IsActive"" boolean NOT NULL DEFAULT true,
                ""IsSystem"" boolean NOT NULL DEFAULT false,
                ""MetadataObjectId"" uuid NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW(),
                CONSTRAINT ""PK_ReportDataSets"" PRIMARY KEY (""Id"")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReportDataSets_Code""
                ON ""ReportDataSets"" (""Code"");

            -- Поля вида набора данных появились позже: добавляем их к существующим
            -- таблицам, чтобы обновление не требовало пересоздания базы.
            ALTER TABLE ""ReportDataSets"" ADD COLUMN IF NOT EXISTS ""Kind"" varchar(20) NOT NULL DEFAULT 'Sql';
            ALTER TABLE ""ReportDataSets"" ADD COLUMN IF NOT EXISTS ""CalculatorCode"" varchar(120) NOT NULL DEFAULT '';
            ALTER TABLE ""ReportDataSets"" ADD COLUMN IF NOT EXISTS ""ParameterDefinitions"" text NOT NULL DEFAULT '';

            CREATE TABLE IF NOT EXISTS ""ReportDataSetFields"" (
                ""Id"" uuid NOT NULL,
                ""ReportDataSetId"" uuid NOT NULL,
                ""Name"" varchar(160) NOT NULL,
                ""DbColumnName"" varchar(160) NOT NULL,
                ""FieldType"" varchar(40) NOT NULL DEFAULT 'String',
                ""Order"" integer NOT NULL DEFAULT 0,
                CONSTRAINT ""PK_ReportDataSetFields"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_ReportDataSetFields_ReportDataSets_ReportDataSetId""
                    FOREIGN KEY (""ReportDataSetId"") REFERENCES ""ReportDataSets"" (""Id"") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ReportDataSetFields_ReportDataSetId_DbColumnName""
                ON ""ReportDataSetFields"" (""ReportDataSetId"", ""DbColumnName"");
        ";

        await using var connection = await OpenStandaloneConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    public async Task EnsureStandardDataSetsAsync()
    {
        await EnsureSchemaAsync();
        await EnsureCashOrderTurnoverDataSetAsync();
        await EnsureInvoiceJournalDataSetAsync();
        await EnsureVatInvoiceSt1DataSetAsync();
        await EnsureSystemCalculatorDataSetsAsync();
    }

    /// <summary>
    /// Заводит набор данных счёта-фактуры по форме СТ-1.
    ///
    /// Набор отдаёт строки позиций вместе с реквизитами сторон: макет бланка
    /// берёт из первой строки продавца, покупателя и данные документа.
    /// </summary>
    private async Task EnsureVatInvoiceSt1DataSetAsync()
    {
        var dataSet = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Code == VatInvoiceSt1DataSetCode);

        if (dataSet == null)
        {
            dataSet = new ReportDataSet
            {
                Id = Guid.NewGuid(),
                Code = VatInvoiceSt1DataSetCode,
                Name = "Счет-фактура СТ-1: строки и реквизиты",
                Description = "SQL-набор данных бланка СТ-1: позиции счёта-фактуры и реквизиты сторон. Образец форматного макета с многоуровневой шапкой.",
                Kind = ReportDataSetKinds.Sql,
                SqlText = BuildVatInvoiceSt1Sql(),
                IsActive = true,
                IsSystem = true
            };
            await _context.ReportDataSets.AddAsync(dataSet);
            await _context.SaveChangesAsync();
        }
        else if (string.Equals(dataSet.Kind, ReportDataSetKinds.Sql, StringComparison.OrdinalIgnoreCase))
        {
            dataSet.IsActive = true;
            dataSet.IsSystem = true;

            var untouched = dataSet.SqlText.Contains("invoice_positions", StringComparison.OrdinalIgnoreCase);
            if (untouched)
                dataSet.SqlText = BuildVatInvoiceSt1Sql();

            await _context.SaveChangesAsync();
        }

        try
        {
            if (!dataSet.Fields.Any())
            {
                await RefreshFieldsFromSqlAsync(dataSet);
                dataSet = await _context.ReportDataSets
                    .Include(item => item.Fields)
                    .FirstAsync(item => item.Id == dataSet.Id);
            }

            await SyncMetadataSourceAsync(dataSet);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            SystemLogService.Error(
                "Ошибка подготовки набора данных СТ-1. Конфигуратор продолжит загрузку.",
                nameof(ReportDataSetService), ex);
        }
    }

    /// <summary>
    /// Запрос бланка СТ-1.
    ///
    /// Реквизиты сторон берутся из справочника организаций и подставляются
    /// в каждую строку: макет читает их из первой строки набора.
    /// </summary>
    private static string BuildVatInvoiceSt1Sql() => @"
WITH invoice_header AS (
    SELECT
        i.""Id""                                     AS invoice_id,
        COALESCE(i.""doc_number"", '')              AS doc_number,
        i.""doc_date""::date                        AS doc_date,
        COALESCE(NULLIF(i.""esf_number"", ''), '')  AS esf_number,
        i.""organization_id""                       AS organization_id
    FROM ""doc_sales_invoice"" i
    WHERE i.""doc_date""::date >= CAST(@PeriodStart AS date)
      AND i.""doc_date""::date <= CAST(@PeriodEnd AS date)
      AND (CAST(@OrganizationId AS text) = '' OR i.""organization_id""::text = CAST(@OrganizationId AS text))
),
seller AS (
    SELECT
        h.invoice_id,
        COALESCE(NULLIF(o.""full_name"", ''), o.""name"", '')       AS seller_name,
        COALESCE(o.""inn"", '')                                     AS seller_inn,
        COALESCE(o.""okpo"", '')                                    AS seller_okpo,
        COALESCE(NULLIF(o.""legal_address"", ''), NULLIF(o.""actual_address"", ''), '') AS seller_address,
        COALESCE(o.""registration_number"", '')                     AS seller_reg_number,
        COALESCE(o.""bank_name"", '')                               AS seller_bank,
        COALESCE(o.""bank_account"", '')                            AS seller_account,
        COALESCE(o.""director"", '')                                AS seller_director,
        COALESCE(o.""chief_accountant"", '')                        AS seller_accountant,
        COALESCE(h.doc_number, '')                                  AS doc_number,
        h.doc_date,
        COALESCE(h.esf_number, '')                                  AS esf_number
    FROM invoice_header h
    LEFT JOIN catalog_organizations o ON o.""Id""::text = h.organization_id::text
),
invoice_positions AS (
    SELECT
        s.*,
        l.""line_number"",
        COALESCE(l.""name"", '')                AS item_name,
        COALESCE(l.""unit_name"", '')           AS unit_name,
        COALESCE(l.""quantity"", 0)             AS quantity,
        CASE WHEN COALESCE(l.""quantity"", 0) <> 0
             THEN ROUND(COALESCE(l.""amount_without_tax"", 0) / l.""quantity"", 2)
             ELSE 0 END                         AS price,
        COALESCE(l.""amount_without_tax"", 0)   AS amount_without_tax,
        COALESCE(l.""vat_rate"", 0)             AS vat_rate,
        COALESCE(l.""vat_amount"", 0)           AS vat_amount,
        COALESCE(l.""sales_tax_rate"", 0)       AS sales_tax_rate,
        COALESCE(l.""sales_tax_amount"", 0)     AS sales_tax_amount,
        COALESCE(l.""line_total"", 0)           AS line_total,
        COALESCE(l.""vat_tax_code"", '')        AS vat_tax_code,
        COALESCE(l.""sales_tax_code"", '')      AS tax_code
    FROM seller s
    JOIN ""doc_sales_invoice_lines"" l ON l.""invoice_id"" = s.invoice_id
),
totals AS (
    SELECT
        SUM(amount_without_tax) AS total_without_tax,
        SUM(vat_amount)         AS total_vat,
        SUM(sales_tax_amount)   AS total_sales_tax,
        SUM(line_total)         AS total_amount
    FROM invoice_positions
)
SELECT
    CAST(p.line_number AS text)                                     AS line_number,
    p.item_name,
    p.unit_name,
    p.quantity,
    p.price,
    p.amount_without_tax,
    p.vat_rate,
    p.vat_amount,
    p.sales_tax_rate,
    p.sales_tax_amount,
    p.line_total,
    p.tax_code,
    p.vat_tax_code,

    -- Реквизиты сторон: их читает шапка бланка.
    p.seller_name,
    p.seller_inn,
    p.seller_okpo,
    p.seller_address,
    p.seller_reg_number,
    p.seller_bank,
    p.seller_account,
    p.seller_director,
    p.seller_accountant,
    ''                                                              AS buyer_name,
    ''                                                              AS buyer_inn,
    ''                                                              AS buyer_address,

    -- Готовые подписи бланка.
    'ИНН: ' || p.seller_inn || '   ОКПО: ' || p.seller_okpo          AS seller_inn_text,
    'Адрес: ' || p.seller_address                                    AS seller_address_text,
    'ИНН: ' || '' || '   ОКПО: ' || ''                              AS buyer_inn_text,
    'Адрес: ' || ''                                                 AS buyer_address_text,
    'Рег. N: ' || p.seller_reg_number                                AS seller_reg_number_text,
    'Р/с: ' || p.seller_account || ' в ' || p.seller_bank            AS seller_bank_text,

    'СЧЕТ-ФАКТУРА N ' || p.doc_number || ' от '
        || TO_CHAR(p.doc_date, 'DD.MM.YYYY')                        AS report_title,
    TO_CHAR(CAST(@PeriodStart AS date), 'DD.MM.YYYY')                AS period_start,
    TO_CHAR(CAST(@PeriodEnd AS date), 'DD.MM.YYYY')                  AS period_end,
    COALESCE(p.esf_number, '')                                       AS esf_number,

    t.total_without_tax,
    t.total_vat,
    t.total_sales_tax,
    t.total_amount
FROM invoice_positions p
CROSS JOIN totals t
-- Сначала документы с заполненными реквизитами: макет берёт их из первой строки.
ORDER BY (CASE WHEN COALESCE(p.seller_name, '') = '' THEN 1 ELSE 0 END),
         p.doc_date DESC, p.doc_number DESC, p.line_number";

    /// <summary>
    /// Заводит набор данных отчёта по счет-фактурам.
    ///
    /// Это образец SQL-набора: администратор открывает его в конфигураторе,
    /// видит запрос целиком и правит под свою форму. Так же делается любой
    /// другой отчёт по документам — без разработчика.
    /// </summary>
    private async Task EnsureInvoiceJournalDataSetAsync()
    {
        var dataSet = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Code == InvoiceJournalDataSetCode);

        if (dataSet == null)
        {
            dataSet = new ReportDataSet
            {
                Id = Guid.NewGuid(),
                Code = InvoiceJournalDataSetCode,
                Name = "Счет-фактуры выданные и полученные",
                Description = "SQL-набор данных по счет-фактурам: документ, организация, суммы без налога, НДС, налог с продаж, всего. Образец для сборки отчёта по документу.",
                Kind = ReportDataSetKinds.Sql,
                SqlText = BuildInvoiceJournalSql(),
                IsActive = true,
                IsSystem = true
            };
            await _context.ReportDataSets.AddAsync(dataSet);
            await _context.SaveChangesAsync();
        }
        else if (string.Equals(dataSet.Kind, ReportDataSetKinds.Sql, StringComparison.OrdinalIgnoreCase))
        {
            dataSet.IsActive = true;
            dataSet.IsSystem = true;

            // Запрос обновляем, только если администратор его не менял.
            // Признак правки — исчезли наши опорные имена.
            var untouched = dataSet.SqlText.Contains("invoice_rows", StringComparison.OrdinalIgnoreCase)
                            && dataSet.SqlText.Contains("@PeriodStart", StringComparison.OrdinalIgnoreCase);
            if (untouched)
                dataSet.SqlText = BuildInvoiceJournalSql();

            await _context.SaveChangesAsync();
        }

        try
        {
            if (!dataSet.Fields.Any())
            {
                await RefreshFieldsFromSqlAsync(dataSet);
                dataSet = await _context.ReportDataSets
                    .Include(item => item.Fields)
                    .FirstAsync(item => item.Id == dataSet.Id);
            }

            await SyncMetadataSourceAsync(dataSet);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            SystemLogService.Error(
                "Ошибка подготовки набора данных счет-фактур. Конфигуратор продолжит загрузку.",
                nameof(ReportDataSetService), ex);
        }
    }

    /// <summary>
    /// Запрос отчёта по счет-фактурам. Образец для администратора:
    /// соединение документа со справочником организаций, приведение типов,
    /// вычисляемые подписи, отбор по периоду и организации.
    /// </summary>
    private static string BuildInvoiceJournalSql() => @"
WITH invoice_rows AS (
    SELECT
        i.""doc_number""                              AS doc_number,
        i.""doc_date""::date                          AS doc_date,
        COALESCE(i.""organization_id""::text, '')     AS organization_id,
        COALESCE(o.""name"", '')                      AS org_name,
        COALESCE(NULLIF(i.""esf_number"", ''), '')    AS esf_number,
        COALESCE(i.""amount_without_tax"", 0)         AS amount_without_tax,
        COALESCE(i.""vat_total"", 0)                  AS vat_total,
        COALESCE(i.""sales_tax_total"", 0)            AS sales_tax_total,
        COALESCE(i.""amount"", 0)                     AS total_amount,
        CASE WHEN COALESCE(i.""is_posted"", false)
             THEN 'Проведён' ELSE 'Не проведён' END   AS posted_state,
        'Выписка'                                     AS document_kind,
        CASE
            WHEN COALESCE(i.""supply_kind"", '') LIKE '%1%' THEN '1 - по ставке 0 процентов'
            WHEN COALESCE(i.""supply_kind"", '') LIKE '%2%' THEN '2 - по ставке 12 процентов'
            WHEN COALESCE(i.""supply_kind"", '') LIKE '%3%' THEN '3 - освобождённый оборот'
            ELSE COALESCE(i.""supply_kind"", '')
        END                                           AS supply_kind
    FROM ""doc_sales_invoice"" i
    LEFT JOIN catalog_organizations o ON i.""organization_id""::text = o.""Id""::text

    UNION ALL

    SELECT
        p.""doc_number"",
        p.""doc_date""::date,
        COALESCE(p.""organization_id""::text, ''),
        COALESCE(o.""name"", ''),
        COALESCE(NULLIF(p.""esf_number"", ''), ''),
        COALESCE(p.""amount_without_tax"", 0),
        COALESCE(p.""vat_total"", 0),
        COALESCE(p.""sales_tax_total"", 0),
        COALESCE(p.""amount"", 0),
        CASE WHEN COALESCE(p.""is_posted"", false)
             THEN 'Проведён' ELSE 'Не проведён' END,
        'Регистрация',
        COALESCE(p.""supply_kind"", '')
    FROM ""doc_purchase_invoice"" p
    LEFT JOIN catalog_organizations o ON p.""organization_id""::text = o.""Id""::text
)
SELECT
    doc_number,
    doc_date,
    organization_id,
    org_name,
    esf_number,
    amount_without_tax,
    vat_total,
    sales_tax_total,
    total_amount,
    posted_state,
    document_kind,
    supply_kind,
    'Журнал счет-фактур за период ' || TO_CHAR(CAST(@PeriodStart AS date), 'DD.MM.YYYY')
        || ' - ' || TO_CHAR(CAST(@PeriodEnd AS date), 'DD.MM.YYYY')  AS report_title,
    TO_CHAR(CAST(@PeriodStart AS date), 'DD.MM.YYYY')                AS period_start,
    TO_CHAR(CAST(@PeriodEnd AS date), 'DD.MM.YYYY')                  AS period_end,
    doc_number                                                       AS doc_number_text,
    CAST(amount_without_tax AS text) || ' / ' || CAST(total_amount AS text) AS amounts_text
FROM invoice_rows
WHERE doc_date >= CAST(@PeriodStart AS date)
  AND doc_date <= CAST(@PeriodEnd AS date)
  -- Типы приводим явно: без этого PostgreSQL не сравнивает text с uuid.
  AND (CAST(@OrganizationId AS text) = '' OR organization_id = CAST(@OrganizationId AS text))
ORDER BY doc_date DESC, doc_number DESC";

    /// <summary>
    /// Заводит системные расчёты как готовые наборы данных.
    ///
    /// Так администратор сразу видит их в списке источников и может строить по ним
    /// отчёты, не написав ни одной строки кода. Колонки берутся из справочника
    /// расчётов, запрос писать не нужно.
    /// </summary>
    private async Task EnsureSystemCalculatorDataSetsAsync()
    {
        foreach (var calculator in SystemReportCalculators.All)
        {
            var existing = await _context.ReportDataSets
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.Code == calculator.Code);

            if (existing == null)
            {
                var created = new ReportDataSet
                {
                    Id = Guid.NewGuid(),
                    Code = calculator.Code,
                    Name = calculator.Name,
                    Description = calculator.Description,
                    Kind = ReportDataSetKinds.System,
                    CalculatorCode = calculator.Code,
                    ParameterDefinitions = BuildParameterDefinitions(calculator),
                    SqlText = string.Empty,
                    IsActive = true,
                    IsSystem = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _context.ReportDataSets.AddAsync(created);
                await _context.SaveChangesAsync();

                foreach (var field in SystemReportCalculators.BuildFieldRows(calculator, created.Id))
                    _context.ReportDataSetFields.Add(field);

                await _context.SaveChangesAsync();
                await SyncMetadataSourceAsync(created);
                _context.ChangeTracker.Clear();
                continue;
            }

            // У существующего набора обновляем только описание и вид:
            // правки администратора не перетираем.
            if (!string.Equals(existing.Kind, ReportDataSetKinds.System, StringComparison.OrdinalIgnoreCase))
                continue;

            existing.CalculatorCode = calculator.Code;
            existing.ParameterDefinitions = BuildParameterDefinitions(calculator);
            await _context.SaveChangesAsync();

            if (existing.Fields.Count != calculator.Fields.Count)
            {
                await RefreshFieldsFromCalculatorAsync(existing);
                await SyncMetadataSourceAsync(existing);
                _context.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Описание параметров расчёта для интерфейса — в виде JSON.</summary>
    private static string BuildParameterDefinitions(SystemReportCalculator calculator) =>
        JsonSerializer.Serialize(calculator.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Title,
            parameter.ParameterType,
            parameter.DefaultValue,
            parameter.IsRequired
        }));

    public async Task<List<ReportDataSet>> GetDataSetsAsync()
    {
        await EnsureSchemaAsync();
        return await _context.ReportDataSets
            .Include(item => item.Fields)
            .OrderBy(item => item.Name)
            .ToListAsync();
    }

    public async Task<ReportDataSet?> GetBySourceAsync(MetadataObject source)
    {
        if (!string.Equals(source.ObjectType, "ReportSource", StringComparison.OrdinalIgnoreCase))
            return null;

        var code = TryReadDataSetCode(source.ReferenceFields);
        if (string.IsNullOrWhiteSpace(code))
            return null;

        await EnsureSchemaAsync();
        return await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Code == code && item.IsActive);
    }

    public async Task<ReportDataSet> SaveAsync(ReportDataSet dataSet)
    {
        await EnsureSchemaAsync();
        dataSet.Code = NormalizeCode(dataSet.Code);

        var isSystem = IsSystemDataSet(dataSet);
        dataSet.Kind = isSystem ? ReportDataSetKinds.System : ReportDataSetKinds.Sql;

        if (isSystem)
        {
            // У системного расчёта SQL нет: проверять нечего.
            dataSet.SqlText = string.Empty;
            dataSet.CalculatorCode = dataSet.CalculatorCode.Trim();
        }
        else
        {
            dataSet.CalculatorCode = string.Empty;
            dataSet.SqlText = NormalizeSql(dataSet.SqlText);
            ValidateSql(dataSet.SqlText);
        }

        dataSet.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Id == dataSet.Id || item.Code == dataSet.Code);

        if (existing == null)
        {
            dataSet.Id = dataSet.Id == Guid.Empty ? Guid.NewGuid() : dataSet.Id;
            dataSet.CreatedAt = DateTime.UtcNow;
            await _context.ReportDataSets.AddAsync(dataSet);
            existing = dataSet;
        }
        else
        {
            existing.Code = dataSet.Code;
            existing.Name = dataSet.Name.Trim();
            existing.Description = dataSet.Description.Trim();
            existing.SqlText = dataSet.SqlText;
            existing.IsActive = dataSet.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.IsSystem = dataSet.IsSystem;

            // Вид набора данных и выбранный расчёт.
            existing.Kind = dataSet.Kind;
            existing.CalculatorCode = dataSet.CalculatorCode;
            existing.ParameterDefinitions = dataSet.ParameterDefinitions;
        }

        await _context.SaveChangesAsync();
        await RefreshFieldsFromSqlAsync(existing);
        await SyncMetadataSourceAsync(existing);
        return existing;
    }

    public async Task DeleteAsync(Guid dataSetId)
    {
        await EnsureSchemaAsync();
        var dataSet = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Id == dataSetId);
        if (dataSet == null)
            return;

        var metadata = dataSet.MetadataObjectId.HasValue
            ? await _context.MetadataObjects.FirstOrDefaultAsync(item => item.Id == dataSet.MetadataObjectId.Value)
            : null;

        if (metadata != null)
            _context.MetadataObjects.Remove(metadata);

        _context.ReportDataSets.Remove(dataSet);
        await _context.SaveChangesAsync();
    }

    public async Task<DataTable> ExecuteAsync(
        ReportDataSet dataSet,
        IReadOnlyDictionary<string, object>? parameters = null,
        int? limit = null)
    {
        // Системный расчёт: данные отдаёт готовый расчёт системы, а не запрос.
        if (IsSystemDataSet(dataSet))
            return await ExecuteSystemCalculatorAsync(dataSet, parameters);

        var sql = NormalizeSql(dataSet.SqlText);
        ValidateSql(sql);

        var useLimit = limit.HasValue;
        var wrappedSql = useLimit
            ? $"SELECT * FROM ({sql}) AS report_dataset_result LIMIT @__bis_limit"
            : $"SELECT * FROM ({sql}) AS report_dataset_result";

        var table = new DataTable(dataSet.Name);
        await using var connection = await OpenStandaloneConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = wrappedSql;
        command.CommandTimeout = 60;
        AddSqlParameters(command, sql, parameters);
        if (useLimit)
        {
            var limitParameter = command.CreateParameter();
            limitParameter.ParameterName = "@__bis_limit";
            limitParameter.Value = Math.Max(0, limit!.Value);
            command.Parameters.Add(limitParameter);
        }

        await using var reader = await command.ExecuteReaderAsync();
        table.Load(reader);
        return table;
    }
    public async Task<DataTable> TestAsync(ReportDataSet dataSet, int limit = 50)
    {
        // Системный расчёт проверяется исполнением: SQL у него нет.
        // Параметры не задаются — расчёт возьмёт период по умолчанию.
        if (IsSystemDataSet(dataSet))
            return await ExecuteSystemCalculatorAsync(dataSet, null);

        dataSet.SqlText = NormalizeSql(dataSet.SqlText);
        ValidateSql(dataSet.SqlText);
        return await ExecuteAsync(dataSet, null, limit);
    }

    /// <summary>
    /// Проверяет, что набор данных — системный расчёт.
    ///
    /// Вид определяется полем Kind. Старые наборы без этого поля считаются
    /// SQL-запросами, поэтому обратная совместимость сохраняется.
    /// </summary>
    public static bool IsSystemDataSet(ReportDataSet dataSet) =>
        string.Equals(dataSet.Kind, ReportDataSetKinds.System, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(dataSet.CalculatorCode);

    /// <summary>
    /// Выполняет системный расчёт и возвращает его результат как таблицу данных.
    /// </summary>
    private async Task<DataTable> ExecuteSystemCalculatorAsync(
        ReportDataSet dataSet,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var calculator = SystemReportCalculators.Find(dataSet.CalculatorCode);
        if (calculator == null)
        {
            throw new InvalidOperationException(
                $"Системный расчёт «{dataSet.CalculatorCode}» не найден. " +
                "Выберите расчёт из списка на экране наборов данных.");
        }

        return await calculator.Build(_context, parameters, CancellationToken.None);
    }

    public async Task RefreshFieldsFromSqlAsync(ReportDataSet dataSet)
    {
        // У системного расчёта колонки объявлены в справочнике расчётов:
        // запрашивать их из базы не нужно.
        if (IsSystemDataSet(dataSet))
        {
            await RefreshFieldsFromCalculatorAsync(dataSet);
            return;
        }

        dataSet.SqlText = NormalizeSql(dataSet.SqlText);
        ValidateSql(dataSet.SqlText);

        var schemaTable = await ExecuteAsync(dataSet, null, 0);
        var desiredFields = schemaTable.Columns
            .Cast<DataColumn>()
            .Select((column, index) => new ReportDataSetField
            {
                Id = Guid.NewGuid(),
                ReportDataSetId = dataSet.Id,
                DbColumnName = column.ColumnName,
                Name = BuildDisplayName(column.ColumnName),
                FieldType = MapType(column.DataType),
                Order = index + 1
            })
            .ToList();

        var dataSetId = dataSet.Id;
        var existingFields = await _context.ReportDataSetFields
            .Where(f => f.ReportDataSetId == dataSetId)
            .ToListAsync();
        _context.ReportDataSetFields.RemoveRange(existingFields);
        await _context.SaveChangesAsync();

        foreach (var field in desiredFields)
            _context.ReportDataSetFields.Add(field);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Записывает колонки системного расчёта.
    /// Берутся из справочника — они известны заранее, в отличие от SQL-запроса.
    /// </summary>
    private async Task RefreshFieldsFromCalculatorAsync(ReportDataSet dataSet)
    {
        var calculator = SystemReportCalculators.Find(dataSet.CalculatorCode);
        if (calculator == null)
            return;

        var dataSetId = dataSet.Id;
        var existingFields = await _context.ReportDataSetFields
            .Where(f => f.ReportDataSetId == dataSetId)
            .ToListAsync();
        _context.ReportDataSetFields.RemoveRange(existingFields);
        await _context.SaveChangesAsync();

        foreach (var field in SystemReportCalculators.BuildFieldRows(calculator, dataSetId))
            _context.ReportDataSetFields.Add(field);
        await _context.SaveChangesAsync();
    }

    public async Task SyncMetadataSourceAsync(ReportDataSet dataSet)
    {
        // ВАЖНО: лог 09:35 — сырой INSERT в MetadataFields падал по FK
        // (MetadataObjectId указывал на отсутствующий MetadataObject), после чего
        // PostgreSQL помечал транзакцию импорта как aborted (25P02) и валил всю
        // загрузку конфигурации. Поэтому: 1) сначала убеждаемся, что родитель
        // реально существует в БД; 2) сиротскую ссылку чиним вместо вставки;
        // 3) ошибку НЕ глотаем — импорт обязан откатить транзакцию, а не продолжать.
        // Отсоединяем dataSet и его поля, иначе EF Core попытается обновить их
        // после правок прямых SQL-команд и упадёт с DbUpdateConcurrencyException.
        foreach (var field in dataSet.Fields.ToList())
            _context.Entry(field).State = EntityState.Detached;
        _context.Entry(dataSet).State = EntityState.Detached;

        var configId = await _context.MetadataConfigurations
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync();

        var metadataId = dataSet.MetadataObjectId;
        if (!metadataId.HasValue)
        {
            // Try to find existing metadata by code reference
            var referenceJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [DataSetReferenceKey] = dataSet.Code
            });
            var foundId = await _context.MetadataObjects
                .Where(item => item.ObjectType == "ReportSource" &&
                               item.ReferenceFields != null &&
                               item.ReferenceFields.Contains(dataSet.Code))
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync();
            if (foundId.HasValue)
                metadataId = foundId;
        }

        if (!metadataId.HasValue)
        {
            // Create new metadata object via raw SQL
            metadataId = Guid.NewGuid();
            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MetadataObjects"" (""Id"", ""Name"", ""TableName"", ""ObjectType"", ""Description"", ""Icon"", ""Order"", ""IsSystem"", ""ParentId"", ""MetadataConfigId"", ""UsePostings"", ""UseBalances"", ""UseMovements"", ""BalanceTable"", ""MovementTable"", ""ReferenceFields"")
                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, NULL, @p8, false, false, false, NULL, NULL, @p9)",
                metadataId.Value,
                dataSet.Name,
                $"dataset_{SanitizeIdentifier(dataSet.Code)}",
                "ReportSource",
                dataSet.Description,
                "🧩",
                0,
                dataSet.IsSystem,
                configId,
                JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [DataSetReferenceKey] = dataSet.Code
                }));
        }
        else
        {
            // Ссылка могла остаться сиротской после ReplaceMetadataAsync при импорте
            // (в транзакции старый MetadataObjects удалён, а ReportDataSets.MetadataObjectId
            // ещё указывает на него). Проверяем родителя ДО вставки полей, иначе
            // получим FK 23503 и отравим транзакцию (25P02).
            var parentExists = await _context.MetadataObjects
                .AnyAsync(item => item.Id == metadataId.Value);
            if (!parentExists)
            {
                metadataId = Guid.NewGuid();
                await _context.Database.ExecuteSqlRawAsync(
                    @"INSERT INTO ""MetadataObjects"" (""Id"", ""Name"", ""TableName"", ""ObjectType"", ""Description"", ""Icon"", ""Order"", ""IsSystem"", ""ParentId"", ""MetadataConfigId"", ""UsePostings"", ""UseBalances"", ""UseMovements"", ""BalanceTable"", ""MovementTable"", ""ReferenceFields"")
                      VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, NULL, @p8, false, false, false, NULL, NULL, @p9)",
                    metadataId.Value,
                    dataSet.Name,
                    $"dataset_{SanitizeIdentifier(dataSet.Code)}",
                    "ReportSource",
                    dataSet.Description,
                    "🧩",
                    0,
                    dataSet.IsSystem,
                    configId,
                    JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        [DataSetReferenceKey] = dataSet.Code
                    }));
            }
            else
            {
                // Update existing metadata via raw SQL
                await _context.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""MetadataObjects""
                      SET ""Name"" = @p0, ""Description"" = @p1, ""TableName"" = @p2, ""IsSystem"" = @p3, ""Icon"" = @p4,
                          ""ReferenceFields"" = @p5
                      WHERE ""Id"" = @p6",
                    dataSet.Name,
                    dataSet.Description,
                    $"dataset_{SanitizeIdentifier(dataSet.Code)}",
                    dataSet.IsSystem,
                    "🧩",
                    JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        [DataSetReferenceKey] = dataSet.Code
                    }),
                    metadataId.Value);
            }
        }

        // Ниже MetadataFields пересоздаются прямым SQL. Если EF уже отслеживает
        // MetadataObject/поля этого объекта, их строки исчезнут в БД без ведома
        // трекера — следующий SaveChanges упадёт с DbUpdateConcurrencyException.
        DetachTrackedMetadataSource(metadataId.Value);

        // Update MetadataObjectId via raw SQL
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE \"ReportDataSets\" SET \"MetadataObjectId\" = @p0 WHERE \"Id\" = @p1",
            metadataId.Value, dataSet.Id);

        // Delete existing metadata fields via raw SQL
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"MetadataFields\" WHERE \"MetadataObjectId\" = @p0",
            metadataId.Value);

        // Insert new metadata fields via raw SQL
        var fields = await _context.ReportDataSetFields
            .Where(field => field.ReportDataSetId == dataSet.Id)
            .OrderBy(field => field.Order)
            .ToListAsync();

        foreach (var field in fields)
        {
            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MetadataFields"" (""Id"", ""MetadataObjectId"", ""Name"", ""DbColumnName"", ""FieldType"", ""Length"", ""Precision"", ""Scale"", ""IsRequired"", ""IsUnique"", ""Order"", ""ReferenceCatalog"", ""Formula"", ""DisplayPattern"", ""DisplayFields"")
                  VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, false, false, @p8, NULL, NULL, NULL, NULL)",
                Guid.NewGuid(),
                metadataId.Value,
                field.Name,
                field.DbColumnName,
                field.FieldType,
                field.FieldType == "String" ? 500 : 0,
                18,
                2,
                field.Order);
        }
    }

    /// <summary>
    /// Отсоединяет от трекера MetadataObject и его поля, если они уже загружены в памяти.
    /// Нужно перед прямым SQL, который пересоздаёт/удаляет строки MetadataObjects/MetadataFields:
    /// иначе трекер продолжит считать их существующими и следующий SaveChanges упадёт
    /// с DbUpdateConcurrencyException.
    /// </summary>
    private void DetachTrackedMetadataSource(Guid metadataObjectId)
    {
        var objectEntry = _context.ChangeTracker.Entries<MetadataObject>()
            .FirstOrDefault(entry => entry.Entity.Id == metadataObjectId);
        if (objectEntry != null)
        {
            foreach (var field in objectEntry.Entity.Fields.ToList())
            {
                var fieldEntry = _context.Entry(field);
                if (fieldEntry.State != EntityState.Detached)
                    fieldEntry.State = EntityState.Detached;
            }

            objectEntry.State = EntityState.Detached;
        }

        foreach (var fieldEntry in _context.ChangeTracker.Entries<MetadataField>()
                     .Where(entry => entry.Entity.MetadataObjectId == metadataObjectId)
                     .ToList())
        {
            fieldEntry.State = EntityState.Detached;
        }
    }

    private async Task EnsureCashOrderTurnoverDataSetAsync()
    {
        var dataSet = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item => item.Code == CashOrderTurnoverDataSetCode);

        if (dataSet == null)
        {
            dataSet = new ReportDataSet
            {
                Id = Guid.NewGuid(),
                Code = CashOrderTurnoverDataSetCode,
                Name = MetadataService.CashOrderTurnoverReportSourceName,
                Description = "SQL-набор данных для кассовой книги и реестра приходов/расходов: группировка РКО/ПКО по корреспондентскому счету.",
                SqlText = BuildCashOrderTurnoverSql(),
                IsActive = true,
                IsSystem = true
            };
            await _context.ReportDataSets.AddAsync(dataSet);
            await _context.SaveChangesAsync();
        }
        else
        {
            dataSet.Name = MetadataService.CashOrderTurnoverReportSourceName;
            dataSet.Description = string.IsNullOrWhiteSpace(dataSet.Description)
                ? "SQL-набор данных для кассовой книги и реестра приходов/расходов."
                : dataSet.Description;
            dataSet.IsActive = true;
            dataSet.IsSystem = true;

            if (string.IsNullOrWhiteSpace(dataSet.SqlText))
                dataSet.SqlText = BuildCashOrderTurnoverSql();

            await _context.SaveChangesAsync();
        }

        try
        {
            if (!dataSet.Fields.Any())
            {
                await RefreshFieldsFromSqlAsync(dataSet);
                dataSet = await _context.ReportDataSets
                    .Include(item => item.Fields)
                    .FirstAsync(item => item.Id == dataSet.Id);
            }

            await SyncMetadataSourceAsync(dataSet);
        }
        catch (Exception ex)
        {
            // Ошибка проглатывается, но прямой SQL мог уже удалить строки MetadataFields,
            // оставшиеся в трекере. Чистим трекер, чтобы не отравить следующий SaveChanges.
            if (ex is DbUpdateConcurrencyException)
            {
                await DbUpdateConcurrencyDiagnostics.LogAsync(
                    _context, ex, nameof(ReportDataSetService), nameof(EnsureCashOrderTurnoverDataSetAsync));
            }

            _context.ChangeTracker.Clear();
            SystemLogService.Error("Ошибка подготовки системного SQL-набора данных РКО/ПКО. Конфигуратор продолжит загрузку.", "ReportDataSetService", ex);
        }
    }

    private static string BuildCashOrderTurnoverSql() => @"
WITH raw_orders AS (
    SELECT
        COALESCE(CAST(""Id"" AS text), '') AS id_text,
        COALESCE(""doc_date"", NOW())::date AS report_date,
        COALESCE(""doc_number"", '') AS doc_number,
        COALESCE(""order_kind"", '') AS order_kind_raw,
        CASE
            WHEN LOWER(COALESCE(""order_kind""::text, '')) = 'receipt'
              OR LOWER(COALESCE(""order_kind""::text, '')) LIKE '%приход%'
            THEN true
            ELSE false
        END AS is_receipt,
        COALESCE(CAST(""cash_desk_id"" AS text), '') AS cash_desk,
        COALESCE(NULLIF(""debit_account"", ''), NULLIF(""cash_account"", ''), '') AS debit_account,
        COALESCE(NULLIF(""credit_account"", ''), NULLIF(""correspondent_account"", ''), '') AS credit_account,
        COALESCE(""amount"", 0) AS amount,
        COALESCE(""basis"", '') AS basis,
        COALESCE(""description"", '') AS description
    FROM ""doc_cash_orders""
    WHERE COALESCE(""is_posted"", false) = true
      AND (@CashDeskId = '' OR CAST(""cash_desk_id"" AS text) = @CashDeskId)
),
base_orders AS (
    SELECT
        raw_orders.*,
        CASE WHEN is_receipt THEN debit_account ELSE credit_account END AS cash_account,
        CASE WHEN is_receipt THEN credit_account ELSE debit_account END AS correspondent_account
    FROM raw_orders
),
filtered_orders AS (
    SELECT *
    FROM base_orders
    WHERE report_date >= CAST(@PeriodStart AS date)
      AND report_date <= CAST(@PeriodEnd AS date)
),
grouped AS (
    SELECT
        report_date,
        cash_desk,
        cash_account,
        correspondent_account,
        is_receipt,
        MIN(doc_number) AS doc_number,
        STRING_AGG(NULLIF(doc_number, ''), ', ' ORDER BY doc_number) AS document_numbers,
        COUNT(*) AS document_count,
        SUM(CASE WHEN is_receipt THEN amount ELSE 0 END) AS sum_debet,
        SUM(CASE WHEN is_receipt THEN 0 ELSE amount END) AS sum_credit,
        SUM(amount) AS amount,
        STRING_AGG(NULLIF(basis, ''), '; ' ORDER BY doc_number) AS basis,
        STRING_AGG(NULLIF(description, ''), '; ' ORDER BY doc_number) AS description
    FROM filtered_orders
    GROUP BY report_date, cash_desk, cash_account, correspondent_account, is_receipt
),
with_balances AS (
    SELECT
        grouped.*,
        COALESCE((
            SELECT SUM(CASE WHEN history.is_receipt THEN history.amount ELSE -history.amount END)
            FROM base_orders history
            WHERE history.cash_desk = grouped.cash_desk
              AND history.cash_account = grouped.cash_account
              AND history.report_date < grouped.report_date
        ), 0) AS opening_balance
    FROM grouped
)
SELECT
    report_date,
    report_date AS d_xls,
    COALESCE(document_numbers, doc_number, '') AS doc_number,
    COALESCE(document_numbers, doc_number, '') AS dok,
    COALESCE(cash_desk, '') AS nuch,
    COALESCE(cash_desk, '') AS d_nuch,
    CASE WHEN is_receipt THEN 'Приходный' ELSE 'Расходный' END AS order_kind,
    cash_desk,
    cash_account,
    correspondent_account,
    opening_balance,
    sum_debet,
    sum_debet AS sum_debit,
    sum_credit,
    opening_balance + sum_debet - sum_credit AS closing_balance,
    sum_debet AS deb,
    sum_credit AS cred,
    amount AS sum,
    COALESCE(NULLIF(basis, ''), NULLIF(description, ''), CASE WHEN is_receipt THEN 'Приход' ELSE 'Расход' END) AS tex,
    correspondent_account AS name_kod,
    COALESCE(basis, '') AS basis,
    COALESCE(description, '') AS description,
    document_count,
    'Финансы' AS module
FROM with_balances
ORDER BY report_date, cash_account, correspondent_account, is_receipt DESC";

    private static string NormalizeSql(string sql)
    {
        sql = StripSqlComments(sql ?? string.Empty).Trim();
        while (sql.EndsWith(";", StringComparison.Ordinal))
            sql = sql[..^1].TrimEnd();
        return sql;
    }

    private static string StripSqlComments(string sql)
    {
        var builder = new StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (!inDoubleQuote && current == '\'')
            {
                builder.Append(current);
                if (inSingleQuote && next == '\'')
                {
                    builder.Append(next);
                    index++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && current == '"')
            {
                builder.Append(current);
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && current == '-' && next == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] != '\r' && sql[index] != '\n')
                    index++;

                if (index < sql.Length)
                    builder.Append(sql[index]);
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && current == '/' && next == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && !(sql[index] == '*' && sql[index + 1] == '/'))
                    index++;
                index++;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code)
            ? $"dataset.{Guid.NewGuid():N}"
            : code.Trim().ToLowerInvariant();

    private static void ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL набора данных пустой.");

        var trimmed = sql.TrimStart();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Набор данных может содержать только SELECT или WITH ... SELECT.");

        if (HasCommandSeparatorOutsideStrings(sql))
            throw new InvalidOperationException("В SQL набора данных запрещены несколько команд. Оставьте один SELECT-запрос.");

        if (ForbiddenSqlRegex.IsMatch(sql))
            throw new InvalidOperationException("SQL набора данных содержит запрещенную команду. Разрешено только чтение данных.");
    }

    private static bool HasCommandSeparatorOutsideStrings(string sql)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (!inDoubleQuote && current == '\'')
            {
                if (inSingleQuote && next == '\'')
                {
                    index++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && current == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && current == ';')
                return true;
        }

        return false;
    }

    private static void AddSqlParameters(
        System.Data.Common.DbCommand command,
        string sql,
        IReadOnlyDictionary<string, object>? parameters)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["PeriodStart"] = DateTime.Today.AddDays(1 - DateTime.Today.Day),
            ["PeriodEnd"] = DateTime.Today,
            ["CashDeskId"] = string.Empty,
            ["OrganizationId"] = string.Empty,
            ["EmployeeId"] = string.Empty,
            ["AccountCode"] = string.Empty,
            ["UserName"] = ServiceLocator.AuthService.CurrentUser?.Login ?? string.Empty
        };

        if (parameters != null)
        {
            foreach (var pair in parameters)
            {
                var key = pair.Key.TrimStart('@');
                values[key] = pair.Value ?? DBNull.Value;
            }
        }

        foreach (var pair in values)
        {
            if (!sql.Contains("@" + pair.Key, StringComparison.OrdinalIgnoreCase))
                continue;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + pair.Key;
            parameter.Value = pair.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    private static string? TryReadDataSetCode(string? referenceFields)
    {
        if (string.IsNullOrWhiteSpace(referenceFields))
            return null;

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(referenceFields);
            return values != null && values.TryGetValue(DataSetReferenceKey, out var code)
                ? code
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDisplayName(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return "Поле";

        return string.Join(" ", columnName
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string MapType(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type) ?? type;
        if (nullableType == typeof(DateTime) || nullableType == typeof(DateOnly))
            return "DateTime";
        if (nullableType == typeof(decimal) || nullableType == typeof(double) || nullableType == typeof(float))
            return "Decimal";
        if (nullableType == typeof(int) || nullableType == typeof(long) || nullableType == typeof(short))
            return "Int";
        if (nullableType == typeof(bool))
            return "Bool";
        if (nullableType == typeof(Guid))
            return "Reference";
        return "String";
    }

    private static string SanitizeIdentifier(string value)
    {
        var result = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9_]+", "_");
        return string.IsNullOrWhiteSpace(result) ? "report_source" : result.Trim('_');
    }
}





