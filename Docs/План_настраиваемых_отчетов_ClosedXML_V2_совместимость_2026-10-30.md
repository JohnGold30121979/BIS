# План настраиваемых отчётов ClosedXML V2 и совместимого переноса

Дата: 30.10.2026. Статус: **оценка и проект, реализация не выполнялась**.

В рамках этой задачи создан только данный документ. Код приложения, существующие отчёты, макеты, SQL-наборы и база данных не изменяются. Примеры ниже — предлагаемый код для последующей реализации: он не добавлен в сборку, не компилировался и не выполнялся в PostgreSQL.

## 1. Вывод: можно ли перевести существующие отчёты без поломки

**Да, возможен постепенный перенос с сохранением существующих отчётов. Гарантировать отсутствие ошибок заранее нельзя.** Совместимость должна подтверждаться для каждого отчёта сравнением данных, сумм и печатного результата.

Не требуется заменять ClosedXML или менять формат XLSX/PDF. Под «V2» здесь понимается новая версия **настраиваемого исполнения отчёта**, а не обязательная новая версия JSON-макета. Первый этап использует существующий `ClosedXmlReportLayout.Version = 1` и текущие рендереры.

Основное правило перехода:

> Отсутствует явная включённая настройка V2 — отчёт исполняется прежним путём. Само обновление программы не переводит существующие отчёты автоматически.

Первый пилот создаётся как отдельная копия ведомости по паре счетов. Исходная ведомость остаётся доступной. SQL-наборы также копируются: изменение общего источника не должно затронуть другие отчёты.

| Категория существующих отчётов | Возможность переноса | Что требуется проверить |
| --- | --- | --- |
| Уже использующие SQL-набор и ClosedXML | Наиболее простой перенос | Типы параметров, поля источника, формат и порядок строк |
| Использующие системный расчёт | Можно подключить через V2 без изменения расчёта | Системный расчёт при этом остаётся C#-кодом; это ещё не устранение всех фиксированных правил |
| Акт сверки, ведомость по счёту/паре со специальным построителем | Можно поэтапно | Плоские служебные строки, начальные остатки, валюты, отдельная маршрутизация окна |
| Форматные бланки: счета-фактуры, кассовые формы | Возможен перенос источника с сохранением макета | Объединения, нумерация граф, реквизиты, подписи, высоты, переходы страниц |
| Пользовательские импортированные FRX | Не включать в эту миграцию | Это отдельный формат, не существующий ClosedXML-макет |

Не следует обещать автоматический массовый конвертер «для всех отчётов одной кнопкой» без инвентаризации. Особую опасность представляют пользовательские изменения макетов и отчёты, разделяющие один набор данных.

## 2. Что уже есть в проекте

Оценка выполнена по исходникам текущего проекта, без повторного запуска миграций или чтения рабочих отчётов из базы.

| Возможность | Реальный код | Состояние |
| --- | --- | --- |
| SQL- и системные наборы данных | `Models/ReportDataSet.cs`, `Services/ReportDataSetService.cs` | Есть `Kind`, `SqlText`, `CalculatorCode`, `ParameterDefinitions`, поля набора |
| Связь набора с источником метаданных | `ReportDataSetService.GetBySourceAsync`, `SyncMetadataSourceAsync` | `MetadataObject.ReferenceFields` содержит `ReportDataSetCode` |
| Общий путь получения данных | `ReportService.GetReportDataAsync` | Умеет выполнять выбранный SQL-набор; не все окна формирования используют этот путь |
| Редактор источников | `Views/ReportDataSetManagementView.xaml` | SQL, выбор системного расчёта, обновление полей, тест запроса |
| Параметры отчёта | `Report.Parameters`, вкладка конструктора | Есть стандартные параметры; пользовательские значения можно передавать словарём |
| Макет | `Report.ClosedXmlLayout` | JSON хранится в базе и исполняется общим рендерером |
| Группировка и суммы | `ClosedXmlGroup`, `ClosedXmlTotal`, оба рендерера | Есть группы, один `Subtotal` группы, `GrandTotal`, дополнительные общие итоги |
| Оформление | `ClosedXmlColumn`, текстовые строки, уровни шапки, правила выделения | Уже настраивается в макете |
| Специальные отчёты | `SystemReportCalculators` | Реестр готовых C#-расчётов |
| Ведомость по паре | `AccountPairTurnoverBuilder`, `AccountingReportsView.AccountPair.cs` | Данные и служебные строки строятся отдельным кодом |

### Существенные ограничения существующей основы

1. `AccountingReportsView.BuildSelectedAccountingReportAsync` выбирает специальный построитель по типу отчёта. Для ведомости по паре общий механизм источников сейчас обходится.
2. Даже общий `BuildClosedXmlReportByCodeAsync` определяет набор через `ResolveDataSetCodeForReport(reportType)`, а не всегда по источнику, выбранному в самом отчёте.
3. `ReportDataSetService` умеет передавать дополнительные параметры из словаря, но окно бухгалтерских отчётов формирует ограниченный фиксированный набор. Произвольный параметр сам по себе не создаёт нужный контрол в этом окне.
4. `ReportDataSetService.AddSqlParameters` ищет параметры строковым `Contains`. Это не полноценный разбор SQL-параметров и не типизированный контракт.
5. В модели поля набора нет универсального визуального редактора бухгалтерских формул. Наличие `ElementExpression` у привязки макета не означает, что есть готовый универсальный расчёт сальдо.
6. Один `ClosedXmlGroup.Subtotal` не описывает четыре произвольные итоговые строки группы с начальными/конечными остатками и знаками. `ExtraTotals` относятся к разделу, а не являются полноценными строками подвала каждой группы.
7. Сейчас наши `ИТОГО`, `САЛЬДО`, `ОБОРОТЫ` приходят как строки данных. Если одновременно включить суммы в рендерере по всем этим строкам, получится двойной счёт.
8. Имена/типы/порядок колонок являются контрактом макета. Просто заменить источник на похожий SQL недостаточно.
9. Значения `TakeFromRow = 1`, `2`, `-1`, служебные колонки подписей и подстановки шапки нужно сохранить. В старом акте удаление первой/последней служебной строки может сломать макет при верных суммах.
10. Общие методы получения/посадки источников вызывают `EnsureSchemaAsync`. Для полностью read-only исполнения нужно отделить подготовку схемы от рабочего чтения.

