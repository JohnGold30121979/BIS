# Авансовые платежи: классическая модель строк и фикс отсутствующей таблицы шапки (`42P01`)

Дата: 2026-10-02. Статус: фикс внесён в код. Сборка не завершена (dotnet-процессы
зависали/убивались, вывод `dotnet build` получить не удалось — см. `_clean_build.txt`
пустой; повторный прогон сборки и сценария `finance-weak-documents` обязателен).

## 1. Симптом

На старых базах (пример: `bis_OsOO1_20260908_112439`) сохранение/проводка авансового
отчёта падало с ошибкой PostgreSQL:

```text
42P01: отношение "doc_advance_payment" не существует
```

При этом в метаданных (`MetadataObjects`) документ «Авансовые платежи» уже указывал
`TableName = doc_advance_payment`, а физически существовала только таблица строк
`doc_advance_payment_lines`.

## 2. Диагноз (root cause)

Цепочка сидинга в `Services/DocumentationMetadataSeedService.cs`:

1. `EnsureDocumentsAsync()` → `EnsureObjectAsync("Авансовые платежи", "doc_advance_payment", ...)`.
   Для **уже существующего** объекта метаданных метод только дополняет поля
   (`EnsureObjectFieldsAsync`) и **физическую таблицу не создаёт**
   (`CreateDynamicTableAsync` вызывается только для новых объектов).
2. `EnsureFinanceDocumentTableNamesAsync()` → `RenameDocumentTableAsync(...)`
   обновлял `TableName` в метаданных на `doc_advance_payment`, а физическую таблицу
   переименовывал только если она существовала (`to_regclass(...) IS NOT NULL`).
   В базах, где шапки вообще не было (таблица строк создавалась отдельно через
   `AdvancePaymentService.EnsureSchemaAsync`), после обновления метаданных
   физическая таблица `doc_advance_payment` так и не появлялась.
3. `EnsureSchemaAsync()` создаёт таблицу строк и вешает FK `advance_id → шапка`,
   но FK добавляется **только если шапка существует** (проверка через `to_regclass`).
   Следствие: либо нет FK (строки-сироты возможны), либо — если код обращается
   к шапке напрямую — `42P01`.

Итого: метаданные и физическая схема рассинхронизировались.

## 3. Что сделано

### 3.1. Исправление сидинга

Файл: `BIS.ERP/Services/DocumentationMetadataSeedService.cs`,
метод `EnsureFinanceDocumentTableNamesAsync()` (строки ~376–411):

- после `RenameDocumentTableAsync(...)` добавлены вызовы
  `EnsureDocumentTableAsync("Авансовые платежи")` и
  `EnsureDocumentTableAsync("Расчет курсовой разницы")` — **до** `EnsureSchemaAsync()`,
  чтобы FK таблицы строк мог сослаться на шапку;
- добавлен приватный метод `EnsureDocumentTableAsync(string documentName)` (~строки 441–455):
  загружает `MetadataObjects` (`ObjectType == "Document"`, с `Include(Fields)`)
  и вызывает `_metadataService.CreateDynamicTableAsync(document)` —
  идемпотентно (`CREATE TABLE IF NOT EXISTS` + `ADD COLUMN IF NOT EXISTS`).

Порядок операций при старте теперь такой:

```text
EnsureObjectAsync (метаданные + поля)
  → RenameDocumentTableAsync (doc_fin_<hash> → понятное имя, если есть что переименовывать)
  → EnsureDocumentTableAsync (гарантия физической таблицы шапки)
  → AdvancePaymentService.EnsureSchemaAsync (таблица строк + FK на шапку)
  → удаление мета-поля expense_lines (чтобы MetadataService не пересоздавал колонку)
  → MigrateLegacyExpenseLinesAsync (перенос JSON expense_lines в строки + DROP COLUMN)
```

### 3.2. Модель данных (без изменений, для справки)

- `Services/AdvancePaymentService.cs` — классическая модель как у счет-фактур:
  шапка `doc_advance_payment` (имя из `MetadataObject.TableName`, константа
  `DefaultHeaderTableName`), строки `<шапка>_lines` с `advance_id → шапка (ON DELETE CASCADE)`.
- `Services/MetadataService.AdvancePayments.cs` — тонкие обёртки
  (`Get/SaveAdvancePaymentLinesAsync`), каждый раз новый `AdvancePaymentService` + `Configure(tableName)`.
- `Services/MetadataService.FinanceDocuments.cs` — проводки строятся **по строкам**
  (`ResolveAdvanceExpensePostingLinesAsync`), JSON `expense_lines` не используется.
- `Views/Dialogs/FinanceDocumentDialog.xaml.cs` (`OnSaveClick`, ~строка 579):
  шапка → `SaveAdvancePaymentLinesAsync` → `PostDocumentAsync`.
- `Views/FinanceDocumentWorkView.xaml.cs` (~строка 74): пакетная загрузка строк
  одним запросом (`GetAdvancePaymentLinesForDocumentsAsync`), без N+1.

### 3.3. Временные изменения окружения (не коммитить без решения)

- `BIS.ERP.TESTS/config/testsettings.json`: пароль БД временно заменён на `qwerty123`
  для локального прогона. **Перед коммитом вернуть исходный или вынести в секреты.**

## 4. Проверка

### 4.1. Сценарий `finance-weak-documents`

```powershell
dotnet run --project <путь>/BIS.ERP.TESTS.Console -- finance-weak-documents run
```

Что проверяет (`BIS.ERP.TESTS/BIS.ERP.TESTS.Core/FinanceWeakDocumentsScenario.cs`):

