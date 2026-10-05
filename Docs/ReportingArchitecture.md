# Отчётность в BIS ERP: как устроена

Документ описывает подсистему отчётности проекта BIS ERP (`D:\C#\BIS.ERP\BIS.ERP`) по фактическому коду.
Цель — чтобы новый разработчик или аналитик мог за один проход понять: где хранится отчёт, откуда берутся данные, как считается макет и куда выводится результат.

---

## 0. Коротко

> Отчёт в системе — это **не файл, а запись в метаданных** `Reports` + **источник данных** + **макет**.
> Данные отчёта материализуются в `DataTable`, дальше этот же `DataTable` уходит в три разных движка вывода: программный, нативный (конструктор) и FoxPro/FRX.

Три важных следствия:

1. Логика отчёта (что выбрать, что отфильтровать) лежит в метаданных, а код отвечает только за транспорт и вёрстку.
2. В системе **три независимых механизма отчётности**, и они не пересекаются — важно не искать отчёт не там.
3. Стандартные отчёты не создаются вручную: они **досеиваются при старте приложения** и «защищены от воскрешения» отдельным сервисом.

---

## 1. Три механизма отчётности

### 1.1 Конструкторные отчёты (метаданные) — основной механизм

Отчёт — строка в таблице `Reports`. Пользователь (или программист через конструктор) выбирает источник данных (объект метаданных), набор колонок, фильтры, оформление. Система сама строит `SELECT` и выводит результат.

| Аспект | Значение |
| --- | --- |
| Где хранится | `Reports`, `ReportFields`, `ReportFilters`, `ReportElementMappings` |
| Источник данных | `Report.DataSourceId` → `MetadataObjects` (справочник или документ) |
| Кто формирует данные | [`ReportService.GetReportDataAsync`](../Services/ReportService.cs) |
| Кто формирует вид | `ReportService.ExportToPdf/ExportToExcel`, `PrintFormService` |
| Настройка | `Views/ReportDesignerWindow.xaml(.cs)` — конструктор |

### 1.2 Программная бухгалтерская отчётность — «жёсткая» аналитика

Отдельный экран `AccountingReportsView` («Бухгалтерская отчетность»). Здесь 9 отчётов **написаны кодом** и собираются из проводок и периодов: оборотно-сальдовая ведомость, главная книга, баланс, финансовые результаты, журнал закупок и продаж, сальдо по организациям, сбор информации за период, реестр платёжных поручений, акт сверки.

| Аспект | Значение |
| --- | --- |
| Где хранится | Код `Views/AccountingReportsView.xaml.cs` (методы `Build*Async`) |
| Источник данных | Проводки, платёжные поручения, счета-фактуры, периоды |
| Результат | Пара `(DataTable, Report)` — отчёт-объект создаётся на лету (`CreateReport`) |
| Нюанс | Эти отчёты **не лежат** в `Reports`, поэтому их нельзя редактировать конструктором |

Каждый такой отчёт дополнительно умеет закрывать/открывать период («Закрыть баланс») и связывается с FRX-вариантами (`LoadTrialBalanceReportVariantsAsync`, `LoadReconciliationReportVariantsAsync`).

### 1.3 Печатные формы и FoxPro/FRX — перенос legacy-форм

Импорт старых форм Visual FoxPro (`.frx`) и их рендеринг. Это отдельный движок: свой парсер, свой язык выражений, своя база знаний и правила распознавания полей.

| Аспект | Значение |
| --- | --- |
| Ядро | [`PrintFormService`](../Services/PrintFormService.cs) (~2900 строк) + `FrxParser` |
| Распознавание | `FrxRecognitionProfileService`, `FoxProReportKnowledgeBase`, `FoxProReportFieldRuleService` |
| Выражения | `FoxExpressionParser` (свой интерпретатор Fox-выражений) |
| Настройка | Вкладки `FRX поля` и `🧱 Нативный макет` в конструкторе |

Подробная инструкция по переносу форм: [`FoxProReportRules.md`](FoxProReportRules.md).

### 1.4 Сравнение механизмов

| Критерий | Конструкторные | Программные (бух. отчётность) | FRX / печатные формы |
| --- | --- | --- | --- |
| Хранится в БД | Да (`Reports`) | Нет (код) | Да (`Reports`, `SourceFormat=FoxProFRX`) |
| Источник данных | Метаданные + SQL-наборы | Проводки напрямую | То же, что у отчёта-владельца |
| Редактируется пользователем | Да, конструктор | Нет | Да, вкладки FRX/нативного макета |
| Типовой вывод | PDF (сразу при открытии) | Excel / PDF / FRX / налоговый | PDF / Excel по макету |

### 1.5 Смежные сервисы

| Сервис | Назначение |
| --- | --- |
| `ReportDataSetService` | Пользовательские SQL-наборы данных поверх объектов метаданных |
| `ReportComputedFieldCatalog` | Канонические вычисляемые поля и алиасы FoxPro-имён |
| `RegulatedReportTemplateService` | Регламентированные шаблоны (xlsx) с версиями и SHA-256 |
| `StandardReportDeletionService` | «Кладбище» кодов удалённых стандартных отчётов |
| `VatTaxReportExportService` | Налоговый Excel по журналу покупок/продаж |
| `AdvancePaymentsTurnoverReportService` | Оборотно-сальдовая ведомость по авансам (Excel, ClosedXML) |
| `StandardFrxReportTemplates.generated.cs` | Сгенерированные FRX-макеты стандартных отчётов |

---

## 2. Карта кода

