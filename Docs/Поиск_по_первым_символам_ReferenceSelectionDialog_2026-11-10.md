# Поиск в окне выбора справочника: с начала записи вместо `LIKE '%…%'`

**Дата:** 10.11.2026
**Версия сборки:** 1.1.890 (Build 890)
**Задача (как сформулировал пользователь):** «ReferenceSelectionDialog поиск — по первому символу, а не по совпадению like».
**Итог:** поле «Поиск» в `ReferenceSelectionDialog` сравнивает значения через `StartsWith` вместо `Contains`; составная подпись «1010 - Касса» находится и по коду, и по наименованию; предзаполнение поиска из ячейки больше не ставит в строку поиска «код - наименование» целиком.

Документ самодостаточный: ниже — что было, что стало, полные листинги изменённых методов, diff, разбор решений и протокол проверки. История правок ТМЗ (`Docs/InventoryDocumentDialog.md`) намеренно не дополнялась — там уже перемешаны темы, эта правка живёт здесь.

---

## 1. Где живёт поиск

Цепочка вызовов в диалоге:

```
Views/Dialogs/ReferenceSelectionDialog.xaml        SearchBox (TextBox)
        │  TextChanged="SearchBox_TextChanged"
        ▼
Views/Dialogs/ReferenceSelectionDialog.xaml.cs     SearchBox_TextChanged
        │
        ├── FilterItems(search)          ← здесь и была логика сравнения
        └── ItemsGrid.ItemsSource = _filteredItems
```

Дополнительно `FilterItems` вызывается из `ReloadItemsAsync` (после «Добавить»/«Изменить» запись справочника список перечитывается с текущим текстом поиска), а `SetSearchText` заполняет строку поиска при открытии окна из ячейки документа.

Один и тот же диалог открывается практически из всех мест, где выбирают элемент справочника:

| Файл | Что выбирают |
|---|---|
| `Views/ReferencePickerControlFactory.cs:418` | ссылки в строках документов (общий механизм) |
| `Views/Dialogs/InventoryDocumentDialog.xaml.cs:832, 1441, 1459, 1602, 1892` | материал, счёт, участок, участок-получатель, МОЛ |
| `Views/Dialogs/CashOrderDialog.xaml.cs:464, 1579, 1830` | касса, сотрудники, организации |
| `Views/Dialogs/PaymentOrderDialog.xaml.cs:408` | классификатор платежей |
| `Views/Dialogs/InvoiceEditDialog.xaml.cs:2036` | значения справочников счёта-фактуры |
| `Views/Dialogs/FixedAssetDetailsDialog.xaml.cs:536` | реквизиты основного средства |
| `Views/DynamicDocumentWorkView.xaml.cs:667` | динамические справочники |
| `Views/AccountingReportsView.xaml.cs:393`, `Views/AccountingReportsView.AccountPair.cs:26` | счета и пары счетов |
| `Views/ReportDesignerWindow.xaml.cs:223` | источники/типы в конструкторе отчётов |
| `Views/Controls/LineAnalyticsColumns.cs:249` | аналитика по счёту в строках |

Поэтому изменение семантики поиска касается всех этих окон сразу — это и требовалось.

## 2. Поведение: было / стало

| Запрос | Было (`Contains`, `LIKE '%…%'`) | Стало (`StartsWith`) |
|---|---|---|
| `кас` | «Касса», «Главная касса», «Кассир по налогам», «Мелкая касса» | «Касса», «Касса №1» |
| `10` (план счетов) | 1010, 1030, **2010, 3010**, 9010 … | 1010, 1030, 1090 |
| `Иванов` (МОЛ) | «Иванов И.И.», «Петров Иван», «Сидоров-Иванов» | «Иванов И.И.» |
| `1010 - Касса` → в поиск попадает «Касса» | — | «Касса» (нормализация `SetSearchText`) |
| `касса` при значении «1010 - Касса» | совпадение | совпадение (префикс после разделителя « - ») |
| `№1` при значении «Склад №1» | совпадение | нет совпадения (это уже не начало записи) |