## 3. Целевая архитектура

```text
Окно бухгалтерских отчётов / предпросмотр конструктора / экспорт
                         |
                 ReportExecutionRouter
                         |
             Нет включённой V2? → прежний путь
                         |
                         V
       Определение исполнения + выбранный источник метаданных
                         |
       Типизированные параметры и выбор из справочников
                         |
         ReportDataSetService: SQL / системный расчёт
                         |
        Проверка типов, полей, порядка и контракта данных
                         |
       Совместимый проектор полей / служебных строк (если нужен)
                         |
             Существующий ClosedXML-макет V1
                         |
                 Сетка / Excel / PDF
```

Новый механизм должен быть единым для окна отчётов, предпросмотра конструктора, «Тест» набора данных и экспорта. Нельзя исправить только кнопку «Сформировать»: иначе один отчёт будет считать по-разному в разных местах.

### Что будет настраиваемым

- источник данных и SQL;
- поля, типы, алиасы, подписи;
- параметры периода, организации, пары, режима сравнения счетов;
- справочник и поля отображения селектора;
- точное/префиксное совпадение счетов;
- формулы, реализованные SQL-запросом;
- заголовок, подписи итогов и формат дат;
- группировка и сортировка, если они представлены поддерживаемыми настройками;
- формат, порядок, ширина колонок, страницы, шапки, подвал;
- после отдельного расширения — несколько настраиваемых строк подвала группы.

### Что остаётся программным механизмом

Исполнитель SQL, привязка параметров, проверка контракта, сериализация, рендерер, безопасный вычислитель поддерживаемых выражений, работа селекторов и режим совместимости. Устранение фиксированных правил конкретной ведомости не означает, что сам движок перестанет быть C#-программой.

## 4. Принцип совместимости и хранения настроек

Рекомендация: не перегружать `ClosedXmlReportLayout.Version` и не перезаписывать весь `Reports.Settings`. Для включения новой схемы исполнения добавить отдельную необязательную запись. Источник по-прежнему выбирается через существующий `Reports.DataSourceId` и связь `ReportSource → ReportDataSetCode`.

Новая таблица не дублирует SQL и макет. Она хранит режим исполнения, определения параметров, контракт и состояние публикации.

### Проект DDL, не выполнять на этом этапе

```sql
CREATE TABLE "ReportExecutionBindings" (
    "ReportId" uuid PRIMARY KEY
        REFERENCES "Reports"("Id") ON DELETE CASCADE,
    "RuntimeVersion" integer NOT NULL DEFAULT 2,
    "State" varchar(20) NOT NULL DEFAULT 'Draft',
    "ContractVersion" integer NOT NULL DEFAULT 1,
    "DefinitionJson" text NOT NULL DEFAULT '{}',
    "ValidatedFingerprint" varchar(64) NOT NULL DEFAULT '',
    "Revision" uuid NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "CK_ReportExecutionBindings_Runtime" CHECK ("RuntimeVersion" = 2),
    CONSTRAINT "CK_ReportExecutionBindings_State"
        CHECK ("State" IN ('Draft', 'Enabled', 'Disabled'))
);

CREATE TABLE "ReportDefinitionSnapshots" (
    "Id" uuid PRIMARY KEY,
    "ReportId" uuid NOT NULL,
    "Reason" text NOT NULL,
    "SnapshotJson" text NOT NULL,
    "Sha256" varchar(64) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
);
```

Снимок должен включать сам `Report`, `Fields`, `Filters`, `Groups`, `Parameters`, `ElementMappings`, `HeadersFooters`, исходный текст макета, определение привязанного набора и его поля. В снимках не нужны пароли/строки подключения и копии проводок. Снимок хранится независимо от удаления исходного отчёта.

Прямой переход через новые свойства в `Report` тоже возможен, но отдельная таблица снижает риск конфликтов со старыми сериализаторами настроек. Окончательный выбор проверить по реальному сохранению конструктора и экспорту/импорту конфигураций.

## 5. Этапы внедрения

| Этап | Работы | Результат и условие завершения | Сложность |
| --- | --- | --- | --- |
| 0 | Инвентаризация отчётов/источников, снимки, контрольные выборки | Известны все потребители общих источников и пользовательские макеты | Средняя |
| 1 | Необязательное описание исполнения, маршрутизатор, режим Legacy | Отчёты без включённой V2 идут старым путём | Средняя |
| 2 | Типизированные параметры и справочные селекторы | Пара/организация/период одинаково передаются из всех окон | Средняя |
| 3 | Разрешение источника по DataSourceId, read-only исполнитель, контракт полей | Запрос и поля реально управляются сохранённым определением | Средняя |
| 4 | Копия ведомости по паре, SQL-набор, совместимые строки | Результат совпадает со старым построителем на контрольных данных | Высокая |
| 5 | Сравнение Excel/PDF, публикация пилота, откат | Оба варианта доступны, старый не изменён | Средняя |
| 6 | Редактор условий/подписей/параметров и единый предпросмотр | Изменение настройки отражается одинаково во всех местах | Средняя/высокая |
| 7 | При необходимости — несколько строк подвала группы | Текущий рендерер расширен с сохранением поведения старых макетов | Высокая, отдельный этап |
| 8 | Поочерёдный перенос остальных отчётов | Каждый прошёл свои проверки и имеет снимок/откат | Зависит от отчёта |