- документ «Авансовые платежи»/«Авансовый отчет» не содержит мета-поле `expense_lines`;
- `TableName == "doc_advance_payment"`;
- физическая таблица `<шапка>_lines` содержит колонки
  `advance_id, line_number, expense_account, credit_account, amount`.

Статус: предыдущие прогоны зависали на этапе сборки/сидинга; требуется повторный
прогон до завершения и разбор `errors`/`details` отчёта.

### 4.2. Ручная проверка в БД (psql)

```sql
-- метаданные
SELECT "Name", "TableName" FROM "MetadataObjects"
 WHERE "ObjectType" = 'Document' AND "Name" = 'Авансовые платежи';
-- физические таблицы
SELECT tablename FROM pg_tables
 WHERE schemaname = 'public' AND tablename LIKE 'doc_advance%';
-- колонки строк
SELECT column_name FROM information_schema.columns
 WHERE table_name = 'doc_advance_payment_lines' ORDER BY ordinal_position;
```

### 4.3. Сквозной тест (E2E, вручную)

Создать авансовый отчёт → строки в таблице → провести → проверить проводки;
на базе со старым JSON — убедиться, что строки перенеслись и колонка
`expense_lines` удалена.


## 5. Анализ кода авансов: найденные проблемы

Упорядочено по серьёзности. Номера строк — на момент написания.

### P1. Потеря данных при миграции JSON → строки

`AdvancePaymentService.MigrateLegacyExpenseLinesAsync()` (~строки 288–322):
невалидный JSON и пустой payload молча пропускаются (`catch { continue; }`),
а в конце колонка `expense_lines` удаляется **безусловно**
(`DROP COLUMN IF EXISTS`). Строки, которые не распарсились, исчезают навсегда.

Предложение: считать документы, которые не удалось перенести; если такие есть —
не дропать колонку, залогировать/бросить исключение с `Id` документов.

### P1 (потенциальный). Данные в старой таблице шапки при «тихом» rename

`RenameDocumentTableAsync` (~строки 413–439 сид-сервиса): если метаданные уже равны
целевому имени — ранний `return`, физическое переименование даже не проверяется.
Если в такой базе осталась старая таблица `doc_fin_<hash>` **с данными**,
они окажутся брошенными (новая пустая шапка + FK на неё), переноса строк шапки нет.
В исследованной базе `bis_OsOO1` старой таблицы не было, но сценарий реален
для баз, где переименование метаданных произошло раньше/иначе.

Предложение: при `TableName == target` проверять наличие таблиц вида
`doc_fin_%` и либо переносить строки шапки (`INSERT ... SELECT`), либо явно
фиксировать находку в логе/отчёте.

### P2. SQL-инъекция через имя таблицы из метаданных

`RenameDocumentTableAsync` подставляет `oldTable`/`targetTableName` в DO-блок
(`to_regclass('public.{oldTable}')`, `ALTER TABLE ""{oldTable}""`) без экранирования.
Имена приходят из метаданных (редактируются через конфигуратор) — кавычка
в имени ломает запрос или хуже. `QuoteIdentifier` в `AdvancePaymentService`
экранирует корректно — использовать тот же подход и здесь.

### P2. Повторное сохранение из диалога дублирует шапку

`FinanceDocumentDialog.OnSaveClick` (~строки 579–620): порядок
«шапка → строки → проводка» не обёрнут в транзакцию. Если строки/проводка упадут
на **новом** документе, шапка уже создана; диалог остаётся открыт, повторный
«Сохранить» снова вызовет `CreateDynamicRecordAsync` (т.к. `_editId == null`) —
документ-дубликат.

Предложение: после успешного создания присваивать `_editId = savedId`,
чтобы повтор шёл через `UpdateDynamicRecordAsync`.

### P2. FK на шапку добавляется один раз и только при наличии шапки

`AdvancePaymentService.EnsureSchemaAsync()` (~строки 91–120): если шапки не было
в момент вызова, таблица строк создаётся без FK и позже он сам не появится.
После фикса сидинга порядок закрывает оба известных документа, но паттерн хрупкий
для будущих переименований. Рассмотреть вызов `EnsureDocumentTableAsync`-аналога
внутри `EnsureSchemaAsync` или явный `EnsureForeignKeyAsync`.

### P3. Замечания

- Имя FK-констрейнта `FK_<lines>_advance` проверяется глобально по `pg_constraint`
  без схемы (`conname = ...`): теоретический конфликт имён между схемами; лучше
  фильтровать по `connamespace`/таблице.
- `SaveLinesAsync` — `DELETE + INSERT` без проверки конкурентных правок:
  два открытых диалога одного документа — побеждает последний молча.
  Для desktop-сценария приемлемо, но стоит знать.
- Сумма шапки («Сумма», «Принято к учету») заполняется из грида в диалоге,
  серверной сверки `SUM(строки) == шапка.Сумма` перед проводкой нет.
  Проводки при этом строятся по строкам, расхождение уйдёт только в шапку.
- `ColumnExistsAsync` открывает/закрывает соединение вручную через
  `OpenConnectionAsync/CloseConnectionAsync` — безопасно для EF (счётчик),
  но хрупко при рефакторинге.
- Порядок удаления мета-поля `expense_lines` (сидинг, строки 397–410) корректен:
  поле удаляется из метаданных до миграции, а миграция читает физическую
  колонку напрямую; при падении миграции повтор запуска идемпотентен
  (`SaveLinesAsync` делает `DELETE + INSERT` по документу) — кроме случая P1 выше.