Порядок строк в таблице не меняется: фильтр только отбрасывает неподходящие строки, исходный порядок (например, план счетов по коду) сохраняется.

## 3. Изменённые файлы

| Файл | Что изменено |
|---|---|
| `Views/Dialogs/ReferenceSelectionDialog.xaml.cs` | `FilterItems` (переписан), новые `MatchesSearch`, `IsSearchableKey`, `DisplaySeparators`; в `SetSearchText` — нормализация «код - наименование» |
| `Views/Dialogs/ReferenceSelectionDialog.xaml` | подсказка `SearchBox.ToolTip` |
| `Docs/PROJECT_SCHEMA.md` | одна строка в таблице диалогов: поиск по первым символам |
| `Docs/Поиск_по_первым_символам_ReferenceSelectionDialog_2026-11-10.md` | этот документ |

---

## 4. Код

### 4.1 `FilterItems` — было

```csharp
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
```

Одна строка `Contains(...)` — это и есть `LIKE '%текст%'`: совпадение в любом месте значения. Плюс `item.Values` тянул в поиск всё подряд, включая `Id` (GUID) и служебные ключи, которых в таблице не видно.

### 4.2 `FilterItems` — стало

```csharp
        private List<Dictionary<string, object>> FilterItems(string search)
        {
            if (string.IsNullOrEmpty(search))
                // ВАЖНО: возвращаем НОВЫЙ список, а не ссылку на _items.
                // Мутация общего экземпляра, привязанного к ItemsGrid, без уведомлений
                // коллекции рассинхронизирует генератор DataGrid (лог 09:49).
                return new List<Dictionary<string, object>>(_items);

            var query = search.Trim();
            if (query.Length == 0)
                return new List<Dictionary<string, object>>(_items);

            return _items
                .Where(item => item.Any(pair => IsSearchableKey(pair.Key) && MatchesSearch(pair.Value, query)))
                .ToList();
        }
```

Что изменилось по существу:

1. `Contains` → `StartsWith` (внутри `MatchesSearch`).
2. Вместо `item.Values` — `item.Any(pair => ...)`: перебираются пары ключ/значение, поэтому можно отсеять служебные ключи по имени.
3. Введён `query = search.Trim()`: текст из пробелов не должен внезапно совпадать со всем списком; пустой запрос — это «показать всё» (тот же новый список, что и раньше).
4. Сравнение по-прежнему `OrdinalIgnoreCase` — регистр не важен, кириллица «А/а», «Ё/ё» сопоставляются.

### 4.3 Новый `MatchesSearch`

```csharp
        /// <summary>
        /// Поиск «по первым символам»: значение подходит, если оно начинается
        /// с введённого текста. Раньше здесь был Contains (аналог LIKE '%текст%'):
        /// одна буква вытаскивала половину справочника, и по списку нельзя было
        /// понять, где именно совпадение. Теперь поведение как в прежних
        /// справочниках — набираете код или начало наименования.
        /// </summary>
        private static bool MatchesSearch(object? value, string query)
        {
            var text = value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return true;

            // Составная подпись «1010 - Касса»: код и наименование могут лежать
            // в одной ячейке (резолв ссылок через DisplayPattern). Тогда
            // наименование ищем с начала — после разделителя.
            foreach (var separator in DisplaySeparators)
            {
                var index = text.IndexOf(separator, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                var tail = text[(index + separator.Length)..].TrimStart();
                if (tail.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
```

Почему появилась вторая проверка (после разделителя): часть значений приходит в диалог не отдельными колонками «Код» и «Наименование», а одной подписью, собранной в `ReferenceDisplayHelper.BuildDisplayValue` по `DisplayPattern` поля-ссылки. В базе это выглядит как `1010 - Касса`: строки `{"Код": "1010", "Наименование": "Касса"}` для счёта, но для поля-ссылки (например, «Дебет»/«Кредит» в парах счетов, «Счёт» в кассе) приходит одна ячейка `"1010 - Касса"`. При строгом префиксе запрос «Касса» по такой ячейке не сработал бы, хотя пользователь видит ровно это слово в таблице. Поэтому префикс проверяется ещё и от начала наименования после разделителя. При этом «середина слова» по-прежнему не находится: «асса» не совпадёт ни с «Касса», ни с «1010 - Касса».