| Слой | Файл | Роль |
| --- | --- | --- |
| Модели | [`Models/Report.cs`](../Models/Report.cs) | `Report`, `ReportField`, `ReportFilter`, `ReportGroup`, `ReportHeaderFooter` |
| Модели | [`Models/ReportDataSet.cs`](../Models/ReportDataSet.cs) | `ReportDataSet`, `ReportDataSetField` |
| Модели | [`Models/FrxReport.cs`](../Models/FrxReport.cs) | `FrxReport`, `FrxBand`, `FrxField`, `FrxExpression` |
| Модели | [`Models/RegulatedReportTemplate.cs`](../Models/RegulatedReportTemplate.cs) | Регламентированные шаблоны |
| Модели | [`Models/FoxProReportFieldRule.cs`](../Models/FoxProReportFieldRule.cs) | Правила распознавания FoxPro-полей |
| Данные | `Data/AppDbContext.cs` | `DbSet<Report*>` и индексы |
| Ядро | [`Services/ReportService.cs`](../Services/ReportService.cs) | Формирование данных + программный Excel/PDF/HTML |
| Ядро | [`Services/ReportDataSetService.cs`](../Services/ReportDataSetService.cs) | SQL-наборы, валидация, синхронизация полей |
| Ядро | [`Services/PrintFormService.cs`](../Services/PrintFormService.cs) | Печатные формы, FRX/native PDF и Excel |
| Ядро | [`Services/FrxParser.cs`](../Services/FrxParser.cs) | Чтение FRX (JSON / бинарный / XML) |
| FoxPro | [`Services/FoxProReportKnowledgeBase.cs`](../Services/FoxProReportKnowledgeBase.cs) | Нормализация имён, алиасы, префиксы наборов |
| FoxPro | [`Services/FoxExpressionParser.cs`](../Services/FoxExpressionParser.cs) | Вычисление Fox-выражений |
| FoxPro | `Services/FoxProReportFieldRuleService.cs` | Правила в БД (`FoxProReportFieldRules`) |
| FoxPro | `Services/FrxRecognitionProfileService.cs` | Профили распознавания и выравнивания |
| Метаданные | [`Services/MetadataService.Reports.cs`](../Services/MetadataService.Reports.cs) | Досев стандартных отчётов и FRX-вариантов |
| Метаданные | `Services/ModuleMetadataService.cs` | Привязка отчётов к модулям и порядок в навигации |
| UI | [`Views/ReportDesignerWindow.xaml`](../Views/ReportDesignerWindow.xaml) | Конструктор (6 вкладок) |
| UI | [`Views/ReportPreviewWindow.xaml`](../Views/ReportPreviewWindow.xaml) | Предпросмотр и экспорт |
| UI | [`Views/AccountingReportsView.xaml`](../Views/AccountingReportsView.xaml) | Бухгалтерская отчётность |
| UI | `Views/ReportDataSetManagementView.xaml(.cs)` | Управление SQL-наборами |
| UI | `Views/MainWorkWindow.xaml.cs` | Навигация и открытие отчёта |

---

## 3. Модель данных

### 3.1 Ядро конструктора (`Reports`)

`Report` — одна таблица, которая описывает и содержание, и вид:

| Группа | Поля |
| --- | --- |
| Идентификация | `Id`, `Name`, `Description`, `Code`, `Icon`, `Order` |
| Источник | `DataSourceType`, `DataSourceId`, `MetadataObjectId` (у наборов данных) |
| Вид | `ReportType`, `SourceFormat` (`Native` / `FoxProFRX`), `Template`, `TemplateVersion`, `Settings` |
| Признаки | `IsActive`, `IsPrintForm`, `IsDefault`, `IsSystem` |
| Страница | `PageOrientation`, `PageWidth/Height`, поля отступов, `FontName`, `FontSize` |
| Оформление | `ShowHeader/Footer/PageNumbers/GridLines/GrandTotal`, `HeaderColor`, `AlternateRowColor` |
| Тексты | `TitleText`, `SubtitleText`, `HeaderText`, `FooterText`, `FooterTotalText`, `FooterSignature`, `SummaryText` |

Дочерние сущности (все с `ON DELETE CASCADE`):

| Сущность | Назначение | Ключевые поля |
| --- | --- | --- |
| `ReportField` | Колонка отчёта | `FieldName` (DbColumnName источника), `DisplayName` (заголовок), `Width`, `Alignment`, `Format`, `AggregateType`, `IsVisible`, `Order` |
| `ReportFilter` | Условие отбора | `FieldName`, `Operation`, `Value`, `Value2`, `Order` |
| `ReportGroup` | Группировка | `FieldName`, `Header`, `Footer`, `PageBreak`, `ShowHeader/Footer` |
| `ReportElementMapping` | Привязка элементов макета к данным | `ElementText`, `ElementExpression`, `BandType`, координаты, `MappedFieldName`, `MappedDisplayName`, `FormatString`, `CustomText` |
| `ReportHeaderFooter` | Тексты шапки/подвала | `SectionType`, `Content`, шрифты |

Индекс, важный для производительности предпросмотра FRX: `ReportElementMappings (ReportId, ElementOrder)`.

### 3.2 Наборы данных (`ReportDataSets`, `ReportDataSetFields`)

| Поле | Назначение |
| --- | --- |
| `Code` | Уникальный код (например `cash.order.turnover`), есть уникальный индекс |
| `MetadataObjectId` | Привязка к объекту метаданных — по ней набор подхватывается отчётом |
| `SqlText` | SQL-текст — фактический источник данных |
| `IsSystem` | Системный набор защищён от случайной перезаписи |
| `Fields` | Снимок колонок результата: `Name`, `DbColumnName`, `FieldType`, `Order` |

