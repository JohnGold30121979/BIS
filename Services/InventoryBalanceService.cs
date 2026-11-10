using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BIS.ERP.Services;

internal sealed class InventoryBalanceService(AppDbContext context)
{
    internal const string EnsureCostColumnsSql = """
        ALTER TABLE IF EXISTS public.doc_inventory_outgoing_lines ADD COLUMN IF NOT EXISTS inventory_cost numeric(18,2);
        ALTER TABLE IF EXISTS public.doc_inventory_movement_lines ADD COLUMN IF NOT EXISTS inventory_cost numeric(18,2);
        """;

    internal const string CreateViewsSql = """
        CREATE OR REPLACE VIEW public.inventory_material_movements AS
        SELECT header."Id" AS document_id, 'doc_inventory_incoming'::text AS document_table,
               header.doc_date AS movement_date, line.material_id, line.site_id::text AS site_id,
               line.responsible_person_id::text AS responsible_person_id, line.account_code,
               line.quantity AS quantity_change, line.amount_without_tax AS recorded_value_change,
               header."CreatedAt" AS document_created_at
        FROM public.doc_inventory_incoming header
        JOIN public.doc_inventory_incoming_lines line ON line.incoming_id = header."Id"
        WHERE header.is_posted AND line.material_id IS NOT NULL
        UNION ALL
        SELECT header."Id", 'doc_inventory_outgoing', header.doc_date,
               line.material_id, line.site_id::text, line.responsible_person_id::text, line.account_code,
               -line.quantity, -COALESCE(line.inventory_cost, line.amount_without_tax), header."CreatedAt"
        FROM public.doc_inventory_outgoing header
        JOIN public.doc_inventory_outgoing_lines line ON line.outgoing_id = header."Id"
        WHERE header.is_posted AND line.material_id IS NOT NULL
        UNION ALL
        SELECT header."Id", 'doc_inventory_movement', header.doc_date, line.material_id,
               movement.site_id, movement.responsible_person_id, movement.account_code,
               movement.direction * line.quantity,
               movement.direction * COALESCE(line.inventory_cost, line.amount_without_tax), header."CreatedAt"
        FROM public.doc_inventory_movement header
        JOIN public.doc_inventory_movement_lines line ON line.movement_id = header."Id"
        LEFT JOIN public.catalog_plan_schetov account
               ON account."Id"::text = header.counterparty_account::text
        CROSS JOIN LATERAL (VALUES
            (line.site_id::text, line.responsible_person_id::text, line.account_code, -1),
            (header.destination_site_id::text, header.destination_person_id::text,
                COALESCE(account.code, NULLIF(header.counterparty_account::text, ''), line.account_code), 1)
        ) AS movement(site_id, responsible_person_id, account_code, direction)
        WHERE header.is_posted AND line.material_id IS NOT NULL;

        CREATE OR REPLACE VIEW public.inventory_material_balances AS
        SELECT material_id, site_id, responsible_person_id, account_code,
               SUM(quantity_change) AS quantity, SUM(recorded_value_change) AS recorded_value
        FROM public.inventory_material_movements
        GROUP BY material_id, site_id, responsible_person_id, account_code
        HAVING SUM(quantity_change) <> 0 OR SUM(recorded_value_change) <> 0;
        """;

