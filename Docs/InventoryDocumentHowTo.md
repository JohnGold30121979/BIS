# Как создать новый системный документ ТМЗ (пошаговая инструкция)

Документ описывает полный цикл добавления нового документа товарно-материальных запасов
(ТМЗ/ТМЦ) в систему: от описания модели до появления окна в меню, сохранения шапки и строк.

Ориентир — три уже работающих документа: **Поступление ТМЗ**, **Выбытие ТМЗ**,
**Внутреннее перемещение ТМЗ**. Новый документ делается по той же схеме.

---

## 0. Что из чего состоит

| Слой | Файл | Роль |
|---|---|---|
| Модель документов | `Services/InventoryDocumentModel.cs` | Список документов, видов операций, состав полей шапки и колонок строк |
| Схема БД и метаданные | `Services/InventoryDocumentSchemaService.cs` | Создаёт `MetadataObject` документа, таблицы шапки и строк, привязывает к модулю |
| Диалог документа | `Views/Dialogs/InventoryDocumentDialog.xaml` / `.xaml.cs` | Форма ввода шапки и строк, сохранение |
| Строки | `Services/InventoryDocumentLineService.cs` | `InventoryLineRow`, чтение/запись строк, расчёт итогов |
| Окно списка документов | `Views/DynamicDocumentWorkView.xaml` / `.xaml.cs` | Грид документов + нижний грид «Строки проводки» |
| Меню / навигация | `Views/MainWorkWindow.xaml.cs` | Открывает `DynamicDocumentWorkView` для документов типа `Document` |
| Миграции/старт | `Services/SchemaMigrationService.cs`, `Services/DocumentationMetadataSeedService.cs` | Вызывают `InventoryDocumentSchemaService.EnsureAsync()` при обновлении базы |

Поток данных:
`MainWorkWindow` → `DynamicDocumentWorkView` (список) → `InventoryDocumentDialog` (ввод) →
`MetadataService.CreateDynamicRecordAsync/UpdateDynamicRecordAsync` (шапка) →
`InventoryDocumentLineService.ReplaceLinesAsync` (строки в отдельной таблице).

---

## 1. Порядок работ (не переставлять)

1. Описать документ в `InventoryDocumentModel.Documents`.
2. Описать его виды операций в `InventoryDocumentModel.Kinds`.
3. Прогнать `InventoryDocumentSchemaService.EnsureAsync()` (миграция/старт) — появятся метаданные,
   таблица шапки, таблица строк и привязка к модулю.
4. Проверить обязательные справочники (материалы, план счетов, участки, МОЛ, валюты, налоги).
5. При необходимости дополнить диалог (`InventoryDocumentDialog`) — только если у документа
   особые реквизиты.
6. Проверить окно списка (`DynamicDocumentWorkView`) — обычно правок не требует.
7. Пройти чек-лист (раздел 9).

---

## 2. Шаг 1. Описать документ в модели

Файл: `Services/InventoryDocumentModel.cs`, массив `Documents`.

Куда вставлять: после третьего элемента (`MovementName`), перед закрывающей `};`.

```csharp
public static IReadOnlyList<DocumentDefinition> Documents { get; } = new[]
{
    new DocumentDefinition(IncomingName, IncomingTable, IncomingLinesTable, "incoming_id", "📥", 1, "...", new[] { "Приход товаров" }),
    new DocumentDefinition(OutgoingName, OutgoingTable, OutgoingLinesTable, "outgoing_id", "📤", 2, "...", new[] { "Расход товаров" }),
    new DocumentDefinition(MovementName, MovementTable, MovementLinesTable, "movement_id", "🔁", 3, "...", new[] { "Внутреннее перемещение ТМЦ" }),

    // НОВЫЙ ДОКУМЕНТ
    new DocumentDefinition(
        "Списание ТМЗ",                  // Name — имя документа (видно в меню и заголовке окна)
        "doc_inventory_writeoff",        // Table — таблица шапки
        "doc_inventory_writeoff_lines",  // LinesTable — таблица строк
        "writeoff_id",                   // ParentColumn — колонка-родитель в строках
        "🗑️",                            // Icon
        4,                               // Order — порядок в списке
        "Списание товарно-материальных запасов по акту",
        new[] { "Списание ТМЦ" })        // LegacyNames — прежние имена для переименования
};
```

