# Центрирование прокрутки дерева навигации на выделенном узле (BIS.ERP, WPF/.NET 8)

Дата: 10.11.2026. Снимок рабочей копии: `git status` показывает `M Views/MainWorkWindow.xaml.cs`
(HEAD `4a73d09` «Миграция Материалов ТМЗ»). Номера строк ниже — по этому снимку; файл правится
параллельно, при расхождении ориентируйтесь на имена методов.

## 1. Задача

`TreeViewItem.BringIntoView()` прокручивает минимально (узел прилипает к верхнему/нижнему краю).
Нужно: если узел далеко вверху/внизу — поставить его примерно в центр видимой области, при
небольших сдвигах прокрутку не трогать.

## 2. Фактическая структура левой панели

`Views/MainWorkWindow.xaml:128` — `<ScrollViewer VerticalScrollBarVisibility="Auto" Background=...>`
(без `x:Name`), внутри `:130` — `<TreeView x:Name="NavigationTree" ...>`, закрываются на `:207`/`:208`.
Между ними ничего нет: содержимое ScrollViewer — сам TreeView.

`Views/MainWorkWindow.xaml.cs`:
- `:362` `GetNavigationContainer(NavigationItem)` — `ContainerFromItem`, затем рекурсивный поиск по
  корням; `:379` `FindNavigationContainerRecursive`.
- `:422` `SelectNavigationItem` — ранний `return` на `:424`, если узел уже выделен; иначе
  `ExpandNavigationAncestors` + `TrySelectNavigationContainer`, при неудаче один отложенный повтор
  на `DispatcherPriority.Loaded` (`:432`–`:435`).
- `:438` `TrySelectNavigationContainer` — `IsSelected = true` под флагом
  `_synchronizingNavigationSelection`, затем `ScrollNavigationItemIntoView(container)` (`:450`).
- `:464` `ScrollNavigationItemIntoView` — текущая реализация центрирования (см. §4).
- `:499` `FindScrollingAncestor` — ближайший вверх `ScrollViewer` с `ScrollableHeight > 0`,
  иначе последний найденный (внешний).
- `:117` `private const double NavigationScrollComfortRatio = 0.25;`
- `:302` `SelectNavigationMatchContainer` (поиск по дереву) — по-прежнему `BringIntoView()` на
  `:308` и `:320`.
- `:2526` `FindVisualParent<T>` — уже есть в классе (возвращает `T`, внутри `return null`, т.е.
  файл уже даёт CS8603; сигнатуру не трогаем).
- `:2069` `activeDocument.Tag = item;`, `:2074`–`:2079` узлы `Section`/`Group` открывают
  «обзор» (`ShowNavigationOverview`), т.е. вкладку тоже помечают узлом-папкой.

## 3. Почему BringIntoView прижимает к краю и где «правильный» ScrollViewer

1. В `ControlTemplate` у `TreeView` **есть собственный `ScrollViewer`** вокруг `ItemsPresenter`
   (документация: «your template might contain a ItemsPresenter within a ScrollViewer», пример
   шаблона содержит `<ScrollViewer Focusable="False" CanContentScroll="False">`).
   Значит визуальные предки `TreeViewItem`: … → внутренний `ScrollViewer` TreeView → `TreeView` →
   внешний `ScrollViewer` левой панели.
2. Внешний `ScrollViewer` (`CanContentScroll` не задан ⇒ `false`, физическая прокрутка) меряет
   содержимое на бесконечную высоту, поэтому TreeView растянут на всю высоту дерева, а его
   внутренний `ScrollViewer` имеет `ScrollableHeight == 0`. Реально прокручивается только внешний.
3. Отсюда два следствия:
   - `container.BringIntoView()` доходит до внешнего ScrollViewer и даёт минимальную прокрутку —
     ровно наблюдаемый симптом;
   - наивный поиск «вверх от TreeViewItem» вернёт внутренний ScrollViewer, у которого
     `ScrollableHeight == 0`, и `ScrollToVerticalOffset` молча ничего не сделает.
