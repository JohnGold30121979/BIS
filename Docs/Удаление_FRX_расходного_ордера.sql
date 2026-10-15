\set ON_ERROR_STOP on
BEGIN;

\echo === 1. копия FRX-формы расходного ордера ===
INSERT INTO "FrxBackup_Reports"
SELECT r.* FROM "Reports" r
WHERE r."Code" = 'cash.payment.foxpro'
  AND NOT EXISTS (SELECT 1 FROM "FrxBackup_Reports" b WHERE b."Id" = r."Id");

\echo === 2. копия пунктов меню, если они ссылались на форму ===
INSERT INTO "FrxBackup_MetadataModuleItems"
SELECT i.* FROM "MetadataModuleItems" i
WHERE i."ObjectId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.payment.foxpro')
  AND NOT EXISTS (SELECT 1 FROM "FrxBackup_MetadataModuleItems" b WHERE b."Id" = i."Id");

\echo === 3. пометка кода как удалённого (чтобы форма не создалась заново) ===
INSERT INTO "DeletedStandardReports" ("Code", "Name", "DeletedAt", "DeletedBy", "Reason")
SELECT r."Code", r."Name", now(), 'admin', 'Печатная форма расходного ордера переведена на ClosedXML'
FROM "Reports" r
WHERE r."Code" = 'cash.payment.foxpro'
  AND NOT EXISTS (SELECT 1 FROM "DeletedStandardReports" d WHERE d."Code" = r."Code");

\echo === 4. удаление формы и её следов ===
DELETE FROM "ReportElementMappings"
WHERE "ReportId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.payment.foxpro');

DELETE FROM "ReportFields"
WHERE "ReportId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.payment.foxpro');

DELETE FROM "MetadataModuleItems"
WHERE "ObjectId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.payment.foxpro');

DELETE FROM "Reports" WHERE "Code" = 'cash.payment.foxpro';

\echo === проверка: печатные формы кассовых ордеров ===
SELECT r."Code", r."Name", r."SourceFormat", r."IsPrintForm", r."IsDefault", r."DataSourceType"
FROM "Reports" r WHERE r."Code" LIKE 'cash.%' ORDER BY r."Code";

COMMIT;
