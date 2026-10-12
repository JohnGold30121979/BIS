# Возврат FRX: инструкция от обратного

Документ описывает **обратную операцию** к отключению FRX.
Если нужно вернуть формирование отчётов по макетам FoxPro — здесь пошагово
указано, что и где изменить.

Отключение выполнено в версии **1.1.172**.

---

## Что именно возвращается

| Что | Количество | Объём |
| --- | --- | --- |
**Всего активных отчётов** | 58 | — |
**FRX-отчётов** | **38** | **1 700 013 символов макетов** |
Отчётов ClosedXML | 1 | «Акт сверки (ClosedXML)» |
Нативных отчётов | 19 | — |

Все 38 FRX-отчётов **имеют сохранённый макет** — ничего не потеряно,
данные ждут включения.

### Перечень FRX-отчётов, которые вернутся

**Акты сверки — 18 вариантов:**

| Название | Код |
| --- | --- |
Акт сверки (FRX FoxPro) | `standard.frx.finance.reconciliation.pr-vzp` |
Акт сверки вариант 1 (FRX FoxPro) | `standard.frx.finance.reconciliation.pr-vzp1` |
Акт сверки краткий (FRX FoxPro) | `standard.frx.finance.reconciliation.pr-vzp-short` |
Акт сверки валютный (FRX FoxPro) | `standard.frx.finance.reconciliation.currency` |
Акт сверки валютный краткий (FRX FoxPro) | `...reconciliation.currency-short` |
Акт сверки валютный по материалам (FRX FoxPro) | `...reconciliation.currency-materials` |
Акт сверки по авансам (FRX FoxPro) | `...reconciliation.advance` |
Акт сверки по авансам с валютой (FRX FoxPro) | `...reconciliation.advance-currency` |
Акт сверки по данным сторон (FRX FoxPro) | `...reconciliation.counterparty` |
Акт сверки по данным сторон краткий (FRX FoxPro) | `...counterparty-short` |
Акт сверки по данным сторон с валютой (FRX FoxPro) | `...counterparty-currency` |
Акт сверки по кассе (FRX FoxPro) | `...reconciliation.cash` |
Акт сверки по материалам (FRX FoxPro) | `...reconciliation.materials` |
Акт сверки по счет-фактурам (FRX FoxPro) | `...reconciliation.invoice` |
Акт сверки по счетам (FRX FoxPro) | `...reconciliation.accounts` |
Акт сверки по счетам краткий (FRX FoxPro) | `...accounts-short` |
Акт сверки по товарам (FRX FoxPro) | `...reconciliation.goods` |
Авансовые платежи pr_av1 (FRX FoxPro) | `...finance.advance-payments.pr-av1` |

**Основные средства — 7 отчётов:** ведомость, движение, журнал движения,
карточка, инвентарная передача, ведомость по МОЛ и другие.

**Прочие финансовые — 13 отчётов:** кассовая книга и др.

---

## Быстрый возврат: три шага

Если нужен **полный** возврат, достаточно трёх действий.

### Шаг 1. Главный переключатель

**Файл:** `Services/ReportingFeatures.cs`, **строка 26**

```csharp
// Было
public const bool FrxEnabled = false;

// Стало
public const bool FrxEnabled = true;
```

**Это действие возвращает почти всё:**

| Что вернётся автоматически | Благодаря чему |
| --- | --- |
Списки вариантов акта (18 штук) | Проверка флага в `LoadReconciliationReportVariantsAsync` |
Списки вариантов оборотки | Проверка в `LoadTrialBalanceReportVariantsAsync` |
Пункты способа макета FRX | `HideFrxLayoutKindItem` покажет пункт |
Вкладки «🔗 FRX поля» и «🧱 Нативный макет» | `ApplyLayoutKindVisibility` |
Поле «FRX-шаблон» и загрузка | То же |
Тип отчёта «Макет Visual FoxPro FRX» | `SyncFoxProReportTypeItem` |
Кнопки «Импорт FRX» и «Анализ FoxPro» | `ApplyReportingFeaturesVisibility` |
Признаки FRX-макета в предпросмотре | `HasFoxProTemplate` |
Работа методов печати | `EnsureFrxEnabled` перестанет бросать исключение |
Подмена формата FRX на ClosedXML | `GetSelectedReportOutputFormat` |

