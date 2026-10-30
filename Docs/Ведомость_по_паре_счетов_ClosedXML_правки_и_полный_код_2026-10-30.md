# Ведомость по паре счетов ClosedXML: изменения, проверки и полный код

Дата: 30.10.2026. Проект: BIS.ERP. Документ фиксирует окончательный согласованный вариант.

## Что реализовано

Добавлен отдельный отчёт **«Оборотная ведомость по паре счетов (ClosedXML)»** в окно «Бухгалтерская отчетность». Он не заменяет обычный акт сверки. Пользователь выбирает запись справочника «Пары счетов» кнопкой «?», период и режим «Все организации» либо конкретную организацию. По каждой организации формируются движения и четыре итоговые строки по образцу фотографии.

Код отчёта: `standard.finance.account-pair-turnover-closedxml`. Тип: `AccountPairTurnover`. Макет хранится в `Reports.ClosedXmlLayout`, доступен конструктору и используется общим механизмом вывода Excel/PDF.

Окончательный состав граф ведомости:

| Графа | Источник и смысл |
| --- | --- |
| Дата | Дата проводки |
| Документ | Номер документа |
| Дебет | Номер дебетового счёта, один экземпляр |
| Кредит | Номер кредитового счёта, один экземпляр |
| Сумма Дт | Дебетовое движение в национальной валюте, `amount_kgs` |
| Сумма Кт | Кредитовое движение в национальной валюте, `amount_kgs` |
| Сумма вал. | Одна графа валютной суммы, `amount_currency` |
| Содержание | Содержание проводки / тип документа |
| Модуль | `module_code`; подпись «Код АРМ» не используется |

В обычном акте сверки сохранены его прежние номера счетов и **«Сумма Дт» / «Сумма Кт»**, добавлена одна **«Сумма вал.»**. Промежуточная идея заменить национальные суммы одной графой «Сумма» отменена по уточнению пользователя и в итоговой реализации не применяется.

## Как открыть отчёт

1. Запустить обновлённую сборку и повторно открыть информационную базу.
2. Открыть «Бухгалтерская отчетность», выбрать «Оборотная ведомость по паре счетов (ClosedXML)».
3. Нажать «?» возле «Пара счетов». Откроется стандартное окно выбора. В нём включены кнопки **«Добавить» и «Изменить»**: новую пару можно создать прямо здесь, без отдельного открытия справочника. Ссылки на счета показаны через справочники; после сохранения пара перечитывается и доступна выбору.
4. Указать период и оставить «Все организации» либо выбрать конкретную организацию.
5. Нажать «Сформировать», «Excel» или «PDF».

Без выбора пары формирование остановится с пояснением. При снятой отметке «Все организации» необходима организация. Перед формированием пара перечитывается: удалённая, неактивная или потерявшая счета запись не используется молча.

При открытии базы выполняются новые одноразовые шаги:

- `2026.10.30.06`: добавление нового стандартного отчёта; точечная замена старого заголовка «Код АРМ» на «Модуль» в сохранённых ClosedXML-макетах актов.
- `2026.10.30.07`: добавление одной колонки «Сумма вал.» после «Сумма Кт» в существующих ClosedXML-макетах актов. Полная замена пользовательской разметки не производится.

Миграции подготовлены в коде. Во время проверки рабочие базы использовались только для чтения; эти шаги на них не запускались.

## Алгоритм и соответствие FOX

1. Выбираются активные пары с обоими счетами, разрешёнными через план счетов. Поддерживаются ссылки UUID и исторические коды.
2. SQL читает активные `doc_postings`, где встречается любой из двух счетов пары. Учитываются проводки с другими корреспондирующими счетами, а не только прямая корреспонденция внутри пары.
3. Совпадение префиксное, как в документе `FOX_ВЕДОМОСТЬ_ПО_ПАРЕ_СЧЕТОВ_ОБОРОТЫ.md`. Для каждого префикса применяется его собственная длина.
4. Каждая подходящая сторона проводки формирует строку. Прямая проводка между счетами пары формирует две строки: одну дебетовую, одну кредитовую. Одинаковые/пересекающиеся префиксы не дублируют одну и ту же сторону повторно.
5. Дата начала периода включается; последний день включается полностью через верхнюю границу `< end.Date.AddDays(1)`.
6. Предыдущие движения участвуют в начальном сальдо. По `AccountOpeningBalances` выбирается последний снимок каждого фактического счёта на начало периода; история до даты такого снимка не учитывается повторно.
7. Организации сортируются по числовому коду, затем наименованию; движения — по дате и номеру документа.
8. После движений выводятся «ИТОГО ПО ОРГАНИЗАЦИИ», «САЛЬДО НА НАЧАЛО», «ОБОРОТЫ», «САЛЬДО НА КОНЕЦ».

Формула сальдо в национальной валюте: `начальное Дт − начальное Кт + обороты Дт − обороты Кт`. Положительное сальдо выводится в Дт, отрицательное по абсолютной величине — в Кт. Организации с остатком без движений включаются. Если данных нет, выводится поясняющая строка.

Из FOX перенесён принцип движения каждого из двух счетов. Специальная знаковая схема `CRED_BEG` и динамическая граница `DTB` по последнему документу не копируются: здесь используется выбранное пользователем начало периода.

## Валютные поля: что проверено и исправлено

До изменений обычный акт загружал `AmountCurrency` и `CurrencyCode`, но не передавал валютную сумму в таблицу/макет. Новый отчёт первоначально формировался только в сомах. Теперь обе цепочки доводят валютную сумму до графы «Сумма вал.».

| Поле/этап | Новая ведомость | Акт сверки |
| --- | --- | --- |
| Национальная сумма | `amount_kgs` → «Сумма Дт» / «Сумма Кт» | Существующая раскладка Дт/Кт сохранена |
| Валютная сумма движения | `amount_currency` → «Сумма вал.» | `AmountCurrency` → «Сумма вал.» |
| Код валюты | Соединение с `catalog_currencies`, код в заголовке блока организации | Код добавлен к описанию движения |
| Валютное начальное сальдо | Из истории проводок | Из `OpeningDebitCurrency − OpeningCreditCurrency` при валютном учёте пары |
| Валютные обороты/конечное сальдо | Рассчитываются отдельно по организации и валюте | Передаются из рассчитанных остатков пары |
| Смешанные валюты | Разные валюты разделены на отдельные блоки организации | Итог одной пары по нескольким валютам не выводится одной суммой; валютные суммы движений сохраняются |
| Сетка, Excel, PDF | Валютное поле присутствует в источнике и макете | Валютное поле присутствует в источнике и макете |

В строках движений «Сумма вал.» содержит исходную сумму операции. В итогах и сальдо, поскольку валютная графа одна, применяется знак **Дт минус Кт**. Поэтому кредитовый валютный оборот/остаток может быть отрицательным. Национальные суммы остаются разнесёнными по двум графам.

`AccountOpeningBalances` хранит остаток по счёту без организации и без валютной суммы. Такой введённый национальный остаток отображается в блоке «Без организации» в режиме всех организаций; автоматически распределять его по контрагентам или придумывать валютный эквивалент нельзя. Валютное начальное сальдо рассчитывается из имеющихся валютных проводок. Для достоверных исторических остатков по контрагентам нужны исходные данные с аналитикой организации.

Старая модель суммирует валютные остатки пары по признаку `UsesCurrency`. Проверка не меняет эту модель хранения. Наличие разных валют только в истории без движений текущего периода требует отдельного разнесения исходных остатков по валютам; по одному агрегату восстановить их нельзя.

## Интеграция с существующей программой TESTS

Проверки добавлены в **`BIS.ERP.TESTS/BIS.ERP.TESTS.Core`**, а не оставлены отдельной временной консольной программой. Сценарии автоматически обнаруживаются существующим `SmokeTestRegistry` через интерфейс `ISmokeTestScenario` и доступны как консоли, так и графическому TESTS.

| Код | Режим | Назначение |
| --- | --- | --- |
| `account-pair-turnover` | Verify | 21 контрольная проверка расчёта, дат, пар, организаций, остатков, Excel, PDF и валют |
| `account-pair-turnover` | Run | Те же проверки плюс чтение существующих проводок всех доступных пар в настроенных базах |
| `reconciliation-currency-fields` | Verify | Все нужные поля обоих отчётов, отсутствие дублирования граф, ссылки макетов на поля, ненулевые валютные значения в акте и Excel |

Для новых сценариев очистка отключена. Run подключается к проверяемым базам с `Options=-c default_transaction_read_only=on`. Ни тестовые, ни рабочие проводки не удаляются и не изменяются.

Команды из каталога `BIS.ERP.TESTS` для проверенной сборки:

```powershell
dotnet ../artifacts/tests-integrated/BIS.ERP.TESTS.Console.dll verify --scenario account-pair-turnover
dotnet ../artifacts/tests-integrated/BIS.ERP.TESTS.Console.dll verify --scenario reconciliation-currency-fields
dotnet ../artifacts/tests-integrated/BIS.ERP.TESTS.Console.dll run --scenario account-pair-turnover
```

Сценарий Run использует настройки существующего TESTS. Во время проверки они указывали на недоступный сервер `192.168.1.19:5432`; этот запуск корректно завершился ошибкой соединения. Настройки не переписывались. Дополнительный успешный прогон сделан с рабочими локальными настройками приложения, запуском той же консоли из `bin/Debug/net8.0-windows`.

## Проверка инструмента создания тестовых проводок в TESTS

Проверены UI, `SmokeTesting.cs`, `OperationImportService.cs`, `FinanceRegistersScenario.cs`, `CashOrderPostingScenario.cs` и сценарий миграции ссылок пар.

**При первоначальной проверке отдельного генератора проводок с выбором пары счетов, организации и валюты в TESTS не было.** Новый `account-pair-turnover` проверяет отчёт; его контрольные движения создаются в памяти, а Run читает существующие записи. Новых проводок в рабочей базе он не создаёт.

Найденные существующие механизмы:

- `finance-registers`: фиксированные контрольные счета; Run сначала вызывает `CleanupControlDataAsync`, затем вставляет новый набор. В INSERT `amount_currency` установлен в 0; аналитика организации и валюта не заполняются. Такой набор не проверяет полноценный валютный отчёт по выбранной паре.
- `cash-order-posting`: создаёт настоящие тестовые ПКО/РКО с фиксированными счетами и оставляет их после проверки. Это проверка кассовых документов, не универсальный генератор по справочнику пар. У него есть отдельный режим Cleanup, удаляющий тестовые документы/проводки и старые тестовые артефакты.
- `schema-pairs-uuid`: проверяет миграцию ссылок справочника пар; тестовые движения по паре не генерирует.
- JSON/Excel-импорт операций: модель `SmokeTestOperation` содержит счета строки/контрагента и налоговые поля, но не содержит выбранную пару, организацию, код валюты, сумму в валюте и курс как отдельный полный набор для такого генератора.

Эти старые сценарии с изменением/очисткой данных не запускались. По указанию пользователя **все проводки оставлены в базе**. После отдельного поручения создать записи в ОСОО2 добавлен специальный сценарий `account-pair-postings-seed`; его состояние и ограничения указаны ниже.

## Проверки, сборки и ограничения

- Основной проект, Core/Console TESTS и графический TESTS успешно скомпилированы.
- Сценарий ведомости: 21 проверка, все пройдены. Проверены прямые/внешние корреспонденции, одинаковые счета, префиксы, границы периода, выбор организации, остатки без движений, снимки остатков, пустой результат, соответствие детализации оборотам, макет и валюты USD/EUR.
- Сценарий валютных полей: пройден. Проверены все используемые графы обоих макетов и передача ненулевых сумм. В акте контрольная сумма Кт 1800 сом и 20 USD находится в разных графах; валютное конечное сальдо — 80 USD.
- Финальный Run на локальных базах: 3 информационные базы, 31 пара (11 + 10 + 10), без ошибок SQL. Все подключения к проверяемым базам read-only.
- Валютные PDF обоих отчётов отрисованы Poppler и просмотрены: нет обрезанных заголовков, наложений и потерянных граф. Excel дополнительно прочитан обратно через ClosedXML и проверен по значениям ячеек.
- Контрольные PDF/Excel сохранены в `BIS.ERP.TESTS/logs/account-pair-turnover/`. Это искусственные контрольные примеры, не копия рабочего финансового отчёта.
- Само окно приложения вручную не прокликивалось. XAML и обработчики проверены сборкой; миграции регистрации и изменения сохранённых макетов подготовлены к следующему открытию базы.
- Существовавшие предупреждения: CS0162, CS0414; в графическом TESTS — CS1998. NU1900 означает недоступность сетевого аудита NuGet, а не ошибку компиляции. Новых пакетов не добавлялось.
- Версия продукта при проверочных сборках автоматически не увеличивалась. Запущенное приложение не останавливалось. Полная отдельная сборка приложения и TESTS находится в `artifacts/tests-integrated/`. Финальная обычная сборка успешно выполнена в `bin/Debug/net8.0-windows`; отдельная проверенная сборка приложения и TESTS сохранена в `artifacts/tests-integrated/`. Для применения изменений нужно перезапустить приложение и открыть базу заново.

Сборка без конфликта с запущенным EXE:

```powershell
dotnet build BIS.ERP.TESTS/BIS.ERP.TESTS.Console/BIS.ERP.TESTS.Console.csproj --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:ProductVersioningEnabled=false -p:BuildingSolutionFile=true '-p:OutputPath=D:/C#/BIS.ERP/BIS.ERP/artifacts/tests-integrated/' -v:quiet
```

## Что и где менялось

| Файл | Изменение |
| --- | --- |
| Services/AccountPairTurnoverBuilder.cs | Новый SQL-источник и расчёт ведомости, разрешение пар, группировка по организации/валюте, остатки и обороты |
| Services/AccountPairTurnoverLayout.cs | Новый девятиколоночный макет с двухуровневой шапкой, группами и итогами |
| Views/AccountingReportsView.AccountPair.cs | Выбор пары из справочника кнопкой «?», проверка выбора и построение отчёта |
| Views/AccountingReportsView.xaml | Панель выбора пары |
| Views/AccountingReportsView.xaml.cs | Маршрутизация типа отчёта, видимость панели, скрытие служебного поля и формат валютной суммы |
| Services/ClosedXmlLayoutTemplates.cs | Выбор макета ведомости в конструкторе; валютная графа стандартного акта |
| Services/MetadataService.Reports.cs | Стандартное определение ведомости, посадка макета, досадка в существующие базы |
| Services/SchemaMigrationService.cs | Шаги 2026.10.30.06/.07: регистрация, «Модуль», добавление валютной графы акта |
| Services/ReconciliationActBuilder.cs | Валютная сумма в таблице/строках/остатках акта, обозначение валюты и защита от смешанных итогов |
| BIS.ERP.TESTS.Core/AccountPairTurnoverChecks.cs | Контрольные расчёты, Excel/PDF и валюты |
| BIS.ERP.TESTS.Core/AccountPairTurnoverScenario.cs | Интеграция проверок и read-only Run в TESTS |
| BIS.ERP.TESTS.Core/ReconciliationCurrencyFieldsScenario.cs | Проверка всех полей обоих отчётов и ненулевых валютных значений |

Ниже автоматически включены точные изменения относительно Git HEAD и **полный код всех новых и изменённых исходных файлов**. Полные снимки содержат и неизменённый контекст; фактические правки выделены отдельным diff.

## Дополнение: создание пары из отчёта и тестовые записи ОСОО2

По уточнению пользователя окно выбора пар дополнено штатным редактированием: `ReferenceSelectionDialog.ConfigureCatalogEditing(catalog, metadataService)`. Это включает существующие кнопки «Добавить» и «Изменить». Используются реальные поля справочника и карты ссылок, а после выбора справочник перечитывается: новая или изменённая пара не теряется из-за старого списка в памяти. Пустой справочник больше не блокирует открытие окна и добавление первой записи.

В TESTS добавлен сценарий `account-pair-postings-seed`:

- Verify читает готовность только ОСОО2, счета и валюту.
- Run рассчитан на создание четырёх ручных помеченных проводок по 14200000 — 32200000, дата 30.10.2026, номера 999830103001–999830103004, национальные и валютные суммы.
- Создание идёт штатным `CreateDynamicRecordAsync`, как в окне ручной проводки: сохраняются проверки метаданных, даты и журнал событий.
- Предусмотрена отдельная тестовая организация с кодом 999830; повторный запуск не дублирует номера.
- Сценарий не выполняет очистку и не создаёт счета/пару автоматически. Пару пользователь добавляет самостоятельно кнопкой «Добавить» в окне выбора отчёта.
- После создания проверяются источники обоих окон: `MetadataService.GetCatalogDataAsync` для «Проводок» и `PostingService.GetAllPostingsAsync` для «Журнала проводок».

**Первоначальная проверка:** счёт 14200000 отсутствовал, поэтому создание было остановлено. Затем пользователь добавил счета 14200000/32200000 и пару №12 «Тестовый». После этого Run успешно создал четыре тестовые проводки; их реквизиты и ссылки на первичные документы проверены ниже. Существующие проводки не удалялись и не изменялись.

Добавленный исходный файл: `BIS.ERP.TESTS/BIS.ERP.TESTS.Core/AccountPairPostingsSeedScenario.cs`. Полный код также приведён ниже.

### Сборка дополнения с кнопками

Дополнение с кнопками «Добавить/Изменить» и новый сценарий TESTS успешно собраны в `artifacts/tests-integrated/`. Повторная сборка обычного каталога скомпилировала код, но завершилась MSB3027/MSB3021 при замене BIS.ERP.exe, занятого запущенной программой. Процесс не останавливался. Для гарантированного запуска полной обновлённой сборки можно использовать `artifacts/tests-integrated/BIS.ERP.exe` после закрытия старого экземпляра; параметры подключения зависят от appsettings.json этой папки.

### Порядок колонок выбора пары

В окне «Выбор: Пары счетов» колонки «Активен» и «Примечание» переставлены непосредственно после «Валюта», в этом порядке. Правило применяется только к справочнику catalog_advance_payments; остальные окна выбора не меняются. Изменяется DisplayIndex колонок, данные и метаданные справочника не перезаписываются. Правка находится в ReferenceSelectionDialog.ConfigureCatalogEditing и сохраняется при обновлении списка после добавления/изменения записи.

### Неизвестный счёт: предложение добавить его в план счетов

В CatalogItemDialog перед сохранением пары проверяются дебетовый и кредитовый счета по свежему плану счетов. Если введённого счёта нет, показывается предупреждение с кнопками «Да / Нет» (по умолчанию «Нет»).

- «Да» открывает штатную карточку добавления в «План счетов» с предварительно введённым кодом. Запись создаётся только после явного сохранения карточки. Применяются обычные проверки обязательных полей и уникальности кода.
- После сохранения новый счёт выбирается в соответствующем поле пары; реестр пикеров обновляется без закрытия формы.
- «Нет» и отмена карточки счёта сохраняют открытой форму пары со всеми введёнными значениями. Неизвестная ссылка в пару не сохраняется. Пользователь может добавить счёт самостоятельно и повторить сохранение или выбрать другой счёт.
- Существующий счёт, добавленный в другом окне, будет найден при следующем сохранении: план перечитывается перед проверкой.
- Повторное нажатие «Сохранить» во время проверки/добавления блокируется флагом, чтобы не открыть несколько карточек и не создать дубликаты.

Изменения относятся к форме добавления/редактирования «Пар счетов»; произвольные счета автоматически не создаются. Исходный файл: `Views/CatalogItemDialog.xaml.cs`. Проверка сборкой; интерактивные ветви «Да / Нет» на рабочей базе автоматически не нажимались.

### Применение предупреждения о счёте

Сборка с предупреждением «Да / Нет» успешно выполнена в artifacts/tests-integrated. Копирование обновлённых BIS.ERP.dll и BIS.ERP.pdb в обычный bin/Debug/net8.0-windows заблокировано запущенным процессом (файлы используются). Программа принудительно не закрывалась. Для применения последнего дополнения необходимо закрыть BIS ERP и заменить DLL/PDB из проверенной сборки либо запустить эту сборку с соответствующими настройками подключения.

## Исправление GUID и названий модулей после добавления

Причина GUID в дебете/кредите новой пары: окно выбора сохраняло карты ссылок, загруженные при открытии. Если счёт создавался через вложенную карточку позже, его Id отсутствовал в старой карте. В ReloadItemsAsync карты теперь загружаются заново перед ResolveRows. Исправлено отображение новых счетов и других новых ссылок после добавления/изменения без закрытия окна.

В колонке «Остаток брать из модуля» отображение использует ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName: 3, Finance и Финансы показываются как «Финансы». В базе остаются исходные коды; замены сохранённых значений нет.

Добавлен сценарий TESTS `account-pair-reference-refresh`. Он запускает настоящее окно выбора на STA-диспетчере, намеренно исключает счета выбранной пары из исходного кэша, вызывает ReloadItemsAsync и проверяет новые подписи, сохранение выделения и преобразование трёх вариантов значения модуля. Тест на ОСОО2 прошёл с read-only подключением: пара №12, Дт «14200000 - Тест», Кт «32200000 - Тест2». Основная сборка и TESTS успешно собраны. Обновлённые DLL/PDB скопированы в bin/Debug/net8.0-windows; для уже запущенного процесса требуется перезапуск.

## Фактически созданные тестовые проводки ОСОО2

Созданы и оставлены в `bis_OsOO2_20260910_123635` через штатный путь ручной проводки. Дата всех записей: **30.10.2026**. Пара №12: **14200000 — 32200000**, «Тестовый». Отдельная организация: код **999830**, «ТЕСТ ПАРЫ 14200000-32200000 ОСОО2», Id a88f9c4b-640c-486a-aaec-ac4a095ea278. Настройки пользовательской пары не менялись.

| Номер | Дебет | Кредит | Сом | Валюта | Назначение теста |
| --- | --- | --- | ---: | ---: | --- |
| 999830103001 | 14200000 | 61100000 | 9000 | 100 USD | Условное начисление с внешней корреспонденцией |
| 999830103002 | 12100000 | 32200000 | 3600 | 40 USD | Условная оплата с внешней корреспонденцией |
| 999830103003 | 14200000 | 32200000 | 900 | 10 USD | Прямая корреспонденция внутри пары |
| 999830103004 | 14200000 | 61100000 | 1500 | 0 | Начисление в национальной валюте |

Для создания тестовой организации потребовалось обязательное поле «Государство»: сценарий выбирает существующую запись Кыргызстана из справочника. Отсутствующее государство автоматически не создаётся. Повторный Run прошёл: все четыре существующие записи оставлены без изменений, дубликаты не созданы. Обе выборки окон — MetadataService.GetCatalogDataAsync и PostingService.GetAllPostingsAsync — видят все четыре записи с контрольными счетами и суммами.

### Что это за документы в первичных документах

Дополнительно выполнена фактическая проверка четырёх записей командой Verify. У всех тип **«Ручная проводка»**, а `document_table` и `document_id` пусты. Это самостоятельные ручные тестовые проводки. Связанные ПКО, РКО, счета-фактуры и другие первичные документы для них не создавались. «Начисление» и «Оплата» в примечании описывают искусственные случаи теста, а не подтверждают наличие первичного документа. При открытии основания штатный механизм может показать детали самой проводки, поскольку связанного первичного документа нет.

Проверка ничего не удаляла; все четыре записи сохранены в базе. В сценарий Verify добавлен вывод типа, счетов, сумм и ссылок на основание для повторного контроля.

<!-- GENERATED_CODE_START -->

## Точный diff существующих файлов

```diff
diff --git a/Services/ClosedXmlLayoutTemplates.cs b/Services/ClosedXmlLayoutTemplates.cs
index 8407fd9..cfeff79 100644
--- a/Services/ClosedXmlLayoutTemplates.cs
+++ b/Services/ClosedXmlLayoutTemplates.cs
@@ -22,7 +22,7 @@ namespace BIS.ERP.Services
         /// <summary>Заголовки акта — те же, что печатает программный экспорт.</summary>
         private static readonly string[] ActHeaders =
         {
-            ActLabelField, "Дата", "N докум", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Модуль"
+            ActLabelField, "Дата", "N докум", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал.", "Модуль"
         };
 
         /// <summary>Колонка набора ведомости, в которой лежат заголовок и период.</summary>
@@ -84,6 +84,9 @@ namespace BIS.ERP.Services
         /// </summary>
         public static ClosedXmlReportLayout CreateForReport(Report? report)
         {
+            if (report?.ReportType == AccountPairTurnoverBuilder.ReportType)
+                return AccountPairTurnoverLayout.Create(report.Name);
+
             if (IsReconciliationAct(report))
                 return CreateReconciliationAct(report!.Name);
 
@@ -159,7 +162,7 @@ namespace BIS.ERP.Services
                                     {
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 1, MergeToColumn = 8, Text = "АКТ СВЕРКИ",
+                                            Column = 1, MergeToColumn = 9, Text = "АКТ СВЕРКИ",
                                             Bold = true, Align = "Center"
                                         }
                                     }
@@ -171,7 +174,7 @@ namespace BIS.ERP.Services
                                     {
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 1, MergeToColumn = 8,
+                                            Column = 1, MergeToColumn = 9,
                                             TakeFromRow = 1, TakeFromField = ActLabelField,
                                             Bold = true, Align = "Center"
                                         }
@@ -184,7 +187,7 @@ namespace BIS.ERP.Services
                                     {
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 1, MergeToColumn = 8,
+                                            Column = 1, MergeToColumn = 9,
                                             TakeFromRow = 2, TakeFromField = ActLabelField,
                                             Align = "Center"
                                         }
@@ -196,7 +199,7 @@ namespace BIS.ERP.Services
                                     // Объединение задано специально — иначе пустая ячейка не переживёт
                                     // сохранение в конструкторе (там строка собирается из непустых ячеек).
                                     Row = 4,
-                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
+                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                 }
                             }
                         },
@@ -215,7 +218,8 @@ namespace BIS.ERP.Services
                                     new ClosedXmlColumn { Field = ActHeaders[4], Header = ActHeaders[4], Width = 12, Align = "Center" },
                                     new ClosedXmlColumn { Field = ActHeaders[5], Header = ActHeaders[5], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                     new ClosedXmlColumn { Field = ActHeaders[6], Header = ActHeaders[6], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
-                                    new ClosedXmlColumn { Field = ActHeaders[7], Header = ActHeaders[7], Width = 12, Align = "Center" }
+                                    new ClosedXmlColumn { Field = ActHeaders[7], Header = ActHeaders[7], Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
+                                    new ClosedXmlColumn { Field = ActHeaders[8], Header = ActHeaders[8], Width = 12, Align = "Center" }
                                 },
                                 HeaderFill = "#D9E6F2",
                                 HeaderBold = true,
@@ -257,7 +261,7 @@ namespace BIS.ERP.Services
                                     {
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 1, MergeToColumn = 8,
+                                            Column = 1, MergeToColumn = 9,
                                             TakeFromRow = -1, TakeFromField = ActLabelField,
                                             Bold = true
                                         }
@@ -277,7 +281,7 @@ namespace BIS.ERP.Services
                                 new ClosedXmlTextRow
                                 {
                                     Row = 1,
-                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
+                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                 },
                                 new ClosedXmlTextRow
                                 {
@@ -293,7 +297,7 @@ namespace BIS.ERP.Services
                                         new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Руководитель", Bold = true },
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 7, MergeToColumn = 8, Bold = true,
+                                            Column = 7, MergeToColumn = 9, Bold = true,
                                             TakeFromRow = 1, TakeFromField = "counterparty"
                                         }
                                     }
@@ -304,7 +308,7 @@ namespace BIS.ERP.Services
                                     Cells =
                                     {
                                         new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
-                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 8, Text = "___________________________ подпись" }
+                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 9, Text = "___________________________ подпись" }
                                     }
                                 },
                                 new ClosedXmlTextRow
@@ -319,7 +323,7 @@ namespace BIS.ERP.Services
                                         },
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 5, MergeToColumn = 8, Align = "Center",
+                                            Column = 5, MergeToColumn = 9, Align = "Center",
                                             TakeFromRow = 1, TakeFromField = "counterparty_director"
                                         }
                                     }
@@ -327,7 +331,7 @@ namespace BIS.ERP.Services
                                 new ClosedXmlTextRow
                                 {
                                     Row = 5,
-                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
+                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                 },
                                 new ClosedXmlTextRow
                                 {
@@ -343,7 +347,7 @@ namespace BIS.ERP.Services
                                         new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Гл. бухгалтер", Bold = true },
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 7, MergeToColumn = 8, Bold = true,
+                                            Column = 7, MergeToColumn = 9, Bold = true,
                                             TakeFromRow = 1, TakeFromField = "counterparty"
                                         }
                                     }
@@ -354,7 +358,7 @@ namespace BIS.ERP.Services
                                     Cells =
                                     {
                                         new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
-                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 8, Text = "___________________________ подпись" }
+                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 9, Text = "___________________________ подпись" }
                                     }
                                 },
                                 new ClosedXmlTextRow
@@ -369,7 +373,7 @@ namespace BIS.ERP.Services
                                         },
                                         new ClosedXmlTextCell
                                         {
-                                            Column = 5, MergeToColumn = 8, Align = "Center",
+                                            Column = 5, MergeToColumn = 9, Align = "Center",
                                             TakeFromRow = 1, TakeFromField = "counterparty_accountant"
                                         }
                                     }
diff --git a/Services/MetadataService.Reports.cs b/Services/MetadataService.Reports.cs
index d140052..250b213 100644
--- a/Services/MetadataService.Reports.cs
+++ b/Services/MetadataService.Reports.cs
@@ -42,6 +42,18 @@ public partial class MetadataService
         await SeedStateService.MarkAsync(_context, SeedStateService.StandardReportsKey, "Стандартные отчёты");
     }
 
+    /// <summary>Досадка нового отчёта в существующие базы, независимо от версии общего сида.</summary>
+    public async Task EnsureAccountPairTurnoverReportAsync()
+    {
+        var deleted = await new StandardReportDeletionService(_context).GetDeletedCodesAsync();
+        if (deleted.Contains(AccountPairTurnoverBuilder.ReportCode)) return;
+        var reports = await _context.Reports.ToListAsync();
+        var sources = await _context.MetadataObjects.AsNoTracking().Include(item => item.Fields).ToListAsync();
+        var definition = BuildStandardReportDefinitions().Single(item => item.Code == AccountPairTurnoverBuilder.ReportCode);
+        await EnsureStandardReportAsync(definition, reports, sources);
+        await _context.SaveChangesAsync();
+    }
+
     private async Task EnsureCashOrderTurnoverReportSourceAsync()
     {
         var source = await _context.MetadataObjects
@@ -255,6 +267,7 @@ public partial class MetadataService
             // ведомости и журнала счет-фактур свои наборы колонок.
             var seedLayout = definition.ReportType switch
             {
+                "AccountPairTurnover" => AccountPairTurnoverLayout.Create(definition.Name),
                 "TrialBalance" => ClosedXmlLayoutTemplates.CreateTrialBalance(definition.Name),
                 "TurnoverByAccount" => ClosedXmlLayoutTemplates.CreateTurnoverByAccount(definition.Name),
                 "InvoiceJournal" => ClosedXmlLayoutTemplates.CreateInvoiceJournal(definition.Name),
@@ -519,6 +532,17 @@ public partial class MetadataService
                 "posting_date", "doc_number", "document_type", "debit_account",
                 "credit_account", "amount_kgs", "organization_id", "employee_id", "description"
             }),
+        new StandardReportDefinition(
+            Code: AccountPairTurnoverBuilder.ReportCode,
+            Name: AccountPairTurnoverBuilder.ReportName,
+            Description: "Движения каждого счёта выбранной пары с остатками и оборотами по организациям.",
+            ModuleCode: ModuleMetadataService.FinanceCode,
+            SourceName: "Проводки",
+            SourceObjectType: "Document",
+            ReportType: AccountPairTurnoverBuilder.ReportType,
+            Icon: "📊", Order: 152, PageOrientation: "Portrait",
+            Fields: new[] { "posting_date", "doc_number", "debit_account", "credit_account", "amount_kgs", "organization_id", "description", "module_code" },
+            SourceFormat: "ClosedXml", SeedClosedXmlLayout: true, ShowInAccountingReports: true),
         new StandardReportDefinition(
             Code: "standard.finance.reconciliation-act-closedxml",
             Name: "Акт сверки (ClosedXML)",
diff --git a/Services/ReconciliationActBuilder.cs b/Services/ReconciliationActBuilder.cs
index 32be64a..bc2279b 100644
--- a/Services/ReconciliationActBuilder.cs
+++ b/Services/ReconciliationActBuilder.cs
@@ -179,6 +179,7 @@ namespace BIS.ERP.Services
             table.Columns.Add("Кредит", typeof(string));
             table.Columns.Add("Сумма Дт", typeof(decimal));
             table.Columns.Add("Сумма Кт", typeof(decimal));
+            table.Columns.Add("Сумма вал.", typeof(decimal));
             table.Columns.Add("Модуль", typeof(string));
 
             // Колонки под канонические имена макета: механизм подстановки ищет
@@ -421,16 +422,22 @@ namespace BIS.ERP.Services
 
                 var pairName = string.IsNullOrWhiteSpace(pair.AccountPairName) ? pair.CounterAccountName : pair.AccountPairName;
                 AddRow(table, $"Пара счетов : {pair.AccountCode} - {pair.CounterAccountCode} ({pairName})");
-                AddRow(table, $"САЛЬДО НА {start:dd/MM/yyyy}",
-                    debitAmount: NonZeroAmount(pair.OpeningDebit),
-                    creditAmount: NonZeroAmount(pair.OpeningCredit));
-
                 var pairMovements = movements
                     .Where(movement => AccountInPair(movement.DebitAccount, pair) || AccountInPair(movement.CreditAccount, pair))
                     .OrderBy(movement => movement.Date)
                     .ThenBy(movement => movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
                     .ToList();
 
+                var currencyCodes = pairMovements.Select(movement => movement.CurrencyCode)
+                    .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
+                var showCurrencyTotals = pair.UsesCurrency && currencyCodes.Count <= 1;
+                AddRow(table, $"САЛЬДО НА {start:dd/MM/yyyy}",
+                    debitAmount: NonZeroAmount(pair.OpeningDebit),
+                    creditAmount: NonZeroAmount(pair.OpeningCredit),
+                    currencyAmount: showCurrencyTotals ? pair.OpeningDebitCurrency - pair.OpeningCreditCurrency : null);
+                if (currencyCodes.Count > 1)
+                    AddRow(table, "Валютные итоги не суммируются: в паре несколько валют. Суммы указаны в движениях.");
+
                 if (pairMovements.Count == 0 && (pair.TurnoverDebit != 0 || pair.TurnoverCredit != 0))
                     AddRow(table, "Движения найдены в остатках, но детализация проводок недоступна.");
 
@@ -438,33 +445,39 @@ namespace BIS.ERP.Services
                 {
                     var movementDate = movement.Date == default ? end : movement.Date;
                     AddRow(table,
-                        BuildMovementDescription(movement),
+                        BuildMovementDescription(movement) + (string.IsNullOrWhiteSpace(movement.CurrencyCode) ? "" : $" ({movement.CurrencyCode})"),
                         movement.DebitAccount,
                         movement.CreditAccount,
                         AccountInPair(movement.DebitAccount, pair) ? movement.Amount : null,
                         AccountInPair(movement.CreditAccount, pair) ? movement.Amount : null,
                         movement.DocumentNumber,
                         movementDate.ToString("dd/MM/yyyy"),
-                        movement.ModuleCode);
+                        movement.ModuleCode, currencyAmount: movement.AmountCurrency);
                 }
 
                 AddRow(table, "ИТОГО ОБОРОТОВ",
                     debitAmount: NonZeroAmount(pair.TurnoverDebit),
-                    creditAmount: NonZeroAmount(pair.TurnoverCredit));
+                    creditAmount: NonZeroAmount(pair.TurnoverCredit),
+                    currencyAmount: showCurrencyTotals ? pair.TurnoverDebitCurrency - pair.TurnoverCreditCurrency : null);
                 AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy}",
                     debitAmount: NonZeroAmount(pair.ClosingDebit),
-                    creditAmount: NonZeroAmount(pair.ClosingCredit));
+                    creditAmount: NonZeroAmount(pair.ClosingCredit),
+                    currencyAmount: showCurrencyTotals ? pair.ClosingDebitCurrency - pair.ClosingCreditCurrency : null);
             }
 
             if (total != null && pairs.Count > 1)
             {
                 AddRow(table, string.Empty);
+                var oneCurrency = movements.Select(movement => movement.CurrencyCode)
+                    .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                 AddRow(table, "ИТОГО ОБОРОТОВ ПО ОРГАНИЗАЦИИ",
                     debitAmount: NonZeroAmount(total.TurnoverDebit),
-                    creditAmount: NonZeroAmount(total.TurnoverCredit));
+                    creditAmount: NonZeroAmount(total.TurnoverCredit),
+                    currencyAmount: oneCurrency ? total.TurnoverDebitCurrency - total.TurnoverCreditCurrency : null);
                 AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy} ПО ОРГАНИЗАЦИИ",
                     debitAmount: NonZeroAmount(total.ClosingDebit),
-                    creditAmount: NonZeroAmount(total.ClosingCredit));
+                    creditAmount: NonZeroAmount(total.ClosingCredit),
+                    currencyAmount: oneCurrency ? total.ClosingDebitCurrency - total.ClosingCreditCurrency : null);
             }
         }
 
@@ -521,12 +534,13 @@ namespace BIS.ERP.Services
             decimal? creditAmount = null,
             string documentNumber = "",
             string date = "",
-            string moduleCode = "")
+            string moduleCode = "", decimal? currencyAmount = null)
         {
             var row = table.NewRow();
             row["Наименование материала, вид операции"] = operation;
             row["Дебет"] = debit;
             row["Кредит"] = credit;
+            row["Сумма вал."] = (object?)currencyAmount ?? DBNull.Value;
             row["Сумма Дт"] = debitAmount.HasValue ? debitAmount.Value : DBNull.Value;
             row["Сумма Кт"] = creditAmount.HasValue ? creditAmount.Value : DBNull.Value;
             row["N докум"] = documentNumber;
diff --git a/Services/SchemaMigrationService.cs b/Services/SchemaMigrationService.cs
index 458b965..13fe869 100644
--- a/Services/SchemaMigrationService.cs
+++ b/Services/SchemaMigrationService.cs
@@ -139,10 +139,78 @@ namespace BIS.ERP.Services
                 new("2026.10.30.05",
                     "Журнал счет-фактур и Счет-фактура НДС: скрыть по умолчанию",
                     false,
-                    HideInvoiceReportsAsync)
+                    HideInvoiceReportsAsync),
+
+                new("2026.10.30.06",
+                    "Ведомость по выбранной паре счетов ClosedXML и подпись Модуль в актах",
+                    false,
+                    EnsureAccountPairTurnoverReportAsync),
+
+                new("2026.10.30.07",
+                    "Акт ClosedXML: Сумма Дт, Сумма Кт и одна Сумма вал.",
+                    false,
+                    EnsureReconciliationCurrencyColumnAsync)
             };
         }
 
+        private static async Task EnsureReconciliationCurrencyColumnAsync(AppDbContext ctx)
+        {
+            var reports = await ctx.Reports.Where(report => report.ReportType.StartsWith("ReconciliationAct")).ToListAsync();
+            foreach (var report in reports)
+            {
+                if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout)) continue;
+                var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out _);
+                if (layout == null) continue;
+                var changed = false;
+                foreach (var block in layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null))
+                {
+                    var columns = block.Table!.Columns;
+                    if (columns.Any(column => column.Field == "Сумма вал.")) continue;
+                    var creditIndex = columns.FindIndex(column => column.Field == "Сумма Кт");
+                    if (creditIndex < 0) continue;
+                    columns.Insert(creditIndex + 1, new BIS.ERP.Models.ClosedXmlColumn
+                    {
+                        Field = "Сумма вал.", Header = "Сумма вал.", Width = 12,
+                        NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right"
+                    });
+                    // Восьмиколоночные объединения шапки стандартного акта расширяем на новую графу.
+                    foreach (var cell in layout.Sheets.SelectMany(sheet => sheet.Blocks).SelectMany(item => item.Rows).SelectMany(row => row.Cells))
+                        if (cell.MergeToColumn == 8) cell.MergeToColumn = 9;
+                    changed = true;
+                }
+                if (!changed) continue;
+                report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(layout);
+                report.UpdatedAt = DateTime.UtcNow;
+            }
+            await ctx.SaveChangesAsync();
+        }
+
+        private static async Task EnsureAccountPairTurnoverReportAsync(AppDbContext ctx)
+        {
+            await new MetadataService(ctx).EnsureAccountPairTurnoverReportAsync();
+            var reports = await ctx.Reports.Where(report => report.ReportType.StartsWith("ReconciliationAct")).ToListAsync();
+            foreach (var report in reports)
+            {
+                if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout)) continue;
+                var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out _);
+                if (layout == null) continue;
+                var changed = false;
+                bool Legacy(string text) => text.Replace(" ", "").Equals("КодАРМ", StringComparison.OrdinalIgnoreCase);
+                foreach (var block in layout.Sheets.SelectMany(sheet => sheet.Blocks))
+                {
+                    if (block.Table == null) continue;
+                    foreach (var column in block.Table.Columns.Where(column => Legacy(column.Header)))
+                    { column.Header = "Модуль"; changed = true; }
+                    foreach (var cell in block.Table.HeaderLevels.SelectMany(level => level.Cells).Where(cell => Legacy(cell.Text)))
+                    { cell.Text = "Модуль"; changed = true; }
+                }
+                if (!changed) continue;
+                report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(layout);
+                report.UpdatedAt = DateTime.UtcNow;
+            }
+            await ctx.SaveChangesAsync();
+        }
+
         /// <summary>
         /// Шаг 3.3 для «Пар счетов»: в базах, созданных до перехода Reference → uuid,
         /// debit_account/credit_account/currency_id остались text, а приложение
diff --git a/Views/AccountingReportsView.xaml b/Views/AccountingReportsView.xaml
index 7d93bed..05ff94c 100644
--- a/Views/AccountingReportsView.xaml
+++ b/Views/AccountingReportsView.xaml
@@ -53,6 +53,12 @@
                                VerticalAlignment="Center" Margin="16,0,0,0"
                                Foreground="{DynamicResource AppSecondaryTextBrush}"/>
                 </WrapPanel>
+                <WrapPanel x:Name="TurnoverPairPanel" Margin="0,10,0,0" Visibility="Collapsed">
+                    <TextBlock Text="Пара счетов:" VerticalAlignment="Center" Margin="0,0,6,0"/>
+                    <TextBox x:Name="TurnoverPairTextBox" Width="460" Height="32" Padding="8,4" IsReadOnly="True"/>
+                    <Button Content="?" Width="34" Height="32" Margin="6,0,0,0"
+                            Click="OnSelectTurnoverPairClick" ToolTip="Выбрать из справочника пар счетов"/>
+                </WrapPanel>
                 <WrapPanel x:Name="ReconciliationVariantPanel" Margin="0,10,0,0" Visibility="Collapsed">
                     <TextBlock Text="Вариант акта сверки:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                     <ComboBox x:Name="ReconciliationVariantCombo" Width="360" Height="32"
diff --git a/Views/AccountingReportsView.xaml.cs b/Views/AccountingReportsView.xaml.cs
index fc9daf2..ff2735b 100644
--- a/Views/AccountingReportsView.xaml.cs
+++ b/Views/AccountingReportsView.xaml.cs
@@ -668,6 +668,9 @@ namespace BIS.ERP.Views
             DateTime start,
             DateTime end)
         {
+            if (selected.ReportType == AccountPairTurnoverBuilder.ReportType)
+                return await BuildAccountPairTurnoverAsync(start, end);
+
             if (IsReconciliationType(selected.ReportType))
                 return await BuildOrganizationReconciliationAsync(start, end, closedXml: true);
 
@@ -1103,6 +1106,7 @@ namespace BIS.ERP.Views
             SetReportFieldWidth(report, "Кредит", 75);
             SetReportFieldWidth(report, "Сумма Дт", 90, "Right", "N2");
             SetReportFieldWidth(report, "Сумма Кт", 90, "Right", "N2");
+            SetReportFieldWidth(report, "Сумма вал.", 90, "Right", "N2");
             SetReportFieldWidth(report, "N докум", 70);
             SetReportFieldWidth(report, "Дата", 70);
             SetReportFieldWidth(report, "Модуль", 45);
@@ -1348,6 +1352,9 @@ namespace BIS.ERP.Views
             var selected = SelectedAccountingReport;
             var isReconciliation = selected != null && IsReconciliationType(selected.ReportType);
             var isTurnover = selected != null && IsTurnoverType(selected.ReportType);
+            if (TurnoverPairPanel != null)
+                TurnoverPairPanel.Visibility = selected?.ReportType == AccountPairTurnoverBuilder.ReportType
+                    ? Visibility.Visible : Visibility.Collapsed;
 
             if (ClosedXmlReconciliationPanel != null)
             {
@@ -1612,7 +1619,7 @@ namespace BIS.ERP.Views
             "operation_name", "debit_account", "credit_account",
             "debit_amount", "credit_amount", "debit_begin", "credit_begin",
             "document_number", "document_date", "module",
-            "report_title", "report_summary", "period_start", "period_end",
+            "report_title", "report_summary", "period_start", "period_end", "period_text",
             // Стороны акта сверки: подписи читает макет, в сетке они лишние.
             "our_organization", "our_director", "our_accountant",
             "counterparty", "counterparty_director", "counterparty_accountant",
diff --git a/Views/CatalogItemDialog.xaml.cs b/Views/CatalogItemDialog.xaml.cs
index e6f24d0..0ffa577 100644
--- a/Views/CatalogItemDialog.xaml.cs
+++ b/Views/CatalogItemDialog.xaml.cs
@@ -23,6 +23,7 @@ namespace BIS.ERP.Views
         private AccountAnalyticsRegistry? _accountAnalytics;
         private string? _assignedModuleName;
         private readonly TaskCompletionSource<bool> _formBuilt = new(TaskCreationOptions.RunContinuationsAsynchronously);
+        private bool _isSaving;
 
         public Dictionary<string, object> ItemData => _itemData;
 
@@ -1449,6 +1450,9 @@ inputControl = AccountPickerControlFactory.Create(
         /// </summary>
         private async void OnSaveClick(object sender, RoutedEventArgs e)
         {
+            if (_isSaving)
+                return;
+            _isSaving = true;
             try
             {
                 // Ждём завершения построения формы, чтобы не выполнять команды
@@ -1461,6 +1465,9 @@ inputControl = AccountPickerControlFactory.Create(
                     Cursor = null;
                 }
 
+                if (!await EnsureAccountPairAccountsExistAsync())
+                    return;
+
                 foreach (var field in GetEditableFields())
                 {
                     if (!_controls.TryGetValue(field.Name, out var control)) continue;
@@ -1502,6 +1509,79 @@ inputControl = AccountPickerControlFactory.Create(
             {
                 MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
             }
+            finally
+            {
+                _isSaving = false;
+            }
+        }
+
+        /// <summary>Не закрывает пару с неизвестным счётом; предлагает создать его штатной формой.</summary>
+        private async Task<bool> EnsureAccountPairAccountsExistAsync()
+        {
+            if (!IsAccountPairsCatalog())
+                return true;
+
+            async Task<AccountAnalyticsRegistry> ReloadAccountsAsync()
+            {
+                var fresh = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
+                // Пикеры держат общий экземпляр реестра в обработчиках «?».
+                // Обновляем список на месте, чтобы новые счета были видны без закрытия пары.
+                if (_accountAnalytics != null)
+                {
+                    _accountAnalytics.Accounts.Clear();
+                    _accountAnalytics.Accounts.AddRange(fresh.Accounts);
+                }
+                return fresh;
+            }
+
+            var accounts = await ReloadAccountsAsync();
+            foreach (var field in GetEditableFields().Where(field =>
+                         field.DbColumnName is "debit_account" or "credit_account"))
+            {
+                if (!_controls.TryGetValue(field.Name, out var control))
+                    continue;
+                var value = ReadFieldValue(field, control);
+                var code = value?.ToString()?.Trim() ?? string.Empty;
+                if (value == DBNull.Value || code.Length == 0)
+                    continue; // Пустое поле проверяется штатной проверкой обязательности.
+
+                var account = accounts.FindAccount(value);
+                if (account != null)
+                {
+                    AccountPickerControlFactory.SetSelectedAccount(control, account);
+                    continue;
+                }
+
+                var answer = MessageBox.Show(
+                    $"Счёт «{code}» не найден в плане счетов (поле «{field.Name}»).\n\n" +
+                    "Открыть форму добавления счёта в план счетов?\n" +
+                    "Счёт будет создан только после заполнения карточки и нажатия «Сохранить».\n\n" +
+                    "«Нет» — вернуться к паре счетов. Вы сможете выбрать другой счёт или добавить его самостоятельно.",
+                    "Счёт не найден", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
+                if (answer != MessageBoxResult.Yes)
+                {
+                    control.Focus();
+                    return false;
+                }
+
+                var catalogs = await _metadataService.GetCatalogsAsync();
+                var chart = catalogs.FirstOrDefault(item => item.Name.Equals("План счетов", StringComparison.OrdinalIgnoreCase))
+                    ?? throw new InvalidOperationException("Справочник «План счетов» не найден.");
+                var codeField = chart.Fields.FirstOrDefault(item => item.DbColumnName == "code");
+                var initial = new Dictionary<string, object>();
+                if (codeField != null && code.Length <= 8 && code.All(char.IsDigit))
+                    initial[codeField.Name] = code;
+                var dialog = new CatalogItemDialog(chart, _metadataService, initial, isNewRecord: true);
+                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, "Добавление счёта в план счетов") != true)
+                    return false;
+
+                var createdId = await _metadataService.AddCatalogItemAsync(chart.Id, dialog.ItemData);
+                accounts = await ReloadAccountsAsync();
+                var created = accounts.FindAccount(createdId)
+                    ?? throw new InvalidOperationException("Счёт сохранён, но не найден при обновлении плана счетов.");
+                AccountPickerControlFactory.SetSelectedAccount(control, created);
+            }
+            return true;
         }
 
         /// <summary>
@@ -2280,4 +2360,4 @@ inputControl = AccountPickerControlFactory.Create(
         }
     }
 
-}
\ No newline at end of file
+}
diff --git a/Views/Dialogs/ReferenceSelectionDialog.xaml.cs b/Views/Dialogs/ReferenceSelectionDialog.xaml.cs
index eeb7023..ff9a6fe 100644
--- a/Views/Dialogs/ReferenceSelectionDialog.xaml.cs
+++ b/Views/Dialogs/ReferenceSelectionDialog.xaml.cs
@@ -1,4 +1,4 @@
-using BIS.ERP.Models;
+﻿using BIS.ERP.Models;
 using System;
 using System.Collections.Generic;
 using System.Globalization;
@@ -18,7 +18,7 @@ namespace BIS.ERP.Views
         private readonly List<Dictionary<string, object>> _items;
         private readonly string _firstField;
         private readonly string _secondField;
-        private readonly IReadOnlyDictionary<string, Dictionary<Guid, string>>? _referenceMaps;
+        private IReadOnlyDictionary<string, Dictionary<Guid, string>>? _referenceMaps;
         private List<Dictionary<string, object>> _filteredItems;
         private Func<Dictionary<string, object>?, Task>? _extraAction;
         private MetadataObject? _catalog;
@@ -58,6 +58,29 @@ namespace BIS.ERP.Views
             EditButton.Visibility = Visibility.Visible;
             AddButton.IsEnabled = true;
             EditButton.IsEnabled = ItemsGrid.SelectedItem != null;
+
+            if (catalog.TableName.Equals("catalog_advance_payments", StringComparison.OrdinalIgnoreCase))
+            {
+                // В выборе пары служебные признаки идут сразу за валютой.
+                string FieldName(string column) => catalog.Fields.FirstOrDefault(field =>
+                    field.DbColumnName.Equals(column, StringComparison.OrdinalIgnoreCase))?.Name ?? column;
+                var currency = FieldName("currency_id");
+                var active = FieldName("is_active");
+                var note = FieldName("description");
+                var columns = ItemsGrid.Columns.OrderBy(column => column.DisplayIndex).ToList();
+                var trailing = new[] { active, note }.Select(name => columns.FirstOrDefault(column =>
+                    string.Equals(column.Header?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
+                    .OfType<DataGridColumn>().ToList();
+                columns.RemoveAll(column => trailing.Contains(column));
+                var currencyIndex = columns.FindIndex(column =>
+                    string.Equals(column.Header?.ToString(), currency, StringComparison.OrdinalIgnoreCase));
+                if (currencyIndex >= 0)
+                {
+                    columns.InsertRange(currencyIndex + 1, trailing);
+                    for (var index = 0; index < columns.Count; index++)
+                        columns[index].DisplayIndex = index;
+                }
+            }
         }
 
         private void RebuildGrid()
@@ -308,6 +331,9 @@ private async void OnEditClick(object sender, RoutedEventArgs e)
                 return;
 
             var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
+            // Пока была открыта карточка, пользователь мог создать счёт или валюту.
+            // Карта, полученная при открытии выбора, таких Id ещё не содержит.
+            _referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_catalog, _metadataService);
             var reloaded = _referenceMaps != null && _referenceMaps.Count > 0
                 ? ReferenceDisplayHelper.ResolveRows(rows, _referenceMaps)
                 : rows;
@@ -498,6 +524,12 @@ private async void OnEditClick(object sender, RoutedEventArgs e)
                 if (current == null)
                     return "—";
 
+                if (key is "Остаток брать из модуля" or "module_code")
+                {
+                    var moduleName = ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName(current);
+                    return string.IsNullOrWhiteSpace(moduleName) ? current : moduleName;
+                }
+
                 if (current.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                     current.Equals("False", StringComparison.OrdinalIgnoreCase))
                 {
@@ -515,4 +547,4 @@ private async void OnEditClick(object sender, RoutedEventArgs e)
             }
         }
     }
-}
\ No newline at end of file
+}

```

## Полный код исходных файлов

### Services/AccountPairTurnoverBuilder.cs

SHA-256: `19f47e8f8e30036a179f1e66e8907bd69ad6ea260a372b9d7d2b5de5a892b0a5`

```csharp
using System.Data;
using System.Globalization;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services;

public sealed record TurnoverAccountPair(Guid Id, string Code, string Name, string FirstAccount, string SecondAccount);

/// <summary>Движения каждого счёта пары, с остатками и отдельными блоками организаций.</summary>
public static class AccountPairTurnoverBuilder
{
    public const string ReportCode = "standard.finance.account-pair-turnover-closedxml";
    public const string ReportType = "AccountPairTurnover";
    public const string ReportName = "Оборотная ведомость по паре счетов (ClosedXML)";

    public static async Task<List<TurnoverAccountPair>> LoadPairsAsync(MetadataService metadata)
    {
        var registry = await AccountAnalyticsRegistry.LoadAsync(metadata);
        var result = new List<TurnoverAccountPair>();
        foreach (var row in await metadata.GetAdvancePaymentPairsAsync())
        {
            string Text(params string[] keys) => keys.Select(key => row.GetValueOrDefault(key)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
            var active = Text("is_active", "Активен");
            if (active.Equals("false", StringComparison.OrdinalIgnoreCase) || active == "0" || active == "Нет")
                continue;
            var first = registry.FindAccount(Text("debit_account", "Дебет"))?.Code;
            var second = registry.FindAccount(Text("credit_account", "Кредит"))?.Code;
            if (!Guid.TryParse(Text("Id"), out var id) || first == null || second == null)
                continue;
            result.Add(new(id, Text("code", "Код"), Text("name", "Вид расчета"), first, second));
        }
        return result.OrderBy(pair => pair.Code, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<DataTable> LoadAsync(AppDbContext context, TurnoverAccountPair pair,
        Guid? organizationId, DateTime start, DateTime end)
    {
        var movements = await LoadMovementsAsync(context, pair, organizationId, end);
        var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
        var openings = await context.AccountOpeningBalances.AsNoTracking()
            .Where(row => row.BalanceDate <= utcStart).ToListAsync();
        return Build(pair, movements, openings, organizationId, start, end);
    }

    private static async Task<List<ReconciliationMovementRow>> LoadMovementsAsync(AppDbContext context,
        TurnoverAccountPair pair, Guid? organizationId, DateTime end)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.posting_date, p.doc_number, p.debit_account, p.credit_account,
                       COALESCE(p.amount_kgs, 0) AS amount,
                       COALESCE(p.amount_currency, 0) AS amount_currency,
                       COALESCE(c.code, '') AS currency_code,
                       COALESCE(p.description, '') AS description,
                       COALESCE(p.document_type, '') AS document_type,
                       COALESCE(p.module_code, '') AS module,
                       p.organization_id::text AS organization_id,
                       COALESCE(NULLIF(o.name, ''), 'Без организации') AS organization_name,
                       COALESCE(o.code, '') AS organization_code
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON o."Id"::text = p.organization_id::text
                LEFT JOIN catalog_currencies c ON c."Id"::text = p.currency_id::text
                WHERE COALESCE(p.is_active, true) = true AND p.posting_date < @end
                  AND (@organization = '' OR p.organization_id::text = @organization)
                  AND (left(p.debit_account, length(@first)) = @first
                    OR left(p.credit_account, length(@first)) = @first
                    OR left(p.debit_account, length(@second)) = @second
                    OR left(p.credit_account, length(@second)) = @second)
                """;
            void Param(string name, object value)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
            }
            Param("end", DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc));
            Param("organization", organizationId?.ToString() ?? "");
            Param("first", pair.FirstAccount.Trim()); Param("second", pair.SecondAccount.Trim());
            var result = new List<ReconciliationMovementRow>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(new ReconciliationMovementRow
                {
                    Date = reader.GetDateTime(reader.GetOrdinal("posting_date")),
                    DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(reader["doc_number"]?.ToString()),
                    DebitAccount = reader["debit_account"]?.ToString() ?? "",
                    CreditAccount = reader["credit_account"]?.ToString() ?? "",
                    Amount = Convert.ToDecimal(reader["amount"], CultureInfo.InvariantCulture),
                    AmountCurrency = Convert.ToDecimal(reader["amount_currency"], CultureInfo.InvariantCulture),
                    CurrencyCode = reader["currency_code"].ToString() ?? "",
                    Description = reader["description"].ToString() ?? "",
                    DocumentType = reader["document_type"].ToString() ?? "",
                    ModuleCode = reader["module"].ToString() ?? "",
                    OrganizationId = Guid.TryParse(reader["organization_id"]?.ToString(), out var id) ? id : null,
                    OrganizationName = reader["organization_name"].ToString() ?? "",
                    OrganizationCode = reader["organization_code"].ToString() ?? ""
                });
            return result;
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    public static DataTable Build(TurnoverAccountPair pair, IReadOnlyList<ReconciliationMovementRow> movements,
        IReadOnlyList<AccountOpeningBalance> openings, Guid? organizationId, DateTime start, DateTime end)
    {
        if (end.Date < start.Date)
            throw new ArgumentException("Дата окончания периода не может быть раньше даты начала.");
        if (string.IsNullOrWhiteSpace(pair.FirstAccount) || string.IsNullOrWhiteSpace(pair.SecondAccount))
            throw new ArgumentException("В паре должны быть заполнены оба счёта.");
        var table = new DataTable(ReportName);
        foreach (var name in new[] { "Дата", "Документ", "Дебет", "Кредит" })
            table.Columns.Add(name, typeof(string));
        table.Columns.Add("Сумма Дт", typeof(decimal));
        table.Columns.Add("Сумма Кт", typeof(decimal));
        table.Columns.Add("Сумма вал.", typeof(decimal));
        table.Columns.Add("debit_amount", typeof(decimal));
        table.Columns.Add("credit_amount", typeof(decimal));
        table.Columns.Add("Содержание", typeof(string));
        table.Columns.Add("Модуль", typeof(string));
        table.Columns.Add("report_title", typeof(string));
        table.Columns.Add("period_text", typeof(string));

        bool Matches(string code) => code.Trim().StartsWith(pair.FirstAccount.Trim(), StringComparison.OrdinalIgnoreCase)
            || code.Trim().StartsWith(pair.SecondAccount.Trim(), StringComparison.OrdinalIgnoreCase);
        var baselines = openings.Where(row => Matches(row.AccountCode) && row.BalanceDate.Date <= start.Date)
            .GroupBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.BalanceDate).First(),
                StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        Bucket Get(Guid? id, string name, string code, string currency = "")
        {
            var key = (id?.ToString() ?? name) + "|" + currency;
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new Bucket(name, code, currency);
            return bucket;
        }
        // Введённый остаток имеет только счёт, без организации: не приписываем его контрагенту.
        if (!organizationId.HasValue)
            foreach (var opening in baselines.Values.Where(row => row.Debit != 0 || row.Credit != 0))
                Get(null, "Без организации", "").Opening += opening.Debit - opening.Credit;

        foreach (var movement in movements.Where(row => row.Date.Date <= end.Date &&
                     (!organizationId.HasValue || row.OrganizationId == organizationId)))
        {
            AddSide(movement.DebitAccount, movement.CreditAccount, true);
            AddSide(movement.CreditAccount, movement.DebitAccount, false);
            void AddSide(string account, string counter, bool debit)
            {
                if (!Matches(account)) return;
                if (movement.Date.Date < start.Date && baselines.TryGetValue(account.Trim(), out var baseline)
                    && movement.Date.Date < baseline.BalanceDate.Date) return;
                var bucket = Get(movement.OrganizationId, movement.OrganizationName, movement.OrganizationCode, movement.CurrencyCode);
                if (movement.Date.Date < start.Date)
                {
                    bucket.Opening += debit ? movement.Amount : -movement.Amount;
                    bucket.OpeningCurrency += debit ? movement.AmountCurrency : -movement.AmountCurrency;
                }
                else
                    bucket.Lines.Add((movement, account, counter, debit));
            }
        }
        foreach (var bucket in buckets.Values.Where(value => value.Opening != 0 || value.OpeningCurrency != 0 || value.Lines.Count > 0)
                     .OrderBy(value => long.TryParse(value.Code, out var number) ? number : long.MaxValue)
                     .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Add($"Организация: {bucket.Code} {bucket.Name} {bucket.Currency}".Trim());
            foreach (var line in bucket.Lines.OrderBy(line => line.Movement.Date)
                         .ThenBy(line => line.Movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase))
                Add(line.Movement.Date.ToString("dd.MM.yy"), line.Movement.DocumentNumber, line.Movement.DebitAccount, line.Movement.CreditAccount,
                    line.Debit ? line.Movement.Amount : null, line.Debit ? null : line.Movement.Amount,
                    string.IsNullOrWhiteSpace(line.Movement.Description) ? line.Movement.DocumentType : line.Movement.Description,
                    line.Movement.ModuleCode, line.Movement.AmountCurrency);
            var debit = bucket.Lines.Where(line => line.Debit).Sum(line => line.Movement.Amount);
            var credit = bucket.Lines.Where(line => !line.Debit).Sum(line => line.Movement.Amount);
            var currencyDebit = bucket.Lines.Where(line => line.Debit).Sum(line => line.Movement.AmountCurrency);
            var currencyCredit = bucket.Lines.Where(line => !line.Debit).Sum(line => line.Movement.AmountCurrency);
            Add($"ИТОГО ПО ОРГАНИЗАЦИИ {bucket.Code}", debit: debit, credit: credit, currency: currencyDebit - currencyCredit);
            Add("САЛЬДО НА НАЧАЛО", debit: Math.Max(bucket.Opening, 0), credit: Math.Max(-bucket.Opening, 0), currency: bucket.OpeningCurrency);
            Add("ОБОРОТЫ", debit: debit, credit: credit, currency: currencyDebit - currencyCredit);
            var closing = bucket.Opening + debit - credit;
            Add("САЛЬДО НА КОНЕЦ", debit: Math.Max(closing, 0), credit: Math.Max(-closing, 0), currency: bucket.OpeningCurrency + currencyDebit - currencyCredit);
            Add("");
        }
        if (table.Rows.Count == 0) Add("Данных по выбранной паре счетов и организации за период нет.");
        return table;

        void Add(string label, string document = "", string account = "", string counter = "",
            decimal? debit = null, decimal? credit = null, string description = "", string module = "", decimal? currency = null)
        {
            table.Rows.Add(label, document, account, counter,
                (object?)debit ?? DBNull.Value, (object?)credit ?? DBNull.Value,
                (object?)currency ?? DBNull.Value, (object?)debit ?? DBNull.Value, (object?)credit ?? DBNull.Value, description, module,
                $"Оборотная ведомость синтетического и аналитического учета\nпо паре счетов {pair.FirstAccount} - {pair.SecondAccount} ({pair.Name}) — сом / валюта",
                $"с {start:dd.MM.yyyy} по {end:dd.MM.yyyy}");
        }
    }

    private sealed class Bucket(string name, string code, string currency)
    {
        public string Name { get; } = string.IsNullOrWhiteSpace(name) ? "Без организации" : name;
        public string Code { get; } = code;
        public string Currency { get; } = currency;
        public decimal OpeningCurrency { get; set; }
        public decimal Opening { get; set; }
        public List<(ReconciliationMovementRow Movement, string Account, string Counter, bool Debit)> Lines { get; } = new();
    }
}
```

### Services/AccountPairTurnoverLayout.cs

SHA-256: `4fb808905ad527cb34172b4e10ad29c789335347198065237621f45be8816539`

```csharp
using BIS.ERP.Models;

namespace BIS.ERP.Services;

public static class AccountPairTurnoverLayout
{
    public static ClosedXmlReportLayout Create(string? name) => new()
    {
        Sheets =
        {
            new ClosedXmlSheet
            {
                Name = name ?? "Ведомость по паре",
                PageSetup = new ClosedXmlPageSetup
                {
                    Orientation = "Portrait", FitToPagesWide = 1, ShowGridLines = false,
                    FontName = "Times New Roman", FontSize = 9, FreezeRows = 5
                },
                Blocks =
                {
                    new ClosedXmlBlock
                    {
                        Type = "TitleRows", Name = "Шапка ведомости",
                        Rows =
                        {
                            new ClosedXmlTextRow { Row = 1, Cells = { new ClosedXmlTextCell
                            {
                                Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "report_title",
                                Bold = true, Align = "Center", WrapText = true
                            }}},
                            new ClosedXmlTextRow { Row = 2, Cells = { new ClosedXmlTextCell
                            {
                                Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "period_text", Align = "Center"
                            }}},
                            new ClosedXmlTextRow { Row = 3, Cells = { new ClosedXmlTextCell { Column = 1, Text = "" }}}
                        }
                    },
                    new ClosedXmlBlock
                    {
                        Type = "TabularSection", Name = "Движения и остатки по организациям",
                        Table = new ClosedXmlTableSection
                        {
                            Columns =
                            {
                                new ClosedXmlColumn { Field = "Дата", Header = "Дата", Width = 10 },
                                new ClosedXmlColumn { Field = "Документ", Header = "Документ", Width = 12 },
                                new ClosedXmlColumn { Field = "Дебет", Header = "Дебет", Width = 12 },
                                new ClosedXmlColumn { Field = "Кредит", Header = "Кредит", Width = 12 },
                                new ClosedXmlColumn { Field = "Сумма Дт", Header = "Сумма Дт", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Сумма Кт", Header = "Сумма Кт", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Сумма вал.", Header = "Сумма вал.", Width = 14, NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right" },
                                new ClosedXmlColumn { Field = "Содержание", Header = "Содержание", Width = 40 },
                                new ClosedXmlColumn { Field = "Модуль", Header = "Модуль", Width = 10 }
                            },
                            HeaderFill = "", RowBorder = "All", SkipEmptyRows = false,
                            HeaderLevels =
                            {
                                new ClosedXmlHeaderLevel { Cells =
                                {
                                    new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "Дата" },
                                    new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Документ" },
                                    new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Дебет" },
                                    new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "Кредит" },
                                    new ClosedXmlHeaderCell { Column = 5, MergeToColumn = 6, Text = "ОБОРОТЫ — сом" },
                                    new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Сумма вал." },
                                    new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Содержание" },
                                    new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Модуль" }
                                }},
                                new ClosedXmlHeaderLevel { Cells =
                                {
                                    new ClosedXmlHeaderCell { Column = 5, Text = "Сумма Дт" },
                                    new ClosedXmlHeaderCell { Column = 6, Text = "Сумма Кт" }
                                }}
                            },
                            EmphasisRules =
                            {
                                new ClosedXmlEmphasisRule { Text = "Организация:", MergeLabelToColumn = 9, Bold = true, Align = "Center" },
                                new ClosedXmlEmphasisRule { Text = "ИТОГО", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "САЛЬДО", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "ОБОРОТЫ", MergeLabelToColumn = 4, Bold = true },
                                new ClosedXmlEmphasisRule { Text = "Данных", MergeLabelToColumn = 9 }
                            }
                        }
                    }
                }
            }
        }
    };
}
```

### Views/AccountingReportsView.AccountPair.cs

SHA-256: `5e320d6a090ff9e4781d357048fd5284714b6396829b14456b86f493a73bd100`

```csharp
using System.Data;
using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Views;

public partial class AccountingReportsView
{
    private TurnoverAccountPair? _selectedTurnoverPair;

    private async void OnSelectTurnoverPairClick(object sender, RoutedEventArgs e)
    {
        try
        {
            IsEnabled = false;
            var catalog = await _context.MetadataObjects.Include(item => item.Fields)
                .SingleOrDefaultAsync(item => item.ObjectType == "Catalog" && item.TableName == "catalog_advance_payments")
                ?? throw new InvalidOperationException("Не найден справочник «Пары счетов».");
            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(catalog, _metadataService);
            var codeField = catalog.Fields.FirstOrDefault(field => field.DbColumnName == "code")?.Name ?? "Код";
            var nameField = catalog.Fields.FirstOrDefault(field => field.DbColumnName == "name")?.Name ?? "Вид расчета";
            var dialog = new ReferenceSelectionDialog(rows, codeField, nameField, maps) { Title = "Выбор: Пары счетов" };
            dialog.ConfigureCatalogEditing(catalog, _metadataService);
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(Window.GetWindow(this), dialog, dialog.Title) != true
                || dialog.SelectedItem == null) return;
            // В окне выбора пару могли только что создать или изменить: перечитываем её.
            var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(_metadataService);
            if (TryReadGuid(dialog.SelectedItem, out var id, "Id"))
                _selectedTurnoverPair = pairs.FirstOrDefault(pair => pair.Id == id)
                    ?? throw new InvalidOperationException("Выбранная пара неактивна или в ней не заполнены оба счёта из плана счетов.");
            TurnoverPairTextBox.Text = _selectedTurnoverPair == null ? "" :
                $"{_selectedTurnoverPair.FirstAccount} - {_selectedTurnoverPair.SecondAccount} ({_selectedTurnoverPair.Name})";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка выбора пары счетов: {ex.Message}", "Пары счетов", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async Task<(DataTable, Report)> BuildAccountPairTurnoverAsync(DateTime start, DateTime end)
    {
        if (_selectedTurnoverPair == null)
            throw new InvalidOperationException("Выберите пару счетов кнопкой «?».");
        var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(_metadataService);
        var pair = pairs.FirstOrDefault(item => item.Id == _selectedTurnoverPair.Id)
            ?? throw new InvalidOperationException("Выбранная пара удалена, неактивна или содержит недоступные счета. Выберите пару заново.");
        var all = AllOrganizationsCheckBox.IsChecked == true;
        if (!all && (!_selectedOrganizationId.HasValue || _selectedOrganizationId == Guid.Empty))
            throw new InvalidOperationException("Выберите организацию или установите «Все организации».");
        var data = await AccountPairTurnoverBuilder.LoadAsync(_context, pair, all ? null : _selectedOrganizationId, start, end);
        var stored = await _context.Reports.AsNoTracking()
            .SingleOrDefaultAsync(report => report.Code == AccountPairTurnoverBuilder.ReportCode)
            ?? throw new InvalidOperationException("Не найден макет ведомости по паре счетов. Повторно откройте информационную базу.");
        var report = CreateReport(data, AccountPairTurnoverBuilder.ReportName, true);
        report.ReportType = AccountPairTurnoverBuilder.ReportType;
        report.Code = AccountPairTurnoverBuilder.ReportCode;
        report.SourceFormat = "ClosedXml";
        report.Template = string.Empty;
        report.ClosedXmlLayout = stored.ClosedXmlLayout;
        report.PageOrientation = "Portrait";
        report.ShowGrandTotal = false;
        report.AlternateRowColors = false;
        return (data, report);
    }
}
```

### BIS.ERP.TESTS/BIS.ERP.TESTS.Core/AccountPairTurnoverChecks.cs

SHA-256: `d844be18340786989385a6612ec34e56de5f159185efc7ea7ee2ab6e17158fdc`

```csharp
using System.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using ClosedXML.Excel;


namespace BIS.ERP.Testing;

public static class AccountPairTurnoverChecks
{
    public static int Run(string outputDirectory, Action<string>? log = null)
    {
        Directory.CreateDirectory(outputDirectory);
        void Log(string message) => log?.Invoke(message);
        var start = new DateTime(2026, 7, 1);
        var end = new DateTime(2026, 7, 31);
        var org = Guid.NewGuid();
        var other = Guid.NewGuid();
        var pair = new TurnoverAccountPair(Guid.NewGuid(), "1", "Расчёты с заказчиками", "14200000", "32200000");
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
            Log("PASS " + message);
        }
        ReconciliationMovementRow Posting(DateTime date, string debit, string credit, decimal amount,
            string number = "1", Guid? organization = null) => new()
        {
            Date = date, DebitAccount = debit, CreditAccount = credit, Amount = amount, DocumentNumber = number,
            OrganizationId = organization ?? org, OrganizationCode = "2", OrganizationName = "Организация 2",
            Description = "Абонентское обслуживание", ModuleCode = "Finance"
        };
        DataRow Label(DataTable table, string text) => table.Rows.Cast<DataRow>().Single(row => (string)row["Дата"] == text);
        decimal Amount(DataRow row, string column)
        {
            var field = column == "Дебет" ? "debit_amount" : column == "Кредит" ? "credit_amount" : column;
            return row.IsNull(field) ? 0 : (decimal)row[field];
        }
        var movements = new[]
        {
            Posting(start.AddDays(-1), "14200000", "611000", 26700),
            Posting(start, "14200000", "611000", 15000, "151"),
            Posting(end.AddHours(23), "14200000", "611000", 12690, "159"),
            Posting(start.AddDays(1), "121000", "32200000", 26700, "220494"),
            Posting(start, "14200000", "32200000", 1000, "DIRECT"),
            Posting(start, "999", "888", 900, "IGNORE"),
            Posting(end.AddDays(1), "14200000", "611000", 999, "FUTURE")
        };
        var table = AccountPairTurnoverBuilder.Build(pair, movements, [], null, start, end);
        var rows = table.Rows.Cast<DataRow>().ToList();
        Check(rows.Count(row => (string)row["Документ"] == "DIRECT") == 2, "Прямая проводка — две стороны, две строки");
        Check(!rows.Any(row => (string)row["Документ"] is "IGNORE" or "FUTURE"), "Посторонние счета и следующий день исключены");
        Check(Amount(Label(table, "ОБОРОТЫ"), "Дебет") == 28690 && Amount(Label(table, "ОБОРОТЫ"), "Кредит") == 27700,
            "Обороты включают начало периода, весь последний день и внешние корреспонденции");
        Check(Amount(Label(table, "САЛЬДО НА НАЧАЛО"), "Дебет") == 26700 &&
              Amount(Label(table, "САЛЬДО НА КОНЕЦ"), "Дебет") == 27690, "Начальное и конечное сальдо");
        Check(rows.Where(row => !string.IsNullOrEmpty((string)row["Документ"])) .Sum(row => Amount(row, "Дебет")) ==
              Amount(Label(table, "ОБОРОТЫ"), "Дебет"), "Детализация равна итогам оборотов");
        var onlyOpening = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start.AddDays(-1), "611000", "32200000", 504)], [], null, start, end);
        Check(Amount(Label(onlyOpening, "САЛЬДО НА КОНЕЦ"), "Кредит") == 504, "Организация с кредитовым остатком без движений");
        var scoped = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start, "14200000", "611000", 100), Posting(start, "14200000", "611000", 900, organization: other)],
            [], org, start, end);
        Check(Amount(Label(scoped, "ОБОРОТЫ"), "Дебет") == 100, "Отбор конкретной организации");
        var same = pair with { SecondAccount = pair.FirstAccount };
        var sameTable = AccountPairTurnoverBuilder.Build(same, [Posting(start, "14200000", "611000", 100)], [], null, start, end);
        Check(sameTable.Rows.Cast<DataRow>().Count(row => (string)row["Документ"] == "1") == 1, "Одинаковые счета не дублируют сторону");
        var prefixTable = AccountPairTurnoverBuilder.Build(pair with { FirstAccount = "142" },
            [Posting(start, "14200000", "611000", 100)], [], null, start, end);
        Check(Amount(Label(prefixTable, "ОБОРОТЫ"), "Дебет") == 100, "Префикс включает субсчета");
        var opening = new AccountOpeningBalance { AccountCode = "14200000", BalanceDate = start.AddDays(-5), Debit = 100 };
        var baselineTable = AccountPairTurnoverBuilder.Build(pair,
            [Posting(start.AddDays(-6), "14200000", "611000", 999), Posting(start.AddDays(-4), "14200000", "611000", 20)],
            [opening], null, start, end);
        Check(baselineTable.Rows.Cast<DataRow>().Where(row => (string)row["Дата"] == "САЛЬДО НА НАЧАЛО")
            .Sum(row => Amount(row, "Дебет")) == 120, "Снимок остатка исключает повторный учёт старой истории");
        var empty = AccountPairTurnoverBuilder.Build(pair, [], [], null, start, end);
        Check(empty.Rows.Count == 1 && ((string)empty.Rows[0]["Дата"]).StartsWith("Данных"), "Пустой отчёт содержит пояснение");
        var layout = AccountPairTurnoverLayout.Create("По паре счетов");
        using var stream = new MemoryStream(ClosedXmlReportRenderer.RenderToBytes(layout, table));
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheet(1);
        Check(sheet.Cell(4, 9).GetString() == "Модуль", "В Excel заголовок Модуль");
        Check(sheet.Cell(4, 5).GetString() == "ОБОРОТЫ — сом" && sheet.Cell(5, 5).GetString() == "Сумма Дт",
            "Двухуровневая шапка оборотов");
        Check(sheet.MergedRanges.Any(range => range.RangeAddress.FirstAddress.RowNumber == 6 && range.ColumnCount() == 9),
            "Заголовок организации объединён на ширину таблицы");
        Check(sheet.Cell(6, 1).GetString().StartsWith("Организация:"), "В теле нет служебных строк шапки");
        var actLayout = ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт сверки");
        Check(actLayout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
            .SelectMany(block => block.Table!.Columns).Any(column => column.Header == "Модуль"), "Предыдущий акт использует заголовок Модуль");
        var output = Path.Combine(outputDirectory, "account-pair-checks.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        book.SaveAs(output);
        var pdf = ClosedXmlReportRenderer.RenderToPdfBytes(layout, table);
        Check(pdf.Length > 1000 && System.Text.Encoding.ASCII.GetString(pdf, 0, 4) == "%PDF", "Печатный PDF сформирован");
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-checks.pdf"), pdf);
        Log($"PASS: {checks} проверок. Excel: {output}");
        
        var currencyRows = new[]
        {
            new ReconciliationMovementRow { Date = start.AddDays(-1), OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "14200000", CreditAccount = "611000", Amount = 9000, AmountCurrency = 100, CurrencyCode = "USD" },
            new ReconciliationMovementRow { Date = start, OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "611000", CreditAccount = "32200000", Amount = 1800, AmountCurrency = 20, CurrencyCode = "USD", DocumentNumber = "USD-1" },
            new ReconciliationMovementRow { Date = start, OrganizationId = org, OrganizationName = "Контрагент", DebitAccount = "14200000", CreditAccount = "611000", Amount = 1000, AmountCurrency = 10, CurrencyCode = "EUR", DocumentNumber = "EUR-1" }
        };
        var currencyTable = AccountPairTurnoverBuilder.Build(pair, currencyRows, [], null, start, end);
        var currencyClosing = currencyTable.Rows.Cast<DataRow>().Where(row => (string)row["Дата"] == "САЛЬДО НА КОНЕЦ").ToList();
        Check(currencyClosing.Count == 2 && currencyClosing.Any(row => Amount(row, "Сумма вал.") == 80) && currencyClosing.Any(row => Amount(row, "Сумма вал.") == 10), "USD и EUR не смешиваются; валютное начальное сальдо и кредит учтены");
        Check(currencyTable.Rows.Cast<DataRow>().Single(row => (string)row["Документ"] == "USD-1").Field<decimal>("Сумма вал.") == 20, "В движении отображается исходная сумма в валюте");
        Check(currencyTable.Columns["Дебет"]!.DataType == typeof(string) && currencyTable.Columns["Кредит"]!.DataType == typeof(string), "Дебет и Кредит — номера счетов, а не суммы");
        Check(layout.Sheets[0].Blocks.Single(block => block.Table != null).Table!.Columns.Count(column => column.Field == "Сумма Дт") == 1 && layout.Sheets[0].Blocks.Single(block => block.Table != null).Table!.Columns.Count(column => column.Field == "Сумма вал.") == 1, "В макете сохранена Сумма Дт и одна Сумма вал.");
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-currency-checks.xlsx"), ClosedXmlReportRenderer.RenderToBytes(layout, currencyTable));
        File.WriteAllBytes(Path.Combine(outputDirectory, "account-pair-currency-checks.pdf"), ClosedXmlReportRenderer.RenderToPdfBytes(layout, currencyTable));
        return checks;
    }
}
```

### BIS.ERP.TESTS/BIS.ERP.TESTS.Core/AccountPairTurnoverScenario.cs

SHA-256: `ce1311e03f16363d3aad876e2ccd897478a688172dc85cbddb173a1d7a7e9aff`

```csharp
using BIS.ERP.Data;
using BIS.ERP.Services;
using Npgsql;

namespace BIS.ERP.Testing;

/// <summary>Verify: контрольные расчёты и вывод. Run: дополнительно чтение доступных баз.</summary>
public sealed class AccountPairTurnoverScenario : SmokeTestScenarioBase
{
    public override string Code => "account-pair-turnover";
    public override string Name => "Ведомость по паре счетов: расчёт, Excel и PDF";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Проверяет обе стороны пары, остатки, отбор организаций и печатный макет. Run дополнительно проверяет SQL на базах только для чтения.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
            return SmokeTestResult.Failure("Сценарий не создаёт данные в БД и не требует очистки.");
        var details = new List<string>();
        var errors = new List<string>();
        var output = Path.Combine(TestEnvironment.GetLogsDirectory(), "account-pair-turnover");
        try
        {
            var count = AccountPairTurnoverChecks.Run(output, message => { details.Add(message); Report(progress, message); });
            details.Add($"Контрольных проверок: {count}. Артефакты: {output}");
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure("Ошибка контрольного расчёта ведомости.", details.Append(DescribeError(ex)));
        }
        if (options.Command == SmokeTestCommand.Run)
        {
            var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
            var pairCount = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var connection = new NpgsqlConnectionStringBuilder(candidate.ConnectionString)
                        { Options = "-c default_transaction_read_only=on" };
                    await using var context = new AppDbContext(connection.ToString());
                    if (!await HasRequiredTableAsync(context, "doc_postings", cancellationToken)) continue;
                    var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(new MetadataService(context));
                    foreach (var pair in pairs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var data = await AccountPairTurnoverBuilder.LoadAsync(context, pair, null,
                            new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today);
                        if (!data.Columns.Contains("Модуль")) throw new InvalidOperationException("Нет колонки Модуль.");
                        pairCount++;
                        details.Add($"{candidate.DatabaseName}: пара {pair.Code}, строк {data.Rows.Count}, read-only.");
                    }
                }
                catch (Exception ex) { errors.Add(DescribeError(ex, candidate)); }
            }
            if (pairCount == 0) errors.Add("Не проверено ни одной пары на доступных базах.");
        }
        return errors.Count == 0
            ? SmokeTestResult.Success("Ведомость по паре счетов: проверки пройдены.", details.ToArray())
            : SmokeTestResult.Failure("Ведомость по паре счетов: ошибки.", errors.Concat(details));
    }
}
```

### BIS.ERP.TESTS/BIS.ERP.TESTS.Core/ReconciliationCurrencyFieldsScenario.cs

SHA-256: `54715e5637cb19dcb14afc9df0ea82c28476de8738d6595391e56917a84e5192`

```csharp
using System.Data;
using BIS.ERP.Services;
using BIS.ERP.Models;
using ClosedXML.Excel;

namespace BIS.ERP.Testing;

/// <summary>Проверка сквозной доступности валютных полей: набор → макет → вывод.</summary>
public sealed class ReconciliationCurrencyFieldsScenario : SmokeTestScenarioBase
{
    public override string Code => "reconciliation-currency-fields";
    public override string Name => "Акт и ведомость по паре: валютные поля";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Проверяет наличие номеров счетов, суммы и суммы в валюте в таблицах и макетах обоих отчётов. Отсутствующие поля считаются ошибкой, а не успешной пустой проверкой.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var errors = new List<string>();
        var pair = new TurnoverAccountPair(Guid.NewGuid(), "1", "Контроль", "142", "322");
        var start = new DateTime(2026, 7, 1);
        var pairTable = AccountPairTurnoverBuilder.Build(pair, [], [], null, start, start);
        Audit("Ведомость по паре счетов", pairTable, AccountPairTurnoverLayout.Create("Пара"));
        Audit("Акт сверки", ReconciliationActBuilder.CreateTable(), ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт"));
        var balances = new OrganizationBalanceRow
        {
            AccountCode = "142", CounterAccountCode = "322", OrganizationName = "Контрагент", UsesCurrency = true,
            OpeningDebit = 9000, OpeningDebitCurrency = 100, TurnoverCredit = 1800, TurnoverCreditCurrency = 20,
            ClosingDebit = 7200, ClosingDebitCurrency = 80
        };
        var movement = new ReconciliationMovementRow
        {
            Date = start.AddDays(1), DebitAccount = "611", CreditAccount = "322", Amount = 1800,
            AmountCurrency = 20, CurrencyCode = "USD", DocumentNumber = "USD-TEST", ModuleCode = "Finance"
        };
        var act = new ReconciliationActBuilder(null!).BuildTable([balances], [], [movement], start, start.AddDays(30), true, "Контрагент").Table;
        var labelField = ClosedXmlLayoutTemplates.ActLabelField;
        void Expect(bool condition, string message) { if (!condition) errors.Add(message); else details.Add("PASS " + message); }
        Expect(act.Rows.Cast<DataRow>().Single(row => (string)row["N докум"] == "USD-TEST").Field<decimal>("Сумма вал.") == 20,
            "Акт: валютная сумма движения передаётся в таблицу");
        Expect(act.Rows.Cast<DataRow>().Single(row => (string)row[labelField] == $"САЛЬДО НА {start.AddDays(30):dd/MM/yyyy}").Field<decimal>("Сумма вал.") == 80,
            "Акт: конечное валютное сальдо передаётся в таблицу");
        var output = Path.Combine(TestEnvironment.GetLogsDirectory(), "account-pair-turnover");
        Directory.CreateDirectory(output);
        var actLayout = ClosedXmlLayoutTemplates.CreateReconciliationAct("Акт валютный");
        var bytes = ClosedXmlReportRenderer.RenderToBytes(actLayout, act);
        using (var stream = new MemoryStream(bytes))
        using (var book = new XLWorkbook(stream))
        {
            var sheet = book.Worksheet(1);
            var detail = sheet.RowsUsed().Single(row => row.Cell(3).GetString() == "USD-TEST");
            Expect(detail.Cell(7).GetValue<decimal>() == 1800 && detail.Cell(8).GetValue<decimal>() == 20,
                "Акт Excel: национальная сумма Кт и сумма в валюте находятся в разных графах");
        }
        File.WriteAllBytes(Path.Combine(output, "reconciliation-currency-checks.xlsx"), bytes);
        File.WriteAllBytes(Path.Combine(output, "reconciliation-currency-checks.pdf"), ClosedXmlReportRenderer.RenderToPdfBytes(actLayout, act));
        return Task.FromResult(errors.Count == 0
            ? SmokeTestResult.Success("Валютные поля обоих отчётов доступны в наборе и макете.", details.ToArray())
            : SmokeTestResult.Failure("Валютные поля: обнаружены пропуски отображения.", errors.Concat(details)));

        void Audit(string name, DataTable data, BIS.ERP.Models.ClosedXmlReportLayout layout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
                .SelectMany(block => block.Table!.Columns).Select(column => column.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            details.Add($"{name}: поля набора — {string.Join(", ", data.Columns.Cast<DataColumn>().Select(column => column.ColumnName))}.");
            foreach (var field in new[] { "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал." })
            {
                if (!data.Columns.Contains(field)) errors.Add($"{name}: в наборе нет поля «{field}».");
                if (!fields.Contains(field)) errors.Add($"{name}: в макете нет поля «{field}».");
            }
            foreach (var field in fields.Where(field => !data.Columns.Contains(field)))
                errors.Add($"{name}: макет ссылается на отсутствующее поле «{field}».");
            var columns = layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null)
                .SelectMany(block => block.Table!.Columns).ToList();
            foreach (var field in new[] { "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал." })
                if (columns.Count(column => column.Field == field) != 1)
                    errors.Add($"{name}: поле «{field}» должно присутствовать в макете один раз.");
            Report(progress, $"{name}: проверены валютные поля набора и макета.");
        }
    }
}
```

### BIS.ERP.TESTS/BIS.ERP.TESTS.Core/AccountPairPostingsSeedScenario.cs

SHA-256: `2842948d2de8257f9bd9301183f796805f3017f49bfe2c7401ae46ed163f45e4`

```csharp
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Testing;

/// <summary>Явное создание сохраняемых тестовых проводок только в ОСОО2. Очистки нет.</summary>
public sealed class AccountPairPostingsSeedScenario : SmokeTestScenarioBase
{
    private const string Marker = "ТЕСТ ПАРЫ 14200000-32200000 ОСОО2";
    private const string TestCode = "999830";
    private static readonly DateTime PostingDate = new(2026, 10, 30);
    public override string Code => "account-pair-postings-seed";
    public override string Name => "ОСОО2: создать и оставить проводки по паре счетов";
    public override string Category => "Финансовые отчёты";
    public override string Description => "Verify проверяет готовность ОСОО2. Run создаёт четыре помеченные проводки по паре 14200000–32200000 на 30.10.2026 и оставляет их в базе. Повторный запуск не дублирует записи.";
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.Command == SmokeTestCommand.Cleanup)
            return SmokeTestResult.Failure("Удаление отключено: тестовые проводки должны остаться в ОСОО2.");
        var details = new List<string>();
        try
        {
            var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
            var targets = candidates.Where(item => item.DatabaseName.StartsWith("bis_OsOO2_", StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.ConnectionString).Select(group => group.First()).ToList();
            if (targets.Count != 1)
                return SmokeTestResult.Failure($"Ожидалась ровно одна база ОСОО2, найдено {targets.Count}. Записей не создано.");
            var target = targets[0];
            await using var context = new AppDbContext(target.ConnectionString);
            var metadata = new MetadataService(context);
            var objects = await context.MetadataObjects.Include(item => item.Fields).ToListAsync(cancellationToken);
            MetadataObject Object(string table) => objects.Single(item => item.TableName == table);
            var postingObject = Object("doc_postings");
            var organizations = Object("catalog_organizations");
            var currencies = Object("catalog_currencies");
            var pairsObject = Object("catalog_advance_payments");
            var accounts = await AccountAnalyticsRegistry.LoadAsync(metadata);
            foreach (var actualPair in await AccountPairTurnoverBuilder.LoadPairsAsync(metadata))
                details.Add($"Доступная пара {actualPair.Code}: {actualPair.FirstAccount} - {actualPair.SecondAccount}, {actualPair.Name}.");
            details.Add("Счета 14*/32*: " + string.Join(", ", accounts.Accounts.Where(item => item.Code.StartsWith("14") || item.Code.StartsWith("32")).Select(item => item.Code)));
            var first = accounts.FindAccount("14200000") ?? throw new Exception("В плане счетов ОСОО2 нет 14200000.");
            var second = accounts.FindAccount("32200000") ?? throw new Exception("В плане счетов ОСОО2 нет 32200000.");
            var externalDebit = accounts.FindAccount("121000") ?? accounts.FindAccount("12100000")
                ?? accounts.Accounts.FirstOrDefault(item => item.Code != first.Code && item.Code != second.Code)
                ?? throw new Exception("Не найден третий счёт для внешней корреспонденции.");
            var externalCredit = accounts.FindAccount("611000") ?? accounts.FindAccount("61100000") ?? externalDebit;
            var currencyRows = await metadata.GetCatalogDataAsync(currencies.Id);
            var usd = currencyRows.FirstOrDefault(row => Text(row, "code", "Код").Equals("USD", StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("В справочнике валют ОСОО2 нет USD. Записей не создано.");
            var usdId = Guid.Parse(Text(usd, "Id"));
            var pairs = await AccountPairTurnoverBuilder.LoadPairsAsync(metadata);
            var pair = pairs.FirstOrDefault(item => item.FirstAccount == first.Code && item.SecondAccount == second.Code);
            details.Add($"База: {target.DatabaseName}; пара: {first.Code} - {second.Code}; {(pair == null ? "пару необходимо добавить пользователю" : $"код пары {pair.Code}, {pair.Name}")}.");
            details.Add($"Внешние счета: {externalDebit.Code}, {externalCredit.Code}; валюта USD; дата {PostingDate:dd.MM.yyyy}.");
            details.Add("Тестовый эквивалент: 90 сом за 1 USD, только для контрольного примера.");
            foreach (var field in postingObject.Fields.OrderBy(field => field.Order))
                details.Add($"Поле проводки: {field.Name} = {field.DbColumnName} ({field.FieldType}), обязательное: {field.IsRequired}.");
            if (options.Command == SmokeTestCommand.Verify)
            {
                var storedRows = await metadata.GetCatalogDataAsync(postingObject.Id);
                foreach (var row in storedRows)
                {
                    string Value(string column) => Text(row,
                        postingObject.Fields.FirstOrDefault(field => field.DbColumnName == column)?.Name ?? column, column);
                    if (!Value("doc_number").StartsWith("99983010300", StringComparison.Ordinal)) continue;
                    details.Add($"ЗАПИСЬ {Value("doc_number")}: тип «{Value("document_type") }», Дт {Value("debit_account")}, Кт {Value("credit_account")}, " +
                        $"сумма {Value("amount_kgs")} сом, валюта {Value("amount_currency")}; " +
                        $"document_table=«{Value("document_table")}», document_id=«{Value("document_id")}»; {Value("description")}.");
                }
                return SmokeTestResult.Success("ОСОО2: готовность и существующие тестовые проводки проверены. Verify ничего не записывает.", details.ToArray());
            }

            // Используем тот же путь, что диалог ручной проводки: проверки даты,
            // метаданных, номеров и штатный журнал событий остаются действующими.
            if (pair == null)
                throw new Exception("Сначала добавьте пару 14200000 - 32200000 кнопкой «Добавить» в бухгалтерском отчёте. Пара автоматически не создаётся.");
            var organizationRows = await metadata.GetCatalogDataAsync(organizations.Id);
            var organization = organizationRows.FirstOrDefault(row => Text(row, "name", "Наименование") == Marker);
            var organizationData = new Dictionary<string, object>
            {
                ["code"] = TestCode, ["name"] = Marker, ["full_name"] = Marker,
                ["is_active"] = true, ["is_primary"] = false
            };
            if (organization == null)
            {
                var countryField = organizations.Fields.FirstOrDefault(field => field.IsRequired &&
                    (field.Name == "Государство" || field.DbColumnName.Contains("country", StringComparison.OrdinalIgnoreCase)));
                if (countryField != null)
                {
                    var countryCatalog = objects.FirstOrDefault(item => item.Name == countryField.ReferenceCatalog)
                        ?? throw new Exception("Не найден справочник государств для тестовой организации.");
                    var countries = await metadata.GetCatalogDataAsync(countryCatalog.Id);
                    var country = countries.FirstOrDefault(row => row.Values.Any(value =>
                        value?.ToString()?.Contains("Кыргыз", StringComparison.OrdinalIgnoreCase) == true ||
                        value?.ToString()?.Contains("Киргиз", StringComparison.OrdinalIgnoreCase) == true));
                    if (country == null) throw new Exception("В справочнике государств не найден Кыргызстан.");
                    organizationData[countryField.DbColumnName] = Guid.Parse(Text(country, "Id"));
                }
            }
            var organizationId = organization == null
                ? await metadata.AddCatalogItemAsync(organizations.Id, Map(organizations, organizationData))
                : Guid.Parse(Text(organization, "Id"));
            var cases = new[]
            {
                (Number: "999830103001", Debit: first.Code, Credit: externalCredit.Code, Amount: 9000m, Currency: 100m, Text: "Начисление, USD"),
                (Number: "999830103002", Debit: externalDebit.Code, Credit: second.Code, Amount: 3600m, Currency: 40m, Text: "Оплата, USD"),
                (Number: "999830103003", Debit: first.Code, Credit: second.Code, Amount: 900m, Currency: 10m, Text: "Прямая корреспонденция пары, USD"),
                (Number: "999830103004", Debit: first.Code, Credit: externalCredit.Code, Amount: 1500m, Currency: 0m, Text: "Начисление, сом")
            };
            var existing = await metadata.GetCatalogDataAsync(postingObject.Id);
            foreach (var item in cases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var previous = existing.FirstOrDefault(row => Text(row, "doc_number", "Номер документа", "Номер") == item.Number);
                if (previous != null)
                {
                    if (!Text(previous, "description", "Примечание", "Содержание", "Описание").StartsWith(Marker, StringComparison.Ordinal))
                        throw new Exception($"Номер {item.Number} уже занят другой записью. Она не изменена.");
                    details.Add($"{item.Number}: ранее созданная тестовая запись оставлена без изменений.");
                    continue;
                }
                var data = new Dictionary<string, object>
                {
                    ["posting_date"] = PostingDate, ["doc_number"] = item.Number,
                    ["document_type"] = "Ручная проводка", ["module_code"] = "Finance",
                    ["debit_account"] = item.Debit, ["credit_account"] = item.Credit,
                    ["amount_kgs"] = item.Amount, ["amount_currency"] = item.Currency,
                    ["organization_id"] = organizationId, ["description"] = Marker + ": " + item.Text,
                    ["is_active"] = true
                };
                if (item.Currency != 0) data["currency_id"] = usdId;
                var id = await metadata.CreateDynamicRecordAsync(postingObject.Id, Map(postingObject, data));
                details.Add($"Создана {item.Number}: Дт {item.Debit}, Кт {item.Credit}, {item.Amount:N2} сом, {item.Currency:N2} USD, Id {id}.");
            }
            // Проверяем именно два источника, которыми пользуются окна программы.
            var savedRows = await metadata.GetCatalogDataAsync(postingObject.Id);
            var journal = await new PostingService(context).GetAllPostingsAsync(PostingDate, PostingDate);
            foreach (var item in cases)
            {
                if (!savedRows.Any(row => Text(row, "doc_number", "Номер документа", "Номер") == item.Number))
                    throw new Exception($"Проводки: не видна запись {item.Number}.");
                var saved = journal.SingleOrDefault(row => row.DocumentNumber == item.Number)
                    ?? throw new Exception($"Журнал проводок: не видна запись {item.Number}.");
                if (saved.Amount != item.Amount || saved.AmountCurrency != item.Currency ||
                    saved.DebitAccount != item.Debit || saved.CreditAccount != item.Credit || !saved.IsActive)
                    throw new Exception($"Журнал проводок: поля записи {item.Number} отличаются от контрольных.");
            }
            details.Add($"Организация: код {TestCode}, {Marker}; Id {organizationId}.");
            details.Add("Все 4 записи видны источникам окон «Проводки» и «Журнал проводок». Записи оставлены в базе; удаления нет.");
            return SmokeTestResult.Success("ОСОО2: тестовые проводки созданы и сохранены.", details.ToArray());
        }
        catch (Exception ex)
        {
            return SmokeTestResult.Failure("Создание тестовых проводок ОСОО2: ошибка. Уже созданные тестовые записи не удалялись.", details.Append(DescribeError(ex)));
        }
    }

    private static string Text(Dictionary<string, object> row, params string[] keys) =>
        keys.Select(key => row.GetValueOrDefault(key)?.ToString()).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static Dictionary<string, object> Map(MetadataObject metadata, Dictionary<string, object> values) =>
        values.Where(item => metadata.Fields.Any(field => field.DbColumnName == item.Key))
            .ToDictionary(item => metadata.Fields.First(field => field.DbColumnName == item.Key).Name, item => item.Value);
}
```

### Services/ClosedXmlLayoutTemplates.cs

SHA-256: `c07bebf9f9292ff990428987a4fc872519c65ccf87ee2d9622cb187e3e713fef`

```csharp
using BIS.ERP.Models;
using System;
using System.Collections.Generic;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Готовые разметки ClosedXML-макета, построенные по РЕАЛЬНОМУ программному отчёту
    /// системы, а не «на глазок»:
    /// <list type="bullet">
    ///   <item>«Акт сверки» — по <c>ReconciliationActBuilder</c> и
    ///     <c>PrintFormService.BuildReconciliationActExcel</c>: 8 настоящих колонок,
    ///     плоский поток строк, выделение важных строк по содержимому.</item>
    /// </list>
    /// Класс намеренно не зависит от WPF: его используют и конструктор, и вывод отчёта.
    /// </summary>
    public static class ClosedXmlLayoutTemplates
    {
        /// <summary>Колонка набора акта, в которой лежат заголовок акта и период.</summary>
        public const string ActLabelField = "Наименование материала, вид операции";

        /// <summary>Заголовки акта — те же, что печатает программный экспорт.</summary>
        private static readonly string[] ActHeaders =
        {
            ActLabelField, "Дата", "N докум", "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Сумма вал.", "Модуль"
        };

        /// <summary>Колонка набора ведомости, в которой лежат заголовок и период.</summary>
        public const string TrialBalanceTitleField = "report_title";

        /// <summary>Заголовки ведомости — те же, что печатает программный вывод.</summary>
        private static readonly string[] TrialBalanceHeaders =
        {
            "Счет", "Наименование",
            "Сальдо на начало Дт", "Сальдо на начало Кт",
            "Оборот Дт", "Оборот Кт",
            "Сальдо на конец Дт", "Сальдо на конец Кт"
        };

        public static ClosedXmlReportLayout CreateBlank() => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = "Лист1",
                    PageSetup = new ClosedXmlPageSetup(),
                    Blocks = { CreateBlock("TitleRows", 1) }
                }
            }
        };

        /// <summary>Программный акт сверки — единственный отчёт этого вида.</summary>
        public static bool IsReconciliationAct(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("Акт сверки", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Разметка по умолчанию для отчёта: у акта сверки и оборотной ведомости
        /// визуальный макет подставляется сразу, поэтому «программный» отчёт
        /// можно открыть и править мышью.
        /// </summary>
        /// <summary>Признак счёта-фактуры по форме СТ-1.</summary>
        public static bool IsVatInvoiceSt1(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "VatInvoiceSt1", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("СТ-1", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Разметка по умолчанию для отчёта: у акта сверки и оборотной ведомости
        /// визуальный макет подставляется сразу, поэтому «программный» отчёт
        /// можно открыть и править мышью.
        /// </summary>
        public static ClosedXmlReportLayout CreateForReport(Report? report)
        {
            if (report?.ReportType == AccountPairTurnoverBuilder.ReportType)
                return AccountPairTurnoverLayout.Create(report.Name);

            if (IsReconciliationAct(report))
                return CreateReconciliationAct(report!.Name);

            if (IsTrialBalance(report))
                return CreateTrialBalance(report!.Name);

            if (IsInvoiceJournal(report))
                return CreateInvoiceJournal(report!.Name);

            if (IsVatInvoiceSt1(report))
                return CreateVatInvoiceSt1(report!.Name);

            if (IsCashReceiptOrder(report))
                return CreateCashReceiptOrder(report!.Name);

            if (IsCashPaymentOrder(report))
                return CreateCashPaymentOrder(report!.Name);

            return CreateBlank();
        }

        // =============================================================
        // Акт сверки — ТОЧНЫЙ перенос программного Excel
        // (PrintFormService.BuildReconciliationActExcel).
        //
        // Раскладка листа:
        //   строка 1  — «АКТ СВЕРКИ», объединение A:H, жирный, по центру
        //   строка 2  — заголовок акта из 1-й строки набора, объединение A:H, жирный, по центру
        //   строка 3  — период из 2-й строки набора, объединение A:H, по центру
        //   строка 4  — пустая
        //   строка 5  — шапка 8 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 6+ — тело: строки набора без «АКТ…», «Период…» и без итоговой фразы
        //   2 пустые строки и итоговая фраза задолженности (последняя строка набора), A:H, жирный
        // =============================================================
        /// <summary>Признак бланка расходного кассового ордера (КО-2).</summary>
        private static bool IsCashPaymentOrder(Report? report) =>
            report != null &&
            (report.Code ?? string.Empty).StartsWith("cash.payment.", StringComparison.OrdinalIgnoreCase);

        /// <summary>Признак бланка приходного кассового ордера (КО-1).</summary>
        private static bool IsCashReceiptOrder(Report? report) =>
            report != null &&
            (report.Code ?? string.Empty).StartsWith("cash.receipt.", StringComparison.OrdinalIgnoreCase);

        public static ClosedXmlReportLayout CreateReconciliationAct(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Акт сверки" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 4,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка акта",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9, Text = "АКТ СВЕРКИ",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9,
                                            TakeFromRow = 1, TakeFromField = ActLabelField,
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9,
                                            TakeFromRow = 2, TakeFromField = ActLabelField,
                                            Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка 4: в программном Excel между периодом и шапкой пусто.
                                    // Объединение задано специально — иначе пустая ячейка не переживёт
                                    // сохранение в конструкторе (там строка собирается из непустых ячеек).
                                    Row = 4,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица акта",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = ActHeaders[0], Header = ActHeaders[0], Width = 44, Align = "Left" },
                                    new ClosedXmlColumn { Field = ActHeaders[1], Header = ActHeaders[1], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[2], Header = ActHeaders[2], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[3], Header = ActHeaders[3], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[4], Header = ActHeaders[4], Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = ActHeaders[5], Header = ActHeaders[5], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[6], Header = ActHeaders[6], Width = 12, Align = "Center", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[7], Header = ActHeaders[7], Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = ActHeaders[8], Header = ActHeaders[8], Width = 12, Align = "Center" }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                // «АКТ…» и «Период…» уже в шапке, итог задолженности выводится отдельно,
                                // пустые строки набора программный экспорт тоже не печатает.
                                SkipRowsStartingWith = { "АКТ", "Период", "Задолженность" },
                                SkipEmptyRows = true,
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "Contains", Text = "Пара счетов", Bold = true },
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "САЛЬДО", Bold = true },
                                    new ClosedXmlEmphasisRule { Match = "Contains", Text = "ИТОГО", Bold = true }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Итог задолженности",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells = { new ClosedXmlTextCell { Column = 1, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells = { new ClosedXmlTextCell { Column = 1, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Итоговая фраза — последняя строка набора (TakeFromRow = -1).
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9,
                                            TakeFromRow = -1, TakeFromField = ActLabelField,
                                            Bold = true
                                        }
                                    }
                                }
                            }
                        },
                        // Подписи сторон: слева — наша первичная организация, справа — контрагент.
                        // Наименования и расшифровки подписей приходят из карточек организаций,
                        // поэтому в макете стоят привязки, а не текст.
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Подписи сторон",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Руководитель", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 3, MergeToColumn = 4, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "our_organization"
                                        },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Руководитель", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 7, MergeToColumn = 9, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "counterparty"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 9, Text = "___________________________ подпись" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 4, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "our_director"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 5, MergeToColumn = 9, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "counterparty_director"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = string.Empty } }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Гл. бухгалтер", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 3, MergeToColumn = 4, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "our_organization"
                                        },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 6, Text = "Гл. бухгалтер", Bold = true },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 7, MergeToColumn = 9, Bold = true,
                                            TakeFromRow = 1, TakeFromField = "counterparty"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "___________________________ подпись" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 9, Text = "___________________________ подпись" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 4, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "our_accountant"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 5, MergeToColumn = 9, Align = "Center",
                                            TakeFromRow = 1, TakeFromField = "counterparty_accountant"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        /// <summary>Признак оборотно-сальдовой ведомости.</summary>
        public static bool IsTrialBalance(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "TrialBalance", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("Оборот", StringComparison.OrdinalIgnoreCase));
        }

        // =============================================================
        // Оборотно-сальдовая ведомость — перенос программного вывода.
        //
        // Раскладка листа:
        //   строка 1  — наименование ведомости, объединение A:H, жирный, по центру
        //   строка 2  — период, объединение A:H, по центру
        //   строка 3  — шапка 8 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 4+ — тело: счета ведомости, последняя строка набора — «ИТОГО»
        // =============================================================
        public static ClosedXmlReportLayout CreateTrialBalance(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Оборотная ведомость" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 3,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка ведомости",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            TakeFromRow = 1, TakeFromField = TrialBalanceTitleField,
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            TakeFromRow = 2, TakeFromField = TrialBalanceTitleField,
                                            Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка между периодом и шапкой таблицы.
                                    Row = 3,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 8, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица ведомости",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[0], Header = TrialBalanceHeaders[0], Width = 14, Align = "Left" },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[1], Header = TrialBalanceHeaders[1], Width = 40, Align = "Left" },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[2], Header = TrialBalanceHeaders[2], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[3], Header = TrialBalanceHeaders[3], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[4], Header = TrialBalanceHeaders[4], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[5], Header = TrialBalanceHeaders[5], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[6], Header = TrialBalanceHeaders[6], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = TrialBalanceHeaders[7], Header = TrialBalanceHeaders[7], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                SkipEmptyRows = true,
                                // Наименование ведомости и период уже выведены в шапке,
                                // и в теле таблицы они не печатаются.
                                SkipRowsStartingWith = { "Оборотно-сальдовая", "Оборотная", "Период" },
                                // Строка итогов выделяется жирным. Отдельной строки-заголовка
                                // у ведомости нет: заголовок и период выведены в шапке.
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ИТОГО", Bold = true }
                                }
                            }
                        }
                    }
                }
            }
        };

        /// <summary>Признак оборотной ведомости по счёту с группировкой.</summary>
        public static bool IsTurnoverByAccount(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "TurnoverByAccount", StringComparison.OrdinalIgnoreCase);
        }

        // =============================================================
        // Оборотная ведомость синтетического и аналитического учёта по счёту.
        //
        // Перенос печатной формы: проводки по счёту, сгруппированные по организации.
        //
        // Раскладка листа:
        //   строка 1  — «Оборотная ведомость синтетического и аналитического учета»
        //   строка 2  — «по счету … с … по …»
        //   строка 3  — пустая
        //   строка 4  — шапка: ОБОРОТЫ-сом над Дебет и Кредит (два уровня)
        //   тело      — заголовок организации, проводки, «ИТОГО по организации»
        //   конец     — общий итог по суммам
        //
        // Источник данных — системный расчёт system.movements с параметром счёта.
        // =============================================================
        public static ClosedXmlReportLayout CreateTurnoverByAccount(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Оборотная ведомость по счету" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 4,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка ведомости",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            Text = "Оборотная ведомость синтетического и аналитического учета",
                                            Bold = true, Align = "Center", FontSize = 12
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 8,
                                            Text = "по счету {AccountCode} с {PeriodStart:dd/MM/yyyy} по {PeriodEnd:dd/MM/yyyy}",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Проводки по счету",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderFill = "#FFFFFF",
                                RowBorder = "All",
                                HeaderRowHeight = 34,

                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "posting_date", Header = "Дата", Width = 11, Align = "Center", NumberFormat = "dd/MM/yy" },
                                    new ClosedXmlColumn { Field = "doc_number", Header = "Документ", Width = 12, Align = "Center" },
                                    new ClosedXmlColumn { Field = "counter_account_code", Header = "Корр. счет", Width = 11, Align = "Center" },
                                    new ClosedXmlColumn { Field = "debit_amount", Header = "Дебет", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "credit_amount", Header = "Кредит", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "description", Header = "Содержание", Width = 34, Align = "Left" },
                                    new ClosedXmlColumn { Field = "currency_code", Header = "Валюта", Width = 10, Align = "Center" },
                                    new ClosedXmlColumn { Field = "amount_currency", Header = "Сумма в валюте", Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "exchange_rate", Header = "Курс", Width = 10, Align = "Right", NumberFormat = "#,##0.0000", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "module_code", Header = "Код АРМ", Width = 9, Align = "Center" }
                                },

                                // Шапка: «ОБОРОТЫ-сом» перекрывает «Дебет» и «Кредит».
                                HeaderLevels =
                                {
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "Дата" },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Документ" },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Корр. счет" },
                                            new ClosedXmlHeaderCell { Column = 4, MergeToColumn = 5, Text = "(ОБОРОТЫ-сом)" },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "Содержание" },
                                            new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Валюта" },
                                            new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Сумма в валюте" },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Курс" },
                                            new ClosedXmlHeaderCell { Column = 10, RowSpan = 2, Text = "Код АРМ" }
                                        }
                                    },
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 4, Text = "Дебет" },
                                            new ClosedXmlHeaderCell { Column = 5, Text = "Кредит" }
                                        }
                                    }
                                },

                                // Группировка по организации: заголовок с номером и
                                // наименованием, затем проводки, затем итог группы.
                                Groups =
                                {
                                    new ClosedXmlGroup
                                    {
                                        // Заголовок группы: «1 ОсОО "Comtec-Soft"».
                                        // Подытог показывает номер: «ИТОГО по 1».
                                        ByFields = { "organization_code", "organization_id" },
                                        SubtotalValueField = "organization_code",
                                        ShowHeader = true,
                                        HeaderFormat = "{organization_code} {organization_id}",
                                        HeaderBold = true,
                                        Subtotal = new ClosedXmlTotal
                                        {
                                            Label = "ИТОГО по",
                                            SumFields = { "debit_amount", "credit_amount" },
                                            Bold = true
                                        }
                                    }
                                },

                                // Общий итог по суммам в конце ведомости.
                                GrandTotal = new ClosedXmlTotal
                                {
                                    Label = "ИТОГО",
                                    SumFields = { "debit_amount", "credit_amount" },
                                    Bold = true
                                }
                            }
                        }
                    }
                }
            }
        };

        /// <summary>Заголовки журнала счет-фактур.</summary>
        private static readonly string[] InvoiceJournalHeaders =
        {
            "Дата", "N докум", "Организация", "ЭСФ", "Вид документа",
            "Без налога", "НДС", "Налог с продаж", "Всего", "Состояние"
        };

        /// <summary>Признак журнала счет-фактур.</summary>
        public static bool IsInvoiceJournal(Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "InvoiceJournal", StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(report.Name) &&
                       report.Name.Contains("счет-фактур", StringComparison.OrdinalIgnoreCase));
        }

        // =============================================================
        // Журнал счет-фактур — образец отчёта, собранного администратором.
        //
        // Раскладка листа:
        //   строка 1  — заголовок из набора, объединение A:J, жирный, по центру
        //   строка 2  — пустая
        //   строка 3  — шапка 10 колонок, заливка #D9E6F2, жирный, по центру
        //   строки 4+ — записи журнала
        // =============================================================
        public static ClosedXmlReportLayout CreateInvoiceJournal(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Журнал счет-фактур" : sheetName,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 3,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 9
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Заголовок журнала",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 10,
                                            TakeFromRow = 1, TakeFromField = "report_title",
                                            Bold = true, Align = "Center"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 10, Text = string.Empty } }
                                }
                            }
                        },
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Журнал",
                            Table = new ClosedXmlTableSection
                            {
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "doc_date", Header = InvoiceJournalHeaders[0], Width = 13, Align = "Center", NumberFormat = "dd.MM.yyyy" },
                                    new ClosedXmlColumn { Field = "doc_number", Header = InvoiceJournalHeaders[1], Width = 11, Align = "Center" },
                                    new ClosedXmlColumn { Field = "org_name", Header = InvoiceJournalHeaders[2], Width = 22, Align = "Left" },
                                    new ClosedXmlColumn { Field = "esf_number", Header = InvoiceJournalHeaders[3], Width = 14, Align = "Center" },
                                    new ClosedXmlColumn { Field = "document_kind", Header = InvoiceJournalHeaders[4], Width = 13, Align = "Center" },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = InvoiceJournalHeaders[5], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_total", Header = InvoiceJournalHeaders[6], Width = 13, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "sales_tax_total", Header = InvoiceJournalHeaders[7], Width = 14, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "total_amount", Header = InvoiceJournalHeaders[8], Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "posted_state", Header = InvoiceJournalHeaders[9], Width = 13, Align = "Center" }
                                },
                                HeaderFill = "#D9E6F2",
                                HeaderBold = true,
                                HeaderWrapText = false,
                                RowBorder = "Outside",
                                SkipEmptyRows = true,
                                // Строка «ИТОГО» строки набора не имеет: сумму считает запрос при необходимости.
                                EmphasisRules =
                                {
                                    new ClosedXmlEmphasisRule { Match = "StartsWith", Text = "ИТОГО", Bold = true }
                                }
                            }
                        }
                    }
                }
            }
        };

        // =============================================================
        // Счёт-фактура по форме СТ-1 — образец форматного макета.
        //
        // Раскладка (число колонок = 13):
        //   строка 1   — «СЧЕТ-ФАКТУРА», A:M, жирный, по центру
        //   строка 2   — «(рег. N … от …)», A:M, по центру
        //   строки 3–7 — реквизиты: слева продавец, справа покупатель
        //   строки 8–9 — шапка 13 графов: два верхних уровня
        //   строка 10  — нижний уровень: номера и подписи графов
        //   строки 11+ — позиции счёта-фактуры
        //
        // Поворот подписей 90°: подписи графов стоят вертикально, как в бланке.
        // =============================================================
        public static ClosedXmlReportLayout CreateVatInvoiceSt1(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Счет-фактура СТ-1" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Landscape",
                        FitToPagesWide = 1,
                        FreezeRows = 10,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        // --- Заголовок формы ---
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Заголовок формы",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 13, Text = "СЧЕТ-ФАКТУРА",
                                            Bold = true, Align = "Center", FontSize = 12
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 13,
                                            Text = "по форме СТ-1 (рег. N 283 от 12.08.2026)",
                                            Align = "Center"
                                        }
                                    }
                                }
                            }
                        },

                        // --- Реквизиты: слева продавец, справа покупатель ---
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Реквизиты сторон",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            Text = "Продавец:", Bold = true, Align = "Left"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            Text = "Покупатель:", Bold = true, Align = "Left"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_name"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_name"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_inn_text"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_inn_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_address_text"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "buyer_address_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            Text = "Номер и дата регистрационного удостоверения:"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            Text = "Расчетный счет, банк:"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 7,
                                            TakeFromRow = 1, TakeFromField = "seller_reg_number"
                                        },
                                        new ClosedXmlTextCell
                                        {
                                            Column = 8, MergeToColumn = 13,
                                            TakeFromRow = 1, TakeFromField = "seller_bank_text"
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    // Пустая строка-разделитель перед таблицей.
                                    Row = 7,
                                    Cells = { new ClosedXmlTextCell { Column = 1, MergeToColumn = 13, Text = string.Empty } }
                                }
                            }
                        },

                        // --- Таблица формы: 13 графов ---
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Таблица СТ-1",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderRowHeight = 110,
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "line_number", Header = "N п/п", Width = 6, Align = "Center" },
                                    new ClosedXmlColumn { Field = "item_name", Header = "Наименование товаров, работ, услуг", Width = 30, Align = "Left" },
                                    new ClosedXmlColumn { Field = "unit_name", Header = "Ед. изм.", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "quantity", Header = "Кол-во", Width = 10, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "price", Header = "Цена", Width = 12, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = "Стоимость без налога", Width = 15, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_rate", Header = "Ставка", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "vat_amount", Header = "Сумма", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "sales_tax_rate", Header = "Ставка", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "sales_tax_amount", Header = "Сумма", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "line_total", Header = "Всего", Width = 14, Align = "Right", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "tax_code", Header = "Код", Width = 8, Align = "Center" },
                                    new ClosedXmlColumn { Field = "vat_tax_code", Header = "Код", Width = 8, Align = "Center" }
                                },
                                HeaderLevels =
                                {
                                    // Верхний уровень: названия групп графов.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "N п/п", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "Наименование товаров, работ, услуг", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "Ед. изм.", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "Кол-во", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 5, RowSpan = 2, Text = "Цена", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "Стоимость без налога", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 7, MergeToColumn = 8, RowSpan = 2, Text = "НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 9, MergeToColumn = 10, RowSpan = 2, Text = "Налог с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 11, RowSpan = 2, Text = "Всего", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 12, MergeToColumn = 13, RowSpan = 2, Text = "Код", TextRotation = 90 }
                                        }
                                    },
                                    // Средний уровень: подписи внутри групп занимают две
                                    // нижние строки — под ними стоят номера графов.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 7, RowSpan = 2, Text = "Ставка НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 8, RowSpan = 2, Text = "Сумма НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "Ставка налога с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 10, RowSpan = 2, Text = "Сумма налога с продаж", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 12, RowSpan = 2, Text = "Код НДС", TextRotation = 90 },
                                            new ClosedXmlHeaderCell { Column = 13, RowSpan = 2, Text = "Код налога с продаж", TextRotation = 90 }
                                        }
                                    },
                                    // Нижний уровень: номера графов 1..13.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Номера граф",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, Text = "1", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 2, Text = "2", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 3, Text = "3", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 4, Text = "4", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 5, Text = "5", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 6, Text = "6", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 7, Text = "7", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 8, Text = "8", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 9, Text = "9", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 10, Text = "10", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 11, Text = "11", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 12, Text = "12", TextRotation = 0 },
                                            new ClosedXmlHeaderCell { Column = 13, Text = "13", TextRotation = 0 }
                                        }
                                    }
                                },
                                HeaderFill = "#EFEFEF",
                                HeaderBold = true,
                                RowBorder = "All"
                            }
                        },

                        // --- Подписи ---
                        new ClosedXmlBlock
                        {
                            Type = "SignaturesRow",
                            Name = "Подписи",
                            Signatures =
                            {
                                new ClosedXmlSignature { Text = "Руководитель ______________", Column = 1, MergeToColumn = 4 },
                                new ClosedXmlSignature { Text = "Главный бухгалтер ______________", Column = 8, MergeToColumn = 13 }
                            }
                        }
                    }
                }
            }
        };

        // =============================================================
        // Счёт-фактура НДС (форма с РНИ/БИК, сомы) — образец бланка на одной странице.
        //
        // Раскладка (число колонок = 9, как графы бланка):
        //   шапка   — заголовок, 102 НОМЕР, 103 ДАТА, пары пунктов 201/301 … 206/306,
        //             401/403 и 404/405: шапка идёт до строк 404/405, ниже — таблица
        //             позиций этого документа;
        //   таблица — позиции счёта-фактуры, двухуровневая шапка НДС («НДС» → «ставка», «сумма»);
        //   итоги   — «ИТОГО:» по колонкам и «ВСЕГО:»;
        //   строки  — «Налог с продаж по ставке … %»: и метку, и сумму отдаёт набор данных;
        //   подписи — М.П., РУКОВОДИТЕЛЬ, ГЛАВНЫЙ БУХГАЛТЕР.
        //
        // Реквизитов покупателя и РНИ/БИК в документе нет: они подставляются
        // параметрами отчёта ({BuyerName}, {BuyerInn}, {SellerRni}, …) — администратор
        // задаёт их на вкладке «Параметры». Ячейки под цифры рисуются рамками,
        // а значение печатается целиком: разложить символы по клеткам макет не умеет.
        // =============================================================
        public static ClosedXmlReportLayout CreateVatInvoiceVatKg(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Счет-фактура НДС" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        // --- Шапка: от заголовка до строк 404/405 ---
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Шапка бланка",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell
                                        {
                                            Column = 1, MergeToColumn = 9, Text = "СЧЕТ-ФАКТУРА НДС",
                                            Bold = true, Align = "Center", FontSize = 12
                                        }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "102 НОМЕР:", Bold = true, Border = "All" },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 6, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 7, MergeToColumn = 9, Text = "103 ДАТА:", Bold = true, Border = "All" }
                                    }
                                },
                                // Дата печатается по клеткам: восемь клеток — день (2), месяц (2), год (4).
                                // Значения режет запрос (SUBSTRING), макет только раскладывает их по клеткам.
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, Text = "дата", Bold = true, Border = "All" },
                                        new ClosedXmlTextCell { Column = 2, TakeFromRow = 1, TakeFromField = "doc_day_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, TakeFromRow = 1, TakeFromField = "doc_day_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 4, TakeFromRow = 1, TakeFromField = "doc_month_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 5, TakeFromRow = 1, TakeFromField = "doc_month_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, TakeFromRow = 1, TakeFromField = "doc_year_1", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 7, TakeFromRow = 1, TakeFromField = "doc_year_2", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, TakeFromRow = 1, TakeFromField = "doc_year_3", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 9, TakeFromRow = 1, TakeFromField = "doc_year_4", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "ПОСТАВЩИК", Bold = true, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, Text = "ПОКУПАТЕЛЬ", Bold = true, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "201 ИНН", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_inn", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "301 ИНН", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_inn", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "202 Ф.И.О/наименование предприятия", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_name", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "302 Ф.И.О/наименование предприятия", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_name", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "203 адрес", Border = "All" },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_address", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "303 адрес", Border = "All" },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_address", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "204 код и наименование РНИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, Text = "{SellerRni}", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "304 код и наименование РНИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_reg_number", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "205 код (БИК) и наименование банка", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_bank_text", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "305 код (БИК) и наименование банка", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_bank", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "206 расчётный счёт", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "seller_account", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "306 расчётный счёт", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "buyer_account", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "401 ДАТА И ТИП ПОСТАВКИ", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "supply_kind_text", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "403 СПОСОБ ПЛАТЕЖА", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "payment_kind_text", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "404 КОРРЕКТИРОВКА", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 5, Text = "к счет-фактуре серии ______ № ______ от ________", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 7, Text = "405 Причины корректировки", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 8, MergeToColumn = 9, Text = "{CorrectionReason}", Border = "All", WrapText = true }
                                    }
                                }
                            }
                        },

                        // --- Таблица позиций: 9 графов, двухуровневая шапка НДС ---
                        new ClosedXmlBlock
                        {
                            Type = "TabularSection",
                            Name = "Позиции счёт-фактуры",
                            Table = new ClosedXmlTableSection
                            {
                                HeaderRowHeight = 54,
                                Columns =
                                {
                                    new ClosedXmlColumn { Field = "group_code", Header = "код группы товаров", Width = 9, Align = "Center" },
                                    new ClosedXmlColumn { Field = "item_name", Header = "наименование товаров, работ(услуг)", Width = 30, Align = "Left" },
                                    new ClosedXmlColumn { Field = "unit_name", Header = "ед. изм.", Width = 7, Align = "Center" },
                                    new ClosedXmlColumn { Field = "quantity", Header = "количество (объем)", Width = 10, Align = "Right", NumberFormat = "#,##0.###" },
                                    new ClosedXmlColumn { Field = "price", Header = "цена (сом)", Width = 11, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "amount_without_tax", Header = "стоимость товаров, работ, услуг без НДС", Width = 16, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "vat_rate", Header = "ставка", Width = 8, Align = "Center", NumberFormat = "#,##0.00" },
                                    new ClosedXmlColumn { Field = "vat_amount", Header = "сумма(сом)", Width = 12, Align = "Right", NumberFormat = "#,##0.00", SuppressZero = true },
                                    new ClosedXmlColumn { Field = "line_total", Header = "всего стоим. реализации (сом)", Width = 15, Align = "Right", NumberFormat = "#,##0.00" }
                                },
                                HeaderLevels =
                                {
                                    // Верхний уровень: графы, под которыми второго уровня нет,
                                    // занимают обе строки шапки — для этого у них RowSpan = 2.
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 1",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 1, RowSpan = 2, Text = "код группы товаров" },
                                            new ClosedXmlHeaderCell { Column = 2, RowSpan = 2, Text = "наименование товаров, работ(услуг)" },
                                            new ClosedXmlHeaderCell { Column = 3, RowSpan = 2, Text = "ед. изм." },
                                            new ClosedXmlHeaderCell { Column = 4, RowSpan = 2, Text = "количество (объем)" },
                                            new ClosedXmlHeaderCell { Column = 5, RowSpan = 2, Text = "цена (сом)" },
                                            new ClosedXmlHeaderCell { Column = 6, RowSpan = 2, Text = "стоимость товаров, работ, услуг без НДС" },
                                            new ClosedXmlHeaderCell { Column = 7, MergeToColumn = 8, Text = "НДС" },
                                            new ClosedXmlHeaderCell { Column = 9, RowSpan = 2, Text = "всего стоим. реализации (сом)" }
                                        }
                                    },
                                    new ClosedXmlHeaderLevel
                                    {
                                        Name = "Уровень 2",
                                        Cells =
                                        {
                                            new ClosedXmlHeaderCell { Column = 7, Text = "ставка" },
                                            new ClosedXmlHeaderCell { Column = 8, Text = "сумма(сом)" }
                                        }
                                    }
                                },
                                // ИТОГО по колонкам бланка и отдельная строка ВСЕГО.
                                GrandTotal = new ClosedXmlTotal
                                {
                                    Label = "ИТОГО:",
                                    SumFields = { "amount_without_tax", "vat_amount", "line_total" }
                                },
                                ExtraTotals =
                                {
                                    new ClosedXmlTotal
                                    {
                                        Label = "ВСЕГО:",
                                        SumFields = { "line_total" }
                                    }
                                }
                            }
                        },

                        // --- «Налог с продаж по ставке … %»: метку и сумму отдаёт набор ---
                        new ClosedXmlBlock
                        {
                            Type = "FreeRows",
                            Name = "Налог с продаж",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 6, TakeFromRow = 1, TakeFromField = "sales_tax_label" },
                                        new ClosedXmlTextCell { Column = 7, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "total_sales_tax", Align = "Right" }
                                    }
                                }
                            }
                        },

                        // --- Подписи ---
                        new ClosedXmlBlock
                        {
                            Type = "SignaturesRow",
                            Name = "Подписи",
                            Signatures =
                            {
                                new ClosedXmlSignature { Text = "М.П.", Column = 1, MergeToColumn = 2 },
                                new ClosedXmlSignature { Text = "РУКОВОДИТЕЛЬ ______________", Column = 3, MergeToColumn = 5 },
                                new ClosedXmlSignature { Text = "ГЛАВНЫЙ БУХГАЛТЕР ______________", Column = 6, MergeToColumn = 9 }
                            }
                        }
                    }
                }
            }
        };

        public static ClosedXmlBlock CreateBlock(string type, int ordinal) => type switch
        {

            "TabularSection" => new ClosedXmlBlock
            {
                Type = "TabularSection",
                Name = $"Таблица {ordinal}",
                Table = new ClosedXmlTableSection
                {
                    Columns =
                    {
                        new ClosedXmlColumn { Field = "Поле1", Header = "Колонка 1", Width = 20 },
                        new ClosedXmlColumn { Field = "Поле2", Header = "Колонка 2", Width = 14, NumberFormat = "#,##0.00", Align = "Right" }
                    }
                }
            },
            "FreeRows" => new ClosedXmlBlock
            {
                Type = "FreeRows",
                Name = $"Строки {ordinal}",
                Rows =
                {
                    new ClosedXmlTextRow
                    {
                        Row = 1,
                        Cells = { new ClosedXmlTextCell { Column = 1, Text = "Текст" } }
                    }
                }
            },
            "SignaturesRow" => new ClosedXmlBlock
            {
                Type = "SignaturesRow",
                Name = $"Подписи {ordinal}",
                Signatures =
                {
                    new ClosedXmlSignature { Text = "Руководитель __________", Column = 1 },
                    new ClosedXmlSignature { Text = "Бухгалтер __________", Column = 5 }
                }
            },
            "PageBreak" => new ClosedXmlBlock
            {
                Type = "PageBreak",
                Name = $"Разрыв {ordinal}"
            },
            _ => new ClosedXmlBlock
            {
                Type = "TitleRows",
                Name = $"Заголовок {ordinal}",
                Rows =
                {
                    new ClosedXmlTextRow
                    {
                        Row = 1,
                        Cells =
                        {
                            new ClosedXmlTextCell
                            {
                                Column = 1,
                                MergeToColumn = 6,
                                Text = "Заголовок",
                                Bold = true,
                                FontSize = 14,
                                Align = "Center"
                            }
                        }
                    }
                }


            }
        };

        /// <summary>
        /// Бланк «ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР» (КО-1) с отрывной квитанцией.
        ///
        /// Левая половина — сам ордер, правая — квитанция. Шапка бланка (наименование, ОКПО, ИНН)
        /// берётся из карточки первичной организации документа, остальные клетки — из полей набора
        /// данных ПКО: номер, дата, дебет/кредит, сумма, сумма прописью, принято от, основание;
        /// для квитанции дополнительно дата словами. Разметку удобно доводить в конструкторе.
        /// </summary>
        public static ClosedXmlReportLayout CreateCashReceiptOrder(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Приходный кассовый ордер" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Бланк КО-1",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "Унифицированная форма №КО-1", FontSize = 7, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Код", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "0310001", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, FontSize = 9, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "Утверждена постановлением Госкомстата КР", FontSize = 6 },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Форма по ОКУД", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "0310001", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 25, Text = "ОКПО", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 26, MergeToColumn = 32, TakeFromRow = 1, TakeFromField = "company_okpo" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "по ОКПО", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "company_okpo", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 25, Text = "ИНН", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 26, MergeToColumn = 32, TakeFromRow = 1, TakeFromField = "company_inn" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "ИНН", FontSize = 7, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "company_inn", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "ПРИХОДНЫЙ КАССОВЫЙ ОРДЕР", Bold = true, FontSize = 11, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "КВИТАНЦИЯ", Bold = true, FontSize = 12, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, Text = "Номер документа", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, Text = "Дата составления", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 37, Text = "к приходному кассовому ордеру №", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "doc_number", Bold = true, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "ДЕБЕТ", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 10, Text = "КРЕДИТ", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 19, Text = "СУММА", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Принято от", Bold = true, FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, TakeFromRow = 1, TakeFromField = "debit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 10, TakeFromRow = 1, TakeFromField = "credit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_text", Align = "Right", Border = "All" },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "payer_name", Bold = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Принято от", Bold = true, FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "payer_name", Bold = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Основание", Bold = true, FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Основание", Bold = true, FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 13,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, TakeFromRow = 1, TakeFromField = "amount_text", Italic = true },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "amount_text", Italic = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 14,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Приложение ____________________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 15,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Главный бухгалтер _______________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 30, MergeToColumn = 40, Text = "М. П.", FontSize = 7, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 16,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 19, Text = "Получил кассир __________________________________", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, TakeFromRow = 1, TakeFromField = "date_text", Bold = true, Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 17,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Главный бухгалтер ______________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 18,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 22, MergeToColumn = 40, Text = "Получил кассир __________________________", FontSize = 7 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };


        /// <summary>
        /// Бланк «РАСХОДНЫЙ КАССОВЫЙ ОРДЕР» (КО-2).
        ///
        /// Шапка бланка (наименование, ОКПО, подписи руководителя и главного бухгалтера) берётся
        /// из карточки первичной организации документа, остальные клетки — из полей набора данных
        /// кассовых ордеров: номер, дата, дебет/кредит, сумма, сумма прописью, получатель и основание.
        /// Разметку удобно доводить в конструкторе.
        /// </summary>
        public static ClosedXmlReportLayout CreateCashPaymentOrder(string? sheetName) => new()
        {
            Sheets =
            {
                new ClosedXmlSheet
                {
                    Name = string.IsNullOrWhiteSpace(sheetName) ? "Расходный кассовый ордер" : sheetName,
                    LayoutKind = ClosedXmlLayoutKinds.Formatted,
                    PageSetup = new ClosedXmlPageSetup
                    {
                        Orientation = "Portrait",
                        FitToPagesWide = 1,
                        ShowGridLines = false,
                        FontName = "Times New Roman",
                        FontSize = 8
                    },
                    Blocks =
                    {
                        new ClosedXmlBlock
                        {
                            Type = "TitleRows",
                            Name = "Бланк КО-2",
                            Rows =
                            {
                                new ClosedXmlTextRow
                                {
                                    Row = 1,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 7, TakeFromRow = 1, TakeFromField = "company_name", Bold = true, FontSize = 9, WrapText = true },
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "Код", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, Text = "", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 2,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "формы по ОКУД", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, Text = "0310002", FontSize = 6, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 3,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 9, MergeToColumn = 12, Text = "по ОКПО", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 16, TakeFromRow = 1, TakeFromField = "company_okpo", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 4,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 17, Text = "РАСХОДНЫЙ КАССОВЫЙ ОРДЕР", Bold = true, FontSize = 11, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 5,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 12, MergeToColumn = 14, Text = "Номер документа", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 17, Text = "Дата составления", FontSize = 6, Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 6,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 12, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "doc_number", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 17, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 7,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "Дебет: Код структурного подразделения", FontSize = 5, Align = "Center", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, Text = "Корреспондирующий счёт", FontSize = 5, Align = "Center", Border = "All", WrapText = true },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, Text = "Кредит", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 15, Text = "Сумма", FontSize = 6, Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 16, MergeToColumn = 18, Text = "Код целевого назначения", FontSize = 5, Align = "Center", Border = "All", WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 8,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 5, Text = "", Border = "All" },
                                        new ClosedXmlTextCell { Column = 6, MergeToColumn = 9, TakeFromRow = 1, TakeFromField = "debit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, TakeFromRow = 1, TakeFromField = "credit_account", Align = "Center", Border = "All" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 15, TakeFromRow = 1, TakeFromField = "amount_text", Align = "Right", Border = "All" },
                                        new ClosedXmlTextCell { Column = 16, MergeToColumn = 18, Text = "", Border = "All" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 9,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "Выдать", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 4, TakeFromRow = 1, TakeFromField = "recipient_number", Align = "Center" },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "recipient_name", Bold = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 10,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Основание", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "basis", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 11,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Сумма", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 12,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 9, Text = "№ довер. ______________________ от", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 10, MergeToColumn = 12, TakeFromRow = 1, TakeFromField = "doc_date_text", Align = "Center" },
                                        new ClosedXmlTextCell { Column = 13, MergeToColumn = 20, Text = "на имя ______________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 13,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "Руководитель", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "director_name", WrapText = true },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 20, Text = "расшифровка подписи", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 14,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 4, Text = "Гл. бухгалтер", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 5, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 14, TakeFromRow = 1, TakeFromField = "accountant_name", WrapText = true },
                                        new ClosedXmlTextCell { Column = 15, MergeToColumn = 20, Text = "расшифровка подписи", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 15,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Получил", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, TakeFromRow = 1, TakeFromField = "amount_in_words", Italic = true, WrapText = true }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 16,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 20, Text = "(сумма прописью)", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 17,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 10, Text = "«____» ____________________ 20___ г.", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 20, Text = "Получил ______________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 18,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 2, Text = "По", FontSize = 7 },
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 20, Text = "______________________________________________________________________________________________", FontSize = 7 }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 19,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 3, MergeToColumn = 20, Text = "(наименование, номер, дата и место выдачи документа, удостоверяющего личность получателя)", FontSize = 5, Align = "Center" }
                                    }
                                },
                                new ClosedXmlTextRow
                                {
                                    Row = 20,
                                    Cells =
                                    {
                                        new ClosedXmlTextCell { Column = 1, MergeToColumn = 3, Text = "Выдал кассир", FontSize = 8 },
                                        new ClosedXmlTextCell { Column = 4, MergeToColumn = 10, Text = "________________ подпись", FontSize = 6, Align = "Center" },
                                        new ClosedXmlTextCell { Column = 11, MergeToColumn = 20, Text = "__________________________ расшифровка подписи", FontSize = 5 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

    }
}
```

### Services/MetadataService.Reports.cs

SHA-256: `3b4c1804f6aae9720bab09fde943bb1a045c290f5c4daa520609c90ddfee26dd`

```csharp
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services;

public partial class MetadataService
{
    public const string CashOrderTurnoverReportSourceName = "РКО/ПКО обороты по кассе";
    public const string CashOrderTurnoverReportSourceTableName = "report_cash_order_turnover_source";

    public async Task EnsureStandardReportsAsync()
    {
        if (await SeedStateService.IsAppliedAsync(_context, SeedStateService.StandardReportsKey))
        {
            SystemLogService.Info("Стандартные отчёты уже посажены — пропуск.", "Seed");
            return;
        }

        await new PrintFormService(_context).EnsureSchemaAsync();
        await new ModuleMetadataService(_context).EnsureDefaultModulesAsync();
        await new ReportDataSetService(_context).EnsureStandardDataSetsAsync();
        _context.ChangeTracker.Clear();
        await EnsureCashOrderTurnoverReportSourceAsync();
        await DeleteDeprecatedObjectTreeReportsAsync();
        var deletedReportCodes = await new StandardReportDeletionService(_context).GetDeletedCodesAsync();

        // Отчёты и источники читаем один раз: раньше на каждое определение уходило
        // до трёх запросов (источник, отчёт по коду, отчёт по имени), а определений десятки.
        // На удалённой базе это главный источник задержки этапа.
        var existingReports = await _context.Reports.ToListAsync();
        var reportSources = await _context.MetadataObjects
            .AsNoTracking()
            .Include(metadata => metadata.Fields)
            .ToListAsync();

        foreach (var definition in BuildStandardReportDefinitions()
                     .Where(definition => !IsDeprecatedObjectTreeReportCode(definition.Code))
                     .Where(definition => !deletedReportCodes.Contains(definition.Code)))
            await EnsureStandardReportAsync(definition, existingReports, reportSources);
        await _context.SaveChangesAsync();

        await SeedStateService.MarkAsync(_context, SeedStateService.StandardReportsKey, "Стандартные отчёты");
    }

    /// <summary>Досадка нового отчёта в существующие базы, независимо от версии общего сида.</summary>
    public async Task EnsureAccountPairTurnoverReportAsync()
    {
        var deleted = await new StandardReportDeletionService(_context).GetDeletedCodesAsync();
        if (deleted.Contains(AccountPairTurnoverBuilder.ReportCode)) return;
        var reports = await _context.Reports.ToListAsync();
        var sources = await _context.MetadataObjects.AsNoTracking().Include(item => item.Fields).ToListAsync();
        var definition = BuildStandardReportDefinitions().Single(item => item.Code == AccountPairTurnoverBuilder.ReportCode);
        await EnsureStandardReportAsync(definition, reports, sources);
        await _context.SaveChangesAsync();
    }

    private async Task EnsureCashOrderTurnoverReportSourceAsync()
    {
        var source = await _context.MetadataObjects
            .Include(item => item.Fields)
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "ReportSource" &&
                item.Name == CashOrderTurnoverReportSourceName);

        if (source == null)
        {
            var configId = await _context.MetadataConfigurations
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync();

            source = new MetadataObject
            {
                Id = Guid.NewGuid(),
                Name = CashOrderTurnoverReportSourceName,
                TableName = CashOrderTurnoverReportSourceTableName,
                ObjectType = "ReportSource",
                Description = "Виртуальный источник кассовых отчетов: РКО/ПКО сгруппированы по корреспондентскому счету с суммами прихода, расхода и остатками.",
                Icon = "📒",
                Order = 95,
                IsSystem = true,
                MetadataConfigId = configId,
                Fields = new List<MetadataField>()
            };
            await _context.MetadataObjects.AddAsync(source);
        }

        foreach (var desired in BuildCashOrderTurnoverReportSourceFields(source.Id))
        {
            var existing = source.Fields.FirstOrDefault(field =>
                field.DbColumnName.Equals(desired.DbColumnName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                source.Fields.Add(desired);
                await _context.MetadataFields.AddAsync(desired);
                continue;
            }

            ApplyCashOrderTurnoverFieldTemplate(existing, desired);
        }

        await _context.SaveChangesAsync();
    }

    private static List<MetadataField> BuildCashOrderTurnoverReportSourceFields(Guid metadataObjectId) => new()
    {
        CashOrderTurnoverField(metadataObjectId, "Дата", "report_date", "DateTime", 1),
        CashOrderTurnoverField(metadataObjectId, "Документ", "doc_number", "String", 3),
        CashOrderTurnoverField(metadataObjectId, "Номер учета", "nuch", "String", 5),
        CashOrderTurnoverField(metadataObjectId, "Тип КО", "order_kind", "String", 7),
        CashOrderTurnoverField(metadataObjectId, "Касса", "cash_desk", "String", 8),
        CashOrderTurnoverField(metadataObjectId, "Счет кассы", "cash_account", "String", 9),
        CashOrderTurnoverField(metadataObjectId, "Корр. счет", "correspondent_account", "String", 10),
        CashOrderTurnoverField(metadataObjectId, "Остаток на начало", "opening_balance", "Decimal", 11),
        CashOrderTurnoverField(metadataObjectId, "Сумма Приход", "sum_debet", "Decimal", 12),
        CashOrderTurnoverField(metadataObjectId, "Сумма Расход", "sum_credit", "Decimal", 13),
        CashOrderTurnoverField(metadataObjectId, "Остаток на конец", "closing_balance", "Decimal", 14),
        CashOrderTurnoverField(metadataObjectId, "Сумма", "sum", "Decimal", 17),
        CashOrderTurnoverField(metadataObjectId, "Содержание", "tex", "String", 18),
        CashOrderTurnoverField(metadataObjectId, "Наименование", "name_kod", "String", 19),
        CashOrderTurnoverField(metadataObjectId, "Основание", "basis", "String", 20),
        CashOrderTurnoverField(metadataObjectId, "Примечание", "description", "String", 21),
        CashOrderTurnoverField(metadataObjectId, "Количество документов", "document_count", "Int", 22),
        CashOrderTurnoverField(metadataObjectId, "Модуль", "module", "String", 23)
    };

    private static MetadataField CashOrderTurnoverField(
        Guid metadataObjectId,
        string name,
        string dbColumnName,
        string fieldType,
        int order) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DbColumnName = dbColumnName,
        FieldType = fieldType,
        Length = fieldType == "String" ? 500 : 0,
        Precision = 18,
        Scale = 2,
        IsRequired = false,
        Order = order,
        MetadataObjectId = metadataObjectId
    };

    private static void ApplyCashOrderTurnoverFieldTemplate(MetadataField existing, MetadataField desired)
    {
        existing.Name = desired.Name;
        existing.FieldType = desired.FieldType;
        existing.Length = desired.Length;
        existing.Precision = desired.Precision;
        existing.Scale = desired.Scale;
        existing.Order = desired.Order;
        existing.IsRequired = false;
        existing.IsUnique = false;
        existing.ReferenceCatalog = desired.ReferenceCatalog;
        existing.DisplayPattern = desired.DisplayPattern;
        existing.DisplayFields = desired.DisplayFields;
    }

    private static readonly string[] DeprecatedObjectTreeReportCodes =
    {
        "standard.finance.trial-balance",
        "standard.finance.general-ledger"
    };

    private static readonly string[] DeprecatedObjectTreeReportNames =
    {
        "Оборотно-сальдовая ведомость",
        "Главная книга"
    };

    private static bool IsDeprecatedObjectTreeReportCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        DeprecatedObjectTreeReportCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    private async Task DeleteDeprecatedObjectTreeReportsAsync()
    {
        var reportIds = await _context.Reports
            .Where(report =>
                DeprecatedObjectTreeReportCodes.Contains(report.Code) ||
                (DeprecatedObjectTreeReportNames.Contains(report.Name) &&
                 ((report.Code == null || report.Code == string.Empty) ||
                  report.Code.StartsWith("standard.finance."))))
            .Select(report => report.Id)
            .ToListAsync();

        if (reportIds.Count == 0)
            return;

        await _context.MetadataModuleItems
            .Where(item => item.ObjectType == "Report" && reportIds.Contains(item.ObjectId))
            .ExecuteDeleteAsync();

        foreach (var reportId in reportIds)
            await DeleteStandardReportDetailsAsync(reportId);

        await _context.Reports
            .Where(report => reportIds.Contains(report.Id))
            .ExecuteDeleteAsync();

        await _context.SaveChangesAsync();
    }

    private async Task EnsureStandardReportAsync(
        StandardReportDefinition definition,
        List<Report> existingReports,
        List<MetadataObject> reportSources)
    {
        var source = reportSources.FirstOrDefault(metadata =>
            metadata.Name == definition.SourceName &&
            metadata.ObjectType == definition.SourceObjectType);

        var report = FindExistingStandardReport(definition, existingReports);
        var isNewReport = report == null;

        if (report == null)
        {
            report = new Report
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            };
            await _context.Reports.AddAsync(report);
            existingReports.Add(report);
        }

        report.Code = definition.Code;
        report.IsSystem = true;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Name))
            report.Name = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.Description))
            report.Description = definition.Description;
        report.DataSourceType = definition.SourceObjectType;
        report.DataSourceId = source?.Id;
        report.ReportType = definition.ReportType;
        if (isNewReport)
        {
            report.Template = string.Empty;
            report.Settings = "{}";
        }
        else if (string.IsNullOrWhiteSpace(report.Settings))
        {
            report.Settings = "{}";
        }
        if (isNewReport || string.IsNullOrWhiteSpace(report.Icon))
            report.Icon = definition.Icon;
        if (isNewReport)
        {
            // Обычные native-отчёты скрыты, пока не сделан точный вывод.
            // Отчёт с посеянным ClosedXML-макетом доступен сразу: макет хранится в базе.
            // Незаконченные образцы (СТ-1) скрыты до включения администратором.
            report.IsActive = definition.SeedClosedXmlLayout && definition.IsActiveByDefault;
            report.IsPrintForm = false;
            report.IsDefault = false;

            // Отметка для окна «Бухгалтерская отчётность» ставится один раз:
            // дальше администратор управляет ею в конструкторе.
            report.ShowInAccountingReports = definition.ShowInAccountingReports;
        }

        report.SourceFormat = definition.SourceFormat;
        if (definition.SeedClosedXmlLayout && string.IsNullOrWhiteSpace(report.ClosedXmlLayout))
        {
            // Макет записывается в Reports."ClosedXmlLayout" один раз и дальше живёт в базе:
            // правки, сделанные в генераторе отчётов, код не перетирает.
            // Вид разметки зависит от типа отчёта: у акта сверки, оборотной
            // ведомости и журнала счет-фактур свои наборы колонок.
            var seedLayout = definition.ReportType switch
            {
                "AccountPairTurnover" => AccountPairTurnoverLayout.Create(definition.Name),
                "TrialBalance" => ClosedXmlLayoutTemplates.CreateTrialBalance(definition.Name),
                "TurnoverByAccount" => ClosedXmlLayoutTemplates.CreateTurnoverByAccount(definition.Name),
                "InvoiceJournal" => ClosedXmlLayoutTemplates.CreateInvoiceJournal(definition.Name),
                "VatInvoiceSt1" => ClosedXmlLayoutTemplates.CreateVatInvoiceSt1(definition.Name),
                "VatInvoiceVatKg" => ClosedXmlVatInvoiceNdSTemplate.Create(definition.Name),
                _ => ClosedXmlLayoutTemplates.CreateReconciliationAct(definition.Name)
            };

            report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(seedLayout);
        }

        report.TemplateVersion = isNewReport ? 1 : Math.Max(report.TemplateVersion, 1);
        if (isNewReport)
            report.Order = definition.Order;
        report.UpdatedAt = DateTime.UtcNow;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageTitle))
            report.PageTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.PageOrientation))
            report.PageOrientation = definition.PageOrientation;
        if (isNewReport || report.PageWidth <= 0)
            report.PageWidth = report.PageOrientation == "Landscape" ? 297 : 210;
        if (isNewReport || report.PageHeight <= 0)
            report.PageHeight = report.PageOrientation == "Landscape" ? 210 : 297;
        if (isNewReport || report.LeftMargin <= 0)
            report.LeftMargin = 10;
        if (isNewReport || report.RightMargin <= 0)
            report.RightMargin = 10;
        if (isNewReport || report.TopMargin <= 0)
            report.TopMargin = 12;
        if (isNewReport || report.BottomMargin <= 0)
            report.BottomMargin = 12;
        if (isNewReport || string.IsNullOrWhiteSpace(report.FontName))
            report.FontName = "Segoe UI";
        if (isNewReport || report.FontSize <= 0)
            report.FontSize = 9;
        if (isNewReport)
        {
            report.ShowHeader = true;
            report.ShowFooter = true;
            report.ShowPageNumbers = true;
            report.ShowGridLines = true;
        }
        if (isNewReport || string.IsNullOrWhiteSpace(report.TitleText))
            report.TitleText = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.SubtitleText))
            report.SubtitleText = definition.Description;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderTitle))
            report.HeaderTitle = definition.Name;
        if (isNewReport || string.IsNullOrWhiteSpace(report.HeaderSubtitle))
            report.HeaderSubtitle = definition.Description;

        var hasFields = await _context.ReportFields.AnyAsync(item => item.ReportId == report.Id);
        if (source != null && !hasFields)
            await AddStandardReportFieldsAsync(report.Id, source, definition.Fields);
        await _context.SaveChangesAsync();
        await AssignStandardReportToModuleAsync(report.Id, definition.ModuleCode, definition.Order);
    }

    private static Report? FindExistingStandardReport(StandardReportDefinition definition, List<Report> existingReports)
    {
        var report = existingReports.FirstOrDefault(item => item.Code == definition.Code);
        if (report != null)
            return report;

        return existingReports.FirstOrDefault(item =>
            item.Name == definition.Name &&
            (string.IsNullOrWhiteSpace(item.Code) ||
             item.Code.StartsWith("assets.") ||
             item.Code.StartsWith("finance.") ||
             item.Code.StartsWith("inventory.")));
    }

    private async Task DeleteStandardReportDetailsAsync(Guid reportId)
    {
        await _context.ReportFields
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportFilters
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportGroups
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
        await _context.ReportElementMappings
            .Where(item => item.ReportId == reportId)
            .ExecuteDeleteAsync();
    }

    private async Task AddStandardReportFieldsAsync(
        Guid reportId,
        MetadataObject source,
        IReadOnlyList<string> fieldNames)
    {
        var fields = new List<ReportField>();
        var order = 1;
        foreach (var fieldName in fieldNames)
        {
            var field = source.Fields.FirstOrDefault(metadataField =>
                metadataField.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                metadataField.DbColumnName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null)
                continue;

            fields.Add(new ReportField
            {
                Id = Guid.NewGuid(),
                ReportId = reportId,
                FieldName = field.DbColumnName,
                DisplayName = field.Name,
                AggregateType = field.FieldType == "Decimal" ? "Sum" : string.Empty,
                Order = order++,
                Width = GetStandardReportFieldWidth(field),
                Alignment = field.FieldType == "Decimal" ? "Right" : "Left",
                Format = GetStandardReportFieldFormat(field),
                IsVisible = true
            });
        }

        if (fields.Count > 0)
            await _context.ReportFields.AddRangeAsync(fields);
    }

    private async Task AssignStandardReportToModuleAsync(Guid reportId, string moduleCode, int order)
    {
        var module = await _context.MetadataModules
            .FirstOrDefaultAsync(item => item.Code == moduleCode);
        if (module == null)
            return;

        var existing = await _context.MetadataModuleItems
            .FirstOrDefaultAsync(item => item.ObjectType == "Report" && item.ObjectId == reportId);

        // Уже привязан — не перетаскиваем: состав модуля мог быть изменён вручную.
        if (existing != null)
            return;

        await _context.MetadataModuleItems.AddAsync(new MetadataModuleItem
        {
            Id = Guid.NewGuid(),
            ModuleId = module.Id,
            ObjectId = reportId,
            ObjectType = "Report",
            Order = order
        });

        await _context.SaveChangesAsync();
    }

    private static int GetStandardReportFieldWidth(MetadataField field) => field.FieldType switch
    {
        "Decimal" => 120,
        "DateTime" => 105,
        "Bool" => 80,
        "Reference" => 170,
        _ => field.Length > 250 ? 220 : 150
    };

    private static string GetStandardReportFieldFormat(MetadataField field) => field.FieldType switch
    {
        "Decimal" => "N2",
        "DateTime" => "dd.MM.yyyy",
        _ => string.Empty
    };

    private static IReadOnlyList<StandardReportDefinition> BuildStandardReportDefinitions() => new[]
    {
        new StandardReportDefinition(
            Code: "standard.finance.postings-journal",
            Name: "Журнал проводок",
            Description: "Единый журнал бухгалтерских проводок финансового модуля.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "Table",
            Icon: "📒",
            Order: 100,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "module_code", "debit_account",
                "credit_account", "amount_kgs", "amount_currency", "currency_id",
                "organization_id", "employee_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.trial-balance",
            Name: "Оборотно-сальдовая ведомость",
            Description: "Финансовая ведомость по оборотам и остаткам на основании общего журнала проводок.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "AccountingTrialBalance",
            Icon: "📊",
            Order: 110,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "debit_account", "credit_account", "amount_kgs",
                "amount_currency", "currency_id", "organization_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.general-ledger",
            Name: "Главная книга",
            Description: "Главная книга по счетам на основании проводок и закрытых остатков.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "GeneralLedger",
            Icon: "📘",
            Order: 120,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "debit_account",
                "credit_account", "amount_kgs", "organization_id", "description"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.bank-statement",
            Name: "Выписка банка",
            Description: "Банковская выписка по платежным поручениям и связанным проводкам.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Платежное поручение",
            SourceObjectType: "Document",
            ReportType: "BankStatement",
            Icon: "🏦",
            Order: 130,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "order_type", "organization_id", "amount",
                "amount_currency", "currency_id", "our_account_id", "correspondent_account",
                "payment_classification_id", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.payment-order-registry",
            Name: "Реестр платежных поручений",
            Description: "Реестр платежных поручений за период.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Платежное поручение",
            SourceObjectType: "Document",
            ReportType: "PaymentOrderRegistry",
            Icon: "📋",
            Order: 140,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "order_type", "organization_id", "amount",
                "currency_id", "our_account_id", "correspondent_account", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.reconciliation-act",
            Name: "Акт сверки",
            Description: "Акт сверки расчетов по организации или подотчетному лицу.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "ReconciliationAct",
            Icon: "🤝",
            Order: 150,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "debit_account",
                "credit_account", "amount_kgs", "organization_id", "employee_id", "description"
            }),
        new StandardReportDefinition(
            Code: AccountPairTurnoverBuilder.ReportCode,
            Name: AccountPairTurnoverBuilder.ReportName,
            Description: "Движения каждого счёта выбранной пары с остатками и оборотами по организациям.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: AccountPairTurnoverBuilder.ReportType,
            Icon: "📊", Order: 152, PageOrientation: "Portrait",
            Fields: new[] { "posting_date", "doc_number", "debit_account", "credit_account", "amount_kgs", "organization_id", "description", "module_code" },
            SourceFormat: "ClosedXml", SeedClosedXmlLayout: true, ShowInAccountingReports: true),
        new StandardReportDefinition(
            Code: "standard.finance.reconciliation-act-closedxml",
            Name: "Акт сверки (ClosedXML)",
            Description: "Акт сверки расчётов, собранный визуальным макетом ClosedXML. Макет хранится в самом отчёте и правится в генераторе отчётов.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Проводки",
            SourceObjectType: "Document",
            ReportType: "ReconciliationAct",
            Icon: "📊",
            Order: 151,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "document_type", "debit_account",
                "credit_account", "amount_kgs", "organization_id", "employee_id", "description"
            },
            SourceFormat: "ClosedXml",
            SeedClosedXmlLayout: true, ShowInAccountingReports: true),
        new StandardReportDefinition(
            Code: "standard.finance.invoice-journal",
            Name: "Журнал счет-фактур",
            Description: "Журнал выданных и полученных счет-фактур за период: документ, организация, суммы без налога, НДС, налог с продаж. Данные берутся из SQL-набора — образец для сборки отчётов администратором.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Счет-фактуры выданные и полученные",
            // Источник создаёт набор данных как объект ReportSource — тип должен совпадать.
            SourceObjectType: "ReportSource",
            ReportType: "InvoiceJournal",
            Icon: "🧾",
            Order: 153,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "org_name", "esf_number", "document_kind",
                "amount_without_tax", "vat_total", "sales_tax_total", "total_amount",
                "posted_state", "supply_kind", "report_title", "period_start", "period_end"
            },
            SourceFormat: "ClosedXml",
            SeedClosedXmlLayout: true, ShowInAccountingReports: false, IsActiveByDefault: false),
        new StandardReportDefinition(
            Code: "standard.finance.vat-invoice-st1",
            Name: "Счёт-фактура по форме СТ-1",
            Description: "Бланк счёта-фактуры по форме СТ-1: реквизиты сторон, многоуровневая шапка из 13 графов, вертикальные подписи. Образец форматного макета.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Счет-фактура СТ-1: строки и реквизиты",
            SourceObjectType: "ReportSource",
            ReportType: "VatInvoiceSt1",
            Icon: "🧾",
            Order: 154,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "line_number", "item_name", "unit_name", "quantity", "price",
                "amount_without_tax", "vat_rate", "vat_amount", "sales_tax_rate",
                "sales_tax_amount", "line_total", "tax_code", "vat_tax_code",
                "seller_name", "buyer_name", "seller_inn_text", "buyer_inn_text",
                "seller_address_text", "buyer_address_text", "seller_reg_number",
                "seller_bank_text", "report_title", "period_start", "period_end", "esf_number"
            },
            SourceFormat: "ClosedXml",
            SeedClosedXmlLayout: true, ShowInAccountingReports: false, IsActiveByDefault: false),
        new StandardReportDefinition(
            Code: "standard.finance.vat-invoice-vat-nds",
            Name: "Счет-фактура НДС (ClosedXML)",
            Description: "Бланк «СЧЕТ-ФАКТУРА НДС»: шапка по пунктам — 102/103, пары 201/301 … 206/306 и 401–405, — таблица позиций с двухуровневой шапкой НДС, строки ИТОГО и ВСЕГО, подписи. Образец бланка на одной странице.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Счет-фактура НДС: позиции и реквизиты",
            SourceObjectType: "ReportSource",
            ReportType: "VatInvoiceVatKg",
            Icon: "🧾",
            Order: 155,
            PageOrientation: "Portrait",
            Fields: new[]
            {
                "line_number", "group_code", "item_name", "unit_name", "quantity", "price",
                "amount_without_tax", "vat_rate", "vat_amount", "line_total",
                "seller_name", "seller_inn", "seller_address", "seller_bank_text",
                "payment_kind_text", "supply_kind_text", "sales_tax_label",
                "total_without_tax", "total_vat", "total_sales_tax", "total_amount",
                "report_title", "period_start", "period_end", "esf_number"
            },
            SourceFormat: "ClosedXml",
            SeedClosedXmlLayout: true, ShowInAccountingReports: false, IsActiveByDefault: false),
        new StandardReportDefinition(
            Code: "standard.finance.trial-balance-closedxml",
            Name: "Оборотная ведомость (ClosedXML)",
            Description: "Оборотная ведомость синтетического и аналитического учёта по счёту: проводки, сгруппированные по организации, с итогом по каждой организации и общим итогом. Источник — системный расчёт «Движения по проводкам». Макет хранится в отчёте и правится в конструкторе.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Движения по проводкам",
            SourceObjectType: "ReportSource",
            ReportType: "TurnoverByAccount",
            Icon: "📊",
            Order: 152,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "posting_date", "doc_number", "counter_account_code",
                "debit_amount", "credit_amount", "description",
                "currency_code", "amount_currency", "exchange_rate",
                "module_code", "organization_code", "organization_name"
            },
            SourceFormat: "ClosedXml",
            SeedClosedXmlLayout: true, ShowInAccountingReports: true),
        new StandardReportDefinition(
            Code: "standard.finance.sales-invoice-journal",
            Name: "Журнал продаж",
            Description: "Журнал выписанных счет-фактур и налоговых сумм.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: InvoiceDocumentTypes.SalesIssue,
            SourceObjectType: "Document",
            ReportType: "SalesInvoiceJournal",
            Icon: "🧾",
            Order: 160,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "esf_number", "organization_id",
                "payment_kind", "delivery_kind", "supply_kind", "amount_without_tax",
                "vat_total", "sales_tax_total", "amount", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.purchase-invoice-journal",
            Name: "Журнал закупок",
            Description: "Журнал зарегистрированных полученных счет-фактур и налоговых сумм.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: InvoiceDocumentTypes.PurchaseRegistration,
            SourceObjectType: "Document",
            ReportType: "PurchaseInvoiceJournal",
            Icon: "📥",
            Order: 170,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "esf_number", "organization_id",
                "payment_kind", "delivery_kind", "supply_kind", "amount_without_tax",
                "vat_total", "sales_tax_total", "amount", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.cash-receipts",
            Name: "Журнал приходных кассовых ордеров",
            Description: "Реестр приходных кассовых документов.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расходный/Приходный КО",
            SourceObjectType: "Document",
            ReportType: "CashReceiptRegistry",
            Icon: "💵",
            Order: 180,
            PageOrientation: "Landscape",
            Fields: new[] { "doc_date", "doc_number", "organization_id", "cash_desk_id", "amount", "basis", "is_posted" }),
        new StandardReportDefinition(
            Code: "standard.finance.cash-payments",
            Name: "Журнал расходных кассовых ордеров",
            Description: "Реестр расходных кассовых документов.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расходный/Приходный КО",
            SourceObjectType: "Document",
            ReportType: "CashPaymentRegistry",
            Icon: "💸",
            Order: 190,
            PageOrientation: "Landscape",
            Fields: new[] { "doc_date", "doc_number", "organization_id", "cash_desk_id", "amount", "basis", "is_posted" }),
        new StandardReportDefinition(
            Code: "standard.finance.advance-reports",
            Name: "Журнал авансовых отчетов",
            Description: "Реестр авансовых отчетов с суммами к учету, перерасходом и возвратом.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Авансовые платежи",
            SourceObjectType: "Document",
            ReportType: "AdvanceReportRegistry",
            Icon: "🧳",
            Order: 200,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "employee_id", "advance_payment_id", "amount",
                "accepted_amount", "overrun_amount", "return_amount", "currency_id", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.finance.exchange-rate-differences",
            Name: "Журнал курсовой разницы",
            Description: "Журнал документов расчета курсовой разницы.",
            ModuleCode: ModuleMetadataService.FinanceCode,
            SourceName: "Расчет курсовой разницы",
            SourceObjectType: "Document",
            ReportType: "ExchangeRateDifferenceRegistry",
            Icon: "💱",
            Order: 210,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "calculation_date", "period_start_date", "period_end_date", "currency_id",
                "exchange_rate", "processed_balances", "created_postings", "gain_amount", "loss_amount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.list",
            Name: "Ведомость основных средств",
            Description: "Ведомость основных средств по зафиксированным остаткам периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetStatement",
            Icon: "🏗",
            Order: 300,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "InventoryNumber", "AssetName", "OrganizationName", "ResponsiblePersonName",
                "SiteName", "InitialCost", "SalvageValue", "AccumulatedDepreciation",
                "CarryingAmount", "LifecycleStatus"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.balances",
            Name: "Оборотная ведомость по ОС",
            Description: "Оборотная ведомость основных средств по снимкам закрытого периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetTurnover",
            Icon: "📊",
            Order: 310,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName",
                "OpeningCost", "OpeningDepreciation", "OpeningCarryingAmount",
                "AcquisitionCost", "DisposalCost", "TransferInCost", "TransferOutCost",
                "RevaluationCost", "AutomaticDepreciation", "ManualDepreciation",
                "DepreciationWriteOff", "DisposalDepreciation",
                "ClosingCost", "ClosingDepreciation", "ClosingCarryingAmount",
                "ResponsiblePersonName", "SiteName"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.by-account",
            Name: "Ведомость ОС по счету",
            Description: "Ведомость основных средств в разрезе счетов учета.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetByAccount",
            Icon: "📒",
            Order: 320,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "AssetAccount", "InventoryNumber", "AssetName",
                "InitialCost", "AccumulatedDepreciation", "CarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.depreciation",
            Name: "Ведомость амортизации",
            Description: "Ведомость начисленной и накопленной амортизации основных средств.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetDepreciation",
            Icon: "📉",
            Order: 330,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "InventoryNumber", "AssetName", "DepreciationAccount",
                "ExpenseAccount", "OpeningDepreciation", "AutomaticDepreciation",
                "ManualDepreciation", "DepreciationAdjustment", "DepreciationWriteOff",
                "ClosingDepreciation", "ClosingCarryingAmount", "MonthlyDepreciation"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.card",
            Name: "Карточка основного средства",
            Description: "Печатная карточка основного средства по текущим реквизитам.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Основные средства",
            SourceObjectType: "Catalog",
            ReportType: "FixedAssetCard",
            Icon: "🪪",
            Order: 340,
            PageOrientation: "Portrait",
            Fields: new[]
            {
                "inventory_number", "name", "asset_group", "asset_account",
                "depreciation_account", "expense_account", "initial_cost",
                "accumulated_depreciation", "carrying_amount", "status", "is_active"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.journal",
            Name: "Журнал движения ОС",
            Description: "Журнал движения основных средств по документам и снимкам периода.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetMovementJournal",
            Icon: "📚",
            Order: 350,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName", "OrganizationName",
                "ResponsiblePersonName", "SiteName", "AssetAccount", "OpeningCost",
                "AcquisitionCost", "DisposalCost", "TransferInCost", "TransferOutCost",
                "ClosingCost", "ClosingCarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.responsible-person",
            Name: "Ведомость ОС по МОЛ",
            Description: "Ведомость основных средств по материально ответственным лицам.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetResponsiblePersonStatement",
            Icon: "👤",
            Order: 360,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodEnd", "ResponsiblePersonName", "InventoryNumber", "AssetName",
                "SiteName", "InitialCost", "AccumulatedDepreciation", "CarryingAmount"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.directory",
            Name: "Справочник ОС",
            Description: "Контрольная печатная форма справочника основных средств.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Основные средства",
            SourceObjectType: "Catalog",
            ReportType: "FixedAssetDirectory",
            Icon: "📇",
            Order: 370,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "code", "inventory_number", "name", "asset_group", "asset_subgroup_id",
                "asset_type_id", "asset_account", "responsible_person_id", "site_id", "status", "is_active"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.inventory-transfer",
            Name: "Инвентарная передача ОС",
            Description: "Печатная ведомость документов передачи основных средств в подотчет.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Передача ОС в подотчет",
            SourceObjectType: "Document",
            ReportType: "FixedAssetInventoryTransfer",
            Icon: "🔁",
            Order: 380,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "doc_date", "doc_number", "asset_id", "organization_id",
                "responsible_person_id", "site_id", "basis", "is_posted"
            }),
        new StandardReportDefinition(
            Code: "standard.fixed-assets.movement",
            Name: "Движение основных средств",
            Description: "Движение основных средств по периоду и местам эксплуатации.",
            ModuleCode: ModuleMetadataService.FixedAssetsCode,
            SourceName: "Снимки остатков ОС",
            SourceObjectType: "ReportSource",
            ReportType: "FixedAssetMovement",
            Icon: "🚚",
            Order: 390,
            PageOrientation: "Landscape",
            Fields: new[]
            {
                "PeriodStart", "PeriodEnd", "InventoryNumber", "AssetName",
                "OrganizationName", "ResponsiblePersonName", "SiteName",
                "AssetAccount", "OpeningCost", "AcquisitionCost", "DisposalCost",
                "TransferInCost", "TransferOutCost", "RevaluationCost",
                "ClosingCost", "ClosingDepreciation", "ClosingCarryingAmount"
            })
    };

    private sealed record StandardReportDefinition(
        string Code,
        string Name,
        string Description,
        string ModuleCode,
        string SourceName,
        string SourceObjectType,
        string ReportType,
        string Icon,
        int Order,
        string PageOrientation,
        IReadOnlyList<string> Fields,
        string SourceFormat = "Native",
        bool SeedClosedXmlLayout = false,

        /// <summary>
        /// Отметить отчёт для списка окна «Бухгалтерская отчётность».
        /// Администратор может снять или поставить отметку в конструкторе.
        /// </summary>
        bool ShowInAccountingReports = false,

        /// <summary>
        /// Показывать отчёт в списках сразу после посева. Для незаконченных
        /// образцов (например, СТ-1) ставится false — админ включает вручную.
        /// </summary>
        bool IsActiveByDefault = true);
}
```

### Services/SchemaMigrationService.cs

SHA-256: `1186bf9acf19633810eb810fb22c40a44c69a0ced2de64282e0e6c11cdeafda1`

```csharp
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Единый механизм миграций схемы при открытии информационной базы.
    ///
    /// Заменяет разрозненные «починим при старте»: все шаги собраны здесь,
    /// пронумерованы и записываются в журнал schema_migrations. Повторяемые шаги
    /// (достройка колонок) выполняются при каждом открытии базы — это самовосстановление;
    /// разовые (чистки, разметка) — только пока не отмечены в журнале.
    ///
    /// Шаги аддитивные и идемпотентные: существующие данные и колонки не меняются.
    /// Ошибка одного шага не блокирует открытие базы — она логируется и идёт дальше.
    /// </summary>
    public sealed class SchemaMigrationService
    {
        private sealed record Step(
            string Version,
            string Description,
            bool Repeatable,
            Func<AppDbContext, Task> Apply);

        private readonly AppDbContext _context;
        private readonly List<Step> _steps;

        public SchemaMigrationService(AppDbContext context)
        {
            _context = context;

            _steps = new List<Step>
            {
                new("2026.10.19.01",
                    "Колонки EF-модели в существующих таблицах (Reports.ClosedXmlLayout и подобные)",
                    true,
                    async ctx => { await new SchemaRepairService(ctx).RepairAsync(); }),

                new("2026.10.19.02",
                    "Колонки справочников и документов по метаданным (bank_name и подобные)",
                    true,
                    async ctx =>
                    {
                        var (tables, columns) = await new MetadataSchemaRepairService(ctx).RepairAsync();
                        if (columns > 0)
                        {
                            SystemLogService.Warning(
                                $"Схема: достроены колонки справочников/документов — {columns} (проверено таблиц: {tables}).",
                                "Schema");
                        }
                    }),

                new("2026.10.19.03",
                    "Схема кассовых дней и закрытия периода",
                    true,
                    ctx => new CashDayClosureService(ctx).EnsureSchemaAsync()),

                new("2026.10.19.04",
                    "Схема наборов данных отчётов",
                    true,
                    ctx => new ReportDataSetService(ctx).EnsureSchemaAsync()),

                new("2026.10.19.05",
                    "Разметка отчётов для окна «Бухгалтерская отчётность»",
                    false,
                    MarkAccountingReportsAsync),

                new("2026.10.19.06",
                    "Разовая чистка FRX-отчётов (этап 2)",
                    false,
                    ctx => new FrxReportsCleanupService(ctx).RunOnceAsync()),

                new("2026.10.19.07",
                    "Схема периодов, остатков и финансовых отчётов (перенесено из AccountingPeriodService)",
                    true,
                    EnsureAccountingPeriodSchemaAsync),

                new("2026.10.20.01",
                    "Канонические имена таблиц документов ОС/ТМЦ (doc_asset_*, doc_inventory_*)",
                    false,
                    NormalizeDocumentTableNamesAsync),

                new("2026.10.21.01",
                    "Модель документов ТМЗ: 3 документа + строки + справочник видов операций",
                    false,
                    EnsureInventoryDocumentModelAsync),

                new("2026.10.22.01",
                    "Справочник видов операций ТМЗ: колонка operation_kind",
                    false,
                    EnsureInventoryEntryOperationKindsAsync),

                new("2026.10.22.02",
                    "doc_postings: colonnes document_table/document_id (восстановление схемы при отсутствии)",
                    true,
                    EnsurePostingDocumentTableColumnsAsync),

                new("2026.10.26.01",
                    "Пары счетов: досид предустановленных записей (счёта — по коду из плана счетов)",
                    true,
                    EnsureAccountPairsSeedAsync),

                new("2026.10.27.01",
                    "Справочник «Ввод нового документа ТМЗ» → «Вид операции ТМЗ»",
                    false,
                    RenameInventoryEntryCatalogAsync),

                new("2026.10.29.01",
                    "Ссылочные колонки пар счетов: text → uuid (код счёта переносится в Id)",
                    true,
                    EnsureAccountPairsReferenceTypesAsync),

                new("2026.10.30.01",
                    "Акт сверки (ClosedXML): обновление сохранённого макета (порядок колонок)",
                    false,
                    EnsureReconciliationActColumnOrderAsync),

                new("2026.10.30.02",
                    "Акт сверки (ClosedXML): операция, дата и номер документа первыми",
                    false,
                    EnsureReconciliationActColumnOrderAsync),

                new("2026.10.30.03",
                    "Возврат названия документа КО: «Банковские операции» → «Расходный/Приходный КО»",
                    false,
                    RestoreCashOrderDocumentNameAsync),

                new("2026.10.30.04",
                    "Счёт-фактура по форме СТ-1: скрыть по умолчанию",
                    false,
                    HideVatInvoiceSt1Async),

                new("2026.10.30.05",
                    "Журнал счет-фактур и Счет-фактура НДС: скрыть по умолчанию",
                    false,
                    HideInvoiceReportsAsync),

                new("2026.10.30.06",
                    "Ведомость по выбранной паре счетов ClosedXML и подпись Модуль в актах",
                    false,
                    EnsureAccountPairTurnoverReportAsync),

                new("2026.10.30.07",
                    "Акт ClosedXML: Сумма Дт, Сумма Кт и одна Сумма вал.",
                    false,
                    EnsureReconciliationCurrencyColumnAsync)
            };
        }

        private static async Task EnsureReconciliationCurrencyColumnAsync(AppDbContext ctx)
        {
            var reports = await ctx.Reports.Where(report => report.ReportType.StartsWith("ReconciliationAct")).ToListAsync();
            foreach (var report in reports)
            {
                if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout)) continue;
                var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out _);
                if (layout == null) continue;
                var changed = false;
                foreach (var block in layout.Sheets.SelectMany(sheet => sheet.Blocks).Where(block => block.Table != null))
                {
                    var columns = block.Table!.Columns;
                    if (columns.Any(column => column.Field == "Сумма вал.")) continue;
                    var creditIndex = columns.FindIndex(column => column.Field == "Сумма Кт");
                    if (creditIndex < 0) continue;
                    columns.Insert(creditIndex + 1, new BIS.ERP.Models.ClosedXmlColumn
                    {
                        Field = "Сумма вал.", Header = "Сумма вал.", Width = 12,
                        NumberFormat = "#,##0.00", SuppressZero = true, Align = "Right"
                    });
                    // Восьмиколоночные объединения шапки стандартного акта расширяем на новую графу.
                    foreach (var cell in layout.Sheets.SelectMany(sheet => sheet.Blocks).SelectMany(item => item.Rows).SelectMany(row => row.Cells))
                        if (cell.MergeToColumn == 8) cell.MergeToColumn = 9;
                    changed = true;
                }
                if (!changed) continue;
                report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(layout);
                report.UpdatedAt = DateTime.UtcNow;
            }
            await ctx.SaveChangesAsync();
        }

        private static async Task EnsureAccountPairTurnoverReportAsync(AppDbContext ctx)
        {
            await new MetadataService(ctx).EnsureAccountPairTurnoverReportAsync();
            var reports = await ctx.Reports.Where(report => report.ReportType.StartsWith("ReconciliationAct")).ToListAsync();
            foreach (var report in reports)
            {
                if (string.IsNullOrWhiteSpace(report.ClosedXmlLayout)) continue;
                var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out _);
                if (layout == null) continue;
                var changed = false;
                bool Legacy(string text) => text.Replace(" ", "").Equals("КодАРМ", StringComparison.OrdinalIgnoreCase);
                foreach (var block in layout.Sheets.SelectMany(sheet => sheet.Blocks))
                {
                    if (block.Table == null) continue;
                    foreach (var column in block.Table.Columns.Where(column => Legacy(column.Header)))
                    { column.Header = "Модуль"; changed = true; }
                    foreach (var cell in block.Table.HeaderLevels.SelectMany(level => level.Cells).Where(cell => Legacy(cell.Text)))
                    { cell.Text = "Модуль"; changed = true; }
                }
                if (!changed) continue;
                report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(layout);
                report.UpdatedAt = DateTime.UtcNow;
            }
            await ctx.SaveChangesAsync();
        }

        /// <summary>
        /// Шаг 3.3 для «Пар счетов»: в базах, созданных до перехода Reference → uuid,
        /// debit_account/credit_account/currency_id остались text, а приложение
        /// (и новые базы) работают с uuid. Значения-коды («14160000») переводятся
        /// в Id по плану счетов; если хоть одно значение не разобралось, колонка
        /// не меняется — данные важнее схемы.
        ///
        /// Почему не «все ссылочные колонки сразу»: массовый перевод ломает сырой
        /// SQL, который передаёт в такие колонки строковые параметры
        /// (doc_postings.organization_id и подобные). Список проверенных колонок
        /// живёт в ReferenceColumnMigrationService и пополняется по мере ревизии.
        /// </summary>
        private static async Task EnsureAccountPairsReferenceTypesAsync(AppDbContext ctx)
        {
            var result = await new ReferenceColumnMigrationService(ctx).ConvertAsync();
            foreach (var item in result.Converted)
            {
                SystemLogService.Info($"Пары счетов: {item} → uuid.", "Schema");
            }

            foreach (var problem in result.Problems)
            {
                SystemLogService.Warning($"Пары счетов: {problem}", "Schema");
            }
        }

        /// <summary>
        /// Возвращает документу кассовых ордеров исходное имя. Переименование
        /// раздела в «Банковские операции» было ошибкой: на имя документа
        /// опираются открытие источника проводки, печатные формы и отчёты.
        /// Меняем только имя «Банковские операции»; другое имя не трогаем.
        /// </summary>
        private static async Task RestoreCashOrderDocumentNameAsync(AppDbContext ctx)
        {
            const string cashOrderName = "Расходный/Приходный КО";
            var document = await ctx.MetadataObjects
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Document" && item.TableName == "doc_cash_orders");

            if (document == null ||
                string.Equals(document.Name, cashOrderName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(document.Name, "Банковские операции", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            document.Name = cashOrderName;
            await ctx.SaveChangesAsync();

            SystemLogService.Info(
                "Документ КО: название возвращено — «Расходный/Приходный КО».",
                "Reports");
        }

        /// <summary>
        /// Скрывает образец «Счёт-фактура по форме СТ-1»: отчёт незаконченный,
        /// по умолчанию не должен появляться ни в «Бухгалтерской отчётности»,
        /// ни в общем списке отчётов. Администратор может включить его вручную.
        /// </summary>
        private static async Task HideVatInvoiceSt1Async(AppDbContext ctx)
        {
            var report = await ctx.Reports
                .FirstOrDefaultAsync(item => item.Code == "standard.finance.vat-invoice-st1");
            if (report == null)
                return;

            if (!report.IsActive && !report.ShowInAccountingReports)
                return;

            report.IsActive = false;
            report.ShowInAccountingReports = false;
            await ctx.SaveChangesAsync();

            SystemLogService.Info("Счёт-фактура по форме СТ-1 скрыта по умолчанию.", "Reports");
        }

        /// <summary>
        /// Скрывает «Журнал счет-фактур» и «Счет-фактура НДС» по умолчанию:
        /// это образцы, которые не должны появляться в «Бухгалтерской отчётности»
        /// и общем списке отчётов, пока администратор не включит их вручную.
        /// </summary>
        private static async Task HideInvoiceReportsAsync(AppDbContext ctx)
        {
            var codes = new[]
            {
                "standard.finance.invoice-journal",
                "standard.finance.vat-invoice-vat-nds"
            };

            var reports = await ctx.Reports
                .Where(item => codes.Contains(item.Code))
                .ToListAsync();

            var changed = 0;
            foreach (var report in reports)
            {
                if (!report.IsActive && !report.ShowInAccountingReports)
                    continue;

                report.IsActive = false;
                report.ShowInAccountingReports = false;
                changed++;
            }

            if (changed == 0)
                return;

            await ctx.SaveChangesAsync();
            SystemLogService.Info(
                $"Скрыты отчёты по умолчанию: {changed} (журнал счет-фактур, счет-фактура НДС).",
                "Reports");
        }

        /// <summary>
        /// Порядки колонок стандартного макета «Акт сверки (ClosedXML)», которые
        /// могли остаться в базе: исходный (операция, суммы, номер и дата в конце)
        /// и промежуточный (дата и номер первыми). Текущий порядок — операция,
        /// дата, номер, затем суммы.
        /// </summary>
        private static readonly string[] LegacyReconciliationActColumns =
        {
            ClosedXmlLayoutTemplates.ActLabelField, "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "N докум", "Дата", "Модуль"
        };

        private static readonly string[] DateFirstReconciliationActColumns =
        {
            "Дата", "N докум", ClosedXmlLayoutTemplates.ActLabelField, "Дебет", "Кредит", "Сумма Дт", "Сумма Кт", "Модуль"
        };

        private const string ReconciliationActClosedXmlCode = "standard.finance.reconciliation-act-closedxml";

        /// <summary>
        /// Обновляет сохранённый макет акта сверки: «Наименование материала, вид
        /// операции», затем «Дата», «N докум», затем дебет/кредит, суммы и модуль.
        /// Меняем только макет, который совпадает с одним из старых стандартных
        /// порядков: пользовательскую разметку шаг не трогает.
        /// </summary>
        private static async Task EnsureReconciliationActColumnOrderAsync(AppDbContext ctx)
        {
            var report = await ctx.Reports
                .FirstOrDefaultAsync(item => item.Code == ReconciliationActClosedXmlCode);
            if (report == null || string.IsNullOrWhiteSpace(report.ClosedXmlLayout))
                return;

            var layout = ClosedXmlReportRenderer.ParseLayout(report.ClosedXmlLayout, out var error);
            if (layout == null)
            {
                SystemLogService.Warning($"Акт сверки (ClosedXML): макет не разобран — {error}", "Reports");
                return;
            }

            var section = layout.Sheets
                .SelectMany(sheet => sheet.Blocks)
                .Where(block => string.Equals(block.Type, "TabularSection", StringComparison.OrdinalIgnoreCase))
                .Select(block => block.Table)
                .FirstOrDefault(table => table != null);
            if (section == null)
                return;

            var fields = section.Columns.Select(column => column.Field).ToArray();
            var isLegacy = fields.Length == LegacyReconciliationActColumns.Length &&
                           fields.SequenceEqual(LegacyReconciliationActColumns, StringComparer.OrdinalIgnoreCase);
            var isDateFirst = fields.Length == DateFirstReconciliationActColumns.Length &&
                              fields.SequenceEqual(DateFirstReconciliationActColumns, StringComparer.OrdinalIgnoreCase);
            if (!isLegacy && !isDateFirst)
            {
                // Уже новый порядок или макет изменён в конструкторе — не трогаем.
                return;
            }

            var updated = ClosedXmlLayoutTemplates.CreateReconciliationAct(report.Name);
            report.ClosedXmlLayout = ClosedXmlReportRenderer.SerializeLayout(updated);
            report.TemplateVersion = Math.Max(report.TemplateVersion, 2);
            report.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();

            SystemLogService.Info(
                "Акт сверки (ClosedXML): порядок колонок — операция, дата, номер документа, затем остальные.",
                "Reports");
        }

        /// <summary>
        /// Предустановленные пары счетов («Виды расчета»).
        ///
        /// Шаг повторяемый: досид идёт по коду записи и ничего не перезаписывает,
        /// поэтому он же лечит базы, созданные до исправления сида. Раньше счёт в сиде
        /// задавался кодом («18200000»), а debit_account/credit_account — uuid-ссылки
        /// на план счетов: вставка падала с 22P02, ошибка уходила в Debug, и справочник
        /// оставался пустым.
        /// </summary>
        private static async Task EnsureAccountPairsSeedAsync(AppDbContext ctx)
        {
            var added = await new MetadataService(ctx).EnsureAccountPairsSeedDataAsync();
            if (added > 0)
            {
                SystemLogService.Info(
                    $"Пары счетов: добавлены предустановленные записи — {added}.",
                    "Schema");
            }
        }

        private static async Task EnsurePostingDocumentTableColumnsAsync(AppDbContext ctx)
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF to_regclass('public.doc_postings') IS NOT NULL THEN
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS document_table text;
                        ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS document_id uuid;
                    END IF;
                END $$;");
        }

        public async Task<SchemaMigrationReport> EnsureAsync()
        {
            await EnsureJournalAsync();

            var alreadyApplied = await LoadAppliedAsync();
            var applied = 0;
            var skipped = 0;
            var failures = new List<string>();

            foreach (var step in _steps)
            {
                if (!step.Repeatable && alreadyApplied.Contains(step.Version))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    // Замер шага: на удалённой базе дорог каждый запрос, нужно видеть,
                    // какой шаг съедает время (этап «Схема и миграции» мерится целиком).
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    await step.Apply(_context);
                    await RecordAsync(step);
                    watch.Stop();
                    applied++;

                    if (watch.ElapsedMilliseconds >= 200)
                    {
                        SystemLogService.Warning(
                            $"⏱ Миграция {step.Version} ({step.Description}): {watch.ElapsedMilliseconds} мс",
                            "Migration");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{step.Version} {step.Description}: {ex.Message}");
                    SystemLogService.Warning(
                        $"Миграция {step.Version} ({step.Description}) не выполнена: {ex.Message}", "Migration");
                }
            }

            if (applied > 0 || failures.Count > 0)
            {
                SystemLogService.Info(
                    $"Миграции схемы: выполнено {applied}, пропущено {skipped}, ошибок {failures.Count}.",
                    "Migration");
            }

            return new SchemaMigrationReport(applied, skipped, failures);
        }

        private async Task EnsureJournalAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version     text PRIMARY KEY,
                    description text NOT NULL DEFAULT '',
                    applied_at  timestamp with time zone NOT NULL DEFAULT now(),
                    runs        integer NOT NULL DEFAULT 1
                );
                ALTER TABLE schema_migrations
                    ADD COLUMN IF NOT EXISTS last_run_at timestamp with time zone;
                UPDATE schema_migrations
                   SET last_run_at = applied_at
                 WHERE last_run_at IS NULL;");
        }

        private async Task<HashSet<string>> LoadAppliedAsync()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var connection = _context.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere)
                await connection.OpenAsync();

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT version FROM schema_migrations";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(reader.GetString(0));
            }
            finally
            {
                if (openedHere)
                    await connection.CloseAsync();
            }

            return result;
        }

        private async Task RecordAsync(Step step)
        {
            // applied_at — момент ПЕРВОГО применения шага и больше не меняется.
            // last_run_at — последний успешный прогон (повторяемые шаги идут при каждом открытии).
            // runs — сколько раз шаг фактически выполнялся.
            var sql = step.Repeatable
                ? @"
                INSERT INTO schema_migrations (version, description, applied_at, last_run_at, runs)
                VALUES (@version, @description, now(), clock_timestamp(), 1)
                ON CONFLICT (version) DO UPDATE
                SET last_run_at = clock_timestamp(),
                    runs = schema_migrations.runs + 1;"
                : @"
                INSERT INTO schema_migrations (version, description, applied_at, last_run_at, runs)
                VALUES (@version, @description, now(), clock_timestamp(), 1)
                ON CONFLICT (version) DO UPDATE
                SET applied_at = now(),
                    last_run_at = clock_timestamp(),
                    runs = schema_migrations.runs + 1;";

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("version", step.Version),
                new NpgsqlParameter("description", step.Description));
        }

        /// <summary>
        /// Схема периодов, остатков и финансовых отчётов: CREATE TABLE IF NOT EXISTS,
        /// ALTER ... ADD COLUMN IF NOT EXISTS и индексы. Раньше этот DDL жил внутри
        /// AccountingPeriodService; теперь единственный источник — здесь,
        /// а сервис вызывает этот метод.
        /// </summary>
        /// <summary>
        /// Миграция справочника видов операций ТМЗ: добавляет operation_kind
        /// и заполняет его из code для существующих строк.
        /// </summary>
        private static async Task EnsureInventoryEntryOperationKindsAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            await new InventoryDocumentSchemaService(isolated).EnsureEntryCatalogOperationKindAsync();
        }

        /// <summary>
        /// Справочник видов операций ТМЗ переименован: «Ввод нового документа ТМЗ» → «Вид операции ТМЗ».
        /// Шаг разовый; идемпотентен: на базе с новым именем ничего не делает.
        /// </summary>
        private static async Task RenameInventoryEntryCatalogAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var report = await new InventoryDocumentSchemaService(isolated).EnsureEntryCatalogRenamedAsync();

            foreach (var action in report.Actions)
                SystemLogService.Info($"ТМЗ: {action}", "Migration");

            foreach (var warning in report.Warnings)
                SystemLogService.Warning($"ТМЗ: {warning}", "Migration");
        }

        /// <summary>
        /// Модель документов ТМЗ: Поступление / Выбытие / Внутреннее перемещение,
        /// у каждого — таблица строк и вид операции реквизитом.
        /// Шаг разовый; идемпотентен (см. InventoryDocumentSchemaService).
        /// </summary>
        private static async Task EnsureInventoryDocumentModelAsync(AppDbContext context)
        {
            // Отдельный контекст: если шаг упадёт, «грязные» отслеживаемые записи
            // не должны ронять остальные шаги и загрузку главного окна
            // (инцидент 20.10.2026: DbUpdateConcurrencyException в MetadataFields).
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var report = await new InventoryDocumentSchemaService(isolated).EnsureAsync();

            foreach (var action in report.Actions)
                SystemLogService.Info($"ТМЗ: {action}", "Migration");

            foreach (var warning in report.Warnings)
                SystemLogService.Warning($"ТМЗ: {warning}", "Migration");
        }

        /// <summary>
        /// Приводит имена таблиц документов ОС/ТМЦ к канону <see cref="DocumentTableNames"/>.
        /// Шаг разовый: после успешного выполнения фиксируется в schema_migrations и больше
        /// не выполняется. Если прогон упал — запись не создаётся, и шаг повторится при
        /// следующем открытии базы. Идемпотентен (см. DocumentTableNameNormalizer).
        /// </summary>
        private static async Task NormalizeDocumentTableNamesAsync(AppDbContext context)
        {
            var connectionString = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Не удалось определить строку подключения инфобазы.");

            await using var isolated = new AppDbContext(connectionString);
            var results = await new DocumentTableNameNormalizer(isolated).NormalizeAsync();
            var renamed = results.Where(item => item.Renamed).Count();

            foreach (var result in results.Where(item => !string.IsNullOrWhiteSpace(item.SkippedReason)))
            {
                SystemLogService.Warning(
                    $"doc-names: {result.DocumentName} — {result.SkippedReason} ({result.OldTableName} -> {result.NewTableName})",
                    "Migration");
            }

            if (renamed > 0)
            {
                SystemLogService.Info($"doc-names: переименовано таблиц — {renamed}.", "Migration");
            }
        }

        public static Task EnsureAccountingPeriodSchemaAsync(AppDbContext context)
            => context.Database.ExecuteSqlRawAsync(AccountingPeriodSchemaSql);

        private const string AccountingPeriodSchemaSql = @"
                CREATE TABLE IF NOT EXISTS ""AccountingPeriods"" (
                    ""Id"" uuid PRIMARY KEY, ""StartDate"" timestamp with time zone NOT NULL,
                    ""EndDate"" timestamp with time zone NOT NULL, ""Status"" varchar(20) NOT NULL,
                    ""CollectedAt"" timestamp with time zone NULL, ""ClosedAt"" timestamp with time zone NULL,
                    ""IsLocked"" boolean NOT NULL DEFAULT false,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountingPeriods_StartDate_EndDate: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountingPeriodModuleStates"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""ModuleId"" uuid NOT NULL,
                    ""IsClosed"" boolean NOT NULL DEFAULT false, ""ClosedAt"" timestamp with time zone NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountingPeriodModuleStates_Period_Module: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountOpeningBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""BalanceDate"" timestamp with time zone NOT NULL,
                    ""AccountCode"" varchar(50) NOT NULL, ""Debit"" numeric(18,2) NOT NULL,
                    ""Credit"" numeric(18,2) NOT NULL, ""UpdatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountOpeningBalances_Date_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""AccountTurnoverSnapshots"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL,
                    ""AccountName"" varchar(300) NOT NULL, ""OpeningDebit"" numeric(18,2) NOT NULL,
                    ""OpeningCredit"" numeric(18,2) NOT NULL, ""TurnoverDebit"" numeric(18,2) NOT NULL,
                    ""TurnoverCredit"" numeric(18,2) NOT NULL, ""ClosingDebit"" numeric(18,2) NOT NULL,
                    ""ClosingCredit"" numeric(18,2) NOT NULL, ""CreatedAt"" timestamp with time zone NOT NULL);
                -- IX_AccountTurnoverSnapshots_Period_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FixedAssetPeriodBalances"" (
                    ""Id"" uuid PRIMARY KEY, ""PeriodId"" uuid NOT NULL,
                    ""PeriodStart"" timestamp with time zone NOT NULL, ""PeriodEnd"" timestamp with time zone NOT NULL,
                    ""AssetId"" uuid NOT NULL, ""InventoryNumber"" varchar(50) NOT NULL,
                    ""AssetName"" varchar(300) NOT NULL, ""OrganizationId"" uuid NULL,
                    ""OrganizationName"" varchar(300) NOT NULL DEFAULT '', ""ResponsiblePersonId"" uuid NULL,
                    ""ResponsiblePersonName"" varchar(300) NOT NULL DEFAULT '', ""SiteId"" uuid NULL,
                    ""SiteName"" varchar(300) NOT NULL DEFAULT '', ""AssetAccount"" varchar(100) NOT NULL DEFAULT '',
                    ""DepreciationAccount"" varchar(100) NOT NULL DEFAULT '', ""ExpenseAccount"" varchar(100) NOT NULL DEFAULT '',
                    ""AcquisitionDate"" timestamp with time zone NULL, ""CommissioningDate"" timestamp with time zone NULL,
                    ""DepreciationStartDate"" timestamp with time zone NULL, ""InitialCost"" numeric(18,2) NOT NULL,
                    ""SalvageValue"" numeric(18,2) NOT NULL, ""AccumulatedDepreciation"" numeric(18,2) NOT NULL,
                    ""CarryingAmount"" numeric(18,2) NOT NULL, ""MonthlyDepreciation"" numeric(18,2) NOT NULL,
                    ""IsActive"" boolean NOT NULL, ""LifecycleStatus"" varchar(100) NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp with time zone NOT NULL);
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodStart"" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodEnd"" timestamp with time zone NOT NULL DEFAULT NOW();
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OrganizationName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ResponsiblePersonName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""SiteName"" varchar(300) NOT NULL DEFAULT '';
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""SalvageValue"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""MonthlyDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningCarryingAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""AcquisitionCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DisposalCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferInCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferOutCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""RevaluationCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""AutomaticDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ManualDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DepreciationAdjustment"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DepreciationWriteOff"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""DisposalDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferInDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""TransferOutDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingCost"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingDepreciation"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingCarryingAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""OpeningMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""PeriodMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""FixedAssetPeriodBalances"" ADD COLUMN IF NOT EXISTS ""ClosingMileage"" numeric(18,2) NOT NULL DEFAULT 0;
                -- IX_FixedAssetPeriodBalances_Period_Asset: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FinancialReportLines"" (
                    ""Id"" uuid PRIMARY KEY, ""ReportCode"" varchar(30) NOT NULL, ""LineCode"" varchar(30) NOT NULL,
                    ""SectionCode"" varchar(30) NOT NULL, ""Name"" varchar(300) NOT NULL,
                    ""SortOrder"" integer NOT NULL, ""Sign"" integer NOT NULL, ""IsTotal"" boolean NOT NULL,
                    ""Formula"" varchar(500) NOT NULL DEFAULT '', ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0,
                    ""IsActive"" boolean NOT NULL);
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""Formula"" varchar(500) NOT NULL DEFAULT '';
                ALTER TABLE ""FinancialReportLines"" ADD COLUMN IF NOT EXISTS ""FixedAmount"" numeric(18,2) NOT NULL DEFAULT 0;
                -- IX_FinancialReportLines_Report_Line: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""FinancialReportLineAccounts"" (
                    ""Id"" uuid PRIMARY KEY, ""LineId"" uuid NOT NULL, ""AccountCode"" varchar(50) NOT NULL);
                -- IX_FinancialReportLineAccounts_Line_Account: владелец индекса — EF (AppDbContext.OnModelCreating), здесь не дублируем (план §4.1)
                CREATE TABLE IF NOT EXISTS ""TaxJournalRecords"" (
                    ""Id"" uuid PRIMARY KEY, ""JournalType"" varchar(20) NOT NULL,
                    ""Date"" timestamp with time zone NOT NULL, ""DocumentNumber"" varchar(50) NOT NULL,
                    ""DocumentType"" varchar(100) NOT NULL, ""Organization"" varchar(300) NOT NULL,
                    ""TaxType"" varchar(50) NOT NULL, ""AmountWithoutTax"" numeric(18,2) NOT NULL,
                    ""TaxAmount"" numeric(18,2) NOT NULL, ""TotalAmount"" numeric(18,2) NOT NULL,
                    ""SourceRecordId"" uuid NULL, ""CreatedAt"" timestamp with time zone NOT NULL);";

        private static readonly string[] DefaultAccountingReportCodes =
        {
            "standard.finance.trial-balance-closedxml",
            "standard.finance.reconciliation-act-closedxml"
        };

        /// <summary>
        /// Разово отмечает рабочие отчёты для окна «Бухгалтерская отчётность».
        /// Если администратор уже что-то отметил — выбор не перетирается.
        /// </summary>
        private static async Task MarkAccountingReportsAsync(AppDbContext context)
        {
            var anyMarked = await context.Reports
                .AsNoTracking()
                .AnyAsync(report => report.ShowInAccountingReports);

            if (anyMarked)
                return;

            var codes = DefaultAccountingReportCodes;

            await context.Reports
                .Where(report => codes.Contains(report.Code))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(report => report.ShowInAccountingReports, true)
                    .SetProperty(report => report.IsActive, true));
        }
    }

    public sealed record SchemaMigrationReport(int Applied, int Skipped, IReadOnlyList<string> Failures);
}
```

### Services/ReconciliationActBuilder.cs

SHA-256: `2a9bbdd8343059fe6272b511010684188cc558eb8cd2ea21ecc15305dd69f9fb`

```csharp
using System.Data;
using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Строка движения по проводке для акта сверки.
    /// Вынесена из AccountingReportsView, чтобы построение таблицы можно было
    /// вызывать и из отчёта, и из диагностического инструмента.
    /// </summary>
    public sealed class ReconciliationMovementRow
    {
        public DateTime Date { get; init; }
        public Guid? OrganizationId { get; init; }
        public string OrganizationName { get; init; } = string.Empty;

        /// <summary>Номер организации из справочника: нужен для заголовка группировки.</summary>
        public string OrganizationCode { get; init; } = string.Empty;
        public string DocumentNumber { get; init; } = string.Empty;
        public string DocumentType { get; init; } = string.Empty;
        public string ModuleCode { get; init; } = string.Empty;
        public string DebitAccount { get; init; } = string.Empty;
        public string CreditAccount { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Description { get; init; } = string.Empty;

        /// <summary>Сумма в валюте: 0, если проводка в сомах.</summary>
        public decimal AmountCurrency { get; init; }

        /// <summary>Код валюты: RUB, USD, EUR. Пусто, если проводка в сомах.</summary>
        public string CurrencyCode { get; init; } = string.Empty;
    }

    /// <summary>
    /// Стороны акта сверки: наша организация и контрагент.
    ///
    /// Наша — запись с is_primary = true в справочнике организаций; контрагент — организация,
    /// по которой строится акт. Руководитель и главный бухгалтер берутся из карточек
    /// (поля director и chief_accountant) и печатаются в подписях.
    /// </summary>
    public sealed record ReconciliationActParties(
        string OurName,
        string OurDirector,
        string OurAccountant,
        string CounterpartyName,
        string CounterpartyDirector,
        string CounterpartyAccountant);

    /// <summary>
    /// Построение таблицы акта сверки: строки данных для макета FRX.
    ///
    /// Логика вынесена из AccountingReportsView в отдельный сервис, чтобы
    /// её можно было проверить диагностическим инструментом без запуска окна.
    /// </summary>
    public class ReconciliationActBuilder
    {
        private readonly AppDbContext _context;

        public ReconciliationActBuilder(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Читает стороны акта из справочника организаций: наша — первичная,
        /// контрагент — по идентификатору, иначе по наименованию.
        /// Пустые значения допустимы: карточка может быть не заполнена.
        /// </summary>
        public async Task<ReconciliationActParties> LoadPartiesAsync(Guid? counterpartyId, string? counterpartyName)
        {
            var ours = await LoadOrganizationCardAsync(null, primaryOnly: true);
            var counterparty = await LoadOrganizationCardAsync(
                counterpartyId, primaryOnly: false, organizationName: counterpartyName);

            return new ReconciliationActParties(
                ours.Name,
                ours.Director,
                ours.Accountant,
                string.IsNullOrWhiteSpace(counterparty.Name) ? counterpartyName ?? string.Empty : counterparty.Name,
                counterparty.Director,
                counterparty.Accountant);
        }

        /// <summary>Карточка организации: наименование, руководитель и главный бухгалтер.</summary>
        private async Task<(string Name, string Director, string Accountant)> LoadOrganizationCardAsync(
            Guid? organizationId,
            bool primaryOnly,
            string? organizationName = null)
        {
            var empty = (Name: string.Empty, Director: string.Empty, Accountant: string.Empty);

            try
            {
                const string columns = @"SELECT COALESCE(NULLIF(""full_name"", ''), ""name"", '') AS card_name,
                                                COALESCE(""director"", '')           AS card_director,
                                                COALESCE(""chief_accountant"", '')   AS card_accountant
                                           FROM ""catalog_organizations""";

                string sql;
                object? value = null;
                if (primaryOnly)
                {
                    sql = columns + " WHERE COALESCE(\"is_primary\", false) = true ORDER BY \"CreatedAt\" LIMIT 1";
                }
                else if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                {
                    sql = columns + " WHERE \"Id\" = @value LIMIT 1";
                    value = organizationId.Value;
                }
                else if (!string.IsNullOrWhiteSpace(organizationName))
                {
                    sql = columns + " WHERE COALESCE(NULLIF(\"full_name\", ''), \"name\", '') = @value LIMIT 1";
                    value = organizationName.Trim();
                }
                else
                {
                    return empty;
                }

                var connection = _context.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                if (value != null)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "value";
                    parameter.Value = value;
                    command.Parameters.Add(parameter);
                }

                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return empty;

                return (
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Стороны акта сверки: {ex.Message}");
                return empty;
            }
        }


        /// <summary>
        /// Колонки, которые нужны только механизму подстановки FRX-макета.
        /// В экранной таблице они скрываются при генерации колонок сетки.
        /// </summary>
        public static readonly HashSet<string> ServiceColumnNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "operation_name", "debit_account", "credit_account",
            "debit_amount", "credit_amount", "debit_begin", "credit_begin",
            "document_number", "document_date", "posting_date", "module", "module_code",
            "report_title", "report_summary", "period_start", "period_end",
            // Стороны акта: значения читает блок подписей макета, в сетке не нужны.
            "our_organization", "our_director", "our_accountant",
            "counterparty", "counterparty_director", "counterparty_accountant"
        };

        /// <summary>Создаёт таблицу акта сверки с колонками отчёта и служебными колонками макета.</summary>
        public static DataTable CreateTable()
        {
            var table = new DataTable("Акт сверки");
            // Первой идёт операция, затем дата и номер документа, потом суммы.
            table.Columns.Add("Наименование материала, вид операции", typeof(string));
            table.Columns.Add("Дата", typeof(string));
            table.Columns.Add("N докум", typeof(string));
            table.Columns.Add("Дебет", typeof(string));
            table.Columns.Add("Кредит", typeof(string));
            table.Columns.Add("Сумма Дт", typeof(decimal));
            table.Columns.Add("Сумма Кт", typeof(decimal));
            table.Columns.Add("Сумма вал.", typeof(decimal));
            table.Columns.Add("Модуль", typeof(string));

            // Колонки под канонические имена макета: механизм подстановки ищет
            // значения именно в них, иначе в макете печатается текст выражения.
            AddAliasColumn(table, "operation_name", typeof(string));
            AddAliasColumn(table, "debit_account", typeof(string));
            AddAliasColumn(table, "credit_account", typeof(string));
            AddAliasColumn(table, "debit_amount", typeof(decimal));
            AddAliasColumn(table, "credit_amount", typeof(decimal));
            AddAliasColumn(table, "debit_begin", typeof(decimal));
            AddAliasColumn(table, "credit_begin", typeof(decimal));
            AddAliasColumn(table, "document_number", typeof(string));
            AddAliasColumn(table, "document_date", typeof(string));
            // Макет акта берёт дату из posting_date: значение заполняет вызывающая сторона.
            AddAliasColumn(table, "posting_date", typeof(string));
            AddAliasColumn(table, "module_code", typeof(string));
            AddAliasColumn(table, "module", typeof(string));
            AddAliasColumn(table, "report_title", typeof(string));
            AddAliasColumn(table, "report_summary", typeof(string));
            AddAliasColumn(table, "period_start", typeof(string));
            AddAliasColumn(table, "period_end", typeof(string));
            // Стороны акта: их читает блок подписей макета.
            AddAliasColumn(table, "our_organization", typeof(string));
            AddAliasColumn(table, "our_director", typeof(string));
            AddAliasColumn(table, "our_accountant", typeof(string));
            AddAliasColumn(table, "counterparty", typeof(string));
            AddAliasColumn(table, "counterparty_director", typeof(string));
            AddAliasColumn(table, "counterparty_accountant", typeof(string));
            return table;
        }

        private static void AddAliasColumn(DataTable table, string columnName, Type columnType)
        {
            if (!table.Columns.Contains(columnName))
                table.Columns.Add(columnName, columnType);
        }

        /// <summary>
        /// Загружает движения по проводкам за период. Фильтр по организации —
        /// необязательный: при пустом значении возвращаются все организации.
        /// </summary>
        public async Task<List<ReconciliationMovementRow>> LoadMovementsAsync(
            Guid? organizationId,
            DateTime start,
            DateTime end)
        {
            var rows = new List<ReconciliationMovementRow>();
            try
            {
                await _context.Database.ExecuteSqlRawAsync(@"
                    DO $$
                    BEGIN
                        IF to_regclass('public.doc_postings') IS NOT NULL THEN
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2) NOT NULL DEFAULT 0;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS organization_id uuid;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS description text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        END IF;
                    END $$;");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                return rows;
            }

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT p.posting_date, p.doc_number, COALESCE(p.document_type, 'Проводка') AS document_type,
                       COALESCE(p.module_code, '') AS module_code,
                       p.debit_account, p.credit_account, COALESCE(p.amount_kgs, 0) AS amount_kgs,
                       COALESCE(p.amount_currency, 0) AS amount_currency,
                       COALESCE(c.""code"", '') AS currency_code,
                       COALESCE(p.description, '') AS description,
                       CASE
                           WHEN COALESCE(p.organization_id::text, '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                           THEN p.organization_id::text
                           ELSE NULL
                       END AS organization_id_text,
                       COALESCE(NULLIF(o.""name"", ''), 'Без организации') AS organization_name,
                       COALESCE(o.""code"", '') AS organization_code
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON p.organization_id::text = o.""Id""::text
                -- currency_id в проводках хранится текстом (исторически),
                -- поэтому сравниваем как текст: uuid = text PostgreSQL не принимает.
                LEFT JOIN catalog_currencies c ON c.""Id""::text = p.currency_id::text
                WHERE COALESCE(p.is_active, true) = true
                  AND (COALESCE(@organizationId, '') = '' OR p.organization_id::text = @organizationId)
                  AND p.posting_date >= @startDate
                  AND p.posting_date < @endDateExclusive
                ORDER BY organization_name, p.posting_date, p.doc_number, p.debit_account, p.credit_account";
            command.Parameters.Add(new NpgsqlParameter("@organizationId",
                organizationId.HasValue && organizationId.Value != Guid.Empty ? organizationId.Value.ToString() : string.Empty));
            command.Parameters.Add(new NpgsqlParameter("@startDate", DateTime.SpecifyKind(start.Date, DateTimeKind.Utc)));
            command.Parameters.Add(new NpgsqlParameter("@endDateExclusive", DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc)));

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var postingDate = reader["posting_date"] is DateTime date
                        ? date
                        : Convert.ToDateTime(reader["posting_date"], CultureInfo.InvariantCulture);

                    rows.Add(new ReconciliationMovementRow
                    {
                        Date = postingDate,
                        OrganizationId = Guid.TryParse(reader["organization_id_text"]?.ToString(), out var movementOrganizationId)
                            ? movementOrganizationId
                            : null,
                        OrganizationName = reader["organization_name"]?.ToString() ?? "Без организации",
                        OrganizationCode = reader["organization_code"]?.ToString() ?? string.Empty,
                        DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(reader["doc_number"]?.ToString()),
                        DocumentType = reader["document_type"]?.ToString() ?? string.Empty,
                        ModuleCode = reader["module_code"]?.ToString() ?? string.Empty,
                        DebitAccount = reader["debit_account"]?.ToString() ?? string.Empty,
                        CreditAccount = reader["credit_account"]?.ToString() ?? string.Empty,
                        Amount = ReadDecimal(reader["amount_kgs"]),
                        Description = reader["description"]?.ToString() ?? string.Empty,
                        AmountCurrency = ReadDecimal(reader["amount_currency"]),
                        CurrencyCode = reader["currency_code"]?.ToString() ?? string.Empty
                    });
                }
            }
            catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703")
            {
                rows.Clear();
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return rows;
        }

        private static decimal ReadDecimal(object? value) =>
            value == null || value == DBNull.Value
                ? 0m
                : Convert.ToDecimal(value, CultureInfo.InvariantCulture);

        /// <summary>
        /// Строит таблицу акта сверки. Возвращает таблицу и описание того,
        /// что в неё попало — для диагностики.
        /// </summary>
        public ReconciliationActTable BuildTable(
            IReadOnlyList<OrganizationBalanceRow> pairs,
            IReadOnlyList<OrganizationBalanceRow> totals,
            IReadOnlyList<ReconciliationMovementRow> movements,
            DateTime start,
            DateTime end,
            bool scoped,
            string titleName,
            ReconciliationActParties? parties = null)
        {
            var table = CreateTable();
            var trace = new List<string>();

            // Наша сторона — первичная организация: её наименование печатается в заголовке.
            var ourName = string.IsNullOrWhiteSpace(parties?.OurName) ? "нашей организацией" : parties!.OurName;
            AddRow(table, scoped
                ? $"АКТ СВЕРКИ между {ourName} и \"{titleName}\""
                : "АКТЫ СВЕРКИ ПО ВСЕМ ОРГАНИЗАЦИЯМ");
            AddRow(table, $"Период: {start:dd/MM/yyyy} - {end:dd/MM/yyyy}");
            AddRow(table, string.Empty);

            if (pairs.Count == 0)
            {
                AddRow(table, scoped
                    ? "Данных по выбранной организации и активным парам счетов за период не найдено."
                    : "Данных по организациям и активным парам счетов за период не найдено.");
            }
            else if (scoped)
            {
                var total = totals.FirstOrDefault(row => MatchesSelectedOrganization(row, null, null));
                AddSection(table, titleName, pairs, total, movements, start, end, false);
                trace.Add($"режим: одна организация '{titleName}', пар {pairs.Count}");
            }
            else
            {
                var groups = pairs
                    .GroupBy(row => new { row.OrganizationId, Name = NormalizeOrganizationName(row.OrganizationName) })
                    .OrderBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var group in groups)
                {
                    var groupName = string.IsNullOrWhiteSpace(group.Key.Name) ? "Без организации" : group.Key.Name;
                    var total = totals.FirstOrDefault(row => SameOrganization(row, group.Key.OrganizationId, groupName));
                    var groupMovements = movements
                        .Where(movement => MatchesMovementOrganization(movement, group.Key.OrganizationId, groupName))
                        .ToList();

                    AddSection(table, groupName, group.ToList(), total, groupMovements, start, end, true);
                    AddRow(table, string.Empty);

                    trace.Add($"организация '{groupName}': пар {group.Count()}, " +
                              $"движений подходящих {groupMovements.Count()} из {movements.Count}");
                }

                trace.Insert(0, $"режим: все организации, групп {groups.Count}, пар всего {pairs.Count}, движений всего {movements.Count}");
            }

            var balance = scoped
                ? totals.FirstOrDefault(row => MatchesSelectedOrganization(row, null, null))?.Balance ?? pairs.Sum(row => row.Balance)
                : totals.Sum(row => row.Balance);
            var summary = scoped
                ? BuildDebtSummary(titleName, balance)
                : BuildAllOrganizationsDebtSummary(totals.Count, balance);
            AddRow(table, summary);
            ApplyHeaderFields(table, start, end, summary);
            ApplyPartyFields(table, parties);

            trace.Add($"строк в таблице: {table.Rows.Count}");
            return new ReconciliationActTable(table, summary, trace);
        }

        private static void AddSection(
            DataTable table,
            string organizationTitle,
            IReadOnlyList<OrganizationBalanceRow> pairs,
            OrganizationBalanceRow? total,
            IReadOnlyList<ReconciliationMovementRow> movements,
            DateTime start,
            DateTime end,
            bool showOrganizationHeader)
        {
            if (showOrganizationHeader)
                AddRow(table, $"Организация: {organizationTitle}");

            var firstPair = true;
            foreach (var pair in pairs)
            {
                if (!firstPair)
                    AddRow(table, string.Empty);
                firstPair = false;

                var pairName = string.IsNullOrWhiteSpace(pair.AccountPairName) ? pair.CounterAccountName : pair.AccountPairName;
                AddRow(table, $"Пара счетов : {pair.AccountCode} - {pair.CounterAccountCode} ({pairName})");
                var pairMovements = movements
                    .Where(movement => AccountInPair(movement.DebitAccount, pair) || AccountInPair(movement.CreditAccount, pair))
                    .OrderBy(movement => movement.Date)
                    .ThenBy(movement => movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                var currencyCodes = pairMovements.Select(movement => movement.CurrencyCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var showCurrencyTotals = pair.UsesCurrency && currencyCodes.Count <= 1;
                AddRow(table, $"САЛЬДО НА {start:dd/MM/yyyy}",
                    debitAmount: NonZeroAmount(pair.OpeningDebit),
                    creditAmount: NonZeroAmount(pair.OpeningCredit),
                    currencyAmount: showCurrencyTotals ? pair.OpeningDebitCurrency - pair.OpeningCreditCurrency : null);
                if (currencyCodes.Count > 1)
                    AddRow(table, "Валютные итоги не суммируются: в паре несколько валют. Суммы указаны в движениях.");

                if (pairMovements.Count == 0 && (pair.TurnoverDebit != 0 || pair.TurnoverCredit != 0))
                    AddRow(table, "Движения найдены в остатках, но детализация проводок недоступна.");

                foreach (var movement in pairMovements)
                {
                    var movementDate = movement.Date == default ? end : movement.Date;
                    AddRow(table,
                        BuildMovementDescription(movement) + (string.IsNullOrWhiteSpace(movement.CurrencyCode) ? "" : $" ({movement.CurrencyCode})"),
                        movement.DebitAccount,
                        movement.CreditAccount,
                        AccountInPair(movement.DebitAccount, pair) ? movement.Amount : null,
                        AccountInPair(movement.CreditAccount, pair) ? movement.Amount : null,
                        movement.DocumentNumber,
                        movementDate.ToString("dd/MM/yyyy"),
                        movement.ModuleCode, currencyAmount: movement.AmountCurrency);
                }

                AddRow(table, "ИТОГО ОБОРОТОВ",
                    debitAmount: NonZeroAmount(pair.TurnoverDebit),
                    creditAmount: NonZeroAmount(pair.TurnoverCredit),
                    currencyAmount: showCurrencyTotals ? pair.TurnoverDebitCurrency - pair.TurnoverCreditCurrency : null);
                AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy}",
                    debitAmount: NonZeroAmount(pair.ClosingDebit),
                    creditAmount: NonZeroAmount(pair.ClosingCredit),
                    currencyAmount: showCurrencyTotals ? pair.ClosingDebitCurrency - pair.ClosingCreditCurrency : null);
            }

            if (total != null && pairs.Count > 1)
            {
                AddRow(table, string.Empty);
                var oneCurrency = movements.Select(movement => movement.CurrencyCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                AddRow(table, "ИТОГО ОБОРОТОВ ПО ОРГАНИЗАЦИИ",
                    debitAmount: NonZeroAmount(total.TurnoverDebit),
                    creditAmount: NonZeroAmount(total.TurnoverCredit),
                    currencyAmount: oneCurrency ? total.TurnoverDebitCurrency - total.TurnoverCreditCurrency : null);
                AddRow(table, $"САЛЬДО НА {end:dd/MM/yyyy} ПО ОРГАНИЗАЦИИ",
                    debitAmount: NonZeroAmount(total.ClosingDebit),
                    creditAmount: NonZeroAmount(total.ClosingCredit),
                    currencyAmount: oneCurrency ? total.ClosingDebitCurrency - total.ClosingCreditCurrency : null);
            }
        }

        private static decimal? NonZeroAmount(decimal value) => value == 0m ? null : value;

        private static string BuildMovementDescription(ReconciliationMovementRow movement)
        {
            var description = string.IsNullOrWhiteSpace(movement.Description)
                ? (string.IsNullOrWhiteSpace(movement.DocumentType) ? "Проводка" : movement.DocumentType)
                : movement.Description;

            return ShortenText(description);
        }

        /// <summary>
        /// Сокращает длинное описание операции: убирает перечисление модулей
        /// и ограничивает длину, иначе текст не помещается в колонку.
        /// </summary>
        internal static string ShortenText(string? text, int maxLength = 70)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length == 0)
                return value;

            var separatorIndex = value.LastIndexOf(';');
            if (separatorIndex >= 0 && separatorIndex < value.Length - 1)
            {
                var tail = value[(separatorIndex + 1)..].Trim();
                if (tail.Length > 0)
                    value = tail;
            }

            return value.Length <= maxLength ? value : value[..maxLength].TrimEnd() + "…";
        }

        /// <summary>Заголовок документа не должен попадать в колонку операций.</summary>
        internal static bool IsDocumentTitleText(string? text)
        {
            var value = (text ?? string.Empty).Trim();
            return value.StartsWith("АКТ СВЕРКИ", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("АКТЫ СВЕРКИ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Добавляет строку таблицы. Служебные колонки заполняются каноническими
        /// именами: механизм подстановки FRX-макета ищет значения именно в них.
        /// </summary>
        internal static void AddRow(
            DataTable table,
            string operation,
            string debit = "",
            string credit = "",
            decimal? debitAmount = null,
            decimal? creditAmount = null,
            string documentNumber = "",
            string date = "",
            string moduleCode = "", decimal? currencyAmount = null)
        {
            var row = table.NewRow();
            row["Наименование материала, вид операции"] = operation;
            row["Дебет"] = debit;
            row["Кредит"] = credit;
            row["Сумма вал."] = (object?)currencyAmount ?? DBNull.Value;
            row["Сумма Дт"] = debitAmount.HasValue ? debitAmount.Value : DBNull.Value;
            row["Сумма Кт"] = creditAmount.HasValue ? creditAmount.Value : DBNull.Value;
            row["N докум"] = documentNumber;
            row["Дата"] = date;
            row["Модуль"] = moduleCode;

            SetServiceValue(row, "operation_name", text: IsDocumentTitleText(operation) ? null : operation);
            SetServiceValue(row, "debit_account", text: debit);
            SetServiceValue(row, "credit_account", text: credit);
            SetServiceValue(row, "document_number", text: documentNumber);
            SetServiceValue(row, "document_date", text: date);
            SetServiceValue(row, "module", text: moduleCode);
            SetServiceValue(row, "debit_amount", amount: debitAmount);
            SetServiceValue(row, "credit_amount", amount: creditAmount);

            table.Rows.Add(row);
        }

        /// <summary>Записывает значение в служебную колонку; в числовую — только DBNull.</summary>
        internal static void SetServiceValue(DataRow row, string columnName, string? text = null, decimal? amount = null)
        {
            if (!row.Table.Columns.Contains(columnName))
                return;

            if (amount.HasValue)
            {
                row[columnName] = amount.Value;
                return;
            }

            var column = row.Table.Columns[columnName]!;
            if (IsNumericColumn(column))
            {
                row[columnName] = DBNull.Value;
                return;
            }

            row[columnName] = string.IsNullOrWhiteSpace(text) ? DBNull.Value : text;
        }

        private static bool IsNumericColumn(DataColumn column) =>
            column.DataType == typeof(decimal) ||
            column.DataType == typeof(double) ||
            column.DataType == typeof(float) ||
            column.DataType == typeof(int) ||
            column.DataType == typeof(long) ||
            column.DataType == typeof(short);

        /// <summary>Проставляет поля шапки макета во всех строках: их читает полоса Title.</summary>
        internal static void ApplyHeaderFields(DataTable table, DateTime start, DateTime end, string summary)
        {
            var periodStart = start.ToString("dd/MM/yyyy");
            var periodEnd = end.ToString("dd/MM/yyyy");

            foreach (DataRow row in table.Rows)
            {
                SetServiceValue(row, "report_title", text: "АКТ СВЕРКИ");
                SetServiceValue(row, "report_summary", text: summary);
                SetServiceValue(row, "period_start", text: periodStart);
                SetServiceValue(row, "period_end", text: periodEnd);
            }
        }

        /// <summary>
        /// Проставляет стороны акта во все строки: их читает блок подписей макета.
        /// </summary>
        internal static void ApplyPartyFields(DataTable table, ReconciliationActParties? parties)
        {
            if (parties == null)
                return;

            foreach (DataRow row in table.Rows)
            {
                SetServiceValue(row, "our_organization", text: parties.OurName);
                SetServiceValue(row, "our_director", text: parties.OurDirector);
                SetServiceValue(row, "our_accountant", text: parties.OurAccountant);
                SetServiceValue(row, "counterparty", text: parties.CounterpartyName);
                SetServiceValue(row, "counterparty_director", text: parties.CounterpartyDirector);
                SetServiceValue(row, "counterparty_accountant", text: parties.CounterpartyAccountant);
            }
        }

        public static bool AccountInPair(string accountCode, OrganizationBalanceRow pair) =>
            SameAccount(accountCode, pair.AccountCode) || SameAccount(accountCode, pair.CounterAccountCode);

        private static bool SameAccount(string left, string right) =>
            left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool SameOrganization(OrganizationBalanceRow row, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return row.OrganizationId.HasValue && row.OrganizationId.Value == organizationId.Value;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return rowName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool MatchesMovementOrganization(ReconciliationMovementRow movement, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return movement.OrganizationId.HasValue && movement.OrganizationId.Value == organizationId.Value;

            var movementName = NormalizeOrganizationName(movement.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return movementName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        public static bool MatchesSelectedOrganization(OrganizationBalanceRow row, Guid? selectedId, string? selectedName)
        {
            if (!selectedId.HasValue || selectedId.Value == Guid.Empty)
                return true;
            if (row.OrganizationId.HasValue && row.OrganizationId.Value == selectedId.Value)
                return true;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(selectedName);
            return !string.IsNullOrWhiteSpace(filterName) &&
                   rowName.Equals(filterName, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>Отбрасывает префикс кода организации: «123 - Название» → «Название».</summary>
        public static string NormalizeOrganizationName(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex >= 0 ? text[(separatorIndex + 3)..].Trim() : text;
        }

        /// <summary>
        /// Итоговая фраза акта. Сумма печатается числом и прописью: в бланке
        /// пропись обязательна, а число нужно бухгалтеру для сверки с таблицей.
        /// </summary>
        private static string BuildDebtSummary(string organizationName, decimal balance)
        {
            var direction = balance >= 0 ? "перед нами" : "за нами";
            var amount = Math.Abs(balance);
            return $"Задолженность {organizationName} {direction} составляет {amount:N2} " +
                   $"({PrintFormService.RussianMoneyInWords(amount)})";
        }

        private static string BuildAllOrganizationsDebtSummary(int organizationCount, decimal balance)
        {
            var direction = balance >= 0 ? "перед нами" : "за нами";
            var amount = Math.Abs(balance);
            return $"Задолженность по {organizationCount} организациям {direction} составляет {amount:N2} " +
                   $"({PrintFormService.RussianMoneyInWords(amount)})";
        }
    }

    /// <summary>Результат построения таблицы акта сверки вместе с диагностическим следом.</summary>
    public sealed record ReconciliationActTable(DataTable Table, string Summary, IReadOnlyList<string> Trace);
}
```

### Views/AccountingReportsView.xaml

SHA-256: `d29860a23277d58bd13cf30b1c8e238d87e100e66ae8d0311e979fd2709263a3`

```xml
<UserControl x:Class="BIS.ERP.Views.AccountingReportsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <StackPanel Margin="0,0,0,15">
            <TextBlock Text="Бухгалтерская отчетность" FontSize="24" FontWeight="Bold"/>
            <TextBlock Text="Баланс, финансовые результаты, журналы и контроль периода"
                       Foreground="Gray" Margin="0,4,0,0"/>
        </StackPanel>

        <Border Grid.Row="1" Background="{DynamicResource AppSurfaceBrush}" CornerRadius="8" Padding="12" Margin="0,0,0,12">
            <StackPanel>
                <WrapPanel VerticalAlignment="Center">
                    <TextBlock Text="Отчет:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <!-- Список отчётов берётся из базы: Reports, где ShowInAccountingReports.
                         Отметка ставится в конструкторе отчётов. -->
                    <ComboBox x:Name="ReportTypeCombo" Width="280" Height="32"
                              DisplayMemberPath="DisplayName"
                              SelectionChanged="OnReportTypeSelectionChanged"
                              ToolTip="Показываются отчёты, отмеченные в конструкторе как «Показывать в бухгалтерской отчетности»."/>
                    <TextBlock Text="С:" VerticalAlignment="Center" Margin="16,0,6,0"/>
                    <DatePicker x:Name="StartDatePicker" Width="130" Height="32"/>
                    <TextBlock Text="По:" VerticalAlignment="Center" Margin="12,0,6,0"/>
                    <DatePicker x:Name="EndDatePicker" Width="130" Height="32"/>
                    <Button Content="Сформировать" Width="125" Height="34" Margin="16,0,6,0"
                            Background="#27AE60" Foreground="White" Click="OnGenerateClick"/>
                    <Button x:Name="ExportExcelButton" Content="📊 Excel" Width="120" Height="34" Margin="10,0,6,0"
                            Background="#1E8449" Foreground="White" Click="OnExportExcelButtonClick"
                            ToolTip="Сформировать отчет и открыть его в Excel"/>
                    <Button x:Name="ExportPdfButton" Content="📄 PDF" Width="110" Height="34" Margin="0,0,6,0"
                            Background="#8E44AD" Foreground="White" Click="OnExportPdfButtonClick"
                            ToolTip="Сформировать отчет и показать его в окне предпросмотра"/>
                </WrapPanel>
                <WrapPanel x:Name="TrialBalanceFilterPanel" Margin="0,10,0,0">
                    <TextBlock Text="Счет:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <TextBox x:Name="TrialBalanceAccountTextBox" Width="320" Height="32" Padding="8,4"
                             IsReadOnly="True" ToolTip="Выберите счет для оборотной ведомости"/>
                    <Button Content="?" Width="34" Height="32" Margin="6,0,0,0"
                            Click="OnSelectTrialBalanceAccountClick"
                            ToolTip="Выбрать счет из плана счетов"/>
                    <Button Content="✕" Width="24" Height="24" Margin="6,0,0,0"
                            Click="OnClearTrialBalanceAccountClick"
                            Background="Transparent" BorderThickness="0" Foreground="#E74C3C"
                            ToolTip="Сбросить счет" Cursor="Hand"/>
                    <TextBlock Text="Организация и период выбираются ниже"
                               VerticalAlignment="Center" Margin="16,0,0,0"
                               Foreground="{DynamicResource AppSecondaryTextBrush}"/>
                </WrapPanel>
                <WrapPanel x:Name="TurnoverPairPanel" Margin="0,10,0,0" Visibility="Collapsed">
                    <TextBlock Text="Пара счетов:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <TextBox x:Name="TurnoverPairTextBox" Width="460" Height="32" Padding="8,4" IsReadOnly="True"/>
                    <Button Content="?" Width="34" Height="32" Margin="6,0,0,0"
                            Click="OnSelectTurnoverPairClick" ToolTip="Выбрать из справочника пар счетов"/>
                </WrapPanel>
                <WrapPanel x:Name="ReconciliationVariantPanel" Margin="0,10,0,0" Visibility="Collapsed">
                    <TextBlock Text="Вариант акта сверки:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <ComboBox x:Name="ReconciliationVariantCombo" Width="360" Height="32"
                              DisplayMemberPath="DisplayName"
                              ToolTip="Макеты акта сверки хранятся в метаданных и выбираются здесь, а формат вывода выбирается сверху."/>
                    <TextBlock x:Name="ReconciliationVariantHint" Text="Варианты из метаданных отчетов"
                               VerticalAlignment="Center" Margin="12,0,0,0"
                               Foreground="{DynamicResource AppSecondaryTextBrush}"/>
                </WrapPanel>
                <WrapPanel x:Name="ClosedXmlReconciliationPanel" Margin="0,10,0,0" Visibility="Collapsed">
                    <TextBlock VerticalAlignment="Center" TextWrapping="Wrap"
                               Foreground="{DynamicResource AppSecondaryTextBrush}"
                               Text="Макет: ClosedXML (визуальный Excel). Формат вывода переключён на «ClosedXML Excel» — отчёт строится встроенным визуальным макетом, FRX-варианты не применяются. Открыть и изменить макет можно в конструкторе отчётов."/>
                </WrapPanel>
                <WrapPanel x:Name="TrialBalanceClosedXmlPanel" Margin="0,10,0,0" Visibility="Collapsed">
                    <TextBlock VerticalAlignment="Center" TextWrapping="Wrap"
                               Foreground="{DynamicResource AppSecondaryTextBrush}"
                               Text="Макет: ClosedXML (визуальный Excel). Формат вывода переключён на «ClosedXML Excel». Макет хранится в отчёте с кодом standard.finance.trial-balance-closedxml и правится в конструкторе отчётов."/>
                </WrapPanel>
                <WrapPanel Margin="0,10,0,0">
                    <TextBlock Text="Организация:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <TextBox x:Name="OrganizationNumberTextBox" Width="240" Height="32" Padding="8,4"
                             KeyDown="OnOrganizationNumberKeyDown"
                             ToolTip="Впишите номер организации и нажмите Enter. Список откроется по кнопке «?»."/>
                    <Button Content="?" Width="34" Height="32" Margin="6,0,0,0"
                            Click="OnSelectOrganizationClick"
                            ToolTip="Выбрать организацию из справочника"/>
                    <CheckBox x:Name="AllOrganizationsCheckBox" Content="Все организации"
                              VerticalAlignment="Center" Margin="12,0,0,0" IsChecked="True"
                              Checked="OnAllOrganizationsChanged" Unchecked="OnAllOrganizationsChanged"
                              ToolTip="Отчет строится по всем организациям. Снимите отметку, чтобы выбрать одну."/>
                    <TextBlock x:Name="OrganizationHintText" Text=""
                               VerticalAlignment="Center" Margin="12,0,0,0"
                               Foreground="{DynamicResource AppSecondaryTextBrush}"/>
                </WrapPanel>
                <WrapPanel Margin="0,10,0,0">
                    <TextBlock Text="Поиск:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                    <TextBox x:Name="SearchBox" Width="250" Height="32" Padding="8,4"
                             TextChanged="OnSearchChanged" ToolTip="Поиск по всем колонкам отчета"/>
                </WrapPanel>
            </StackPanel>
        </Border>
        <!-- Колонки строятся по таблице отчёта: у каждого отчёта свой состав. -->
        <DataGrid x:Name="ReportGrid" Grid.Row="2" AutoGenerateColumns="True" IsReadOnly="True"
                  CanUserAddRows="False" CanUserDeleteRows="False" FrozenColumnCount="2"
                  MouseDoubleClick="OnReportGridDoubleClick"
                  AutoGeneratingColumn="OnReportGridAutoGeneratingColumn"/>

        <StatusBar Grid.Row="3" Margin="0,10,0,0">
            <StatusBarItem><TextBlock x:Name="StatusText" Text="Выберите отчет и период"/></StatusBarItem>
        </StatusBar>
    </Grid>
</UserControl>
```

### Views/AccountingReportsView.xaml.cs

SHA-256: `0c9d92807a2c25ccb50c5039feef5f118eb741f81a8c6d4ddb63ddac03ae56cf`

```csharp
using BIS.ERP.Behaviors;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Views
{
    public partial class AccountingReportsView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly BalanceService _balanceService;
        private readonly OrganizationBalanceService _organizationBalanceService;
        private readonly ReportService _reportService;
        private readonly AccountingPeriodService _periodService;
        private readonly PostingService _postingService;
        private readonly MetadataService _metadataService;
        private DataTable? _currentData;
        private Report? _currentReport;
        private bool _reconciliationVariantsLoaded;
        private bool _trialBalanceVariantsLoaded;

        /// <summary>Виды ОСВ. Список из одного расчётного варианта.</summary>
        private List<TrialBalanceReportVariant> _trialBalanceVariants = new();
        private Guid? _selectedOrganizationId;
        private string? _selectedOrganizationName;
        private string _trialBalanceAccountCode = string.Empty;
        private string _trialBalanceAccountName = string.Empty;
        private IReadOnlyList<OrganizationSelectionItem> _organizationSelectionItems = Array.Empty<OrganizationSelectionItem>();

        public AccountingReportsView(AppDbContext context)
        {
            InitializeComponent();
            AmountColumnsBehavior.Attach(ReportGrid); // авто-генерируемые колонки отчётов — суммы с разрядами через пробел
            // Служебные колонки акта сверки нужны только механизму подстановки FRX-макета
            // и не должны появляться в экранной таблице, поэтому отменяем их генерацию.
            ReportGrid.AutoGeneratingColumn += OnReportGridAutoGeneratingColumn;
            _context = context;
            _balanceService = new BalanceService(context);
            _organizationBalanceService = new OrganizationBalanceService(context);
            _reportService = new ReportService(context);
            _periodService = new AccountingPeriodService(ServiceLocator.DbContextFactory);
            _postingService = new PostingService(context);
            _metadataService = new MetadataService(context);
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            EndDatePicker.SelectedDate = DateTime.Today;
            Loaded += async (_, _) =>
            {
                // Loaded срабатывает при каждом перемонтировании окна в дереве
                // (переключение вкладок, возврат из диалога). Повторная загрузка
                // пересобирала список отчётов и сбрасывала выбор пользователя
                // на первый пункт.
                if (_initialized)
                    return;

                _initialized = true;

                await LoadAccountingReportsAsync();
                await LoadReconciliationReportVariantsAsync();
                await LoadTrialBalanceReportVariantsAsync();
                await LoadOrganizationsAsync();
            };
            UpdateReconciliationVariantVisibility();
        }

        /// <summary>Первичная загрузка выполнена. Повторные Loaded её не повторяют.</summary>
        private bool _initialized;

        /// <summary>
        /// Пункт списка отчётов окна «Бухгалтерская отчётность».
        ///
        /// Список строится по таблице Reports: отчёт попадает в него, если он
        /// активен и в конструкторе отмечен «Показывать в бухгалтерской отчетности».
        /// </summary>
        private sealed class AccountingReportItem
        {
            public Guid Id { get; init; }
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string ReportType { get; init; } = string.Empty;

            /// <summary>Код набора данных отчёта; пусто — берётся по типу отчёта.</summary>
            public string DataSetCode { get; init; } = string.Empty;

            public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;

            public override string ToString() => DisplayName;
        }

        /// <summary>Выбранный отчёт списка.</summary>
        private AccountingReportItem? SelectedAccountingReport =>
            ReportTypeCombo?.SelectedItem as AccountingReportItem;

        /// <summary>
        /// Загружает список отчётов из базы.
        ///
        /// Показываются только отчёты с отметкой «Показывать в бухгалтерской
        /// отчетности», включённой в конструкторе отчётов.
        /// </summary>
        private async Task LoadAccountingReportsAsync()
        {
            try
            {
                var reports = await _context.Reports.AsNoTracking()
                    .Where(report => report.IsActive && report.ShowInAccountingReports)
                    .OrderBy(report => report.Order)
                    .ThenBy(report => report.Name)
                    .Select(report => new
                    {
                        report.Id,
                        report.Code,
                        report.Name,
                        report.ReportType
                    })
                    .ToListAsync();

                var items = reports
                    .Select(report => new AccountingReportItem
                    {
                        Id = report.Id,
                        Code = report.Code,
                        Name = report.Name,
                        ReportType = report.ReportType ?? string.Empty
                    })
                    .ToList();

                ReportTypeCombo.ItemsSource = items;

                // Выбор из главного окна применяется после загрузки списка:
                // до неё выбирать не из чего, и выбор сбрасывался на первый пункт.
                if (!string.IsNullOrWhiteSpace(_requestedReportCode))
                    ApplyRequestedReportSelection();
                else
                    ReportTypeCombo.SelectedIndex = items.Count > 0 ? 0 : -1;

                UpdateReconciliationVariantVisibility();

                StatusText.Text = items.Count == 0
                    ? "В конструкторе отчётов не отмечен ни один отчёт для этого окна"
                    : $"Доступно отчетов: {items.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки списка отчетов: {ex.Message}", "Отчеты",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Пользователь вписал номер организации вручную.
        ///
        /// По Enter номер ищется в справочнике: сначала точное совпадение кода,
        /// затем — по началу кода или наименования.
        /// </summary>
        private void OnOrganizationNumberKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            ApplyTypedOrganization();
        }

        /// <summary>
        /// Отметка «Все организации».
        ///
        /// Стоит — отчёт строится по всем организациям. Снята — нужен выбор
        /// конкретной организации: подсказка просит вписать номер.
        ///
        /// Обработчик вызывается и при построении окна: галка отмечена прямо
        /// в разметке, поэтому соседние поля могут быть ещё не созданы —
        /// все обращения к ним идут через проверки.
        /// </summary>
        private void OnAllOrganizationsChanged(object sender, RoutedEventArgs e)
        {
            if (AllOrganizationsCheckBox?.IsChecked == true)
            {
                _selectedOrganizationId = null;
                _selectedOrganizationName = null;

                if (OrganizationNumberTextBox != null)
                    OrganizationNumberTextBox.Text = string.Empty;

                if (OrganizationHintText != null)
                    OrganizationHintText.Text = string.Empty;

                return;
            }

            UpdateOrganizationHint();

            if (!_selectedOrganizationId.HasValue && OrganizationHintText != null)
                OrganizationHintText.Text = "Впишите номер организации";
        }

        /// <summary>
        /// Применяет номер организации, вписанный пользователем.
        ///
        /// Если совпадение не найдено, выбор не меняется и пользователь
        /// видит подсказку.
        /// </summary>
        private void ApplyTypedOrganization()
        {
            var typed = (OrganizationNumberTextBox?.Text ?? string.Empty).Trim();
            if (typed.Length == 0)
            {
                ClearOrganizationFilter();
                return;
            }

            var match = FindOrganizationByTyped(typed);
            if (match == null)
            {
                if (OrganizationHintText != null)
                    OrganizationHintText.Text = $"Организация «{typed}» не найдена";

                return;
            }

            SelectOrganization(match);
        }

        /// <summary>
        /// Ищет организацию по вписанному номеру: точное совпадение кода,
        /// затем код или наименование с этого текста.
        /// </summary>
        private OrganizationSelectionItem? FindOrganizationByTyped(string typed)
        {
            var items = _organizationSelectionItems;
            if (items.Count == 0)
                return null;

            var exact = items.FirstOrDefault(item =>
                string.Equals(item.Code, typed, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return exact;

            return items.FirstOrDefault(item =>
                       item.Code?.StartsWith(typed, StringComparison.OrdinalIgnoreCase) == true)
                   ?? items.FirstOrDefault(item =>
                       item.Name?.StartsWith(typed, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Применяет выбранную организацию: подставляет её номер в поле
        /// и снимает отметку «Все организации».
        /// </summary>
        private void SelectOrganization(OrganizationSelectionItem organization)
        {
            _selectedOrganizationId = organization.IsAll ? null : organization.Id;
            _selectedOrganizationName = organization.IsAll ? null : organization.Name;

            if (OrganizationNumberTextBox != null)
            {
                // Если у организации нет номера, показываем наименование:
                // поле не должно остаться пустым после выбора.
                OrganizationNumberTextBox.Text = string.IsNullOrWhiteSpace(organization.Code)
                    ? organization.Name ?? string.Empty
                    : organization.Code;
            }

            // Явный выбор организации снимает отметку «Все организации».
            if (AllOrganizationsCheckBox != null && AllOrganizationsCheckBox.IsChecked == true)
                AllOrganizationsCheckBox.IsChecked = false;

            UpdateOrganizationHint();
        }

        /// <summary>Показывает, по какой организации строится отчёт.</summary>
        private void UpdateOrganizationHint()
        {
            if (OrganizationHintText == null)
                return;

            OrganizationHintText.Text = _selectedOrganizationId.HasValue
                ? _selectedOrganizationName
                : string.Empty;
        }

        /// <summary>
        /// Снимает выбор организации: отчёт строится по всем организациям.
        /// </summary>
        private void ClearOrganizationFilter()
        {
            _selectedOrganizationId = null;
            _selectedOrganizationName = null;

            if (OrganizationNumberTextBox != null)
                OrganizationNumberTextBox.Text = string.Empty;

            if (AllOrganizationsCheckBox != null)
                AllOrganizationsCheckBox.IsChecked = true;

            UpdateOrganizationHint();
        }

        private async void OnSelectTrialBalanceAccountClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Загрузка плана счетов...";
                var accounts = await LoadTrialBalanceAccountSelectionDataAsync();
                if (accounts.Count == 0)
                {
                    MessageBox.Show("План счетов пуст или недоступен.", "План счетов",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var owner = Window.GetWindow(this);
                var selection = new AccountSelectionView(accounts);

                if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(owner, "Выбор счета", selection) != true || selection.SelectedAccount == null)
                    return;

                _trialBalanceAccountCode = ReadReportString(selection.SelectedAccount, "Код", "code", "Code", "Счет", "account_code");
                _trialBalanceAccountName = ReadReportString(selection.SelectedAccount, "Наименование", "name", "Name");
                UpdateTrialBalanceAccountText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выбора счета: {ex.Message}", "План счетов",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
                StatusText.Text = "Выберите отчет и период";
            }
        }

        private void OnClearTrialBalanceAccountClick(object sender, RoutedEventArgs e)
        {
            _trialBalanceAccountCode = string.Empty;
            _trialBalanceAccountName = string.Empty;
            UpdateTrialBalanceAccountText();
        }

        private void UpdateTrialBalanceAccountText()
        {
            if (TrialBalanceAccountTextBox == null)
                return;

            TrialBalanceAccountTextBox.Text = string.IsNullOrWhiteSpace(_trialBalanceAccountCode)
                ? string.Empty
                : string.IsNullOrWhiteSpace(_trialBalanceAccountName)
                    ? _trialBalanceAccountCode
                    : $"{_trialBalanceAccountCode} - {_trialBalanceAccountName}";
        }

        private async void OnSelectOrganizationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Загрузка организаций...";

                var organizations = (await LoadOrganizationSelectionItemsAsync(false)).ToList();
                if (organizations.Count == 0)
                {
                    MessageBox.Show(
                        "В справочнике организаций нет записей. Проверьте catalog_organizations или системный справочник Organizations.",
                        "Организации", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var rows = organizations.Select(org => new Dictionary<string, object>
                {
                    ["Id"] = org.Id ?? Guid.Empty,
                    ["Код"] = org.Code,
                    ["Наименование"] = org.Name,
                    ["Полное наименование"] = org.FullName,
                    ["Активна"] = LocalizationService.DisplayValue(org.IsActive),
                    ["Первичная"] = LocalizationService.DisplayValue(org.IsPrimary)
                }).ToList();

                var owner = Window.GetWindow(this);
                var dialog = new ReferenceSelectionDialog(rows, "Код", "Наименование")
                {
                    Title = $"Выбор: Организации ({organizations.Count})"
                };
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(owner, dialog, dialog.Title) != true || dialog.SelectedItem == null)
                    return;

                var selectedOrg = TryReadGuid(dialog.SelectedItem, out var selectedId, "Id")
                    ? organizations.FirstOrDefault(org => org.Id == selectedId)
                    : null;
                if (selectedOrg == null)
                {
                    var selectedCode = ReadReportString(dialog.SelectedItem, "Код", "code");
                    var selectedName = ReadReportString(dialog.SelectedItem, "Наименование", "name");
                    selectedOrg = organizations.FirstOrDefault(org =>
                        org.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase) &&
                        org.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
                }
                if (selectedOrg == null)
                    return;

                // Запись из справочника могла не попасть в загруженный список:
                // добавляем её, чтобы выбор не потерялся при следующей перезагрузке.
                if (_organizationSelectionItems.All(item => item.Id != selectedOrg.Id))
                {
                    _organizationSelectionItems = _organizationSelectionItems
                        .Concat(new[] { selectedOrg })
                        .ToList();
                }

                SelectOrganization(selectedOrg);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе организации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
                StatusText.Text = "Выберите отчет и период";
            }
        }
        /// <summary>
        /// Загружает организации для выбора.
        ///
        /// Отдельного поля «Первичная» нет: оно было избыточным. По умолчанию
        /// отчёт строится по всем организациям — отметка «Все организации» стоит.
        /// </summary>
        private async Task LoadOrganizationsAsync()
        {
            try
            {
                _organizationSelectionItems = await LoadOrganizationSelectionItemsAsync(true);

                if (OrganizationNumberTextBox != null)
                    OrganizationNumberTextBox.Text = string.Empty;

                if (AllOrganizationsCheckBox != null)
                    AllOrganizationsCheckBox.IsChecked = true;

                UpdateOrganizationHint();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки организаций: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<IReadOnlyList<OrganizationSelectionItem>> LoadOrganizationSelectionItemsAsync(bool includeAll)
        {
            var organizations = new List<OrganizationSelectionItem>();
            if (includeAll)
                organizations.Add(OrganizationSelectionItem.CreateAll());

            try
            {
                var metadataRows = await LoadMetadataOrganizationsAsync();
                organizations.AddRange(metadataRows.Select(BuildOrganizationSelectionItem)
                    .Where(org => org.Id.HasValue || !string.IsNullOrWhiteSpace(org.Name)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки catalog_organizations: {ex.Message}");
            }

            return DeduplicateOrganizationSelectionItems(organizations)
                .OrderByDescending(org => org.IsAll)
                .ThenByDescending(org => org.IsPrimary)
                .ThenBy(org => org.Code, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(org => org.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private async Task<List<Dictionary<string, object>>> LoadMetadataOrganizationsAsync()
        {
            var organizationCatalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            return organizationCatalog == null
                ? new List<Dictionary<string, object>>()
                : await _metadataService.GetCatalogDataAsync(organizationCatalog.Id);
        }

        private static OrganizationSelectionItem BuildOrganizationSelectionItem(Dictionary<string, object> row)
        {
            var id = TryReadGuid(row, out var organizationId, "Id") ? organizationId : (Guid?)null;
            var code = ReadReportString(row, "Код", "code");
            var name = ReadReportString(row, "Наименование", "name");
            var fullName = ReadReportString(row, "Полное наименование", "full_name");
            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrWhiteSpace(fullName) ? code : fullName;

            return new OrganizationSelectionItem
            {
                Id = id,
                Code = code,
                Name = name,
                FullName = fullName,
                IsActive = !TryGetReportValue(row, out var activeValue, "Активна", "is_active") || ReadReportBoolValue(activeValue, true),
                IsPrimary = TryGetReportValue(row, out var primaryValue, "Первичная", "Первичная организация", "is_primary") && ReadReportBoolValue(primaryValue, false)
            };
        }

        private static IReadOnlyList<OrganizationSelectionItem> DeduplicateOrganizationSelectionItems(IEnumerable<OrganizationSelectionItem> organizations)
        {
            return organizations
                .Where(org => org.IsAll || org.Id.HasValue || !string.IsNullOrWhiteSpace(org.Name))
                .GroupBy(org => org.Id.HasValue ? $"id:{org.Id}" : $"name:{org.Code}:{org.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        /// <summary>
        /// Код отчёта, который нужно выбрать после загрузки списка.
        ///
        /// Список грузится асинхронно, а запрос выбора приходит сразу после
        /// создания окна. Поэтому код запоминается и применяется, когда
        /// список уже построен.
        /// </summary>
        private string _requestedReportCode = string.Empty;

        /// <summary>
        /// Выбирает отчёт по коду отчёта или по типу.
        ///
        /// Вызывается из главного окна при открытии отчёта из дерева.
        /// </summary>
        public void SelectReportType(string reportType)
        {
            if (string.IsNullOrWhiteSpace(reportType))
                return;

            _requestedReportCode = reportType;
            ApplyRequestedReportSelection();
        }

        /// <summary>
        /// Применяет запомненный выбор отчёта к загруженному списку.
        ///
        /// Ищет совпадение по коду отчёта, затем по типу. Если ничего не
        /// найдено, выбор остаётся первым пунктом.
        /// </summary>
        private void ApplyRequestedReportSelection()
        {
            if (ReportTypeCombo?.ItemsSource == null || string.IsNullOrWhiteSpace(_requestedReportCode))
                return;

            var items = ReportTypeCombo.Items.OfType<AccountingReportItem>().ToList();
            if (items.Count == 0)
                return;

            var match = items.FirstOrDefault(item =>
                            string.Equals(item.Code, _requestedReportCode, StringComparison.OrdinalIgnoreCase))
                        ?? items.FirstOrDefault(item =>
                            string.Equals(item.ReportType, _requestedReportCode, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                ReportTypeCombo.SelectedItem = match;
        }

        private (DateTime Start, DateTime End) GetPeriod()
        {
            var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            var end = EndDatePicker.SelectedDate ?? DateTime.Today;
            if (end < start)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала");
            return (start, end);
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Формирование отчета...";
                var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                var end = EndDatePicker.SelectedDate ?? DateTime.Today;
                if (end < start)
                    throw new Exception("Дата окончания периода не может быть раньше даты начала");

                await _periodService.EnsureSchemaAsync();

                var selectedReport = SelectedAccountingReport;
                if (selectedReport == null)
                    throw new InvalidOperationException(
                        "В списке нет отчетов. Отметьте нужный отчет в конструкторе: «Показывать в бухгалтерской отчетности».");

                (_currentData, _currentReport) = await BuildSelectedAccountingReportAsync(selectedReport, start, end);

                ReportGrid.ItemsSource = _currentData.DefaultView;
                SearchBox.Clear();
                StatusText.Text = $"Сформировано строк: {_currentData.Rows.Count}";
            }
            catch (ReportNeedsAccountException ex)
            {
                // Не сбой, а недостающий выбор: подсказка вместо «ошибки».
                MessageBox.Show(ex.Message, "Выбор счёта",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Нужно выбрать счёт";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка формирования";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentData == null)
                return;
            var search = SearchBox.Text.Trim().Replace("'", "''").Replace("[", "[[]");
            _currentData.DefaultView.RowFilter = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : string.Join(" OR ", _currentData.Columns.Cast<DataColumn>()
                    .Where(column => column.DataType == typeof(string))
                    .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '%{search}%'"));
        }

        /// <summary>
        /// Отменяет автоматическую колонку для служебных полей акта сверки.
        /// Сетка строится по колонкам таблицы данных, поэтому без этой отмены
        /// служебные колонки попадали бы в экранную таблицу отчёта.
        /// </summary>
        private void OnReportGridAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName != null && ServiceColumnNames.Contains(e.PropertyName))
                e.Cancel = true;
        }

        private void OnReportGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ReportGrid.SelectedItem is not DataRowView row || !row.Row.Table.Columns.Contains("Счет"))
                return;
            var code = row["Счет"]?.ToString();
            if (string.IsNullOrWhiteSpace(code) || code == "ИТОГО")
                return;
            var name = row.Row.Table.Columns.Contains("Наименование") ? row["Наименование"]?.ToString() ?? code : code;
            var (start, end) = GetPeriod();
            new TurnoversDialog(code, name, start, end, _postingService) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        /// <summary>
        /// Строит выбранный отчёт по его типу из базы.
        ///
        /// Акт сверки и ведомость по счёту строятся своими построителями:
        /// им нужны отбор по счёту и особая форма. Остальные отчёты собираются
        /// по визуальному макету ClosedXML из набора данных отчёта.
        /// </summary>
        private async Task<(DataTable, Report)> BuildSelectedAccountingReportAsync(
            AccountingReportItem selected,
            DateTime start,
            DateTime end)
        {
            if (selected.ReportType == AccountPairTurnoverBuilder.ReportType)
                return await BuildAccountPairTurnoverAsync(start, end);

            if (IsReconciliationType(selected.ReportType))
                return await BuildOrganizationReconciliationAsync(start, end, closedXml: true);

            if (IsTurnoverType(selected.ReportType))
                return await BuildTurnoverStatementAsync(start, end);

            return await BuildClosedXmlReportByCodeAsync(selected.Code, start, end);
        }

        /// <summary>
        /// Оборотная ведомость по счёту по визуальному макету ClosedXML.
        ///
        /// Таблица строится системным расчётом «Движения по проводкам»: он отдаёт
        /// те же колонки, что ждёт макет (дата, документ, корреспондентский счёт,
        /// обороты, валюта, организация). Оборотно-сальдовый расчёт здесь не годится —
        /// у него колонки сальдо, и макет не нашёл бы ни одной своей колонки.
        /// </summary>
        private async Task<(DataTable, Report)> BuildTurnoverStatementAsync(DateTime start, DateTime end)
        {
            if (string.IsNullOrWhiteSpace(_trialBalanceAccountCode))
                throw new ReportNeedsAccountException(
                    "Для оборотной ведомости нужен счёт. Нажмите 🔍 рядом с полем «Счёт», выберите счёт и повторите формирование.");

            var report = await _context.Reports.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == "standard.finance.trial-balance-closedxml");

            if (report == null)
                throw new InvalidOperationException(
                    "Отчёт «Оборотная ведомость (ClosedXML)» не найден в базе. Проверьте метаданные отчётов.");

            var dataSet = await _context.ReportDataSets.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == SystemReportCalculators.MovementsCode);

            if (dataSet == null)
                throw new InvalidOperationException(
                    "Набор данных «Движения по проводкам» не найден. Проверьте наборы данных отчётов.");

            var selectedOrganization = GetSelectedOrganizationScope();

            var parameters = new Dictionary<string, object>
            {
                ["PeriodStart"] = start,
                ["PeriodEnd"] = end,
                // Отбор по счёту: без него в ведомость попадут все проводки.
                ["AccountCode"] = _trialBalanceAccountCode,
                ["OrganizationId"] = selectedOrganization?.Id?.ToString() ?? string.Empty
            };

            var table = await new ReportDataSetService(_context).ExecuteAsync(dataSet, parameters);

            var reportTitle = $"Оборотная ведомость по счету {_trialBalanceAccountCode} " +
                              $"с {start:dd/MM/yyyy} по {end:dd/MM/yyyy}";

            var current = CreateReport(table, reportTitle, true);
            current.Name = report.Name;
            current.ReportType = report.ReportType;
            current.SourceFormat = "ClosedXml";
            current.IsPrintForm = false;
            current.ClosedXmlLayout = report.ClosedXmlLayout;
            current.PageOrientation = report.PageOrientation;

            // Параметры для подстановок в шапке макета: «по счету {AccountCode}
            // с {PeriodStart:dd/MM/yyyy} по {PeriodEnd:dd/MM/yyyy}». Без них
            // в шапке печаталось «по счету с по».
            SetRenderParameters(current,
                ("AccountCode", _trialBalanceAccountCode),
                ("PeriodStart", start),
                ("PeriodEnd", end));

            return (table, current);
        }

        /// <summary>
        /// Записывает параметры отчёта, которые подставляет макет.
        ///
        /// Значения попадают в отчёт как значения по умолчанию: построитель
        /// уже выбрал данные, поэтому макет обязан показать тот же счёт и период.
        /// Даты хранятся в неизменном виде — формат задаёт сам макет.
        /// </summary>
        private static void SetRenderParameters(Report report, params (string Name, object Value)[] values)
        {
            report.Parameters ??= new List<ReportParameter>();

            var order = report.Parameters.Count;

            foreach (var (name, value) in values)
            {
                if (string.IsNullOrWhiteSpace(name) || value == null)
                    continue;

                var parameter = report.Parameters
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

                if (parameter == null)
                {
                    parameter = new ReportParameter
                    {
                        ReportId = report.Id,
                        Name = name,
                        Order = order++
                    };
                    report.Parameters.Add(parameter);
                }

                parameter.DefaultValue = value switch
                {
                    // Дата пишется в неизменном формате: его понимает разбор
                    // значения по умолчанию независимо от культуры системы.
                    DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty
                };
                parameter.ParameterType = value is DateTime
                    ? ReportParameterTypes.Date
                    : ReportParameterTypes.String;
            }
        }

        /// <summary>
        /// Строит отчёт по визуальному макету ClosedXML из его набора данных.
        ///
        /// Набор данных определяется по типу отчёта: макет ждёт определённые
        /// колонки, и набор обязан их отдавать.
        /// </summary>
        private async Task<(DataTable, Report)> BuildClosedXmlReportByCodeAsync(
            string reportCode,
            DateTime start,
            DateTime end)
        {
            var stored = await _context.Reports.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == reportCode);

            if (stored == null)
                throw new InvalidOperationException($"Отчёт с кодом «{reportCode}» не найден.");

            var dataSetCode = ResolveDataSetCodeForReport(stored.ReportType);

            var dataSet = await _context.ReportDataSets.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Code == dataSetCode);

            if (dataSet == null)
                throw new InvalidOperationException(
                    $"Набор данных «{dataSetCode}» для отчёта «{stored.Name}» не найден.");

            var selectedOrganization = GetSelectedOrganizationScope();

            var parameters = new Dictionary<string, object>
            {
                ["PeriodStart"] = start,
                ["PeriodEnd"] = end,
                ["OrganizationId"] = selectedOrganization?.Id?.ToString() ?? string.Empty
            };

            var table = await new ReportDataSetService(_context).ExecuteAsync(dataSet, parameters);

            var report = CreateReport(table, stored.Name, true);

            // Поля построителя замещаются сохранёнными в отчёте: макет читает
            // именно их, а не колонки, угаданные по таблице.
            report.Name = stored.Name;
            report.ReportType = stored.ReportType;
            report.SourceFormat = "ClosedXml";
            report.IsPrintForm = false;
            report.ClosedXmlLayout = stored.ClosedXmlLayout;

            if (!string.IsNullOrWhiteSpace(stored.PageOrientation))
                report.PageOrientation = stored.PageOrientation;

            return (table, report);
        }

        /// <summary>Набор данных, который отдаёт колонки макета этого типа отчёта.</summary>
        private static string ResolveDataSetCodeForReport(string? reportType)
        {
            if (string.Equals(reportType, "InvoiceJournal", StringComparison.OrdinalIgnoreCase))
                return ReportDataSetService.InvoiceJournalDataSetCode;

            if (string.Equals(reportType, "VatInvoiceSt1", StringComparison.OrdinalIgnoreCase))
                return ReportDataSetService.VatInvoiceSt1DataSetCode;

            if (string.Equals(reportType, "VatInvoiceVatKg", StringComparison.OrdinalIgnoreCase))
                return ReportDataSetService.VatInvoiceVatKgDataSetCode;

            if (IsReconciliationType(reportType))
                return SystemReportCalculators.ReconciliationActCode;

            // Ведомость по счёту и прочие табличные отчёты — движения по проводкам.
            return SystemReportCalculators.MovementsCode;
        }

        /// <summary>
        /// Строит оборотно-сальдовую ведомость. При <paramref name="closedXml"/> отчёт
        /// помечается как ClosedXML-макет: FRX-вариант не применяется, макет берётся
        /// из отчёта «Оборотная ведомость (ClosedXML)» в базе.
        /// </summary>
        private async Task<(DataTable, Report)> BuildTrialBalanceAsync(
            DateTime start,
            DateTime end,
            bool closedXml = false)
        {
            if (string.IsNullOrWhiteSpace(_trialBalanceAccountCode))
                throw new ReportNeedsAccountException(
                    "Для оборотно-сальдовой ведомости нужен счёт. Нажмите 🔍 рядом с полем «Счёт», выберите счёт и повторите формирование.");

            var variant = closedXml ? null : GetSelectedTrialBalanceReportVariant();
            var selectedOrganization = GetSelectedOrganizationScope();
            var side = GetSelectedTrialBalanceSide();

            // Построение таблицы выполняет общий сервис: тот же код используется
            // диагностическим инструментом, поэтому поведение можно проверить
            // без запуска окна.
            var builder = new TrialBalanceBuilder(_context);
            var request = new TrialBalanceRequest(
                _trialBalanceAccountCode,
                start,
                end,
                side,
                ToOrganizationScope(selectedOrganization));
            var balance = await builder.BuildTableAsync(request);
            var table = balance.Table;

            var sideTitle = side == TrialBalanceSide.Credit ? "кредиту" : "дебету";
            var organizationSuffix = selectedOrganization == null
                ? string.Empty
                : $", организация: {ResolveOrganizationTitle(selectedOrganization.Id, selectedOrganization.Name)}";
            var titlePrefix = closedXml ? "Оборотная ведомость (ClosedXML)" : "Оборотно-сальдовая ведомость";
            var reportTitle = $"{titlePrefix} по {sideTitle} счета {_trialBalanceAccountCode} за {start:dd/MM/yyyy} - {end:dd/MM/yyyy}{organizationSuffix}";
            var report = CreateReport(table, reportTitle, true);
            report.ReportType = "TrialBalance";
            report.SubtitleText = variant?.IsStandard == false
                ? $"Вид ОСВ: {variant.DisplayName}"
                : $"Счет {_trialBalanceAccountCode}, сторона отбора: {sideTitle}";
            if (variant?.IsStandard == false)
                report.FooterText = $"Выбран FRX-макет ОСВ: {variant.DisplayName}.";

            if (closedXml)
            {
                // Макет берётся из отчёта «Оборотная ведомость (ClosedXML)» в базе.
                report.SourceFormat = "ClosedXml";
                report.IsPrintForm = false;
                report.ClosedXmlLayout = await LoadClosedXmlTrialBalanceLayoutAsync();
            }
            else
            {
                await ApplyTrialBalanceVariantLayoutAsync(report, variant);
            }

            return (table, report);
        }

        /// <summary>Переводит выбранную организацию в ограничение для сервиса ведомости.</summary>
        private static TrialBalanceOrganizationScope? ToOrganizationScope(OrganizationSelectionItem? organization)
        {
            if (organization == null)
                return null;

            return new TrialBalanceOrganizationScope(
                organization.Id,
                organization.Name,
                organization.DisplayName,
                organization.FullName,
                organization.Code);
        }

        private OrganizationSelectionItem? GetSelectedOrganizationScope()
        {
            if (!_selectedOrganizationId.HasValue || _selectedOrganizationId.Value == Guid.Empty)
                return null;

            return _organizationSelectionItems.FirstOrDefault(item => item.Id == _selectedOrganizationId.Value)
                ?? new OrganizationSelectionItem
                {
                    Id = _selectedOrganizationId,
                    Name = _selectedOrganizationName ?? string.Empty
                };
        }

        /// <summary>
        /// Сторона счёта для оборотной ведомости.
        ///
        /// Выбор стороны убран из интерфейса: ведомость показывает и дебет,
        /// и кредит по выбранному счёту. Значение оставлено для совместимости
        /// с программной оборотно-сальдовой ведомостью.
        /// </summary>
        private static TrialBalanceSide GetSelectedTrialBalanceSide() => TrialBalanceSide.Debit;

        /// <summary>Строки плана счетов — для выбора счёта в интерфейсе.</summary>
        private async Task<List<Dictionary<string, object>>> LoadTrialBalanceAccountSelectionDataAsync() =>
            await new TrialBalanceBuilder(_context).LoadAccountSelectionDataAsync();

        private async Task<(DataTable, Report)> BuildPaymentOrderRegisterAsync(DateTime start, DateTime end)
        {
            var rows = await LoadPaymentOrderReportRowsAsync(start, end);
            var table = new DataTable("Реестр платежных поручений");
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Тип", typeof(string));
            table.Columns.Add("Наш счет", typeof(string));
            table.Columns.Add("Корр. счет", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));
            table.Columns.Add("Валюта", typeof(string));
            table.Columns.Add("Сумма в валюте", typeof(decimal));
            table.Columns.Add("Курс", typeof(decimal));
            table.Columns.Add("Классификация платежа", typeof(string));
            table.Columns.Add("Назначение платежа", typeof(string));
            table.Columns.Add("Проведен", typeof(string));
            table.Columns.Add("Примечание", typeof(string));

            foreach (var row in rows)
            {
                table.Rows.Add(row.Date, row.Number, row.OrderType, row.OurAccount, row.CorrespondentAccount,
                    row.Organization, row.Amount, row.Currency, row.AmountCurrency, row.ExchangeRate,
                    row.PaymentClassification, row.Purpose, LocalizationService.DisplayValue(row.IsPosted), row.Note);
            }

            table.Rows.Add(DBNull.Value, "ИТОГО", string.Empty, string.Empty, string.Empty, string.Empty,
                rows.Sum(row => row.Amount), string.Empty, rows.Sum(row => row.AmountCurrency),
                0m, string.Empty, string.Empty, string.Empty, string.Empty);

            var report = CreateReport(table,
                $"Реестр платежных поручений за {start:dd/MM/yyyy} - {end:dd/MM/yyyy}", true);
            report.ShowGrandTotal = true;
            report.SummaryText = "Отчет строится по документам платежных поручений и показывает только документы выбранного периода.";
            return (table, report);
        }

        /// <summary>Код стандартного отчёта, в котором хранится визуальный макет акта сверки.</summary>
        private const string ClosedXmlReconciliationActCode = "standard.finance.reconciliation-act-closedxml";

        /// <summary>
        /// Код стандартного отчёта, в котором хранится визуальный макет
        /// оборотно-сальдовой ведомости.
        /// </summary>
        /// <summary>
        /// Отчёт требует выбора счёта: это не ошибка, а недостающий шаг пользователя.
        /// </summary>
        private sealed class ReportNeedsAccountException : Exception
        {
            public ReportNeedsAccountException(string message) : base(message) { }
        }

        private const string ClosedXmlTrialBalanceCode = "standard.finance.trial-balance-closedxml";

        /// <summary>
        /// Читает макет акта сверки из одноимённого отчёта в базе. Источник макета — сам отчёт,
        /// поэтому правки, сделанные в генераторе отчётов, сразу влияют на вывод.
        /// </summary>
        private async Task<string> LoadClosedXmlActLayoutAsync()
        {
            var stored = await _context.Reports.AsNoTracking()
                .Where(report => report.Code == ClosedXmlReconciliationActCode)
                .Select(report => report.ClosedXmlLayout)
                .FirstOrDefaultAsync();

            return stored ?? string.Empty;
        }

        /// <summary>
        /// Читает макет оборотно-сальдовой ведомости из одноимённого отчёта в базе.
        /// </summary>
        private async Task<string> LoadClosedXmlTrialBalanceLayoutAsync()
        {
            var stored = await _context.Reports.AsNoTracking()
                .Where(report => report.Code == ClosedXmlTrialBalanceCode)
                .Select(report => report.ClosedXmlLayout)
                .FirstOrDefaultAsync();

            return stored ?? string.Empty;
        }

        /// <summary>
        /// Строит акт сверки. При <paramref name="closedXml"/> отчёт помечается как
        /// ClosedXML-макет: FRX-вариант не применяется, макет берётся из базы.
        /// </summary>
        private async Task<(DataTable, Report)> BuildOrganizationReconciliationAsync(
            DateTime start,
            DateTime end,
            bool closedXml = false)
        {
            var variant = closedXml ? null : GetSelectedReconciliationReportVariant();
            var selectedOrganizationId = _selectedOrganizationId.GetValueOrDefault();
            var scoped = selectedOrganizationId != Guid.Empty;
            Guid? selectedFilterId = scoped ? selectedOrganizationId : null;
            var selected = scoped ? _organizationSelectionItems.FirstOrDefault(org => org.Id == selectedOrganizationId) : null;
            var filterName = !string.IsNullOrWhiteSpace(selected?.Name) ? selected!.Name : NormalizeOrganizationName(_selectedOrganizationName);
            var titleName = scoped ? ResolveOrganizationTitle(selectedFilterId, filterName) : "Все организации";

            var calc = await _organizationBalanceService.CalculateAsync(start, end);
            var pairs = calc.Rows
                .Where(row => !row.IsOrganizationTotal && ReconciliationActBuilder.MatchesSelectedOrganization(row, selectedFilterId, filterName))
                .OrderBy(row => row.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CounterAccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totals = calc.Rows
                .Where(row => row.IsOrganizationTotal && ReconciliationActBuilder.MatchesSelectedOrganization(row, selectedFilterId, filterName))
                .ToList();

            // Построение таблицы выполняется общим сервисом: тот же код используется
            // диагностическим инструментом, поэтому поведение можно проверить без окна.
            var builder = new ReconciliationActBuilder(_context);
            var movements = await builder.LoadMovementsAsync(selectedFilterId, start, end);
            var parties = await builder.LoadPartiesAsync(scoped ? selectedFilterId : null, scoped ? titleName : null);
            var act = builder.BuildTable(pairs, totals, movements, start, end, scoped, titleName, parties);
            var table = act.Table;

            WriteReconciliationTrace(start, end, scoped, titleName, filterName, selectedFilterId,
                calc, pairs, totals, movements, act, variant);

            var summary = act.Summary;

            var titlePrefix = closedXml
                ? "Акт сверки (ClosedXML)"
                : variant?.IsStandard == false ? variant.DisplayName : "Акт сверки";
            var report = CreateReport(table, scoped ? $"{titlePrefix}: {titleName} на {end:dd/MM/yyyy}" : $"{titlePrefix}: все организации на {end:dd/MM/yyyy}", true);
            report.ReportType = "ReconciliationAct";
            report.SubtitleText = $"Период: {start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
            report.ShowGrandTotal = false;
            report.AlternateRowColors = false;
            report.FontSize = 8;
            report.SummaryText = summary;
            report.FooterSignature = "Руководитель ____________________    Главный бухгалтер ____________________";
            if (calc.Warnings.Count > 0)
                report.FooterText = string.Join(Environment.NewLine, calc.Warnings);
            if (variant?.IsStandard == false)
            {
                var variantText = $"Выбран вариант печатной формы из метаданных: {variant.DisplayName}.";
                report.FooterText = string.IsNullOrWhiteSpace(report.FooterText) ? variantText : report.FooterText + Environment.NewLine + variantText;
            }

            SetReportFieldWidth(report, "Наименование материала, вид операции", 280);
            SetReportFieldWidth(report, "Дебет", 75);
            SetReportFieldWidth(report, "Кредит", 75);
            SetReportFieldWidth(report, "Сумма Дт", 90, "Right", "N2");
            SetReportFieldWidth(report, "Сумма Кт", 90, "Right", "N2");
            SetReportFieldWidth(report, "Сумма вал.", 90, "Right", "N2");
            SetReportFieldWidth(report, "N докум", 70);
            SetReportFieldWidth(report, "Дата", 70);
            SetReportFieldWidth(report, "Модуль", 45);

            if (closedXml)
            {
                // Макет берётся из отчёта «Акт сверки (ClosedXML)» в базе — код его не подставляет.
                report.SourceFormat = "ClosedXml";
                report.IsPrintForm = false;
                report.ClosedXmlLayout = await LoadClosedXmlActLayoutAsync();
            }
            else
            {
                await ApplyReconciliationVariantLayoutAsync(report, variant);
            }

            return (table, report);
        }

        /// <summary>
        /// Записывает диагностический след построения акта сверки в файл журнала.
        /// Нужен, чтобы видеть, что именно попало в отчёт, не добавляя служебные
        /// строки в саму печатную форму.
        /// </summary>
        private static void WriteReconciliationTrace(
            DateTime start,
            DateTime end,
            bool scoped,
            string titleName,
            string? filterName,
            Guid? selectedFilterId,
            OrganizationBalanceCalculationResult calc,
            IReadOnlyList<OrganizationBalanceRow> pairs,
            IReadOnlyList<OrganizationBalanceRow> totals,
            IReadOnlyList<ReconciliationMovementRow> movements,
            ReconciliationActTable act,
            ReconciliationReportVariant? variant)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reconciliation-trace.log");
                var lines = new List<string>
                {
                    new string('=', 100),
                    $"Время: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
                    $"Версия программы: {AppVersion.Display}",
                    $"Период: {start:dd.MM.yyyy} - {end:dd.MM.yyyy}",
                    $"Режим: {(scoped ? "одна организация" : "все организации")}",
                    $"Выбранная организация: Id={selectedFilterId?.ToString() ?? "<нет>"} Name='{filterName ?? "<нет>"}'",
                    $"Заголовок отчёта: '{titleName}'",
                    $"Вариант макета: {variant?.DisplayName ?? "<не выбран>"}",
                    $"Строк расчёта всего: {calc.Rows.Count}, из них пар {calc.Rows.Count(r => !r.IsOrganizationTotal)}, " +
                    $"итогов {calc.Rows.Count(r => r.IsOrganizationTotal)}",
                    $"Пар передано в построение: {pairs.Count}",
                    $"Итогов передано в построение: {totals.Count}",
                    $"Движений загружено: {movements.Count}",
                    $"Организаций в движениях: {movements.Select(m => m.OrganizationId).Distinct().Count()}",
                    $"Пустых дат: {movements.Count(m => m.Date == default)}, " +
                    $"пустых номеров: {movements.Count(m => string.IsNullOrWhiteSpace(m.DocumentNumber))}, " +
                    $"пустых модулей: {movements.Count(m => string.IsNullOrWhiteSpace(m.ModuleCode))}",
                    string.Empty,
                    "--- Движения по организациям ---"
                };

                foreach (var group in movements.GroupBy(m => new { m.OrganizationId, m.OrganizationName }))
                {
                    lines.Add($"  Id={group.Key.OrganizationId?.ToString() ?? "<NULL>"} " +
                              $"Name='{group.Key.OrganizationName}' движений={group.Count()}");
                }

                lines.Add(string.Empty);
                lines.Add("--- Пары по организациям ---");
                foreach (var group in pairs.GroupBy(p => new { p.OrganizationId, p.OrganizationName }))
                {
                    lines.Add($"  Id={group.Key.OrganizationId?.ToString() ?? "<NULL>"} " +
                              $"Name='{group.Key.OrganizationName}' пар={group.Count()}");
                }

                lines.Add(string.Empty);
                lines.Add("--- След построения ---");
                foreach (var line in act.Trace)
                    lines.Add($"  {line}");

                lines.Add(string.Empty);
                lines.Add($"--- Таблица акта: строк {act.Table.Rows.Count} ---");
                foreach (DataRow row in act.Table.Rows)
                {
                    var operation = row["Наименование материала, вид операции"]?.ToString() ?? string.Empty;
                    var debit = row["Дебет"]?.ToString() ?? string.Empty;
                    var credit = row["Кредит"]?.ToString() ?? string.Empty;
                    var debitAmount = row["Сумма Дт"] == DBNull.Value ? string.Empty : Convert.ToDecimal(row["Сумма Дт"]).ToString("N2");
                    var creditAmount = row["Сумма Кт"] == DBNull.Value ? string.Empty : Convert.ToDecimal(row["Сумма Кт"]).ToString("N2");
                    var document = row["N докум"]?.ToString() ?? string.Empty;
                    var date = row["Дата"]?.ToString() ?? string.Empty;
                    var module = row["Модуль"]?.ToString() ?? string.Empty;

                    lines.Add($"  [{operation}] Дт={debit} Кт={credit} " +
                              $"СуммаДт={debitAmount} СуммаКт={creditAmount} " +
                              $"N={document} Дата={date} Модуль={module}");
                }

                lines.Add(string.Empty);
                File.AppendAllLines(path, lines);
            }
            catch
            {
                // Диагностика не должна мешать построению отчёта.
            }
        }
        private async Task ApplyReconciliationVariantLayoutAsync(Report report, ReconciliationReportVariant? variant)
        {
            if (variant?.ReportId == null)
                return;

            var templateReport = await _reportService.GetReportAsync(variant.ReportId.Value);
            if (templateReport == null || string.IsNullOrWhiteSpace(templateReport.Template))
                return;

            report.SourceFormat = templateReport.SourceFormat;
            report.Template = templateReport.Template;
            report.TemplateVersion = templateReport.TemplateVersion;
            report.PageOrientation = templateReport.PageOrientation;
            report.PageWidth = templateReport.PageWidth;
            report.PageHeight = templateReport.PageHeight;
            report.LeftMargin = templateReport.LeftMargin;
            report.RightMargin = templateReport.RightMargin;
            report.TopMargin = templateReport.TopMargin;
            report.BottomMargin = templateReport.BottomMargin;
            report.FontName = templateReport.FontName;
            report.ShowHeader = templateReport.ShowHeader;
            report.ShowFooter = templateReport.ShowFooter;
            report.ShowPageNumbers = templateReport.ShowPageNumbers;
            report.ShowGridLines = templateReport.ShowGridLines;
            report.AlternateRowColors = templateReport.AlternateRowColors;
            report.AlternateRowColor = templateReport.AlternateRowColor;
            report.HeaderColor = templateReport.HeaderColor;
            report.ElementMappings = templateReport.ElementMappings.Select(mapping => new ReportElementMapping
            {
                ReportId = report.Id,
                ElementOrder = mapping.ElementOrder,
                ElementType = mapping.ElementType,
                ElementText = mapping.ElementText,
                ElementExpression = mapping.ElementExpression,
                BandType = mapping.BandType,
                Left = mapping.Left,
                Top = mapping.Top,
                Width = mapping.Width,
                Height = mapping.Height,
                FontName = mapping.FontName,
                FontSize = mapping.FontSize,
                Bold = mapping.Bold,
                Italic = mapping.Italic,
                Alignment = mapping.Alignment,
                Order = mapping.Order,
                MappedFieldName = mapping.MappedFieldName,
                MappedDisplayName = mapping.MappedDisplayName,
                DataSource = mapping.DataSource,
                FormatString = mapping.FormatString,
                IsVisible = mapping.IsVisible,
                CustomText = mapping.CustomText
            }).ToList();
        }
        private Task ApplyTrialBalanceVariantLayoutAsync(Report report, TrialBalanceReportVariant? variant)
        {
            if (variant?.ReportId == null)
                return Task.CompletedTask;

            return ApplyReconciliationVariantLayoutAsync(report, new ReconciliationReportVariant
            {
                ReportId = variant.ReportId,
                DisplayName = variant.DisplayName,
                Description = variant.Description,
                Code = variant.Code,
                IsStandard = variant.IsStandard
            });
        }

        /// <summary>
        /// Виды ОСВ убраны из интерфейса: оборотная ведомость строится
        /// встроенным макетом. Список оставлен из одного расчётного варианта —
        /// его читает построение отчёта.
        /// </summary>
        private Task LoadTrialBalanceReportVariantsAsync()
        {
            _trialBalanceVariantsLoaded = true;
            _trialBalanceVariants = new List<TrialBalanceReportVariant>
            {
                new()
                {
                    DisplayName = "Программная ОСВ",
                    Description = "Расчетный вариант BIS ERP.",
                    IsStandard = true
                }
            };

            return Task.CompletedTask;
        }

        /// <summary>Выбранный вид ОСВ: он всегда расчётный.</summary>
        private TrialBalanceReportVariant? GetSelectedTrialBalanceReportVariant() =>
            _trialBalanceVariants?.FirstOrDefault();

        private async Task LoadReconciliationReportVariantsAsync()
        {
            if (_reconciliationVariantsLoaded)
                return;

            _reconciliationVariantsLoaded = true;
            var variants = new List<ReconciliationReportVariant>
            {
                new()
                {
                    DisplayName = "Акт сверки",
                    Description = "Стандартный расчетный вариант BIS ERP.",
                    IsStandard = true
                }
            };

            // Варианты расчёта по макетам FoxPro FRX удалены вместе с подсистемой:
            // остаётся только стандартный расчётный вариант (вывод через ClosedXML).
            ReconciliationVariantCombo.ItemsSource = variants;
            ReconciliationVariantCombo.SelectedIndex = 0;
            ReconciliationVariantCombo.IsEnabled = false;

        }

        private void OnReportTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReconciliationVariantVisibility();
        }

        /// <summary>
        /// Показывает панели, нужные выбранному отчёту.
        ///
        /// Вид отчёта определяется его типом из базы: тип <c>ReconciliationAct</c> —
        /// акт сверки, <c>TurnoverByAccount</c> — ведомость по счёту.
        /// </summary>
        private void UpdateReconciliationVariantVisibility()
        {
            if (ReportTypeCombo == null)
                return;

            var selected = SelectedAccountingReport;
            var isReconciliation = selected != null && IsReconciliationType(selected.ReportType);
            var isTurnover = selected != null && IsTurnoverType(selected.ReportType);
            if (TurnoverPairPanel != null)
                TurnoverPairPanel.Visibility = selected?.ReportType == AccountPairTurnoverBuilder.ReportType
                    ? Visibility.Visible : Visibility.Collapsed;

            if (ClosedXmlReconciliationPanel != null)
            {
                ClosedXmlReconciliationPanel.Visibility = isReconciliation
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (TrialBalanceClosedXmlPanel != null)
            {
                TrialBalanceClosedXmlPanel.Visibility = isTurnover
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (TrialBalanceFilterPanel != null)
            {
                // Счёт нужен ведомости по счёту.
                TrialBalanceFilterPanel.Visibility = isTurnover
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>Тип отчёта — акт сверки.</summary>
        private static bool IsReconciliationType(string? reportType) =>
            !string.IsNullOrWhiteSpace(reportType) &&
            reportType.StartsWith("ReconciliationAct", StringComparison.OrdinalIgnoreCase);

        /// <summary>Тип отчёта — ведомость по счёту.</summary>
        private static bool IsTurnoverType(string? reportType) =>
            string.Equals(reportType, "TurnoverByAccount", StringComparison.OrdinalIgnoreCase);

        private ReconciliationReportVariant? GetSelectedReconciliationReportVariant() =>
            ReconciliationVariantCombo?.SelectedItem as ReconciliationReportVariant;

        private static string BuildTrialBalanceVariantDisplayName(string name, string sourceFormat, string reportType)
        {
            var cleanName = CleanTrialBalanceVariantName(name);
            var sourceLabel = GetReconciliationVariantSourceLabel(sourceFormat, reportType);
            return string.IsNullOrWhiteSpace(sourceLabel) ? cleanName : $"{cleanName} [{sourceLabel}]";
        }

        private static string BuildReconciliationVariantDisplayName(string name, string sourceFormat, string reportType)
        {
            var cleanName = CleanReconciliationVariantName(name);
            var sourceLabel = GetReconciliationVariantSourceLabel(sourceFormat, reportType);
            return string.IsNullOrWhiteSpace(sourceLabel) ? cleanName : $"{cleanName} [{sourceLabel}]";
        }

        private static string GetReconciliationVariantSourceLabel(string? sourceFormat, string? reportType)
        {
            if (string.Equals(sourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase))
                return "FRX";
            if (string.Equals(sourceFormat, "Native", StringComparison.OrdinalIgnoreCase))
                return "Нативный";
            return string.Empty;
        }

        private static string CleanTrialBalanceVariantName(string name) =>
            (name ?? string.Empty)
                .Replace("Оборотно-сальдовая ведомость", "ОСВ", StringComparison.OrdinalIgnoreCase)
                .Replace(" (FRX FoxPro)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" (FoxPro report template)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

        private static string CleanReconciliationVariantName(string name) =>
            (name ?? string.Empty)
                .Replace(" (FRX FoxPro)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" (FoxPro report template)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        private async Task<List<PaymentOrderReportRow>> LoadPaymentOrderReportRowsAsync(DateTime? start, DateTime end)
        {
            var document = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Платежное поручение");
            if (document == null)
                return new List<PaymentOrderReportRow>();

            var rows = await _metadataService.GetCatalogDataAsync(document.Id);
            var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(document, _metadataService);
            var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            var result = new List<PaymentOrderReportRow>();

            foreach (var row in rows)
            {
                var date = ReadReportDate(row, "Дата", "doc_date");
                if (!date.HasValue || date.Value.Date > end.Date)
                    continue;
                if (start.HasValue && date.Value.Date < start.Value.Date)
                    continue;

                var correspondentRaw = ReadReportString(row, "Корр. счет", "correspondent_account", "Корр счет", "Коррсчет");
                var correspondentAccount = ResolveReportReference(row, referenceMaps, "Корр. счет", "correspondent_account", "Корр счет", "Коррсчет");
                var analyticAccount = accountAnalytics.FindAccount(correspondentRaw);
                if (analyticAccount != null)
                    correspondentAccount = analyticAccount.DisplayName;

                result.Add(new PaymentOrderReportRow
                {
                    Date = date.Value,
                    Number = ReadReportString(row, "Номер", "doc_number"),
                    OrderType = ReadReportString(row, "Тип", "order_type"),
                    OurAccount = ResolveReportReference(row, referenceMaps, "Наш счет", "our_account_id"),
                    CorrespondentAccount = correspondentAccount,
                    Organization = ResolveReportReference(row, referenceMaps, "Организация", "organization_id"),
                    Amount = ReadReportDecimal(row, "Сумма", "amount"),
                    Currency = ResolveReportReference(row, referenceMaps, "Валюта", "currency_id"),
                    AmountCurrency = ReadReportDecimal(row, "Сумма в валюте", "amount_currency"),
                    ExchangeRate = ReadReportDecimal(row, "Курс", "exchange_rate"),
                    PaymentClassification = ResolveReportReference(row, referenceMaps, "Классификация платежа", "payment_classification_id"),
                    Purpose = ReadReportString(row, "Назначение платежа", "purpose"),
                    Note = ReadReportString(row, "Примечание", "description"),
                    IsPosted = ReadReportBool(row, "Проведён", "Проведен", "is_posted")
                });
            }

            return result
                .OrderBy(row => row.Date)
                .ThenBy(row => row.Number, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string ResolveReportReference(
            Dictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;

                var text = value.ToString() ?? string.Empty;
                if (Guid.TryParse(text, out var id))
                {
                    foreach (var mapKey in keys)
                    {
                        if (referenceMaps.TryGetValue(mapKey, out var map) && map.TryGetValue(id, out var display))
                            return display;
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        private static string ReadReportString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (TryGetReportValue(row, out var value, key) && value != null && value != DBNull.Value)
                    return value.ToString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static DateTime? ReadReportDate(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is DateTime date)
                    return date;
                if (value is DateTimeOffset dateTimeOffset)
                    return dateTimeOffset.LocalDateTime;
                if (DateTime.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static decimal ReadReportDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is decimal decimalValue)
                    return decimalValue;
                if (value is double doubleValue)
                    return Convert.ToDecimal(doubleValue);
                if (value is int intValue)
                    return Convert.ToDecimal(intValue);
                if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                    decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return 0m;
        }

        private static bool ReadReportBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is bool boolValue)
                    return boolValue;
                if (bool.TryParse(value.ToString(), out var parsed))
                    return parsed;
                if (int.TryParse(value.ToString(), out var intValue))
                    return intValue != 0;
            }

            return false;
        }

        private static bool TryGetReportValue(Dictionary<string, object> row, out object? value, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out value))
                    return true;

                var match = row.FirstOrDefault(pair =>
                    pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (!match.Equals(default(KeyValuePair<string, object>)))
                {
                    value = match.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        // ==================================================================================
        // УСТАРЕВШИЕ МЕТОДЫ ПОСТРОЕНИЯ АКТА СВЕРКИ.
        //
        // Логика построения таблицы акта перенесена в Services/ReconciliationActBuilder.cs,
        // чтобы её можно было проверить диагностическим инструментом (.tools/ReconProbe)
        // без запуска окна. Методы ниже не вызываются: они оставлены только на время
        // проверки переноса и подлежат удалению.
        //
        // При правке поведения акта сверки менять нужно ReconciliationActBuilder,
        // а не эти методы.
        // ==================================================================================

        /// <summary>
        /// Колонки, которые нужны только механизму подстановки макета.
        /// В экранной таблице отчёта они помечаются невидимыми: сетка строится
        /// по полям отчёта, а не по колонкам таблицы данных.
        ///
        /// Список объединяет служебные имена акта сверки, оборотно-сальдовой
        /// ведомости и оборотной ведомости по счёту.
        /// </summary>
        private static readonly HashSet<string> ServiceColumnNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Акт сверки.
            "operation_name", "debit_account", "credit_account",
            "debit_amount", "credit_amount", "debit_begin", "credit_begin",
            "document_number", "document_date", "module",
            "report_title", "report_summary", "period_start", "period_end", "period_text",
            // Стороны акта сверки: подписи читает макет, в сетке они лишние.
            "our_organization", "our_director", "our_accountant",
            "counterparty", "counterparty_director", "counterparty_accountant",
            // Оборотно-сальдовая ведомость.
            "account_code", "account_name",
            "opening_debit", "opening_credit", "turnover_debit", "turnover_credit",
            "closing_debit", "closing_credit",
            // Оборотная ведомость по счёту.
            "posting_date", "doc_number", "counter_account_code", "description",
            "module_code", "report_org", "report_period",
            "opening_balance", "closing_balance",
            "currency_code", "amount_currency", "exchange_rate",
            "organization_id", "organization_code", "organization_name",
            "document_type", "amount_kgs", "row_kind"
        };

        /// <summary>
        /// Проставляет поля шапки макета во всех строках: заголовок отчёта, период
        /// и итоговое описание. Их читает полоса Title.
        /// </summary>

        /// <summary>Добавляет колонку под каноническое имя поля отчёта, если её ещё нет.</summary>

        /// <summary>
        /// Записывает значение в служебную колонку. В числовую колонку нельзя записать
        /// пустую строку — только DBNull.
        /// </summary>

        private static bool IsNumericColumn(DataColumn column) =>
            column.DataType == typeof(decimal) ||
            column.DataType == typeof(double) ||
            column.DataType == typeof(float) ||
            column.DataType == typeof(int) ||
            column.DataType == typeof(long) ||
            column.DataType == typeof(short);

        private static void SetReportFieldWidth(Report report, string fieldName, int width, string alignment = "Left", string format = "")
        {
            var field = report.Fields.FirstOrDefault(item => item.FieldName == fieldName);
            if (field == null)
                return;
            field.Width = width;
            field.Alignment = alignment;
            field.Format = format;
        }
        private string ResolveOrganizationTitle(Guid? organizationId, string? organizationName)
        {
            var organization = organizationId.HasValue && organizationId.Value != Guid.Empty
                ? _organizationSelectionItems.FirstOrDefault(item => item.Id == organizationId.Value)
                : null;
            if (organization != null)
                return string.IsNullOrWhiteSpace(organization.FullName) ? organization.Name : organization.FullName;

            var normalized = NormalizeOrganizationName(organizationName);
            return string.IsNullOrWhiteSpace(normalized) ? "Без организации" : normalized;
        }

        private static bool SameOrganization(OrganizationBalanceRow row, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return row.OrganizationId.HasValue && row.OrganizationId.Value == organizationId.Value;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return rowName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool MatchesMovementOrganization(ReconciliationMovementRow movement, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return movement.OrganizationId.HasValue && movement.OrganizationId.Value == organizationId.Value;

            var movementName = NormalizeOrganizationName(movement.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return movementName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool MatchesSelectedOrganization(OrganizationBalanceRow row, Guid? selectedId, string? selectedName)
        {
            if (!selectedId.HasValue || selectedId.Value == Guid.Empty)
                return true;
            if (row.OrganizationId.HasValue && row.OrganizationId.Value == selectedId.Value)
                return true;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(selectedName);
            return !string.IsNullOrWhiteSpace(filterName) &&
                   rowName.Equals(filterName, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string NormalizeOrganizationName(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex >= 0 ? text.Substring(separatorIndex + 3).Trim() : text;
        }

        private static bool AccountInPair(string accountCode, OrganizationBalanceRow pair)
        {
            return SameAccount(accountCode, pair.AccountCode) || SameAccount(accountCode, pair.CounterAccountCode);
        }

        private static bool SameAccount(string left, string right) =>
            left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

        private static decimal? NonZeroAmount(decimal value) => value == 0m ? null : value;

        /// <summary>
        /// Сокращает длинное описание операции: убирает перечисление модулей
        /// («Авансовые платежи; Расчеты по прочим услугам») и ограничивает длину,
        /// иначе текст не помещается в колонку и наезжает на соседние.
        /// </summary>

        /// <summary>
        /// Заголовок документа не должен попадать в колонку операций: он уже выведен
        /// в шапке, иначе «АКТ СВЕРКИ» печатается дважды.
        /// </summary>

        private static string BuildDebtSummary(string organizationName, decimal balance)
        {
            if (balance > 0m)
                return $"Задолженность \"{organizationName}\" перед нами составляет {balance:N2}";
            if (balance < 0m)
                return $"Наша задолженность перед \"{organizationName}\" составляет {Math.Abs(balance):N2}";
            return $"Задолженность с \"{organizationName}\" отсутствует.";
        }

        private static string BuildAllOrganizationsDebtSummary(int organizationCount, decimal balance)
        {
            if (balance > 0m)
                return $"Сформировано актов по организациям: {organizationCount}. Итоговая задолженность организаций перед нами составляет {balance:N2}";
            if (balance < 0m)
                return $"Сформировано актов по организациям: {organizationCount}. Итоговая наша задолженность перед организациями составляет {Math.Abs(balance):N2}";
            return $"Сформировано актов по организациям: {organizationCount}. Итоговая задолженность отсутствует.";
        }

        /// <summary>
        /// Признак наличия координатного макета печатной формы у отчёта.
        /// </summary>
        private static bool HasFoxProTemplate(Report report) =>
            !string.IsNullOrWhiteSpace(report.Template) &&
            (string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase));

        private static bool HasNativeTemplate(Report report) =>
            !string.IsNullOrWhiteSpace(report.Template) &&
            string.Equals(report.SourceFormat, "Native", StringComparison.OrdinalIgnoreCase);

        private bool ShouldUseFoxProExcelLayout(Report report) =>
            HasFoxProTemplate(report) && IsFoxProExcelFormat(GetSelectedReportOutputFormat());

        /// <summary>
        /// Запрошенный формат вывода.
        ///
        /// Формат задают кнопки «Excel» и «PDF»: список форматов убран из интерфейса,
        /// поэтому выбранное значение хранится в поле.
        /// </summary>
        private string _requestedOutputFormat = "ProgramExcel";

        /// <summary>Формат вывода, запрошенный кнопкой.</summary>
        private string GetSelectedReportOutputFormat() => _requestedOutputFormat;

        private static bool IsProgrammaticExcelFormat(string format) =>
            FormatIs(format, "ProgramExcel", "ExcelNative");

        private static bool IsProgrammaticPdfFormat(string format) =>
            FormatIs(format, "ProgramPdf");

        private static bool IsNativeExcelFormat(string format) =>
            FormatIs(format, "NativeExcel");

        private static bool IsNativePdfFormat(string format) =>
            FormatIs(format, "NativePdf");

        /// <summary>Формат вывода по визуальной разметке ClosedXML (вкладка «📊 ClosedXML макет»).</summary>
        private static bool IsClosedXmlExcelFormat(string format) =>
            FormatIs(format, "ClosedXmlExcel", "ExcelClosedXml");

        /// <summary>Формат вывода PDF по визуальной разметке ClosedXML.</summary>
        private static bool IsClosedXmlPdfFormat(string format) =>
            FormatIs(format, "ClosedXmlPdf", "PdfClosedXml");

        private static bool HasClosedXmlLayout(Report report) =>
            report != null && !string.IsNullOrWhiteSpace(report.ClosedXmlLayout);

        private static bool IsFoxProExcelFormat(string format) =>
            FormatIs(format, "FoxProExcel", "ExcelFoxPro");

        private static bool IsFoxProPdfFormat(string format) =>
            FormatIs(format, "FoxProPdf", "PdfFoxPro");

        private static bool IsTaxExcelFormat(string format) =>
            FormatIs(format, "TaxExcel");

        private static bool FormatIs(string format, params string[] expected) =>
            expected.Any(value => string.Equals(format, value, StringComparison.OrdinalIgnoreCase));

        private static bool IsReconciliationActReport(DataTable? dataTable, Report? report)
        {
            if (report == null)
                return false;

            return string.Equals(report.ReportType, "ReconciliationAct", StringComparison.OrdinalIgnoreCase) ||
                   ContainsIgnoreCase(report.Name, "Акт сверки") ||
                   (dataTable != null && ContainsIgnoreCase(dataTable.TableName, "Акт сверки"));
        }

        private static bool ContainsIgnoreCase(string? value, string fragment) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase);

        private static bool TryReadGuid(Dictionary<string, object> row, out Guid id, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is Guid guid)
                {
                    id = guid;
                    return true;
                }
                if (Guid.TryParse(value.ToString(), out id))
                    return true;
            }

            id = Guid.Empty;
            return false;
        }

        private static bool ReadReportBoolValue(object? value, bool defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;
            if (value is bool boolValue)
                return boolValue;
            if (bool.TryParse(value.ToString(), out var parsedBool))
                return parsedBool;
            if (int.TryParse(value.ToString(), out var intValue))
                return intValue != 0;
            var text = value.ToString()?.Trim() ?? string.Empty;
            if (text.Equals("да", StringComparison.OrdinalIgnoreCase) || text.Equals("истина", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.Equals("нет", StringComparison.OrdinalIgnoreCase) || text.Equals("ложь", StringComparison.OrdinalIgnoreCase))
                return false;
            return defaultValue;
        }

        private static decimal ReadDbDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;
            if (value is decimal decimalValue)
                return decimalValue;
            if (value is double doubleValue)
                return Convert.ToDecimal(doubleValue);
            if (value is float floatValue)
                return Convert.ToDecimal(floatValue);
            if (value is int intValue)
                return intValue;
            if (value is long longValue)
                return longValue;
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                   decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out parsed)
                ? parsed
                : 0m;
        }
        private sealed class OrganizationSelectionItem
        {
            public Guid? Id { get; init; }
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public bool IsActive { get; init; } = true;
            public bool IsPrimary { get; init; }
            public bool IsAll => !Id.HasValue || Id.Value == Guid.Empty;
            public string DisplayName
            {
                get
                {
                    if (IsAll)
                        return "Все организации";
                    var name = string.IsNullOrWhiteSpace(Name) ? FullName : Name;
                    var display = string.IsNullOrWhiteSpace(Code) ? name : $"{Code} - {name}";
                    return IsActive ? display : $"{display} (неактивна)";
                }
            }

            public static OrganizationSelectionItem CreateAll() => new()
            {
                Id = Guid.Empty,
                Name = "Все организации"
            };
        }
        private sealed class ReconciliationReportVariant
        {
            public Guid? ReportId { get; init; }
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public bool IsStandard { get; init; }
        }

        private sealed class TrialBalanceReportVariant
        {
            public Guid? ReportId { get; init; }
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public bool IsStandard { get; init; }
        }

        private sealed class PaymentOrderReportRow
        {
            public DateTime Date { get; init; }
            public string Number { get; init; } = string.Empty;
            public string OrderType { get; init; } = string.Empty;
            public string OurAccount { get; init; } = string.Empty;
            public string CorrespondentAccount { get; init; } = string.Empty;
            public string Organization { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public string Currency { get; init; } = string.Empty;
            public decimal AmountCurrency { get; init; }
            public decimal ExchangeRate { get; init; }
            public string PaymentClassification { get; init; } = string.Empty;
            public string Purpose { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
            public bool IsPosted { get; init; }

            public bool IsIncoming =>
                OrderType.Contains("вход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("приход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("поступ", StringComparison.OrdinalIgnoreCase);

            public bool IsOutgoing =>
                OrderType.Contains("исход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("расход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("оплат", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(DataTable, Report)> BuildGeneralLedgerAsync(int year)
        {
            var ledger = await _balanceService.GetGeneralLedgerAsync(year);
            var table = new DataTable("Главная книга");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сальдо нач. Дт", typeof(decimal));
            table.Columns.Add("Сальдо нач. Кт", typeof(decimal));
            for (var month = 1; month <= 12; month++)
            {
                table.Columns.Add($"{month:00} Дт", typeof(decimal));
                table.Columns.Add($"{month:00} Кт", typeof(decimal));
            }
            table.Columns.Add("Год Дт", typeof(decimal));
            table.Columns.Add("Год Кт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Дт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Кт", typeof(decimal));

            foreach (var item in ledger)
            {
                var values = new object[table.Columns.Count];
                values[0] = item.AccountCode;
                values[1] = item.AccountName;
                values[2] = item.OpeningDebit;
                values[3] = item.OpeningCredit;
                for (var month = 1; month <= 12; month++)
                {
                    values[2 + month * 2] = item.MonthlyTurnoverDebit[month];
                    values[3 + month * 2] = item.MonthlyTurnoverCredit[month];
                }
                values[28] = item.YearTurnoverDebit;
                values[29] = item.YearTurnoverCredit;
                values[30] = item.ClosingDebit;
                values[31] = item.ClosingCredit;
                table.Rows.Add(values);
            }

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Главная книга за {year} год", true));
        }

        private async Task<(DataTable, Report)> BuildFinancialPositionAsync(DateTime date)
        {
            var balance = await _balanceService.GetEnterpriseBalanceAsync(date);
            var table = new DataTable("Баланс предприятия");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in balance.Assets)
                table.Rows.Add("Активы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Активы", string.Empty, "Итого активы", balance.TotalAssets);
            foreach (var item in balance.Liabilities)
                table.Rows.Add("Обязательства", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Обязательства", string.Empty, "Итого обязательства", balance.TotalLiabilities);
            foreach (var item in balance.Equity)
                table.Rows.Add("Капитал", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Капитал", string.Empty, "Итого капитал", balance.TotalEquity);
            table.Rows.Add("Контроль", string.Empty, "Расхождение баланса", balance.Difference);

            var report = CreateReport(table, $"Баланс предприятия на {date:dd/MM/yyyy}", false);
            report.SummaryText = balance.IsBalanced
                ? "Контроль пройден: активы равны обязательствам и капиталу."
                : $"Контроль не пройден. Расхождение: {balance.Difference:N2}";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildFinancialResultsAsync(DateTime start, DateTime end)
        {
            var results = await _balanceService.GetFinancialResultsAsync(start, end);
            var table = new DataTable("Финансовые результаты");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in results.Income)
                table.Rows.Add("Доходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Доходы", string.Empty, "Итого доходы", results.TotalIncome);
            foreach (var item in results.Expenses)
                table.Rows.Add("Расходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Расходы", string.Empty, "Итого расходы", results.TotalExpenses);
            table.Rows.Add("Результат", string.Empty,
                results.ProfitOrLoss >= 0 ? "Прибыль" : "Убыток", results.ProfitOrLoss);

            return (table, CreateReport(table,
                $"Финансовые результаты за {start:dd/MM/yyyy} - {end:dd/MM/yyyy}", false));
        }

        private async Task<(DataTable, Report)> BuildPurchaseSalesJournalAsync(DateTime start, DateTime end)
        {
            var entries = await _balanceService.GetPurchaseSalesJournalAsync(start, end);
            var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
            var utcEnd = DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc);
            var savedEntries = await _context.TaxJournalRecords.AsNoTracking()
                .Where(record => record.Date >= utcStart && record.Date < utcEnd)
                .OrderBy(record => record.Date).ThenBy(record => record.DocumentNumber).ToListAsync();
            var table = new DataTable("Журнал закупок и продаж");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Модуль", typeof(string));
            table.Columns.Add("Бланк", typeof(string));
            table.Columns.Add("Ставка налога", typeof(string));
            table.Columns.Add("Сумма без налога", typeof(decimal));
            table.Columns.Add("НДС", typeof(decimal));
            table.Columns.Add("Налог с продаж", typeof(decimal));
            table.Columns.Add("Налог", typeof(decimal));
            table.Columns.Add("Всего", typeof(decimal));
            table.Columns.Add("Проведен", typeof(string));
            table.Columns.Add("Примечание", typeof(string));

            if (savedEntries.Count > 0)
            {
                foreach (var entry in savedEntries)
                    table.Rows.Add(LocalizationService.DisplayValue(entry.JournalType), entry.Date,
                        entry.DocumentNumber, entry.DocumentType, entry.Organization, string.Empty, string.Empty, entry.TaxType,
                        entry.AmountWithoutTax, 0m, 0m, entry.TaxAmount, entry.TotalAmount, LocalizationService.DisplayValue(true), string.Empty);
            }
            else
            {
                foreach (var entry in entries)
                    table.Rows.Add(entry.Section, entry.Date, entry.DocumentNumber, entry.DocumentType,
                        entry.Organization, entry.ModuleCode, entry.TaxBlankNumber, entry.TaxType, entry.AmountWithoutTax,
                        entry.VatAmount, entry.SalesTaxAmount, entry.VatAmount + entry.SalesTaxAmount, entry.Amount,
                        LocalizationService.DisplayValue(entry.IsPosted), entry.Note);
            }

            var report = CreateReport(table,
                $"Журнал закупок и продаж за {start:dd/MM/yyyy} - {end:dd/MM/yyyy}", true);
            report.ShowGrandTotal = true;
            var amountField = report.Fields.First(field => field.FieldName == "Всего");
            amountField.AggregateType = "Sum";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildOrganizationBalancesAsync(DateTime start, DateTime end)
        {
            var calculation = await _organizationBalanceService.CalculateAsync(start, end);
            var table = new DataTable("Сальдо по организациям");
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Вид расчета", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Счет корр.", typeof(string));
            table.Columns.Add("Сальдо нач. Дт", typeof(decimal));
            table.Columns.Add("Сальдо нач. Кт", typeof(decimal));
            table.Columns.Add("Оборот Дт", typeof(decimal));
            table.Columns.Add("Оборот Кт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Дт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Кт", typeof(decimal));
            table.Columns.Add("Сальдо", typeof(decimal));
            table.Columns.Add("Модуль", typeof(string));

            var rowsToAdd = calculation.Rows.AsEnumerable();
            if (_selectedOrganizationId.HasValue && _selectedOrganizationId.Value != Guid.Empty)
                rowsToAdd = rowsToAdd.Where(row => MatchesSelectedOrganization(row, _selectedOrganizationId, _selectedOrganizationName));

            foreach (var row in rowsToAdd)
            {
                table.Rows.Add(
                    row.OrganizationName,
                    row.IsOrganizationTotal ? "ИТОГО" : row.AccountPairName,
                    row.AccountCode,
                    row.CounterAccountCode,
                    row.OpeningDebit,
                    row.OpeningCredit,
                    row.TurnoverDebit,
                    row.TurnoverCredit,
                    row.ClosingDebit,
                    row.ClosingCredit,
                    row.Balance,
                    row.ModuleCode);
            }

            var titleSuffix = _selectedOrganizationId.HasValue
                ? $", фильтр: {_selectedOrganizationName}"
                : string.Empty;
            var report = CreateReport(table,
                $"Сальдо по организациям за {start:dd/MM/yyyy} - {end:dd/MM/yyyy}{titleSuffix}", true);
            report.SummaryText = calculation.Warnings.Count == 0
                ? "Расчет выполнен по активным парам счетов справочника авансовых платежей."
                : string.Join(Environment.NewLine, calculation.Warnings);
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildPeriodCollectionAsync(DateTime start, DateTime end)
        {
            var collection = await _balanceService.CollectPeriodInformationAsync(start, end);
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Сбор информации за периода");
            table.Columns.Add("Показатель", typeof(string));
            table.Columns.Add("Всего", typeof(int));
            table.Columns.Add("Проведено", typeof(int));
            table.Columns.Add("Не проведено", typeof(int));
            table.Columns.Add("Сумма документов", typeof(decimal));
            table.Columns.Add("Дебет", typeof(decimal));
            table.Columns.Add("Кредит", typeof(decimal));
            table.Columns.Add("Разница", typeof(decimal));

            foreach (var item in collection.Documents)
                table.Rows.Add(item.DocumentType, item.DocumentCount, item.PostedCount, item.UnpostedCount, item.Amount, 0m, 0m, 0m);
            var openingDebit = balances.Sum(item => item.OpeningDebit);
            var openingCredit = balances.Sum(item => item.OpeningCredit);
            var closingDebit = balances.Sum(item => item.ClosingDebit);
            var closingCredit = balances.Sum(item => item.ClosingCredit);
            table.Rows.Add("КОНТРОЛЬ НАЧАЛЬНОГО САЛЬДО", 0, 0, 0, 0m,
                openingDebit, openingCredit, openingDebit - openingCredit);
            table.Rows.Add("БУХГАЛТЕРСКИЕ ПРОВОДКИ", collection.PostingCount,
                collection.PostingCount, 0, collection.DebitTurnover,
                collection.DebitTurnover, collection.CreditTurnover, collection.Difference);
            table.Rows.Add("КОНТРОЛЬ КАРТОЧЕК ОС", collection.Warnings.Count,
                0, collection.Warnings.Count, 0m, 0m, 0m, 0m);
            table.Rows.Add("КОНТРОЛЬ КОНЕЧНОГО САЛЬДО", 0, 0, 0, 0m,
                closingDebit, closingCredit, closingDebit - closingCredit);

            var report = CreateReport(table,
                $"Сбор информации за периода {start:dd/MM/yyyy} - {end:dd/MM/yyyy}", false);
            var isBalanced = collection.IsBalanced && Math.Abs(openingDebit - openingCredit) < 0.01m &&
                             Math.Abs(closingDebit - closingCredit) < 0.01m;
            report.SummaryText = isBalanced
                ? "Контроль пройден: начальное сальдо, обороты и конечное сальдо сбалансированы."
                : "Контроль не пройден: обнаружено расхождение дебета и кредита.";
            if (collection.Warnings.Count > 0)
                report.SummaryText += Environment.NewLine + string.Join(Environment.NewLine, collection.Warnings);
            return (table, report);
        }

        private static Report CreateReport(DataTable table, string title, bool landscape)
        {
            var report = new Report
            {
                Name = title,
                TitleText = title,
                PageOrientation = landscape ? "Landscape" : "Portrait",
                FontName = "Arial",
                FontSize = table.Columns.Count > 12 ? 7 : 9,
                ShowGrandTotal = false,
                ShowGridLines = true,
                AlternateRowColors = true
            };

            var order = 1;
            foreach (DataColumn column in table.Columns)
            {
                report.Fields.Add(new ReportField
                {
                    FieldName = column.ColumnName,
                    DisplayName = column.ColumnName,
                    Order = order++,
                    Width = column.DataType == typeof(string) ? 160 : 85,
                    // Служебные колонки нужны только механизму подстановки FRX-макета,
                    // поэтому в экранной таблице отчёта они не показываются.
                    IsVisible = !ServiceColumnNames.Contains(column.ColumnName)
                });
            }
            return report;
        }

        private static void AddTotalsRow(DataTable table, int firstNumericColumn)
        {
            var row = table.NewRow();
            row[0] = "ИТОГО";
            for (var index = firstNumericColumn; index < table.Columns.Count; index++)
                row[index] = table.Rows.Cast<DataRow>().Sum(item => Convert.ToDecimal(item[index]));
            table.Rows.Add(row);
        }

        /// <summary>
        /// Кнопка «Excel»: формирует отчёт и открывает его в Excel.
        ///
        /// Формат вывода выбирается здесь, а не в списке: список форматов убран.
        /// </summary>
        private async void OnExportExcelButtonClick(object sender, RoutedEventArgs e)
        {
            _requestedOutputFormat = ResolveClosedXmlLayoutUsage()
                ? "ClosedXmlExcel"
                : "ProgramExcel";

            await EnsureReportBuiltAsync();
            OnExportExcelClick(sender, e);
        }

        /// <summary>
        /// Кнопка «PDF»: формирует отчёт и показывает его в окне предпросмотра.
        /// </summary>
        private async void OnExportPdfButtonClick(object sender, RoutedEventArgs e)
        {
            _requestedOutputFormat = ResolveClosedXmlLayoutUsage()
                ? "ClosedXmlPdf"
                : "ProgramPdf";

            await EnsureReportBuiltAsync();

            if (string.Equals(_requestedOutputFormat, "ClosedXmlPdf", StringComparison.OrdinalIgnoreCase))
                OnExportClosedXmlPdfClick(sender, e);
            else
                OnExportProgrammaticPdfClick(sender, e);
        }

        /// <summary>
        /// Отчёт выводится по визуальному макету ClosedXML.
        ///
        /// В этом окне все отчёты строятся по макету ClosedXML, поэтому вывод
        /// всегда идёт визуальным макетом.
        /// </summary>
        private bool ResolveClosedXmlLayoutUsage() => SelectedAccountingReport != null;

        /// <summary>
        /// Формирует отчёт, если он ещё не построен или построен за другой период.
        /// Кнопки вывода работают без отдельного нажатия «Сформировать».
        /// </summary>
        private async Task EnsureReportBuiltAsync()
        {
            if (_currentData == null || _currentReport == null)
            {
                OnGenerateClick(this, new RoutedEventArgs());
                return;
            }
        }

        private void OnOpenReportClick(object sender, RoutedEventArgs e)
        {
            var format = GetSelectedReportOutputFormat();
            if (IsTaxExcelFormat(format))
            {
                OnExportVatClick(sender, e);
                return;
            }

            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "Отчет",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsProgrammaticPdfFormat(format))
            {
                OnExportProgrammaticPdfClick(sender, e);
                return;
            }

            if (IsNativePdfFormat(format))
            {
                OnExportNativePdfClick(sender, e);
                return;
            }

            if (IsFoxProPdfFormat(format))
            {
                OnExportFrxPdfClick(sender, e);
                return;
            }

            if (IsClosedXmlPdfFormat(format))
            {
                OnExportClosedXmlPdfClick(sender, e);
                return;
            }

            OnExportExcelClick(sender, e);
        }

        /// <summary>
        /// Вывод отчёта в PDF по визуальной разметке ClosedXML.
        ///
        /// Идёт по той же модели макета, что и вывод в Excel, поэтому документ
        /// совпадает по составу и оформлению. Для акта сверки набор данных
        /// строится тем же сервисом, что и Excel-вывод.
        /// </summary>
        private async void OnExportClosedXmlPdfClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentData == null || _currentReport == null)
                {
                    MessageBox.Show("Сначала сформируйте отчет.", "ClosedXML PDF",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!HasClosedXmlLayout(_currentReport))
                {
                    MessageBox.Show(
                        "Для выбранного отчета не сохранён ClosedXML-макет. Откройте конструктор, вкладку «📊 ClosedXML макет», соберите разметку и сохраните отчет.",
                        "ClosedXML PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                IsEnabled = false;
                StatusText.Text = "Открытие ClosedXML PDF...";

                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                var layout = ClosedXmlReportRenderer.ParseLayout(reportSnapshot.ClosedXmlLayout, out var layoutError);
                if (layout == null)
                    throw new InvalidOperationException(layoutError);

                var renderParameters = await _reportService.ResolveParametersForPreviewAsync(reportSnapshot);
                // Период берётся из окна и передаётся всегда: иначе подстановки {PeriodStart}
                // и {PeriodEnd} в макете берут значения по умолчанию и шапка расходится с данными.
                // Организация необязательна: пусто значит «все организации».
                var (renderPeriodStart, renderPeriodEnd) = GetPeriod();
                renderParameters["PeriodStart"] = renderPeriodStart;
                renderParameters["PeriodEnd"] = renderPeriodEnd;
                renderParameters["OrganizationId"] = _selectedOrganizationId?.ToString() ?? string.Empty;

                var preparedData = ClosedXmlReportRenderer.PrepareData(dataSnapshot, reportSnapshot);
                var typedParameters = renderParameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
                var pdfBytes = await Task.Run(() =>
                    ClosedXmlReportRenderer.RenderToPdfBytes(layout, preparedData, typedParameters));

                var previewWindow = new PdfPreviewWindow(pdfBytes, _currentReport.Name) { Owner = Window.GetWindow(this) };
                previewWindow.ShowDialog();

                StatusText.Text = "✅ ClosedXML PDF открыт";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка вывода ClosedXML PDF";
                MessageBox.Show($"Ошибка вывода ClosedXML PDF: {ex.Message}", "ClosedXML PDF",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static Report CloneReportForBackgroundPreview(Report source)
        {
            var report = new Report
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                DataSourceType = source.DataSourceType,
                DataSourceId = source.DataSourceId,
                ReportType = source.ReportType,
                Template = source.Template,
                ClosedXmlLayout = source.ClosedXmlLayout,
                Settings = source.Settings,
                Icon = source.Icon,
                Code = source.Code,
                IsActive = source.IsActive,
                IsPrintForm = source.IsPrintForm,
                IsDefault = source.IsDefault,
                SourceFormat = source.SourceFormat,
                TemplateVersion = source.TemplateVersion,
                Order = source.Order,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                PageTitle = source.PageTitle,
                PageOrientation = source.PageOrientation,
                PageWidth = source.PageWidth,
                PageHeight = source.PageHeight,
                LeftMargin = source.LeftMargin,
                RightMargin = source.RightMargin,
                TopMargin = source.TopMargin,
                BottomMargin = source.BottomMargin,
                FontName = source.FontName,
                FontSize = source.FontSize,
                ShowHeader = source.ShowHeader,
                ShowFooter = source.ShowFooter,
                ShowPageNumbers = source.ShowPageNumbers,
                ShowGridLines = source.ShowGridLines,
                AlternateRowColor = source.AlternateRowColor,
                HeaderTitle = source.HeaderTitle,
                HeaderSubtitle = source.HeaderSubtitle,
                HeaderLogo = source.HeaderLogo,
                HeaderText = source.HeaderText,
                FooterText = source.FooterText,
                FooterTotalText = source.FooterTotalText,
                FooterSignature = source.FooterSignature,
                TitleText = source.TitleText,
                SubtitleText = source.SubtitleText,
                SummaryText = source.SummaryText,
                AlternateRowColors = source.AlternateRowColors,
                ShowGrandTotal = source.ShowGrandTotal,
                HeaderColor = source.HeaderColor
            };

            report.Fields = source.Fields.Select(field => new ReportField
            {
                Id = field.Id,
                ReportId = field.ReportId,
                FieldName = field.FieldName,
                DisplayName = field.DisplayName,
                AggregateType = field.AggregateType,
                Order = field.Order,
                Width = field.Width,
                Alignment = field.Alignment,
                Format = field.Format,
                IsVisible = field.IsVisible
            }).ToList();

            report.ElementMappings = source.ElementMappings.Select(mapping => new ReportElementMapping
            {
                Id = mapping.Id,
                ReportId = mapping.ReportId,
                ElementOrder = mapping.ElementOrder,
                ElementType = mapping.ElementType,
                ElementText = mapping.ElementText,
                ElementExpression = mapping.ElementExpression,
                BandType = mapping.BandType,
                Left = mapping.Left,
                Top = mapping.Top,
                Width = mapping.Width,
                Height = mapping.Height,
                FontName = mapping.FontName,
                FontSize = mapping.FontSize,
                Bold = mapping.Bold,
                Italic = mapping.Italic,
                Alignment = mapping.Alignment,
                Order = mapping.Order,
                MappedFieldName = mapping.MappedFieldName,
                MappedDisplayName = mapping.MappedDisplayName,
                DataSource = mapping.DataSource,
                FormatString = mapping.FormatString,
                IsVisible = mapping.IsVisible,
                CustomText = mapping.CustomText
            }).ToList();

            // Параметры переносятся обязательно: по ним заполняются подстановки
            // в шапке макета. Без них в шапке печаталось «по счету с по».
            report.Parameters = source.Parameters.Select(parameter => new ReportParameter
            {
                Id = parameter.Id,
                ReportId = parameter.ReportId,
                Name = parameter.Name,
                Title = parameter.Title,
                ParameterType = parameter.ParameterType,
                DefaultValue = parameter.DefaultValue,
                IsRequired = parameter.IsRequired,
                ReferenceSource = parameter.ReferenceSource,
                Order = parameter.Order
            }).ToList();

            return report;
        }

        private async void OnExportProgrammaticPdfClick(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "Программный PDF",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                StatusText.Text = "Открытие программного PDF...";

                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                byte[] pdfBytes;
                if (IsReconciliationActReport(dataSnapshot, reportSnapshot))
                {
                    var printFormService = new PrintFormService(_context);
                    pdfBytes = await Task.Run(() =>
                        printFormService.ExportProgrammaticReconciliationActPreview(dataSnapshot, reportSnapshot));
                }
                else
                {
                    pdfBytes = await Task.Run(() => _reportService.ExportToPdf(dataSnapshot, reportSnapshot));
                }

                ShowPdfPreview(pdfBytes, $"{_currentReport.Name} (программный)");
                StatusText.Text = "Программный PDF открыт в окне предпросмотра";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия программного PDF";
                MessageBox.Show($"Ошибка открытия программного PDF: {ex.Message}", "Программный PDF",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnExportNativePdfClick(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "Нативный PDF",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!HasNativeTemplate(_currentReport))
            {
                MessageBox.Show(
                    "Для выбранного отчета нет нативного макета конструктора. Откройте конструктор и сохраните макет либо выберите программный или FRX-формат.",
                    "Нативный PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                StatusText.Text = "Открытие PDF по нативному макету конструктора...";

                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                var printFormService = new PrintFormService(_context);
                var pdfTask = Task.Run(() => printFormService.ExportReportTemplatePreview(dataSnapshot, reportSnapshot));
                var completedTask = await Task.WhenAny(pdfTask, Task.Delay(TimeSpan.FromSeconds(20)));
                if (completedTask != pdfTask)
                {
                    _ = pdfTask.ContinueWith(task =>
                    {
                        if (task.Exception != null)
                            System.Diagnostics.Debug.WriteLine(task.Exception.GetBaseException().Message);
                    }, TaskContinuationOptions.OnlyOnFaulted);

                    StatusText.Text = "Нативный PDF обрабатывается слишком долго.";
                    MessageBox.Show(
                        "Нативный макет обрабатывается слишком долго. Операция остановлена, приложение не будет ждать бесконечно.",
                        "Нативный PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ShowPdfPreview(await pdfTask, $"{_currentReport.Name} (нативный)");
                StatusText.Text = "Нативный PDF открыт в окне предпросмотра";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия нативного PDF";
                MessageBox.Show($"Ошибка открытия нативного PDF: {ex.Message}", "Нативный PDF",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnExportFrxPdfClick(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "PDF по FRX-макету",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!HasFoxProTemplate(_currentReport))
            {
                MessageBox.Show(
                    "Для выбранного отчета не подключен FRX-шаблон. Выберите FRX-вариант отчета или подключите макет в конфигураторе.",
                    "PDF по FRX-макету", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                StatusText.Text = "Открытие PDF по старому FRX-макету FoxPro...";

                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                var printFormService = new PrintFormService(_context);
                var pdfTask = Task.Run(() => printFormService.ExportReportTemplatePreview(dataSnapshot, reportSnapshot));
                var completedTask = await Task.WhenAny(pdfTask, Task.Delay(TimeSpan.FromSeconds(20)));
                if (completedTask != pdfTask)
                {
                    _ = pdfTask.ContinueWith(task =>
                    {
                        if (task.Exception != null)
                            System.Diagnostics.Debug.WriteLine(task.Exception.GetBaseException().Message);
                    }, TaskContinuationOptions.OnlyOnFaulted);

                    StatusText.Text = "FRX PDF обрабатывается слишком долго.";
                    MessageBox.Show(
                        "FRX-макет обрабатывается слишком долго. Операция остановлена, приложение не будет ждать бесконечно.",
                        "PDF по FRX-макету", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ShowPdfPreview(await pdfTask, $"{_currentReport.Name} (FRX)");
                StatusText.Text = "PDF по FRX-макету открыт в окне предпросмотра";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия PDF по FRX";
                MessageBox.Show($"Ошибка открытия PDF по FRX-макету: {ex.Message}", "PDF по FRX-макету",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }
        private async void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentData == null || _currentReport == null)
                {
                    MessageBox.Show("Сначала сформируйте отчет.", "Excel",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var format = GetSelectedReportOutputFormat();
                if (IsTaxExcelFormat(format))
                {
                    OnExportVatClick(sender, e);
                    return;
                }

                var useFoxProExcelLayout = IsFoxProExcelFormat(format);
                var useNativeExcelLayout = IsNativeExcelFormat(format);
                var useClosedXmlLayout = IsClosedXmlExcelFormat(format);
                if (useFoxProExcelLayout && !HasFoxProTemplate(_currentReport))
                {
                    MessageBox.Show(
                        "Для выбранного отчета не подключен FRX-шаблон. Выберите FRX-вариант отчета или откройте программный формат.",
                        "FRX FoxPro Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (useNativeExcelLayout && !HasNativeTemplate(_currentReport))
                {
                    MessageBox.Show(
                        "Для выбранного отчета нет нативного макета конструктора. Откройте конструктор и сохраните макет либо выберите программный или FRX-формат.",
                        "Нативный Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (useClosedXmlLayout && !HasClosedXmlLayout(_currentReport))
                {
                    MessageBox.Show(
                        "Для выбранного отчета не сохранён ClosedXML-макет. Откройте конструктор, вкладку «📊 ClosedXML макет», соберите разметку и сохраните отчет.",
                        "ClosedXML Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                IsEnabled = false;
                var formatTitle = useFoxProExcelLayout
                    ? "FRX FoxPro Excel"
                    : useNativeExcelLayout
                        ? "Нативный Excel"
                        : useClosedXmlLayout
                            ? "ClosedXML Excel"
                            : "Программный Excel";
                StatusText.Text = $"Открытие {formatTitle}...";

                var suffix = useFoxProExcelLayout
                    ? "FRX"
                    : useNativeExcelLayout
                        ? "NATIVE"
                        : useClosedXmlLayout
                            ? "CLOSEDXML"
                            : "PROGRAM";
                var outputPath = BuildTemporaryExcelPath($"{_currentReport.Name}_{suffix}", "xlsx");
                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                byte[] excelBytes;
                if (useClosedXmlLayout)
                {
                    // Макет берётся только из самого отчёта (Reports."ClosedXmlLayout").
                    var layout = ClosedXmlReportRenderer.ParseLayout(reportSnapshot.ClosedXmlLayout, out var layoutError);
                    if (layout == null)
                        throw new InvalidOperationException(layoutError);

                    var renderParameters = await _reportService.ResolveParametersForPreviewAsync(reportSnapshot);
                    var preparedData = ClosedXmlReportRenderer.PrepareData(dataSnapshot, reportSnapshot);
                    var typedParameters = renderParameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
                    excelBytes = await Task.Run(() =>
                        ClosedXmlReportRenderer.RenderToBytes(layout, preparedData, typedParameters));
                }
                else if (useFoxProExcelLayout || useNativeExcelLayout)
                {
                    var printFormService = new PrintFormService(_context);
                    excelBytes = await Task.Run(() =>
                        printFormService.ExportReportTemplateExcel(dataSnapshot, reportSnapshot));
                }
                else if (IsReconciliationActReport(dataSnapshot, reportSnapshot))
                {
                    var printFormService = new PrintFormService(_context);
                    excelBytes = await Task.Run(() =>
                        printFormService.ExportProgrammaticReconciliationActExcel(dataSnapshot, reportSnapshot));
                }
                else
                {
                    excelBytes = await Task.Run(() => _reportService.ExportToExcel(dataSnapshot, reportSnapshot));
                }

                File.WriteAllBytes(outputPath, excelBytes);
                OpenGeneratedFile(outputPath);
                StatusText.Text = $"{formatTitle} открыт: {Path.GetFileName(outputPath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия Excel";
                MessageBox.Show($"Ошибка открытия бухгалтерского отчета в Excel: {ex.Message}", "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnExportVatClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                if (start.Year != end.Year || start.Month != end.Month)
                {
                    if (MessageBox.Show(
                            "Для налогового отчета обычно выбирается один календарный месяц. Продолжить выгрузку для выбранного диапазона?",
                            "Tax Excel",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                var templateService = new RegulatedReportTemplateService(_context);
                var activeTemplate = await templateService.GetActiveTemplateAsync(VatTaxReportExportService.OfficialTemplateCode);
                var templateExtension = activeTemplate?.FileExtension;
                if (string.IsNullOrWhiteSpace(templateExtension))
                    templateExtension = Path.GetExtension(activeTemplate?.OriginalFileName);

                var defaultExtension = string.Equals(templateExtension, ".xls", StringComparison.OrdinalIgnoreCase)
                    ? "xls"
                    : "xlsx";
                var outputPath = BuildTemporaryExcelPath($"STI062_tax_{start:yyyyMM}", defaultExtension);

                IsEnabled = false;
                StatusText.Text = activeTemplate == null
                    ? "Формирование внутреннего налогового Excel-отчета..."
                    : $"Заполнение шаблона {activeTemplate.Code} из базы данных...";

                var exportService = new VatTaxReportExportService(_context);
                var exportResult = await exportService.ExportMonthlyVatReportAsync(start, end, outputPath);
                OpenGeneratedFile(outputPath);

                StatusText.Text = exportResult.UsedOfficialTemplate
                    ? $"Налоговый Excel открыт. Шаблон: {exportResult.TemplateCode}."
                    : "Налоговый Excel открыт. Активный официальный шаблон не найден, использован внутренний отчет.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия налогового Excel";
                MessageBox.Show($"Ошибка открытия налогового Excel: {ex.Message}", "Tax Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static string BuildTemporaryExcelPath(string baseName, string extension) =>
            BuildTemporaryReportPath(baseName, extension);

        private static string BuildTemporaryReportPath(string baseName, string extension)
        {
            var safeName = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "report";

            var normalizedExtension = (extension ?? "xlsx").Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(normalizedExtension))
                normalizedExtension = "xlsx";

            var directory = Path.Combine(Path.GetTempPath(), "BIS.ERP", "ReportOutput");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalizedExtension}");
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = (value ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(chars).Trim(' ', '.', '_');
        }

        /// <summary>
        /// Показывает сформированный PDF в окне предпросмотра.
        ///
        /// Документ не пишется на диск и не открывается внешней программой:
        /// просмотр и печать выполняются в самом окне.
        /// </summary>
        private void ShowPdfPreview(byte[] pdfBytes, string documentName)
        {
            var previewWindow = new PdfPreviewWindow(pdfBytes, documentName)
            {
                Owner = Window.GetWindow(this)
            };
            previewWindow.ShowDialog();
        }

        private static void OpenGeneratedFile(string filePath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
    }
}
```

### Views/Dialogs/ReferenceSelectionDialog.xaml.cs

SHA-256: `1b7509433d34bdc2c86a71f190f72f8cad61feabaaee4e123481d42dfc89796f`

```csharp
using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class ReferenceSelectionDialog : Window
    {
        private static readonly IValueConverter GridValueConverter = new ReferenceGridValueConverter();
        private readonly List<Dictionary<string, object>> _items;
        private readonly string _firstField;
        private readonly string _secondField;
        private IReadOnlyDictionary<string, Dictionary<Guid, string>>? _referenceMaps;
        private List<Dictionary<string, object>> _filteredItems;
        private Func<Dictionary<string, object>?, Task>? _extraAction;
        private MetadataObject? _catalog;
        private MetadataService? _metadataService;

        public Dictionary<string, object> SelectedItem { get; private set; }

        public ReferenceSelectionDialog(
            List<Dictionary<string, object>> items,
            string firstField = null,
            string secondField = null,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps = null)
        {
            InitializeComponent();
            var sourceItems = items ?? new List<Dictionary<string, object>>();
            _referenceMaps = referenceMaps;
            _items = referenceMaps != null && referenceMaps.Count > 0
                ? ReferenceDisplayHelper.ResolveRows(sourceItems, referenceMaps)
                : sourceItems;
            _firstField = firstField;
            _secondField = secondField;
            _filteredItems = _items;

            RebuildGrid();
        }

        /// <summary>
        /// Включает кнопки «Добавить» и «Изменить», позволяя создавать и
        /// редактировать записи нужного справочника прямо из окна выбора.
        /// </summary>
        public void ConfigureCatalogEditing(MetadataObject catalog, MetadataService metadataService)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));

            AddButton.Visibility = Visibility.Visible;
            EditButton.Visibility = Visibility.Visible;
            AddButton.IsEnabled = true;
            EditButton.IsEnabled = ItemsGrid.SelectedItem != null;

            if (catalog.TableName.Equals("catalog_advance_payments", StringComparison.OrdinalIgnoreCase))
            {
                // В выборе пары служебные признаки идут сразу за валютой.
                string FieldName(string column) => catalog.Fields.FirstOrDefault(field =>
                    field.DbColumnName.Equals(column, StringComparison.OrdinalIgnoreCase))?.Name ?? column;
                var currency = FieldName("currency_id");
                var active = FieldName("is_active");
                var note = FieldName("description");
                var columns = ItemsGrid.Columns.OrderBy(column => column.DisplayIndex).ToList();
                var trailing = new[] { active, note }.Select(name => columns.FirstOrDefault(column =>
                    string.Equals(column.Header?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
                    .OfType<DataGridColumn>().ToList();
                columns.RemoveAll(column => trailing.Contains(column));
                var currencyIndex = columns.FindIndex(column =>
                    string.Equals(column.Header?.ToString(), currency, StringComparison.OrdinalIgnoreCase));
                if (currencyIndex >= 0)
                {
                    columns.InsertRange(currencyIndex + 1, trailing);
                    for (var index = 0; index < columns.Count; index++)
                        columns[index].DisplayIndex = index;
                }
            }
        }

        private void RebuildGrid()
        {
            if (_items.Count > 0)
            {
                // Определяем все возможные ключи (поля)
                var allKeys = _items.SelectMany(d => d.Keys).Distinct().ToList();
                // Исключаем служебные поля
                var excludeKeys = new[] { "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy", "IsDeleted" };
                var displayKeys = allKeys.Where(k => !excludeKeys.Contains(k) && k != "Id").ToList();

                // Если заданы поля для отображения, используем их в первую очередь
                if (!string.IsNullOrEmpty(_firstField) && displayKeys.Contains(_firstField))
                {
                    // Помещаем firstField на первое место
                    displayKeys.Remove(_firstField);
                    displayKeys.Insert(0, _firstField);
                }
                if (!string.IsNullOrEmpty(_secondField) && displayKeys.Contains(_secondField))
                {
                    // Помещаем secondField на второе место, если оно не firstField
                    if (displayKeys.Contains(_secondField) && _secondField != _firstField)
                    {
                        displayKeys.Remove(_secondField);
                        if (displayKeys.Count > 0 && displayKeys[0] == _firstField)
                            displayKeys.Insert(1, _secondField);
                        else
                            displayKeys.Insert(0, _secondField);
                    }
                }

                // Добавляем колонки в DataGrid
                ItemsGrid.AutoGenerateColumns = false;
                ItemsGrid.Columns.Clear();

                foreach (var key in displayKeys)
                {
                    ItemsGrid.Columns.Add(CreateColumn(
                        key,
                        key == _secondField,
                        IsAccountColumn(key),
                        IsLogicalColumn(_items, key)));
                }

                // Если нет колонок, добавляем колонку "Id"
                if (ItemsGrid.Columns.Count == 0)
                {
                    ItemsGrid.Columns.Add(CreateColumn("Id", isName: true, isAccount: false, isLogical: false));
                }
            }
            else
            {
                ItemsGrid.AutoGenerateColumns = false;
                ItemsGrid.Columns.Clear();

                foreach (var key in new[] { _firstField, _secondField }
                             .Where(key => !string.IsNullOrWhiteSpace(key))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ItemsGrid.Columns.Add(CreateColumn(
                        key,
                        key == _secondField,
                        IsAccountColumn(key),
                        IsLogicalColumn(_items, key)));
                }

                if (ItemsGrid.Columns.Count == 0)
                {
                    ItemsGrid.Columns.Add(CreateColumn("Id", isName: true, isAccount: false, isLogical: false));
                }
            }

            ItemsGrid.ItemsSource = _filteredItems;
        }
public void ConfigureExtraAction(string caption, Func<Dictionary<string, object>?, Task> action, string tooltip = null)
        {
            _extraAction = action ?? throw new ArgumentNullException(nameof(action));
            ExtraActionButton.Content = caption;
            ExtraActionButton.ToolTip = tooltip;
            ExtraActionButton.Visibility = Visibility.Visible;
            ExtraActionButton.IsEnabled = ItemsGrid.SelectedItem != null;
        }

        /// <summary>
        /// Заранее заполняет поле поиска: окно открывается уже отфильтрованным по тексту,
        /// введённому в ячейку (поиск материала по коду или наименованию).
        /// </summary>
        public void SetSearchText(string? text)
        {
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value))
                return;

            SearchBox.Text = value;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            _filteredItems = FilterItems(search);
            // Обновление источника — только через UI-очередь и с полной
            // заменой коллекции (reset), а не мутацией привязанного списка.
            ItemsGrid.ItemsSource = null;
            ItemsGrid.ItemsSource = _filteredItems;
        }

        private void ApplySort()
        {
            // Сортировка отключена: порядок задаётся уже отфильтрованным списком.
        }

        private List<Dictionary<string, object>> FilterItems(string search)
        {
            if (string.IsNullOrEmpty(search))
                // ВАЖНО: возвращаем НОВЫЙ список, а не ссылку на _items.
                // Мутация общего экземпляра, привязанного к ItemsGrid, без уведомлений
                // коллекции рассинхронизирует генератор DataGrid (лог 09:49).
                return new List<Dictionary<string, object>>(_items);

            return _items
                .Where(item => item.Values.Any(v => v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectButton.IsEnabled = ItemsGrid.SelectedItem != null;
            ExtraActionButton.IsEnabled = ExtraActionButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
            EditButton.IsEnabled = EditButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
        }

        private void ItemsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectItem();
        }

        private async void OnExtraActionClick(object sender, RoutedEventArgs e)
        {
            if (_extraAction == null)
                return;

            if (ItemsGrid.SelectedItem is not Dictionary<string, object> selectedItem)
            {
                MessageBox.Show("Сначала выберите запись справочника.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ExtraActionButton.IsEnabled = false;
                await _extraAction(selectedItem);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка выполнения действия: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ExtraActionButton.IsEnabled = ExtraActionButton.Visibility == Visibility.Visible && ItemsGrid.SelectedItem != null;
            }
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (_catalog == null || _metadataService == null)
                return;

            try
            {
                var dialog = new CatalogItemDialog(_catalog, _metadataService);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, $"Добавление: {_catalog.Name}") != true)
                    return;

                Cursor = Cursors.Wait;
                var newId = await _metadataService.CreateDynamicRecordAsync(_catalog.Id, dialog.ItemData);
                await ReloadItemsAsync(newId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления записи: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }
private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            if (_catalog == null || _metadataService == null)
                return;

            var selectedItem = ItemsGrid.SelectedItem as Dictionary<string, object>;
            if (selectedItem == null)
            {
                MessageBox.Show("Сначала выберите запись для редактирования.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var recordId = GetRecordId(selectedItem);
            if (!recordId.HasValue)
            {
                MessageBox.Show("У выбранной записи не найден идентификатор.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Всегда берём свежие «сырые» данные из БД, чтобы поля-ссылки
                // не оказались подменены отображаемыми значениями при сохранении.
                var rawRows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
                var existingData = rawRows.FirstOrDefault(row => GetRecordId(row) == recordId);
                if (existingData == null)
                {
                    MessageBox.Show("Выбранная запись справочника не найдена.", _catalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(_catalog, _metadataService, existingData);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, $"Редактирование: {_catalog.Name}") != true)
                    return;

                Cursor = Cursors.Wait;
                await _metadataService.UpdateDynamicRecordAsync(_catalog.Id, recordId.Value, dialog.ItemData);
                await ReloadItemsAsync(recordId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления записи: {ex.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async Task ReloadItemsAsync(Guid? selectRecordId = null)
        {
            if (_catalog == null || _metadataService == null)
                return;

            var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
            // Пока была открыта карточка, пользователь мог создать счёт или валюту.
            // Карта, полученная при открытии выбора, таких Id ещё не содержит.
            _referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_catalog, _metadataService);
            var reloaded = _referenceMaps != null && _referenceMaps.Count > 0
                ? ReferenceDisplayHelper.ResolveRows(rows, _referenceMaps)
                : rows;

            // Безопасное обновление ItemsGrid через одну UI-очередь:
            // сбрасываем привязку, затем заменяем содержимое новым списком.
            // Мутация привязанного списка по месту рассинхронизирует
            // генератор DataGrid (лог 09:49).
            await Dispatcher.InvokeAsync(() =>
            {
                ItemsGrid.ItemsSource = null;
                _items.Clear();
                foreach (var row in reloaded)
                    _items.Add(row);

                var search = SearchBox.Text?.Trim() ?? string.Empty;
                _filteredItems = FilterItems(search);
                ApplySort();
                ItemsGrid.ItemsSource = _filteredItems;

                if (selectRecordId.HasValue)
                {
                    var target = _filteredItems.FirstOrDefault(row => GetRecordId(row) == selectRecordId);
                    if (target != null)
                    {
                        ItemsGrid.SelectedItem = target;
                        ItemsGrid.ScrollIntoView(target);
                    }
                }
            });
        }

        private static Guid? GetRecordId(Dictionary<string, object> row)
        {
            if (row.TryGetValue("Id", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
                return id;

            return null;
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            SelectItem();
        }

        private void SelectItem()
        {
            SelectedItem = ItemsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedItem != null)
            {
                MdiDialogService.CloseWithResult(this, true);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            MdiDialogService.CloseWithResult(this, false);
        }

/// <summary>
        /// Ширина колонки. Логические признаки ужимаются до ширины «Да»/«Нет»,
        /// «Вид расчета» и счета дебета/кредита забирают всю свободную ширину
        /// окна, остальные поля получают умеренный размер по содержимому.
        ///
        /// Ширины подобраны по образцу рабочего справочника «Пары счетов»:
        /// там логические признаки занимают 40–62 px, а «Вид расчета» 220 px.
        /// Раньше все колонки имели ширину Auto по содержимому, из-за чего
        /// заголовок «Формировать проводки авансовых платежей» растягивал
        /// колонку на всю ширину, а сумма ширин не помещалась в окно —
        /// колонки не менялись при растягивании окна мышью.
        /// </summary>
        private static DataGridTextColumn CreateColumn(
            string key,
            bool isName,
            bool isAccount,
            bool isLogical)
        {
            return new DataGridTextColumn
            {
                Header = isLogical ? GetCompactHeader(key) : key,
                Width = isName || isAccount
                    ? new DataGridLength(isName ? 3 : 2, DataGridLengthUnitType.Star)
                    : new DataGridLength(isLogical ? GetLogicalColumnWidth(key) : GetFixedColumnWidth(key)),
                MinWidth = isName ? 240 : isAccount ? 140 : isLogical ? 40 : 70,
                CanUserSort = true,
                CanUserResize = true,
                CanUserReorder = true,
                Binding = CreateValueBinding(key)
            };
        }

        /// <summary>
        /// Ширина логической колонки. Заголовки сокращены до «Разн. расч.»,
        /// «Анал. плат.», «Внт. рач.», поэтому 72 px хватает и на заголовок,
        /// и на значение «Да»/«Нет».
        /// </summary>
        private const double LogicalColumnWidth = 72;

        /// <summary>
        /// Ширина логической колонки: «Да/Нет» хватает 72 px, но у «Активен»
        /// заголовок длиннее значения — даём 90 px, чтобы он не обрезался.
        /// </summary>
        private static double GetLogicalColumnWidth(string key) => key switch
        {
            "Активен" or "is_active" => 90,
            _ => LogicalColumnWidth
        };

        private static double GetFixedColumnWidth(string key)
        {
            return key switch
            {
                "Код" or "code" => 56,
                "Тип счета" or "account_type" => 120,
                "Остаток брать из модуля" => 78,
                "Табельный номер" => 92,
                _ => 110
            };
        }

        /// <summary>
        /// Короткие заголовки длинных логических полей — те же сокращения,
        /// что и в справочнике, чтобы колонка «Да/Нет» не раздувалась
        /// под длинный заголовок.
        /// </summary>
        private static string GetCompactHeader(string key) => key switch
        {
            "Участвует во взаиморасчетах" => "Разн. расч.",
            "Формировать проводки авансовых платежей" => "Анал. плат.",
            "Участвует во внутренних взаиморасчетах" => "Внт. рач.",
            "Остаток брать из модуля" => "Модуль",
            "Организации" => "Орг",
            "Сотрудники" => "Сотр.",
            "Валютный учет" or "Валютный учёт" => "Вал. учёт",
            _ => key
        };

        private static bool IsAccountColumn(string key) =>
            key is "Дебет" or "Кредит" or "Счет дебета" or "Счет кредита" or "Счёт дебета" or "Счёт кредита" or "Корр счет";

        /// <summary>
        /// Логическое поле — во всех строках только «Да»/«Нет». Такие колонки
        /// не должны занимать ширину по длине заголовка.
        /// </summary>
        private static bool IsLogicalColumn(IEnumerable<Dictionary<string, object>> items, string key)
        {
            var hasValue = false;
            foreach (var item in items)
            {
                if (!item.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
                    continue;

                hasValue = true;
                if (value is bool)
                    continue;

                if (value is string text)
                {
                    if (text.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals("False", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                return false;
            }

            return hasValue;
        }

        private static Binding CreateValueBinding(string key)
        {
            return new Binding($"[{key}]")
            {
                Converter = GridValueConverter,
                ConverterParameter = key
            };
        }

        private sealed class ReferenceGridValueConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var key = parameter as string ?? string.Empty;
                var current = value?.ToString();

                if (current == null)
                    return "—";

                if (key is "Остаток брать из модуля" or "module_code")
                {
                    var moduleName = ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName(current);
                    return string.IsNullOrWhiteSpace(moduleName) ? current : moduleName;
                }

                if (current.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                    current.Equals("False", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Equals("True", StringComparison.OrdinalIgnoreCase)
                        ? (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase) ? "Активен" : "Да")
                        : (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase) ? "Неактивен" : "Нет");
                }

                return current;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
```

### Views/CatalogItemDialog.xaml.cs

SHA-256: `60caeeb4c4a85a5f77454e16a322d64265174edc3d997d8204ce7373a20ddbb6`

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CatalogItemDialog : Window
    {
        private readonly MetadataObject _catalog;
        private readonly Dictionary<string, object> _itemData;
        private readonly Dictionary<string, Control> _controls;
        private readonly Dictionary<string, Panel> _fieldPanels = new(StringComparer.OrdinalIgnoreCase);
        private readonly MetadataService _metadataService;
        private readonly bool _isNewRecord;
        private Dictionary<string, Dictionary<Guid, string>> _referenceCache;
        private AccountAnalyticsRegistry? _accountAnalytics;
        private string? _assignedModuleName;
        private readonly TaskCompletionSource<bool> _formBuilt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isSaving;

        public Dictionary<string, object> ItemData => _itemData;

        /// <param name="isNewRecord">
        /// true — форма открывается для новой записи, даже если переданы значения
        /// по умолчанию (например, «Организация» при добавлении счёта из карточки
        /// организации). Тогда в форму подставляются дефолты (валюта — сом).
        /// </param>
        public CatalogItemDialog(
            MetadataObject catalog,
            MetadataService metadataService,
            Dictionary<string, object> existingData = null,
            bool isNewRecord = false)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _itemData = new Dictionary<string, object>();
            _controls = new Dictionary<string, Control>();
            _referenceCache = new Dictionary<string, Dictionary<Guid, string>>();
            _isNewRecord = existingData == null || isNewRecord;

            DialogTitle.Text = _isNewRecord
                ? $"Добавление в справочник: {catalog.Name}"
                : $"Редактирование справочника: {catalog.Name}";

            // Ширина полей равна ширине окна. Без этого ScrollViewer со скроллбаром
            // отдаёт панели бесконечную ширину, поля уезжают за правый край окна и их
            // ширину нельзя ни увидеть, ни изменить мышью. ViewportWidth уже вычтен
            // из размера вертикальная полоса прокрутки.
            FieldsScroll.SizeChanged += (_, _) => FitFieldsPanelToViewport();
            FitFieldsPanelToViewport();

            Loaded += async (s, e) => await BuildFieldsAsync(existingData);
        }

        private void FitFieldsPanelToViewport()
        {
            var viewportWidth = FieldsScroll.ViewportWidth;
            if (viewportWidth > 0 && FieldsPanel.MaxWidth != viewportWidth)
                FieldsPanel.MaxWidth = viewportWidth;
        }

        private async System.Threading.Tasks.Task BuildFieldsAsync(Dictionary<string, object> existingData)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync START for {_catalog.Name} ===");

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                _assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(_catalog.Id, _catalog.ObjectType);

                // Для новой записи дозаполняем дефолты (валюта расчётного счёта — сом)
                existingData = await ApplyNewRecordDefaultsAsync(existingData, catalogsDict);

                foreach (var field in GetEditableFields())
                {
                    System.Diagnostics.Debug.WriteLine($"Processing field: {field.Name}, Type: {field.FieldType}");

                    var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
                    // Звёздочка — обязательное поле: без него запись не сохранится
                    // (в «Парах счетов» так помечен «Вид расчета»).
                    var label = new TextBlock
                    {
                        Text = field.IsRequired ? $"{GetFieldLabel(field)} *" : GetFieldLabel(field),
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    panel.Children.Add(label);

                    Control inputControl;

                    var choiceControl = await CreateChartOfAccountsChoiceControlAsync(field, existingData);
                    if (choiceControl != null)
                    {
                        inputControl = choiceControl;
                    }
                    else if (ShouldUseAccountPicker(field))
                    {
inputControl = AccountPickerControlFactory.Create(
                    _accountAnalytics!,
                    GetExistingValue(field, existingData),
                    this,
                    // Счета материала не делятся по модулю: хранение относится к ТМЦ,
                    // а доход и себестоимость — к финансовым/затратным счетам. Поэтому
                    // в карточке материала показывается весь план счетов.
                    moduleCodeOrName: IsMaterialsCatalog() ? null : _assignedModuleName,
                    // В «Парах счетов» счёт дебета/кредита вводится вручную так же,
                    // как в остальных документах: только цифры, до 8 знаков, плюс
                    // выбор из плана счетов кнопкой «?». В карточке материала счёт
                    // тоже набирается кодом, как в прежней форме (см. макет).
                    allowManualInput: IsAccountPairsCatalog() || IsMaterialsCatalog());
                    }
                    // УНИВЕРСАЛЬНАЯ ОБРАБОТКА REFERENCE ПОЛЕЙ
                    else if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                    {
                        inputControl = await CreateReferenceControl(field, catalogsDict, existingData);
                    }
                    else
                    {
                        inputControl = CreateRegularControl(field, existingData);
                    }

                    await ApplyGeneratedCodeAsync(field, inputControl);
                    ApplyOrganizationCountryDefault(field, inputControl);
                    ApplyCatalogFieldState(field, inputControl);
                    ApplyMaterialFieldHints(field, inputControl);

                    panel.Children.Add(inputControl);

                    // У материала код ТН ВЭД не смешивается с остальными реквизитами:
                    // он уезжает в блок «Международные коды товаров/услуг» под полями.
                    var targetPanel = IsMaterialsCatalog() && IsInternationalCodeField(field)
                        ? ExtraBlockFields
                        : FieldsPanel;
                    targetPanel.Children.Add(panel);
                    _controls[field.Name] = inputControl;
                    _fieldPanels[field.Name] = panel;
                }

                UpdateExtraBlockVisibility();

                // «Валютный учет» в «Парах счетов» включает выбор валюты пары.
                AttachAccountPairsCurrencyFlag();
                await UpdateAccountPairsCurrencyVisibilityAsync();

                AttachOrganizationNameSync();

                System.Diagnostics.Debug.WriteLine($"=== BuildFieldsAsync COMPLETED ===");
                _formBuilt.TrySetResult(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BuildFieldsAsync ERROR: {ex.Message}");
                _formBuilt.TrySetException(ex);
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private string GetFieldLabel(MetadataField field)
        {
            if (IsOrganizationsCatalog())
            {
                return field.Name switch
                {
                    "Регистрационный номер" => "Районная налоговая инспекция",
                    _ => field.Name
                };
            }

            if (!IsEmployeesCatalog())
                return field.Name;

            return field.Name switch
            {
                "Должность (справочник)" => "Должность",
                "Примечание" => "Примечания",
                _ => field.Name
            };
        }

        private void NormalizeCatalogItemDataBeforeSave()
        {
            if (!IsEmployeesCatalog())
                return;

            var personnelNumber = GetItemText("Табельный номер", "personnel_number", "Код", "code");
            var fullName = GetItemText("ФИО", "full_name", "Наименование", "name");

            _itemData["Код"] = personnelNumber;
            _itemData["Наименование"] = fullName;

            if (!TryGetTextItem("Статус", out _))
                _itemData["Статус"] = "Активен";

            NormalizeOptionalEmployeeChoice("Пол");
            NormalizeOptionalEmployeeChoice("Семейное положение");

            if (_itemData.TryGetValue("Кем выдан", out var issuer) && issuer is string issuerText)
                _itemData["Кем выдан"] = issuerText.Trim().ToUpperInvariant();
        }

        private void NormalizeOptionalEmployeeChoice(string fieldName)
        {
            if (!_itemData.TryGetValue(fieldName, out var value))
                return;

            if (value == null || value == DBNull.Value)
                return;

            var text = value.ToString()?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Equals("Не указано", StringComparison.OrdinalIgnoreCase))
                _itemData[fieldName] = DBNull.Value;
        }

        private bool TryGetTextItem(string fieldName, out string text)
        {
            text = GetItemText(fieldName);
            return !string.IsNullOrWhiteSpace(text);
        }
        private void AttachOrganizationNameSync()
        {
            if (!string.Equals(_catalog.Name, "Организации", StringComparison.OrdinalIgnoreCase))
                return;

            if (!_controls.TryGetValue("Наименование", out var nameControl) ||
                !_controls.TryGetValue("Полное наименование", out var fullNameControl) ||
                nameControl is not TextBox nameBox ||
                fullNameControl is not TextBox fullNameBox)
                return;

            var lastAutoValue = string.Empty;
            if (string.IsNullOrWhiteSpace(fullNameBox.Text) && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                fullNameBox.Text = nameBox.Text;
                lastAutoValue = nameBox.Text;
            }

            nameBox.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(fullNameBox.Text) &&
                    !string.Equals(fullNameBox.Text, lastAutoValue, StringComparison.Ordinal))
                    return;

                fullNameBox.Text = nameBox.Text;
                lastAutoValue = nameBox.Text;
            };
        }
        private bool ShouldUseAccountPicker(MetadataField field)
        {
            return AccountAnalyticsRules.IsAccountSelectorField(field) &&
                   _accountAnalytics?.Accounts.Count > 0;
        }

        // Пикеры «Банк» и «Расчетный счет» для карточки организации удалены:
        // эти реквизиты больше не входят в метаданные организаций.
        // Банк, счёт и валюта выбираются в справочниках «Банки» и
        // «Расчетные счета организаций» (в карточке счёта — «Организация»,
        // «Банк», «Счет», «Валюта»).

        private static object? GetExistingValue(MetadataField field, Dictionary<string, object> existingData)
        {
            if (existingData == null)
                return null;

            if (existingData.TryGetValue(field.Name, out var byName))
                return NormalizeExistingValue(byName);

            if (!string.IsNullOrWhiteSpace(field.DbColumnName) &&
                existingData.TryGetValue(field.DbColumnName, out var byColumn))
                return NormalizeExistingValue(byColumn);

            return null;
        }

        private static object? NormalizeExistingValue(object? value)
        {
            return value == null || value == DBNull.Value ? null : value;
        }

        // Универсальное создание ComboBox для любого Reference поля
        private async Task<Control> CreateReferenceControl(
       MetadataField field,
       Dictionary<string, MetadataObject> catalogsDict,
       Dictionary<string, object> existingData)
        {
            if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
            {
                var comboBox = new ComboBox
                {
                    Height = 35,
                    Name = field.Name.Replace(" ", "_").Replace("-", "_"),
                    DisplayMemberPath = "DisplayName",
                    SelectedValuePath = "Id",
                    MinWidth = 200,
                    Tag = field
                };

                comboBox.ItemsSource = new List<ReferenceItem>
        {
            new ReferenceItem { Id = Guid.Empty, DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден" }
        };
                return comboBox;
            }

            var picker = await ReferencePickerControlFactory.CreateAsync(
                _metadataService,
                field,
                refCatalog,
                GetExistingValue(field, existingData),
                this);

            // ========== ДОБАВИТЬ ЭТОТ БЛОК ==========
            // Автоподстановка для поля "Табельный номер"
            if (field.Name == "Табельный номер")
            {
                picker.ComboBox.SelectionChanged += async (s, args) =>
                {
                    if (picker.ComboBox.SelectedItem is ReferenceItem selectedEmployee)
                    {
                        await AutoFillFromEmployee(selectedEmployee.Id);
                    }
                };
            }
            // =======================================

            return picker;
        }

        private static IEnumerable<string> GetReferenceLookupKeys(
            Dictionary<string, object> row,
            string displayName)
        {
            foreach (var keyName in new[]
                     {
                         "Id", "Код", "code", "Code", "Счет", "account_code",
                         "Код организации", "organization_code",
                         "Наименование", "name", "ФИО", "full_name"
                     })
            {
                if (!row.TryGetValue(keyName, out var value))
                    continue;

                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                yield return displayName.Trim();

            var displayKey = NormalizeReferenceLookupKey(displayName);
            if (!string.IsNullOrWhiteSpace(displayKey))
                yield return displayKey;

            foreach (var value in row.Values)
            {
                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? normalized[..separatorIndex].Trim()
                : normalized;
        }

        /// <summary>
        /// Универсальное определение имени поля для отображения
        /// </summary>
        private string GetDisplayFieldName(MetadataObject catalog, List<Dictionary<string, object>> data)
        {
            // Приоритеты для отображения (в порядке убывания)
            var preferredFields = new[]
            {
                "Наименование", "name", "Name",
                "ФИО", "full_name", "FullName",
                "Наименование вида", "Наименование категории",
                "site_name", "Наименование участка",
                "Код", "code", "Code"
            };

            if (data.Count == 0) return "Id";

            var firstRow = data[0];

            foreach (var prefField in preferredFields)
            {
                if (firstRow.ContainsKey(prefField))
                    return prefField;
            }

            // Если ничего не нашли - возвращаем первый строковый ключ
            foreach (var key in firstRow.Keys)
            {
                if (firstRow[key] is string && key != "Id" && key != "CreatedAt" && key != "UpdatedAt")
                    return key;
            }

            return "Id";
        }

        /// <summary>
        /// Универсальное получение отображаемого значения
        /// </summary>
        private string GetDisplayValueOLD(Dictionary<string, object> row, string displayFieldName, string catalogName)
        {
            if (row.ContainsKey(displayFieldName) && row[displayFieldName] != null)
                return row[displayFieldName].ToString();

            // Специальные случаи для конкретных справочников (если нужно форматирование)
            if (catalogName == "Сотрудники (Списочный состав)")
            {
                var personnelNumber = "";
                var fullName = "";

                if (row.ContainsKey("Табельный номер")) personnelNumber = row["Табельный номер"]?.ToString() ?? "";
                if (row.ContainsKey("ФИО")) fullName = row["ФИО"]?.ToString() ?? "";
                if (row.ContainsKey("Наименование")) fullName = row["Наименование"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(personnelNumber) && !string.IsNullOrEmpty(fullName))
                    return $"{personnelNumber} - {fullName}";
                if (!string.IsNullOrEmpty(fullName))
                    return fullName;
                if (!string.IsNullOrEmpty(personnelNumber))
                    return personnelNumber;
            }

            // Если ничего не нашли - возвращаем Id
            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
        }
        /// <summary>
        /// Универсальное получение отображаемого значения по шаблону из метаданных
        /// </summary>
        private string GetDisplayValue(Dictionary<string, object> row, MetadataField field, string catalogName)
        {
            // Если есть шаблон - используем его
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var fld in fieldNames)
                {
                    var fieldKey = fld.Trim();
                    var value = "";

                    if (row.ContainsKey(fieldKey))
                        value = row[fieldKey]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.Replace(" ", "_")))
                        value = row[fieldKey.Replace(" ", "_")]?.ToString() ?? "";

                    result = result.Replace($"{{{fieldKey}}}", value);
                }

                return result;
            }

            // === БЕЗ ШАБЛОНА - автоопределение ===

            // Приоритеты полей для отображения (общие для всех справочников)
            var priorityFields = new[]
            {
        "Наименование", "name", "Name",
        "ФИО", "full_name", "FullName",
        "Наименование вида", "Наименование категории",
        "site_name", "Наименование участка",
        "Код", "code", "Code"
    };

            foreach (var priority in priorityFields)
            {
                if (row.ContainsKey(priority) && row[priority] != null)
                    return row[priority].ToString();
            }

            // Ищем любое строковое поле
            foreach (var key in row.Keys)
            {
                if (key != "Id" && key != "CreatedAt" && key != "UpdatedAt" && row[key] is string strVal && !string.IsNullOrEmpty(strVal))
                    return strVal;
            }

            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
        }
        /// <summary>
        /// Создание обычного контрола (не Reference)
        /// </summary>
        private async Task<Control?> CreateChartOfAccountsChoiceControlAsync(
            MetadataField field,
            Dictionary<string, object> existingData)
        {
            // «Остаток брать из модуля» в справочнике «Пары счетов» — выбор модуля
            // из списка, а не свободный текст: поле знает только «Финансы» либо
            // несколько вариантов, введённых вручную.
            if (IsAccountPairsCatalog() &&
                string.Equals(field.DbColumnName, "module_code", StringComparison.OrdinalIgnoreCase))
            {
                var moduleOptions = await GetClosingModuleOptionsAsync();
                var existingModule = GetExistingValue(field, existingData)?.ToString();
                var moduleComboBox = new ComboBox
                {
                    Height = 35,
                    MinWidth = 200,
                    ItemsSource = moduleOptions
                };
                moduleComboBox.SelectedItem = moduleOptions
                    .OfType<ChoiceItem>()
                    .FirstOrDefault(option => option.Matches(NormalizeClosingModuleChoice(existingModule)))
                    ?? moduleOptions.FirstOrDefault();
                return moduleComboBox;
            }

            if (_catalog.Name != "План счетов")
                return null;

            IReadOnlyList<object>? options = field.Name switch
            {
                "Тип счета" => new object[]
                {
                    new ChoiceItem("Active", LocalizationService.DisplayValue("Active"), "Активный"),
                    new ChoiceItem("Passive", LocalizationService.DisplayValue("Passive"), "Пассивный"),
                    new ChoiceItem("ActivePassive", LocalizationService.DisplayValue("ActivePassive"), "Активно-пассивный")
                },
                "Закрывает модуль" => await GetClosingModuleOptionsAsync(),
                "Признак печати" => BuildModeChoiceItems(ChartOfAccountsSelectionMetadata.PrintModeOptions),
                "Сохранять остатки" => BuildModeChoiceItems(ChartOfAccountsSelectionMetadata.BalanceModeOptions),
                _ => null
            };

            if (options == null || options.Count == 0)
                return null;

            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                ItemsSource = options
            };

            var existingValue = NormalizeChartOfAccountsChoice(
                field.Name,
                GetExistingValue(field, existingData)?.ToString());

            comboBox.SelectedItem = options
                .OfType<ChoiceItem>()
                .FirstOrDefault(option => option.Matches(existingValue));
            comboBox.SelectedItem ??= options.FirstOrDefault(option =>
                string.Equals(option?.ToString(), existingValue, StringComparison.OrdinalIgnoreCase));
            comboBox.SelectedItem ??= field.Name == "Тип счета"
                ? options.OfType<ChoiceItem>().FirstOrDefault(option => option.Code == "Active")
                : options.FirstOrDefault();

            comboBox.SelectedItem ??= options.FirstOrDefault();
            return comboBox;
        }

        private async Task<IReadOnlyList<object>> GetClosingModuleOptionsAsync()
        {
            var modules = await _metadataService.GetModulesAsync();
            var options = new List<object>();
            var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // closing_module_code — целое поле: храним НОМЕР модуля (3 — финансы, 6 — материалы,
            // 7 — ОС, 4 — сбыт), который NormalizeModuleKey сопоставляет модулю.
            foreach (var module in modules)
            {
                var number = GetClosingModuleNumber(module.Code);
                if (string.IsNullOrWhiteSpace(number) || !usedValues.Add(number))
                    continue;

                options.Add(new ChoiceItem(number, module.Name, module.Code, module.Name));
            }

            foreach (var option in GetFoxClosingModuleChoices())
            {
                if (usedValues.Add(option.Code))
                    options.Add(option);
            }

            return options;
        }

        /// <summary>
        /// Номер модуля для целочисленного closing_module_code.
        /// </summary>
        private static string GetClosingModuleNumber(string? moduleCode) => moduleCode switch
        {
            ModuleMetadataService.FinanceCode => "3",
            ModuleMetadataService.FixedAssetsCode => "7",
            ModuleMetadataService.InventoryCode => "6",
            ModuleMetadataService.SalesCode => "4",
            ModuleMetadataService.RawMaterialsCode => "12",
            ModuleMetadataService.CostAccountingCode => "8",
            _ => string.Empty
        };

        private static IEnumerable<ChoiceItem> GetFoxClosingModuleChoices()
        {
            yield return new ChoiceItem("3", "Финансы", "2", "9", "Finance", "Финансы");
            yield return new ChoiceItem("4", "Сбыт", "Sales", "Продажи", "Реализация", "Регистратура");
            yield return new ChoiceItem("6", "Учет материальных ценностей", "Inventory", "УМЦ", "ТМЦ", "Материалы");
            yield return new ChoiceItem("7", "Основные средства", "FixedAssets", "ОС");
            yield return new ChoiceItem("8", "Вспомогательное производство");
            yield return new ChoiceItem("12", "Сырье", "Сырьё", "RawMaterials");
            yield return new ChoiceItem("66", "Меню");
        }

        private static string NormalizeChartOfAccountsChoice(string fieldName, string? value)
        {
            value ??= string.Empty;

            if (fieldName == "Закрывает модуль")
                return NormalizeClosingModuleChoice(value);

            if (fieldName is "Признак печати" or "Сохранять остатки")
                return ChartOfAccountsSelectionMetadata.NormalizeModeValue(fieldName, value);

            if (fieldName != "Тип счета")
                return value;

            return LocalizationService.DisplayValue(value);
        }

        private static IReadOnlyList<object> BuildModeChoiceItems(
            IEnumerable<ChartOfAccountsChoiceDefinition> options)
        {
            return options
                .Select(option => (object)new ChoiceItem(option.Code, option.Display, option.Aliases))
                .ToList();
        }

        private static string NormalizeAccountTypeCode(string? value) => value switch
        {
            "Активный" => "Active",
            "Пассивный" => "Passive",
            "Активно-пассивный" => "ActivePassive",
            _ => value ?? "Active"
        };

        private Control? CreateEmployeeChoiceControl(MetadataField field, Dictionary<string, object> existingData)
        {
            if (!IsEmployeesCatalog())
                return null;

            var choices = field.Name switch
            {
                "Пол" => new[]
                {
                    new ChoiceItem(string.Empty, "Не указано"),
                    new ChoiceItem("Мужской", "Мужской"),
                    new ChoiceItem("Женский", "Женский")
                },
                "Семейное положение" => new[]
                {
                    new ChoiceItem(string.Empty, "Не указано"),
                    new ChoiceItem("Не состоит в браке", "Не состоит в браке"),
                    new ChoiceItem("Состоит в браке", "Состоит в браке"),
                    new ChoiceItem("Разведен(а)", "Разведен(а)"),
                    new ChoiceItem("Вдовец/вдова", "Вдовец/вдова")
                },
                "Статус" => new[]
                {
                    new ChoiceItem("Активен", "Активен"),
                    new ChoiceItem("Уволен", "Уволен"),
                    new ChoiceItem("В отпуске", "В отпуске"),
                    new ChoiceItem("Декрет", "Декрет")
                },
                _ => null
            };

            if (choices == null)
                return null;

            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                Tag = field,
                ItemsSource = choices
            };

            var existingValue = GetExistingValue(field, existingData)?.ToString();
            comboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Matches(existingValue)) ?? choices[0];
            return comboBox;
        }

        /// <summary>
        /// Вид налога в справочнике «Налоги» выбирается из списка, а не вводится текстом:
        /// от него зависит, в каком списке формы счёта-фактуры появится налог.
        /// </summary>
        private Control? CreateTaxKindChoiceControl(MetadataField field, Dictionary<string, object> existingData)
        {
            if (!IsTaxCatalog() ||
                !string.Equals(field.DbColumnName, "tax_kind", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var choices = new[]
            {
                new ChoiceItem("BOTH", "НДС и налог с продаж", "BOTH", "ОБА", "ВСЕ", "НДС/НСП"),
                new ChoiceItem("VAT", "НДС", "VAT", "НДС"),
                new ChoiceItem("SALES", "Налог с продаж", "SALES", "НСП", "продаж")
            };

            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                Tag = field,
                ItemsSource = choices,
                ToolTip = "Определяет, в каком списке формы счёта-фактуры появится налог."
            };

            var existingValue = GetExistingValue(field, existingData)?.ToString();
            comboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Matches(existingValue)) ?? choices[0];
            return comboBox;
        }

        /// <summary>
        /// Выпадающие списки для справочника «Вид операции ТМЗ»,
        /// чтобы пользователь без технических знаний мог создать запись,
        /// не вводя вручную названия документов и видов движения.
        /// </summary>
        private Control? CreateInventoryEntryChoiceControl(MetadataField field, Dictionary<string, object> existingData)
        {
            if (!IsInventoryEntryCatalog())
                return null;

            var existingValue = GetExistingValue(field, existingData)?.ToString();

            return field.Name switch
            {
                "Документ ТМЗ" => BuildInventoryEntryDocumentCombo(field, existingValue),
                "Вид движения" => BuildFixedChoiceComboBox(field,
                    new[]
                    {
                        new ChoiceItem("Incoming", "Приход"),
                        new ChoiceItem("Outgoing", "Расход"),
                        new ChoiceItem("Movement", "Перемещение"),
                    }, existingValue, "Выберите вид движения операции"),
                _ => null
            };
        }

        /// <summary>
        /// Поле «Документ ТМЗ»: фиксированный список из трёх документов.
        /// При выборе документа в поле «Вид движения» предлагается подходящий вид движения.
        /// </summary>
        private ComboBox BuildInventoryEntryDocumentCombo(MetadataField field, string? existingValue)
        {
            var comboBox = BuildFixedChoiceComboBox(field,
                new[]
                {
                    new ChoiceItem(InventoryDocumentModel.IncomingName, "Поступление ТМЗ"),
                    new ChoiceItem(InventoryDocumentModel.OutgoingName, "Выбытие ТМЗ"),
                    new ChoiceItem(InventoryDocumentModel.MovementName, "Внутреннее перемещение ТМЗ"),
                    new ChoiceItem("Другое…", "Другое…")
                }, existingValue, "Выберите документ ТМЗ (если нужного нет — выберите «Другое…» и впишите вручную)");

            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedItem is not ChoiceItem selected)
                {
                    return;
                }

                // Если выбран «Другое…» — разрешаем свободный ввод названия документа
                if (string.Equals(selected.Code, "Другое…", StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.IsEditable = true;
                    comboBox.Text = string.Empty;
                    comboBox.Focus();
                    return;
                }

                comboBox.IsEditable = false;

                if (!_controls.TryGetValue("Вид движения", out var movementControl) ||
                    movementControl is not ComboBox movementCombo)
                {
                    return;
                }

                string movement = selected.Display switch
                {
                    InventoryDocumentModel.IncomingName => "Приход",
                    InventoryDocumentModel.OutgoingName => "Расход",
                    InventoryDocumentModel.MovementName => "Перемещение",
                    _ => null
                };

                if (movement != null)
                    movementCombo.SelectedItem = movementCombo.Items.OfType<ChoiceItem>()
                        .FirstOrDefault(item => string.Equals(item.Display, movement, StringComparison.OrdinalIgnoreCase));
            };

            return comboBox;
        }

        /// <summary>
        /// Универсальный ComboBox с фиксированным списком вариантов (без свободного ввода).
        /// </summary>
        private static ComboBox BuildFixedChoiceComboBox(MetadataField field, ChoiceItem[] choices, string? existingValue, string toolTip)
        {
            var comboBox = new ComboBox
            {
                Height = 35,
                MinWidth = 200,
                Tag = field,
                ItemsSource = choices,
                ToolTip = toolTip,
                IsEditable = false
            };

            comboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Matches(existingValue)) ?? choices[0];
            return comboBox;
        }

        private bool IsTaxCatalog() =>
            string.Equals(_catalog.Name, "Налоги", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_taxes", StringComparison.OrdinalIgnoreCase);

        /// <summary>Справочник «Пары счетов» (прежнее название — «Авансовые платежи»).</summary>
        private bool IsAccountPairsCatalog() =>
            string.Equals(_catalog.Name, "Пары счетов", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_advance_payments", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.Name, "Авансовые платежи", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// В «Парах счетов» флаг «Валютный учет» управляет выбором валюты пары:
        /// пока флаг снят, поле «Валюта» скрыто и в базу не пишется; когда флаг
        /// установлен — поле показано и по умолчанию подставлена базовая валюта
        /// (киргизский сом) из «Справочника валют».
        /// </summary>
        private void AttachAccountPairsCurrencyFlag()
        {
            if (!IsAccountPairsCatalog())
                return;

            if (!_controls.TryGetValue(GetFieldName(AccountPairsCurrencyFlagField), out var control) ||
                control is not CheckBox checkBox)
            {
                return;
            }

            checkBox.Checked += async (_, _) => await UpdateAccountPairsCurrencyVisibilityAsync();
            checkBox.Unchecked += async (_, _) => await UpdateAccountPairsCurrencyVisibilityAsync();
        }

        private async Task UpdateAccountPairsCurrencyVisibilityAsync()
        {
            if (!IsAccountPairsCatalog())
                return;

            var currencyField = GetFieldName(AccountPairsCurrencyField);
            if (!_fieldPanels.TryGetValue(currencyField, out var panel))
                return;

            var useCurrency = _controls.TryGetValue(GetFieldName(AccountPairsCurrencyFlagField), out var flagControl) &&
                              flagControl is CheckBox checkBox &&
                              checkBox.IsChecked == true;

            panel.Visibility = useCurrency ? Visibility.Visible : Visibility.Collapsed;

            if (!useCurrency)
                return;

            // Валютная пара не должна остаться без валюты: подставляем базовую.
            if (!_controls.TryGetValue(currencyField, out var inputControl) ||
                inputControl is not ReferencePickerControl picker ||
                picker.SelectedReferenceItem != null)
            {
                return;
            }

            var catalogs = (await _metadataService.GetCatalogsAsync())
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var defaultCurrencyId = await ResolveDefaultCurrencyIdAsync(catalogs);
            if (string.IsNullOrWhiteSpace(defaultCurrencyId))
                return;

            var baseCurrency = picker.ComboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id.ToString().Equals(defaultCurrencyId, StringComparison.OrdinalIgnoreCase));

            if (baseCurrency != null)
                picker.SelectedReferenceItem = baseCurrency;
        }

        private static readonly (string Name, string Column) AccountPairsCurrencyFlagField = ("Валютный учет", "use_currency");
        private static readonly (string Name, string Column) AccountPairsCurrencyField = ("Валюта", "currency_id");

        private string GetFieldName((string Name, string Column) field) =>
            GetEditableFields().Any(item => IsField(item, field))
                ? field.Name
                : field.Column;

        private static bool IsField(MetadataField field, (string Name, string Column) candidate) =>
            field.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase) ||
            field.DbColumnName.Equals(candidate.Column, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Если валютный учёт пары выключен, ссылка на валюту не сохраняется:
        /// иначе после повторного включения флага вернулась бы валюта,
        /// которую пользователь снял.
        /// </summary>
        private void NormalizeAccountPairsCurrencyBeforeSave()
        {
            if (!IsAccountPairsCatalog())
                return;

            if (IsAccountPairsCurrencyEnabled())
                return;

            var currencyField = GetFieldName(AccountPairsCurrencyField);
            if (_itemData.ContainsKey(currencyField))
                _itemData[currencyField] = DBNull.Value;
        }

        /// <summary>
        /// У валютной пары валюта обязательна: без неё проводки не проводятся.
        /// </summary>
        private bool ValidateAccountPairsCurrency()
        {
            if (!IsAccountPairsCatalog() || !IsAccountPairsCurrencyEnabled())
                return true;

            var currencyField = GetFieldName(AccountPairsCurrencyField);
            var value = _itemData.TryGetValue(currencyField, out var stored)
                ? stored
                : GetItemValue(currencyField);

            if (value != null && value != DBNull.Value && !string.IsNullOrWhiteSpace(value.ToString()))
                return true;

            MessageBox.Show(
                "Для пары счетов с включённым валютным учётом выберите валюту.",
                "Пары счетов", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (_fieldPanels.TryGetValue(currencyField, out var panel))
                panel.Visibility = Visibility.Visible;

            if (_controls.TryGetValue(currencyField, out var control))
                control.Focus();

            return false;
        }

        private bool IsAccountPairsCurrencyEnabled()
        {
            if (!_controls.TryGetValue(GetFieldName(AccountPairsCurrencyFlagField), out var control))
                return GetItemValue(GetFieldName(AccountPairsCurrencyFlagField)) is true;

            return control switch
            {
                CheckBox checkBox => checkBox.IsChecked == true,
                ComboBox comboBox => comboBox.SelectedItem?.ToString() is "Да" or "True" or "true" or "1",
                TextBox textBox => bool.TryParse(textBox.Text, out var parsed) && parsed,
                _ => GetItemValue(GetFieldName(AccountPairsCurrencyFlagField)) is true
            };
        }

        private Control CreateRegularControl(MetadataField field, Dictionary<string, object> existingData)
        {
            var employeeChoiceControl = CreateEmployeeChoiceControl(field, existingData);
            if (employeeChoiceControl != null)
                return employeeChoiceControl;

            var taxKindChoiceControl = CreateTaxKindChoiceControl(field, existingData);
            if (taxKindChoiceControl != null)
                return taxKindChoiceControl;

            var entryChoiceControl = CreateInventoryEntryChoiceControl(field, existingData);
            if (entryChoiceControl != null)
                return entryChoiceControl;

            var existingValue = GetExistingValue(field, existingData);

            switch (field.FieldType)
            {
                case "Int":
                    var intBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                    ConfigureIntegerInput(intBox, 10);
                    if (existingValue != null)
                        intBox.Text = existingValue.ToString();
                    return intBox;

                case "Decimal":
                    var decimalBox = new TextBox { Height = 35, Padding = new Thickness(10) };
                    ConfigureDecimalInput(decimalBox);
                    if (existingValue != null)
                        decimalBox.Text = existingValue.ToString();
                    return decimalBox;

                case "DateTime":
                    var datePicker = new DatePicker { Height = 35 };
                    if (existingValue is DateTime existingDate)
                        datePicker.SelectedDate = existingDate;
                    else if (DateTime.TryParse(existingValue?.ToString(), out var parsedDate))
                        datePicker.SelectedDate = parsedDate;
                    return datePicker;

                case "Bool":
                    var checkBox = new CheckBox { Content = "Да", Margin = new Thickness(0, 5, 0, 0) };
                    if (TryGetBool(existingValue, out var isChecked))
                        checkBox.IsChecked = isChecked;
                    else if (_isNewRecord && ShouldDefaultActiveField(field))
                        checkBox.IsChecked = true;
                    return checkBox;

                default: // String
                    var textBox = new TextBox { Height = 35, Padding = new Thickness(10) };

                    if (IsChartOfAccountsCatalog() && IsCatalogCodeField(field))
                        ConfigureChartOfAccountsCodeInput(textBox);
                    // «Табельный номер» в метаданных исторически String, но по смыслу
                    // это целое число: буквы в него попадать не должны.
                    else if (IsPersonnelNumberField(field))
                        ConfigureIntegerInput(textBox, 10);

                    // ФИО в самом справочнике сотрудников вводится вручную; в остальных местах поле может быть автозаполняемым.
                    if (!IsEmployeesCatalog() &&
                        !IsOrganizationsCatalog() &&
                        (field.Name.Equals("ФИО", StringComparison.OrdinalIgnoreCase) ||
                         field.Name.Equals("FullName", StringComparison.OrdinalIgnoreCase) ||
                         field.Name.Equals("full_name", StringComparison.OrdinalIgnoreCase) ||
                         field.DbColumnName.Equals("full_name", StringComparison.OrdinalIgnoreCase)))
                    {
                        textBox.IsReadOnly = true;
                        textBox.Background = System.Windows.Media.Brushes.LightGray;
                    }

                    if (existingValue != null)
                        textBox.Text = existingValue.ToString();
                    return textBox;
            }
        }

        private async Task ApplyGeneratedCodeAsync(MetadataField field, Control inputControl)
        {
            // Для плана счетов код счёта вводится вручную (например «1210»):
            // автогенерация «макс+1» не подходит, т.к. коды счетов
            // регламентированы планом счетов. Поле остаётся пустым и редактируемым.
            if (IsChartOfAccountsCatalog())
                return;

            if (!_isNewRecord || !IsCatalogCodeField(field) || inputControl is not TextBox textBox)
                return;

            textBox.Text = await GenerateNextCatalogCodeAsync(field);

            // Для налогов код участвует в учётной логике: он попадает в проводки и
            // сопоставления ЭСФ, поэтому предложенное значение остаётся редактируемым
            // (например, НДС20 вместо автоматического числового кода).
            if (IsTaxCatalog())
            {
                textBox.ToolTip = "Предложенный код можно изменить вручную, например НДС20.";
                return;
            }

            textBox.IsReadOnly = true;
            textBox.ToolTip = "Код формируется автоматически при добавлении записи.";
            textBox.SetResourceReference(Control.BackgroundProperty, "AppReadOnlyBackgroundBrush");
        }

        private bool IsChartOfAccountsCatalog()
        {
            return string.Equals(_catalog.Name, "План счетов", StringComparison.OrdinalIgnoreCase);
        }

        private void ConfigureChartOfAccountsCodeInput(TextBox textBox)
        {
            textBox.MaxLength = 8;
            textBox.ToolTip = "Только цифры, до 8 знаков";
            textBox.PreviewTextInput += (_, args) =>
                args.Handled = !IsAsciiDigits(args.Text);
            textBox.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Space)
                    args.Handled = true;
            };

            DataObject.AddPastingHandler(textBox, (_, args) =>
            {
                if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    args.CancelCommand();
                    return;
                }

                var text = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
                if (!IsAsciiDigits(text))
                    args.CancelCommand();
            });
        }

        private static bool IsAsciiDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                if (character is < '0' or > '9')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Целое поле: только цифры. Закрыты и вставка, и пробел — в числовом
        /// поле текст появляться не должен (табельный номер, количества и т. п.).
        /// </summary>
        private void ConfigureIntegerInput(TextBox textBox, int maxLength)
        {
            if (maxLength > 0)
                textBox.MaxLength = maxLength;

            textBox.ToolTip ??= "Только целое число";
            textBox.PreviewTextInput += (_, args) => args.Handled = !IsAsciiDigits(args.Text);
            textBox.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Space)
                    args.Handled = true;
            };

            DataObject.AddPastingHandler(textBox, (_, args) =>
            {
                if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    args.CancelCommand();
                    return;
                }

                var text = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
                if (!IsAsciiDigits(text))
                    args.CancelCommand();
            });
        }

        /// <summary>
        /// Дробное поле: цифры и один разделитель (запятая или точка), плюс знак.
        /// </summary>
        private void ConfigureDecimalInput(TextBox textBox)
        {
            textBox.ToolTip ??= "Число; разделитель — запятая или точка";
            textBox.PreviewTextInput += (_, args) => args.Handled = !IsDecimalText(args.Text);
            textBox.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Space)
                    args.Handled = true;
            };

            DataObject.AddPastingHandler(textBox, (_, args) =>
            {
                if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
                {
                    args.CancelCommand();
                    return;
                }

                var text = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
                if (!IsDecimalText(text))
                    args.CancelCommand();
            });
        }

        private static bool IsDecimalText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                var allowed = character is >= '0' and <= '9' or ',' or '.' or '-' or '+';
                if (!allowed)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// «Табельный номер»: поле помечено в метаданных как String, но по смыслу
        /// это целое число — проверяем и по имени, и по колонке.
        /// </summary>
        private static bool IsPersonnelNumberField(MetadataField field)
        {
            return string.Equals(field.DbColumnName, "personnel_number", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.Name, "Табельный номер", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIntegerField(MetadataField field)
        {
            return string.Equals(field.FieldType, "Int", StringComparison.OrdinalIgnoreCase) ||
                   IsPersonnelNumberField(field);
        }

        /// <summary>
        /// Страховка на сохранении: даже если текст попал в поле в обход фильтра
        /// (вставка, программная подстановка), запись не уйдёт с буквами в числе.
        /// </summary>
        private bool ValidateNumericFields()
        {
            var invalid = new List<string>();

            foreach (var field in GetEditableFields())
            {
                if (!_controls.TryGetValue(field.Name, out var control) || control is not TextBox textBox)
                    continue;

                var text = textBox.Text?.Trim() ?? string.Empty;
                if (text.Length == 0)
                    continue;

                if (IsIntegerField(field))
                {
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        invalid.Add($"{GetFieldLabel(field)} — целое число");
                }
                else if (string.Equals(field.FieldType, "Decimal", StringComparison.OrdinalIgnoreCase) &&
                         !decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out _) &&
                         !decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    invalid.Add($"{GetFieldLabel(field)} — число");
                }
            }

            if (invalid.Count == 0)
                return true;

            MessageBox.Show(
                "Проверьте числовые поля:\n• " + string.Join("\n• ", invalid),
                _catalog.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        private void ApplyCatalogFieldState(MetadataField field, Control inputControl)
        {
            if (!IsCashDeskBalanceField(field))
                return;

            if (inputControl is TextBox textBox)
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                    textBox.Text = "0";

                textBox.IsReadOnly = true;
            }

            inputControl.IsEnabled = false;
            inputControl.ToolTip = "Поле рассчитывается системой и временно недоступно для ручного изменения.";
            inputControl.SetResourceReference(Control.BackgroundProperty, "AppReadOnlyBackgroundBrush");
        }

        private bool IsCashDeskBalanceField(MetadataField field)
        {
            if (!string.Equals(_catalog.Name, "Кассы", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(field.Name, "Начальный остаток", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.Name, "Текущий остаток", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.DbColumnName, "initial_balance", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.DbColumnName, "current_balance", StringComparison.OrdinalIgnoreCase);
        }

        private bool ShouldDefaultActiveField(MetadataField field)
        {
            return string.Equals(_catalog.ObjectType, "Catalog", StringComparison.OrdinalIgnoreCase) &&
                   IsActiveField(field);
        }

        private static bool IsActiveField(MetadataField field)
        {
            return string.Equals(field.Name, "Активен", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.Name, "Активна", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.DbColumnName, "is_active", StringComparison.OrdinalIgnoreCase);
        }
        private async Task<string> GenerateNextCatalogCodeAsync(MetadataField codeField)
        {
            var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
            var existingCodes = rows
                .Select(row => GetCodeValue(row, codeField))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var numericCodes = existingCodes
                .Select(value => new
                {
                    Text = value,
                    IsNumeric = long.TryParse(value, out var number),
                    Number = long.TryParse(value, out var numberValue) ? numberValue : 0
                })
                .Where(item => item.IsNumeric)
                .ToList();

            var nextNumber = numericCodes.Count == 0
                ? 1
                : numericCodes.Max(item => item.Number) + 1;

            var width = numericCodes
                .Where(item => item.Text.Length > item.Number.ToString().Length || item.Text.StartsWith("0", StringComparison.Ordinal))
                .Select(item => item.Text.Length)
                .DefaultIfEmpty(0)
                .Max();

            if (IsOrganizationsCatalog())
                width = 0;

            string BuildCode(long number) => width > 0
                ? number.ToString().PadLeft(width, '0')
                : number.ToString();

            var code = BuildCode(nextNumber);
            var usedCodes = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            while (usedCodes.Contains(code))
            {
                nextNumber++;
                code = BuildCode(nextNumber);
            }

            return code;
        }

        private static string GetCodeValue(IReadOnlyDictionary<string, object> row, MetadataField codeField)
        {
            foreach (var key in GetCodeLookupKeys(codeField))
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value.ToString() ?? string.Empty;
            }

            var pair = row.FirstOrDefault(item => IsCodeKey(item.Key));
            return pair.Value?.ToString() ?? string.Empty;
        }

        private static IEnumerable<string> GetCodeLookupKeys(MetadataField codeField)
        {
            yield return codeField.Name;
            yield return codeField.DbColumnName;
            yield return "Код";
            yield return "code";
            yield return "Code";
        }

        private static bool IsCatalogCodeField(MetadataField field) =>
            IsCodeKey(field.Name) || IsCodeKey(field.DbColumnName);

        private static bool IsCodeKey(string? value) =>
            value?.Trim().Equals("Код", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("code", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("Код организации", StringComparison.OrdinalIgnoreCase) == true ||
            value?.Trim().Equals("organization_code", StringComparison.OrdinalIgnoreCase) == true;
        private async Task AutoFillFromEmployee(Guid employeeId)
        {
            try
            {
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");

                if (employeeCatalog == null) return;

                var employees = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
                var employee = employees.FirstOrDefault(e => e.ContainsKey("Id") && Guid.Parse(e["Id"].ToString()) == employeeId);

                if (employee == null) return;

                // Заполняем ФИО
                if (_controls.ContainsKey("ФИО") && _controls["ФИО"] is TextBox fioBox)
                {
                    if (employee.ContainsKey("ФИО"))
                        fioBox.Text = employee["ФИО"].ToString();
                    else if (employee.ContainsKey("Наименование"))
                        fioBox.Text = employee["Наименование"].ToString();
                    else if (employee.ContainsKey("name"))
                        fioBox.Text = employee["name"].ToString();
                }

                // Заполняем Должность
                if (_controls.ContainsKey("Должность") && _controls["Должность"] is TextBox positionBox)
                {
                    if (employee.ContainsKey("Должность"))
                        positionBox.Text = employee["Должность"].ToString();
                }

                // Заполняем Телефон
                if (_controls.ContainsKey("Телефон") && _controls["Телефон"] is TextBox phoneBox)
                {
                    if (employee.ContainsKey("Телефон"))
                        phoneBox.Text = employee["Телефон"].ToString();
                }

                // Если есть поле "Участок" (Reference) - его тоже можно заполнить
                if (_controls.ContainsKey("Участок") && employee.ContainsKey("Участок"))
                {
                    var siteId = employee["Участок"].ToString();

                    if (_controls["Участок"] is ReferencePickerControl sitePicker)
                    {
                        var selectedSite = sitePicker.ComboBox.Items
                            .OfType<ReferenceItem>()
                            .FirstOrDefault(i => i.Id.ToString() == siteId);
                        if (selectedSite != null)
                            sitePicker.SelectedReferenceItem = selectedSite;
                    }
                    else if (_controls["Участок"] is ComboBox siteBox)
                    {
                        var selectedSite = siteBox.Items
                            .OfType<ReferenceItem>()
                            .FirstOrDefault(i => i.Id.ToString() == siteId);
                        if (selectedSite != null)
                            siteBox.SelectedItem = selectedSite;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoFillFromEmployee error: {ex.Message}");
            }
        }
        /// <summary>
        /// Проверка и сбор значений карточки. Саму запись выполняет вызывающая сторона
        /// (MetadataService.AddCatalogItemAsync), она и запирает контекст.
        /// </summary>
        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isSaving)
                return;
            _isSaving = true;
            try
            {
                // Ждём завершения построения формы, чтобы не выполнять команды
                // БД параллельно с ещё не законченной загрузкой (Npgsql:
                // «A command is already in progress»).
                if (_formBuilt.Task.IsCompletedSuccessfully == false)
                {
                    Cursor = System.Windows.Input.Cursors.Wait;
                    await _formBuilt.Task;
                    Cursor = null;
                }

                if (!await EnsureAccountPairAccountsExistAsync())
                    return;

                foreach (var field in GetEditableFields())
                {
                    if (!_controls.TryGetValue(field.Name, out var control)) continue;

                    _itemData[field.Name] = ReadFieldValue(field, control) ?? DBNull.Value;
                }

                NormalizeCatalogItemDataBeforeSave();
                NormalizeAccountPairsCurrencyBeforeSave();

                if (!ValidateNumericFields())
                    return;

                if (!ValidateRequiredFields())
                    return;

                if (!ValidateOrganizationCountry())
                    return;

                if (!ValidateAccountPairsCurrency())
                    return;

                if (!ValidateChartOfAccountsCode())
                    return;

                if (!await ValidateChartOfAccountsCodeUniqueAsync())
                    return;

                if (!ValidateChartOfAccountsCatalogLinks())
                    return;

                if (!await ValidateInventoryEntryRequired())
                    return;

                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSaving = false;
            }
        }

        /// <summary>Не закрывает пару с неизвестным счётом; предлагает создать его штатной формой.</summary>
        private async Task<bool> EnsureAccountPairAccountsExistAsync()
        {
            if (!IsAccountPairsCatalog())
                return true;

            async Task<AccountAnalyticsRegistry> ReloadAccountsAsync()
            {
                var fresh = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                // Пикеры держат общий экземпляр реестра в обработчиках «?».
                // Обновляем список на месте, чтобы новые счета были видны без закрытия пары.
                if (_accountAnalytics != null)
                {
                    _accountAnalytics.Accounts.Clear();
                    _accountAnalytics.Accounts.AddRange(fresh.Accounts);
                }
                return fresh;
            }

            var accounts = await ReloadAccountsAsync();
            foreach (var field in GetEditableFields().Where(field =>
                         field.DbColumnName is "debit_account" or "credit_account"))
            {
                if (!_controls.TryGetValue(field.Name, out var control))
                    continue;
                var value = ReadFieldValue(field, control);
                var code = value?.ToString()?.Trim() ?? string.Empty;
                if (value == DBNull.Value || code.Length == 0)
                    continue; // Пустое поле проверяется штатной проверкой обязательности.

                var account = accounts.FindAccount(value);
                if (account != null)
                {
                    AccountPickerControlFactory.SetSelectedAccount(control, account);
                    continue;
                }

                var answer = MessageBox.Show(
                    $"Счёт «{code}» не найден в плане счетов (поле «{field.Name}»).\n\n" +
                    "Открыть форму добавления счёта в план счетов?\n" +
                    "Счёт будет создан только после заполнения карточки и нажатия «Сохранить».\n\n" +
                    "«Нет» — вернуться к паре счетов. Вы сможете выбрать другой счёт или добавить его самостоятельно.",
                    "Счёт не найден", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    control.Focus();
                    return false;
                }

                var catalogs = await _metadataService.GetCatalogsAsync();
                var chart = catalogs.FirstOrDefault(item => item.Name.Equals("План счетов", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Справочник «План счетов» не найден.");
                var codeField = chart.Fields.FirstOrDefault(item => item.DbColumnName == "code");
                var initial = new Dictionary<string, object>();
                if (codeField != null && code.Length <= 8 && code.All(char.IsDigit))
                    initial[codeField.Name] = code;
                var dialog = new CatalogItemDialog(chart, _metadataService, initial, isNewRecord: true);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, "Добавление счёта в план счетов") != true)
                    return false;

                var createdId = await _metadataService.AddCatalogItemAsync(chart.Id, dialog.ItemData);
                accounts = await ReloadAccountsAsync();
                var created = accounts.FindAccount(createdId)
                    ?? throw new InvalidOperationException("Счёт сохранён, но не найден при обновлении плана счетов.");
                AccountPickerControlFactory.SetSelectedAccount(control, created);
            }
            return true;
        }

        /// <summary>
        /// Дефолты новой записи. Для расчётных счетов и счетов плана счетов сразу
        /// выбирается валюта по умолчанию — базовая (киргизский сом): счёт не должен
        /// сохраняться без валюты.
        /// </summary>
        private async Task<Dictionary<string, object>> ApplyNewRecordDefaultsAsync(
            Dictionary<string, object> existingData,
            IReadOnlyDictionary<string, MetadataObject> catalogsDict)
        {
            if (!_isNewRecord)
                return existingData;

            var result = existingData == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(existingData, StringComparer.OrdinalIgnoreCase);

            var currencyColumn = GetDefaultCurrencyColumn();
            if (currencyColumn == null)
                return result;

            var currencyField = GetEditableFields().FirstOrDefault(field =>
                string.Equals(field.DbColumnName, currencyColumn, StringComparison.OrdinalIgnoreCase));

            if (currencyField == null || HasMeaningfulValue(result, currencyField))
                return result;

            var defaultCurrencyId = await ResolveDefaultCurrencyIdAsync(catalogsDict);
            if (!string.IsNullOrWhiteSpace(defaultCurrencyId))
                result[currencyField.Name] = defaultCurrencyId;

            return result;
        }

        /// <summary>
        /// Колонка валюты, заполняемая по умолчанию для новых записей:
        /// у расчётного счёта — <c>currency_id</c>, у счёта плана счетов —
        /// <c>account_currency_id</c> («Валюта счета»). Для остальных справочников
        /// дефолт валюты не задаётся.
        /// </summary>
        private string? GetDefaultCurrencyColumn()
        {
            if (IsBankAccountsCatalog() || IsCashDesksCatalog())
                return "currency_id";

            if (IsChartOfAccountsCatalog())
                return "account_currency_id";

            return null;
        }

        private bool IsBankAccountsCatalog() =>
            string.Equals(_catalog.TableName, "catalog_bank_accounts", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.Name, "Расчетные счета организаций", StringComparison.OrdinalIgnoreCase);

        private bool IsCashDesksCatalog() =>
            string.Equals(_catalog.TableName, "catalog_cash_desks", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.Name, "Кассы", StringComparison.OrdinalIgnoreCase);

        /// <summary>Справочник «Вид операции ТМЗ» (виды операций с ТМЗ).</summary>
        private bool IsInventoryEntryCatalog() =>
            string.Equals(_catalog.Name, InventoryDocumentModel.EntryCatalogName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, InventoryDocumentModel.EntryCatalogTable, StringComparison.OrdinalIgnoreCase);

        /// <summary>Справочник материалов: у него есть счета и международные коды.</summary>
        private bool IsMaterialsCatalog() =>
            string.Equals(_catalog.TableName, "catalog_materials", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.Name, "Справочник материалов", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Поле международной классификации (код товара/услуги ТН ВЭД). Такое поле
        /// показывается отдельным блоком под остальными реквизитами материала.
        /// </summary>
        private static bool IsInternationalCodeField(MetadataField field) =>
            string.Equals(field.DbColumnName, "tnved_code", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("ТНВЭД", StringComparison.OrdinalIgnoreCase) ||
            field.Name.Contains("ТН ВЭД", StringComparison.OrdinalIgnoreCase);

        /// <summary>Блок международных кодов нужен, только если в нём есть поля.</summary>
        private void UpdateExtraBlockVisibility()
        {
            ExtraBlockGroup.Visibility = ExtraBlockFields.Children.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ApplyMaterialFieldHints(MetadataField field, Control inputControl)
        {
            if (!IsMaterialsCatalog() || !IsInternationalCodeField(field) || inputControl is not TextBox textBox)
                return;

            textBox.MaxLength = field.Length > 0 ? field.Length : 30;
            textBox.ToolTip = "Код товара или услуги по международной классификации (ТН ВЭД)";
        }

        private async Task<string?> ResolveDefaultCurrencyIdAsync(
            IReadOnlyDictionary<string, MetadataObject> catalogsDict)
        {
            if (catalogsDict == null || !catalogsDict.TryGetValue("Справочник валют", out var currenciesCatalog))
                return null;

            var rows = await _metadataService.GetCatalogDataAsync(currenciesCatalog.Id);
            if (rows.Count == 0)
                return null;

            // 1) базовая валюта (киргизский сом), 2) страховка по коду KGS
            var baseRow = rows.FirstOrDefault(row => IsTrueFlag(GetRowValue(row, "is_base", "Основная", "Базовая")));
            var row = baseRow ?? rows.FirstOrDefault(candidate =>
                string.Equals(GetRowValue(candidate, "code", "Код")?.ToString()?.Trim(), "KGS",
                    StringComparison.OrdinalIgnoreCase));

            return row == null ? null : GetRowValue(row, "Id")?.ToString();
        }

        private static object? GetRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value;
            }

            return null;
        }

        private static bool IsTrueFlag(object? value) =>
            value switch
            {
                bool flag => flag,
                string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               text == "1" ||
                               text.Equals("Да", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

        private static bool HasMeaningfulValue(IReadOnlyDictionary<string, object> data, MetadataField field)
        {
            object? value = null;
            if (!string.IsNullOrWhiteSpace(field.Name))
                data.TryGetValue(field.Name, out value);

            if (value == null && !string.IsNullOrWhiteSpace(field.DbColumnName))
                data.TryGetValue(field.DbColumnName, out value);

            return value switch
            {
                null => false,
                DBNull => false,
                string text => !string.IsNullOrWhiteSpace(text),
                _ => true
            };
        }

        private bool ShouldStoreReferenceDisplayValue(MetadataField field)
        {
            return string.Equals(_catalog.Name, "Организации", StringComparison.OrdinalIgnoreCase) &&
                   (field.Name.Equals("Банк", StringComparison.OrdinalIgnoreCase) ||
                    field.DbColumnName.Equals("bank_name", StringComparison.OrdinalIgnoreCase) ||
                    field.Name.Equals("Расчетный счет", StringComparison.OrdinalIgnoreCase) ||
                    field.DbColumnName.Equals("bank_account", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeClosingModuleChoice(string? value) => value?.Trim() switch
        {
            "2" or "3" or "9" or "Finance" or "Финансы" => "3",
            "4" or "Sales" or "Сбыт" => "4",
            "6" or "Inventory" or "Учет материальных ценностей" or "УМЦ" or "ТМЦ" or "Материалы" => "6",
            "7" or "FixedAssets" or "Основные средства" or "ОС" => "7",
            "8" or "Вспомогательное производство" => "8",
            "12" or "Сырье" or "Сырьё" or "RawMaterials" => "12",
            "66" or "Меню" => "66",
            _ => value?.Trim() ?? string.Empty
        };

        private sealed record ChoiceItem(string Code, string Display, params string[] LookupKeys)
        {
            public bool Matches(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return false;

                return Code.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                       Display.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                       LookupKeys.Any(key => key.Equals(value, StringComparison.OrdinalIgnoreCase));
            }

            public override string ToString() => Display;
        }

        private object GetValueFromControl(Control control, MetadataField field)
        {
            switch (field.FieldType)
            {
                case "Int":
                    if (control is TextBox tbInt && int.TryParse(tbInt.Text, out int intVal))
                        return intVal;
                    return null;

                case "Decimal":
                    if (control is TextBox tbDec && decimal.TryParse(tbDec.Text, out decimal decVal))
                        return decVal;
                    return null;

                case "DateTime":
                    if (control is DatePicker dp)
                        return dp.SelectedDate;
                    return null;

                case "Bool":
                    if (control is CheckBox cb)
                        return cb.IsChecked ?? false;
                    return null;

                default:
                    if (control is TextBox tb)
                        return tb.Text;
                    if (control is ComboBox combo)
                    {
                        if (combo.SelectedItem is ChoiceItem ci)
                            return ci.Code;
                        if (!string.IsNullOrWhiteSpace(combo.Text))
                            return combo.Text.Trim();
                    }
                    return null;
            }
        }

        /// <summary>
        /// Значение поля из его контрола — ровно то, что уйдёт в запись. Вынесено
        /// из OnSaveClick, чтобы проверка обязательных полей смотрела на те же
        /// значения, а не на текст контрола.
        /// </summary>
        private object? ReadFieldValue(MetadataField field, Control control)
        {
            if (control is UserControl && AccountPickerControlFactory.GetSelectedAccount(control) != null)
                return AccountPickerControlFactory.GetSelectedAccountValue(field, control);

            // Счёт мог быть сохранён кодом, которого нет в плане счетов (старые
            // карточки материалов). Пикер такой код не узнаёт, но терять его при
            // сохранении нельзя — отдаём введённый текст.
            if (control is UserControl and not ReferencePickerControl)
            {
                var accountCode = AccountPickerControlFactory.GetAccountCode(control);
                if (!string.IsNullOrWhiteSpace(accountCode))
                    return accountCode;
            }

            if (control is ReferencePickerControl referencePicker)
            {
                return ShouldStoreReferenceDisplayValue(field)
                    ? referencePicker.SelectedReferenceItem?.DisplayName ?? referencePicker.ComboBox.Text ?? string.Empty
                    : referencePicker.SelectedReferenceItem?.Id.ToString() ?? string.Empty;
            }

            if (control is ComboBox comboBox)
            {
                if (comboBox.SelectedItem is ReferenceItem selectedItem)
                    return selectedItem.Id.ToString();
                if (comboBox.SelectedItem is ChoiceItem choiceItem)
                    return choiceItem.Code;
                return comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? string.Empty;
            }

            return GetValueFromControl(control, field);
        }

        /// <summary>
        /// Обязательные поля, которые есть в форме, должны быть заполнены до закрытия
        /// диалога. Иначе INSERT уходит без значения, и PostgreSQL отвечает 23502
        /// «значение NULL в столбце "name" нарушает ограничение NOT NULL»
        /// (лог 26.10.2026 13:33 — «Пары счетов», пустой «Вид расчета»). Здесь же
        /// видно, что именно не заполнено, и введённое не теряется.
        /// </summary>
        private bool ValidateRequiredFields()
        {
            var emptyFields = new List<MetadataField>();

            foreach (var field in GetEditableFields())
            {
                if (!field.IsRequired)
                    continue;

                if (!_controls.TryGetValue(field.Name, out var control))
                    continue;

                if (IsEmptyFieldValue(ReadFieldValue(field, control)))
                    emptyFields.Add(field);
            }

            if (emptyFields.Count == 0)
                return true;

            MessageBox.Show(
                "Заполните обязательные поля:\n• " +
                string.Join("\n• ", emptyFields.Select(GetFieldLabel)),
                _catalog.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            if (_controls.TryGetValue(emptyFields[0].Name, out var firstControl))
            {
                firstControl.Focus();
                if (firstControl is TextBox textBox)
                    textBox.SelectAll();
            }

            return false;
        }

        private static bool IsEmptyFieldValue(object? value)
        {
            return value == null ||
                   value == DBNull.Value ||
                   (value is string text && string.IsNullOrWhiteSpace(text));
        }

        private bool ValidateChartOfAccountsCode()
        {
            if (!IsChartOfAccountsCatalog())
                return true;

            var code = GetItemText("Код", "code");
            if (code.Length == 0 ||
                code.Length > 8 ||
                code.Any(character => character is < '0' or > '9'))
            {
                MessageBox.Show(
                    "Код счета должен содержать от 1 до 8 цифр.",
                    "План счетов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                if (_controls.TryGetValue("Код", out var control) && control is TextBox codeBox)
                {
                    codeBox.Focus();
                    codeBox.SelectAll();
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// План счетов: не даём добавить второй счёт с тем же кодом.
        /// Проверяем до закрытия формы, чтобы пользователь исправил код,
        /// а не получал ошибку уже после сохранения.
        /// </summary>
        private async Task<bool> ValidateChartOfAccountsCodeUniqueAsync()
        {
            if (!IsChartOfAccountsCatalog() || !_isNewRecord)
                return true;

            var code = GetItemText("Код", "code").Trim();
            if (code.Length == 0)
                return true;

            var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
            var exists = rows.Any(row =>
            {
                var value = row.GetValueOrDefault("Код") ?? row.GetValueOrDefault("code");
                return string.Equals(value?.ToString()?.Trim(), code, StringComparison.OrdinalIgnoreCase);
            });

            if (!exists)
                return true;

            MessageBox.Show(
                $"Счёт с кодом «{code}» уже существует. Укажите другой код счёта.",
                "План счетов",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            if (_controls.TryGetValue("Код", out var control) && control is TextBox codeBox)
            {
                codeBox.Focus();
                codeBox.SelectAll();
            }

            return false;
        }

        private bool ValidateChartOfAccountsCatalogLinks()
        {
            if (!string.Equals(_catalog.Name, "План счетов", StringComparison.OrdinalIgnoreCase))
                return true;

            var accountCode = GetItemText("Код", "code");
            if (!IsAccountWithoutCatalogLinks(accountCode))
                return true;

            var linkedFields = GetEditableFields()
                .Where(IsChartOfAccountsCatalogLinkField)
                .Where(field => TryGetBool(GetItemValue(field.Name), out var isLinked) && isLinked)
                .Select(field => field.Name)
                .ToList();

            if (linkedFields.Count == 0)
                return true;

            MessageBox.Show(
                $"Счета класса 6 и выше не могут быть связаны со справочниками.\n\n" +
                $"Счет {accountCode} ведется сам по себе.\n" +
                $"Отключите признаки связи: {string.Join(", ", linkedFields)}.",
                "План счетов",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }

        /// <summary>Проверка обязательных полей при добавлении в «Вид операции ТМЗ».</summary>
        private async Task<bool> ValidateInventoryEntryRequired()
        {
            if (!IsInventoryEntryCatalog())
                return true;

            var name = GetItemText("Наименование", "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "Заполните поле «Наименование».",
                    InventoryDocumentModel.EntryCatalogName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                if (_controls.TryGetValue("Наименование", out var control) && control is TextBox nameBox)
                {
                    nameBox.Focus();
                    nameBox.SelectAll();
                }

                return false;
            }

            // Поле «Код» генерируется автоматически при создании формы.
            // Если по какой-то причине осталось пустым — пытаемся сгенерировать сейчас.
            var codeField = GetEditableFields().FirstOrDefault(IsCatalogCodeField);
            if (codeField != null)
            {
                var code = GetItemText(codeField.Name, codeField.DbColumnName);
                if (string.IsNullOrWhiteSpace(code))
                {
                    var generated = await GenerateNextCatalogCodeAsync(codeField);
                    if (!string.IsNullOrWhiteSpace(generated))
                    {
                        if (_controls.TryGetValue(codeField.Name, out var codeControl) &&
                            codeControl is TextBox codeBox)
                        {
                            codeBox.Text = generated;
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Не удалось сгенерировать код для поля «{codeField.Name}». Введите код вручную.",
                            InventoryDocumentModel.EntryCatalogName,
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        if (_controls.TryGetValue(codeField.Name, out var codeControl2) &&
                            codeControl2 is TextBox codeBox2)
                        {
                            codeBox2.Focus();
                            codeBox2.SelectAll();
                        }

                        return false;
                    }
                }
            }

            // «Документ ТМЗ» и «Вид движения» — выпадающие списки со значением по умолчанию,
            // «Активен» установлен по умолчанию. Дополнительных проверок не требуется.
            return true;
        }

        private static bool IsChartOfAccountsCatalogLinkField(MetadataField field)
        {
            return field.DbColumnName is "link_organizations" or
                   "link_employees" or
                   "link_currencies" or
                   "link_personal_accounts" or
                   "link_materials" or
                   "link_construction_objects" or
                   "link_sites";
        }

        private static bool IsAccountWithoutCatalogLinks(string? accountCode)
        {
            var firstDigit = accountCode?.Trim().FirstOrDefault(char.IsDigit);
            return firstDigit >= '6' && firstDigit <= '9';
        }
        private void ApplyOrganizationCountryDefault(MetadataField field, Control inputControl)
        {
            if (!_isNewRecord || !IsOrganizationsCatalog() || !IsOrganizationCountryField(field))
                return;

            if (inputControl is not ReferencePickerControl picker || picker.ComboBox.SelectedItem != null)
                return;

            var country = picker.ComboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(IsDefaultOrganizationCountry);

            if (country != null)
                picker.SelectedReferenceItem = country;
        }

        private bool ValidateOrganizationCountry()
        {
            if (!IsOrganizationsCatalog())
                return true;

            var field = GetEditableFields().FirstOrDefault(IsOrganizationCountryField);
            if (field == null)
                return true;

            var value = GetItemValue(field.Name) ?? GetItemValue(field.DbColumnName);
            if (value != null && value != DBNull.Value && !string.IsNullOrWhiteSpace(value.ToString()))
                return true;

            MessageBox.Show("Заполните обязательное поле \"Государство\".", "Организации",
                MessageBoxButton.OK, MessageBoxImage.Warning);

            if (_controls.TryGetValue(field.Name, out var control))
                control.Focus();

            return false;
        }

        private static bool IsOrganizationCountryField(MetadataField field)
        {
            return field.Name.Equals("Государство", StringComparison.OrdinalIgnoreCase) ||
                   field.DbColumnName.Equals("country_id", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefaultOrganizationCountry(ReferenceItem item)
        {
            return item.LookupKeys.Any(key =>
                key.Equals("KG", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Кыргызстан", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("Киргиз", StringComparison.OrdinalIgnoreCase));
        }

        private string GetItemText(params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetItemValue(key);
                if (value != null && value != DBNull.Value && !string.IsNullOrWhiteSpace(value.ToString()))
                    return value.ToString()!.Trim();
            }

            return string.Empty;
        }

        private object? GetItemValue(string key)
        {
            if (_itemData.TryGetValue(key, out var value))
                return value;

            var field = _catalog.Fields.FirstOrDefault(item =>
                item.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                item.DbColumnName.Equals(key, StringComparison.OrdinalIgnoreCase));

            if (field != null && _itemData.TryGetValue(field.Name, out value))
                return value;

            return null;
        }

        private static bool TryGetBool(object? value, out bool result)
        {
            if (value is bool boolValue)
            {
                result = boolValue;
                return true;
            }

            if (value == null || value == DBNull.Value)
            {
                result = false;
                return false;
            }

            return bool.TryParse(value.ToString(), out result);
        }
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        private bool IsEmployeesCatalog()
        {
            return string.Equals(_catalog.Name, "Сотрудники (Списочный состав)", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOrganizationsCatalog()
        {
            return string.Equals(_catalog.Name, "Организации", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<MetadataField> GetFieldsInFormOrder()
        {
            if (!IsEmployeesCatalog())
            {
                foreach (var field in _catalog.Fields
                             .OrderBy(NormalizeFieldOrder)
                             .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase))
                    yield return field;

                yield break;
            }

            var desiredOrder = new[]
            {
                "Табельный номер",
                "ФИО",
                "Пол",
                "Семейное положение",
                "Должность (справочник)",
                "Подразделение",
                "Дата рождения",
                "Дата приема",
                "Дата увольнения",
                "Статус",
                "Телефон",
                "Email",
                "ИНН",
                "Паспорт №/ID",
                "Кем выдан",
                "Дата выдачи",
                "Адрес",
                "Примечание"
            };

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fieldsByName = _catalog.Fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Name))
                .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(NormalizeFieldOrder).ThenBy(field => field.Id).First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var fieldName in desiredOrder)
            {
                if (!fieldsByName.TryGetValue(fieldName, out var field))
                    continue;

                emitted.Add(field.Name);
                yield return field;
            }

            foreach (var field in _catalog.Fields
                         .OrderBy(NormalizeFieldOrder)
                         .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (IsEmployeeHiddenTechnicalField(field) || !emitted.Add(field.Name))
                    continue;

                yield return field;
            }
        }

        private static int NormalizeFieldOrder(MetadataField field)
        {
            return field.Order <= 0 ? int.MaxValue : field.Order;
        }

        private static bool IsEmployeeHiddenTechnicalField(MetadataField field)
        {
            return field.Name.Equals("Код", StringComparison.OrdinalIgnoreCase) ||
                   field.Name.Equals("Наименование", StringComparison.OrdinalIgnoreCase) ||
                   field.Name.Equals("Должность (текст)", StringComparison.OrdinalIgnoreCase) ||
                   field.Name.Equals("Активен", StringComparison.OrdinalIgnoreCase) ||
                   field.DbColumnName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                   field.DbColumnName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                   field.DbColumnName.Equals("position_text", StringComparison.OrdinalIgnoreCase) ||
                   field.DbColumnName.Equals("is_active", StringComparison.OrdinalIgnoreCase);
        }
        private List<MetadataField> GetEditableFields()
        {
            var allowedColumns = GetAllowedCatalogColumns();
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<MetadataField>();

            foreach (var field in GetFieldsInFormOrder())
            {
                if (allowedColumns != null &&
                    (string.IsNullOrWhiteSpace(field.DbColumnName) || !allowedColumns.Contains(field.DbColumnName)))
                {
                    continue;
                }

                // Служебный признак налогов («Служебная запись») заполняется системой
                // при первичном наполнении справочника, в форме он не показывается.
                if (IsTaxCatalog() &&
                    string.Equals(field.DbColumnName, "is_system", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(field.DbColumnName) &&
                    !usedColumns.Add(field.DbColumnName))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(field.Name) &&
                    !usedNames.Add(field.Name))
                {
                    continue;
                }

                result.Add(field);
            }

            return result;
        }

        private HashSet<string>? GetAllowedCatalogColumns()
        {
            if (_catalog.Name == "План счетов")
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "account_type",
                    "description",
                    "is_active",
                    "closing_module_code",
                    "analytic_group",
                    "print_mode",
                    "balance_mode",
                    "link_organizations",
                    "link_employees",
                    "link_currencies",
                    "link_personal_accounts",
                    "link_materials",
                    "link_construction_objects",
                    "link_sites",
                    "tax_code",
                    "account_currency_id"
                };
            }

            if (IsAccountPairsCatalog())
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "use_organizations",
                    "use_personnel",
                    "use_currency",
                    "currency_id",
                    "module_code",
                    "debit_account",
                    "credit_account",
                    "use_settlements",
                    "generate_postings",
                    "use_internal_settlements",
                    "is_active",
                    "description"
                };
            }

            return null;
        }
    }

}
```

### BIS.ERP.TESTS/BIS.ERP.TESTS.Core/AccountPairReferenceRefreshScenario.cs

SHA-256: `39d341dfd717dc8660e6eeeff2a81967d9d17bfc5be540e54a56bf9fe6381daf`

```csharp
using System.Reflection;
using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using BIS.ERP.Data;
using BIS.ERP.Services;
using BIS.ERP.Views;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Testing;

/// <summary>Воспроизводит устаревший кэш ссылок без добавления или изменения данных базы.</summary>
public sealed class AccountPairReferenceRefreshScenario : SmokeTestScenarioBase
{
    public override string Code => "account-pair-reference-refresh";
    public override string Name => "Выбор пары: счета вместо GUID после добавления";
    public override string Category => "Интерфейс";
    public override string Description => "Исключает счета выбранной пары из исходного кэша, перезагружает настоящее окно выбора и проверяет расшифровку Дт/Кт и выделение строки. База только для чтения.";
    public override bool SupportsRun => false;
    public override bool SupportsCleanup => false;

    public override async Task<SmokeTestResult> ExecuteAsync(SmokeTestRunOptions options,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = await LoadDatabaseCandidatesAsync(LoadSettings(), progress, cancellationToken);
        var target = candidates.FirstOrDefault(item => item.DatabaseName.StartsWith("bis_OsOO2_", StringComparison.OrdinalIgnoreCase));
        if (target == null) return SmokeTestResult.Failure("Не найдена ОСОО2 для проверки.");
        var completion = new TaskCompletionSource<SmokeTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                ReferenceSelectionDialog? dialog = null;
                try
                {
                    var connection = new NpgsqlConnectionStringBuilder(target.ConnectionString)
                        { Options = "-c default_transaction_read_only=on" };
                    await using var context = new AppDbContext(connection.ToString());
                    var metadata = new MetadataService(context);
                    var catalog = await context.MetadataObjects.Include(item => item.Fields)
                        .SingleAsync(item => item.TableName == "catalog_advance_payments", cancellationToken);
                    var rows = await metadata.GetCatalogDataAsync(catalog.Id);
                    var maps = await ReferenceDisplayHelper.LoadMapsAsync(catalog, metadata);
                    var debit = catalog.Fields.Single(field => field.DbColumnName == "debit_account").Name;
                    var credit = catalog.Fields.Single(field => field.DbColumnName == "credit_account").Name;
                    var code = catalog.Fields.Single(field => field.DbColumnName == "code").Name;
                    var name = catalog.Fields.Single(field => field.DbColumnName == "name").Name;
                    bool HasAccount(Dictionary<string, object> row, string field) =>
                        Guid.TryParse(row.GetValueOrDefault(field)?.ToString(), out var id) && maps[field].ContainsKey(id);
                    var selected = rows.Where(row => HasAccount(row, debit) && HasAccount(row, credit))
                        .OrderByDescending(row => row.GetValueOrDefault(code)?.ToString() == "12").First();
                    var selectedId = Guid.Parse(selected["Id"].ToString()!);
                    var debitId = Guid.Parse(selected[debit].ToString()!);
                    var creditId = Guid.Parse(selected[credit].ToString()!);
                    var expectedDebit = maps[debit][debitId];
                    var expectedCredit = maps[credit][creditId];
                    var stale = maps.ToDictionary(item => item.Key, item => new Dictionary<Guid, string>(item.Value));
                    stale[debit].Remove(debitId);
                    stale[credit].Remove(creditId);
                    dialog = new ReferenceSelectionDialog(rows.Where(row => !Equals(row["Id"], selected["Id"])).ToList(), code, name, stale);
                    dialog.ConfigureCatalogEditing(catalog, metadata);
                    var reload = typeof(ReferenceSelectionDialog).GetMethod("ReloadItemsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    await (Task)reload.Invoke(dialog, new object?[] { selectedId })!;
                    var grid = (DataGrid)dialog.FindName("ItemsGrid");
                    var reloaded = grid.Items.Cast<Dictionary<string, object>>().Single(row => Guid.Parse(row["Id"].ToString()!) == selectedId);
                    if (reloaded[debit]?.ToString() != expectedDebit || reloaded[credit]?.ToString() != expectedCredit)
                        throw new Exception("После перезагрузки Дт/Кт не расшифрованы свежими картами ссылок.");
                    if (!ReferenceEquals(grid.SelectedItem, reloaded)) throw new Exception("Выделение добавленной строки потеряно.");
                    if (Guid.TryParse(reloaded[debit]?.ToString(), out _) || Guid.TryParse(reloaded[credit]?.ToString(), out _))
                        throw new Exception("Вместо названия счёта отображается GUID.");
                    var module = catalog.Fields.Single(field => field.DbColumnName == "module_code").Name;
                    var moduleColumn = grid.Columns.OfType<DataGridTextColumn>().Single(column => column.Header?.ToString() == module);
                    var binding = (Binding)moduleColumn.Binding;
                    foreach (var value in new[] { "3", "Finance", "Финансы" })
                        if (binding.Converter.Convert(value, typeof(string), binding.ConverterParameter, CultureInfo.InvariantCulture)?.ToString() != "Финансы")
                            throw new Exception($"Модуль «{value}» не отображается как «Финансы».");
                    completion.TrySetResult(SmokeTestResult.Success("Новые счета отображаются без GUID после обновления выбора.",
                        $"ОСОО2, пара {selected[code]}; Дт: {expectedDebit}; Кт: {expectedCredit}.",
                        "Проверено настоящее окно и ReloadItemsAsync с заведомо устаревшим кэшем; строка выделена; база read-only.",
                        "Поле остатка: 3 / Finance / Финансы отображаются как Финансы, без изменения сохранённого значения."));
                }
                catch (Exception ex) { completion.TrySetResult(SmokeTestResult.Failure("Обновление ссылок: ошибка.", new[] { DescribeError(ex) })); }
                finally { dialog?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
    }
}
```