Работы не стоит объединять в одну массовую миграцию. Наиболее трудоёмкая часть — не редактор SQL, а эквивалентность бухгалтерских сумм, структурных строк, параметров и печатных форм.

### Этап 0. Инвентаризация и фиксация исходного результата

Для каждого отчёта собрать: Id, Code, ReportType, SourceFormat, DataSourceId, набор/тип набора, CalculatorCode, параметры, хеш макета, используемые поля, `TakeFromRow`, группировки, общие потребители источника, способ запуска. Классифицировать источники: SQL, системный расчёт, особый построитель, обычная таблица.

Проверять отдельно опубликованные и отключённые отчёты, пользовательские копии, форматы `Simple`/`Formatted`, PDF и Excel. Не опираться на количество отчётов из старой документации: получить актуальный список при реализации.

Снять результаты нескольких характерных периодов. Исключить из сравнения только явно непостоянные значения: дату печати/номер генерируемого файла. Порядок строк и суммы не исключать.

### Этап 1. Включаемый режим исполнения

Добавить модель, миграцию и маршрутизатор. Не создавать включённые привязки для всех старых отчётов. Извлечь текущую маршрутизацию специальных отчётов в Legacy-путь, не меняя её поведения.

Доступность таблицы режима нельзя проверять новым SQL с ошибкой на старой базе при каждом формировании. Схему готовит существующий механизм миграций; работа со старой неподготовленной базой должна явно оставлять Legacy-режим или сообщать о необходимости обновления согласно принятой политике.

### Этап 2. Параметры

Использовать существующие `ReportParameter` и `ParameterDefinitions`, добавив совместимое описание источника справочного выбора. Определить приоритет: введённое пользователем значение → сохранённое значение по умолчанию → отсутствие значения. Для обязательного параметра отсутствие — ошибка до SQL.

Пара выбирается по Id. Её счета разрешаются через метаданные справочников, а не путём передачи GUID вместо кода счёта. Проекция параметров должна поддерживать получение кода счёта из ссылки UUID. Имена/Id справочников берутся из определения, а не из отдельной ветки `if (AccountPairTurnover)`.

Режим «Все организации» передаётся одним согласованным способом. В V2 рекомендуется `OrganizationId = null` при `AllOrganizations = true`; пустая строка допускается только в явном Legacy-адаптере. Два противоречащих друг другу значения должны отклоняться.

Для дат определить бизнес-семантику: начало дня включено, конец последнего дня включён, технически используется исключающая верхняя граница. Не менять заодно часовой пояс хранения всех старых отчётов. Типы `date`, `timestamp`, `timestamptz` проверить по реальной схеме каждой базы.

### Этап 3. Исполнение и контракт

Набор выбирать через `DataSourceId → MetadataObject → ReportDataSetCode`. При явной V2-привязке отсутствующий источник — ошибка; подставлять `system.movements` по умолчанию нельзя.

Рабочее исполнение не вызывает посадку схемы. Проверку схемы и создание источников оставить миграциям/редактору. Пользовательский SQL исполнять в read-only транзакции, с тайм-аутом и отменой. Ограничение «SELECT/WITH» сохранить, но не считать текущий regex полной гарантией read-only.

Сначала проверить полный набор данных для макета, затем применять видимость колонок. Скрытые поля могут использоваться заголовками, подписями, фильтрами и группировками.

### Этап 4. Пилот ведомости по паре

Сначала сохранить текущую плоскую структуру: строки организаций, движений и итогов уже приходят данными. SQL может формировать те же строки, а их подписи — получать из настроек/параметров. Это позволяет убрать специальный C#-построитель без одновременной замены рендерера.

Затем, отдельно и только при необходимости, переходить на семантические строки (`RowKind`, ключи групп, числовые поля) и несколько настраиваемых строк подвала группы. Такое разделение уменьшает количество одновременно меняющихся механизмов.

Копия отчёта получает новый Code, новый ReportId и собственный SQL-набор. Исходный `ClosedXmlLayout` переносится побайтно. На первом этапе не переставлять графы, не менять подписи и не «исправлять» расчёт вместе с переносом.

## 6. Проект моделей и маршрутизатора

Следующий код описывает предлагаемые новые типы. Это не существующие классы проекта и не готовый патч. Реализации репозиториев, получения настроек и адаптеров нужно добавить на соответствующих этапах.