### 3.3 FoxPro-правила (`FoxProReportFieldRules`)

`SourcePattern` (индексируется), `CanonicalField`, `MappedFieldName`, `ProfileCode`, `Priority`, `IsRegex`, `IsActive`, `Description`.
Индексы: по `SourcePattern` и по `(ProfileCode, IsActive)`.

### 3.4 Регулируемые шаблоны

`RegulatedReportTemplates`: `Code` + `Version` (уникальная пара), `TemplateData` (байты файла), `Sha256`, `MimeType`, `EffectiveFrom`, `IsActive`.
Активной считается последняя версия по коду; `SetActiveTemplateAsync` гасит остальные версии, `InferDraftFromFileName` разбирает имя файла в код/название/версию.

### 3.5 Служебная таблица

`DeletedStandardReports`: `Code` (PK), `Name`, `DeletedAt`, `DeletedBy`, `Reason`.
Это «кладбище»: при досеве стандартных отчётов коды отсюда пропускаются, иначе удалённый пользователем отчёт возвращался бы после каждого запуска.
`IsStandardReportCode` считает стандартными коды с префиксами `standard.`, `assets.`, `inventory.`, `cash.receipt.`, `cash.payment.`, `invoice.sales.`, `invoice.purchase.`, `payment.order.`.

### 3.6 Финансовые формульные строки

`FinancialReportLines` (уникальный `ReportCode` + `LineCode`) и `FinancialReportLineAccounts` (уникальный `LineId` + `AccountCode`) — настройка формул баланса и финансовых результатов.

---

## 4. Как отчёт привязан к данным

Цепочка связей:

```
Report.DataSourceId
      │
      ▼
MetadataObjects ──► MetadataObject.TableName        (физическая таблица в PostgreSQL)
      │           └► MetadataObject.Fields[]  ──► MetadataField.DbColumnName / Name / FieldType
      │
      └──► (если есть) ReportDataSets, у которых MetadataObjectId = этот объект
```

Ключевой момент: `ReportField.FieldName` — это **не** заголовок колонки, а физическое имя колонки источника (`DbColumnName`).
`ReportField.DisplayName` — то, что видит пользователь. Именно поэтому в SQL используется конструкция
`CAST("db_column" AS text) AS "Отображаемое имя"`.

Типы полей источника (`MetadataField.FieldType`) влияют на поведение:

| Тип | Поведение |
| --- | --- |
| `Reference` | В SQL приводится к `text`, затем GUID заменяется на отображаемое имя через `ReferenceDisplayHelper` |
| `Decimal` | Формат по умолчанию `N2` |
| `DateTime` | Формат по умолчанию `dd.MM.yyyy` |
| Остальные | Формат по умолчанию пустой |

Если в отчёте поле не описано, а в источнике поля нет, срабатывают два «моста» совместимости:
`TryBuildComputedReportFieldSelect` (вычисляемое поле из `ReportComputedFieldCatalog`) и
`TryBuildCompatibleReportFieldSelect` (спец-совместимость для источников «Проводки» и «Касса»).
Если не сработал ни один — бросается понятная ошибка: «Поле '...' отсутствует в источнике '...'».

---

## 5. Конвейер формирования данных (по шагам)

Всё живёт в `ReportService.GetReportDataAsync`. Последовательность:

1. **Проверки.** Отчёт не пустой; заполнен `DataSourceId`; источник метаданных существует.
2. **Определение колонок.** Берутся видимые `ReportFields` по `Order`. Если полей нет вообще — берутся все поля источника, кроме `Id`.
3. **Ветвление источника** (три ветки, порядок важен):

   | Условие | Что происходит |
   | --- | --- |
   | У источника есть свой `ReportDataSet` | Выполняется SQL набора, результат — основа отчёта |
   | Источник — оборотно-сальдовая ведомость по кассе | Специализированный `GetCashOrderTurnoverReportDataAsync` с расчётом входящих/исходящих остатков |
   | Иначе | Универсальный `SELECT` по таблице источника |

4. **Сборка SELECT.** Для каждой колонки: `QuoteIdentifier(DbColumnName) AS QuoteIdentifier(DisplayName)`. Если ни одной — исключение «В отчете не выбрано ни одного видимого поля».
5. **Фильтры.** `BuildFilterClauses` строит `WHERE` **с параметрами** (`@filter0`, `@filter1`, …), а не конкатенацией строк. Поддерживаются `=`, `<`, `>`, `<=`, `>=`, `<>`, `Like` (`ILIKE`), `Between`. Значения приводятся к типу поля (`ConvertFilterValue`).
6. **Служебные фильтры.** `AddDefaultFixedAssetSnapshotFilterAsync` подставляет период по ОС, если в условиях его ещё нет.
7. **Выполнение.** Таймаут 30 с (60 с для тяжёлых отчётов), `DataTable.Load(reader)`.
8. **Вычисляемые поля.** `ReportComputedFieldCatalog.AddComputedColumns` добавляет канонические колонки, которых не хватает.
9. **Ссылки.** `ResolveReportReferencesAsync` заменяет GUID на отображаемые значения.

Ошибки PostgreSQL ловятся отдельно (`PostgresException`) — сообщение переводится на русский и показывается пользователю.

---

## 6. Наборы данных (`ReportDataSetService`)

Это способ задать произвольную выборку (JOIN, CTE, оконные функции) и подключить её к обычному объекту метаданных. После этого любой отчёт на этот объект будет работать с новым SQL.

