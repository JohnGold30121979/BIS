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

    private static readonly Regex ForbiddenSqlRegex = new(
        @"\b(insert|update|delete|drop|alter|create|truncate|grant|revoke|copy|call|execute|merge|vacuum|analyze|do|begin|commit|rollback)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppDbContext _context;

    public ReportDataSetService(AppDbContext context)
    {
        _context = context;
    }

    public async Task EnsureSchemaAsync()
    {
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""ReportDataSets"" (
                ""Id"" uuid NOT NULL,
                ""Code"" varchar(160) NOT NULL,
                ""Name"" varchar(300) NOT NULL,
                ""Description"" varchar(1000) NOT NULL DEFAULT '',
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
        ");
    }

    public async Task EnsureStandardDataSetsAsync()
    {
        await EnsureSchemaAsync();
        await EnsureCashOrderTurnoverDataSetAsync();
    }

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
        dataSet.SqlText = NormalizeSql(dataSet.SqlText);
        ValidateSql(dataSet.SqlText);
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
        var sql = NormalizeSql(dataSet.SqlText);
        ValidateSql(sql);

        var useLimit = limit.HasValue;
        var wrappedSql = useLimit
            ? $"SELECT * FROM ({sql}) AS report_dataset_result LIMIT @__bis_limit"
            : $"SELECT * FROM ({sql}) AS report_dataset_result";

        var table = new DataTable(dataSet.Name);
        using var command = _context.Database.GetDbConnection().CreateCommand();
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

        var opened = false;
        try
        {
            await _context.Database.OpenConnectionAsync();
            opened = true;
            using var reader = await command.ExecuteReaderAsync();
            table.Load(reader);
        }
        finally
        {
            if (opened)
                await _context.Database.CloseConnectionAsync();
        }

        return table;
    }

    public async Task<DataTable> TestAsync(ReportDataSet dataSet, int limit = 50)
    {
        dataSet.SqlText = NormalizeSql(dataSet.SqlText);
        ValidateSql(dataSet.SqlText);
        return await ExecuteAsync(dataSet, null, limit);
    }

    public async Task RefreshFieldsFromSqlAsync(ReportDataSet dataSet)
    {
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

        var existing = await _context.ReportDataSets
            .Include(item => item.Fields)
            .FirstAsync(item => item.Id == dataSet.Id);

        _context.ReportDataSetFields.RemoveRange(existing.Fields);
        foreach (var field in desiredFields)
            existing.Fields.Add(field);

        await _context.SaveChangesAsync();
    }

    public async Task SyncMetadataSourceAsync(ReportDataSet dataSet)
    {
        var configId = await _context.MetadataConfigurations
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync();

        var metadata = dataSet.MetadataObjectId.HasValue
            ? await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.Id == dataSet.MetadataObjectId.Value)
            : null;

        metadata ??= await _context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "ReportSource" &&
                item.ReferenceFields != null &&
                item.ReferenceFields.Contains(dataSet.Code));
        metadata ??= await _context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "ReportSource" &&
                (item.Name == dataSet.Name ||
                 item.TableName == $"dataset_{SanitizeIdentifier(dataSet.Code)}" ||
                 (dataSet.Code == CashOrderTurnoverDataSetCode &&
                  item.TableName == MetadataService.CashOrderTurnoverReportSourceTableName)));

        if (metadata == null)
        {
            metadata = new MetadataObject
            {
                Id = Guid.NewGuid(),
                ObjectType = "ReportSource",
                Icon = "🧩",
                IsSystem = dataSet.IsSystem,
                MetadataConfigId = configId,
                Fields = new List<MetadataField>()
            };
            await _context.MetadataObjects.AddAsync(metadata);
        }

        metadata.Name = dataSet.Name;
        metadata.Description = dataSet.Description;
        metadata.TableName = $"dataset_{SanitizeIdentifier(dataSet.Code)}";
        metadata.ReferenceFields = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [DataSetReferenceKey] = dataSet.Code
        });
        metadata.IsSystem = dataSet.IsSystem;
        metadata.Icon = string.IsNullOrWhiteSpace(metadata.Icon) ? "🧩" : metadata.Icon;

        dataSet.MetadataObjectId = metadata.Id;

        var currentFields = metadata.Fields.ToList();
        _context.MetadataFields.RemoveRange(currentFields);

        var fields = await _context.ReportDataSetFields
            .Where(field => field.ReportDataSetId == dataSet.Id)
            .OrderBy(field => field.Order)
            .ToListAsync();

        foreach (var field in fields)
        {
            metadata.Fields.Add(new MetadataField
            {
                Id = Guid.NewGuid(),
                MetadataObjectId = metadata.Id,
                Name = field.Name,
                DbColumnName = field.DbColumnName,
                FieldType = field.FieldType,
                Length = field.FieldType == "String" ? 500 : 0,
                Precision = 18,
                Scale = 2,
                Order = field.Order
            });
        }

        await _context.SaveChangesAsync();
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
                await RefreshFieldsFromSqlAsync(dataSet);

            await SyncMetadataSourceAsync(dataSet);
        }
        catch (Exception ex)
        {
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