Что произойдёт автоматически:

- диалог `InventoryDocumentDialog` найдёт определение по имени документа
  (`_definition = Documents.FirstOrDefault(...)`), возьмёт из него таблицы для сохранения строк;
- окно списка `DynamicDocumentWorkView` начнёт считать документ «своим» (`IsInventoryDocument`)
  и включит нижний грид строк;
- `InventoryDocumentSchemaService` создаст метаданные и таблицы.

Константы имён и таблиц держите рядом с остальными (`IncomingTable`, `OutgoingTable`, …) —
так проще искать по коду.

---

## 3. Шаг 2. Описать виды операций

Файл: `Services/InventoryDocumentModel.cs`, массив `Kinds`.

`MovementKind` — ключевая вещь: по нему диалог фильтрует список «Вид операции»
(`MovementKindForDocument`). Допустимые значения: `Incoming`, `Outgoing`, `Movement`.

```csharp
public static IReadOnlyList<KindDefinition> Kinds { get; } = new[]
{
    // ... существующие восемь ...

    // НОВЫЕ ВИДЫ ОПЕРАЦИЙ ДЛЯ НОВОГО ДОКУМЕНТА
    new KindDefinition("WRITEOFF_ACT", "Списание по акту", "Списание ТМЗ", "Outgoing", 9,
        "Списание недостачи/порчи по акту"),
    new KindDefinition("WRITEOFF_DAMAGE", "Списание порчи", "Списание ТМЗ", "Outgoing", 10,
        "Списание испорченных ТМЗ")
};
```

`OperationKind` (последний параметр) — код, который пишется в шапку в колонку `operation_kind`.
Если не указан, берётся `Code`.

Виды операций живут в справочнике **«Вид операции ТМЗ»** (`catalog_inventory_document_entries`,
поля `code`, `name`, `document_name`, `movement_kind`, `operation_kind`, `sort_order`,
`description`, `is_active`). Заполняет его `InventoryDocumentSchemaService.SeedKindsAsync`
при прогоне схемы — вручную в БД вставлять не нужно.

---

## 4. Шаг 3. Схема БД и метаданные

Файл: `Services/InventoryDocumentSchemaService.cs`. Метод `EnsureAsync()` делает всё сразу
и идемпотентно:

1. `EnsureEntryCatalogAsync` — справочник видов операций (создание/переименование из
   `Ввод нового документа ТМЗ`, поля, `operation_kind`).
2. `SeedKindsAsync` — восемь (теперь больше) видов операций из `InventoryDocumentModel.Kinds`.
3. `EnsureDocumentAsync(definition)` для каждого документа:
   - ищет `MetadataObject` с `ObjectType = "Document"` и нужным именем;
   - если нет — переименовывает legacy-документ (`LegacyNames`);
   - если всё равно нет — создаёт `MetadataObject` с полями из `InventoryDocumentModel.HeaderFields(...)`,
     флагом `UsePostings = true`, и вызывает `_metadataService.CreateDynamicTableAsync(document)`;
   - если таблица пропала — создаёт заново (`TableExistsAsync`);
   - `EnsureHeaderColumnsAsync` — добавляет недостающие колонки шапки (удобно, когда добавили новое поле).
4. `EnsureLinesTablesAsync` — таблицы строк по `InventoryDocumentModel.LineColumns`.
5. `EnsureModuleAssignmentsAsync` — привязывает к модулю только документы, созданные этим прогоном.
6. `RemoveObsoleteDocumentsAsync` — удаляет документы из `ObsoleteDocumentNames`.