### 4.4 `IsSearchableKey`

```csharp
        /// <summary>Служебные колонки в поиске не участвуют: их не видно в таблице.</summary>
        private static bool IsSearchableKey(string key) =>
            !key.Equals("Id", StringComparison.OrdinalIgnoreCase) &&
            !key.StartsWith("__", StringComparison.Ordinal);
```

`RebuildGrid` и так исключает `Id` и служебные `CreatedAt/UpdatedAt/CreatedBy/UpdatedBy/IsDeleted` из колонок. Но в поиск они попадали: при `Contains` совпадение по GUID или по дате выглядело случайным шумом, при `StartsWith` — тем более (запрос «a» находил бы строки по GUID). Теперь в сравнении участвуют только те ключи, которые видны в таблице; `__…` — внутренние маркеры (например, `__search_match`).

### 4.5 `DisplaySeparators`

```csharp
        private static readonly string[] DisplaySeparators = { " - ", " – ", " — " };
```

Три варианта разделителя, которые реально встречаются в подписях: дефис с пробелами (`1010 - Касса`, `01 - Склад`), короткое тире (`–`) и длинное (`—`). Массив статический — не пересоздаётся на каждое нажатие клавиши.

### 4.6 `SetSearchText` — было / стало

Было:

```csharp
        public void SetSearchText(string? text)
        {
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value))
                return;

            SearchBox.Text = value;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
```

Стало:

```csharp
        public void SetSearchText(string? text)
        {
            // В ячейке счёт, материал или участок хранится как «1010 - Касса».
            // В строку поиска ставим наименование: при поиске по первым символам
            // составная подпись не совпала бы с отдельными колонками Код/Наименование.
            var value = text?.Trim();
            var separatorIndex = value?.IndexOf(" - ", StringComparison.Ordinal) ?? -1;
            if (separatorIndex > 0 && separatorIndex + 3 < value!.Length)
                value = value[(separatorIndex + 3)..].Trim();

            if (string.IsNullOrEmpty(value))
                return;

            SearchBox.Text = value;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
```

Зачем: окно выбора открывается из ячейки с предзаполненным поиском.

| Вызов | Что приходит | Что попадает в поиск теперь |
|---|---|---|
| `InventoryDocumentDialog.OpenAccountPickerAsync` → `AccountSearchText(row.AccountText)` | `1010 - Касса` → `Касса` (своя нормализация была и раньше) | `Касса` |
| `OpenSitePickerAsync` → `row.SiteText` | `01 - Склад` | `Склад` (раньше — `01 - Склад`) |
| `OpenMaterialPickerAsync` → `MaterialSearchText(row.MaterialText)` | `А-001 - Гвоздь 100` → `Гвоздь 100` | `Гвоздь 100` |

Раньше при `Contains` строка `01 - Склад` тоже ничего не находила (в колонках лежит `01` и `Склад` по отдельности) — то есть предзаполнение участка было сломано и до правки; теперь нормализация общая для всех вызовов, а собственные `AccountSearchText`/`MaterialSearchText` остаются (они не мешают — второй раз префикса «код - » уже нет).

Метод `SearchBox_TextChanged` не менялся:

```csharp
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            _filteredItems = FilterItems(search);
            // Обновление источника — только через UI-очередь и с полной
            // заменой коллекции (reset), а не мутацией привязанного списка.
            ItemsGrid.ItemsSource = null;
            ItemsGrid.ItemsSource = _filteredItems;
        }
```

### 4.7 XAML

```xml
            <TextBlock Grid.Column="0" Text="Поиск:" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <TextBox x:Name="SearchBox" Grid.Column="1" Height="30" MinWidth="160" Padding="8,4"
                     TextChanged="SearchBox_TextChanged" ToolTip="Поиск с начала записи: код или начало наименования"/>
```