```csharp
public sealed record ReportExecutionDefinition(
    int RuntimeVersion,
    int ContractVersion,
    string State,
    IReadOnlyList<ReportParameterSpec> Parameters,
    IReadOnlyList<ReportColumnContract> Columns,
    IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyList<ReportSortSpec> Sort,
    string OutputMode); // например: LegacyFlatRows или SemanticRows

public sealed record ReportParameterSpec(
    string Name,
    string Title,
    string Type,
    bool Required,
    object? DefaultValue,
    Guid? ReferenceSourceId,
    string? ValueField,
    IReadOnlyList<string>? DisplayFields);

public sealed record ReportColumnContract(
    string Name, string Type, bool AllowNull, bool Required);

public sealed record ReportSortSpec(
    string Field, string Direction, string Comparison, string Nulls);

public sealed record ReportBuildResult(
    DataTable Data,
    Report EffectiveReport,
    string DefinitionFingerprint,
    IReadOnlyList<string> Diagnostics);

public interface IConfigurableReportRunner
{
    Task<ReportBuildResult> BuildAsync(
        Report report,
        ReportExecutionDefinition definition,
        IReadOnlyDictionary<string, object?> input,
        CancellationToken cancellationToken);
}

public interface IReportExecutionDefinitionStore
{
    // Отсутствие записи означает Legacy, а не автоматическое создание V2.
    Task<ReportExecutionDefinition?> FindAsync(Guid reportId, CancellationToken ct);
}

public sealed class ReportExecutionRouter
{
    private readonly IReportExecutionDefinitionStore definitions;
    private readonly IConfigurableReportRunner v2;

    public ReportExecutionRouter(
        IReportExecutionDefinitionStore definitions,
        IConfigurableReportRunner v2)
    {
        this.definitions = definitions;
        this.v2 = v2;
    }

    public async Task<ReportBuildResult> BuildAsync(
        Report report,
        IReadOnlyDictionary<string, object?> input,
        Func<CancellationToken, Task<ReportBuildResult>> buildLegacy,
        CancellationToken ct)
    {
        var definition = await definitions.FindAsync(report.Id, ct);
        if (definition == null || definition.State != "Enabled")
            return await buildLegacy(ct);

        if (definition.RuntimeVersion != 2)
            throw new InvalidOperationException("Неподдерживаемая версия исполнения отчёта.");

        // Ошибка V2 не маскируется молчаливым возвратом к другому расчёту.
        return await v2.BuildAsync(report, definition, input, ct);
    }
}
```

Legacy-callback должен вызывать существующую маршрутизацию только один раз. Не допустить рекурсии, когда общий `BuildSelectedAccountingReportAsync` вызывает маршрутизатор, а callback снова вызывает этот же метод.

### Разрешение источника: использовать имеющуюся связь

```csharp
// Предлагаемый метод для V2. GetBySourceReadOnlyAsync пока не существует:
// его нужно выделить из GetBySourceAsync без вызова EnsureSchemaAsync.
private async Task<ReportDataSet> ResolveDataSetAsync(Report report, CancellationToken ct)
{
    if (report.DataSourceId == null)
        throw new InvalidOperationException("У отчёта не выбран источник данных.");

    var source = await context.MetadataObjects.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == report.DataSourceId.Value, ct)
        ?? throw new InvalidOperationException("Источник отчёта не найден.");

    return await dataSets.GetBySourceReadOnlyAsync(source, ct)
        ?? throw new InvalidOperationException("Источник не связан с активным набором данных.");
}
```

Не менять существующий `GetBySourceAsync` сразу во всех местах: сначала добавить отдельный read-only путь V2, затем проверять возможность общего рефакторинга.

## 7. Пример определения параметров пилота

Вместо UUID-заглушек при посадке подставляются Id реальных метаданных конкретной базы. Имена новых свойств ещё требуют реализации редактора/сериализатора.

```json
{
  "runtimeVersion": 2,
  "contractVersion": 1,
  "state": "Draft",
  "outputMode": "LegacyFlatRows",
  "parameters": [
    { "name": "PeriodStart", "title": "С", "type": "Date", "required": true },
    { "name": "PeriodEnd", "title": "По", "type": "Date", "required": true },
    { "name": "AllOrganizations", "title": "Все организации", "type": "Bool", "defaultValue": true },
    { "name": "OrganizationId", "title": "Организация", "type": "Uuid", "required": false },
    { "name": "PairId", "title": "Пара счетов", "type": "Reference", "required": true },
    { "name": "MatchMode", "title": "Сравнение счетов", "type": "Enum", "defaultValue": "Prefix", "allowedValues": ["Exact", "Prefix"] }
  ],
  "referenceSelectors": {
    "PairId": {
      "sourceMetadataId": "<Id справочника пар этой базы>",
      "valueField": "Id",
      "displayFields": ["code", "name", "debit_account", "credit_account"],
      "allowCreate": true,
      "allowEdit": true,
      "activeOnly": true
    }
  },
  "referenceProjections": [
    { "from": "PairId", "field": "debit_account", "resolveReferenceField": "code", "to": "FirstAccount" },
    { "from": "PairId", "field": "credit_account", "resolveReferenceField": "code", "to": "SecondAccount" },
    { "from": "PairId", "field": "name", "to": "PairName" }
  ],
  "labels": {
    "Organization": "Организация:",
    "OrganizationTotal": "ИТОГО ПО ОРГАНИЗАЦИИ",
    "Opening": "САЛЬДО НА НАЧАЛО",
    "Turnover": "ОБОРОТЫ",
    "Closing": "САЛЬДО НА КОНЕЦ",
    "Empty": "Данных по выбранной паре счетов и организации за период нет."
  },
  "dateFormats": { "detail": "dd.MM.yy", "period": "dd.MM.yyyy" }
}
```

Секции `referenceSelectors`, `referenceProjections`, `labels`, `dateFormats` отсутствуют в текущем универсальном контракте: это проект расширения. Они должны быть типизированы, проверяться до исполнения и сохраняться редактором без потерь. Проекция ссылок должна читать `ReferenceCatalog`/Id справочника и разрешать UUID через его `Id → code`, а не предполагать, что строка UUID является кодом счёта.

## 8. Типизированное связывание параметров

Не подставлять значения через интерполяцию SQL. Имена таблиц и колонок — часть сохранённого SQL источника, значения — параметры. Разрешённые имена/типы параметров извлекаются из определения; отсутствующий обязательный параметр — ошибка до запроса.

