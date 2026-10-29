# Пары счетов и uuid: `22P02` при сохранении, контракт записи и миграция ссылочных колонок

Документ фиксирует разбор ошибки «неверный синтаксис для типа uuid» при сохранении
пары счетов у партнёров, все изменения в коде и проверки.

Дата: 29.10.2026. Версия на момент работ: `1.1.76x`.

Связанные документы:

- `Docs/Задержки_запуска_и_оптимизация.md` — старт на удалённой базе (включая
  оптимизации посева пар и шага миграции, п. 4.10);
- `Docs/Модель_данных_как_сделать_правильно_по_шагам.md`, п. 3.3 — стратегический
  переход ссылок `text → uuid`.

---

## 1. Симптом

В логах базы партнёров:

```
2026-10-29T04:39:54.1945355Z	User	Update	Catalog	План счетов	df3055c0-…	{"Number":null}
2026-10-29T04:40:29.4739538Z	User	Update	Catalog	План счетов	533ab5ec-…	{"Number":null}
2026-10-29T04:40:59.4364655Z	User	Update	Catalog	План счетов	df3055c0-…	{"Number":null}

2026-10-29 10:41:12.061	Error	User	MessageBox	Ошибка: Ошибка обновления: 22P02: неверный синтаксис для типа uuid: "14160000"  POSITION: 380
2026-10-29 10:43:18.530	Error	User	MessageBox	Ошибка: Ошибка обновления: 22P02: неверный синтаксис для типа uuid: "18200000"  POSITION: 372
```

На базе разработчика те же действия работают.

---

## 2. Диагноз

### 2.1. Две генерации схемы

Приложение создаёт динамические таблицы по метаданным, и для `FieldType = "Reference"`
тип колонки менялся: раньше это был `text`, теперь `uuid`
(`MetadataService.GetSqlTypeForField`, `BisPatchService.GetSqlTypeForField`,
`MetadataSchemaRepairService.GetSqlType`).

Поэтому базы делятся на две генерации. Проверка на локальной машине:

| База | `catalog_advance_payments.debit_account` / `credit_account` | Данные |
|---|---|---|
| `bis_OsOO1_20260908_112439` (сентябрь) | `text` | коды: `18200000`, `14160000`… |
| `bis_OsOO2_20261019_130122` (октябрь) | `uuid` | Id: `c13e4424-…` |
| `bis_OsOO3_20261026_133202` (октябрь) | `uuid` | Id: `c13e4424-…` |

При этом **метаданные во всех трёх базах одинаковые**: `FieldType = Reference`,
`ReferenceCatalog = "План счетов"`. Разница только в физическом типе колонки:
таблица создавалась в разное время.

В старой базе таких «ссылок в text» — 241 колонка; в новой — только служебные
исключения (`catalog_cash_desks.code`, `catalog_organizations.group_code` и т. п.).

### 2.2. Что писала форма

Цепочка сохранения пары:

```
CatalogItemDialog.OnSaveClick
  → ReadFieldValue(field, control)                        Views/CatalogItemDialog.xaml.cs
  → AccountPickerControlFactory.GetSelectedAccountValue   Views/AccountPickerControlFactory.cs
  → AccountAnalyticsRules.GetAccountValueForField         Services/AccountAnalyticsHelper.cs
  → MetadataService.UpdateDynamicRecordAsync              Services/MetadataService.cs
  → FormatSqlValue(...)                                   Services/MetadataService.cs
```

Старый `GetAccountValueForField` возвращал **код** для любого поля с
`ReferenceCatalog = "План счетов"` — независимо от типа поля:

```csharp
public static object GetAccountValueForField(MetadataField field, AccountReferenceItem account)
{
    if (field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true)
        return account.Code;                       // ← код и для Reference, и для String

    …
    return account.Id.ToString();
}
```

`UpdateDynamicRecordAsync` подставляет значение в SQL как литерал
(`FormatSqlValue`): на text-колонке `'14160000'` — нормально, на uuid-колонке —
`22P02`.

### 2.3. Почему ошибка только у партнёров

