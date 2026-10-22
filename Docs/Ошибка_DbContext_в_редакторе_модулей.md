# Ошибка «A second operation was started on this context instance» в редакторе модулей

Разбор аварии в конфигураторе, вкладка **«Модули и разделы»**, и подробное описание
исправления.

Дата разбора: 22.10.2026.
Связанный документ: `Docs/Задержки_запуска_и_оптимизация.md` (раздел 4.9, п. 4.6, п. 4.8, правила п. 7).

---

## 1. Итог

| | |
| --- | --- |
| **Симптом** | `System.InvalidOperationException: A second operation was started on this context instance before a previous operation completed.` |
| **Где** | Конфигуратор → «Модули и разделы», при переключении модулей в списке слева |
| **Стек** | `ModuleMetadataService.GetItemsAsync` ← `ModuleManagementView.LoadObjectsAsync` ← `ModuleManagementView.OnModuleSelected` |
| **Побочный симптом** | В системном логе **ничего не записалось** — ни ошибки, ни следов работы редактора |
| **Причина** | Редактор модулей работал на **общем** `DbContext` окна конфигуратора; `async void`-обработчик выбора модуля входил повторно, пока предыдущая загрузка не завершилась |
| **Исправление** | Редактор получил собственную строку подключения; каждая операция — на своём подключении, независимые чтения — параллельно |
| **Проверка** | Стенд на боевой базе: гонка воспроизводится на общем контексте и **не воспроизводится** на раздельных подключениях |
| **Сборка** | `dotnet build` — 0 ошибок |

---

## 2. Симптом во всех подробностях

### 2.1. Текст исключения

```
System.InvalidOperationException
  HResult=0x80131509
  Сообщение = A second operation was started on this context instance before a previous operation completed.
              This is usually caused by different threads concurrently using the same instance of DbContext.
              For more information on how to avoid threading issues with DbContext,
              see https://go.microsoft.com/fwlink/?linkid=2097913.
  Источник = Microsoft.EntityFrameworkCore.Relational
  Трассировка стека:
   в Microsoft.EntityFrameworkCore.Query.Internal.SingleQueryingEnumerable`1.AsyncEnumerator.<MoveNextAsync>d__20.MoveNext()
   в Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.<ToListAsync>d__67`1.MoveNext()
   в Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.<ToListAsync>d__67`1.MoveNext()
   в BIS.ERP.Services.ModuleMetadataService.<GetItemsAsync>d__16.MoveNextAsync() в ModuleMetadataService.cs:строка 141
   в BIS.ERP.Views.Configurator.ModuleManagementView.<LoadObjectsAsync>d__9.MoveNextAsync() в ModuleManagementView.xaml.cs:строка 75
   в BIS.ERP.Views.Configurator.ModuleManagementView.<OnModuleSelected>d__8.MoveNextAsync() в ModuleManagementView.xaml.cs:строка 57