### 6.1 Жизненный цикл

| Шаг | Метод | Что делает |
| --- | --- | --- |
| 1 | `EnsureSchemaAsync` | Создаёт `ReportDataSets` и `ReportDataSetFields` через `CREATE TABLE IF NOT EXISTS` |
| 2 | `SaveAsync` | Сохраняет набор; `NormalizeSql` чистит текст |
| 3 | `TestAsync(dataSet, limit = 50)` | Прогон под лимитом для проверки |
| 4 | `RefreshFieldsFromSqlAsync` | Выполняет `LIMIT 0`, читает колонки результата и **пересоздаёт** список полей |
| 5 | `SyncMetadataSourceAsync` | Синхронизирует объект метаданных с набором (создаёт/чинит поля объекта) |
| 6 | `ExecuteAsync` | Рабочий прогон с параметрами |

### 6.2 Как выполняется SQL

```
SELECT * FROM (<текст набора>) AS report_dataset_result   [LIMIT @__bis_limit]
```

- Обёртка в подзапрос нужна, чтобы `LIMIT` и параметры добавлялись без правки пользовательского SQL.
- Подключение — отдельное `NpgsqlConnection` (`OpenStandaloneConnectionAsync`), а не контекст EF.
- Таймаут 60 секунд.
- `AddSqlParameters` подставляет значения только для плейсхолдеров, реально встречающихся в тексте.
- Разрешены только `SELECT` и `WITH` — это указано прямо в заголовке экрана `ReportDataSetManagementView`.

Поддерживаемые параметры (объявлены на экране управления наборами):

| Параметр | Смысл |
| --- | --- |
| `@PeriodStart` | Начало периода |
| `@PeriodEnd` | Конец периода |
| `@CashDeskId` | Касса |
| `@OrganizationId` | Организация |
| `@EmployeeId` | Сотрудник |
| `@AccountCode` | Код счёта |

### 6.3 Безопасность

Текст набора проходит нормализацию и валидацию:

| Проверка | Реализация |
| --- | --- |
| Запрещённые операции | `ForbiddenSqlRegex`: `insert`, `update`, `delete`, `drop`, `alter`, `create`, `truncate`, `grant`, `revoke`, `copy`, `call`, `execute`, `merge`, `vacuum`, `analyze`, `do`, `begin`, `commit`, `rollback` |
| Несколько команд | `HasCommandSeparatorOutsideStrings` ищет `;` вне строковых литералов |
| Комментарии | `StripSqlComments` вырезает `--` и `/* */` перед проверкой, чтобы обойти фильтр не получилось |
| Идентификаторы | `SanitizeIdentifier` |

### 6.4 Системные наборы

`EnsureCashOrderTurnoverDataSetAsync` создаёт набор `cash.order.turnover` (оборотно-сальдовая ведомость по кассе) с готовым SQL: он уже отдаёт FoxPro-совместимые псевдонимы (`debsum`, `credsum`, `nuch`, `tex`, `name_kod`, `module`, …), чтобы старые FRX-макеты работали без правил распознавания.

---

## 7. Вычисляемые поля и нормализация имён

### 7.1 `ReportComputedFieldCatalog`

Канонические поля, которых может не быть в источнике. Каждое имеет набор FoxPro-алиасов:

| Каноническое поле | Тип | Алиасы (примеры) |
| --- | --- | --- |
| `number` | String | `doc_number`, `document_number`, `dok`, `nomdok` |
| `date` | DateTime | `doc_date`, `document_date`, `d_doc`, `d_oper` |
| `organization` | String | `primary_organization`, `contractor`, `name_kod` |
| `cash_desk` | String | `cashdesk`, `cash_desk_name` |
| `debit_account` | String | `debet`, `schet`, `sch` |
| `credit_account` | String | `credit`, `kredit`, `kor_sch`, `korsch` |
| `amount` | Decimal | `sum`, `summa`, `debsum`, `credsum` |
| `basis` | String | `osn`, `osnov`, `основание` |
| `description` | String | `note`, `text`, `tex`, `sod` |
| `module` | String | `module_name`, `module_code`, `prs` |
| `blank_number` | String | `tax_blank_number`, `nom_bl`, `ser_bl` |
| `organization_tax_number` | String | `inn`, `tin`, `nni` |
| `page_number` | Int | `_pageno` |
| `amount_words` | String | прописью (сейчас числовая строка с `N2`) |

Нормализация (`Normalize`) снимает кавычки, фигурные скобки, знак `=`, заменяет `!` на `.` и обрезает префикс набора — поэтому `=name_kod`, `{name_kod}` и `ved.name_kod` сводятся к одному смыслу.

### 7.2 `FoxProReportKnowledgeBase`

Отдельная база знаний именно для FoxPro:

- **Префиксы наборов данных**: `ved`, `ved2..ved4`, `db_cr`, `dbcr`, `ksprorg`, `avt_p`, `fact`, `irfactsw`.
- **Префиксы строк отчёта**: `line1_name_kod`, `line1.name_kod` → первая строка и т. д.
- **Обёртки выражений**: `ALLTRIM(x)`, `DTOC(x)`, `NVL(...)`.
- **Правила по умолчанию** (`GetDefaultRules`) — сидятся в БД при первом запуске (`SeedDefaultRulesAsync`).
- **Сопоставление**: `RuleMatches` (точное совпадение или regex), `GetTargetFieldCandidates`, `LooksLikeKnownDatasetField`.

### 7.3 `FoxExpressionParser`