- у разработчика база сентябрьская: колонки `text`, код в них помещается — всё работает;
- у партнёров база создана после перехода на uuid: `debit_account` — `uuid`,
  запись кода отклоняется;
- коды из лога — это дебет пар 10 (`14160000`) и 1 (`18200000`) из сида;
- текст «Ошибка обновления: …» формируется только в `CatalogDataView.OnEditClick`,
  то есть правили запись справочника, а не документ.

Дополнительно на uuid-базе ломалось и **добавление** пары: в
`AddCatalogItemCoreAsync` значение проходит через `DbValueConverter.Convert`,
который для `uuid` нераспознанный текст превращает в `DBNull` — пользователь
получал ложное «Заполните обязательные поля: Дебет, Кредит».

### 2.4. Воспроизведение

На копии новой базы тот же UPDATE, что строит `UpdateDynamicRecordAsync`:

```sql
UPDATE "catalog_advance_payments"
SET "module_code" = 'Финансы', "use_organizations" = TRUE, "debit_account" = '14160000', …
WHERE "Id" = '…';
-- ERROR: неверный синтаксис для типа uuid: "14160000"
```

Позиция в SQL (372/380) зависит от фактического порядка полей в конкретной базе;
разница 380 − 372 = 8 = разница длин `name` (25 vs 18) + `code` (2 vs 1) у пар 10 и 1,
то есть падало именно на `debit_account`.

---

## 3. Что сделано

### 3.1. Контракт записи: `Reference` → Id, `String` → код

`Services/AccountAnalyticsHelper.cs`:

```csharp
/// <summary>
/// Значение счёта для записи в поле. Ссылочное поле хранит Id (uuid-колонка),
/// строковое — код счёта: в проводках, кассах и счетах материала колонки
/// текстовые, и там исторически лежит код («14160000»).
/// </summary>
public static object GetAccountValueForField(MetadataField field, AccountReferenceItem account)
{
    if (string.Equals(field.FieldType, "Reference", StringComparison.OrdinalIgnoreCase))
        return account.Id.ToString();

    var normalizedName = NormalizeFieldName(field.Name);
    var normalizedColumn = NormalizeFieldName(field.DbColumnName);
    if (field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true ||
        normalizedName is "дебет" or "кредит" or "счет дебета" or "счет кредита" or
        "корр счет" or "коррсчет" or "счет кассы" or "счет учета" or
        "счет амортизации" or "затратный счет" or "новый затратный счет" ||
        normalizedColumn is "debit account" or "credit account" or "corr account" or
        "correspondent account" or "cash account" or "asset account" or
        "depreciation account" or "expense account" or "new expense account")
    {
        return account.Code;
    }

    return account.Id.ToString();
}
```

Это каноническая часть: диалог больше не кладёт код в uuid-колонку.

### 3.2. Приведение значения к фактическому типу колонки (страховка)

`Services/MetadataService.cs` — новый метод и вызовы в трёх точках записи
(`CreateDynamicRecordAsync`, `UpdateDynamicRecordAsync`, `AddCatalogItemCoreAsync`).

Метод читает типы колонок таблицы (`information_schema`) и для **uuid-колонок**
превращает код счёта в Id; текстовые колонки не трогает:

```csharp
private async Task NormalizeAccountReferenceValuesAsync(
    MetadataObject metadata,
    Dictionary<string, object> data,
    IReadOnlyDictionary<string, string>? columnTypes = null)
{
    if (data == null || string.IsNullOrWhiteSpace(metadata.TableName))
        return;

    var accountFields = metadata.Fields.Where(IsAccountReferenceField).ToList();
    if (accountFields.Count == 0)
        return;

    columnTypes ??= await LoadColumnTypesAsync(metadata.TableName);
    if (columnTypes.Count == 0)
        return;

    var pending = new List<(MetadataField Field, string Code)>();
    var codes = new List<string>();

    foreach (var field in accountFields)
    {
        if (!columnTypes.TryGetValue(field.DbColumnName, out var columnType) ||
            !string.Equals(columnType, "uuid", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!TryGetFieldValue(data, field, out var rawValue) || rawValue is null or DBNull)
            continue;

        if (rawValue is Guid)
            continue;

        var text = rawValue.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0 || Guid.TryParse(text, out _))
            continue;

        var code = NormalizeAccountCodeText(text);   // "14160000 - …" → "14160000"
        if (code.Length == 0)
            continue;

        pending.Add((field, code));
        if (!codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            codes.Add(code);
    }

    if (pending.Count == 0)
        return;

    var idsByCode = await LoadChartOfAccountIdsByCodeAsync(codes);

    foreach (var (field, code) in pending)
    {
        if (!idsByCode.TryGetValue(code, out var accountId))
        {
            throw new InvalidOperationException(
                $"Счет «{code}» не найден в плане счетов (поле «{field.Name}»). " +
                "Выберите счёт из плана счетов и сохраните запись повторно.");
        }

        data[field.Name] = accountId;
    }
}
```

