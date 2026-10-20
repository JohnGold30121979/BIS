-- =============================================================================
-- BIS.ERP — нормализация имён таблиц документов ОС и ТМЦ
-- Дата: 20.10.2026
--
-- Канон: doc_asset_<смысл> / doc_inventory_<смысл>, строки — <шапка>_lines.
-- Полный реестр — Docs/Имена_таблиц_документов_ОС_ТМЦ.md, код — Services/DocumentTableNames.cs.
--
-- Скрипт для ручного применения к любой базе (когда не сработали ни EF-миграция,
-- ни runtime-нормализатор при старте приложения). Идемпотентен: повторный запуск
-- не делает ни одного DDL.
--
-- Запуск:  psql -h localhost -p 5432 -U postgres -d <база> -f normalize_doc_table_names.sql
-- =============================================================================

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

-- =============================================================================
-- Проверка после прогона
-- =============================================================================

-- 1. Документы ОС/ТМЦ и их таблицы (ожидаем канон)
SELECT "Name", "TableName"
FROM "MetadataObjects"
WHERE "ObjectType" = 'Document'
  AND ("TableName" LIKE 'doc\_asset\_%' OR "TableName" LIKE 'doc\_inventory\_%'
       OR "TableName" IN ('doc_incoming', 'doc_outgoing'))
ORDER BY "Name";

-- 2. Остались ли хеш-имена (ожидаем 0 строк)
SELECT "Name", "TableName"
FROM "MetadataObjects"
WHERE "TableName" ~ '^doc_(asset|inventory)_[0-9a-f]{12}$';

-- 3. Таблицы без объекта метаданных и метаданные без таблицы
SELECT m."Name", m."TableName",
       (to_regclass(format('public.%I', m."TableName")) IS NOT NULL) AS table_exists
FROM "MetadataObjects" m
WHERE m."ObjectType" = 'Document'
  AND m."TableName" LIKE 'doc\_%'
ORDER BY table_exists, m."Name";