Где вызывается:

| Место | Когда |
|---|---|
| `Services/SchemaMigrationService.cs` (шаги вокруг строк 382, 396, 420) | при обновлении базы |
| `Services/DocumentationMetadataSeedService.cs` (строка ~422) | при создании/наполнении базы |

Если добавляете новый документ — **ничего дополнительно вызывать не нужно**: цикл по
`Documents` подхватит его сам.

### Поля шапки

Файл: `Services/InventoryDocumentModel.cs`, метод `HeaderFields(Guid metadataObjectId)`.

Состав общий для всех документов ТМЗ (номер, дата, вид операции, счёт, организация, сотрудник,
вид покупки, налоги, валюта, курс, итоги, примечание, участок/МОЛ-получатель, счета дебета/кредита,
`is_posted`). Новое поле добавляется так:

```csharp
Field(metadataObjectId, "Склад-источник", "source_site_id", "Reference", 33,
    reference: "Участки", displayPattern: "{Код} - {Наименование}", displayFields: "Код,Наименование"),
```

После этого — прогнать `EnsureAsync()`: `EnsureHeaderColumnsAsync` добавит колонку в существующую
таблицу, метаданные получат новое поле, диалог покажет его автоматически (шапка строится по
`_document.Fields`).

### Колонки строк

Файл: `Services/InventoryDocumentModel.cs`, `LineColumns`:

```csharp
("line_number", "integer NOT NULL DEFAULT 0"),
("material_id", "uuid NULL"),
("material_name", "varchar(500) NULL"),
("unit_name", "varchar(50) NULL"),
("account_code", "varchar(50) NULL"),      // «Дебет»
("site_id", "uuid NULL"),                  // «Участок»
("responsible_person_id", "uuid NULL"),    // МОЛ
("quantity", "numeric(18,3) NOT NULL DEFAULT 0"),
("price", "numeric(18,6) NOT NULL DEFAULT 0"),
("amount_without_tax", "numeric(18,2) NOT NULL DEFAULT 0"),
("vat_rate", "numeric(8,2) NOT NULL DEFAULT 0"),
("vat_amount", "numeric(18,2) NOT NULL DEFAULT 0"),
("sales_tax_rate", "numeric(8,2) NOT NULL DEFAULT 0"),
("sales_tax_amount", "numeric(18,2) NOT NULL DEFAULT 0"),
("line_total", "numeric(18,2) NOT NULL DEFAULT 0"),
("amount_currency", "numeric(18,2) NOT NULL DEFAULT 0"),
("description", "varchar(500) NULL")
```

Добавили строку — не забудьте про INSERT в `InventoryDocumentLineService.ReplaceLinesAsync`
(там перечислены имена колонок и параметры; иначе значение не сохранится).

---

## 5. Шаг 4. Диалог документа

Файл: `Views/Dialogs/InventoryDocumentDialog.xaml.cs`.

Инициализация (`InitializeAsync`):

1. загрузка справочников `LoadRowsAsync(catalogs, "...")` — принимает несколько имён;
2. `BuildTopRow` — номер, дата, вид операции (только «свои» `movement_kind`), пачка, ЭСФ;
3. `BuildSupplierCard` — «Поставщик»: счёт, организация, сотрудник, вид покупки; для перемещения
   добавляются «Участок-получатель» и «МОЛ-получатель»;
4. `BuildTaxCard` — налоги и оплата, по контролу в строке;
5. `BuildCurrencyCard` — блок «Валюта» (валюта из счёта, курс из справочника, «?» и «Текущий курс»);
6. `ApplyExistingValues` — значения существующей записи;
7. `LoadLinesAsync` → строки в `_lines` (`ObservableCollection<InventoryLineRow>`).

Что менять при новом документе:

- **Ничего**, если у него тот же набор реквизитов. Диалог найдёт определение в
  `InventoryDocumentModel.Documents`.
