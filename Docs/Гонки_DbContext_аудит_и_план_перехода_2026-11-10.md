# Аудит гонок DbContext и план перехода на «новое подключение на действие»

**Дата:** 10.11.2026
**Состояние:** только аудит и план. Код в этом шаге не менялся.
**Направление, заданное пользователем:** «убирать DbContext, переход на каждое действие новое подключение к БД».

Документ фиксирует: (1) что именно осталось от гонки и где, (2) целевую архитектуру «одно действие — одно подключение», (3) порядок правок с шаблонами кода и чек-листом по файлам, (4) критерии приёмки и подводные камни.

> Метод: статический разбор кода (чтение обработчиков, сервисов, старта). Runtime-подтверждения нет — в документе отмечено, где вывод основан на коде, а не на логе.

---

## 1. Резюме

| Слой | Найдено | Критичных | Характер |
|---|---|---|---|
| UI-окна и диалоги | 10 | 4 | повторный вход/параллельные загрузки на одном контексте |
| Сервисы | 13 | 2 | соединение открыто без `finally`, закрытие при живом reader |
| Старт / режимы / межокно | 14 | 2 | двойные цепочки миграций и посадок, `RaiseEvent(Loaded)` в чужом `await` |
| **Итого (без дублей)** | **~35** | **8** | — |

Корневая причина одна: **`AppDbContext` один на приложение** и передаётся в окна/сервисы как долгоживущий объект. Ни `DbContext`, ни `NpgsqlConnection` не переносят параллельных команд — вторая команда даёт `A command is already in progress` / `A second operation was started on this context instance`.

В проекте уже есть правильный механизм — ворота `Services/DbContextGate.cs` («контекст один на всё приложение»), но он **используется ровно в одном месте**: `MetadataService.cs:521` (`AddCatalogItemCoreAsync`). Поэтому гонка не устранена, а «размазана» по приложению.

Целевая схема избавляет от ворот как от обязательного атрибута: если **каждое действие** берёт своё подключение, ворота нужны только для переходного периода и для миграций.

---

## 2. Целевая архитектура: «одно действие — одно подключение»

### 2.1. Правила

1. **Окно/диалог не хранит `AppDbContext` и не хранит сервис, привязанный к одному контексту, дольше одного действия.** Максимум, что можно хранить: строку подключения, `IAppDbContextFactory` или «фабрику контекста» (`Func<Task<AppDbContext>>`).
2. **Действие = загрузка списка, открытие карточки, сохранение, пересчёт, экспорт.** Действие получает контекст в начале и освобождает в `finally`/`await using`.
3. **Транзакция живёт внутри одного действия и одного контекста.** Нельзя начать транзакцию на контексте окна, а закончить на новом.
4. **`Task.WhenAll` по одному контексту запрещён.** Если нужно параллельно — параллельно по *разным* контекстам (или последовательно в одном).
5. **Для сырого SQL** — отдельное `NpgsqlConnection` (пул Npgsql переиспользует физические соединения): `await using var connection = new NpgsqlConnection(connectionString);` и `try/finally` вокруг `Open/Close`.
6. **Долгие операции чтения** (отчёты, выгрузки) — своё подключение на операцию; в UI не должно быть «контекст окна, который живёт, пока окно открыто».

### 2.2. Чем создавать контекст

В проекте уже есть готовая точка — фабрика:

```csharp
// Services/IAppDbContextFactory.cs
public interface IAppDbContextFactory
{
    /// <summary>Контекст текущей информационной базы. Вызывающий обязан освободить.</summary>
    Task<AppDbContext> CreateAsync();
}

// Services/AppDbContextFactory.cs
public sealed class AppDbContextFactory : IAppDbContextFactory
{
    private readonly InfoBaseManager _infoBaseManager;
    public AppDbContextFactory(InfoBaseManager infoBaseManager) => _infoBaseManager = infoBaseManager;

    public Task<AppDbContext> CreateAsync() => _infoBaseManager.GetCurrentDbContextAsync();   // → new AppDbContext(connectionString)
}
```

Использование (базовый шаблон действия):

```csharp
        await using var context = await ServiceLocator.DbContextFactory.CreateAsync();
        try
        {
            var service = new InvoiceService(context);
            …
        }
        finally
        {
            // await using закроет контекст и его соединение
        }
```