Было: `ToolTip="Введите текст для поиска"`. Подсказка теперь говорит, что искать нужно с начала — иначе пользователь, привыкший к `LIKE`, будет искать «середину» фамилии и не поймёт, почему пусто.

### 4.8 Полный diff по `ReferenceSelectionDialog.xaml.cs`

Фрагмент только по этой задаче (в файле есть и другие незакоммиченные правки — `Tag = MdiDialogService.ManageOwnScrollTag`, ширина колонки «МОЛ» — они к поиску не относятся):

```diff
@@ -171,7 +175,14 @@ public void ConfigureExtraAction(string caption, Func<Dictionary<string, object>
         /// </summary>
         public void SetSearchText(string? text)
         {
+            // В ячейке счёт, материал или участок хранится как «1010 - Касса».
+            // В строку поиска ставим наименование: при поиске по первым символам
+            // составная подпись не совпала бы с отдельными колонками Код/Наименование.
             var value = text?.Trim();
+            var separatorIndex = value?.IndexOf(" - ", StringComparison.Ordinal) ?? -1;
+            if (separatorIndex > 0 && separatorIndex + 3 < value!.Length)
+                value = value[(separatorIndex + 3)..].Trim();
+
             if (string.IsNullOrEmpty(value))
                 return;
 
@@ -202,11 +213,55 @@ public void ConfigureExtraAction(string caption, Func<Dictionary<string, object>
                 // коллекции рассинхронизирует генератор DataGrid (лог 09:49).
                 return new List<Dictionary<string, object>>(_items);
 
+            var query = search.Trim();
+            if (query.Length == 0)
+                return new List<Dictionary<string, object>>(_items);
+
             return _items
-                .Where(item => item.Values.Any(v => v?.ToString()?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
+                .Where(item => item.Any(pair => IsSearchableKey(pair.Key) && MatchesSearch(pair.Value, query)))
                 .ToList();
         }
 
+        /// <summary>
+        /// Поиск «по первым символам»: значение подходит, если оно начинается
+        /// с введённого текста. Раньше здесь был Contains (аналог LIKE '%текст%'):
+        /// одна буква вытаскивала половину справочника, и по списку нельзя было
+        /// понять, где именно совпадение. Теперь поведение как в прежних
+        /// справочниках — набираете код или начало наименования.
+        /// </summary>
+        private static bool MatchesSearch(object? value, string query)
+        {
+            var text = value?.ToString()?.Trim();
+            if (string.IsNullOrEmpty(text))
+                return false;
+
+            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
+                return true;
+
+            // Составная подпись «1010 - Касса»: код и наименование могут лежать
+            // в одной ячейке (резолв ссылок через DisplayPattern). Тогда
+            // наименование ищем с начала — после разделителя.
+            foreach (var separator in DisplaySeparators)
+            {
+                var index = text.IndexOf(separator, StringComparison.Ordinal);
+                if (index < 0)
+                    continue;
+
+                var tail = text[(index + separator.Length)..].TrimStart();
+                if (tail.StartsWith(query, StringComparison.OrdinalIgnoreCase))
+                    return true;
+            }
+
+            return false;
+        }
+
+        /// <summary>Служебные колонки в поиске не участвуют: их не видно в таблице.</summary>
+        private static bool IsSearchableKey(string key) =>
+            !key.Equals("Id", StringComparison.OrdinalIgnoreCase) &&
+            !key.StartsWith("__", StringComparison.Ordinal);
+
+        private static readonly string[] DisplaySeparators = { " - ", " – ", " — " };
+
         private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
```

---

## 5. Решения и почему именно так

