# Выделение узла в дереве навигации: темы, кисти, механика отрисовки

Дата: 2026-11-10. Репозиторий: `D:\C#\BIS.ERP\BIS.ERP` (WPF, .NET 8).
Режим: только чтение. Единственный созданный файл — этот документ.

> **Главное, что нужно знать до правки:** ключи `AppNavigationSelectionBrush`,
> `AppNavigationSelectionTextBrush`, `AppNavigationSelectionAccentBrush` **уже есть**
> во всех трёх темах и уже подключены в `Views/MainWorkWindow.xaml`. Это
> незакоммиченные изменения рабочего дерева (`git status`: `M Themes/*.xaml`,
> `M Views/MainWorkWindow.xaml`; mtime 10.11.2026 11:56). Реально отсутствует
> только отдельный ключ для hover. Подробности — раздел 4.

---

## 1. Файлы тем

| Файл | Имя темы | Роль |
|---|---|---|
| `Themes/Default.xaml` | `Default` («Светлая») | базовая тема, подключена в `App.xaml:10` |
| `Themes/Dark.xaml` | `Dark` («Темная») | самостоятельный словарь |
| `Themes/Dracula.xaml` | `Dracula` | `ResourceDictionary.MergedDictionaries` → `/Themes/Dark.xaml` (`Themes/Dracula.xaml:4-6`), сверху ~45 своих кистей |

Переключаются все три: `Services/ThemeService.cs:17-19` регистрирует
`Default`/`Dark`/`Dracula`, `GetNextTheme` (`:116-118`) даёт цикл
Default → Dark → Dracula → Default. `ThemeService.Apply` (`:64-70`) очищает
`Application.Current.Resources.MergedDictionaries` и кладёт один словарь.

Следствие наследования: **Dracula получает все ключи Dark, которые сама не
переопределила**. Пропущенный в Dracula ключ молча унаследуется от Dark
(так сейчас и происходит с `SystemColors.InactiveSelectionHighlightBrushKey`:
в Dracula он равен dark-овскому `#2878B8`). Новые ключи надо добавлять в
Dracula обязательно, иначе значение «приедет» из тёмной темы.

---

## 2. Существующие ключи и их фактические значения

| Ключ | Default | Dark | Dracula |
|---|---|---|---|
| `AppSidebarBackgroundBrush` | `#2C3E50` (`:7`) | `#1E1E2A` (`:7`) | `#282A36` (`:10`) |
| `AppSurfaceBrush` | `#FFFFFF` | `#24283B` | `#282A36` |
| `AppAccentBrush` | `#3498DB` (`:57`) | `#3498DB` (`:57`) | `#BD93F9` (`:51`) |
| `AppNavigationForegroundBrush` | `#FFFFFF` (`:52`) | `#F4F7FB` (`:52`) | `#F8F8F2` (`:46`) |
| `AppNavigationMutedForegroundBrush` | `#C4D4E4` | `#B8C7D9` | `#DDE1F4` |
| `AppHighlightBrush` | `#34495E` (`:59`) | `#2A3A4A` (`:59`) | `#44475A` (`:53`) |
| `AppHighlightColor` | `#34495E` (`:60`) | `#2A3A4A` (`:60`) | `#44475A` (`:54`) |
| `AppSelectionBrush` / `Text` | `#2F80C9` / `#FFFFFF` | `#2878B8` / `#FFFFFF` | `#BD93F9` / `#191A21` |
| `SystemColors.HighlightBrushKey` | `#2F80C9` (`:24`) | `#2878B8` (`:24`) | нет → из Dark `#2878B8` |
| `SystemColors.HighlightTextBrushKey` | `#FFFFFF` (`:25`) | `#FFFFFF` (`:25`) | нет → `#FFFFFF` |
| `SystemColors.InactiveSelectionHighlightBrushKey` | `#2F80C9` (`:26`) | `#2878B8` (`:26`) | нет → `#2878B8` |
| `…InactiveSelectionHighlightTextBrushKey` | `#FFFFFF` (`:27`) | `#FFFFFF` (`:27`) | нет → `#FFFFFF` |
| `AppNavigationSelectionBrush` | `#2C79B8` (`:70`) | `#2C79B8` (`:64`) | `#4F5D8A` (`:58`) |
| `AppNavigationSelectionColor` | `#2C79B8` (`:71`) | `#2C79B8` (`:65`) | `#4F5D8A` (`:59`) |
| `AppNavigationSelectionTextBrush` | `#FFFFFF` (`:72`) | `#FFFFFF` (`:66`) | `#FFFFFF` (`:60`) |
| `AppNavigationSelectionTextColor` | `#FFFFFF` (`:73`) | `#FFFFFF` (`:67`) | `#FFFFFF` (`:61`) |
| `AppNavigationSelectionAccentBrush` | `#64B5F6` (`:74`) | `#64B5F6` (`:68`) | `#BD93F9` (`:62`) |
| `AppNavigationHoverBrush` | **нет** | **нет** | **нет** |

