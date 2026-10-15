-- Удаление неактивных Native-остатков (рабочая база ОсОО1, выполнено 15.10.2026).
--
-- Решение заказчика: убрать только неактивные legacy-определения и дубли.
-- Программные печатные формы (печать платёжки, ПКО, РКО) и действующие табличные отчёты
-- (ведомости ОС, материалы, журналы) НЕ удаляются: ClosedXML-замен у них пока нет.
--
-- Удалено 26 строк (все IsActive = false):
--   * 10 отчётов standard.finance.*  — legacy, заменены ClosedXML-вариантами;
--   *  7 отчётов standard.fixed-assets.* — активные balances / by-account / depreciation оставлены;
--   *  9 дублей standard.metadata.fixed.assets.statement — активная строка этого кода оставлена.
-- Вместе с отчётами ушли 278 строк полей и 26 пунктов меню (MetadataModuleItems).
--
-- Коды удалённых стандартных отчётов внесены в "DeletedStandardReports" (17 записей) — это штатный
-- механизм приложения: MetadataService.EnsureStandardReportsAsync пропускает такие коды и не создаёт
-- отчёты заново. Откат: снять пометки (в конфигураторе есть восстановление удалённых стандартных
-- отчётов) и вернуть строки из NativeBackup_*.
--
-- Копии (сняты до удаления): NativeBackup_Reports (46), NativeBackup_ReportFields (440),
-- NativeBackup_MetadataModuleItems (38), плюс пустые копии остальных дочерних таблиц.
-- Эти копии покрывают все 46 Native-отчётов, а не только удалённые 26.
--
-- Проверки внутри транзакции: в наборе нет активных отчётов; после удаления неактивных строк набора
-- не осталось; активных Native-отчётов по-прежнему 20.

BEGIN;

CREATE TABLE IF NOT EXISTS "NativeBackup_Reports" AS
SELECT * FROM "Reports" WHERE "SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportFields" AS
SELECT f.* FROM "ReportFields" f JOIN "Reports" r ON r."Id" = f."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportFilters" AS
SELECT f.* FROM "ReportFilters" f JOIN "Reports" r ON r."Id" = f."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportParameters" AS
SELECT p.* FROM "ReportParameters" p JOIN "Reports" r ON r."Id" = p."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportGroups" AS
SELECT g.* FROM "ReportGroups" g JOIN "Reports" r ON r."Id" = g."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportElementMappings" AS
SELECT m.* FROM "ReportElementMappings" m JOIN "Reports" r ON r."Id" = m."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportHeaderFooter" AS
SELECT h.* FROM "ReportHeaderFooter" h JOIN "Reports" r ON r."Id" = h."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_ReportUserSettings" AS
SELECT s.* FROM "ReportUserSettings" s JOIN "Reports" r ON r."Id" = s."ReportId" WHERE r."SourceFormat" = 'Native';

CREATE TABLE IF NOT EXISTS "NativeBackup_MetadataModuleItems" AS
SELECT m.* FROM "MetadataModuleItems" m JOIN "Reports" r ON r."Id" = m."ObjectId"
WHERE m."ObjectType" = 'Report' AND r."SourceFormat" = 'Native';

CREATE TEMP TABLE native_drop ON COMMIT DROP AS
SELECT "Id", "Code", "Name", "IsActive" FROM "Reports"
WHERE "SourceFormat" = 'Native'
  AND NOT "IsActive"
  AND ("Code" LIKE 'standard.finance.%'
       OR "Code" LIKE 'standard.fixed-assets.%'
       OR "Code" = 'standard.metadata.fixed.assets.statement');

DO $$
DECLARE
    drop_count bigint;
    active_drop bigint;
BEGIN
    SELECT count(*), count(*) FILTER (WHERE "IsActive") INTO drop_count, active_drop FROM native_drop;
    RAISE NOTICE 'К удалению отчётов: %, из них активных: %', drop_count, active_drop;
    IF active_drop <> 0 THEN
        RAISE EXCEPTION 'В наборе есть активные отчёты — транзакция откатывается';
    END IF;
    IF drop_count = 0 THEN
        RAISE EXCEPTION 'Набор пуст — транзакция откатывается';
    END IF;
END $$;

INSERT INTO "DeletedStandardReports" ("Code", "Name", "DeletedAt", "DeletedBy", "Reason")
SELECT DISTINCT d."Code", d."Name", now(), 'admin',
       'Удаление legacy Native-отчётов: остаются только ClosedXML'
FROM native_drop d
WHERE d."Code" <> 'standard.metadata.fixed.assets.statement'
ON CONFLICT ("Code") DO NOTHING;

DELETE FROM "MetadataModuleItems"
WHERE "ObjectType" = 'Report' AND "ObjectId" IN (SELECT "Id" FROM native_drop);

DELETE FROM "ReportFields"          WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportFilters"         WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportParameters"      WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportGroups"          WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportElementMappings" WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportHeaderFooter"    WHERE "ReportId" IN (SELECT "Id" FROM native_drop);
DELETE FROM "ReportUserSettings"    WHERE "ReportId" IN (SELECT "Id" FROM native_drop);

DELETE FROM "Reports" WHERE "Id" IN (SELECT "Id" FROM native_drop);

DO $$
DECLARE
    left_drop bigint;
    left_active_native bigint;
BEGIN
    SELECT count(*) INTO left_drop FROM "Reports"
    WHERE NOT "IsActive" AND "SourceFormat" = 'Native'
      AND ("Code" LIKE 'standard.finance.%' OR "Code" LIKE 'standard.fixed-assets.%'
           OR "Code" = 'standard.metadata.fixed.assets.statement');

    SELECT count(*) INTO left_active_native FROM "Reports"
    WHERE "SourceFormat" = 'Native' AND "IsActive";

    RAISE NOTICE 'Неактивных строк набора осталось: % (ожидается 0), активных Native: %',
        left_drop, left_active_native;

    IF left_drop <> 0 THEN
        RAISE EXCEPTION 'Удаление неполное — транзакция откатывается';
    END IF;
    IF left_active_native < 20 THEN
        RAISE EXCEPTION 'Задеты активные Native-отчёты — транзакция откатывается';
    END IF;
END $$;

COMMIT;