Для окон, у которых уже есть `MetadataService`, есть короткий путь — `CreateIndependentContext()` (`Services/MetadataService.cs:84`), он делает то же самое (фабрика → строка подключения → `new AppDbContext`):

```csharp
        public AppDbContext CreateIndependentContext()
        {
            if (_contextFactory != null)
                return _contextFactory.CreateDbContext();
            if (!string.IsNullOrEmpty(_connectionString))
                return new AppDbContext(_connectionString);
            var connString = _context?.Database.GetConnectionString() ?? AppSettings.Instance.GetMasterConnectionString();
            return new AppDbContext(connString);
        }
```

И `CreateIndependent()` — «сервис на своём подключении» для окна/диалога (`MetadataService.cs:113`).

### 2.3. Что в проекте уже сделано в этом направлении

| Место | Что уже правильно |
|---|---|
| `MetadataService.CreateIndependent()` / `CreateIndependentContext()` | сервис/контекст на своё подключение — готовый инструмент |
| `Views/CashOrderWorkView`, `PostingsView`, `PaymentOrderWorkView`, `InvoiceWorkView`, `FinanceDocumentWorkView` | берут `metadataService.CreateIndependent()` |
| `Views/Dialogs/CashOrderDialog`, `FinanceDocumentDialog`, `PaymentOrderDialog`, `InvoiceEditDialog` | то же |
| `Views/EsfExportWorkView` (правка 10.11) | `CreateIsolatedContext()` на загрузку и на выгрузку |
| `Services/PostingService`, `ReportDataSetService`, `SeedStateService`, `DatabaseSchemaCache`, `InventoryDocumentLineService` | работают на собственных соединениях |
| `Services/TableColumnProbe` (правка 10.11) | чтение `information_schema` без DDL |
| `BIS.ERP.TESTS/…/ContextIsolationScenario.cs` | уже есть тест-сценарий «изоляция окон по подключениям» (`concurrency-isolation`) |

### 2.4. Что осталось на общей схеме

- `Views/CatalogDataView.xaml.cs:55` — `_metadataService = metadataService;` (общий, из `MainWorkWindow:2099`).
- `Views/DynamicDocumentWorkView.xaml.cs:40` — то же (`MainWorkWindow:2115`), плюс fire-and-forget загрузка строк.
- `Views/MutualSettlementsView.xaml.cs:21` — то же (`MainWorkWindow:2140`).
- `Views/Dialogs/InventoryDocumentDialog.xaml.cs:99` — то же.
- `Views/AccountingReportsView.xaml.cs:44/56` — держит `AppDbContext` и `PostingService` на всё окно.
- `Views/PostingsJournalView.xaml.cs:27` — держит `PostingService` (контекст, созданный в `MainWorkWindow:2127`).

---

## 3. Найденные гонки (полный список)

### 3.1. UI-слой (10)

| № | Место | Что происходит | Опасность |
|---|---|---|---|
| A1 | `Views/DynamicDocumentWorkView.xaml.cs:92` | `_ = LoadPostingLinesAsync()` из `SelectionChanged` (89) без флага; накладывается на `LoadDataCore` (303–346), контекст общий | высокая |
| A2 | `Views/EmployeesCatalogView.xaml.cs:179` | `async void TextChanged → SearchEmployees`: запрос на каждый символ параллельно `LoadEmployees` | высокая |
| A3 | `Views/Dialogs/InventoryDocumentDialog.xaml.cs:129` | `Loaded += async → InitializeAsync` без флага, ~15 запросов (136–158); `Loaded` в MDI приходит повторно | высокая |
| A4 | `Views/CatalogDataView.xaml.cs:1407` | `_ = RefreshOrganizationAccountsAsync` из `SelectionChanged` (378); `requestId` (1531) не сериализует запросы (1551–1560) | высокая |
| A5 | `Views/PostingsJournalView.xaml.cs:36,159` | `Loaded` и «Обновить» зовут `LoadPostingsAsync` без флага — два `GetAllPostingsAsync` | средняя |
| A6 | `Views/MutualSettlementsView.xaml.cs:27` | `IsVisibleChanged += async` → `LoadAdvancePaymentPairs` + `LoadData`; `btnRefresh_Click` (123) параллельно | средняя |
| A7 | `Views/UserAccessManagementView.xaml.cs:44` | `async void SelectionChanged` без защиты; `LoadUsersAsync` (41) сам ставит `SelectedIndex=0` → повторный вход | средняя |
| A8 | `Views/AccountingSetupView.xaml.cs:125` | `OnReportCodeChanged → LoadLinesAsync`; тот же метод зовёт `LoadAsync` (40) | средняя |
| A9 | `Views/ReportDesignerWindow.xaml.cs:53` | `_ = InitializeAsync(report)` в конструкторе; `OnPreviewClick` (1024), `OnSaveClick` (1038) без флага | средняя |
| A10 | `Views/ConfiguratorWindow.xaml.cs:3150` | `_ = CopyReportAsync` и `OnRefreshClick` делят `_context` (3164) | низкая |