- Если нужны особые поля в карточке «Поставщик» (как у перемещения) — добавьте условие:

```csharp
if (string.Equals(_definition.Name, "Списание ТМЗ", StringComparison.OrdinalIgnoreCase))
{
    AddReference(SupplierPanel, "Участок-источник", "source_site_id", sites);
}
```

- Если нужен свой блок — по образцу `BuildCurrencyCard`: `GroupBox` в XAML + метод, который
  создаёт контролы и складывает их в `_editors[dbColumnName]` (по этому словарю идёт сохранение).

Сохранение (`OnSaveClick` → `BuildItemData`):

1. пустые строки (созданные Enter) выбрасываются;
2. проверка: у каждой строки есть материал и количество > 0;
3. шапка: `CreateDynamicRecordAsync` / `UpdateDynamicRecordAsync`;
4. строки: `ReplaceLinesAsync(_definition.LinesTable, _definition.ParentColumn, SavedRecordId, _lines)`;
5. закрытие: `MdiDialogService.CloseWithResult(this, true)` — **не** `DialogResult` (в MDI окно
   немодальное, `DialogResult` бросает исключение).

---

## 6. Шаг 5. Строки документа

Файл: `Services/InventoryDocumentLineService.cs`.

- Модель строки — `InventoryLineRow` (`INotifyPropertyChanged`): `LineNumber`, `MaterialId`,
  `MaterialName`, `MaterialCode`, `UnitName`, `AccountCode`, `AccountText`, `SiteId`, `SiteText`,
  `ResponsiblePersonId`, `Quantity`, `Price`, `AmountWithoutTax`, `VatRate`, `VatAmount`,
  `SalesTaxRate`, `SalesTaxAmount`, `LineTotal`, `AmountCurrency`, `Description`.
- `GetLinesAsync(linesTable, parentColumn, headerId)` — выборка по `line_number`.
- `ReplaceLinesAsync(...)` — DELETE + INSERT в одной транзакции, перенумерация с 1.
- `RecalculateTotals(...)` — итоги по строкам.

Правила пересчёта (`InventoryLineRow.Recalculate`):

```
AmountWithoutTax = round(Quantity × Price, 2)
VatAmount       = round(AmountWithoutTax × VatRate / 100, 2)
SalesTaxAmount  = round(AmountWithoutTax × SalesTaxRate / 100, 2)
LineTotal       = round(AmountWithoutTax + VatAmount + SalesTaxAmount, 2)
```

В диалоге строки подписаны на `PropertyChanged`: изменение `Quantity`, `Price`, `VatRate`,
`SalesTaxRate` сразу вызывает `Recalculate()` и `UpdateTotals()`.

---

## 7. Шаг 6. Окно списка документов

Файл: `Views/DynamicDocumentWorkView.xaml` / `.xaml.cs`.

Открывается из меню (`Views/MainWorkWindow.xaml.cs`, `case "DynamicDocument"`):

```csharp
var documentContext = await _infoBaseManager.GetCurrentDbContextAsync();
var dynamicView = new DynamicDocumentWorkView(document, _metadataService, documentContext);
_navigation.NavigateTo(dynamicView, item.Name, $"document:{item.Id}");
```

Что уже есть для документов ТМЗ:

- основной грид — все записи документа (колонки строятся по `_documentMetadata.Fields`);
  ссылочные поля показываются текстом (`referenceCatalogs`), «Вид операции» — наименованием
  из `catalog_inventory_document_entries` (`LoadEntryNamesAsync`);
- нижний блок «Строки проводки» (`PostingLinesGrid`) — строки выбранного документа
  (`LoadPostingLinesAsync` → `GetLinesAsync`), колонки №, Код, Материал, Ед., Дебет, Участок,
  %, Кол-во, Цена, Без НДС, НДС; наименования участков — через `EnsureSiteNamesAsync`;
- кнопки: Добавить (диалог), Редактировать, Удалить, Провести, Обновить.