Интерпретатор выражений FoxPro: `Evaluate(expression, row)`, `Sum(rows, field)`, `Count`, `Avg`, `SetVariable`.
Переменные (`DefaultVariables` из профиля распознавания) позволяют подставлять, например, организацию или период прямо в макет.

---

## 8. Ссылки, фильтры, группировки, итоги

### 8.1 Ссылки

`ResolveReportReferencesAsync` находит поля с типом `Reference`, загружает карты `Guid → Название` через `ReferenceDisplayHelper.LoadMapsAsync` и заменяет значения в `DataTable`. Колонка предварительно переводится в `ReadOnly = false`, иначе запись невозможна.

### 8.2 Фильтры — два уровня

| Уровень | Где | Как реализовано |
| --- | --- | --- |
| SQL-фильтры | `BuildFilterClauses` | **Параметризовано**, безопасно, применяется на сервере БД |
| Фильтры представления | `ApplyFilters` / `GetFilterExpression` | `DataTable.DefaultView.RowFilter`, строки собираются вручную |

Во втором уровне значения подставляются текстом без экранирования кавычек — это известное слабое место (см. раздел 13).

### 8.3 Группировка

`ApplyGroups` сейчас **не агрегирует**: группа вычисляется, результат не применяется. Поля `ReportGroup` и `AggregateType` в `ReportField` заведены как задел.

### 8.4 Итоги

`CalculateReportTotals` считает суммы по числовым колонкам и выводит их строками, если включён `ShowGrandTotal`. Для бухгалтерских отчётов строки итогов часто добавляются вручную (`AddTotalsRow` в `AccountingReportsView`).

---

## 9. Пользовательский интерфейс

### 9.1 Навигация (`MainWorkWindow`)

Отчёты попадают в дерево навигации в три места:

| Раздел | Источник | Условие |
| --- | --- | --- |
| Внутри модуля | `MetadataModuleItems` с `ObjectType = "Report"` | Отчёт привязан к активному модулю |
| Общий раздел «Отчёты» | `GetNavigationReportsAsync()` | `IsActive && !IsPrintForm` |
| «Бухгалтерские отчёты» | Отдельный пункт `AccountingReports` | Всегда доступен |

Скрытие отчётов:

- `IsHiddenByUnavailableModule` — скрывает, если источник отчёта скрыт или отчёт назначен только неактивным модулям;
- `DevelopmentHiddenNavigationObjectNames` — список имён, скрытых на время разработки;
- `HideUnassignedObjectsInNavigationDuringDevelopment` — прячет неназначенные модулям объекты;
- `ApplyUserPermissionsAsync` — фильтрует дерево по правам пользователя (`UserAccessService.GetAllowedKeysAsync`).

**Открытие отчёта** (`OpenReport`): если это акт сверки — открывается `AccountingReportsView`, иначе отчёт молча формируется и показывается в `PdfPreviewWindow`:

```
OpenReport(report)
   ├─ GetReportAsync(id)          — полная загрузка с полями и привязками
   ├─ GetReportDataAsync(report)  — данные
   └─ ExportToPdf(data, report)   — PDF → PdfPreviewWindow
```

Следствие: из навигации нельзя «просто посмотреть» отчёт таблицей — только PDF. Интерактивная таблица есть в `ReportPreviewWindow` (конструктор) и в `AccountingReportsView`.

### 9.2 Конструктор (`ReportDesignerWindow`)

Шесть вкладок:

| Вкладка | Содержимое |
| --- | --- |
| `📋 Общие` | Название, описание, источник данных, тип отчёта, заголовки/подвалы, шаблон, флажки `Отчет доступен` / `Печатная форма` / `По умолчанию` |
| `📊 Поля` | Список доступных полей источника, список вычисляемых полей, таблица колонок отчёта (ширина, выравнивание, видимость, порядок) |
| `🔗 FRX поля` | Привязка элементов макета к полям данных + блок общих правил распознавания FoxPro-полей |
| `🧱 Нативный макет` | Холст: элементы, блоки (PageHeader/Header/Detail/Footer/Summary), координаты, шрифты, выражения, выделение подстроки |
| `🎨 Оформление` | Ориентация, шрифт, размер, цвет шапки, чередование строк, линии сетки, итоги, нумерация страниц |
| `🔍 Фильтры` | Условия отбора: поле, операция, значение |

Кнопки: `👁️ Предпросмотр`, `💾 Сохранить`, `PDF`, `❌ Отмена`.
Служебные кнопки источника: `?` — выбрать существующий источник, `+` — создать набор данных.

Ключевые методы: `LoadAvailableFields`, `SyncReportFieldsWithSourceIfStale` (подтягивает новые поля источника в отчёт), `BuildReportFromForm`, `SaveReport`, `GenerateAndShowReport`, `ApplyFoxProRulesToEmptyMappings`, `SaveMappedFoxProRulesAsync`.

### 9.3 Предпросмотр (`ReportPreviewWindow`)

Показывает `DataTable` с применённым оформлением отчёта (`ApplyReportAppearance`) и даёт экспорт: Excel, HTML, PDF, печать.

### 9.4 Бухгалтерская отчётность (`AccountingReportsView`)

Панель управления: тип отчёта, период «С / По», кнопка `Сформировать`, выбор **формата вывода**, организация, поиск по всем колонкам, «Собрать период», «Закрыть баланс», рабочий модуль, вариант ОСВ / вариант акта сверки.

---

## 10. Формирование и вывод

### 10.1 Семь форматов вывода