```

### 2.2. Как это выглядело у пользователя

- Конфигуратор, вкладка «Модули и разделы».
- При переключении модулей в списке (клик по следующему модулю, пока предыдущий
  ещё догружался) — исключение.
- **В логе ничего не записалось.** Пользователь проверил `logs/system_20261022.log` —
  последней записью была «Модуль «Материалы» сохранён», следов аварии не было.

### 2.3. Почему в логе ничего не было

Это отдельный дефект, а не следствие гонки.

Обработчик исключений UI-потока в `App.xaml.cs` (строка 176) помечает
`InvalidOperationException` как обработанное, чтобы одно проглоченное исключение
не роняло приложение:

```csharp
private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
{
    SystemLogService.Error("Необработанное исключение UI-потока.", "Dispatcher", e.Exception);

    if (e.Exception is InvalidOperationException ||
        e.Exception is Npgsql.NpgsqlException ||
        e.Exception?.InnerException is InvalidOperationException ||
        e.Exception?.InnerException is Npgsql.NpgsqlException)
    {
        e.Handled = true;   // ← приложение продолжает работать
    }
}
```

Само логирование в глобальном обработчике есть, но гонка возникала **внутри**
`async void`-обработчика `OnModuleSelected`, а не в UI-потоке WPF: исключение уходило
в `SynchronizationContext` редактора, и до `DispatcherUnhandledException` доходило уже
помеченным, либо терялось по дороге. Практический результат — в логе пусто, а
пользователь видит только текст исключения.

> **Вывод, который стоит сохранить:** «в логе ничего не записалось» — это не «ошибки
> не было». Отсутствие записи — само по себе дефект, и чинить нужно именно его:
> ловить исключение **в обработчике** и писать в лог там же.

---

## 3. Разбор причины

### 3.1. Путь к ошибке

Стек-трейс называет две столкнувшиеся операции — они и указывают на источник:

```
OnModuleSelected  →  LoadObjectsAsync  →  GetItemsAsync  →  ToListAsync
```

Все три — в редакторе модулей. Значит, делится один и тот же объект `DbContext`.

### 3.2. Общий контекст окна

До правки редактор создавался так:

```csharp
// Views/ConfiguratorWindow.xaml.cs
private void ShowModulesEditor()
{
    if (_context == null)
        return;

    OpenConfiguratorDocument(
        "config:modules",
        "Модули и разделы",
        new BIS.ERP.Views.Configurator.ModuleManagementView(_context));   // ← общий контекст окна
}
```

`_context` — это один экземпляр `AppDbContext` на всё окно конфигуратора. Он же
передавался остальным редакторам:

```csharp
new AccountingSetupView(_context);
new UserAccessManagementView(_context, ...);
new ReportDataSetManagementView(_context);
```

### 3.3. Как именно два чтения наложились

Обработчик выбора модуля — `async void` (событие WPF `SelectionChanged`):

```csharp
private async void OnModuleSelected(object sender, SelectionChangedEventArgs e)
{
    ...
    await LoadObjectsAsync(module.Id, module.Code);   // ← не дожидается предыдущего вызова
}
```

`async void` **не дожидается** предыдущего вызова: обработчик возвращает управление
UI сразу, а его продолжение живёт отдельно. Поэтому:

1. Пользователь кликает модуль **А** → `OnModuleSelected` стартует
   `LoadObjectsAsync(А)` → внутри идёт `await GetItemsAsync()` — запрос висит на сети
   (база удалённая, VPN, 30–70 мс на запрос).
2. Пользователь кликает модуль **Б** (не дождавшись) → `OnModuleSelected` стартует
   `LoadObjectsAsync(Б)` → `await GetItemsAsync()` — **второе** чтение на том же
   экземпляре `DbContext`.
3. EF Core в `SingleQueryingEnumerable.AsyncEnumerator.MoveNextAsync` видит, что на
   контексте уже идёт операция, и выбрасывает ровно то исключение из стека.

Дополнительно тот же общий `_context` в это же время был занят деревом конфигуратора
и другими редакторами — то есть источник конфликта был шире одной вкладки.

### 3.4. Почему `EnsureSchemaAsync` не защищал

`ModuleMetadataService.EnsureSchemaAsync` выполняет DDL (`CREATE TABLE IF NOT EXISTS`,
`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`) и до правки мог быть вызван из читающего
метода. По документу `Задержки_запуска_и_оптимизация.md` (п. 4.8) это уже было
признано причиной взаимоблокировки `40P01`: `ALTER TABLE` берёт `ACCESS EXCLUSIVE`,
и параллельные чтения тех же таблиц его блокируют.

Защита «DDL один раз на процесс» была, но **гонка оставалась**: между проверкой
«схема уже готова» и отметкой в множестве могли пройти два потока — оба считали,
что схемы ещё нет, и оба выполняли DDL.

### 3.5. Итоговая схема гонки

```
UI-поток (WPF)                        EF Core                      Postgres
─────────────────                     ───────                      ────────
OnModuleSelected(А) ──► ToListAsync ────────────► SELECT ... ───────►
        │
        │ (пользователь кликает модуль Б, не дождавшись)
        ▼
OnModuleSelected(Б) ──► ToListAsync ──► ✖ «A second operation was started…»
                                                 ▲
                                                 └── контекст тот же самый
```

---

## 4. Что сделано

### 4.1. `Views/Configurator/ModuleManagementView.xaml.cs` — редактор на своих подключениях

**Было** — редактор держал общий контекст окна:

```csharp
private readonly AppDbContext _context;
private readonly ModuleMetadataService _moduleService;