Кисти `AppHighlightBrush`/`AppHighlightColor` в коде используются ровно в одном
месте — триггер наведения дерева навигации (`Views/MainWorkWindow.xaml:169`);
больше нигде (проверено поиском по `*.xaml`/`*.cs`).

---

## 3. Чем на самом деле закрашивается выделение

### 3.1. Дефолтный шаблон TreeViewItem (.NET, тема Aero2)

Источник: `dotnet/wpf`, `src/Microsoft.DotNet.Wpf/src/Themes/XAML/TreeViewItem.xaml`
([raw](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/Themes/XAML/TreeViewItem.xaml)),
строки 484-534:

```xml
<Border Name="Bd" Grid.Column="1" Background="{TemplateBinding Background}" ...>
...
<Trigger Property="IsSelected" Value="true">
    <Setter TargetName="Bd" Property="Background"
            Value="{DynamicResource {x:Static SystemColors.HighlightBrushKey}}"/>
    <Setter Property="Foreground"
            Value="{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}"/>
</Trigger>
<MultiTrigger>
    <MultiTrigger.Conditions>
        <Condition Property="IsSelected" Value="true"/>
        <Condition Property="IsSelectionActive" Value="false"/>
    </MultiTrigger.Conditions>
    <Setter TargetName="Bd" Property="Background"
            Value="{DynamicResource {x:Static SystemColors.InactiveSelectionHighlightBrushKey}}"/>
    ...
</MultiTrigger>
```

Ключевые факты:

1. Фон выделения задаётся **именованному `Border` внутри шаблона**, а не свойству
   `TreeViewItem.Background`. Поэтому `<Setter Property="Background">` в
   `ItemContainerStyle` для выделенного узла **не работает** — его перекрывает
   триггер шаблона. Работают только `Foreground` и `FontWeight` из стиля
   (триггер стиля приоритетнее триггера шаблона для свойств самого контрола).
2. В шаблоне **нет триггера `IsMouseOver`** — наведение рисуется через
   `TemplateBinding Background`, то есть стилевой сеттер наведения реально
   работает. Отсюда асимметрия: hover — из стиля, selection — из `SystemColors`.
3. Ветки две: с клавиатурным фокусом внутри `TreeView` — `HighlightBrushKey`,
   без фокуса — `InactiveSelectionHighlightBrushKey`.
4. `IsSelectionActive` = «фокус клавиатуры внутри Selector»
   (`Selector.OnIsKeyboardFocusWithinChanged`, `Selector.cs:1297-1335`).
   Программная установка `container.IsSelected = true`
   (`Views/MainWorkWindow.xaml.cs:442`) фокус не переносит: `TreeViewItem.Select`
   фокусируется только если дерево уже было в фокусе
   (`TreeViewItem.cs:493-496`). То есть при синхронизации с MDI-вкладкой
   почти всегда работает **неактивная** ветка.

### 3.2. Что было до правки (git HEAD) и почему «не видно»

| Тема | Фон выделения с фокусом (= `AppHighlightColor`) | Контраст к панели | Фон без фокуса | Контраст к панели |
|---|---|---|---|---|
| Default | `#34495E` | **1.18:1** | `#2F80C9` | 2.64:1 |
| Dark | `#2A3A4A` | **1.41:1** | `#2878B8` | 3.51:1 |
| Dracula | `#44475A` | **1.56:1** | `#2878B8` (из Dark) | 3.03:1 |

Плюс в `ItemContainerStyle` наведение и выделение были одной кистью
`AppHighlightBrush`, а `AppHighlightBrush` = `AppHighlightColor` во всех трёх
темах. Итог: пока фокус в дереве — выделение неотличимо от hover и почти
сливается с панелью (1.18:1 в светлой теме); как только фокус ушёл в MDI —
узел внезапно становится синим. Отсюда жалоба «выделение плохо видно».

---

## 4. Что уже сделано в рабочем дереве (незакоммичено)

`git diff --stat`: `Themes/Default.xaml +14`, `Themes/Dark.xaml +8`,
`Themes/Dracula.xaml +8`, `Views/MainWorkWindow.xaml +43/-5`.