| Решение | Причина |
|---|---|
| `StartsWith`, а не `Contains` | Требование пользователя: «по первому символу, а не по совпадению like». Классическое поведение справочников в прежней программе: набираешь код или начало наименования. |
| `StringComparison.OrdinalIgnoreCase` | Регистр не важен, но без «культурных» сюрпризов `CurrentCulture` (турецкая i и т. п.); для кириллицы `OrdinalIgnoreCase` корректно сворачивает регистр. |
| Префикс после `" - "` / `" – "` / `" — "` | Ссылочные поля приходят одной подписью «код - наименование»; без этого «Касса» перестала бы находить счёт 1010 в окнах, где счёт — ссылка. |
| Пропуск `Id` и `__…` | Их нет в таблице; совпадение по GUID/служебному маркеру пользователь не может объяснить. |
| Нормализация в `SetSearchText` | Предзаполнение из ячейки — «1010 - Касса»; при поиске с начала такая строка не совпала бы ни с колонкой «Код», ни с «Наименование». |
| `Trim()` и ранний выход на пустом запросе | Пробелы не должны «фильтровать всё»; пустой запрос = полный список (как раньше). |
| Новый `List` в `return` | Сохранено намеренно: мутация списка, привязанного к `ItemsGrid`, рассинхронизирует генератор `DataGrid` (это уже ловили — «лог 09:49»). |
| Порядок строк не сортируется по релевантности | В диалоге порядок задаётся источником (план счетов — по коду). Сортировка «умных» совпадений меняла бы привычный порядок; при префиксном поиске это не нужно. |

Что сознательно **не** менялось: `RebuildGrid`, `ReloadItemsAsync`, `ApplySort` (пустой), ширины и заголовки колонок, кнопки «Добавить/Изменить/Выбрать», поведение MDI-вкладки.

---

## 6. Проверка

### 6.1 Логика фильтра