4. Поэтому корректный поиск — «ближайший вверх ScrollViewer с `ScrollableHeight > 0`»
   (уже реализовано в `FindScrollingAncestor`), либо старт от самого `NavigationTree`
   (`FindVisualParent<ScrollViewer>(NavigationTree)` всегда даёт внешний).

Проверка «с другого угла»: глобальный `Behaviors/MouseWheelScrollBehavior.cs:42`–`:57` ищет
прокручиваемого предка именно по `ScrollableHeight`/`CanScroll`, т.е. в этом окне цепочка
ScrollViewer'ов уже учитывается таким способом.

## 4. Дефекты текущей реализации `ScrollNavigationItemIntoView` (`:464`)

### 4.1. Главный: центрируется весь `TreeViewItem`, а он включает поддерево

`container.ActualHeight` — высота всего контейнера: в шаблоне `TreeViewItem` строка заголовка и
`ItemsPresenter` лежат в одном `Grid` (строки 0 и 1), поэтому у раскрытого узла в `ActualHeight`
входят все потомки. В приложении `NavigationItem.IsExpanded` по умолчанию `true` и привязан
TwoWay (`Views/MainWorkWindow.xaml:146`, `:177`), так что папки почти всегда раскрыты.

Последствия при `height = ActualHeight` контейнера:
- `targetOffset = VerticalOffset + top − (viewport − height) / 2` ставит в центр **середину
  поддерева**, а не строку: у папки с 10 детьми (~360 px при `Padding="5,8"`, `FontSize=13`,
  строка ≈ 33 px) строка окажется примерно на 170 px выше центра при viewport 700 px;
- при поддереве выше viewport (≈30 раскрытых детей) `(viewport − height)` отрицательно, прокрутка
  уходит вниз и строка выделения уезжает **за верхнюю границу** — узел не виден вообще, т.е. хуже,
  чем исходный `BringIntoView`;
- проверка «уже в средней половине» для такого контейнера практически никогда не проходит
  (`top + height <= viewport − comfort`), поэтому дерево дёргается на каждом переключении.

Воспроизведение: открыть папку (`Section`/`Group`) → её «обзор» получает `Tag = item` (`:2069`) →
переключиться на другой документ и обратно → `SelectNavigationItem` выделяет узел-папку.

Лечение: центрировать **строку заголовка**: `top` берётся от контейнера (у него верхняя кромка =
верхняя кромка строки), а высота — у части `PART_Header` (`ContentPresenter`, имя части
документировано в «TreeViewItem Parts»). Тот же приём применяет Rick Strahl в разборе этой задачи
(он ищет `ContentPresenter` внутри item именно потому, что `TreeViewItem` включает детей).

### 4.2. Нет гарантии актуальной раскладки

`ScrollNavigationItemIntoView` вызывается синхронно сразу после `IsSelected = true`, без
`UpdateLayout()`. Если предки только что раскрыты (`ExpandNavigationAncestors`), размеры/трансформы
могут быть от предыдущего прохода раскладки; при `container.ActualHeight == 0` метод молча
откатывается на `BringIntoView()` — т.е. на старое поведение «к краю». Отложенного повтора нет
(в отличие от `SelectNavigationItem`, где повтор есть).

### 4.3. Повторный выбор узла не центрирует

`:424`: если `NavigationTree.SelectedItem` уже равен узлу, метод выходит. Сценарий «прокрутил
дерево руками → переключился на другую закладку → вернулся» не центрирует ничего.

### 4.4. Поиск по дереву остался на старом поведении

`SelectNavigationMatchContainer` (`:302`) вызывает `BringIntoView()` в двух местах (`:308`, `:320`) —
переход по совпадениям Ctrl+F по-прежнему прилипает к краю.

### 4.5. Мелочь: `PART_Header` не учитывает `Padding`

`Padding="5,8"` применяется на `Border` вокруг `PART_Header`, поэтому центрирование по высоте
заголовка даёт смещение ~8 px от геометрического центра строки. Для «примерно по центру» и порога
25 % это несущественно.