- `Views/MainWorkWindow.xaml:139-154` — `TreeView.Resources` переопределяет все
  четыре системных ключа, цвет берётся из `AppNavigationSelectionColor` /
  `AppNavigationSelectionTextColor` (не из `AppHighlightColor`, как раньше).
- `:156-178` — `ItemContainerStyle`: `IsMouseOver` объявлен **первым**,
  `IsSelected` — вторым (перекрывает hover), в `IsSelected` добавлены
  `Foreground` и `FontWeight=SemiBold`.
- `:180-206` — в `HierarchicalDataTemplate` добавлена полоса
  `Border x:Name="SelectionBar" Width="3"` с постоянным местом под неё
  (`Background="Transparent"`) и `DataTrigger` по `IsSelected` предка-`TreeViewItem`,
  который красит её в `AppNavigationSelectionAccentBrush`.

Измеренный контраст текущих значений:

| Метрика | Default | Dark | Dracula |
|---|---|---|---|
| заливка выделения к панели | 2.37:1 | 3.56:1 | 2.21:1 |
| текст выделения к заливке | **4.63:1** | **4.63:1** | **6.43:1** |
| акцентная полоса к заливке | 2.09:1 | 2.09:1 | 2.67:1 |
| акцентная полоса к панели | 4.96:1 | 7.44:1 | 5.90:1 |
| hover (`AppHighlightBrush`) к панели | 1.18:1 | 1.41:1 | 1.56:1 |
| hover к заливке выделения | 2.01:1 | 2.52:1 | 1.42:1 |

Формула WCAG 2.1: линеаризация `c/12.92` при `c ≤ 0.04045`, иначе
`((c+0.055)/1.055)^2.4`; `L = 0.2126R + 0.7152G + 0.0722B`;
`ratio = (L_light + 0.05) / (L_dark + 0.05)`.

---

## 5. Рекомендуемые значения

### 5.1. Три кисти выделения (для справки — уже стоят в темах)

| Ключ | Default | Dark | Dracula | Проверка |
|---|---|---|---|---|
| `AppNavigationSelectionBrush` | `#2C79B8` | `#2C79B8` | `#4F5D8A` | белый текст 4.63 / 4.63 / 6.43 — **SC 1.4.3 пройден** |
| `AppNavigationSelectionTextBrush` | `#FFFFFF` | `#FFFFFF` | `#FFFFFF` | — |
| `AppNavigationSelectionAccentBrush` | `#64B5F6` | `#64B5F6` | `#BD93F9` | к панели 4.96 / 7.44 / 5.90 |

Опционально, если нужен строгий SC 1.4.11 (3:1) **для самой полосы** к заливке:
`#A9DAFA` вместо `#64B5F6` (3.11:1 к `#2C79B8`; к панели 6.6 / 9.9) и
`#D6B8FF` вместо `#BD93F9` (3.72:1 к `#4F5D8A`). Это меняет вид полосы на
почти белый/бледно-лиловый — решать дизайну.

Про заливку в Default: одновременно «заливка ≥ 3:1 к панели `#2C3E50`» и
«белый текст ≥ 4.5:1» недостижимы. Панель `L = 0.0454` требует `L_fill ≥ 0.236`
для 3:1, а белый текст требует `L_fill ≤ 0.1833` для 4.5:1 — интервал пуст.
Поэтому в светлой теме 3:1 даёт акцентная полоса (4.96:1), а заливка остаётся
2.37:1. Альтернатива — светлая заливка с тёмным текстом (другой визуальный язык).

В Dracula заливку можно поднять до `#6272A4` (палитра Dracula): 3.03:1 к панели
и 4.71:1 к белому — обе цели выполняются. Текущий `#4F5D8A` даёт 2.21:1.

### 5.2. Hover — новый ключ `AppNavigationHoverBrush`

Нейтральный (не синий/не лиловый), заметно светлее панели, но явно темнее
заливки выделения — состояние отличается и по светлоте, и по тону:

| Ключ | Default | Dark | Dracula |
|---|---|---|---|
| `AppNavigationHoverBrush` | `#3D566E` | `#303748` | `#3B3E52` |
| контраст к панели | 1.44:1 | 1.39:1 | 1.35:1 |
| контраст к заливке выделения | 1.65:1 | 2.57:1 | 1.64:1 |
| контраст к прежнему hover | `#34495E` → +светлее | `#2A3A4A` → +светлее | `#44475A` → темнее |