### Шаг 2. Пункты формата вывода

**Файл:** `Views/AccountingReportsView.xaml`, **строки 52–53**

Это **единственное место**, где видимость задана в разметке, а не кодом.

```xml
<!-- Было -->
<ComboBoxItem Tag="FoxProExcel" Visibility="Collapsed">FRX FoxPro Excel</ComboBoxItem>
<ComboBoxItem Tag="FoxProPdf" Visibility="Collapsed">FRX FoxPro PDF</ComboBoxItem>

<!-- Стало -->
<ComboBoxItem Tag="FoxProExcel">FRX FoxPro Excel</ComboBoxItem>
<ComboBoxItem Tag="FoxProPdf">FRX FoxPro PDF</ComboBoxItem>
```

### Шаг 3. Пересобрать

```powershell
dotnet build "D:\C#\BIS.ERP\BIS.ERP\BIS.ERP.csproj" -c Debug
```

---

## Полный возврат: удаление проверок из кода

Шаги 1–3 **достаточны** для работы FRX. Проверки в коде при `FrxEnabled = true`
безвредны — они всегда пропускают выполнение дальше.

Но если нужно **полностью очистить код** от следов отключения, ниже
перечислены все 14 мест.

### `Services/ReportingFeatures.cs`

| Строка | Действие |
| --- | --- |
26 | `FrxEnabled = true` |

Файл можно оставить целиком: он полезен как переключатель на будущее.

### `Views/AccountingReportsView.xaml`

| Строки | Действие |
| --- | --- |
52, 53 | Убрать `Visibility="Collapsed"` |

### `Views/AccountingReportsView.xaml.cs`

| Строка | Что находится | Действие |
| --- | --- | --- |
1076 | Блок `if (!ReportingFeatures.FrxEnabled)` в `LoadTrialBalanceReportVariantsAsync` | Удалить блок (строки 1076–1083) |
1080 | `TrialBalanceVariantCombo.IsEnabled = false;` | Внутри удаляемого блока |
1151 | Блок `if (!ReportingFeatures.FrxEnabled)` в `LoadReconciliationReportVariantsAsync` | Удалить блок (строки 1151–1158) |
1657 | `ReportingFeatures.FrxEnabled &&` в `HasFoxProTemplate` | Удалить строку |
1677 | `if (!ReportingFeatures.FrxEnabled && ReportingFeatures.IsFrxFormat(format))` | Удалить блок в `GetSelectedReportOutputFormat` |

**Удаляемый блок 1076–1083** (для ориентира):

```csharp
// При отключённом FRX остаётся только расчётный вариант.
if (!ReportingFeatures.FrxEnabled)
{
    TrialBalanceVariantCombo.ItemsSource = variants;
    TrialBalanceVariantCombo.SelectedIndex = 0;
    TrialBalanceVariantCombo.IsEnabled = false;
    return;
}
```

**Удаляемый блок 1151–1158:**

```csharp
// При отключённом FRX список вариантов не заполняется: остаётся только
// стандартный расчётный вариант, который выводится через ClosedXML.
if (!ReportingFeatures.FrxEnabled)
{
    ReconciliationVariantCombo.ItemsSource = variants;
    ReconciliationVariantCombo.SelectedIndex = 0;
    ReconciliationVariantCombo.IsEnabled = false;
    return;
}
```

**Восстановление `GetSelectedReportOutputFormat`** (строка 1677) —
вернуть исходный однострочный вид:

```csharp
private string GetSelectedReportOutputFormat() =>
    (ReportOutputFormatCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ProgramExcel";
```

### `Views/ReportDesignerWindow.xaml.cs`