### 3.2. Сервисы (13)

Критичные:

| № | Место | Проблема |
|---|---|---|
| B1 | `Services/InvoiceService.cs:253` `GetInvoicesAsync` | `Open` + reader без `finally`; зовут `EsfExportWorkView` и `InvoiceWorkView` |
| B2 | `Services/InvoiceService.cs:364` `GetInvoiceAsync` | `Open` без `finally`: исключение при маппинге оставляет соединение открытым |

Высокие:

| № | Место | Проблема |
|---|---|---|
| B3 | `Services/MetadataService.cs:542,605` `AddCatalogItemCoreAsync` | **два** `OpenConnectionAsync` при одном `Close` (607) → соединение остаётся открытым; исключение «Заполните обязательные поля» тоже течёт |
| B4 | `Services/MetadataService.cs:489` `LoadReferenceDictionaryAsync` | `Close` при живом reader, `catch` глотает всё → тихая утечка |
| B5 | `Services/MetadataService.cs:1987` `TryReadCurrencyIdAsync` | open/close без `finally`, `catch` глотает |
| B6 | `Services/MetadataService.cs:2964` `UpdateRecordFieldAsync` | open без `finally`, зовётся пачками при пересчёте ОС |

Средние:

| № | Место | Проблема |
|---|---|---|
| B7 | `Services/MetadataService.cs:2450` `DeleteDynamicRecordAsync` | open без `finally` |
| B8 | `Services/MetadataService.cs:5457` `CreateDocumentNumberingTableAsync` | open без `finally` |
| B9 | `Services/MetadataService.Catalogs.cs:1912` `EnsurePositionCatalogDataAsync` | open без `finally` |
| B10 | `Services/MetadataService.DataSeed.cs:638` `EnsurePrimaryOrganizationDataAsync` | open без `finally` |
| B11 | `Services/ReportService.cs:877` `CheckTableExistsAsync` | open без `finally` |
| B12 | `Services/DynamicObjectService.cs:106` `GetRecordDataAsync` | `Close` при открытом reader |
| B13 | `Services/DynamicObjectService.cs:292` `UpdateRecordFieldAsync` | open без `finally` |

Чисто (для протокола): `Task.WhenAll`/`Parallel`, `.Result`, статических `DbContext` в сервисах нет; `SystemLogService` пишет в файл, не в БД.

### 3.3. Старт, режимы, межокно (14, топ-10)

| № | Место | Сценарий | Опасность |
|---|---|---|---|
| C1 | `Views/MainWorkWindow.xaml.cs:609` `OnLoaded` | миграции (654), посадки (670–680) без флага повторного входа: второй `Loaded` = вторая цепочка DDL+seed | критично |
| C2 | `Views/MainWorkWindow.xaml.cs:2838` | смена режима: `Show()` + `Close()`, `OnLoaded` нового окна не отменён → две цепочки миграций одновременно | критично |
| C3 | `Views/ConfiguratorWindow.xaml.cs:239` | DDL + посадки на `_context`, окно уже кликабельно; `_isLoading` (203) — не ворота | высокая |
| C4 | `Views/ConfiguratorWindow.xaml.cs:2082` | тумблер отчёта пишет в `_context` во время загрузки | высокая |
| C5 | `Views/UserAccessManagementView.xaml.cs:44` | `async void SelectionChanged` на контексте конфигуратора (560): два клика = две команды | высокая |
| C6 | `Views/Dialogs/FinanceDocumentDialog.xaml.cs:280→317` | `BeginInvoke(async)` → неохраняемый `GetAdvancePaymentPairsAsync` поверх `InitializeAsync` | высокая |
| C7 | `Views/Controls/MdiDialogHostControl.cs:26` | `RaiseEvent(Loaded)` не показанному окну: инициализация диалога в чужом `await`, сервис общий (258) | высокая |
| C8 | `Views/MainWorkWindow.xaml.cs:2330,2351` | один клик по дереву → два `OpenNavigationItemAsync`; `_isLoadingReport:70` узкий; `DbfDocuments` → два `DynamicDocumentsView` на одном `DocumentService` (2121) | средняя |
| C9 | `Views/MainWorkWindow.xaml.cs:2414→943` | дерево читает `_documentService` (без ворот, 319) параллельно вкладкам | средняя |
| C10 | `Views/ConfiguratorWindow.xaml.cs:1044` | refresh шаблонов: `GetTemplatesAsync` на `_context` без флага | средняя |

