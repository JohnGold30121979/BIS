using System.Data;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BIS.ERP.Services;

internal static class InventoryTurnoverReport
{
    internal const string MaterialCode = "standard.inventory.material-turnover";
    internal const string ResponsiblePersonCode = "standard.inventory.responsible-person-turnover";
    internal const string MaterialName = "Оборотная ведомость по материалам";
    internal const string ResponsiblePersonName = "Оборотная ведомость по материально ответственным лицам";
    internal const string ReportType = "InventoryTurnover";
    internal const string MaterialDataSetCode = "system.inventory-material-turnover";
    internal const string ResponsiblePersonDataSetCode = "system.inventory-responsible-person-turnover";

    private const string ReportTitleField = "report_title";
    private const string PeriodField = "period_text";
    private static readonly string[] PeriodGroups = { "На начало", "Приход", "Расход", "На конец" };

    private static IEnumerable<ClosedXmlColumn> BuildColumns(bool byPerson)
    {
        if (byPerson)
        {
            yield return new() { Field = "Материально ответственное лицо", Width = 28 };
            yield return new() { Field = "Участок", Width = 19 };
        }

        yield return new() { Field = "Код материала", Width = 13 };
        yield return new() { Field = "Материал", Width = 34 };
        yield return new() { Field = "Единица измерения", Width = 10 };
        yield return new() { Field = "Счёт хранения", Width = 13 };

        foreach (var group in PeriodGroups)
        {
            yield return new() { Field = $"{group}: количество", Width = 13, NumberFormat = "#,##0.####" };
            yield return new() { Field = $"{group}: сумма", Width = 15, NumberFormat = "#,##0.00" };
        }
    }

    internal static IReadOnlyList<SystemCalculatorField> GetDataSetFields(bool byPerson)
    {
        var columns = BuildColumns(byPerson).ToList();
        var dimensionCount = columns.Count - PeriodGroups.Length * 2;
        var fields = columns.Select((column, index) =>
            new SystemCalculatorField(column.Field, column.Field, index < dimensionCount ? "String" : "Number")).ToList();
        fields.Add(new SystemCalculatorField(ReportTitleField, "Заголовок ведомости"));
        fields.Add(new SystemCalculatorField(PeriodField, "Период ведомости"));
        return fields;
    }

