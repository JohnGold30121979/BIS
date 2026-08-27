# Механизм интеграций «HTTP-Сервисы» (аналог 1С: Общие → HTTP-Сервисы)

Документ: подробный план реализации
Статус: предложение (design proposal)
Дата: август 2026 года

---

## 1. Постановка задачи

В 1С механизм «HTTP-Сервисы» (Общие → HTTP-Сервисы) позволяет описывать набор
веб-сервисов, у каждого — свой корневой URL, а у сервиса — ресурсы (шаблоны
URI) с методами GET/POST/PUT/DELETE, каждому ресурсу назначается серверная
функция из модуля HTTP-сервиса. Конфигуратор задаёт маршрут, метод, параметры
(из шаблона пути, строки запроса, заголовков и тела), а разработчик пишет
только обработчик.

Цель для BIS.ERP — сделать **настраиваемый, конфигурируемый через конфигуратор
механизм интеграций**, который:

1. Позволяет описывать HTTP-сервисы декларативно (метаданные), без правки кода
   основного приложения для типовых интеграций;
2. Использует **встроенный HTTP-сервер** внутри настольного приложения
   (приложение остаётся автономным и работает как локальный/сетевой сервис на
   хосте, где запущен BIS.ERP);
3. Безопасно исполняет логику: для каждой точки привязки назначается либо
   «встроенное действие» (готовый CRUD по метаданным — справочники/документы),
   либо «серверный обработчик по коду» (реализован в приложении и зарегистрирован
   в реестре обработчиков, как это делают модули в 1С);
4. Сохраняет метаданные HTTP-сервисов в БД, даёт возможность настраивать их в
   конфигураторе, выгружать/загружать через `.bisconfig` и доставлять клиентам
   через патчи `.bispatch`;

---

## 2. Где это будет жить в BIS.ERP

| Пласт | Где в приложении | Что появится |
|---|---|---|
| Модель | Проект `BIS.ERP/Services` | классы `HttpServerConfiguration`, `HttpServiceDefinition`, ... |
| Рантайм | Приложение `App.OnStartup` | запуск/остановка встроенного сервера при старте |
| Метаданные | Конфигуратор | раздел «Интеграции → HTTP-Сервисы» |
| Патчи | `.bispatch` / `.bisconfig` | `integration.json` + `data.json` |
| Тесты | `BIS.ERP.TESTS` | scenario-сценарии запросов к серверу |

---

## 3. Ключевые архитектурные решения

### 3.1 Встроенный HTTP-сервер: вариант реализации

Для встраивания выбираем один из двух движков. Рекомендация — `HttpListener`
(NET, без внешних зависимостей, достаточен для внутренних интеграций):

| Критерий | Kestrel (ASP.NET Core) | HttpListener (System.Net) |
|---|---|---|
| Путь запуска | нужен ServiceCollection + конфигурация хоста | один класс, `Start/Stop` |
| Производительность | выше (многопоточность) | достаточная для ERP-интеграций |
| Зависимости | добавляет пакеты ASP.NET Core | System-only |
| Встраивание в WPF | есть примеры, но сложнее | простое, явное |
| HTTPS/TLS | Kestrel настраивает сам | через `https` префикс + cert |

Примечание: для высоких нагрузок/облачного деплоя можно в перспективе перейти на
Kestrel, сохранив интерфейсы `IHttpRequestDispatcher`.

### 3.2 Модель «обработчиков» (безопасная + без exec произвольного C#)

Запрещается исполнять произвольный источник код пользователя через отклик
HTTP-запроса (опасно и непредсказуемо). Вместо этого вводим две модели:

- **Встроенные действия (Built-in actions)** — безопасные операции над данными
  метаданных: чтение/создание/изменение/проведение справочников и документов,
  запрос остатков, выполнение отчёта, запуск типового действия. Выбираются в
  конфигураторе из раскрывающегося списка; не требуют написания кода.
- **Серверные обработчики (code handlers)** — функции в приложении, которые
  регистрируются в реестре `HttpHandlerRegistry` по программному ключу
  (например `cash.getOrder`). Интеграция указывает этот ключ; при обработке
  запроса движок вызывает соответствующую функцию. Такой подход — прямой аналог
  серверных модулей 1С, но безопасный: выполняется только код, собранный в
  приложении.

### 3.3 Цикл обработки запроса

```
HTTP-запрос → HttpServer.Station → Router (match URL+метод)
  → Authentication (токен/CORS/ip-allowlist)
  → Validation (параметры шаблона/запроса/тела)
  → Action (Built-in или CodeHandler) → результат DataTable/JSON
  → Response (JSON/XML/приложение) + Content-Type
  → журналирование в events + интеграционный журнал
```
---

## 4. Модель данных в БД (описание таблиц)