Вспомогательные методы:

```csharp
private static bool IsAccountReferenceField(MetadataField field)
{
    if (string.IsNullOrWhiteSpace(field.DbColumnName))
        return false;

    if (!string.Equals(field.FieldType, "Reference", StringComparison.OrdinalIgnoreCase))
        return false;

    return field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true ||
           AccountAnalyticsRules.IsAccountSelectorField(field);
}

private static string NormalizeAccountCodeText(string text)
{
    var trimmed = (text ?? string.Empty).Trim();
    var separatorIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
    return separatorIndex > 0 ? trimmed[..separatorIndex].Trim() : trimmed;
}

private async Task<Dictionary<string, string>> LoadColumnTypesAsync(string tableName)
{
    var connection = _context.Database.GetDbConnection();
    var openedHere = connection.State != ConnectionState.Open;
    if (openedHere)
        await _context.Database.OpenConnectionAsync();

    try
    {
        return await DbValueConverter.LoadColumnTypesAsync(connection, tableName);
    }
    finally
    {
        if (openedHere)
            await _context.Database.CloseConnectionAsync();
    }
}
```

Места вызова:

```csharp
// CreateDynamicRecordAsync / UpdateDynamicRecordAsync
await EnsureLargeDynamicTextColumnsAsync(metadata);
await EnsureDocumentDateCanBeModifiedAsync(metadata, data);
ValidateChartOfAccountsCode(metadata, data);

// Счета-ссылки: код из диалога → uuid для новых баз (иначе 22P02).
await NormalizeAccountReferenceValuesAsync(metadata, data);

// AddCatalogItemCoreAsync — типы уже загружены для INSERT, передаём их
await NormalizeAccountReferenceValuesAsync(catalog, itemData, columnTypes);
```

### 3.3. Миграция `text → uuid` для ссылочных колонок

Новый файл `Services/ReferenceColumnMigrationService.cs` + повторяемый шаг
`2026.10.29.01` в `Services/SchemaMigrationService.cs`.

Проверенный список колонок:

```csharp
private static readonly (string Table, string Column)[] VerifiedTargets =
{
    ("catalog_advance_payments", "debit_account"),
    ("catalog_advance_payments", "credit_account"),
    ("catalog_advance_payments", "currency_id")
};
```

Список намеренно короткий: массовый перевод всех 241 text-ссылок ломает сырой SQL,
который передаёт в такие колонки строковые параметры (`doc_postings.organization_id`
и подобные). Список расширяется после ревизии запросов.

Алгоритм:

1. **Быстрый путь.** Прочитать только типы целевых колонок (1 запрос на таблицу).
   Если ни одна не `text` — выйти, не читая метаданные (шаг повторяемый, и на
   каждом старте полный проход метаданных недопустим).
2. Для text-колонки: `ALTER TABLE … ADD COLUMN "<col>__uuid_migr" uuid`.
3. `UPDATE` — значение переводится по фактическому содержимому: Guid остаётся Guid,
   код ищется по `code` справочника поля (план счетов, валюты).
4. Проверка: `count(*) WHERE value <> '' AND tmp IS NULL` — если есть неразобранные,
   транзакция откатывается, колонка остаётся как была, причина пишется в лог.
5. Проверка `NOT NULL`: если колонка была `NOT NULL`, пустых значений быть не должно.
6. `DROP COLUMN <col>` + `RENAME COLUMN <tmp> TO <col>`; при `NOT NULL` — вернуть его.