    public static ClosedXmlReportLayout CreateLayout(string title, bool byPerson)
    {
        var columns = BuildColumns(byPerson).ToList();
        var dimensionCount = columns.Count - PeriodGroups.Length * 2;
        var groups = new ClosedXmlHeaderLevel();
        var captions = new ClosedXmlHeaderLevel();
        for (var index = 0; index < dimensionCount; index++)
            groups.Cells.Add(new ClosedXmlHeaderCell { Column = index + 1, RowSpan = 2, Text = columns[index].Field });

        for (var index = 0; index < PeriodGroups.Length; index++)
        {
            var column = dimensionCount + 1 + index * 2;
            groups.Cells.Add(new ClosedXmlHeaderCell { Column = column, MergeToColumn = column + 1, Text = PeriodGroups[index] });
            captions.Cells.Add(new ClosedXmlHeaderCell { Column = column, Text = "Количество" });
            captions.Cells.Add(new ClosedXmlHeaderCell { Column = column + 1, Text = "Сумма" });
        }

        var table = new ClosedXmlTableSection { HeaderFill = "", RowBorder = "All", SkipEmptyRows = false };
        foreach (var column in columns)
            table.Columns.Add(column);
        table.HeaderLevels.Add(groups);
        table.HeaderLevels.Add(captions);
        table.EmphasisRules.Add(new ClosedXmlEmphasisRule { Text = "ИТОГО", MergeLabelToColumn = dimensionCount, Bold = true });
        return new ClosedXmlReportLayout
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = byPerson ? "Обороты по ответственным лицам" : "Обороты по материалам",
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape", FitToPagesWide = 1, ShowGridLines = false,
                        FontName = "Segoe UI", FontSize = 9, FreezeRows = 4
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows", Name = title,
                            Rows =
                            {
                                new ClosedXmlTextRow { Row = 1, Cells = { new ClosedXmlTextCell
                                {
                                    Column = 1, MergeToColumn = columns.Count, TakeFromRow = 1,
                                    TakeFromField = ReportTitleField, Bold = true, Align = "Center"
                                }}},
                                new ClosedXmlTextRow { Row = 2, Cells = { new ClosedXmlTextCell
                                {
                                    Column = 1, MergeToColumn = columns.Count, TakeFromRow = 1,
                                    TakeFromField = PeriodField, Align = "Center"
                                }}}
                            }
                        },
                        new ClosedXmlBlock { Type = "TabularSection", Name = "Обороты и остатки", Table = table }
                    }
                }
            }
        };
    }

    public static async Task<DataTable> LoadAsync(
        AppDbContext context, DateTime start, DateTime end, bool byPerson, bool excludeInternalTransfers)
    {
        if (end.Date < start.Date)
            throw new ArgumentException("Дата окончания периода не может быть раньше даты начала.");

        var excludeTransfers = !byPerson && excludeInternalTransfers;
        var title = byPerson ? ResponsiblePersonName : MaterialName;
        var period = $"За период с {start:dd.MM.yyyy} по {end:dd.MM.yyyy}";
        if (excludeTransfers)
            period += " · Без перемещений внутри счёта хранения";

        var table = new DataTable(title);
        var columns = BuildColumns(byPerson).ToList();
        var dimensionCount = columns.Count - PeriodGroups.Length * 2;
        for (var index = 0; index < columns.Count; index++)
            table.Columns.Add(columns[index].Field, index < dimensionCount ? typeof(string) : typeof(decimal));
        table.Columns.Add(ReportTitleField, typeof(string));
        table.Columns.Add(PeriodField, typeof(string));

        var openedHere = context.Database.GetDbConnection().State != ConnectionState.Open;
        if (openedHere)
            await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                WITH source AS (
                    SELECT document_id, document_table, movement_date, material_id,
                           CASE WHEN @byPerson THEN NULLIF(trim(site_id), '') END AS site_id,
                           CASE WHEN @byPerson THEN NULLIF(trim(responsible_person_id), '') END AS person_id,
                           trim(COALESCE(account_code, '')) AS account_code, quantity_change, recorded_value_change
                    FROM public.inventory_material_movements
                    WHERE movement_date < @endExclusive
                ), movements AS (
                    SELECT * FROM source
                    WHERE NOT @excludeTransfers OR document_table <> 'doc_inventory_movement'
                    UNION ALL
                    SELECT document_id, document_table, movement_date, material_id, site_id, person_id, account_code,
                           SUM(quantity_change), SUM(recorded_value_change)
                    FROM source
                    WHERE @excludeTransfers AND document_table = 'doc_inventory_movement'
                    GROUP BY document_id, document_table, movement_date, material_id, site_id, person_id, account_code
                ), totals AS (
                    SELECT material_id, site_id, person_id, account_code,
                           COALESCE(SUM(quantity_change) FILTER (WHERE movement_date < @start), 0) AS opening_quantity,
                           COALESCE(SUM(recorded_value_change) FILTER (WHERE movement_date < @start), 0) AS opening_value,
                           COALESCE(SUM(quantity_change) FILTER (WHERE movement_date >= @start AND quantity_change > 0), 0) AS receipt_quantity,
                           COALESCE(SUM(recorded_value_change) FILTER (WHERE movement_date >= @start AND
                               (quantity_change > 0 OR (quantity_change = 0 AND recorded_value_change > 0))), 0) AS receipt_value,
                           -COALESCE(SUM(quantity_change) FILTER (WHERE movement_date >= @start AND quantity_change < 0), 0) AS issue_quantity,
                           -COALESCE(SUM(recorded_value_change) FILTER (WHERE movement_date >= @start AND
                               (quantity_change < 0 OR (quantity_change = 0 AND recorded_value_change < 0))), 0) AS issue_value
                    FROM movements
                    GROUP BY material_id, site_id, person_id, account_code
                )
                SELECT COALESCE(person.full_name, totals.person_id, 'Не указано'),
                       COALESCE(site.site_name, totals.site_id, 'Не указан'),
                       COALESCE(material.code, ''), COALESCE(material.name, totals.material_id::text),
                       COALESCE(material.unit, ''), totals.account_code,
                       opening_quantity, opening_value, receipt_quantity, receipt_value, issue_quantity, issue_value
                FROM totals
                LEFT JOIN public.catalog_materials material ON material."Id" = totals.material_id
                LEFT JOIN public.catalog_sites site ON site."Id"::text = totals.site_id
                LEFT JOIN public.catalog_responsible_persons person ON person."Id"::text = totals.person_id
                WHERE opening_quantity <> 0 OR opening_value <> 0 OR receipt_quantity <> 0
                   OR receipt_value <> 0 OR issue_quantity <> 0 OR issue_value <> 0
                ORDER BY 1, 2, 3, 4, totals.account_code, totals.person_id, totals.site_id, totals.material_id;
                """;
            command.Parameters.Add(new NpgsqlParameter("byPerson", byPerson));
            command.Parameters.Add(new NpgsqlParameter("excludeTransfers", excludeTransfers));
            command.Parameters.Add(new NpgsqlParameter("start", DateTime.SpecifyKind(start.Date, DateTimeKind.Unspecified)));
            command.Parameters.Add(new NpgsqlParameter("endExclusive",
                DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Unspecified)));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = table.NewRow();
                for (var index = 0; index < dimensionCount; index++)
                    row[index] = reader.GetString(index + (byPerson ? 0 : 2));

                for (var index = 0; index < 6; index++)
                    row[dimensionCount + index] = reader.GetDecimal(6 + index);

                row[dimensionCount + 6] = reader.GetDecimal(6) + reader.GetDecimal(8) - reader.GetDecimal(10);
                row[dimensionCount + 7] = reader.GetDecimal(7) + reader.GetDecimal(9) - reader.GetDecimal(11);
                table.Rows.Add(row);
            }
        }
        finally
        {
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }

        var footer = table.NewRow();
        footer[0] = table.Rows.Count == 0 ? "Движений и остатков за период нет" : "ИТОГО";
        if (table.Rows.Count > 0)
        {
            // Количество разных материалов и единиц измерения не суммируется.
            for (var index = dimensionCount + 1; index < columns.Count; index += 2)
                footer[index] = table.Rows.Cast<DataRow>().Sum(row => row.Field<decimal>(index));
        }
        table.Rows.Add(footer);
        table.Rows[0][ReportTitleField] = title;
        table.Rows[0][PeriodField] = period;
        return table;
    }
}
