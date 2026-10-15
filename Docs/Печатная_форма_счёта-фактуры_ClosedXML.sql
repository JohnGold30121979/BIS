-- Печатная форма счёта-фактуры: перевод на ClosedXML (рабочая база ОсОО1, 15.10.2026).
--
-- Зачем: в окне «Выписка счет-фактур» (InvoiceWorkView) список печатных форм показывал
-- «Счет-фактура на реализацию» (SourceFormat = FoxProFRX). Причина — фильтр
-- PrintFormService.GetPrintFormsAsync: в список попадают только формы, привязанные
-- к самому документу (DataSourceType = Document). Бланк ClosedXML
-- (standard.finance.vat-invoice-vat-nds) привязан к набору данных
-- «Счет-фактура НДС: позиции и реквизиты», поэтому в список не попадал.
--
-- Что делает этот скрипт:
--   * копирует текущие строки invoice.sales.foxpro / invoice.purchase.foxpro
--     в FrxBackup_Reports (копии этапа 1 хранят прежние строки с другими Id: эти две
--     формы приложение создало заново после чистки FRX, см. SeedInvoiceFormsAsync);
--   * помечает коды удалёнными в DeletedStandardReports — сидер проверяет этот список
--     и больше формы не создаёт;
--   * удаляет строки этих отчётов.
--
-- Печать счёта-фактуры после правки идёт по бланку ClosedXML: он показывается
-- в списке печатных форм документа вместе с документными формами
-- (PrintFormService.GetInvoicePrintFormsAsync) и выводится через ClosedXmlReportRenderer
-- по строкам набора данных, отфильтрованным по идентификатору счёта-фактуры.
--
-- Откат: снять пометки в DeletedStandardReports (в конфигураторе есть восстановление
-- удалённых стандартных отчётов) — приложение создаст формы заново.

BEGIN;

INSERT INTO "FrxBackup_Reports"
SELECT * FROM "Reports" WHERE "Code" IN ('invoice.sales.foxpro', 'invoice.purchase.foxpro');

INSERT INTO "DeletedStandardReports" ("Code", "Name", "DeletedAt", "DeletedBy", "Reason")
SELECT "Code", "Name", now(), 'admin', 'Печатная форма счёта-фактуры переведена на ClosedXML'
FROM "Reports" WHERE "Code" IN ('invoice.sales.foxpro', 'invoice.purchase.foxpro')
ON CONFLICT ("Code") DO NOTHING;

DELETE FROM "MetadataModuleItems"
WHERE "ObjectType" = 'Report'
  AND "ObjectId" IN (SELECT "Id" FROM "Reports" WHERE "Code" IN ('invoice.sales.foxpro', 'invoice.purchase.foxpro'));

DELETE FROM "Reports" WHERE "Code" IN ('invoice.sales.foxpro', 'invoice.purchase.foxpro');

DO $$
DECLARE
    left_forms bigint;
    closedxml_forms bigint;
BEGIN
    SELECT count(*) INTO left_forms FROM "Reports"
    WHERE "Code" IN ('invoice.sales.foxpro', 'invoice.purchase.foxpro');

    SELECT count(*) INTO closedxml_forms FROM "Reports"
    WHERE "IsPrintForm" AND "SourceFormat" = 'ClosedXml'
      AND "ClosedXmlLayout" IS NOT NULL AND "ClosedXmlLayout" <> ''
      AND "ReportType" IN ('VatInvoiceVatKg', 'VatInvoiceSt1');

    RAISE NOTICE 'FoxPro-форм счёта-фактуры осталось: %, бланков ClosedXML доступно: %',
        left_forms, closedxml_forms;

    IF left_forms <> 0 THEN
        RAISE EXCEPTION 'FoxPro-формы не удалены — транзакция откатывается';
    END IF;
    IF closedxml_forms = 0 THEN
        RAISE EXCEPTION 'Не найден бланк ClosedXML — печатать будет нечем, транзакция откатывается';
    END IF;
END $$;

COMMIT;

-- Отдельной правкой (вне транзакции) в набор данных «Счет-фактура НДС: позиции и реквизиты»
-- добавлена колонка invoice_id — по ней печать берёт строки одного счёта-фактуры:
--
-- UPDATE "ReportDataSets"
-- SET "SqlText" = replace("SqlText",
--         E'SELECT\r\n    CAST(p.line_number AS text)',
--         E'SELECT\r\n    -- Идентификатор документа: печатная форма берёт строки одного счёта-фактуры.\r\n    p.invoice_id,\r\n    CAST(p.line_number AS text)')
-- WHERE "MetadataObjectId" = '901f547a-8ecc-473f-abd6-9a7bbbdb85f4'
--   AND position('p.invoice_id,' in "SqlText") = 0;
--
-- В коде тот же запрос обновлён в ReportDataSetService.BuildVatInvoiceVatKgSql();
-- при запуске приложение перезапишет запрос в базе, если администратор его не менял.