| Строка | Что находится | Действие |
| --- | --- | --- |
1338 | `if (!ReportingFeatures.FrxEnabled && ...)` в `SetLayoutKind` | Удалить блок (1338–1342) |
1360 | `ReportingFeatures.FrxEnabled &&` в `ApplyLayoutKindVisibility` | Удалить строку |
1363 | `HideFrxLayoutKindItem();` | Удалить вызов |
1394 | Метод `HideFrxLayoutKindItem` | Удалить метод целиком |

**Удаляемый блок 1338–1342:**

```csharp
// При отключённом FRX способ макета всегда ClosedXML.
if (!ReportingFeatures.FrxEnabled &&
    string.Equals(kind, FoxProLayoutKind, StringComparison.OrdinalIgnoreCase))
{
    kind = ClosedXmlLayoutKind;
}
```

### `Views/ReportPreviewWindow.xaml.cs`

| Строка | Что находится | Действие |
| --- | --- | --- |
231 | `ReportingFeatures.FrxEnabled &&` в `HasFoxProTemplate` | Удалить строку |

### `Views/ConfiguratorWindow.xaml.cs`

| Строка | Что находится | Действие |
| --- | --- | --- |
3195 | Метод `ApplyReportingFeaturesVisibility` | Удалить метод (3195–3207) |
3209 | `if (!ReportingFeatures.FrxEnabled) return;` в `OnImportFrxClick` | Удалить |
— | Вызов `ApplyReportingFeaturesVisibility();` | Удалить вызов в `InitializeAsync` |

### `Services/PrintFormService.cs`

| Строка | Что находится | Действие |
| --- | --- | --- |
1074 | Метод `EnsureFrxEnabled` | Удалить метод |
982 | `EnsureFrxEnabled();` в `ExportTemplatePreview` | Удалить |
1005, 1015, 1025 | `EnsureFrxEnabled();` в трёх перегрузках `ExportReportTemplatePreview` | Удалить |
1037, 1047, 1057 | `EnsureFrxEnabled();` в трёх перегрузках `ExportReportTemplateExcel` | Удалить |

---

## Что проверять после возврата

### Список вариантов акта сверки

Открыть **Бухгалтерская отчётность** → **Акт сверки** → список вариантов.

| Ожидание | Значение |
| --- | --- |
Пунктов в списке | **19** (стандартный + 18 FRX) |
Список активен | Да |
Первый пункт | «Акт сверки» — стандартный расчётный |

### Список вариантов оборотной ведомости

| Ожидание | Значение |
| --- | --- |
Пунктов | Программная ОСВ + FRX-варианты ОСВ |
Список активен | Да |

### Форматы вывода

Всего в списке **9 пунктов** — из них 2 скрыты при отключённом FRX.

| Состояние | Видимых пунктов | Какие |
| --- | --- | --- |
FRX отключён (сейчас) | **7** | Программный Excel, Программный PDF, Нативный Excel, Нативный PDF, ClosedXML Excel, ClosedXML PDF, Налоговый Excel |
FRX включён | **9** | То же плюс «FRX FoxPro Excel» и «FRX FoxPro PDF» |

### Конструктор отчётов

| Проверка | Ожидание |
| --- | --- |
Способ макета | Выбор из двух: ClosedXML и **FRX** |
Вкладка «🔗 FRX поля» | Видна при способе FRX |
Вкладка «🧱 Нативный макет» | Видна при способе FRX |
Вкладка «📊 ClosedXML макет» | Видна при способе ClosedXML |
Поле «FRX-шаблон» | Видно при способе FRX |
Тип отчёта | Есть «Макет Visual FoxPro FRX» |

### Конфигуратор

| Проверка | Ожидание |
| --- | --- |
Кнопка «📁 Импорт FRX» | Видна |
Кнопка «Анализ FoxPro» | Видна |
Пункт меню «Анализ FoxPro-проекта...» | Виден |

### Формирование отчёта

| Проверка | Ожидание |
| --- | --- |
Акт сверки вариант 1 → FRX FoxPro PDF | Формируется |
Организаций в отчёте | Все 5 |
Колонки «Дата» и «АРМ» | Заполнены |
Страниц | 2 (по 15 строк) |

---