| Формат в UI | Кто реализует | Особенности |
| --- | --- | --- |
| Программный Excel | `ReportService.ExportToExcel` (ClosedXML) | Заголовок, дата формирования, таблица со стилем, автоширина |
| Программный PDF | `ReportService.ExportToPdf` (QuestPDF, `LicenseType.Community`) | Шапка с реквизитами организации, таблица, чередование строк, итоги, «Стр. N из M» |
| Нативный Excel | `PrintFormService.ExportReportTemplateExcel` | Берёт макет конструктора |
| Нативный PDF | `PrintFormService.ExportReportTemplatePreview` | Берёт макет конструктора |
| FRX FoxPro Excel | `PrintFormService` + `ConfigureFrxExcelWorksheet` | Раскладка по координатам элементов, линии и рамки FRX переносятся в ячейки Excel |
| FRX FoxPro PDF | `PrintFormService` + `BuildTemplateLayoutPdf` | Полный рендер FRX с упаковкой шаблонов |
| Налоговый Excel | `VatTaxReportExportService.ExportMonthlyVatReportAsync` | Заполнение регламентированного шаблона; при несовпадении шапок — `ExportFallbackWorkbook` |

Рендеринг FRX-шаблонов перед выводом идёт через `PrepareTemplateForReportRendering`, который:
- применяет профиль распознавания (`FrxRecognitionProfileService`),
- «упаковывает» разрозненные блоки в нормализованные (`BuildPackedTabularFrxTemplate`, `BuildPackedReconciliationFrxTemplate`, `BuildPackedStaticFrxTemplate`) — так печатные формы перестают «рассыпаться» по страницам.

### 10.2 Особенности программного PDF

- Размер страницы — A4, ориентация из `Report.PageOrientation`.
- В шапке автоматически подтягивается основная организация (`GetPrimaryOrganization`): наименование, ИНН, ОКПО, директор, главный бухгалтер.
- Ширина колонки берётся из `ReportField.Width`.
- Для отчёта типа `InvoiceMaterialsKg` печатается заголовок `СЧЕТ-ФАКТУРА` и строка подписей.

### 10.3 Налоговый экспорт

`VatTaxReportExportService` работает через COM-мост Excel (`dynamic`, `ReleaseComObject`). Он умеет:
- найти лист-регистр по ключевым словам (`FindWorksheet`),
- распознать шапку регистра (`DetectRegisterHeader`, `TryDetectColumnRole`) и подставить значения по ролям колонок,
- при неудаче построить книгу с нуля (`CreateFallbackRegisterLayout`, `ExportFallbackWorkbook`),
- заполнить итоговые формулы в подвале (`WriteFooterFormulas`).

---

## 11. Импорт FoxPro/FRX и печатные формы

### 11.1 Маршрут импорта

```
Файл .frx / .json FoxPro
   │
   ▼
FrxParser.ParseFrxFile(...)            три формата: FoxPro-JSON, бинарный FRX, FRX-XML
   │                                  (+ ReadVisualFoxProRecords для DBF-записей)
   ▼
PrintFormTemplate (блоки, элементы, выражения, линии, рамки)
   │
   ▼
FrxRecognitionProfileService.ApplyImportProfile / PrepareForRendering
   │   • определение профиля по шаблонам имени файла и обязательным выражениям
   │   • переопределение выравнивания, нормализация координат блоков
   │   • CompactVerticalGaps — схлопывание пустот
   ▼
Report.Template (JSON в метаданных отчёта)
   │
   ▼
ReportElementMappings — привязка элементов к полям (вручную или по правилам)
```

### 11.2 Профили распознавания

`FrxRecognitionProfile` описывает: `FileNamePatterns`, `RequiredExpressions`, `DefaultVariables`, `Layout`, `AlignmentOverrides`.
Профиль определяется автоматически (`DetectProfile`) по совпадению масок и выражений. Для актов сверки используется профиль `foxpro_reconciliation_act`.

### 11.3 Правила распознавания полей

Три уровня приоритета:

| Уровень | Где задаётся | Когда применять |
| --- | --- | --- |
| 1. Код | `FoxProReportKnowledgeBase` (алиасы, префиксы, обёртки) | Системные и повторяющиеся соответствия |
| 2. БД | `FoxProReportFieldRules` (с `Priority`) | Рабочие правила для конкретной группы форм |
| 3. Вручную | `ReportElementMappings` | Разовая привязка элемента к полю |

Кнопки на вкладке `FRX поля`: `Применить общие правила`, `Сохранить привязки как правила`, `Сохранить список правил`.

### 11.4 Конвертация FRX в обычный отчёт

`FrxToReportConverter.ConvertToStandardReport` переносит `FrxReport` в `Report`: банды `Header`/`Footer` → тексты шапки и подвала, видимые `FrxField` → `ReportField` с шириной и выравниванием. `GenerateHtmlFromFrx` даёт быстрый HTML-предпросмотр макета.

### 11.5 Классификация отчётов

`ReportClassificationService` отвечает на вопрос «это акт сверки?»:

```csharp
IsReconciliationReport(report) =>
    ReportType == "ReconciliationAct"
 || Code начинается с "standard.frx.finance.reconciliation."
 || в Name / Code / Description / Template есть маркер:
      "reconciliation", "akt_sver", "akt-sver", "aktsver", "sver", "свер"
```

От этого зависит выбор: открывать `AccountingReportsView` или обычный PDF-отчёт.

---

## 12. Стандартные отчёты

### 12.1 Как они появляются