Ещё: `ConfiguratorWindow.xaml.cs:3167, 3573`, `MainWorkWindow.xaml.cs:2221`, `UserAccessManagementView.xaml.cs:34`.

---

## 4. План правок по этапам

Порядок выбран так, чтобы каждый этап был самостоятельно проверяемым и не требовал переписывать пол-приложения сразу.

### Этап 0. Каркас (0.5 дня)

1. Ввести в окнах одно правило: «контекст создаётся в начале действия и живёт до его конца». Никаких `_context`-полей в новых окнах.
2. Добавить маленький helper, чтобы не повторять три строки в каждом обработчике:

```csharp
// Services/DbContextScope.cs
public static class DbContextScope
{
    /// <summary>
    /// Контекст на одно действие: загрузка, сохранение, экспорт.
    /// Освобождается вызывающим (await using).
    /// </summary>
    public static Task<AppDbContext> CreateAsync() => ServiceLocator.DbContextFactory.CreateAsync();
}
```

3. Для сырого SQL — helper на `NpgsqlConnection` с гарантированным закрытием:

```csharp
public static async Task<T> WithConnectionAsync<T>(Func<NpgsqlConnection, Task<T>> action)
{
    await using var connection = new NpgsqlConnection(CurrentConnectionString());
    await connection.OpenAsync();
    try
    {
        return await action(connection);
    }
    finally
    {
        await connection.CloseAsync();
    }
}
```

4. Договориться о переходном статусе `DbContextGate`: он остаётся только для миграций/старта; новый код его не использует (иначе смысл «своё подключение на действие» теряется, и приложение снова сериализуется в одну очередь).

### Этап 1. Сервисы: соединение всегда закрывается (1 день)

Правки чисто механические, эффект — перестают «залипать» соединения после первого же исключения (это и есть причина, почему после одной ошибки начинают падать все последующие запросы).

Шаблон «было / стало»:

```csharp
// было: Open без finally
var connection = _context.Database.GetDbConnection();
await _context.Database.OpenConnectionAsync();
await using var reader = await command.ExecuteReaderAsync();   // исключение → соединение открыто навсегда
…
await _context.Database.CloseConnectionAsync();

// стало
var connection = _context.Database.GetDbConnection();
var openedHere = connection.State != ConnectionState.Open;
if (openedHere)
    await _context.Database.OpenConnectionAsync();
try
{
    await using var reader = await command.ExecuteReaderAsync();
    …
}
finally
{
    if (openedHere)
        await _context.Database.CloseConnectionAsync();
}
```

Файлы и точные места:

| Файл · метод | Что сделать |
|---|---|
| `Services/InvoiceService.cs:253` `GetInvoicesAsync` | обернуть `Open/reader` в `try/finally`, закрывать только если открывали сами |
| `Services/InvoiceService.cs:364` `GetInvoiceAsync` | то же |
| `Services/MetadataService.cs:542,605` `AddCatalogItemCoreAsync` | оставить **один** `Open` и один `Close` в `finally` (сейчас два `Open`, один `Close`) |
| `Services/MetadataService.cs:489` `LoadReferenceDictionaryAsync` | закрывать после освобождения reader; не глотать исключение в `catch` (логировать и пробрасывать) |
| `Services/MetadataService.cs:1987` `TryReadCurrencyIdAsync` | `try/finally`, `catch` — только логирование + `false` |
| `Services/MetadataService.cs:2964` `UpdateRecordFieldAsync` | `try/finally` |
| `Services/MetadataService.cs:2450`, `:5457` | `try/finally` |
| `Services/MetadataService.Catalogs.cs:1912` | `try/finally` |
| `Services/MetadataService.DataSeed.cs:638` | `try/finally` |
| `Services/ReportService.cs:877` | `try/finally` |
| `Services/DynamicObjectService.cs:106,292` | `try/finally`; в 106 закрывать после reader |

