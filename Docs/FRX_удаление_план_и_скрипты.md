# FRX: удаление из системы — план, скрипты, состояние

Файл восстановлен после моей ошибки: команда чистки документов по имени `*frx*` задела и служебные документы
этапов 1–3. Поэтому скрипты и план теперь собраны в одном файле — копируйте SQL-блоки в pgAdmin.

**Состояние:**

- Этап 3, шаг 1 — **сделан**: утилита `BIS.ERP.FrxMetadataGenerator` убрана из решения и с диска, сборка 1.1.478 без ошибок.
- Документы-вложения FRX — **удалены**: `Docs/frx-extracted`, `Docs/preview-frx*`, `Анализ_StandardFrxReportTemplates.docx`,
  `Возврат_FRX_инструкция.md`, `Отключение_FRX.md`, `Обучение_полям_FRX.docx`, `Описание_привязка_FRX_и_пагинация.docx`,
  `Правила_полос_FRX_по_шагам.md/.docx`, `FoxProReportRules.md`.
- Осталось: этап 3 шаги 2–8, этап 4 (схема), этап 5 (четыре прохода проверки). Карта шага 2 — в конце файла.
- **15.10.2026, этап 2 выполнен в рабочей базе ОсОО1** (`bis_OsOO1_20260908_112439`): удалено 40 отчётов FRX,
  15 пунктов меню, 255 полей, 99 привязок элементов, 58 строк правил. Копии `FrxBackup_*` сняты до удаления
  (40/255/99/15/58). Протокол — `Docs/Разбор_ошибок_сборки_2026-10-15.md`, раздел 9; выполненный скрипт —
  `Docs/Этап2_удаление_данных_скорректированный.sql`.
- **15.10.2026, этап 3 шаги 2–5 выполнены**: код FRX вырезан, `BIS.ERP.sln` собирается без ошибок.
  Документы-вложения FRX, перечисленные в списке удалённых, в `Docs` восстановлены из репозитория.

---

## Этап 1. Страховка и счётчики (ничего не удаляет)