`MetadataService.EnsureStandardReportsAsync()` вызывается при старте приложения (`MainWorkWindow`) и идемпотентно создаёт/обновляет записи из `BuildStandardReportDefinitions()`.
Для FRX-отчётов макеты берутся из сгенерированного файла `StandardFrxReportTemplates.generated.cs` (шаблоны хранятся как base64 + zip и распаковываются через `DecodeStandardFrxTemplate`).

### 12.2 Реестр кодов

| Модуль | Код | Название |
| --- | --- | --- |
| Финансы | `standard.finance.postings-journal` | Журнал проводок |
| Финансы | `standard.finance.trial-balance` | Оборотно-сальдовая ведомость |
| Финансы | `standard.finance.general-ledger` | Главная книга |
| Финансы | `standard.finance.bank-statement` | Выписка банка |
| Финансы | `standard.finance.payment-order-registry` | Реестр платежных поручений |
| Финансы | `standard.finance.reconciliation-act` | Акт сверки |
| Финансы | `standard.finance.sales-invoice-journal` | Журнал продаж |
| Финансы | `standard.finance.purchase-invoice-journal` | Журнал закупок |
| Финансы | `standard.finance.cash-receipts` | Приходные кассовые ордера |
| Финансы | `standard.finance.cash-payments` | Расходные кассовые ордера |
| Финансы | `standard.finance.advance-reports` | Авансовые отчёты |
| Финансы | `standard.finance.exchange-rate-differences` | Курсовые разницы |
| Основные средства | `standard.fixed-assets.list` | Список ОС |
| Основные средства | `standard.fixed-assets.balances` | Оборотно-сальдовая ведомость по ОС |
| Основные средства | `standard.fixed-assets.by-account` | Ведомость ОС по счёту |
| Основные средства | `standard.fixed-assets.depreciation` | Ведомость амортизации |
| Основные средства | `standard.fixed-assets.card` | Карточка ОС |
| Основные средства | `standard.fixed-assets.journal` | Журнал операций по ОС |
| Основные средства | `standard.fixed-assets.responsible-person` | Ведомость ОС по МОЛ |
| Основные средства | `standard.fixed-assets.directory` | Справочник ОС |
| Основные средства | `standard.fixed-assets.inventory-transfer` | Передача ОС в подотчёт |
| Основные средства | `standard.fixed-assets.movement` | Движение ОС |

Каждое определение — record `StandardReportDefinition(Code, Name, Description, ModuleCode, SourceName, SourceObjectType, ReportType, Icon, Order, PageOrientation, Fields)`.
Ширина и формат колонок выводятся из типа поля: `GetStandardReportFieldWidth`, `GetStandardReportFieldFormat` (`N2` / `dd.MM.yyyy`).

### 12.3 Варианты макетов

| Что | Зачем |
| --- | --- |
| `MarkReconciliationFrxReportsAsTemplateVariantsAsync` | Несколько FRX-макетов акта сверки помечаются как варианты одного отчёта |
| `MarkTrialBalanceFrxReportsAsTemplateVariantsAsync` | То же для ОСВ |
| `IsStandaloneStandardFrxVariant` | Отделяет самостоятельные FRX-отчёты от вариантов |
| `EnsureCashOrderTurnoverReportSourceAsync` | Создаёт объект-источник для оборотно-сальдовой ведомости по кассе |
| `EnsurePaymentOrderPrintFormAsync` | Гарантирует печатную форму платёжного поручения |
| `DeleteDeprecatedObjectTreeReportsAsync` | Вычищает устаревшие отчёты дерева объектов |

### 12.4 Защита от удаления

`StandardReportDeletionService.MarkDeletedAsync` записывает код в `DeletedStandardReports`, и при следующем досеве этот отчёт больше не создаётся.

---

## 13. Ограничения и точки роста

| Проблема | Где | Следствие |
| --- | --- | --- |
| `ApplyGroups` не агрегирует | `ReportService` | Группировка из конструктора визуально не работает; итоги считаются только через `ShowGrandTotal` |
| `GetFilterExpression` склеивает значения без экранирования | `ReportService` | Риск некорректного `RowFilter` при кавычках в значении. SQL-путь параметризован и безопасен |
| `ReportHeaderFooter` не зарегистрирован в `AppDbContext` | `Data/AppDbContext.cs` | Модель есть, но набор данных не подключён — часть настроек шапки/подвала не используется |
| `amount_words` возвращает число строкой | `ReportComputedFieldCatalog` | Нужен отдельный сервис прописи суммы |
| Открытие из навигации — только PDF | `MainWorkWindow.OpenReport` | Нет интерактивного просмотра с фильтрами; `AccountingReportsView` закрывает потребность лишь частично |
| `VatTaxReportExportService` использует COM-мост Excel | `Services/VatTaxReportExportService.cs` | Нужен установленный Excel, риск зависших COM-объектов |
| `QuestPDF.Settings.License = LicenseType.Community` | `ReportService.ExportToPdf` | Ограничение лицензии на размер и состав документа |
| `PrintFormService` ≈ 2900 строк, синхронные методы | `Services/PrintFormService.cs` | Сложно тестировать и поддерживать, нет асинхронности |
| `StandardFrxReportTemplates.generated.cs` ≈ 200 КБ | `Services` | Сгенерированный код в репозитории, конфликты при правках |
| Схемы создаются из кода (`CREATE TABLE IF NOT EXISTS`) | `ReportDataSetService`, `RegulatedReportTemplateService`, `StandardReportDeletionService` | Расхождение с EF-моделями, миграции не используются |

---

## 14. Типовые рецепты

### 14.1 Добавить обычный отчёт на справочник (без кода)