Приёмка этапа: сценарий «вызвать метод, спровоцировать исключение, затем выполнить любой другой запрос» — второй запрос проходит.

### Этап 2. Окна, которые держат общий сервис (2–3 дня)

Цель: окно получает **фабрику**, а не готовый сервис. Каждая загрузка/сохранение — свой контекст.

Шаблон «было / стало» для окна:

```csharp
// было
private readonly MetadataService _metadataService;   // общий, из MainWorkWindow
public CatalogDataView(MetadataObject catalog, MetadataService metadataService)
{
    _metadataService = metadataService;               // живёт, пока живёт вкладка
}

private async Task LoadDataAsync()
{
    var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);   // конкурирует с другими окнами
}

// стало
private readonly MetadataService _metadataService;   // только как «источник строки подключения/фабрики»
…
private async Task LoadDataAsync()
{
    await using var context = _metadataService.CreateIndependentContext();
    var scoped = new MetadataService(context);
    var rows = await scoped.GetCatalogDataAsync(_catalog.Id);
}
```

Правило: **в поле окна не остаётся ничего, что держит контекст**; `MetadataService` в поле допустим как «фабрика контекста» (у него есть `CreateIndependentContext()`), но его методы для запросов — не вызывать.

Файлы этапа:

| Файл | Что заменить |
|---|---|
| `Views/CatalogDataView.xaml.cs` | все вызовы `_metadataService.*` (загрузка, сохранение, справочники) → локальный контекст на действие; убрать `requestId`-костыль (1531–1560), заменив его single-flight (этап 3) |
| `Views/DynamicDocumentWorkView.xaml.cs` | `LoadDataCore` (303–346), `LoadPostingLinesAsync` (104), `Reload` → каждый на своём контексте; `_context` (переданный извне) убрать из полей |
| `Views/MutualSettlementsView.xaml.cs` | `LoadData` (52+), `LoadAdvancePaymentPairsAsync` (44+) → свой контекст на загрузку |
| `Views/Dialogs/InventoryDocumentDialog.xaml.cs` | `InitializeAsync` (129+): справочники, план счетов, строки — один контекст на инициализацию, отдельный на сохранение, отдельный на пересчёт |
| `Views/AccountingReportsView.xaml.cs` | `_postingService` (56) → создавать `PostingService` на построение отчёта |
| `Views/PostingsJournalView.xaml.cs` | `LoadPostingsAsync` → новый `PostingService`/контекст на загрузку |
| `Views/ReferencePickerControlFactory.cs:490` | `new CatalogDataView(catalog, metadataService)` — проверить, что передаётся не общий сервис, либо перевести на фабрику |
| `Views/MainWorkWindow.xaml.cs:2099,2115,2140` | передавать в окна фабрику/`CreateIndependent()`, а не `_metadataService` |

Приёмка этапа: две вкладки, открытые из дерева, одновременно грузятся без ошибок; переключение документов в ТМЗ не даёт `A command is already in progress`.

### Этап 3. Повторный вход в UI (1–2 дня)

Даже при своём подключении на действие повторный вход нежелателен: пользователь видит мигание, данные перезаписываются, лишние запросы. Правило: **одна загрузка за раз, лишний запрос — в очередь на один повтор** (coalesce), а не параллельно.

```csharp
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private bool _reloadRequested;

        private async Task LoadAsync()
        {
            if (!await _loadGate.WaitAsync(0))
            {
                _reloadRequested = true;      // пришло во время загрузки — повторим один раз после
                return;
            }

            try
            {
                do
                {
                    _reloadRequested = false;
                    await LoadCoreAsync();
                }
                while (_reloadRequested);
            }
            finally
            {
                _loadGate.Release();
            }
        }
```

Точечно по местам:

| Место | Правка |
|---|---|
| `DynamicDocumentWorkView:92` | `_ = LoadPostingLinesAsync()` → через `_loadGate` с `_reloadRequested` (см. шаблон); при смене выбора — отмена предыдущего по `CancellationTokenSource` |
| `EmployeesCatalogView:179` | дебаунс 250–300 мс + отмена предыдущего поиска (`CancellationTokenSource`) |
| `InventoryDocumentDialog:129` | `Loaded` → `OnFirstLoaded` с отпиской: `Loaded -= OnFirstLoaded;` — инициализация строго один раз |
| `CatalogDataView:1407` | заменить `requestId` на `_loadGate` + отмена |
| `PostingsJournalView:36,159` | `_loadGate` вокруг `LoadPostingsAsync` |
| `MutualSettlementsView:27` | `_loadGate` + отписка `IsVisibleChanged` при уходе со вкладки |
| `UserAccessManagementView:44` | не ставить `SelectedIndex` внутри `SelectionChanged`; загрузку — через `_loadGate` |
| `AccountingSetupView:125` | `_loadGate`; `LoadAsync` и `OnReportCodeChanged` — один вход |
| `ReportDesignerWindow:53` | `_ = InitializeAsync` → `await` в `Loaded` + флаг `_initialized` |
| `ConfiguratorWindow:3150` | `_ = CopyReportAsync` → `await` в обработчике, один контекст на копирование |

### Этап 4. Старт и переключение режимов (1 день)

1. `MainWorkWindow.OnLoaded` — флаг и ворота:

```csharp
        private static readonly SemaphoreSlim StartupGate = new(1, 1);
        private bool _startupCompleted;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_startupCompleted)
                return;

            await StartupGate.WaitAsync();
            try
            {
                if (_startupCompleted)
                    return;
                …
                _startupCompleted = true;
            }
            finally
            {
                StartupGate.Release();
            }
        }
```

2. Смена режима (`MainWorkWindow:2838`): не показывать новое окно до завершения инициализации либо передавать «инициализация уже идёт» флагом (например, статический `StartupGate` — общий для рабочего окна и конфигуратора).
3. `ConfiguratorWindow:239` — те же ворота; `_isLoading` заменить на реальный guard (не путать «окно загружается» и «окно можно кликать»).
4. Миграции/посадки — либо под общими воротами, либо идемпотентные шаги с проверкой `schema_migrations` внутри транзакции (сейчас проверка снаружи).
5. `MdiDialogHostControl:26` — не поднимать `Loaded` вручную до показа; если нужно инициализировать скрытое окно, делать это явным методом `InitializeAsync()` контракта диалога, а не событием жизненного цикла.

### Этап 5. Уборка и закрепление (0.5–1 день)

1. `DbContextGate` — оставить только для миграций; из `MetadataService` убрать `_contextAccess` (он дублирует ворота и сериализует вообще всё).
2. Расширить `BIS.ERP.TESTS/…/ContextIsolationScenario.cs`: сейчас 4 окна × 10 итераций чтения; добавить сценарии «две загрузки одного окна», «исключение в середине чтения → следующий запрос», «сохранение документа параллельно с загрузкой списка».
3. Добавить в лог-мониторинг поиск по строкам `A command is already in progress` / `A second operation was started` — как индикатор регрессии (сейчас эти ошибки частично глушатся в `App.xaml.cs:182`).

---

## 5. Чек-лист по файлам