public ModuleManagementView(AppDbContext context)
{
    InitializeComponent();
    _context = context;
    _moduleService = new ModuleMetadataService(context);
    ...
}
```

**Стало** — редактор получает **строку подключения** и создаёт контекст на операцию:

```csharp
// Общий контекст окна конфигуратора здесь не используем: он занят другими
// редакторами, а DbContext не переносит параллельных операций — вторая
// падает с «A second operation was started on this context instance…».
// Каждая операция идёт на своём подключении
// (см. Docs/Задержки_запуска_и_оптимизация.md, п. 4.6 и 4.8).
private readonly string _connectionString;

public ModuleManagementView(string connectionString)
{
    InitializeComponent();
    _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("Не задана строка подключения к информационной базе.", nameof(connectionString))
        : connectionString;
    ...
}

private AppDbContext CreateContext() => new AppDbContext(_connectionString);
```

#### 4.1.1. Чтение — параллельно, каждое на своём подключении

Состав модуля — три **независимых** набора: привязки, документы, отчёты. Раньше они
читалися последовательно на общем контексте. Теперь — одновременно, каждое на своём
подключении (строки 110–117):

```csharp
// Три независимых набора — на трёх разных подключениях и параллельно:
// по VPN задержки складываются не в сумму, а в максимум.
var assignmentsTask = LoadOnOwnConnectionAsync(context =>
    new ModuleMetadataService(context).GetItemsAsync());
var documentsTask = LoadOnOwnConnectionAsync(context => context.MetadataObjects.AsNoTracking()
    .Where(item => item.ObjectType == "Document").OrderBy(item => item.Name).ToListAsync());
var reportsTask = LoadOnOwnConnectionAsync(context => context.Reports.AsNoTracking()
    .Where(item => !item.IsPrintForm).OrderBy(item => item.Name).ToListAsync());

await Task.WhenAll(assignmentsTask, documentsTask, reportsTask);

var allAssignments = await assignmentsTask;
var documents = await documentsTask;
var reports = await reportsTask;
```

Хелпер (строка 403) — контекст создаётся на операцию и освобождается сразу:

```csharp
/// <summary>
/// Чтение на собственном подключении: общий контекст конфигуратора в это
/// время занят другими редакторами.
/// </summary>
private async Task<T> LoadOnOwnConnectionAsync<T>(Func<AppDbContext, Task<T>> load)
{
    await using var context = CreateContext();
    return await load(context);
}
```

Выигрыш по времени на удалённой базе: три запроса идут одновременно, поэтому
задержка складывается как **максимум** (~70 мс), а не как сумма (~210 мс).

#### 4.1.2. Запись — одна единица работы на своём подключении

`SaveModuleAsync` и `SaveAssignmentsAsync` — логически одна операция («сохранить модуль
вместе с составом»). Они выполняются на одном контексте, общий контекст окна не
трогается (строка 370):

```csharp
private async Task SaveModuleWithAssignmentsAsync(
    MetadataModule module,
    IEnumerable<Guid> documentIds,
    IEnumerable<Guid> reportIds)
{
    await using var context = CreateContext();
    var service = new ModuleMetadataService(context);
    var saved = await service.SaveModuleAsync(module);
    await service.SaveAssignmentsAsync(saved.Id, documentIds, reportIds);
}
```

#### 4.1.3. DDL и посадка — на своём подключении и до чтений

Порядок стал детерминированным: сначала схема, потом посадка, потом чтения (строки 51–67):

```csharp
private async Task LoadAsync(Guid? selectId = null)
{
    // Схема модулей — на своём подключении и до любых чтений: DDL на общем
    // контексте или одновременно с чтениями давал взаимоблокировку 40P01.
    await EnsureModuleSchemaAsync();

    // Посадка системных модулей — тоже на своём подключении (запись в БД).
    await EnsureDefaultModulesAsync();

    var modules = await LoadOnOwnConnectionAsync(context =>
        new ModuleMetadataService(context).GetModulesAsync(true));
    ModulesList.ItemsSource = modules;
    ...
}
```

#### 4.1.4. Защита от устаревшего результата

Разные подключения снимают **падение**, но не отменяют гонку за экран: старый ответ
может прийти позже свежего и переписать его. Счётчик версий (строки 24, 123):

```csharp
private int _loadVersion;