### 4.1 `HttpServerSettings` — настройки сервера (одна запись на инфобазу)

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `IsEnabled` | bool | Включён ли встроенный сервер |
| `BaseAddress` | varchar(500) | Корневой префикс, напр. `http://+:8088/` |
| `Host` | varchar(200) | Разрешённый host для запросов (или `+` / `*`) |
| `Port` | int | Порт (как правило 8088, настраивается) |
| `UseHttps` | bool | Признак TLS |
| `CertificateThumbprint` | varchar(200) | Отпечаток сертификата (для `https`) |
| `AuthMode` | varchar(40) | `None` / `Token` / `Basic` |
| `AccessToken` | varchar(200) | Ключ доступа (если `Token`) |
| `AllowedIp` | text | Разрешённые IP/CIDR (строка, разделитель `;`) |
| `CorsOrigins` | text | Разрешённые источники CORS (`*` или список) |
| `MaxBodyBytes` | int | Лимит тела запроса (по умолчанию 10 МБ) |
| `RequestTimeoutMs` | int | Таймаут обработки |
| `UpdatedAt` | datetime | Дата изменения |

Хранится в таблице `HttpServerSettings` (1 запись на инфобазу), аналогично
`SystemConfigurations`.

### 4.2 `HttpService` — HTTP-Сервис (мета уровень)

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `Code` | varchar(100) | Уникальный код сервиса (например `Cash`), аналог имени HTTP-Сервиса 1С |
| `Name` | varchar(300) | Человекочитаемое имя |
| `Root` | varchar(200) | Корневой сегмент URL: `/hs/cash` |
| `Description` | varchar(1000) | Описание |
| `IsSystem` | bool | Признак системного |
| `IsActive` | bool | Активен ли |
| `Order` | int | Порядок при выгрузке |
| `MetadataConfigId` | guid? | Привязка к метаданным (как у `MetadataObject`) |

### 4.3 `HttpResource` — ресурс сервиса (шаблон URI)

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `ServiceId` | guid | FK на `HttpServer` |
| `PathTemplate` | varchar(500) | Шаблон пути: `/orders/{orderId}` |
| `HttpMethod` | varchar(10) | GET / POST / PUT / PATCH / DELETE |
| `DisplayName` | varchar(300) | Имя ресурса |
| `Order` | int | Порядок |
| `Description` | varchar(1000) | Описание |

### 4.4 `HttpResourceParameter` — параметр ресурса

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `ResourceId` | guid | FK |
| `Name` | varchar(100) | Имя параметра (`order`) |
| `Source` | varchar(20) | `Path` / `Query` / `Header` / `Body` |
| `DataType` | varchar(30) | String / Guid / Int / Decimal / DateTime / Bool |
| `IsRequired` | bool | Обязателен |
| `DefaultValue` | varchar(500) | Значение по умолчанию |
| `Order` | int | Порядок |

### 4.5 `HttpActionBinding` — привязка ресурса к действию

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `ResourceId` | guid | FK |
| `ActionKind` | varchar(40) | `BuiltIn` или `CodeHandler` |
| `ActionCode` | varchar(300) | Код действия (например `metadata.read`, `cash.getOrder`) |
| `BodySchema` | text | JSON-схема/пример тела (опционально) |
| `ResponseContentType` | varchar(60) | `application/json` / `application/xml` / приложение |
| `RequestTemplate` | text | Доп. маппинг параметров `{field}->{arg}` |

### 4.6 `HttpRequestLog` — журнал запросов

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | guid | PK |
| `Timestamp` | datetime | Дата |
| `Method`, `Url` | varchar | Запрос |
| `StatusCode` | int | Код ответа |
| `DurationMs` | int | Время обработки |
| `ServiceCode`, `ResourceId` | varchar | Какая точка обработана |
| `RemoteIp` | varchar(100) | IP клиента |
| `Error` | varchar(2000) | Текст ошибки (если была) |

---

## 5. Публичное API движка (проект Services)

```
public enum HttpActionKind { BuiltIn, CodeHandler }

public interface IHttpRequestContext
{
    HttpServiceDefinition Service { get; }
    HttpResourceDefinition Resource { get; }
    string HttpMethod { get; }
    IReadOnlyDictionary<string, object> Parameters { get; } // из шаблона + запроса + заголовков
    string? RawBody { get; }
    string? ContentType { get; }
    Dictionary<string,string> Headers { get; }
    bool TryReadJson<T>(out T value);
}

public interface IHttpActionResult
{
    int StatusCode { get; }
    string ContentType { get; }
    object? Data { get; }        // сериализуется в JSON/XML
    // или byte[] Body / string Body
}

public interface IBuiltInHttpAction
{
    string Code { get; }
    string Name { get; }
    Task<IHttpActionResult> ExecuteAsync(IHttpContext context, CancellationToken ct);
}

public class HttpHandlerRegistry
{
    void Register(IBuiltInHttpAction action);
    IReadOnlyList<IBuiltInHttpAction> List();
}
```