```csharp
// Пример ядра привязки. Проверку Enum/Required/источника справочника
// выполнить отдельным валидатором до этого метода.
private static NpgsqlParameter Bind(
    string name, string type, object? value)
{
    var parameter = new NpgsqlParameter { ParameterName = name };
    switch (type)
    {
        case "Uuid":
            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid;
            parameter.Value = value == null ? DBNull.Value
                : value is Guid id ? id : Guid.Parse(value.ToString()!);
            break;
        case "Date":
            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date;
            parameter.Value = value == null ? DBNull.Value
                : value is DateOnly date ? date
                : DateOnly.FromDateTime((DateTime)value);
            break;
        case "Bool":
            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean;
            parameter.Value = value == null ? DBNull.Value : Convert.ToBoolean(value);
            break;
        case "Decimal":
            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Numeric;
            parameter.Value = value == null ? DBNull.Value : Convert.ToDecimal(value);
            break;
        case "Text":
        case "Enum":
            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
            parameter.Value = value == null ? DBNull.Value : value.ToString()!;
            break;
        default:
            throw new InvalidOperationException($"Неподдерживаемый тип: {type}");
    }
    return parameter;
}
```

В реальном коде различать пустую строку и null, не принимать культуры-зависимые строки дат/чисел без явного разбора и использовать `InvariantCulture` для сериализованных чисел. Тип `Reference` разрешается в реальный тип ключа до SQL. Старые системные расчёты получают прежний формат параметров через отдельный адаптер, а не глобальную замену пустых строк на null.

### Проект read-only исполнения

```csharp
private async Task<DataTable> ExecuteReadOnlyAsync(
    string sql, IReadOnlyList<NpgsqlParameter> parameters,
    CancellationToken ct)
{
    ValidateSingleSelect(sql); // существующую проверку усилить, не удалять
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(ct);
    await using var transaction = await connection.BeginTransactionAsync(ct);
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        await readOnly.ExecuteNonQueryAsync(ct);

    await using var command = new NpgsqlCommand(sql, connection, transaction)
    {
        CommandTimeout = 60
    };
    foreach (var parameter in parameters) command.Parameters.Add(parameter);
    await using var reader = await command.ExecuteReaderAsync(ct);
    var table = new DataTable();
    table.Load(reader);
    await reader.DisposeAsync();
    await transaction.CommitAsync(ct);
    return table;
}
```

Это проект ядра, а не замена текущего сервиса одним методом: дополнительно нужны ограничение результата предпросмотра, отмена длительного чтения, диагностика без паролей, выделение разрешённых параметров и отделение миграций от исполнения. При необходимости использовать отдельную роль БД с правами чтения. Нельзя рассчитывать, что один regex над SQL блокирует любые побочные эффекты пользовательских функций.

## 9. SQL-часть ведомости: проект расчётного ядра

Ниже — **заготовка ядра**, а не законченный SQL совместимого отчёта. Она показывает перенос правил в редактируемый источник. Перед пилотом нужно добавить ввод начальных снимков, их отсечение, совместимые структурные строки, сортировку и все поля контракта. Запускать эту заготовку как замену действующему отчёту нельзя.

Предположение примера: типы дат и границы периода уже согласованы со схемой, `FirstAccount`/`SecondAccount` разрешены из выбранной пары. Для текущих данных нельзя просто отбросить `AccountOpeningBalances`.

```sql
WITH movements AS (
    SELECT p."Id" AS posting_id, p.posting_date, p.doc_number,
           p.debit_account, p.credit_account,
           coalesce(p.amount_kgs, 0)::numeric AS amount_kgs,
           coalesce(p.amount_currency, 0)::numeric AS amount_currency,
           p.organization_id, coalesce(o.name, 'Без организации') AS organization_name,
           coalesce(o.code, '') AS organization_code,
           coalesce(c.code, '') AS currency_code,
           coalesce(p.description, '') AS description,
           coalesce(p.module_code, '') AS module_code
    FROM doc_postings p
    LEFT JOIN catalog_organizations o ON o."Id"::text = p.organization_id::text
    LEFT JOIN catalog_currencies c ON c."Id"::text = p.currency_id::text
    WHERE coalesce(p.is_active, true)
      AND p.posting_date < @PeriodEndExclusive
      AND (@AllOrganizations OR p.organization_id::text = @OrganizationId::text)
), sides AS (
    SELECT m.*, s.side, s.account_code,
           CASE WHEN s.side = 'D' THEN m.amount_kgs ELSE 0 END AS debit_amount,
           CASE WHEN s.side = 'C' THEN m.amount_kgs ELSE 0 END AS credit_amount,
           CASE WHEN s.side = 'D' THEN m.amount_currency ELSE -m.amount_currency END AS signed_currency
    FROM movements m
    CROSS JOIN LATERAL (
        VALUES ('D', m.debit_account), ('C', m.credit_account)
    ) s(side, account_code)
    WHERE
      (@MatchMode = 'Exact' AND
          (s.account_code = @FirstAccount OR s.account_code = @SecondAccount))
      OR
      (@MatchMode = 'Prefix' AND
          (left(s.account_code, length(@FirstAccount)) = @FirstAccount
           OR left(s.account_code, length(@SecondAccount)) = @SecondAccount))
), classified AS (
    SELECT *,
           CASE WHEN posting_date < @PeriodStart THEN 'Opening' ELSE 'Movement' END AS row_kind
    FROM sides
)
SELECT posting_id, posting_date, doc_number, debit_account, credit_account,
       organization_id, organization_name, organization_code, currency_code,
       side, row_kind, debit_amount, credit_amount, amount_currency, signed_currency,
       description, module_code
FROM classified
```