Ключевые фрагменты:

```csharp
// 1. Быстрый путь: только типы колонок
var columnTypesByTable = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
var requiresWork = false;

foreach (var tableName in VerifiedTargets.Select(item => item.Table)
             .Distinct(StringComparer.OrdinalIgnoreCase))
{
    var columnTypes = await LoadColumnTypesAsync(tableName);
    columnTypesByTable[tableName] = columnTypes;

    foreach (var (table, column) in VerifiedTargets)
    {
        if (!string.Equals(table, tableName, StringComparison.OrdinalIgnoreCase))
            continue;

        if (columnTypes.TryGetValue(column, out var dataType) && IsTextColumn(dataType))
            requiresWork = true;
    }
}

if (!requiresWork)
    return result;

// … только теперь читаем MetadataObjects + Fields
```

```csharp
// 3–4. Перенос значений и проверка
await ExecuteAsync($"ALTER TABLE {quotedTable} DROP COLUMN IF EXISTS {quotedTemp};");
await ExecuteAsync($"ALTER TABLE {quotedTable} ADD COLUMN {quotedTemp} uuid;");

var valueExpression = mapping == null
    ? $"CASE WHEN COALESCE(btrim({quotedColumn}::text), '') = '' THEN NULL " +
      $"ELSE btrim({quotedColumn}::text)::uuid END"
    : $@"CASE
            WHEN COALESCE(btrim({quotedColumn}::text), '') = '' THEN NULL
            WHEN btrim({quotedColumn}::text) ~ @guidPattern THEN btrim({quotedColumn}::text)::uuid
            ELSE (
                SELECT target.{Quote(mapping.IdColumn)}
                FROM {Quote(mapping.TableName)} target
                WHERE COALESCE(btrim(target.{Quote(mapping.CodeColumn)}::text), '') =
                      btrim({quotedColumn}::text)
                LIMIT 1
            )
        END";

await ExecuteAsync(
    $"UPDATE {quotedTable} SET {quotedTemp} = {valueExpression};",
    ("@guidPattern", GuidPattern));

var unresolved = await ScalarLongAsync(
    $"SELECT count(*) FROM {quotedTable} " +
    $"WHERE COALESCE(btrim({quotedColumn}::text), '') <> '' AND {quotedTemp} IS NULL;");
if (unresolved > 0)
{
    await transaction.RollbackAsync();
    result.Problems.Add(
        $"{tableName}.{columnName}: не удалось разобрать {unresolved} знач. — колонка оставлена как была");
    return;
}
```

Почему DDL/DML идёт через свой `ExecuteAsync`, а не `ExecuteSqlRawAsync`:
в SQL есть регулярное выражение `'^[0-9a-f]{8}-…'`, а EF-овский `ExecuteSqlRaw`
с параметрами прогоняет строку через `string.Format` и падает на фигурных скобках
(«Index (zero based)…»). Свой метод на `DbCommand` этого не делает и попадает
в текущую транзакцию.

Шаг в журнале миграций:

```csharp
new("2026.10.29.01",
    "Ссылочные колонки пар счетов: text → uuid (код счёта переносится в Id)",
    true,
    EnsureAccountPairsReferenceTypesAsync)
```

```csharp
private static async Task EnsureAccountPairsReferenceTypesAsync(AppDbContext ctx)
{
    var result = await new ReferenceColumnMigrationService(ctx).ConvertAsync();
    foreach (var item in result.Converted)
        SystemLogService.Info($"Пары счетов: {item} → uuid.", "Schema");

    foreach (var problem in result.Problems)
        SystemLogService.Warning($"Пары счетов: {problem}", "Schema");
}
```

### 3.4. Нормализация пар при старте — по фактическому типу колонки

`Services/MetadataService.Finance.cs`, `NormalizeAdvancePaymentRowsAsync`.

Старый SQL сравнивал `currency_id` с пустой строкой (`NULLIF("currency_id", '')`).
На uuid-базе это `22P02: неверный синтаксис для типа uuid: ""`, ошибка глоталась
в `Debug`, и нормализация не выполнялась **ни разу**.