### Встроенные действия (первая поставка)

| Код | Что делает | Возвращает |
|---|---|---|
| `catalog.list` | Получить список записей справочника по `Name`/`TableName` | JSON-массив |
| `catalog.read` | Прочитать запись по `Id` | JSON объект |
| `catalog.create` | Создать запись (тело = поля) | созданный объект |
| `catalog.update` | Обновить запись | Обновлённый объект |
| `catalog.delete` | Удалить запись | `{ok}` |
| `document.post` | Создать и провести документ по метаданным | результат + проводки |
| `document.postings` | Вернуть проводки документа | JSON-массив |
| `report.run` | Выполнить отчёт по `ReportCode`/`Id` с параметрами | данные отчёта |
| `balance.query` | Получить остатки по счёту за период | JSON-массив |

Эти действия переиспользуют существующие сервисы: `MetadataService`
(`CreateDynamicRecordAsync`, `PostDocumentAsync`, `GetCatalogDataAsync`),
`BalanceService.GetTurnoverBalanceAsync`, `ReportService`. Отдельных новых
бизнес-реализаций не требуется — нужны только обёртки.

---

## 6. Пошаговый план реализации (фазы)

### Фаза 0. Фундамент и инфраструктура (1–2 недели)
1. Создать папку `BIS.ERP/Integrations` и папку `BIS.ERP/Integrations/Contracts`.
2. Определить интерфейсы `IHttpServer`, `IHttpContext`, `IHttpActionResult`, `IHttpAction`, `IHttpRequestDispatcher`.
3. Определить контракты метаданных: `HttpModuleDefinition`, `HttpResourceDefinition`, `HttpResourceParameter`, `HttpActionBinding`.
4. Реализовать `IntegrationHttpServer` (обёртка `HttpListener`) с `StartAsync()` / `StopAsync()`.
5. Подключить жизненный цикл в `App.OnStartup` (запуск) и при выходе (остановка).
6. Модель настроек БД `HttpServerSettings` + создание одной записи `IsEnabled=false` в `EnsureSchemaAsync`.

**Критерий:** при `IsEnabled=false` сервер не запущен; при включении — стартует, отдаёт `503`/`404` на несвязанные маршруты и пишет запросы в `HttpRequestLog`.

### Фаза 1. Рантайм-движок (2–3 недели)
1. `HttpRouter` — матчинг сервис + ресурс по `HttpMethod` и шаблону `{param}`, получение параметров из пути (path placeholders).
2. `ParameterBinder` — заполнение параметров из: пути, строки запроса, заголовков (`Header`), тела запроса.
3. `HttpActionExecutor` — преобразование `HttpActionBinding` в действие:
   - `BuiltIn` → поиск в `HttpHandlerRegistry`;
   - `CodeHandler` → поиск по ключу в реестре.
4. Формирование ответов: `JsonResult`, `XmlResult`, `RawContentResult`, ошибки → `{ error: ... }` + статус-код.
5. `HttpHandlerRegistry` — регистрация встроенных действий.
6. Встроенные действия (базовый набор): `catalog.list/read/create/update/delete`, `document.post/read/postings`, `report.get`, `balance.query`.

**Критерий:** curl-запрос к тестовому маршруту возвращает JSON; на ошибки — корректные `400/404/405/500`.

### Фаза 2. Метаданные HTTP-сервисов в БД + конфигуратор (2–3 недели)
1. Модели `HttpServer` (сервис), `HttpResource`, `HttpResourceParameter`, `HttpActionBinding` + `EnsureSchemaAsync`.
2. Сервис `HttpMetadataService`: CRUD сервисов и ресурсов, `EnsureDefaultServicesAsync`, seed системного сервиса.
3. UI конфигуратора: ветвь «Интеграции → HTTP-Сервисы» в `BuildMetadataTree`, список сервисов (карточки), редактор сервиса (вкладки: Общие, Ресурсы, Параметры, Привязка, Предпросмотр), по образцу `ModuleManagementView` и `ReportDesignerWindow`.
4. Редактор ресурса: шаблон пути, `HttpMethod`, параметры (из пути/query/заголовков), привязка действия из реестра (`BuiltIn` список + `CodeHandler`).

**Критерий:** в конфигураторе создаётся сервис, ресурс `GET /orders/{orderId}`, привязка `catalog.read`; при включённом сервере маршрут обрабатывается.

