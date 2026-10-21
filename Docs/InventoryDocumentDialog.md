# Документ ТМЗ: диалог `InventoryDocumentDialog`

## Назначение

Окно редактирования документа товарно-материальных запасов (ТМЗ) по образцу счёта-фактуры: **шапка документа** (реквизиты заголовка) + **таблица строк** (материалы, количества, цены, налоги, итоги) + панель итогов.

Диалог используется для трёх видов документов:

| Документ | Имя в метаданных | Таблица шапки | Таблица строк | Иконка |
|---|---|---|---|---|
| Поступление ТМЗ | `Поступление ТМЗ` | `doc_inventory_incoming` | `doc_inventory_incoming_lines` | 📥 |
| Выбытие ТМЗ | `Выбытие ТМЗ` | `doc_inventory_outgoing` | `doc_inventory_outgoing_lines` | 📤 |
| Внутреннее перемещение ТМЗ | `Внутреннее перемещение ТМЗ` | `doc_inventory_movement` | `doc_inventory_movement_lines` | 🔁 |

Размер окна: 1240 × 740, позиционирование `CenterOwner`.

**Пространство имён:** `BIS.ERP.Views.Dialogs`  
**Класс:** `public partial class InventoryDocumentDialog : Window`  
**Файлы:** `Views/Dialogs/InventoryDocumentDialog.xaml`, `Views/Dialogs/InventoryDocumentDialog.xaml.cs`
## Открытие и закрытие

Диалог открывается из `DynamicDocumentWorkView` (панель работы с документами) через `MdiDialogService.ShowInWorkspaceForResultAsync` как MDI-дочернее окно workspace.

### Создание нового документа

```csharp
var dialog = new InventoryDocumentDialog(_documentMetadata, _metadataService, _context);
dialog.Owner = Window.GetWindow(this);
if (await MdiDialogService.ShowInWorkspaceForResultAsync(
        Window.GetWindow(this),
        dialog,
        $"Новый документ: {_documentMetadata.Name}") == true)
{
    await LoadData();
    DataGrid.SelectRowById(dialog.SavedRecordId, DataGridSelectionHelper.GetIdFromDataRow);
    StatusText.Text = "✅ Документ добавлен";
}
```

Параметры конструктора:

| Параметр | Тип | Назначение |
|---|---|---|
| `document` | `MetadataObject` | Метаданные документа (из `MetadataService`) |
| `metadataService` | `MetadataService` | Сервис для работы с динамическими записями и справочниками |
| `context` | `AppDbContext` | Контекст БД (передаётся в `InventoryDocumentLineService`) |
| `recordId` | `Guid?` | Идентификатор существующей записи (для редактирования); `null` — создание |
| `existingData` | `Dictionary<string, object>?` | Данные существующей записи (для редактирования) |

### Редактирование существующего документа

```csharp
var rows = await _metadataService.GetCatalogDataAsync(_documentMetadata.Id);
var existing = rows.FirstOrDefault(row =>
    row.TryGetValue("Id", out var value) && value is Guid guid && guid == id);

var dialog = new InventoryDocumentDialog(_documentMetadata, _metadataService, _context, id, existing);
dialog.Owner = Window.GetWindow(this);
if (await MdiDialogService.ShowInWorkspaceForResultAsync(
        Window.GetWindow(this),
        dialog,
        $"Редактирование: {_documentMetadata.Name}") == true)
{
    await LoadData();
    DataGrid.SelectRowById(id, DataGridSelectionHelper.GetIdFromDataRow);
    StatusText.Text = "✅ Документ обновлён";
}
```

### Результат диалога

- `DialogResult = true` — пользователь нажал «Запись»; документ сохранён. Доступен через свойство `SavedRecordId` (Guid).
- `DialogResult = false` — пользователь нажал «Отмена» или закрыл окно; изменения не сохранены.

Если MDI workspace недоступен, `MdiDialogService` откроет окно обычным модальным `ShowDialog()`.

## Инициализация

Инициализация выполняется асинхронно в обработчике события `Loaded`:

```csharp
Loaded += async (_, _) => await InitializeAsync();
```

### Этапы инициализации