Теперь SQL собирается по типу колонки:

```csharp
var columnTypes = await LoadColumnTypesAsync(catalog.TableName);
var currencyIsUuid = columnTypes.TryGetValue("currency_id", out var currencyType) &&
                     string.Equals(currencyType, "uuid", StringComparison.OrdinalIgnoreCase);

var currencyAssignment = currencyIsUuid
    ? @"""currency_id"" = CASE
            WHEN ""currency_id"" IS NOT NULL THEN ""currency_id""
            WHEN NOT COALESCE(""use_currency"", false) THEN NULL
            ELSE COALESCE(
                (SELECT c.""Id"" FROM ""catalog_currencies"" c
                 WHERE COALESCE(c.""is_base"", false) = true ORDER BY c.""code"" LIMIT 1),
                (SELECT c.""Id"" FROM ""catalog_currencies"" c WHERE UPPER(c.""code"") = 'KGS' LIMIT 1)
            )
        END"
    : @"""currency_id"" = CASE
            WHEN COALESCE(NULLIF(""currency_id"", ''), '') <> '' THEN ""currency_id""
            WHEN NOT COALESCE(""use_currency"", false) THEN NULL
            ELSE COALESCE(
                (SELECT c.""Id""::text FROM ""catalog_currencies"" c
                 WHERE COALESCE(c.""is_base"", false) = true ORDER BY c.""code"" LIMIT 1),
                (SELECT c.""Id""::text FROM ""catalog_currencies"" c WHERE UPPER(c.""code"") = 'KGS' LIMIT 1)
            )
        END";
```

Ошибка теперь видна в логе, а не только в `Debug`.

### 3.5. Подсветка скорректированной записи

В «Парах счетов» после сохранения список перезагружается (MDI повторно
прикрепляет вкладку-вызыватель), и выделение слетало. Изменения:

`Views/CatalogDataView.xaml.cs`:

```csharp
/// <summary>
/// Запись, которую только что добавили или изменили. Нужна, чтобы остаться
/// на ней после перезагрузки списка: MDI заново прикрепляет вкладку-вызыватель,
/// её Loaded повторно зовёт LoadData() — и без этого выделение слетало.
/// </summary>
private Guid? _recordToKeepSelected;
```

```csharp
private void RestorePendingSelection()
{
    if (_dataTable == null || _recordToKeepSelected is not { } target || target == Guid.Empty)
        return;

    DataRowView? match = null;
    foreach (DataRowView candidate in _dataTable.DefaultView)   // уже с учётом поиска
    {
        if (!candidate.Row.Table.Columns.Contains("Id"))
            continue;

        if (TryReadGuid(candidate["Id"], out var id) && id == target)
        {
            match = candidate;
            break;
        }
    }

    if (match == null)
        return;

    DataGrid.SelectedItem = match;
    DataGrid.ScrollIntoView(match);
    EditButton.IsEnabled = true;
    DeleteButton.IsEnabled = true;
}
```

Вызывается в конце `LoadDataCore()` (после `ApplySearchFilter` и
`RestoreOrganizationSelection`), заполняется в `OnAddClick`/`OnEditClick`,
сбрасывается в `DataGrid_SelectionChanged`, когда пользователь сам выбрал другую
строку, и при удалении.

`Views/DataGridSelectionHelper.cs` — удержание выделения:

```csharp
// Вкладка MDI на время диалога прячется, а после закрытия показывается снова
// и заново грузит данные. Пока таблица невидима, выделять строку бессмысленно,
// но и бросать попытки нельзя: дождёмся появления.
void OnGridVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
{
    if (!grid.IsVisible)
        return;

    grid.IsVisibleChanged -= OnGridVisibilityChanged;
    grid.Unloaded -= OnGridUnloaded;
    KeepRowSelected(grid, findTargetRow, isTargetRow);
}
```

```csharp
// Список перезагрузили (ItemsSource заменили) — отсчёт стабильности начинается
// заново, иначе удержание прекращалось раньше, чем приходили новые строки.
itemsChanged = (_, _) => stableTicks = 0;
observableItems.CollectionChanged += itemsChanged;
```