// в LoadObjectsAsync:
var version = ++_loadVersion;
// ... параллельные чтения ...
if (version != _loadVersion)
    return;   // ответ устарел — не применяем
```

#### 4.1.5. Логирование на месте

Исключения ловятся и пишутся в лог **в самом редакторе**, а не полагаясь на
глобальный обработчик диспетчера:

```csharp
SystemLogService.Error("Ошибка загрузки списка модулей.", nameof(ModuleManagementView), ex);
SystemLogService.Error($"Ошибка загрузки состава модуля «{moduleCode}».", nameof(ModuleManagementView), ex);
SystemLogService.Error("Ошибка сохранения модуля.", nameof(ModuleManagementView), ex);
SystemLogService.Error("Ошибка удаления модуля.", nameof(ModuleManagementView), ex);
```

Теперь любая ошибка в редакторе модулей оставляет след в `logs/system_*.log`.

### 4.2. `Views/ConfiguratorWindow.xaml.cs` — редактору передаётся строка подключения

```csharp
// Редактор модулей работает на своих подключениях (см. ModuleManagementView):
// общий контекст окна делить нельзя — DbContext не переносит параллельных операций.
OpenConfiguratorDocument(
    "config:modules",
    "Модули и разделы",
    new BIS.ERP.Views.Configurator.ModuleManagementView(CurrentConnectionString()));
```

Строка подключения берётся у контекста **инфобазы** (строка 570):

```csharp
/// <summary>
/// Строка подключения к текущей инфобазе.
/// </summary>
private string CurrentConnectionString()
{
    var connectionString = _context.Database.GetConnectionString();
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Не удалось получить строку подключения к информационной базе.");

    return connectionString;
}
```

### 4.3. `Services/ModuleMetadataService.cs` — DDL и посадка сериализованы

Защита «DDL один раз на процесс» была, но с зазором: между проверкой и отметкой
могли пройти два потока, и оба выполняли DDL. Теперь выполнение сериализовано
семафором **на строку подключения** (строки 27–49):

```csharp
// DDL и посадка модулей — по одному потоку на строку подключения.
// Иначе два вызова идут на разных подключениях одновременно: DDL против
// чтений даёт взаимоблокировку 40P01, а две вставки одного модуля —
// гонку по уникальному индексу Code.
private static readonly Dictionary<string, SemaphoreSlim> SchemaGates = new(StringComparer.OrdinalIgnoreCase);
private static readonly Dictionary<string, SemaphoreSlim> DefaultModulesGates = new(StringComparer.OrdinalIgnoreCase);