## 5. Формула

`TransformToAncestor(scrollViewer)` даёт координаты в системе самого ScrollViewer, т.е. **уже с
учётом текущего `VerticalOffset`**: `y` — расстояние от верхней кромки видимой области.
Целевое состояние: центр строки совпадает с центром viewport.

```
y_new = (viewport − rowHeight) / 2
y_new = y − Δ,  Δ = y − (viewport − rowHeight) / 2
target = VerticalOffset + Δ = VerticalOffset + y − (viewport − rowHeight) / 2
```

Далее `Math.Clamp(target, 0, ScrollableHeight)` — у начала/конца списка центр недостижим, и это
нормально. Формула совпадает с опубликованной в разборе west-wind (там же нормировка
`ViewportHeight * ScrollableHeight / ExtentHeight` — она нужна только для `CanContentScroll=true`,
где `ViewportHeight` измеряется в элементах; в нашем XAML логическая прокрутка не включена).

Самопроверка после вставки: если узел уезжает примерно на удвоенное расстояние, значит координата
уже в системе содержимого — тогда из формулы убирается `VerticalOffset` (признак `CanContentScroll=true`).

## 6. Правило «центрировать или нет»

Центрируем, только если строка узла **не помещается целиком в среднюю половину** видимой области:
её верх выше `0.25 × viewport` или её низ ниже `0.75 × viewport`; иначе прокрутка не трогается.
Порог — константа `NavigationScrollComfortRatio = 0.25` (`:117`), менять её — единственный
настроечный параметр (0.3 = агрессивнее центрирует, 0.15 = спокойнее).

## 7. Готовый код

### 7.1. Замена `ScrollNavigationItemIntoView` (`:464`)

```csharp
/// <summary>
/// Прокручивает левую панель к выделенному узлу: ставит строку узла в середину,
/// если она оказалась вне средней половины видимой области.
/// </summary>
private void ScrollNavigationItemIntoView(TreeViewItem container, bool allowRetry = true)
{
    if (FindScrollingAncestor(container) is not ScrollViewer scrollViewer)
    {
        container.BringIntoView();
        return;
    }

    // Замер не должен зависеть от того, успел ли пройти layout после раскрытия предков.
    scrollViewer.UpdateLayout();

    // Центрируем строку узла, а не весь TreeViewItem: у раскрытого узла
    // в ActualHeight входят все потомки и «центр» уехал бы в середину поддерева.
    var row = container.Template?.FindName("PART_Header", container) as FrameworkElement ?? container;

    if (row.ActualHeight <= 0 || scrollViewer.ViewportHeight <= 0)
    {
        if (allowRetry)
        {
            // Контейнер мог быть создан в этом же проходе раскладки — один отложенный повтор.
            Dispatcher.BeginInvoke(
                new Action(() => ScrollNavigationItemIntoView(container, false)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            container.BringIntoView();
        }

        return;
    }

    // TransformToAncestor уже учитывает текущий VerticalOffset → координата от кромки viewport.
    var itemTop = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
    var viewportHeight = scrollViewer.ViewportHeight;
    var comfort = viewportHeight * NavigationScrollComfortRatio;

    if (itemTop >= comfort && itemTop + row.ActualHeight <= viewportHeight - comfort)
        return; // строка в средней половине — прокрутку не трогаем

    var targetOffset = scrollViewer.VerticalOffset + itemTop - (viewportHeight - row.ActualHeight) / 2;
    scrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight));
}
```

Компактный вариант того же метода (24 строки, если нужен минимальный дифф):

