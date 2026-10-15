-- Этап 2 плана удаления FRX: выполненный скрипт (рабочая база ОсОО1, 15.10.2026).
--
-- Что важно знать перед повторным запуском (например, на другой ИБ):
--
-- 1. Набор отчётов задан ТОЛЬКО признаками FRX:
--        "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout' OR "SourceFormat" = 'FoxProFRX'
--    В исходном скрипте плана было ещё условие COALESCE("Template", '') <> ''.
--    Оно захватывало 7 отчётов, которые удалять нельзя: четыре отчёта ClosedXML
--    (акт сверки, оборотная ведомость, журнал счет-фактур, счёт-фактура НДС) и три
--    нативные печатные формы (cash.payment.native, cash.receipt.native, payment.order.native).
--    В наборе по признакам FRX на 15.10.2026 было 40 отчётов: 36 с кодами standard.frx.*
--    и 4 программные формы (*.foxpro). Программные формы приложение создаёт заново
--    при старте (PrintFormService.SeedCashOrderFormsAsync / SeedInvoiceFormsAsync).
--
-- 2. Копии (этап 1) обязательны и уже существуют в базе: FrxBackup_Reports (40),
--    FrxBackup_ReportFields (255), FrxBackup_ReportElementMappings (99),
--    FrxBackup_MetadataModuleItems (15), FrxBackup_FoxProReportFieldRules (58),
--    плюс пустые FrxBackup_ReportFilters/ReportParameters/ReportGroups/ReportHeaderFooter/
--    ReportUserSettings/DeletedStandardReports. Копии не удалять.
--
-- 3. Схема: имя таблицы — "ReportHeaderFooter" (в плане было "ReportHeadersFooters");
--    таблицы "ReportParameters" и "ReportUserSettings" в базе есть.
--
-- Результат выполнения 15.10.2026: удалено 40 отчётов, 15 пунктов меню,
-- 255 полей, 99 привязок элементов, 58 строк правил; в базе осталось 51 отчёт
-- (5 ClosedXml + 46 Native). Проверка внутри транзакции прошла, COMMIT выполнен.

BEGIN;

CREATE TEMP TABLE step2_reports ON COMMIT DROP AS
SELECT "Id" FROM "Reports"
WHERE "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout' OR "SourceFormat" = 'FoxProFRX';

DELETE FROM "MetadataModuleItems"
WHERE "ObjectType" = 'Report' AND "ObjectId" IN (SELECT "Id" FROM step2_reports);

DELETE FROM "ReportFields"           WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportFilters"          WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportParameters"       WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportGroups"           WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportElementMappings"  WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportHeaderFooter"     WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "ReportUserSettings"     WHERE "ReportId" IN (SELECT "Id" FROM step2_reports);

DELETE FROM "Reports" WHERE "Id" IN (SELECT "Id" FROM step2_reports);
DELETE FROM "FoxProReportFieldRules";
DELETE FROM "DeletedStandardReports" WHERE "Code" LIKE 'standard.frx.%';

DO $$
DECLARE
    left_reports bigint;
    left_rules bigint;
    left_children bigint;
BEGIN
    SELECT count(*) INTO left_reports FROM "Reports"
    WHERE "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout' OR "SourceFormat" = 'FoxProFRX';
    SELECT count(*) INTO left_rules FROM "FoxProReportFieldRules";
    SELECT count(*) INTO left_children
    FROM "ReportElementMappings" m
    LEFT JOIN "Reports" r ON r."Id" = m."ReportId"
    WHERE r."Id" IS NULL;

    RAISE NOTICE 'Осталось FRX-отчётов: %, правил: %, осиротевших привязок: %',
        left_reports, left_rules, left_children;

    IF left_reports <> 0 OR left_rules <> 0 OR left_children <> 0 THEN
        RAISE EXCEPTION 'Удаление неполное — транзакция откатывается';
    END IF;
END $$;

COMMIT;