Альтернативы, если нужен hover заметнее: Default `#435E78` (1.63:1 к панели),
Dark `#37405A` (1.60:1), Dracula `#42465C` (1.53:1).

---

## 6. Точные якоря правки

### 6.1. Вставка `AppNavigationHoverBrush`

Вставить строку **после** `AppNavigationSelectionAccentBrush` (перед пустой
строкой и блоком `Configurator*`):

| Файл | Строка-якорь (после неё вставлять) | Текст якоря |
|---|---|---|
| `Themes/Default.xaml` | **74** | `<SolidColorBrush x:Key="AppNavigationSelectionAccentBrush" Color="#64B5F6"/>` |
| `Themes/Dark.xaml` | **68** | `<SolidColorBrush x:Key="AppNavigationSelectionAccentBrush" Color="#64B5F6"/>` |
| `Themes/Dracula.xaml` | **62** | `<SolidColorBrush x:Key="AppNavigationSelectionAccentBrush" Color="#BD93F9"/>` |

Готовый фрагмент (значение подставить по теме):

```xml
    <SolidColorBrush x:Key="AppNavigationHoverBrush" Color="#3D566E"/>
```

### 6.2. Подключение hover

`Views/MainWorkWindow.xaml:169` — заменить ресурс в сеттере наведения:

```xml
<!-- было -->
<Setter Property="Background" Value="{DynamicResource AppHighlightBrush}"/>
<!-- стало -->
<Setter Property="Background" Value="{DynamicResource AppNavigationHoverBrush}"/>
```

### 6.3. Уже стоят на месте (менять не нужно)

- `Views/MainWorkWindow.xaml:146-153` — четыре оверрайда `SystemColors.*`
  (`HighlightBrushKey`, `InactiveSelectionHighlightBrushKey`,
  `HighlightTextBrushKey`, `InactiveSelectionHighlightTextBrushKey`).
- `:171-173` — сеттеры `IsSelected` (фон, текст, `FontWeight`).
- `:186-203` — `SelectionBar` + `DataTrigger`.

Порядок триггеров в стиле менять нельзя: `IsMouseOver` (`:168`) должен идти
**до** `IsSelected` (`:171`), иначе наведение перекроет выделение.

---

## 7. Остаточные дефекты и риски

1. **Бейдж на выделенной строке.** `TextBlock` бейджа жёстко красится в
   `AppNavigationMutedForegroundBrush` (`Views/MainWorkWindow.xaml:193`) и не
   реагирует на выделение: контраст к заливке 3.06:1 (Default), **2.69:1** (Dark),
   4.95:1 (Dracula). При 11 px это ниже 4.5:1 в двух темах. Лечится `DataTrigger`
   по `IsSelected` предка → `AppNavigationSelectionTextBrush`.
2. **Акцентная полоса 2.09:1 / 2.09:1 / 2.67:1 к заливке** — ниже 3:1 SC 1.4.11.
   Само состояние при этом различимо (заливка + текст + полоса), но если нужен
   строгий формальный критерий — см. 5.1.
3. **`<Setter Property="Background">` в триггере `IsSelected`** (`:172`) —
   визуально мёртвый код: `Border` внутри шаблона перекрывает его. Не мешает, но
   при рефакторинге легко принять за работающий механизм.
4. **`AppHighlightBrush` остаётся перегруженным**: это и цвет hover, и бывший
   цвет выделения, и (совпадающее значение) `AppHighlightColor`. После ввода
   `AppNavigationHoverBrush` ключ нужен только как «фон наведения прочих мест».
5. **Наследование Dracula ← Dark**: любой новый ключ, забытый в Dracula, молча
   возьмёт тёмное значение. Проверять все три файла.
6. **`GridSplitter`** (`Views/MainWorkWindow.xaml:213-214`) захардкожен
   `Background="#34495E"` и не следует теме — вне рамок задачи, но рядом.

---

## 8. Что не проверено

- Приложение не запускалось: фактическая ветка (`active`/`inactive`) и вид
  выделения подтверждены только статическим анализом шаблона и исходников WPF,
  не рантаймом. Быстрая проверка: выделить узел мышью (фокус в дереве) и
  переключить MDI-вкладку (фокус уходит) — фон должен остаться `#2C79B8`.
- `Views/ConfiguratorWindow.xaml:179-237` имеет собственную копию шаблона
  `TreeViewItem` и свои кисти (`ConfiguratorTreeItem*`) — в задачу не входило,
  не анализировалось на контраст.
- Числа контраста получены расчётом по WCAG 2.1, без колориметрических измерений
  на реальном мониторе.