SDK на машине есть (`C:\Program Files\dotnet`, 8.0.425), но проект целиком проверялся сборкой (см. 6.2). Отдельно алгоритм прогнан изолированно компилятором .NET Framework (`csc.exe`, C# 5) — код `MatchesSearch` перенесён один-в-один, без `?.` и range-операторов:

```csharp
    private static readonly string[] DisplaySeparators = { " - ", " – ", " — " };

    private static bool MatchesSearch(object value, string query)
    {
        var raw = value == null ? null : value.ToString();
        var text = raw == null ? null : raw.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var separator in DisplaySeparators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index < 0)
                continue;

            var tail = text.Substring(index + separator.Length).TrimStart();
            if (tail.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
```

Кейсы и результат прогона:

| Значение | Запрос | Ожидание | Факт |
|---|---|---|---|
| `Касса` | `кас` | да | OK |
| `Касса` | `Касса` | да | OK |
| `Главная касса` | `кас` | нет | OK |
| `1010 - Касса` | `1010` | да | OK |
| `1010 - Касса` | `касса` | да (после разделителя) | OK |
| `1010 - Касса` | `10` | да | OK |
| `2010` | `10` | нет | OK |
| `Склад №1` | `склад` | да | OK |
| `Склад №1` | `№1` | нет | OK |
| `` (пусто) | `а` | нет | OK |
| `null` | `а` | нет | OK |
| `DBNull.Value` | `а` | нет | OK |
| `Иванов Иван` | `иван` | да | OK |
| `Петров Иван` | `иван` | нет | OK |

Вывод прогона: `ИТОГ: все проверки прошли` (14 из 14).

### 6.2 Сборка

Сборка запускается системным SDK, а не тем `dotnet` из PATH (в PATH — runtime AutoClaw без SDK, `DOTNET_ROOT` указывает на него). Кроме того, в окружении терминала нет `APPDATA` — без неё NuGet падает с `Value cannot be null (Parameter 'path1')`. Рабочая команда:

```powershell
$env:DOTNET_ROOT = $null
$env:APPDATA     = "C:\Users\admin\AppData\Roaming"
$env:LOCALAPPDATA= "C:\Users\admin\AppData\Local"
$env:USERPROFILE = "C:\Users\admin"
& "C:\Program Files\dotnet\dotnet.exe" build "BIS.ERP.csproj" -nologo -v m
```

Результат: **0 ошибок**, предупреждения — только прежнее `CS0414` в `Views/AccountingReportsView.xaml.cs:34` (`_trialBalanceVariantsLoaded`). Версия продукта поднялась до `1.1.890` (инкремент делает `Build/bump-version.ps1` через `Directory.Build.props`).

Важно: во время проверки приложение было запущено (`BIS.ERP`, PID 22156) и держало `bin\Debug\net8.0-windows\BIS.ERP.exe`, поэтому обычная сборка падала на копировании apphost (`MSB3027`: файл занят). Контрольная сборка сделана в отдельную папку — `-p:OutDir=bin\check\` — она прошла с 0 ошибок; для проверки в интерфейсе приложение нужно закрыть и пересобрать в `bin`.

---

## 7. Как проверить руками

1. Закрыть запущенный `BIS.ERP`, пересобрать (`dotnet build`), запустить.
2. «План счетов» → в любом документе ТМЗ строка → «?» у «Дебет»:
   - `10` → только счета, начинающиеся с 10; `2010` в списке нет;
   - `Касса` → 1010 «Касса» находится (в т. ч. если счёт приходит подписью «1010 - Касса»);
   - `асса` → пусто (середина слова не ищется).
3. «Поступление ТМЗ» → «?» у «Материал»: в ячейке был `А-001 - Гвоздь 100` → окно открывается с поиском `Гвоздь 100` и уже отфильтрованным списком.
4. То же для «Участок» (в ячейке `01 - Склад` → поиск `Склад`) и «МОЛ».
5. Поиск по фамилии: в справочнике МОЛ `Иванов` находит «Иванов И.И.», «Петров Иван» — нет.
6. «Пары счетов» (авансовые платежи): поиск по коду счёта и по началу наименования, кнопки «Добавить/Изменить» и добавление записи не сломались — после сохранения список перечитывается с тем же текстом поиска.

---

## 8. Что осталось на прежнем поведении

Изменился только `ReferenceSelectionDialog`. По-прежнему ищут по вхождению:

| Место | Что это |
|---|---|
| `Services/ReferenceComboBoxSearchHelper.cs` (`Matches` → `Contains`) | выпадающие списки-`ComboBox` справочников в диалогах (организации, банки, валюты, сотрудники, материалы, виды оплаты и т. п.) |
| `Views/CatalogDataView.xaml.cs` (`BuildGenericSearchFilter` → `LIKE '%…%'`) | список самого справочника (не окно выбора) |
| `Views/Controls/AccountsDataView.xaml.cs:153` (`LIKE`) | список плана счетов |
| `Views/AccountingReportsView.xaml.cs:633` (`LIKE`) | поиск в отчётах |
| `Views/PostingsView.xaml.cs:141-143`, `Views/PostingsJournalView.xaml.cs:145-147`, `Views/PaymentOrderWorkView.xaml.cs:261-263`, `Views/InvoiceWorkView.xaml.cs:222-224`, `Views/CashOrderWorkView.xaml.cs:426-428`, `Views/FinanceDocumentWorkView.xaml.cs:141-143`, `Views/CatalogDataView.xaml.cs:2281` | поиск по счёту в журналах: цифры — по началу кода, текст — по началу строки (уже префикс) |
| `Views/Dialogs/AccountTurnoverSelectionDialog.xaml.cs:177-201` | фильтр оборотов по счёту: код — по началу |

Если нужно единообразие — переводить на префикс надо `ReferenceComboBoxSearchHelper` (одна строка `Contains` → `StartsWith` в `Matches`) и `CatalogDataView` (там `RowFilter` с `LIKE`, придётся собирать выражение вида `CONVERT(...) LIKE 'текст%'`, экранируя `%`, `*`, `[`, `]`).

## 9. Откат

Только эта правка:

```powershell
git checkout -- Views/Dialogs/ReferenceSelectionDialog.xaml
```

По `.xaml.cs` файл содержит и другие незакоммиченные правки, поэтому `git checkout` вернёт и их. Точечно: заменить в `FilterItems` вызов `MatchesSearch` обратно на `Contains` по `item.Values`, удалить `MatchesSearch`/`IsSearchableKey`/`DisplaySeparators` и вернуть прежний `SetSearchText` (листинги — в разделах 4.1, 4.6).