## Что было исправлено в FRX до отключения

При возврате эти исправления **сохраняются** — они в коде, не в настройках.

| Исправление | Файл | Метод |
| --- | --- | --- |
Опознание полей строки без префикса | `PrintFormService.cs` | `IsReconciliationRowElement` |
Дата проводки вместо сегодняшней | `PrintFormService.cs` | `BuildFoxVariables`, `ResolveDateVariable`, `IsDateVariableKey` |
Публикация `posting_date` и `module_code` | `PrintFormService.cs` | `BuildReportPreviewDataForRow` |
Разбиение по 15 строк на страницу | `PrintFormService.cs` | `SplitTemplateIntoPages` |
Общая логика построения акта | `ReconciliationActBuilder.cs` | весь файл |

**Важно:** исправление пагинации учитывает `LayoutNormalized` и работает
только с упакованными макетами. Обычные макеты делятся методом `SplitByHeight`.

---

## Известные ограничения FRX при возврате

| Ограничение | Состояние |
| --- | --- |
Повтор шапки колонок на страницах акта | **Не сделан**: определение шапки в упакованном макете ненадёжно |
Разбиение строк по 15 на страницу | Работает, число задаётся константой `rowsPerPage` |
FRX не поддерживает многорядные шапки | Ограничение модели |
Движения грузятся без фильтра по счетам пары | Только по организации и датам |

Это те же ограничения, что были до отключения — возврат их не устраняет.

---

## Альтернатива полному возврату

Если нужен **не весь FRX, а один конкретный отчёт**, есть более точечный путь.

### Вариант A. Перевести один FRX-отчёт на ClosedXML

1. Открыть отчёт в конструкторе.
2. Собрать разметку на вкладке «📊 ClosedXML макет».
3. Сохранить отчёт.

Отчёт станет обычным ClosedXML-отчётом и заработает в форматах
«ClosedXML Excel» и «ClosedXML PDF» — **при выключенном FRX**.

Так уже сделан «Акт сверки (ClosedXML)».

### Вариант B. Включить FRX для одного вида отчётов

В `ReportingFeatures.cs` можно заменить константу на свойство с условием:

```csharp
public const bool FrxEnabled = false;

// Вместо константы — проверка по коду отчёта:
public static bool IsFrxAllowed(string? reportCode) =>
    FrxEnabled || (reportCode?.StartsWith("standard.frx.finance.reconciliation") ?? false);
```

Это позволит включить FRX только для актов сверки, оставив остальное
на ClosedXML. Потребуется передавать код отчёта в места проверок.

---

## Сводка правок отключения

| Файл | Строк изменено | Характер |
| --- | --- | --- |
`Services/ReportingFeatures.cs` | создан, 46 | Переключатель |
`Views/AccountingReportsView.xaml` | 52, 53 | `Visibility="Collapsed"` |
`Views/AccountingReportsView.xaml.cs` | 1076–1083, 1151–1158, 1657, 1677 | Проверки флага |
`Views/ReportDesignerWindow.xaml.cs` | 1338–1342, 1360, 1363, 1394–1407 | Проверки + метод |
`Views/ReportPreviewWindow.xaml.cs` | 231 | Проверка флага |
`Views/ConfiguratorWindow.xaml.cs` | 3195–3207, 3209, вызов | Метод + проверка |
`Services/PrintFormService.cs` | 982, 1005, 1015, 1025, 1037, 1047, 1057, 1074–1085 | Метод + 7 вызовов |

**Итого: 7 файлов, 1 новый, 14 мест проверки.**

---

## Связанные документы

| Документ | О чём |
| --- | --- |
`Docs/Отключение_FRX.md` | Прямая инструкция: как отключить |
`Docs/Журнал_правок_три_сессии.md` | Что и где менялось в трёх сессиях |
`Docs/Акты_сверки_устройство_и_дефекты.md` | Устройство актов, найденные дефекты |
`Docs/Правила_полос_FRX_по_шагам.md` | Механика полос FRX |
`Docs/ClosedXml_итоги_и_план.md` | Состояние ClosedXML |