```csharp
private void ScrollNavigationItemIntoView(TreeViewItem container, bool allowRetry = true)
{
    if (FindScrollingAncestor(container) is not ScrollViewer scrollViewer)
    {
        container.BringIntoView();
        return;
    }

    scrollViewer.UpdateLayout();
    var row = container.Template?.FindName("PART_Header", container) as FrameworkElement ?? container;
    if (row.ActualHeight <= 0 || scrollViewer.ViewportHeight <= 0)
    {
        if (allowRetry)
            Dispatcher.BeginInvoke(new Action(() => ScrollNavigationItemIntoView(container, false)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        else
            container.BringIntoView();
        return;
    }

    var top = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
    var viewport = scrollViewer.ViewportHeight;
    var comfort = viewport * NavigationScrollComfortRatio;
    if (top >= comfort && top + row.ActualHeight <= viewport - comfort)
        return;

    var target = scrollViewer.VerticalOffset + top - (viewport - row.ActualHeight) / 2;
    scrollViewer.ScrollToVerticalOffset(Math.Clamp(target, 0, scrollViewer.ScrollableHeight));
}
```

### 7.2. Правки в местах вызова (по желанию, но они закрывают §4.3 и §4.4)

`TrySelectNavigationContainer` уже вызывает правильный метод — менять не нужно.

Поиск по дереву, `SelectNavigationMatchContainer` (`:302`), два места `:308` и `:320`:
`container.BringIntoView();` → `ScrollNavigationItemIntoView(container);`
(и `lateContainer.BringIntoView();` → `ScrollNavigationItemIntoView(lateContainer);`).

`SelectNavigationItem` (`:422`) — центрировать и при уже выделенном узле:

```csharp
if (ReferenceEquals(NavigationTree.SelectedItem, item))
{
    // Узел уже выделен, но мог уехать из вида после ручной прокрутки.
    if (GetNavigationContainer(item) is TreeViewItem current)
    {
        Dispatcher.BeginInvoke(
            new Action(() => ScrollNavigationItemIntoView(current)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    return;
}
```

## 8. using, стиль, nullable

Новых `using` не требуется. Уже есть в шапке `Views/MainWorkWindow.xaml.cs`:
`System` (`Math`, `Action`), `System.Windows` (`Point`, `FrameworkElement`, `FrameworkTemplate.FindName`),
`System.Windows.Controls` (`ScrollViewer`, `TreeViewItem`), `System.Windows.Media`
(`TransformToAncestor` — метод `Visual`). `DispatcherPriority` в этом файле традиционно пишется
полностью (`System.Windows.Threading.DispatcherPriority`), так и оставлено.

Стиль совпадает: `private`-методы, XML-doc на русском, `_camelCase` для полей, `is not`-паттерны,
`ScrollViewer?` при `Nullable=enable` (`BIS.ERP.csproj`: `<Nullable>enable</Nullable>`,
`<ImplicitUsings>enable</ImplicitUsings>`, `net8.0-windows` ⇒ C# 12).
`Template?.FindName(...) as FrameworkElement ?? container` даёт non-nullable `FrameworkElement`.

## 9. На что обратить внимание при вставке

1. **Не искать ScrollViewer «просто вверх»**: первым найдётся внутренний ScrollViewer шаблона
   TreeView с `ScrollableHeight == 0` — центрирование молча не сработает. Либо
   `FindScrollingAncestor` с проверкой `ScrollableHeight > 0`, либо старт от `NavigationTree`.
2. **Имя части `PART_Header`** — из документированных частей `TreeViewItem`; в стандартной теме оно
   есть. Если тему переопределят своим шаблоном без этой части, сработает fallback на весь
   контейнер (снова неточность для раскрытых папок) — при кастомной теме имя надо проверить.
3. **`UpdateLayout()`** стоит один проход раскладки на переключение закладки. Дерево не
   виртуализируется (внешний ScrollViewer меряет на бесконечную высоту), поэтому проход по всем
   узлам; на типичном дереве это доли миллисекунды, на тысячах узлов — повод кэшировать.
4. **Один повтор, не цикл**: `allowRetry` защищает от бесконечной очереди, когда панель скрыта
   (высота 0). Не превращать в `while`.
5. **Порядок с фокусом**: если в месте вызова есть `container.Focus()` (как в
   `SelectNavigationMatchContainer`), центрировать нужно после него, иначе фокус/BringIntoView
   перебьёт смещение. Надёжнее — отложить вызов на `DispatcherPriority.Loaded`, как в §7.2.
6. **`CanContentScroll`**: при `true` (логическая прокрутка) `ViewportHeight`/`VerticalOffset`
   измеряются в элементах и формула неверна. В текущем XAML не включено; не включать.
7. **`x:Name` для ScrollViewer** (альтернатива, требует правки XAML): назвать его
   `NavigationScrollViewer` и обращаться напрямую — уходит поиск по дереву и вся §9.1. Границы
   задачи запрещают правку XAML, поэтому в коде оставлен поиск.
8. **`BringIntoView` как fallback** оставлен только на вырожденные случаи (нет ScrollViewer,
   нулевые размеры после повтора) — это старое поведение «к краю», но лучше, чем ничего.

## 10. Что проверено и что нет

Проверено чтением кода: структура XAML, все точки `BringIntoView`/`ScrollToVerticalOffset`/
`ScrollIntoView` по решению, отсутствие готового помощника (см. §11), наличие внутреннего
ScrollViewer у TreeView (по документации шаблона), путь «папка → обзор → Tag → выбор узла».

Не проверено запуском: в среде нет .NET SDK (`dotnet --version` → «A compatible .NET SDK was not
found», `global.json` требует 8.0.425), поэтому сборка и визуальная проверка не выполнялись.
Числовые примеры в §4.1 — арифметика по `Padding="5,8"`, `FontSize=13`, высоте строки ≈ 33 px.

## 11. Готовые помощники в решении (поиск по `BringIntoView`, `ScrollToVerticalOffset`,
`ScrollIntoView`, `ScrollToEnd`, `Center*`)