1. **Загрузка справочников** — `MetadataService.GetCatalogsAsync()` возвращает все каталоги.
2. **Наполнение combo-полей шапки** — для каждого справочника загружаются данные через `MetadataService.GetCatalogDataAsync(catalog.Id)` и形成 списки `RefItem` (Id + Display):
   - `План счетов` — поле «Счет» (`counterparty_account`), id как код.
   - `Организации` — поле «Организация» (`organization_id`).
   - `Сотрудники (Списочный состав)` — поле «Сотрудник» (`employee_id`).
   - `Типы поставки` — поле «Вид покупки» (`supply_kind`).
   - `Налоги` — поля «Вид НДС» (`vat_type`) и «Налог» (`sales_tax_type`).
   - `Виды оплаты` — поле «Вид оплаты» (`payment_kind`).
   - `Справочник валют` — поле «Валюта» (`currency_id`).
   - `Участки` — поля «Участок-получатель» (`destination_site_id`) и колонка строк «Участок».
   - `МОЛ` — поля «МОЛ-получатель» (`destination_person_id`) и колонка строк «МОЛ».
   - `Справочник материалов` — источник для колонки «Материал» в строках (кэш `_materialRows`).
   - `Ввод нового документа ТМЗ` (`EntryCatalogName`) — поле «Вид операции» (`document_entry_id`), кэш `_entryCodes` для получения кода операции.
3. **Построение заголовка** — для каждого поля метаданных создаётся редактор (`TextBox`, `DatePicker`, `ComboBox`) на `HeaderPanel` (WrapPanel).
4. **Применение существующих значений** — для режима редактирования значения полей из `_existing` устанавливаются в соответствующие элементы управления.
5. **Генерация номера документа** — для новой записи, если поле `doc_number` пустое, вызывается `_metadataService.GetNextDocumentNumberAsync(_document)`.
6. **Загрузка строк** — `LoadLinesAsync()`:
   - Для новой записи: добавляется одна пустая строка (`AddLine()`).
   - Для существующей: строки загружаются через `_lineService.GetLinesAsync(linesTable, parentColumn, recordId)`.
7. **Пересчёт итогов** — `UpdateTotals()` вызывается после загрузки строк.

Если на любом этапе возникает исключение, показывается `MessageBox` с текстом ошибки и окно закрывается (`Close()`).

## Обновление справочников

Данные справочника «Ввод нового документа ТМЗ» (`catalog_inventory_document_entries`) загружаются один раз при открытии диалога и кэшируются в `_entryCodes` (соответствие `Id` → `operation_kind`). Если reference-данные изменяются в БД пока окно открыто, изменения не отображаются автоматически.

### Кнопка «Обновить справочники»

На панели действий есть кнопка **«Обновить справочники»** (`OnRefreshClick` → `RefreshEntriesAsync()`), которая:

1. Снова загружает все каталоги через `MetadataService.GetCatalogsAsync()`.
2. Перезагружает строки `catalog_inventory_document_entries` через `MetadataService.GetCatalogDataAsync()`.
3. Очищает кэш `_entryCodes` и заполняет его заново из свежих данных.
4. Обновляет `ItemsSource` ComboBox «Вид операции».
5. Сбрасывает выбранное значение в ComboBox.

Ошибки при обновлении отображаются в `MessageBox` с иконкой предупреждения, окно не закрывается.

## Поля шапки документа

Поля создаются динамически на основе метаданных документа (`_document.Fields`). Основные поля:

| Поле | Имя колонки | Тип управления | Справочник | Примечание |
|---|---|---|---|---|
| Номер | `doc_number` | TextBox | — | Генерируется автоматически для новой записи |
| Дата | `doc_date` | DatePicker | — | Глобальная маска `DatePickerInputMaskBehavior`: посимвольный ввод и шаблон `DD/MM/YYYY` + календарь. Значение читается через `SelectedDate`. По умолчанию `DateTime.Today` |
| Вид операции | `document_entry_id` | ComboBox | Ввод нового документа ТМЗ | Код операции сохраняется в `operation_kind` |
| № пачки | `pack_number` | TextBox | — | |
| Серия/№ бланка | `tax_blank_number` | TextBox | — | |
| Номер ЭСФ | `esf_number` | TextBox | — | |
| Счет | `counterparty_account` | ComboBox | План счетов | uuid → План счетов |
| Организация | `organization_id` | ComboBox | Организации | |
| Сотрудник | `employee_id` | ComboBox | Сотрудники | |
| Вид покупки | `supply_kind` | ComboBox | Типы поставки | |
| Вид НДС | `vat_type` | ComboBox | Налоги | |
| Ставка НДС, % | `vat_rate` | TextBox | — | Используется как начальная для строк |
| Вид оплаты | `payment_kind` | ComboBox | Виды оплаты | |
| Налог | `sales_tax_type` | ComboBox | Налоги | |
| Ставка налога, % | `sales_tax_rate` | TextBox | — | Используется как начальная для строк |
| Валюта | `currency_id` | ComboBox | Справочник валют | |
| Курс | `exchange_rate` | TextBox | — | |
| Участок-получатель | `destination_site_id` | ComboBox | Участки | |
| МОЛ-получатель | `destination_person_id` | ComboBox | МОЛ | |
| Примечание | `basis` | TextBox | — | |