1. Открыть `Конфигуратор` (`ConfiguratorWindow`) и перейти в раздел отчётов (кнопка `📊 Отчет` или меню `Отчеты` → `Создать`). Вызывается `OnCreateReportClick`.
2. Вкладка `📋 Общие`: задать название, тип отчёта (`Таблица` / `Список` / `Карточка` / `Расходный/Приходный КО` / `Счет-фактура на материалы (КР)` / `Макет Visual FoxPro FRX`), при необходимости флажок `Печатная форма документа`.
3. Выбрать источник данных (`?` рядом с ComboBox) — справочник или документ метаданных.
4. Вкладка `📊 Поля`: отметить нужные поля, задать `Отображаемое имя`, ширину, выравнивание, порядок (`⬆ Вверх` / `⬇ Вниз`).
5. При необходимости вкладка `🔍 Фильтры`: `➕ Добавить условие`.
6. Вкладка `🎨 Оформление`: ориентация, шрифт, цвет шапки, чередование строк, линии сетки, общий итог, номера страниц.
7. `💾 Сохранить` → `👁️ Предпросмотр` → `PDF`.
8. Привязать отчёт к модулю, чтобы он появился в нужном разделе навигации.

### 14.2 Отчёт со сложным SQL

1. `Конфигуратор` → `🧩 Наборы данных отчетов` (кнопка `ReportDataSetsButton` или меню `Отчеты` → `🧩 Наборы данных отчетов`).
2. `+ Новый`, указать код, название, объект-источник и SQL (только `SELECT` / `WITH`).
3. `Тест` — прогон с `LIMIT 50`.
4. `Обновить поля` — перечитывает колонки результата.
5. `Сохранить`.
6. Дальше — как в 14.1: конструктор отчёта на этот источник.
7. Если набор системный и должен жить в коде — добавить его в `EnsureStandardDataSetsAsync`.

### 14.3 Перенести печатную форму из FoxPro

1. В Конфигураторе: `Создать` отчёт → вкладка `🔗 FRX поля` → `📂 Загрузить FRX-файл`
   (либо сразу импорт через `FrxImportWindow`, кнопка меню `Отчеты` → импорт FRX).
2. Дождаться разбора файла и определения профиля.
3. `Применить общие правила` — заполнить привязки, которые распознались.
4. Долить руками оставшиеся элементы в таблице «Соответствие элементов макета полям данных».
5. `Сохранить привязки как правила` — чтобы то же сработало на других формах.
6. Проверить `👁️ Предпросмотр` и выбрать формат `FRX FoxPro PDF/Excel`.
7. Если правила нужны всегда — добавить их в `FoxProReportKnowledgeBase.GetDefaultRules()`.

### 14.4 Добавить системный стандартный отчёт

1. Добавить запись в `BuildStandardReportDefinitions()` (код с префиксом `standard.`).
2. Указать `ModuleCode`, `SourceName`, список `Fields`.
3. При необходимости — добавить объект-источник через `Ensure...ReportSourceAsync`.
4. Пересобрать и запустить приложение: отчёт появится сам.
5. Проверить, что он не попал под фильтр `IsHiddenByUnavailableModule` и назначен модулю.

### 14.5 Добавить новый формат вывода

1. Реализовать метод экспорта рядом с существующими в `ReportService` или `PrintFormService`.
2. Добавить `ComboBoxItem` с новым `Tag` в `AccountingReportsView.xaml`.
3. Добавить распознавание тега в `OnOpenReportClick` / `GetSelectedReportOutputFormat`.

---

## 15. Тесты

`BIS.ERP.TESTS.Core/FinanceReportsScenario.cs` — смоук-сценарий на формульную основу Fox-совместимых финансовых отчётов (строки баланса и финансовых результатов).
Отдельного покрытия на `ReportDataSetService`, `PrintFormService` и правилах распознавания на момент написания документа нет — при доработке отчётности это первые кандидаты на тесты.

---

## 16. Глоссарий

| Термин | Значение |
| --- | --- |
| **Отчёт (`Report`)** | Запись в метаданных: источник + колонки + фильтры + оформление |
| **Источник данных** | Объект метаданных (`MetadataObjects`): его таблица и поля |
| **Набор данных (`ReportDataSet`)** | Пользовательский SQL, подменяющий выборку источника |
| **Печатная форма** | Отчёт с `IsPrintForm = true`, печатается по конкретной записи документа |
| **FRX** | Файл макета Visual FoxPro; в системе — `Report.Template` + `ReportElementMappings` |
| **Профиль распознавания** | Набор правил выравнивания, переменных и проверок для группы FRX-форм |
| **Вычисляемое поле** | Каноническое поле (`ReportComputedFieldCatalog`), которого может не быть в источнике |
| **Алиас** | Старое FoxPro-имя, приравненное к каноническому полю |
| **Правило распознавания** | Связь «FoxPro-выражение → поле данных» с приоритетом и профилем |
| **Вариант макета** | Несколько FRX-форм одного отчёта (ОСВ, акт сверки) |
| **Программный / нативный / FRX вывод** | Три движка вывода: кодом, по макету конструктора, по FoxPro-макету |

---

## 17. Связанные документы

| Документ | О чём |
| --- | --- |
| [`FoxProReportRules.md`](FoxProReportRules.md) | Настройка распознавания FoxPro-полей в FRX |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Общая архитектура приложения |
| [`PROJECT_SCHEMA.md`](PROJECT_SCHEMA.md) | Схема данных проекта |
| [`MDI_WORKSPACE.md`](MDI_WORKSPACE.md) | Рабочее пространство и навигация |