- `BringIntoView`: `Views/ConfiguratorWindow.xaml.cs:188`, `:2030`, `:2058`;
  `Views/InfoBasesView.xaml.cs:103`; `Views/Dialogs/DynamicDocumentItemDialog.xaml.cs:1481`, `:1501`;
  `Views/MainWorkWindow.xaml.cs:308`, `:320`, `:469`, `:478`.
- `ScrollToVerticalOffset`: `Behaviors/MouseWheelScrollBehavior.cs:38` (колесо),
  `Views/ConfiguratorWindow.xaml.cs:3646` (колесо в левой навигации), `Views/SetupWindow.xaml.cs:32`,
  и текущий `MainWorkWindow` (§7).
- `ScrollIntoView` — только `DataGrid` (`CatalogDataView`, `DataGridSelectionHelper` и диалоги).
- `ScrollToEnd` — один, в тестовом стенде `BIS.ERP.TESTS/.../MainWindow.xaml.cs:557`.
- Общего класса-помощника (`UiHelper`, `VisualHelper`, `ScrollHelper`, `WindowUtilities`,
  `VisualTreeExtensions`) в решении нет; ближайшее по смыслу — `Behaviors/MouseWheelScrollBehavior.cs`
  (обход предков + `Clamp`), его логику и повторяет `FindScrollingAncestor`.

## 12. Источники

- Код: `Views/MainWorkWindow.xaml:128`–`:208`, `Views/MainWorkWindow.xaml.cs:117`, `:302`–`:322`,
  `:362`, `:379`, `:422`–`:512`, `:2069`, `:2074`–`:2079`, `:2526`;
  `Behaviors/MouseWheelScrollBehavior.cs:38`–`:57`; `BIS.ERP.csproj` (`Nullable`, `net8.0-windows`).
- Документация: TreeView Styles and Templates — «your template might contain a ItemsPresenter
  within a ScrollViewer», часть `PART_Header` у TreeViewItem:
  https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/treeview-styles-and-templates
- Разбор той же проблемы с центрированием (BringIntoView прижимает к краю, центрировать надо
  строку-заголовок, а не весь TreeViewItem): https://weblog.west-wind.com/posts/2025/Jul/15/Centering-a-WPF-TreeViewItem-in-the-TreeView-ScrollViewer