```sql
SELECT 'Reports: код standard.frx.*' AS "что", COUNT(*)::text AS "сколько"
FROM "Reports" WHERE "Code" LIKE 'standard.frx.%'
UNION ALL SELECT 'Reports: тип FoxProLayout', COUNT(*)::text FROM "Reports" WHERE "ReportType" = 'FoxProLayout'
UNION ALL SELECT 'Reports: формат FoxProFRX', COUNT(*)::text FROM "Reports" WHERE "SourceFormat" = 'FoxProFRX'
UNION ALL SELECT 'Reports: шаблон с маркером FoxProFRX', COUNT(*)::text FROM "Reports" WHERE "Template" LIKE '%FoxProFRX%'
UNION ALL SELECT 'Правила полей FoxPro', COUNT(*)::text FROM "FoxProReportFieldRules";

SELECT "Code", "Name", "ReportType", "SourceFormat",
       CASE WHEN COALESCE("Template", '') = '' THEN 'нет' ELSE 'есть' END AS "FRX-шаблон"
FROM "Reports"
WHERE "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout'
   OR "SourceFormat" = 'FoxProFRX'
ORDER BY "Code";

DO $$
DECLARE
    frx_where text := '("Code" LIKE ''standard.frx.%'' OR "ReportType" = ''FoxProLayout''
                        OR "SourceFormat" = ''FoxProFRX'')';
BEGIN
    EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_Reports" AS SELECT * FROM "Reports" WHERE %s', frx_where);
    IF to_regclass('public."ReportFields"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportFields" AS
                        SELECT f.* FROM "ReportFields" f JOIN "Reports" r ON r."Id" = f."ReportId" WHERE %s', frx_where);
    END IF;
    IF to_regclass('public."ReportFilters"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportFilters" AS
                        SELECT f.* FROM "ReportFilters" f JOIN "Reports" r ON r."Id" = f."ReportId" WHERE %s', frx_where);
    END IF;
    IF to_regclass('public."ReportParameters"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportParameters" AS
                        SELECT p.* FROM "ReportParameters" p JOIN "Reports" r ON r."Id" = p."ReportId" WHERE %s', frx_where);
    END IF;
    IF to_regclass('public."ReportGroups"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportGroups" AS
                        SELECT g.* FROM "ReportGroups" g JOIN "Reports" r ON r."Id" = g."ReportId" WHERE %s', frx_where);
    END IF;
    IF to_regclass('public."ReportElementMappings"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportElementMappings" AS
                        SELECT m.* FROM "ReportElementMappings" m JOIN "Reports" r ON r."Id" = m."ReportId" WHERE %s', frx_where);
    END IF;
    -- Имя таблицы — "ReportHeaderFooter" (в прежней версии скрипта было "ReportHeadersFooters").
    IF to_regclass('public."ReportHeaderFooter"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportHeaderFooter" AS
                        SELECT h.* FROM "ReportHeaderFooter" h JOIN "Reports" r ON r."Id" = h."ReportId" WHERE %s', frx_where);
    END IF;
    -- Копии того, что удаляет этап 2 помимо самих отчётов: настройки пользователей,
    -- пункты меню и список удалённых стандартных отчётов.
    IF to_regclass('public."ReportUserSettings"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_ReportUserSettings" AS
                        SELECT s.* FROM "ReportUserSettings" s JOIN "Reports" r ON r."Id" = s."ReportId" WHERE %s', frx_where);
    END IF;
    IF to_regclass('public."MetadataModuleItems"') IS NOT NULL THEN
        EXECUTE format('CREATE TABLE IF NOT EXISTS "FrxBackup_MetadataModuleItems" AS
                        SELECT m.* FROM "MetadataModuleItems" m JOIN "Reports" r ON r."Id" = m."ObjectId"
                        WHERE m."ObjectType" = ''Report'' AND %s', frx_where);
    END IF;
    IF to_regclass('public."DeletedStandardReports"') IS NOT NULL THEN
        EXECUTE 'CREATE TABLE IF NOT EXISTS "FrxBackup_DeletedStandardReports" AS
                 SELECT * FROM "DeletedStandardReports" WHERE "Code" LIKE ''standard.frx.%''';
    END IF;
    IF to_regclass('public."FoxProReportFieldRules"') IS NOT NULL THEN
        EXECUTE 'CREATE TABLE IF NOT EXISTS "FrxBackup_FoxProReportFieldRules" AS SELECT * FROM "FoxProReportFieldRules"';
    END IF;
    RAISE NOTICE 'Копии сняты. Строк в FrxBackup_Reports: %', (SELECT COUNT(*) FROM "FrxBackup_Reports");
END $$;

DO $$
DECLARE item record; item_count bigint;
BEGIN
    FOR item IN SELECT tablename FROM pg_tables
                WHERE schemaname = 'public' AND tablename LIKE 'FrxBackup_%' ORDER BY tablename
    LOOP
        EXECUTE format('SELECT COUNT(*) FROM %I', item.tablename) INTO item_count;
        RAISE NOTICE 'Копия %: строк %', item.tablename, item_count;
    END LOOP;
END $$;
```

---

## Этап 2. Удаление данных FRX (необратимо, только после этапа 1)

> Набор отчётов задаётся **только** признаками FRX. Прежнее условие `COALESCE("Template", '') <> ''`
> убрано: под него попадали ещё 7 отчётов, которые удалять нельзя — четыре отчёта ClosedXML
> (журнал счет-фактур, акт сверки, оборотная ведомость, счёт-фактура НДС) и три нативные печатные
> формы (ПКО, РКО, платёжное поручение). Проверено на рабочей базе ОсОО1 15.10.2026: по признакам
> FRX — 40 отчётов, с прежним условием — 47.

