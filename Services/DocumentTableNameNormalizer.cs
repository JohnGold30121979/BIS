using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    /// <summary>Результат нормализации имени таблицы одного документа.</summary>
    public sealed class DocumentTableRenameResult
    {
        public string DocumentName { get; init; } = string.Empty;
        public string OldTableName { get; init; } = string.Empty;
        public string NewTableName { get; init; } = string.Empty;

        /// <summary>Физическая таблица переименована.</summary>
        public bool Renamed { get; init; }

        /// <summary>Причина, по которой переименование пропущено (если пропущено).</summary>
        public string? SkippedReason { get; init; }
    }

    /// <summary>
    /// Приводит имена таблиц документов ОС/ТМЦ к канону <see cref="DocumentTableNames"/>.
    ///
    /// Что делает для каждого документа, имя которого есть в реестре:
    /// <list type="number">
    /// <item>если <c>MetadataObjects.TableName</c> уже канонический — ничего;</item>
    /// <item>если физическая таблица есть, а целевой нет — <c>ALTER TABLE ... RENAME TO</c>
    ///   и переименование PK в <c>&lt;таблица&gt;_pkey</c>;</item>
    /// <item>если физической таблицы нет — правится только запись метаданных
    ///   (следующее создание таблицы пойдёт уже под каноническим именем);</item>
    /// <item>если существуют обе таблицы — ничего не трогаем и сообщаем причину:
    ///   нельзя молча отвязать метаданные от таблицы с данными.</item>
    /// </list>
    ///
    /// Идемпотентно: повторный запуск на нормализованной базе не делает ни одного DDL.
    /// </summary>
    public sealed class DocumentTableNameNormalizer
    {
        private readonly AppDbContext _context;

        /// <summary>Историческое имя таблицы строк «Учета движения ОС».</summary>
        private const string LegacyMovementLinesTable = "doc_fixed_asset_movement_lines";

        public DocumentTableNameNormalizer(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<DocumentTableRenameResult>> NormalizeAsync()
        {
            var documents = await _context.MetadataObjects
                .Where(item => item.ObjectType == "Document")
                .ToListAsync();

            var results = new List<DocumentTableRenameResult>();

            if (await NormalizeMovementLinesTableAsync())
            {
                results.Add(new DocumentTableRenameResult
                {
                    DocumentName = "Учет движения ОС (строки)",
                    OldTableName = LegacyMovementLinesTable,
                    NewTableName = DocumentTableNames.AssetMovementLines,
                    Renamed = true
                });
            }

            foreach (var document in documents)
            {
                if (!DocumentTableNames.TryGetCanonicalTableName(document.Name, out var canonicalTable))
                    continue;

                var currentTable = document.TableName?.Trim() ?? string.Empty;
                if (string.Equals(currentTable, canonicalTable, StringComparison.OrdinalIgnoreCase))
                    continue;

                var currentExists = await TableExistsAsync(currentTable);
                var targetExists = await TableExistsAsync(canonicalTable);

                if (currentExists && targetExists)
                {
                    results.Add(new DocumentTableRenameResult
                    {
                        DocumentName = document.Name,
                        OldTableName = currentTable,
                        NewTableName = canonicalTable,
                        SkippedReason = "целевая таблица уже существует: метаданные не изменены, данные не тронуты"
                    });
                    continue;
                }

                try
                {
                    if (currentExists)
                    {
                        await RenameTableAsync(currentTable, canonicalTable);

                        // Метаданные фиксируем сразу после переименования таблицы:
                        // если следующий шаг упадёт, база не останется с таблицей
                        // под новым именем и метаданными под старым.
                        document.TableName = canonicalTable;
                        await _context.SaveChangesAsync();

                        try
                        {
                            await RenamePrimaryKeyAsync(canonicalTable);
                        }
                        catch (Exception ex)
                        {
                            SystemLogService.Warning(
                                $"doc-names: {document.Name} — PK не переименован: {ex.Message}",
                                nameof(DocumentTableNameNormalizer));
                        }
                    }
                    else
                    {
                        document.TableName = canonicalTable;
                        await _context.SaveChangesAsync();
                    }

                    results.Add(new DocumentTableRenameResult
                    {
                        DocumentName = document.Name,
                        OldTableName = currentTable,
                        NewTableName = canonicalTable,
                        Renamed = currentExists
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new DocumentTableRenameResult
                    {
                        DocumentName = document.Name,
                        OldTableName = currentTable,
                        NewTableName = canonicalTable,
                        SkippedReason = ex.Message
                    });
                }
            }

            return results;
        }

        private async Task<bool> TableExistsAsync(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            var rows = await _context.Database
                .SqlQueryRaw<string>(
                    "SELECT COALESCE(to_regclass(format('public.%I', @table_name::text))::text, '') AS \"Value\"",
                    new NpgsqlParameter("table_name", tableName))
                .ToListAsync();

            return rows.Count > 0 && !string.IsNullOrWhiteSpace(rows[0]);
        }

        private async Task<bool> NormalizeMovementLinesTableAsync()
        {
            if (!await TableExistsAsync(LegacyMovementLinesTable))
                return false;

            if (await TableExistsAsync(DocumentTableNames.AssetMovementLines))
                return false;

            await RenameTableAsync(LegacyMovementLinesTable, DocumentTableNames.AssetMovementLines);
            await RenamePrimaryKeyAsync(DocumentTableNames.AssetMovementLines);
            return true;
        }

        /// <summary>
        /// Переименование таблицы. DDL собирается на сервере через format('%I') и выполняется
        /// отдельной командой: параметры внутри DO-блока невозможны — Npgsql не подставляет их
        /// в dollar-quoted тело, и PL/pgSQL читает "@p0" как обращение к колонке (ошибка 42703).
        /// </summary>
        private async Task RenameTableAsync(string oldTable, string newTable)
        {
            var statement = await BuildStatementAsync(
                "SELECT format('ALTER TABLE public.%I RENAME TO %I', @old_name::text, @new_name::text) AS \"Value\"",
                new NpgsqlParameter("old_name", oldTable),
                new NpgsqlParameter("new_name", newTable));

            if (!string.IsNullOrWhiteSpace(statement))
                await _context.Database.ExecuteSqlRawAsync(statement);
        }

        private async Task RenamePrimaryKeyAsync(string tableName)
        {
            var pkName = await GetPrimaryKeyNameAsync(tableName);
            if (string.IsNullOrWhiteSpace(pkName))
                return;

            var wanted = tableName + "_pkey";
            if (string.Equals(pkName, wanted, StringComparison.OrdinalIgnoreCase))
                return;

            // Имя индекса PK глобально в схеме: если такое уже занято — не трогаем.
            if (await RelationExistsAsync(wanted))
                return;

            var statement = await BuildStatementAsync(
                "SELECT format('ALTER TABLE public.%I RENAME CONSTRAINT %I TO %I', @table_name::text, @pk_name::text, @wanted::text) AS \"Value\"",
                new NpgsqlParameter("table_name", tableName),
                new NpgsqlParameter("pk_name", pkName),
                new NpgsqlParameter("wanted", wanted));

            if (!string.IsNullOrWhiteSpace(statement))
                await _context.Database.ExecuteSqlRawAsync(statement);
        }

        private async Task<string> BuildStatementAsync(string sql, params NpgsqlParameter[] parameters)
        {
            var rows = await _context.Database
                .SqlQueryRaw<string>(sql, parameters.Cast<object>().ToArray())
                .ToListAsync();

            return rows.Count > 0 ? rows[0] ?? string.Empty : string.Empty;
        }

        private async Task<string> GetPrimaryKeyNameAsync(string tableName)
        {
            var rows = await _context.Database.SqlQueryRaw<string>(
                "SELECT COALESCE((SELECT conname FROM pg_constraint " +
                "WHERE conrelid = to_regclass(format('public.%I', @table_name::text)) AND contype = 'p' LIMIT 1), '') AS \"Value\"",
                new NpgsqlParameter("table_name", tableName)).ToListAsync();

            return rows.Count > 0 ? rows[0] ?? string.Empty : string.Empty;
        }

        private async Task<bool> RelationExistsAsync(string relationName)
        {
            var rows = await _context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_class WHERE relname = @relation_name",
                new NpgsqlParameter("relation_name", relationName)).ToListAsync();

            return rows.Count > 0 && rows[0] > 0;
        }
    }
}