| Файл | Этап | Действие |
|---|---|---|
| `Services/DbContextScope.cs` (новый) | 0 | helper «контекст на действие» + `WithConnectionAsync` |
| `Services/InvoiceService.cs` | 1 | `try/finally` в `GetInvoicesAsync` (253), `GetInvoiceAsync` (364) |
| `Services/MetadataService.cs` | 1 | один `Open`/`Close` в `AddCatalogItemCoreAsync` (542/605/607); `finally` в 489, 1987, 2450, 2964, 5457 |
| `Services/MetadataService.Catalogs.cs` | 1 | `finally` в 1912 |
| `Services/MetadataService.DataSeed.cs` | 1 | `finally` в 638 |
| `Services/ReportService.cs` | 1 | `finally` в 877 |
| `Services/DynamicObjectService.cs` | 1 | `finally` в 106, 292; порядок reader/close |
| `Views/CatalogDataView.xaml.cs` | 2, 3 | контекст на действие; single-flight вместо `requestId` |
| `Views/DynamicDocumentWorkView.xaml.cs` | 2, 3 | контекст на действие; `LoadPostingLinesAsync` — под `_loadGate` |
| `Views/MutualSettlementsView.xaml.cs` | 2, 3 | контекст на действие; `_loadGate`, отписка `IsVisibleChanged` |
| `Views/Dialogs/InventoryDocumentDialog.xaml.cs` | 2, 3 | контексты по действиям; `OnFirstLoaded` |
| `Views/AccountingReportsView.xaml.cs` | 2 | `PostingService` на построение отчёта |
| `Views/PostingsJournalView.xaml.cs` | 2, 3 | `PostingService` на загрузку; `_loadGate` |
| `Views/MainWorkWindow.xaml.cs` | 2, 4 | фабрика в окна (2099/2115/2140); guard в `OnLoaded`; смена режима |
| `Views/ConfiguratorWindow.xaml.cs` | 3, 4 | guard загрузки; тумблер отчёта (2082); `CopyReportAsync` (3150); refresh шаблонов (1044) |
| `Views/EmployeesCatalogView.xaml.cs` | 3 | дебаунс + отмена |
| `Views/UserAccessManagementView.xaml.cs` | 3, 4 | убрать `SelectedIndex` из `SelectionChanged`; `_loadGate` |
| `Views/AccountingSetupView.xaml.cs` | 3 | `_loadGate` |
| `Views/ReportDesignerWindow.xaml.cs` | 3 | `await` инициализации в `Loaded`, флаг |
| `Views/Dialogs/FinanceDocumentDialog.xaml.cs` | 3 | `BeginInvoke(async)` → `await` в обработчике |
| `Views/Controls/MdiDialogHostControl.cs` | 4 | не поднимать `Loaded` вручную |
| `Views/ReferencePickerControlFactory.cs` | 2 | проверка, что в `CatalogDataView` не идёт общий сервис |
| `BIS.ERP.TESTS/…/ContextIsolationScenario.cs` | 5 | новые сценарии конкуренции |

---

## 6. Критерии приёмки

1. В логе за рабочий день нет `A command is already in progress` и `A second operation was started on this context`.
2. `concurrency-isolation` (BIS.ERP.TESTS) проходит, включая новые сценарии этапа 5.
3. Две вкладки грузятся одновременно (два клика по дереву подряд), третья открывается — без ошибок и «залипших» соединений.
4. Сценарий «исключение в середине загрузки → следующее действие» — следующее действие проходит (проверяет этап 1).
5. Переключение режима «Рабочий ↔ Конфигуратор» не запускает вторую цепочку миграций (в логе — одна запись о миграциях/посадках).
6. `pg_stat_activity` во время бездействия: соединений не больше, чем было до правок (контексты закрываются, пул не растёт).

## 7. Подводные камни

| Риск | Что учесть |
|---|---|
| EF change tracking | Контекст на действие означает, что сущность, сохранённая в поле окна, «отвязана» от нового контекста. Сохранять по `Id` и перечитывать, а не `Update(entity)` из другого контекста |
| Транзакции | Транзакция и все её команды — на одном контексте/соединении. Нельзя разнести по действиям |
| Производительность на VPN | Новое соединение — это `new AppDbContext(connectionString)` (пул Npgsql переиспользует физические). Не открывать контекст на каждый элемент цикла: одно действие — один контекст |
| Смена инфобазы | Контекст брать через `ServiceLocator.DbContextFactory.CreateAsync()` (смотрит на текущую инфобазу), а не из строки, запомненной при открытии окна |
| Долгие операции | Отчёты/экспорт: отдельный контекст + `CancellationToken`, чтобы закрытие вкладки не оставляло запрос |
| Логи/аудит | `SystemLogService` пишет в файл — его можно звать из любого контекста, в том числе внутри транзакции |
| «Тихие» `catch` | В `MetadataService` есть `catch`, которые глотают ошибки (489, 1987) — при переносе на `finally` их нужно заменить на «лог + проброс» или «лог + осмысленный fallback» |

## 8. Оценка

| Этап | Объём | Что даёт |
|---|---|---|
| 0. Каркас | ~0.5 дня | единая точка создания контекста, helper-ы |
| 1. Сервисы (`finally`) | ~1 день | исчезают «залипшие» соединения — самая частая причина каскадных падений |
| 2. Окна на своё подключение | ~2–3 дня | окна перестают мешать друг другу |
| 3. Повторный вход | ~1–2 дня | нет мигания и лишних запросов, поиск/выбор не «догоняют» друг друга |
| 4. Старт и режимы | ~1 день | нет двойных миграций при старте и смене режима |
| 5. Уборка и тесты | ~0.5–1 день | `DbContextGate` уходит из горячего пути, регрессии ловятся тестом |
| **Итого** | **~6–8 дней** | приложение перестаёт зависеть от одного общего контекста |