Для нового документа правок в этом окне не требуется — достаточно `InventoryDocumentModel.Documents`.

---

## 8. Шаг 7. Нумерация и проведение

**Нумерация.** Для новой записи диалог вызывает
`_metadataService.GetNextDocumentNumberAsync(_document)`: сервис создаёт таблицу `doc_numbering`,
сканирует существующие документы типа и возвращает следующий номер; счётчик только растёт.

**Проведение.** Кнопка «Провести» в окне списка вызывает `MetadataService.PostDocumentAsync(documentId, recordId)`.
Для документов ТМЗ в `PostDocumentAsync` есть отдельная ветка `IsInventoryDocumentName(...)` →
`ProcessInventoryDocumentAsync(...)`, которая пишет проводки в `doc_postings`:

| Что | Значение |
|---|---|
| Количество строк | по одной проводке на строку документа (`InventoryDocumentLineService.GetLinesAsync`) |
| Сумма (`amount_kgs`) | `line_total`, а если он 0 — `amount_without_tax` |
| Поступление (`Incoming`) | Дт = счёт строки («Дебет», `account_code`), Кт = счёт шапки («Счет», `counterparty_account`) |
| Выбытие / перемещение (`Outgoing` / `Movement`) | Дт = счёт шапки, Кт = счёт строки |
| `amount_currency` | сумма строки / курс шапки (`exchange_rate`), при курсе 0 — 0 |
| `document_type` | `document.Name` (например, «Поступление ТМЗ») |
| `doc_number`, `posting_date` | номер и дата документа |
| `document_table`, `document_id` | таблица и `Id` шапки — для точного удаления |
| `organization_id`, `currency_id`, `description` | из шапки / «Документ: материал» |

Правила: строка без счёта, с нулевой суммой или с одинаковыми Дт/Кт — пропускается;
если не создано ни одной проводки — `InvalidOperationException` с подсказкой (заполните «Счет»
в шапке и «Дебет» в строках), транзакция откатывается. После вставки шапка получает
`is_posted = true`. Повторное проведение запрещено — «Документ уже проведён!».

**Удаление — сначала проводки, потом документ.** В `DeleteDynamicRecordAsync` порядок такой:

1. `DeleteInventoryPostingsAsync(...)` — `DELETE FROM doc_postings WHERE document_table = @table AND document_id = @id`;
2. `DeleteDocumentPostingsAsync(...)` — контрольное удаление по `doc_number` + `document_type`;
3. только после этого — `DELETE FROM <шапка> WHERE Id = ...`.

Так в `doc_postings` не остаётся висящих строк, даже если у документа не было номера.

Пока `is_posted = true`, редактирование, удаление и повторное проведение заблокированы
(`UpdateButtonsState` в `DynamicDocumentWorkView`).

---

## 9. Чек-лист проверки нового документа

1. **Метаданные**: в конфигураторе появился документ с нужным именем, `ObjectType = Document`,
   `UsePostings = true`.
2. **Таблицы**: существуют `<шапка>` и `<шапка>_lines`, колонки совпадают с `HeaderFields`/`LineColumns`.
3. **Справочник видов операций**: в «Вид операции ТМЗ» есть записи нового документа, `movement_kind`
   совпадает с ожидаемым.
4. **Меню**: документ виден в навигации (привязка к модулю в `EnsureModuleAssignmentsAsync`).
5. **Диалог**: «Добавить» открывает форму, вид операции фильтруется, справочники не пустые.
6. **Валюта**: при валютном счёте появляется блок «Валюта», «?» открывает курсы, «Текущий курс»
   запрашивает НБКР.
7. **Строки**: «?» по материалу/дебету/участку открывает справочник вкладкой MDI; Enter идёт
   по колонкам и создаёт новую строку в конце; суммы пересчитываются при вводе.
8. **Сохранение**: шапка попадает в `<шапка>`, строки — в `<шапка>_lines` (`line_number` = 1,2,3…),
   окно закрывается (`MdiDialogService.CloseWithResult`).