### 3.6. Тест-сценарий

`BIS.ERP.TESTS/BIS.ERP.TESTS.Core/PairsReferenceMigrationScenario.cs`,
код `schema-pairs-uuid`.

Что проверяет на базе из `testsettings.json`:

- типы `debit_account/credit_account/currency_id` после миграции — `uuid`;
- коды счетов превратились в Id по `catalog_plan_schetov` (сверка по коду);
- Guid остался Guid, пустое значение — `NULL`;
- число строк не изменилось;
- повторный запуск ничего не переводит (идемпотентность);
- при неразобранном коде колонка остаётся `text` (проверка отката).

Запуск:

```powershell
dotnet run --project BIS.ERP.TESTS\BIS.ERP.TESTS.Console -- schema-pairs-uuid verify
```

Сценарий работает **только** с базой из `testsettings.json`, чтобы не пройтись
миграцией по всем зарегистрированным информационным базам.

---

## 4. Проверка

| Проверка | Результат |
|---|---|
| Сборка `BIS.ERP.csproj` | 0 ошибок (`1.1.76x`) |
| Копия старой базы `bis_OsOO1` (text, коды), 10 пар | `debit/credit/currency → uuid`, значения = Id счетов, 10 строк, повторный прогон — no-op |
| Копия с битым кодом `99999999` | `debit_account` остался `text`, данные целы, в отчёте «не удалось разобрать 1 знач. — колонка оставлена как была» |
| Копия с пустым справочником пар | созданы 10 пар, колонки `uuid`, счета совпадают по кодам (`18200000…14160000`), `currency_id` пустой как в сиде |
| `ui-grid-selection` | выделение после повторной загрузки, фокус, пользовательский выбор — ок |
| Повторное сохранение пары на uuid-базе | код больше не пишется в uuid-колонку (контракт отдаёт Id) |

---

## 5. Что осталось

1. **Расширять `VerifiedTargets`.** Следующие кандидаты — ссылочные колонки
   справочников ОС, «Расчет курсовой разницы», «Настройка доступа к счетам».
   Перед добавлением — ревизия запросов, которые пишут в колонку строковыми
   параметрами.
2. **После перевода всех баз** нормализацию на записи (п. 3.2) можно убрать или
   оставить как страховку для баз, восстановленных из старых бэкапов.
3. **Полный переход 3.3** (все 241 колонка) — отдельная работа с проверкой по
   каждой колонке и откатом; список в `ReferenceColumnMigrationService` — её
   пошаговый вариант.

---

## 6. Файлы

| Файл | Что изменено |
|---|---|
| `Services/AccountAnalyticsHelper.cs` | `GetAccountValueForField`: `Reference` → Id, `String` → код |
| `Services/MetadataService.cs` | `NormalizeAccountReferenceValuesAsync` + `IsAccountReferenceField`, `NormalizeAccountCodeText`, `LoadColumnTypesAsync`; вызовы в `CreateDynamicRecordAsync`, `UpdateDynamicRecordAsync`, `AddCatalogItemCoreAsync`; `using System.Data` |
| `Services/MetadataService.Finance.cs` | `NormalizeAdvancePaymentRowsAsync` — SQL по фактическому типу колонки; `AddAdvancePaymentDataToTable` — быстрый выход по существующим кодам; `LoadAccountPairCodesAsync` |
| `Services/ReferenceColumnMigrationService.cs` | **новый**: `text → uuid` с переносом кодов в Id, транзакция, проверки, откат, быстрый путь |
| `Services/SchemaMigrationService.cs` | шаг `2026.10.29.01` + `EnsureAccountPairsReferenceTypesAsync` |
| `Views/CatalogDataView.xaml.cs` | `_recordToKeepSelected`, `RestorePendingSelection`, сброс при выборе другой строки |
| `Views/DataGridSelectionHelper.cs` | ожидание появления грида, сброс стабильности при перезагрузке списка |
| `BIS.ERP.TESTS/BIS.ERP.TESTS.Core/PairsReferenceMigrationScenario.cs` | **новый** сценарий `schema-pairs-uuid` |
| `version.json` | обновляется сборкой (не ручная правка) |