Каждый редактор хранится в словаре `_editors` по имени колонки (`DbColumnName`).

## Строки инвентаризации

Строки документа хранятся в отдельной таблице `<шапка>_lines` (например, `doc_inventory_incoming_lines`). Связь с шапкой через колонку-родитель (`incoming_id`, `outgoing_id` или `movement_id`).

### Модель строки: `InventoryLineRow`

Реализует `INotifyPropertyChanged`. Свойства:

| Свойство | Тип | Описание |
|---|---|---|
| `Id` | `Guid` | Идентификатор строки |
| `LineNumber` | `int` | Номер строки (пересчитывается при сохранении) |
| `MaterialId` | `Guid?` | Ссылка на материал (Справочник материалов) |
| `MaterialName` | `string` | Наименование материала |
| `UnitName` | `string` | Единица измерения |
| `AccountCode` | `string` | Код счета дебета строки |
| `SiteId` | `Guid?` | Участок |
| `ResponsiblePersonId` | `Guid?` | МОЛ |
| `Quantity` | `decimal` | Количество |
| `Price` | `decimal` | Цена |
| `AmountWithoutTax` | `decimal` | Сумма без налога (Quantity × Price) |
| `VatRate` | `decimal` | Ставка НДС, % |
| `VatAmount` | `decimal` | Сумма НДС |
| `SalesTaxRate` | `decimal` | Ставка налога с продаж, % |
| `SalesTaxAmount` | `decimal` | Сумма налога с продаж |
| `LineTotal` | `decimal` | Итого строки (AmountWithoutTax + VatAmount + SalesTaxAmount) |
| `AmountCurrency` | `decimal` | Сумма в валюте |
| `Description` | `string` | Примечание |

### Сервис строк: `InventoryDocumentLineService`

Конструктор принимает `AppDbContext` и открывает собственное Npgsql-соединение к БД.

| Метод | Назначение |
|---|---|
| `GetLinesAsync(linesTable, parentColumn, headerId)` | Загрузка строк документа из таблицы строк по идентификатору шапки, упорядоченные по `line_number`. |
| `ReplaceLinesAsync(linesTable, parentColumn, headerId, lines)` | Полная замена строк: внутри транзакции удаляются все старые строки шапки, затем вставляются новые с пересчётом номеров (начиная с 1). |
| `RecalculateTotals(lines, out amountWithoutTax, out vatAmount, out salesTaxAmount, out total)` | Статический метод: пересчитывает итоги по всем строкам документа. |

### Работа со строками в диалоге

- **Добавление строки** — кнопка «Добавить строку» → `AddLine()`. Новая строка получает начальные значения `VatRate` и `SalesTaxRate` из полей шапки.
- **Удаление строки** — кнопка «Удалить строку» удаляет выбранную строку из `_lines` и вызывает `UpdateTotals()`.
- **Пересчёт** — кнопка «Пересчет» вызывает `Recalculate()` для каждой строки, обновляет грид и итоги.
- **Редактирование материала** — при окончании редактирования ячейки материала (`OnCellEditEnding`) из кэша `_materialRows` подставляются `MaterialName` и `UnitName`, затем выполняется `Recalculate()` и обновление итогов.
- **Проверка перед сохранением** — в каждой строке должен быть выбран материал (`MaterialId.HasValue`) и указано количество (> 0). Иначе показывается предупреждение и сохранение прерывается.

### Логика пересчёта строки (`Recalculate()`)

```csharp
AmountWithoutTax = Round(Quantity × Price, 2);
VatAmount = Round(AmountWithoutTax × VatRate / 100, 2);
SalesTaxAmount = Round(AmountWithoutTax × SalesTaxRate / 100, 2);
LineTotal = Round(AmountWithoutTax + VatAmount + SalesTaxAmount, 2);
```

Округление: `MidpointRounding.AwayFromZero`, 2 знака.

## Сохранение документа

Кнопка «Запись» (`OnSaveClick`) выполняет следующую последовательность:

1. **Валидация строк** — каждая строка должна иметь материал (`MaterialId.HasValue`) и количество > 0. Иначе показывается предупреждение и сохранение прерывается.
2. **Построение данных шапки** — `BuildItemData()`:
   - Для каждого поля метаданных считывается значение из соответствующего редактора (`ReadEditorValue`).
   - Пустые текстовые значения пропускаются (`null`).
   - Добавляются итоговые суммы: `amount_without_tax`, `vat_total`, `sales_tax_total`, `amount`, `amount_currency`.
   - Для поля «Вид операции» (`document_entry_id`) из выбранного элемента combo берется код операции через `_entryCodes` и сохраняется в `operation_kind`. Если выбранный элемент не найден в `_entryCodes`, выбрасывается `InvalidOperationException` с сообщением о необходимости указать вид документа ТМЗ.
   - Для новой записи устанавливается `is_posted = false`.
3. **Создание или обновление записи шапки**:
   - Редактирование: `_metadataService.UpdateDynamicRecordAsync(document.Id, recordId, data)`.
   - Создание: `SavedRecordId = await _metadataService.CreateDynamicRecordAsync(document.Id, data)`.
4. **Замена строк** — `_lineService.ReplaceLinesAsync(linesTable, parentColumn, SavedRecordId, _lines.ToList())` внутри транзакции.
5. **Закрытие** — `DialogResult = true`.

Все ошибки сохранения перехватываются и отображаются в `MessageBox` (окно не закрывается).

## Нумерация документа

Номер документа для новой записи получается через:

```csharp
numberBox.Text = await _metadataService.GetNextDocumentNumberAsync(_document);
```

`MetadataService.GetNextDocumentNumberAsync(MetadataObject document)`:

1. Создаёт таблицу нумерации `doc_numbering` (если не существует).
2. Определяет ключ нумерации документа (`GetDocumentNumberingKey(document)`).
3. Сканирует существующие документы этого типа и вычисляет следующий номер (`GetSuggestedNextDocumentNumberAsync`).
4. Вставляет запись в `doc_numbering` (`ON CONFLICT DO NOTHING`).
5. Обновляет счётчик только в сторону увеличения (никогда не уменьшает), чтобы избежать сброса нумерации.


## Обработка ошибок

| Место | Обработка |
|---|---|
| `InitializeAsync()` | Весь метод обёрнут в `try/catch`. При исключении — `MessageBox.Show(ex.Message, ... Error)` и `Close()`. |
| `OnSaveClick()` | Весь метод обёрнут в `try/catch`. При исключении — `MessageBox.Show(ex.Message, ... Error)`. Окно остаётся открытым (`DialogResult` не устанавливается). |
| Валидация строк | `MessageBox` с предупреждением (`MessageBoxImage.Warning`), сохранение прерывается (`return`). |
| `ReplaceLinesAsync()` | Ошибки внутри транзакции пробрасываются в `OnSaveClick` и обрабатываются общим `catch`. |
Номер не пересчитывается при редактировании существующего документа — поле `doc_number` редактируется пользователем вручную.

## Схема взаимодействия

```mermaid
sequenceDiagram
    participant User
    participant DDV as DynamicDocumentWorkView
    participant MDS as MdiDialogService
    participant IDD as InventoryDocumentDialog
    participant ILS as InventoryDocumentLineService
    participant MS as MetadataService
    participant DB as PostgreSQL (AppDbContext)

    User->>DDV: Нажать «Добавить» / «Редактировать»
    DDV->>MDS: ShowInWorkspaceForResultAsync(new InventoryDocumentDialog)
    MDS->>IDD: Создать окно (конструктор)
    IDD->>MS: GetCatalogsAsync()
    MS-->>IDD: Список каталогов
    loop Для каждого справочника
        IDD->>MS: GetCatalogDataAsync(catalog.Id)
        MS-->>IDD: Строки справочника
    end
    IDD->>IDD: Построить editors шапки (BuildHeader)
    alt Редактирование
        IDD->>IDD: ApplyExistingValues()
    else Новый документ
        IDD->>MS: GetNextDocumentNumberAsync(document)
        MS-->>IDD: Номер документа
        MS->>DB: doc_numbering (INSERT / UPDATE)
    end
    alt Редактирование
        IDD->>ILS: GetLinesAsync(linesTable, parentColumn, recordId)
        ILS->>DB: SELECT FROM <таблица строк>
        DB-->>ILS: Строки
        ILS-->>IDD: Список строк
    else Новый документ
        IDD->>IDD: AddLine() (одна пустая строка)
    end
    IDD->>IDD: UpdateTotals()
    IDD-->>User: Открыть окно диалога

    User->>IDD: Редактировать шапку и строки
    User->>IDD: Нажать «Запись»
    IDD->>IDD: BuildItemData() (сбор реквизитов + итоги)
    IDD->>MS: CreateDynamicRecordAsync / UpdateDynamicRecordAsync
    MS->>DB: INSERT / UPDATE <таблица шапки>
    DB-->>MS: Id записи
    MS-->>IDD: SavedRecordId
    IDD->>ILS: ReplaceLinesAsync(linesTable, parentColumn, SavedRecordId, lines)
    ILS->>DB: DELETE FROM <строки> WHERE parentColumn = @header
    loop Для каждой строки
        ILS->>DB: INSERT INTO <строки> (...)
    end
    ILS-->>IDD: OK (коммит транзакции)
    IDD->>IDD: DialogResult = true
    IDD-->>MDS: Закрыть окно с результатом true
    MDS-->>DDV: Вернуть true
    DDV->>MS: LoadData() (перезагрузка списка документов)
    DDV->>DDV: SelectRowById(SavedRecordId)
    DDV->>DDV: StatusText = "✅ Документ добавлен/обновлён"
```