```sql
BEGIN;

CREATE TEMP TABLE frx_reports ON COMMIT DROP AS
SELECT "Id" FROM "Reports"
WHERE "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout'
   OR "SourceFormat" = 'FoxProFRX';

DELETE FROM "MetadataModuleItems"
WHERE "ObjectType" = 'Report' AND "ObjectId" IN (SELECT "Id" FROM frx_reports);

DO $$
DECLARE linked_table text;
BEGIN
    FOREACH linked_table IN ARRAY ARRAY['ReportFields', 'ReportFilters', 'ReportParameters',
                                        'ReportGroups', 'ReportElementMappings', 'ReportHeaderFooter']
    LOOP
        IF to_regclass('public.' || quote_ident(linked_table)) IS NOT NULL THEN
            EXECUTE format('DELETE FROM %I WHERE "ReportId" IN (SELECT "Id" FROM frx_reports)', linked_table);
        END IF;
    END LOOP;
END $$;

DO $$
BEGIN
    IF to_regclass('public."ReportUserSettings"') IS NOT NULL THEN
        EXECUTE 'DELETE FROM "ReportUserSettings" WHERE "ReportId" IN (SELECT "Id" FROM frx_reports)';
    END IF;
END $$;

DELETE FROM "Reports" WHERE "Id" IN (SELECT "Id" FROM frx_reports);
DELETE FROM "FoxProReportFieldRules";

DO $$
BEGIN
    IF to_regclass('public."DeletedStandardReports"') IS NOT NULL THEN
        EXECUTE 'DELETE FROM "DeletedStandardReports" WHERE "Code" LIKE ''standard.frx.%''';
    END IF;
END $$;

DO $$
DECLARE left_reports bigint; left_rules bigint; left_orphans bigint;
BEGIN
    SELECT COUNT(*) INTO left_reports FROM "Reports"
    WHERE "Code" LIKE 'standard.frx.%' OR "ReportType" = 'FoxProLayout'
       OR "SourceFormat" = 'FoxProFRX';
    SELECT COUNT(*) INTO left_rules FROM "FoxProReportFieldRules";
    SELECT COUNT(*) INTO left_orphans FROM "ReportElementMappings" m
    LEFT JOIN "Reports" r ON r."Id" = m."ReportId" WHERE r."Id" IS NULL;
    RAISE NOTICE 'Осталось FRX-отчётов: %, строк в правилах: %, осиротевших привязок: %',
        left_reports, left_rules, left_orphans;
    IF left_reports <> 0 OR left_rules <> 0 OR left_orphans <> 0 THEN
        RAISE EXCEPTION 'Удаление неполное — транзакция откатывается';
    END IF;
END $$;

COMMIT;
```

---

## Этап 3. Удаление кода — шаги

1. **Сделано:** проект-утилита `BIS.ERP.FrxMetadataGenerator` (удалён из решения и с диска, сборка 0 ошибок).
2. Удалить `Services/StandardFrxReportTemplates.generated.cs` — **карта правок ниже**.
3. Удалить `Services/FrxParser.cs`, `FrxToReportConverter.cs`, `FrxRecognitionProfileService.cs`, `Models/FrxReport.cs`,
   `Models/FoxProAnalysisModels.cs` + сервис анализа, `Views/FrxImportWindow.xaml(.cs)`, FRX-сценарии тестов.
4. Убрать вкладки конструктора «🔗 FRX поля» и «🧱 Нативный макет» вместе с кодом; оставить только способ макета
   «ClosedXML — визуальный Excel»; убрать тип отчёта «Макет Visual FoxPro FRX».
5. Убрать FRX из печати и обмена (`PrintFormService`, `AccountingReportsView`, `MainWorkWindow`, конфигуратор,
   `ConfigurationExchangeService`) и удалить класс `ReportingFeatures` целиком.
6. Схема: убрать `FoxProReportFieldRule` (модель и `DbSet`), удалить таблицу правил; при необходимости убрать `Report.Template`.
   Копии `FrxBackup_*` не трогать.
7. Документы и вложения FRX — **сделано** (список в начале файла).
8. Проверки: сборка → поиск `frx|FoxPro|FrxEnabled` = 0 → сверка стандартных отчётов (СТ-1, НДС, ведомость,
   журнал, программный акт сверки) → ваш запуск.

### Шаг 2 — карта (проверено поиском)

Ссылки на FRX-макеты — в **одном** боевом файле `Services/MetadataService.Reports.cs`:

| Место | Что делать |
| --- | --- |
Строки 31–34: `foreach (var definition in StandardFrxReportTemplates.GetDefinitions()…) await EnsureStandardFrxReportAsync(definition);` | Удалить цикл |
Строка 37: `await MarkReconciliationFrxReportsAsTemplateVariantsAsync();` | Удалить вызов и метод |
Строки 46–56: сидер платёжного поручения, константа `PaymentOrderPrPl23ReportCode` | Удалить метод и константу |
`EnsureStandardFrxReportAsync` | Удалить |
Сидеры кассовой книги и реестра, константы `CashBookFrxReportCode`, `ReceiptExpenseRegisterFrxReportCode` | Удалить методы и константы |
`Services/StandardFrxReportTemplates.generated.cs` | Удалить файл после правок выше |

Порядок: вырезать перечисленное → удалить сгенерированный файл → сборка → добор оставшихся ссылок по подсказкам компилятора.