Обязательные дополнения для полного пилота:

1. Выбрать последний `AccountOpeningBalances` по фактическому счёту на дату начала.
2. Отсечь предыдущие проводки до даты применимого снимка точно как в Legacy.
3. Введённые остатки без организации не разносить вымышленно по контрагентам; оставить прежнюю логику «Без организации».
4. Не придумывать валютный остаток из национального: в этой таблице нет валютной суммы.
5. Сформировать совместимые строки групп и четыре итога. Их подписи получать из настроек/параметров.
6. Для движения передавать исходную валютную сумму; для единственной валютной графы итогов — прежний знак Дт минус Кт.
7. Прямую проводку между счетами пары отражать двумя сторонами, не удваивать сторону из-за пересекающихся префиксов.
8. Не смешивать USD/EUR/RUB; сохранить валюту в ключе группировки.
9. Не исключать организации с одним остатком без движений.
10. Вернуть понятную пустую строку при отсутствии данных и сохранить служебные поля шапки.
11. Перенести сортировку явно, включая код организации и номер документа. PostgreSQL collation и `CurrentCultureIgnoreCase` не обязательно эквивалентны.

Разница «Exact/Prefix» должна менять только отбор, а не источник начальных остатков или ключи групп. Нельзя объявить SQL эквивалентным, пока все перечисленные правила не реализованы и не сравнены.

## 10. Контракт данных и совместимые алиасы

Для пилота зафиксировать точные имена и типы текущих полей. Например:

```json
{
  "contractVersion": 1,
  "columns": [
    { "name": "Дата", "type": "String", "required": true, "allowNull": true },
    { "name": "Документ", "type": "String", "required": true, "allowNull": true },
    { "name": "Дебет", "type": "String", "required": true, "allowNull": true },
    { "name": "Кредит", "type": "String", "required": true, "allowNull": true },
    { "name": "Сумма Дт", "type": "Decimal", "required": true, "allowNull": true },
    { "name": "Сумма Кт", "type": "Decimal", "required": true, "allowNull": true },
    { "name": "Сумма вал.", "type": "Decimal", "required": true, "allowNull": true },
    { "name": "Содержание", "type": "String", "required": true, "allowNull": true },
    { "name": "Модуль", "type": "String", "required": true, "allowNull": true },
    { "name": "report_title", "type": "String", "required": true, "allowNull": false },
    { "name": "period_text", "type": "String", "required": true, "allowNull": false },
    { "name": "debit_amount", "type": "Decimal", "required": true, "allowNull": true },
    { "name": "credit_amount", "type": "Decimal", "required": true, "allowNull": true }
  ]
}
```

Состав контракта не выбирать только по видимым графам. Проверять все `TakeFromField`, поля таблиц, `Groups.ByFields`, `SumFields`, `LabelField`, ссылки правил, заголовков и подписей.

```csharp
public static void ValidateColumns(
    DataTable data, IReadOnlyList<ReportColumnContract> contract)
{
    foreach (var field in contract)
    {
        if (!data.Columns.Contains(field.Name))
        {
            if (field.Required)
                throw new InvalidOperationException($"Источник не вернул поле «{field.Name}».");
            continue;
        }

        var column = data.Columns[field.Name]!;
        var expected = field.Type switch
        {
            "String" => typeof(string),
            "Decimal" => typeof(decimal),
            "DateTime" => typeof(DateTime),
            "Guid" => typeof(Guid),
            "Bool" => typeof(bool),
            _ => throw new InvalidOperationException($"Неизвестный тип контракта: {field.Type}")
        };
        if (column.DataType != expected)
            throw new InvalidOperationException($"Поле «{field.Name}»: ожидался {expected.Name}, получен {column.DataType.Name}.");
        if (!field.AllowNull && data.Rows.Cast<DataRow>().Any(row => row.IsNull(column)))
            throw new InvalidOperationException($"Поле «{field.Name}» содержит NULL.");
    }
}
```

Если SQL возвращает английские технические имена, использовать отдельную сохраняемую карту алиасов. Не угадывать неоднозначные поля только удалением пробелов/регистра. Исходный ключ и пользовательская подпись должны различаться. Переименование графы в макете не должно менять бухгалтерский смысл поля.

## 11. Дополнительные итоговые строки группы: отдельное расширение

Только после успешного SQL-пилота решать, нужен ли визуальный редактор четырёх строк подвала. Если нужен, предлагается аддитивное свойство, например `FooterRows`, у групп. Старое `Subtotal` остаётся поддерживаемым.

```csharp
// Проект расширения, не текущая модель.
public sealed class ReportGroupFooterRow
{
    public string Label { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new();
    public bool Bold { get; set; } = true;
}

// В новой модели группы:
// public List<ReportGroupFooterRow> FooterRows { get; set; } = new();
```

```json
{
  "byFields": ["OrganizationId", "CurrencyCode"],
  "footerRows": [
    { "label": "САЛЬДО НА НАЧАЛО", "values": { "Сумма Дт": "MAX(OpeningNet, 0)", "Сумма Кт": "MAX(-OpeningNet, 0)" } },
    { "label": "ОБОРОТЫ", "values": { "Сумма Дт": "TurnoverDebit", "Сумма Кт": "TurnoverCredit" } },
    { "label": "САЛЬДО НА КОНЕЦ", "values": { "Сумма Дт": "MAX(OpeningNet + TurnoverDebit - TurnoverCredit, 0)", "Сумма Кт": "MAX(-(OpeningNet + TurnoverDebit - TurnoverCredit), 0)" } }
  ]
}
```