## Классы и типы

### `InventoryDocumentLineService` (`Services/InventoryDocumentLineService.cs`)

- `InventoryDocumentLineService(AppDbContext context)` — конструктор.
- `Task<List<InventoryLineRow>> GetLinesAsync(string linesTable, string parentColumn, Guid headerId)` — загрузка строк.
- `Task ReplaceLinesAsync(string linesTable, string parentColumn, Guid headerId, IReadOnlyList<InventoryLineRow> lines)` — замена строк (DELETE + INSERT в транзакции).
- `static void RecalculateTotals(...)` — статический расчёт итогов.

### `InventoryLineRow` (`Services/InventoryDocumentLineService.cs`)

Модель строки с `INotifyPropertyChanged` и методом `Recalculate()`.

### `InventoryDocumentModel` (`Services/InventoryDocumentModel.cs`)

Статический класс с константами и определениями документов:

- `IncomingName`, `OutgoingName`, `MovementName` — имена документов.
- `IncomingTable`, `OutgoingTable`, `MovementTable` — таблицы шапок.
- `IncomingLinesTable`, `OutgoingLinesTable`, `MovementLinesTable` — таблицы строк.
- `EntryCatalogName = "Ввод нового документа ТМЗ"` — каталог видов операций.
- `DocumentDefinition` — описание документа (имя, таблица, строки, колонка-родитель, иконка, порядок, описание, legacy-имена).
- `KindDefinition` — описание вида операции (код, название, документ, группа движения, порядок, описание). Всего 8 видов операций.

### `MetadataService` (`Services/MetadataService.cs`)

Ключевые методы, используемые диалогом:

| Метод | Назначение |
|---|---|
| `GetCatalogsAsync()` | Список всех каталогов метаданных. |
| `GetCatalogDataAsync(Guid catalogId)` | Данные каталога (строки). |
| `GetNextDocumentNumberAsync(MetadataObject document)` | Следующий номер документа. |
| `CreateDynamicRecordAsync(Guid metadataId, Dictionary<string, object> data)` | Создание динамической записи (документа). |
| `UpdateDynamicRecordAsync(Guid metadataId, Guid recordId, Dictionary<string, object> data)` | Обновление существующей динамической записи. |

### `MdiDialogService` (`Services/MdiDialogService.cs`)

| Метод | Назначение |
|---|---|
| `ShowInWorkspaceForResultAsync(Window owner, Window dialog, string? title, ...)` | Открыть диалог как MDI-документ с возвратом результата (`Task<bool?>`). При недоступности workspace — модальное `ShowDialog()`. |

## Проверка и notas

- Файл документации создан без изменения исходного кода проекта.
- Сборка проекта: `dotnet build .\BIS.ERP.sln`.
- Диалог работает с PostgreSQL через Npgsql (страницы строк — прямые SQL-запросы через `AppDbContext`).
- Поле `counterparty_account` (Счет шапки) — ссылка на справочник «План счетов», хранится как uuid.
- Вид операции из поля `document_entry_id` сохраняется отдельно в `operation_kind` (код из `_entryCodes`).
- Номер документа генерируется только для новых записей и только если поле пустое.
- Поле `doc_date` (Дата шапки) реализовано как `DatePicker` (метод `AddDateText`); оно использует глобальную маску `DatePickerInputMaskBehavior` (инициализируется в `App.xaml.cs` на `DatePicker.Loaded`) — посимвольный ввод, шаблон `DD/MM/YYYY` и календарь. Значение засевается через `SetDateEditor` (`SelectedDate`), применяется в `ApplyExistingValues`, читается при сохранении в `ReadEditorValue` (`case DatePicker`).