9. **Проведение**: «Провести» создаёт строки в `doc_postings` (по одной на строку документа,
   `document_type` = имя документа, `document_id`/`document_table` заполнены), `is_posted = true`.
10. **Удаление**: после удаления документа в `doc_postings` не остаётся записей с его `document_id`
   или `doc_number`.
11. **Окно списка**: выбранный документ показывает строки в нижнем гриде, «Вид операции» — наименование.

---

## 10. Типичные ошибки (проверено на практике)

| Симптом | Причина | Решение |
|---|---|---|
| Окно выбора справочника не открывается | пустой список строк для окна | проверить имя справочника; окно должно открываться и при пустом справочнике (включён `ConfigureCatalogEditing`) |
| В ячейках «Участок» пусто | в справочнике «Участки» поля `site_code` / `site_name`, а читалось «Наименование» | использовать `SiteName()` / `SiteCode()` |
| «?» в гриде не срабатывает с первого клика | DataGrid тратит клик на выделение строки | обработка `PreviewMouseDown`/`PreviewMouseUp` с `handledEventsToo` |
| `DialogResult можно задать только после…` | окно открыто немодально (MDI) | `MdiDialogService.CloseWithResult(this, …)` |
| Не пересчитываются суммы при вводе | обновление только по `CellEditEnding` | подписка на `PropertyChanged` + `UpdateSourceTrigger=PropertyChanged` |
| Строка залита синим | системный стиль `DataGridRow/Cell` | `RowStyle`/`CellStyle` с фоном из темы, выделение — акцентной рамкой |
| «Вид операции» показывает GUID | не подставлено наименование | `LoadEntryNamesAsync` → `name` из `catalog_inventory_document_entries` |
| После сохранения ошибка про пустую строку | Enter создал пустую строку | пустые строки отбрасываются в начале `OnSaveClick` |
| «Неизвестный тип документа» при проведении | для документа не было ветки в `PostDocumentAsync` | добавлена `IsInventoryDocumentName(...)` → `ProcessInventoryDocumentAsync` |
| «Не удалось сформировать проводки» | не заполнен «Счет» в шапке или «Дебет» в строках, либо нулевые суммы | заполнить счёт шапки и счета строк |
| После удаления документа остались проводки | удаление шло только по номеру | в `DeleteDynamicRecordAsync` вызывается `DeleteInventoryPostingsAsync` по `document_table` + `document_id` |

---

## 11. Пример целиком: документ «Списание ТМЗ»

1. `Services/InventoryDocumentModel.cs`:
   - `Documents` += `new DocumentDefinition("Списание ТМЗ", "doc_inventory_writeoff",
     "doc_inventory_writeoff_lines", "writeoff_id", "🗑️", 4, "...", new[] { "Списание ТМЦ" })`;
   - `Kinds` += `new KindDefinition("WRITEOFF_ACT", "Списание по акту", "Списание ТМЗ", "Outgoing", 9, "...")`
     и, при необходимости, второй вид.
2. Прогнать миграцию (запуск приложения или `new InventoryDocumentSchemaService(context).EnsureAsync()`).
3. Проверить в конфигураторе: документ, таблицы, записи в «Вид операции ТМЗ».
4. Открыть документ в меню → «Добавить» → заполнить шапку → выбрать строки → «Добавить/Запись».
5. Убедиться, что в `doc_inventory_writeoff_lines` появились строки с правильным `writeoff_id`,
   а в окне списка нижний грид показывает их без перезапуска.

---

## 12. Что менять в документации после добавления

- `Docs/InventoryDocumentDialog.md` — таблица документов (раздел «Назначение»),
  раздел «История правок».
- `Docs/InventoryDocumentHowTo.md` (этот файл) — если появились новые поля/колонки/виды операций.

Сборка и проверка: `dotnet build .\BIS.ERP.sln`, затем прогон по чек-листу из раздела 9.
