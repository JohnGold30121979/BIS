using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BIS.ERP.Migrations
{
    /// <summary>
    /// Нормализация имён таблиц документов модулей ОС и ТМЦ.
    ///
    /// Причина: исторически шапки документов ОС/ТМЦ создавались под хеш-именами
    /// (doc_asset_&lt;12 hex&gt;, doc_inventory_&lt;12 hex&gt;), а документы ТМЦ —
    /// под doc_incoming / doc_outgoing. Канон (Docs/Имена_таблиц_документов_ОС_ТМЦ.md):
    /// doc_asset_&lt;смысл&gt; / doc_inventory_&lt;смысл&gt;, строки — &lt;шапка&gt;_lines.
    ///
    /// Миграция идемпотентна и работает по данным: находит документ по имени в
    /// "MetadataObjects", переименовывает его физическую таблицу и PK, правит TableName.
    /// Если таблицы ещё нет — правится только запись метаданных (следующее создание
    /// таблицы пойдёт под каноническим именем). Если существуют обе таблицы — ничего
    /// не трогаем и пишем NOTICE: молча отвязывать метаданные от таблицы с данными нельзя.
    ///
    /// Дополняет нормализацию при открытии базы: шаг SchemaMigrationService
    /// "2026.10.20.01" (журнал schema_migrations) и вызов из
    /// DocumentationMetadataSeedService. Миграция закрывает базы, проходящие
    /// через Migrate (создание инфобазы); шаг — уже существующие базы.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261020101500_NormalizeDocumentTableNames")]
    public partial class NormalizeDocumentTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    rec record;
                    old_table text;
                    canonical text;
                    old_exists boolean;
                    new_exists boolean;
                    pk_name text;
                    wanted_pk text;
                BEGIN
                    IF to_regclass('public."MetadataObjects"') IS NULL THEN
                        RAISE NOTICE 'doc-names: MetadataObjects отсутствует — пропуск';
                        RETURN;
                    END IF;

                    FOR rec IN
                        SELECT * FROM (VALUES
                            ('Учет движения ОС',              'doc_asset_movement'),
                            ('Покупка ОС',                    'doc_asset_purchase'),
                            ('Ввод ОС в эксплуатацию',        'doc_asset_commissioning'),
                            ('Приход из производства ОС',     'doc_asset_production_receipt'),
                            ('Переоценка ОС',                 'doc_asset_revaluation'),
                            ('Реализация ОС',                 'doc_asset_sale'),
                            ('Частичная реализация ОС',       'doc_asset_partial_sale'),
                            ('Ликвидация ОС',                 'doc_asset_liquidation'),
                            ('Укомплектация ОС',              'doc_asset_completion'),
                            ('Разукомплектация ОС',           'doc_asset_decompletion'),
                            ('Начисление амортизации',        'doc_asset_depreciation'),
                            ('Списание амортизации',          'doc_asset_depreciation_writeoff'),
                            ('Консервация ОС',                'doc_asset_conservation'),
                            ('Расконсервация ОС',             'doc_asset_reopening'),
                            ('Передача ОС в подотчет',        'doc_asset_assignment'),
                            ('Смена затратного счета',        'doc_asset_expense_account_change'),
                            ('Смена группы ОС',               'doc_asset_group_change'),
                            ('Приход товаров',                'doc_inventory_receipt'),
                            ('Расход товаров',                'doc_inventory_issue'),
                            ('Внутреннее перемещение ТМЦ',    'doc_inventory_transfer'),
                            ('Приход из производства ТМЦ',    'doc_inventory_production_receipt'),
                            ('Расход в производство',         'doc_inventory_production_issue'),
                            ('Передача ТМЦ в подотчет',       'doc_inventory_assignment'),
                            ('Инвентаризация ТМЦ',            'doc_inventory_count')
                        ) AS t(document_name, canonical_table)
                    LOOP
                        old_table := NULL;

                        SELECT "TableName" INTO old_table
                        FROM "MetadataObjects"
                        WHERE "ObjectType" = 'Document'
                          AND "Name" = rec.document_name
                        LIMIT 1;

                        IF old_table IS NULL OR old_table = rec.canonical_table THEN
                            CONTINUE;
                        END IF;

                        canonical := rec.canonical_table;
                        old_exists := to_regclass(format('public.%I', old_table)) IS NOT NULL;
                        new_exists := to_regclass(format('public.%I', canonical)) IS NOT NULL;

                        IF old_exists AND new_exists THEN
                            RAISE NOTICE 'doc-names: % пропущен — существуют и %, и %',
                                rec.document_name, old_table, canonical;
                            CONTINUE;
                        END IF;

                        IF old_exists THEN
                            EXECUTE format('ALTER TABLE public.%I RENAME TO %I', old_table, canonical);
                            new_exists := true;
                        END IF;

                        IF new_exists THEN
                            pk_name := NULL;

                            SELECT conname INTO pk_name
                            FROM pg_constraint
                            WHERE conrelid = to_regclass(format('public.%I', canonical))
                              AND contype = 'p';

                            wanted_pk := canonical || '_pkey';

                            IF pk_name IS NOT NULL
                               AND pk_name <> wanted_pk
                               AND NOT EXISTS (SELECT 1 FROM pg_class WHERE relname = wanted_pk)
                            THEN
                                EXECUTE format('ALTER TABLE public.%I RENAME CONSTRAINT %I TO %I',
                                    canonical, pk_name, wanted_pk);
                            END IF;
                        END IF;

                        UPDATE "MetadataObjects"
                        SET "TableName" = canonical
                        WHERE "ObjectType" = 'Document'
                          AND "Name" = rec.document_name;

                        RAISE NOTICE 'doc-names: % -> %', rec.document_name, canonical;
                    END LOOP;

                    -- Табличная часть «Учета движения ОС»: отдельного объекта метаданных у неё нет.
                    IF to_regclass('public.doc_fixed_asset_movement_lines') IS NOT NULL
                       AND to_regclass('public.doc_asset_movement_lines') IS NULL THEN
                        ALTER TABLE public.doc_fixed_asset_movement_lines
                            RENAME TO doc_asset_movement_lines;

                        pk_name := NULL;
                        SELECT conname INTO pk_name
                        FROM pg_constraint
                        WHERE conrelid = to_regclass('public.doc_asset_movement_lines')
                          AND contype = 'p';

                        wanted_pk := 'doc_asset_movement_lines_pkey';

                        IF pk_name IS NOT NULL AND pk_name <> wanted_pk
                           AND NOT EXISTS (SELECT 1 FROM pg_class WHERE relname = wanted_pk)
                        THEN
                            EXECUTE format('ALTER TABLE public.%I RENAME CONSTRAINT %I TO %I',
                                'doc_asset_movement_lines', pk_name, wanted_pk);
                        END IF;

                        RAISE NOTICE 'doc-names: строки движения ОС -> doc_asset_movement_lines';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откат не выполняется осознанно: имена нормализованы, а прежние
            // хеш-имена (doc_asset_<hash>) невосстановимы — их никто не хранит.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    RAISE NOTICE 'doc-names: откат нормализации имён не поддерживается';
                END $$;
                """);
        }
    }
}