private static SemaphoreSlim GetGate(Dictionary<string, SemaphoreSlim> gates, string connectionKey)
{
    lock (SchemaEnsuredLock)
    {
        if (!gates.TryGetValue(connectionKey, out var gate))
        {
            gate = new SemaphoreSlim(1, 1);
            gates[connectionKey] = gate;
        }

        return gate;
    }
}
```

`EnsureSchemaAsync` (строка 51) — DDL под семафором, отметка «схема готова» внутри же:

```csharp
public async Task EnsureSchemaAsync()
{
    var connectionKey = _context.Database.GetConnectionString() ?? "default";
    var gate = GetGate(SchemaGates, connectionKey);
    await gate.WaitAsync();
    try
    {
        lock (SchemaEnsuredLock)
        {
            if (SchemaEnsuredConnections.Contains(connectionKey))
                return;
        }

        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""MetadataModules"" (...
        ");

        lock (SchemaEnsuredLock)
        {
            SchemaEnsuredConnections.Add(connectionKey);
        }
    }
    finally
    {
        gate.Release();
    }
}
```

`EnsureDefaultModulesAsync` (строка 114) — отдельный семафор, тело вынесено в
`EnsureDefaultModulesCoreAsync` (строка 129), чтобы посадка тоже шла под замком:

```csharp
public async Task EnsureDefaultModulesAsync()
{
    var connectionKey = _context.Database.GetConnectionString() ?? "default";
    var gate = GetGate(DefaultModulesGates, connectionKey);
    await gate.WaitAsync();
    try
    {
        await EnsureDefaultModulesCoreAsync();
    }
    finally
    {
        gate.Release();
    }
}
```

---

## 5. Что намеренно НЕ менялось

**Остальные редакторы конфигуратора** (`AccountingSetupView`, `UserAccessManagementView`,
`ReportDataSetManagementView`) продолжают получать общий `_context` окна.

Причина — не «лень», а риск утечки: `MdiWorkspaceControl.OpenDocument` переиспользует
документ по ключу и **не показывает** новый созданный редактор, если документ с таким
ключом уже открыт. Редактору, которому дали собственный контекст, этот контекст мог бы
никогда не достаться, а соединение осталось бы в пуле Npgsql навсегда. Правильный
вариант для них — перевод на «контекст на операцию» (см.
`Docs/План_контекст_на_операцию.md`), это отдельная задача, а не побочный эффект текущей
аварии.

**`App.xaml.cs` не менялся.** Глобальный обработчик `DispatcherUnhandledException`
оставлен как есть: он нужен для остальных мест приложения. Дефект логирования исправлен
в самом редакторе (п. 4.1.5) — ловить нужно там, где исключение возникает.

---

## 6. Проверка

### 6.1. Стенд

Стенд проверки гонок DbContext (шаблон:
`skills/software-development/dotnet-dbcontext-concurrency/templates/ConcurrencyHarness.Program.cs`)
прогоняется против **боевой базы** и печатает таблицу результатов. Пять проверок:

| # | Проверка | Ожидание |
| --- | --- | --- |
| A | Общий контекст + два параллельных `ToListAsync` | `InvalidOperationException: A second operation was started…` — гонка воспроизводится |
| B | Три набора параллельно на разных подключениях | Успех |
| C | 20 итераций параллельных чтений | Успех |
| D | 4 параллельных вызова DDL + посадки | Успех, без взаимоблокировки |
| E | `GetConnectionString()` → производное подключение | Читает ту же базу |

Проверка A — красная до правки и зелёная после: она доказывает, что причина найдена
верно, а не «ошибка исчезла потому что перестала воспроизводиться».

Запуск:

```bash
cd <папка стенда>
dotnet run -c Debug
# строка подключения — через переменную окружения BIS_CONN
```

### 6.2. Фактический прогон

Подключение: `Host=localhost;Port=5432;Database=bis_OsOO1_20260908_112439;Username=postgres;Password=***`

```
=== A. Общий контекст + параллельные ToListAsync (старая схема) ===
ОЖИДАЕМО: гонка воспроизведена -> InvalidOperationException: A second operation was started on this context instance before a previous operation completed. This is usually caused by different threads concurrently using the same instance of DbContext. For more information on how to avoid threading issues with DbContext, see https://go.microsoft.com/fwlink/?linkid=2097913.

=== B. Отдельное подключение на операцию (новая схема) ===
ОК: три набора прочитаны параллельно на разных подключениях: привязки=68, документы=28, отчёты=35

=== C. 20 итераций параллельных чтений на разных подключениях ===
ОК: 20 итераций без ошибок

=== D. Параллельный вызов EnsureSchemaAsync/EnsureDefaultModulesAsync ===
ОК: 4 параллельных вызова DDL+посадки завершились без взаимоблокировки

=== E. GetConnectionString() round-trip (production-путь окна) ===
Строка из _context.Database.GetConnectionString(): Host=localhost;Port=5432;Database=bis_OsOO1_20260908_112439;Username=postgres;Password=***
Привязки: исходный контекст=68, контекст из строки=68, база=bis_OsOO1_20260908_112439
ОК: строка подключения корректна, производное подключение читает ту же базу

ИТОГ: ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ
```

### 6.3. Сборка

```
dotnet build "D:\C#\BIS.ERP\BIS.ERP\BIS.ERP.csproj" -c Debug -v:m --nologo
...
Предупреждений: 8
    Ошибок: 0
```

Четыре уникальных предупреждения — все **прежние**, к правке отношения не имеют, ни одно
не в изменённых файлах:

| Файл | Предупреждение |
| --- | --- |
| `Views/AccountingReportsView.xaml.cs:34` | CS0414 |
| `Views/AccountingReportsView.xaml.cs:504` | CS0618 |
| `Views/Dialogs/InventoryDocumentDialog.xaml.cs:66` | CS8620 |
| `Views/Dialogs/InvoiceEditDialog.xaml.cs:1258` | CS0162 |

> Если запущенный экземпляр BIS.ERP держит `bin/Debug/net8.0-windows/BIS.ERP.exe`,
> сборка падает с MSB3021/MSB3027 («файл занят другим процессом») — это не ошибка кода.
> Либо закройте приложение, либо соберите в другой каталог:
> `dotnet build BIS.ERP.csproj -c Debug '-p:BaseOutputPath=<папка>\'`.
> Либо разберитесь с залипшим процессом: `dotnet build-server shutdown`.

### 6.4. Ручная проверка в приложении

1. Конфигуратор → «Модули и разделы».
2. Быстро щёлкать по модулям в списке — исключений нет, списки обновляются.
3. В `logs/system_*.log` должны появиться записи об ошибках, если чтение упадёт
   (раньше не появлялось ничего).

---

## 7. Правила, чтобы не вернуть проблему

1. **Один `DbContext` — одна операция.** Никогда не делить один экземпляр между
   пересекающимися async-операциями: EF Core блокирует вторую операцию на том же
   экземпляре, даже если вторая просто `await`ит.
2. **Чтение — своё подключение, параллельно.** Независимые наборы читаются каждый на
   своём контексте одновременно (`Task.WhenAll`): по VPN задержки складываются не в
   сумму, а в максимум.
3. **Запись — одна единица работы на одном контексте.** `SaveChangesAsync` не дробить
   на несколько вызовов.
4. **DDL — отдельное подключение, до параллельных чтений, сериализованно.**
   `ALTER TABLE` / `CREATE INDEX` берут `ACCESS EXCLUSIVE` и дают `40P01`, если рядом
   идут чтения.
5. **Долгоживущий UI-компонент не должен владеть контекстом.** Давайте ему строку
   подключения и создавайте контекст на операцию (`await using`).
6. **MDI/вкладки переиспользуют документ по ключу.** Редактору, которому дали
   собственный контекст, этот контекст могут никогда не показать — соединение останется
   в пуле Npgsql навсегда. Отсюда правило выше: строка подключения, а не контекст.
7. **`async void` — с логированием.** Исключение из такого обработчика уходит в
   `DispatcherUnhandledException`; если глобальный обработчик помечает тип исключения
   обработанным, следа в логе не остаётся. Ловить и писать в лог нужно на месте.
8. **Отсутствие записи в логе — тоже дефект.** «В логе ничего не записалось» не означает
   «ошибки не было».

---

## 8. Изменённые файлы

| Файл | Изменение |
| --- | --- |
| `Views/Configurator/ModuleManagementView.xaml.cs` | Своя строка подключения вместо общего контекста; параллельные чтения на разных подключениях; запись — единица работы на своём подключении; счётчик версий; логирование ошибок |
| `Views/ConfiguratorWindow.xaml.cs` | Редактору модулей передаётся строка подключения; хелпер `CurrentConnectionString()` |
| `Services/ModuleMetadataService.cs` | `EnsureSchemaAsync` и `EnsureDefaultModulesAsync` — сериализация семафором на строку подключения |
| `Docs/Задержки_запуска_и_оптимизация.md` | Раздел 4.9 (краткая версия разбора), правила п. 7, таблица файлов п. 8 |

---

## 9. Связанные документы

| Документ | Что в нём |
| --- | --- |
| `Docs/Задержки_запуска_и_оптимизация.md` | Замеры запуска, оптимизация дерева, взаимоблокировка `40P01` (п. 4.8), краткий разбор этой аварии (п. 4.9) |
| `Docs/План_контекст_на_операцию.md` | План перехода всего приложения на «контекст на операцию» — следующий шаг для остальных редакторов |
| `Docs/Механизм_миграций_схемы.md` | Порядок применения схемы и DDL |