### Фаза 3. Безопасность и политики (2 недели)
1. Аутентификация: режимы `None`, `Token` (Bearer / `X-API-Key`), `Basic`.
2. Ограничение по IP/CIDR.
3. CORS: заголовки + `OPTIONS` preflight для браузерных клиентов.
4. Лимиты: размер тела, таймаут, защита от path traversal, обработчик ошибок.
5. Права: опциональная проверка через `UserAccessPermission`; логирование через `HttpRequestLogService` (без паролей и секретов).

**Критерий:** запрос без токена/с неразрешённого IP → `401/403`; попытки журналируются.

### Фаза 4. Выгрузка и патчи (1–2 недели)
1. Расширить `ConfigurationExchangeService`: загрузить/выгрузить HTTP-сервисы (в `ConfigurationPackage`).
2. Добавить в формат патча файл `integration.json` (сервисы/ресурсы/параметры/привязки) и обработать в `BisPatchService`.
3. При применении — `EnsureHttpDataAsync`.
4. Seed системного HTTP-сервиса при первой установке (подобно `EnsureDefaultModulesAsync`).

**Критерий:** патч с HTTP-сервисами ставится на другую инфобазу; сервис появляется в конфигураторе и обрабатывает запросы.
### Фаза 5. Система действий и редактор кода (1–2 недели)
1. Реестр встроенных действий: `HttpHandlerRegistry`, регистрация серверных
   обработчиков по коду (аналог серверных модулей 1С).
2. Готовые встроенные действия (обёртки над существующими сервисами):
   `catalog.list/read/create/update/delete`, `document.post/read`, `document.postings`,
   `report.get`, `balance.query`, `tax.invoice` (счёт-фактура).
3. Валидация параметров по типу (String/Guid/Int/Decimal/DateTime/Bool) и
   обязательности.
4. Обработка тела JSON в `IHttpAction` через `TryReadJson<T>`.

**Критерий:** встроенное действие `catalog.read` возвращает запись справочника по
`Id`; `document.post` создаёт документ и (опционально) проводит его.

### Фаза 6. Тесты (1–2 недели)
1. Набор `BIS.ERP.TESTS` сценарий `integration-http` (verify/run/cleanup):
   поднять тестовый порт, выполнить GET/POST к встроенным действиям,
   проверить JSON-ответ и журнал `HttpRequestLog`.
2. Юнит-тесты роутера: разбор шаблонов, параметров, кодов ошибок.
3. Тест миграции/патча с HTTP-сервисами (используя `ConfigurationExchangeService`/`BisPatchService`).
4. Автотест безопасности: без токена/чужой IP → 401/403.

**Критерий:** прогон через `BIS.ERP.TESTS` зелёный, сценарии `verify` и `run` проходят без ошибок.

---

## 7. Безопасность (чек-лист)

- TLS/HTTPS обязателен при доступе не только с localhost.
- По умолчанию сервер выключен (`IsEnabled=false`), привязка к 127.0.0.1.
- Аутентификация токеном по умолчанию, отключение только явно администратором.
- Ограничение по IP/CIDR.
- Ограничение размера тела и таймауты.
- Логирование всех запросов и ошибок без секретов (пароли/токены не попадают в журнал).
- Не выполняется произвольный код: только встроенные действия и серверные
  обработчики, входящие в сборку приложения.

---

## 8. Контрольные точки

| Неделя | Фаза | Результат |
|---|---|---|
| 1 | 0 | Поднять `HttpListener`, старт/стоп, журнал, `IsEnabled` |
| 2–3 | 1 | Роутер, параметры, `HttpMetadataService`, базовый CRUD метаданных |
| 4–5 | 2 | Конфигуратор: ветвь «Интеграции → HTTP-Сервисы», редактор |
| 6 | 3 | Безопасность (TLS, токен, IP, CORS, лимиты) |
| 7 | 4 | Выгрузка/загрузка `.bisconfig`, патч `integration.json` |
| 8–9 | 5 | Встроенные действия, реестр handler |
| 10–11 | 6 | Тесты, документация |

---

## 9. Резюме

План строится на существующей архитектуре BIS.ERP: метаданные в БД, конфигуратор,
патчи `.bispatch`, `.bisconfig`, `ConfigurationExchangeService`, `ServiceLocator`,
модель `Infobase`, `AuthService` и тестовый контур `BIS.ERP.TESTS`. Встроенный
HTTP-сервер + настраиваемые «HTTP-Сервисы» (метаданные) добавляют автономную
интеграционную шину: внешние системы обращаются к BIS.ERP по HTTP(S), 
конфигуратор задаёт маршруты и безопасность, а логика выполняется через
безопасные встроенные действия или серверные обработчики. Это покрывает типовые
интеграции (обмен справочниками/документами, приём заказов, экспорт отчётных
данных, банковские и налоговые сервисы) без изменения кода приложения.