    public async Task<Dictionary<(Guid MaterialId, string AccountCode), decimal>> ReadAvailableAsync(
        Guid siteId, Guid responsiblePersonId, DateTime documentDate, Guid? excludedDocumentId)
    {
        var quantities = new Dictionary<(Guid, string), decimal>();
        var openedHere = context.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (openedHere)
            await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT material_id, trim(COALESCE(account_code, '')), SUM(quantity_change)
                FROM public.inventory_material_movements
                WHERE site_id = @site AND responsible_person_id = @person
                  AND movement_date < @endOfDay AND document_id <> @excludedDocument
                GROUP BY 1, 2;
                """;
            command.Parameters.Add(new NpgsqlParameter("site", siteId.ToString()));
            command.Parameters.Add(new NpgsqlParameter("person", responsiblePersonId.ToString()));
            command.Parameters.Add(new NpgsqlParameter("endOfDay", documentDate.Date.AddDays(1)));
            command.Parameters.Add(new NpgsqlParameter("excludedDocument", excludedDocumentId ?? Guid.Empty));

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                quantities.Add((reader.GetGuid(0), reader.GetString(1)), reader.GetDecimal(2));

            return quantities;
        }
        finally
        {
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }
    }

    internal record DocumentOrder(Guid Id, string Table, DateTime Date, DateTime CreatedAt);

    internal async Task<List<DocumentOrder>> ReadCostingDocumentsAsync(DateTime fromDate, Guid? currentDocumentId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT "Id", document_table, doc_date, "CreatedAt" FROM (
                SELECT "Id", 'doc_inventory_outgoing' AS document_table, doc_date, "CreatedAt", is_posted
                FROM public.doc_inventory_outgoing
                UNION ALL
                SELECT "Id", 'doc_inventory_movement', doc_date, "CreatedAt", is_posted
                FROM public.doc_inventory_movement
            ) documents
            WHERE doc_date >= @fromDate AND (is_posted OR "Id" = @currentDocument)
            ORDER BY doc_date, "CreatedAt", "Id", document_table;
            """;
        command.Parameters.Add(new NpgsqlParameter("fromDate", fromDate));
        command.Parameters.Add(new NpgsqlParameter("currentDocument", currentDocumentId ?? Guid.Empty));
        var documents = new List<DocumentOrder>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            documents.Add(new DocumentOrder(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2), reader.GetDateTime(3)));
        return documents;
    }

    internal async Task<Dictionary<(Guid MaterialId, string Site, string Person, string Account), (decimal Quantity, decimal Value)>>
        ReadBeforeAsync(DocumentOrder order, Guid[] materialIds)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT material_id, site_id, responsible_person_id, trim(COALESCE(account_code, '')),
                   SUM(quantity_change), SUM(recorded_value_change)
            FROM public.inventory_material_movements
            WHERE material_id = ANY(@materials) AND site_id IS NOT NULL AND responsible_person_id IS NOT NULL
              AND (movement_date, document_created_at, document_id, document_table)
                  < (@date, @created, @id, @table)
            GROUP BY 1, 2, 3, 4;
            """;
        command.Parameters.Add(new NpgsqlParameter("materials", materialIds));
        command.Parameters.Add(new NpgsqlParameter("date", order.Date));
        command.Parameters.Add(new NpgsqlParameter("created", order.CreatedAt));
        command.Parameters.Add(new NpgsqlParameter("id", order.Id));
        command.Parameters.Add(new NpgsqlParameter("table", order.Table));
        var quantities = new Dictionary<(Guid, string, string, string), (decimal, decimal)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            quantities.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)),
                (reader.GetDecimal(4), reader.GetDecimal(5)));
        return quantities;
    }

    public async Task<List<Dictionary<string, object>>> ReadAsync()
    {
        var rows = new List<Dictionary<string, object>>();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COALESCE(material.code, ''), COALESCE(material.name, balance.material_id::text),
                       COALESCE(site.site_name, balance.site_id::text, ''),
                       COALESCE(person.full_name, balance.responsible_person_id::text, ''),
                       COALESCE(balance.account_code, ''), balance.quantity, balance.recorded_value
                FROM public.inventory_material_balances balance
                LEFT JOIN public.catalog_materials material ON material."Id" = balance.material_id
                LEFT JOIN public.catalog_sites site ON site."Id"::text = balance.site_id
                LEFT JOIN public.catalog_responsible_persons person ON person."Id"::text = balance.responsible_person_id
                ORDER BY material.code, site.site_name, person.full_name, balance.account_code;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["Код"] = reader.GetString(0),
                    ["Материал"] = reader.GetString(1),
                    ["Участок"] = reader.GetString(2),
                    ["Материально ответственное лицо"] = reader.GetString(3),
                    ["Счёт хранения"] = reader.GetString(4),
                    ["Количество"] = reader.GetDecimal(5),
                    ["Сумма по документам"] = reader.GetDecimal(6)
                });
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        return rows;
    }
}