Это **не поддерживаемый сейчас синтаксис**. Нужны определение контекста группы, ограниченная грамматика, decimal-вычисление и одинаковая реализация Excel/PDF. Не использовать `DataTable.Compute`, произвольный C# или динамическую компиляцию как необъяснённый «редактор формул».

Значения `OpeningNet`, `TurnoverDebit`, `TurnoverCredit` должны считаться из явно определённого набора строк, а не суммой уже напечатанных итогов. Для агрегированного остатка, повторённого у каждой строки движения, SUM недопустим. Либо один отдельный ряд остатка на группу, либо отдельный контекст агрегатов.

Для макетов с новым `FooterRows` вводить новую версию/минимальные возможности редактора. Старый редактор может потерять незнакомые JSON-поля при сохранении. Поэтому не разрешать редактирование такого макета старой версией программы. До этого этапа пилот остаётся на макете V1.

## 12. Изменения интерфейсов

### Конфигуратор / наборы данных

- Оставить текущие SQL/системные наборы; добавить контракт и типизированные параметры, тест с введёнными параметрами, диагностический список полей.
- Дать «Копировать набор», чтобы не менять общий источник всех отчётов.
- Показывать список отчётов-потребителей перед сохранением изменений в существующем наборе.
- Не обновлять автоматически изменённый SQL при следующей инициализации. Для новых источников сохранять начальный хеш и отличать неизменённый seed от пользовательской редакции.

### Конструктор отчётов

- Выбор режима Legacy / V2, состояние черновика и контракт.
- Выбор источника по DataSourceId, без скрытого выбора по ReportType.
- Параметры со справочным выбором пары/организации и управлением «Все организации».
- «Проверить поля», «Сравнить с прежним», «Сохранить черновик», «Включить V2», «Вернуть прежний вариант».
- Текстовые подписи/форматы редактируются отдельно от технических имён полей.
- Неподдерживаемые свойства не теряются при round-trip. Неизвестная версия определения блокирует изменение, а не пересохраняется урезанным объектом.

### Бухгалтерские отчёты

- Получать форму параметров из определения, сохранив старые панели для Legacy.
- Переиспользовать исправленные селекторы с «Добавить/Изменить», свежими картами ссылок и названиями модулей.
- Параметры предпросмотра и экспорта не теряются при смене вкладки.
- При ошибке запроса показывать понятное сообщение и не выдавать старую таблицу за только что пересчитанный результат.

## 13. Предполагаемые изменения файлов

Названия новых классов проектные; список уточнить при реализации, не создавать все файлы механически.

| Файл / область | Планируемое изменение |
| --- | --- |
| `Models/ReportExecutionDefinition.cs` (новый) | Типизированное описание V2 |
| `Models/ReportExecutionBinding.cs` (новый) | Необязательная привязка и состояние |
| `Data/AppDbContext.cs` | Регистрация сущностей новых настроек/снимков |
| `Services/SchemaMigrationService.cs` | Аддитивные таблицы, без массового переключения отчётов |
| `Services/ReportExecutionRouter.cs` (новый) | Явный выбор Legacy/V2 |
| `Services/ConfigurableReportRunner.cs` (новый) | Общий запуск, проверка контракта, подготовка результата |
| `Services/ReportParameterResolver.cs` (новый) | Типы, справочники, производные параметры |
| `Services/ReportDataContractValidator.cs` (новый) | Поля, типы, зависимости макета |
| `Services/ReportMigrationSnapshotService.cs` (новый) | Копии, контроль хешей, откат |
| `Services/ReportDataSetService.cs` | Read-only рабочий путь, параметры, отмена, неизменность seed |
| `Services/ReportService.cs` | Единая точка выполнения с сохранением Legacy |
| `Views/AccountingReportsView.xaml.cs` | Убрать специальный обход только у явно включённых V2 |
| `Views/AccountingReportsView.AccountPair.cs` | У пилота передавать параметры общему движку; старый путь оставить |
| `Views/ReportDesignerWindow.xaml(.cs)` | Настройки V2, предпросмотр, контракт, сравнение |
| `Views/ReportDataSetManagementView.xaml(.cs)` | Типизированные параметры, копирование источника, проверка потребителей |
| `Services/MetadataService.Reports.cs` | Только первоначальная посадка новой копии/источника |
| `Models/ClosedXmlReportLayout.cs` и оба рендерера | Только на отдельном этапе FooterRows, не в первом пилоте |
| `BIS.ERP.TESTS/BIS.ERP.TESTS.Core` | Тесты совместимости, сумм, макетов, отката и неизменности базы |

`AccountPairTurnoverBuilder` не удалять до окончания пилота и периода сравнения. Впоследствии его можно оставить как эталон тестов/Legacy-адаптер, если потребуется поддержка старых конфигураций.

## 14. Проверки и критерии приёмки

### Контроль данных

Сравнивать не только общую сумму. Для каждой группы проверить:

- число движений и структурных строк;
- обе стороны прямой проводки;
- внешние корреспонденции;
- одинаковые/пересекающиеся префиксы;
- начальное сальдо, обороты Дт/Кт, конечное сальдо;
- валютные значения и разделение валют;
- null/пусто/ноль и знак кредитового остатка;
- даты на обеих границах, время последнего дня, часовой пояс;
- организацию без движений, но с остатком;
- введённые снимки без аналитики организации;
- одинаковые имена организаций с разными Id;
- неактивные записи и пустой результат;
- порядок групп и документов, дублирующиеся номера.

