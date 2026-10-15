\set ON_ERROR_STOP on
BEGIN;

\echo === 1. копия FRX-формы приходного ордера ===
INSERT INTO "FrxBackup_Reports"
SELECT r.* FROM "Reports" r
WHERE r."Code" = 'cash.receipt.foxpro'
  AND NOT EXISTS (SELECT 1 FROM "FrxBackup_Reports" b WHERE b."Id" = r."Id");

\echo === 2. копия пунктов меню, если они ссылались на форму ===
INSERT INTO "FrxBackup_MetadataModuleItems"
SELECT i.* FROM "MetadataModuleItems" i
WHERE i."ObjectId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.receipt.foxpro')
  AND NOT EXISTS (SELECT 1 FROM "FrxBackup_MetadataModuleItems" b WHERE b."Id" = i."Id");

\echo === 3. пометка кода как удалённого (чтобы форма не создалась заново) ===
INSERT INTO "DeletedStandardReports" ("Code", "Name", "DeletedAt", "DeletedBy", "Reason")
SELECT r."Code", r."Name", now(), 'admin', 'Печатная форма приходного ордера переведена на ClosedXML'
FROM "Reports" r
WHERE r."Code" = 'cash.receipt.foxpro'
  AND NOT EXISTS (SELECT 1 FROM "DeletedStandardReports" d WHERE d."Code" = r."Code");

\echo === 4. удаление формы и её следов ===
DELETE FROM "ReportElementMappings"
WHERE "ReportId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.receipt.foxpro');

DELETE FROM "ReportFields"
WHERE "ReportId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.receipt.foxpro');

DELETE FROM "MetadataModuleItems"
WHERE "ObjectId" IN (SELECT "Id" FROM "Reports" WHERE "Code" = 'cash.receipt.foxpro');

DELETE FROM "Reports" WHERE "Code" = 'cash.receipt.foxpro';

\echo === 5. ссылка формы ClosedXML — на источник набора данных, а не на документ ===
UPDATE "Reports" SET "DataSourceType" = 'ReportSource', "UpdatedAt" = now()
WHERE "Code" = 'cash.receipt.closedxml';

UPDATE "Reports" SET "DataSourceId" = (
        SELECT d."MetadataObjectId" FROM "ReportDataSets" d WHERE d."Code" = 'cash.receipt-order.print'
    ), "UpdatedAt" = now()
WHERE "Code" = 'cash.receipt.closedxml';

\echo === проверка ===
SELECT "Code", "Name", "SourceFormat", "IsPrintForm", "IsDefault", "DataSourceType", "DataSourceId"
FROM "Reports" WHERE "Code" LIKE 'cash.%' ORDER BY "Code";

SELECT "Code", "Name" FROM "DeletedStandardReports" WHERE "Code" LIKE 'cash.%';

COMMIT;