Суммы `decimal` сравнивать точно в принятой точности, без произвольного допуска, маскирующего копейки. Разрешённое округление задавать явно до сравнения.

### Контроль формы

В Excel проверять значения ячеек, форматы, формулы, объединения, ширины, ориентацию, шапки, подписи, повторение страниц. В PDF — текст, числовые значения и визуальный рендер всех страниц. Побайтовое равенство XLSX/PDF не требуется: метаданные/время генерации могут отличаться.

### Контроль совместимости

1. Нет V2-записи — вызывается только прежний построитель.
2. Состояние Draft/Disabled — прежнее поведение.
3. Включённая V2 исполняет сохранённый источник, даже если ReportType совпадает со специальным старым типом.
4. Изменение выбранного источника влияет на окно отчётов, предпросмотр и оба экспорта одинаково.
5. Отсутствующее поле/несовместимый тип выдаёт ошибку до печати.
6. Скрытые поля шапки/подписей остаются доступными.
7. Старый JSON-макет сохраняет исходные байты, если его не редактировали.
8. Повторная инициализация не перетирает SQL/параметры/макет пользователя.
9. Миграция определений не меняет `doc_postings` и первичные документы.
10. Откат восстанавливает полное определение, а не только текст макета.
11. Ошибка V2 не приводит к тихому исполнению другой финансовой формулы.
12. Совместное использование набора не меняет другие отчёты при переносе копии.

### Проект теста маршрутизатора

```csharp
// Псевдокод сценария TESTS: инфраструктуру фикстур нужно реализовать.
var calls = new List<string>();
var legacy = new FakeLegacyRunner(() => calls.Add("Legacy"));
var configurable = new FakeV2Runner(() => calls.Add("V2"));

await fixture.BuildWithBindingAsync(binding: null);
AssertSequence(calls, "Legacy");

calls.Clear();
await fixture.BuildWithBindingAsync(binding: EnabledV2());
AssertSequence(calls, "V2");

var oldResult = await fixture.BuildLegacyAccountPairAsync(parameters);
var newResult = await fixture.BuildDraftAccountPairAsync(parameters);
CompareSchema(oldResult.Data, newResult.Data);
CompareRowsAndDecimals(oldResult.Data, newResult.Data);
CompareWorkbookCells(oldResult, newResult);
await AssertNoPostingChangesAsync(fixture.BeforePostingSnapshot);
```

### Доступный контрольный набор ОСОО2

Использовать уже оставленные записи по паре №12 **14200000 — 32200000**, дата **30.10.2026**, организация **999830**, номера **999830103001–999830103004**. Входные суммы: 9000/100 USD, 3600/40 USD, 900/10 USD, 1500 сом. Прямая проводка отражается по двум сторонам.

Для этой организации за этот день без прочих записей национальные обороты: Дт **11 400**, Кт **4500**, разность **6900 сом**. Для USD-части: Дт **110**, Кт **50**, разность **60 USD**. Национальная разность USD-блока **5400 сом**, отдельный сомовый блок **1500 сом**. Эти значения служат проверкой ведомости; не включать их константами в рабочий отчёт.

Параметры валютного учёта самой пары пользовательские. При сравнении старого акта отдельно учесть его зависимость от `UsesCurrency`; не менять флаг автоматически ради совпадения теста. Все эти записи — ручные тестовые проводки без первичных документов-оснований.

## 15. Публикация, откат и версии клиентов

Рекомендуемая последовательность:

1. Развернуть поддержку V2, не включая ни одного старого отчёта.
2. Создать отдельные копии отчёта и источника со статусом Draft.
3. Снять снимок исходного определения и выполнить сравнение.
4. Проверить конфликт параллельного редактирования через `Revision` и отпечаток определения.
5. Включить только пилотную копию, сохранив старый вариант.
6. После проверки переносить следующий отчёт, а не включать V2 всем типам сразу.

Отпечаток включает источник, SQL/CalculatorCode, параметры, контракт, поля, используемые настройки и макет. Не полагаться только на `UpdatedAt`: старый редактор может не обновить новую таблицу привязки.

Откат пилотной копии — отключить её V2-привязку/саму копию и использовать исходный отчёт. При переводе существующего ReportId откат должен восстановить его источник, дочерние поля/параметры и макет из снимка. Изменения проводок не являются частью миграции и не должны происходить.

**Старые исполняемые версии программы не обязаны понимать новые V2-определения.** До включения V2 обновить все рабочие клиенты, которым доступны эти отчёты. Для старых отчётов сохраняется прежний путь; для новых конфигураций с неизвестными свойствами нужна явная проверка версии. Если позже вводится макет Version=2, старым редакторам нельзя разрешать его пересохранять с потерей новых полей.

## 16. Границы обещания «без поломки»

Можно обеспечить архитектурно: отсутствие автоматической замены старых отчётов, сохранение исходных макетов, отдельные источники-копии, контроль контракта, поэтапное включение и обратимый откат.

Нельзя гарантировать одним проектным документом: совпадение всех бухгалтерских итогов, поведение пользовательских SQL и макетов, идентичность всех PDF-страниц, совместимость новых определений с уже выпущенными старыми бинарными версиями. Это подтверждается реализацией и проверками, а не предположением.

**Рекомендуемый первый результат:** копия ведомости по паре на настраиваемом SQL-источнике, с прежним ClosedXML-макетом V1, прежним составом строк и успешным сравнением. Визуальные формулы и новый подвал групп — следующий самостоятельный этап, а не условие первого безопасного переноса.

Документ завершает оценку. Разрешение на реализацию переноса этой задачей не подразумевается.
