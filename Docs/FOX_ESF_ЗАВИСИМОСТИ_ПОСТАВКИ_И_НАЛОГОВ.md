# FOX / ЭСФ: зависимости поставки и налогов в `InvoiceEditDialog`

Дата анализа: 30.10.2026. Этот документ описывает требуемую самостоятельную доработку; исходный код приложения им **не изменяется**.

## Итог проверки

В текущем `InvoiceEditDialog` переключатели **«Вид поставки ЭСФ»** (`DeliveryKindCombo`) и **«Тип поставки ЭСФ»** (`SupplyKindCombo`) только загружают, отображают, нормализуют старые коды и сохраняют значения в заголовок счёта-фактуры. Они не имеют `SelectionChanged`-обработчиков и не участвуют в расчёте НДС или НСП.

Расчёт выполняется независимо от поставки:

```csharp
// Services/InvoiceService.cs, RecalculateLine
line.VatAmount = Math.Round(line.AmountWithoutTax * line.VatRate / 100m, 2);
line.SalesTaxAmount = Math.Round(line.AmountWithoutTax * line.SalesTaxRate / 100m, 2);
line.LineTotal = line.AmountWithoutTax + line.VatAmount + line.SalesTaxAmount;
```

Ставки из шапки переносятся во **все** строки методом `ApplySelectedHeaderTaxesToLines()`. Он вызывается как при смене каждого налога, так и в начале `RecalculateTotals()`. Поэтому изменить ставку в строке отдельно невозможно: следующее пересчитывание вернёт значение из шапки.

## Что подтверждено в реализации Fox/ЭСФ

| Поле формы | Хранимое поле | Справочник и коды | Назначение по текущим данным |
|---|---|---|---|
| Вид поставки ЭСФ | `delivery_kind` | «Виды поставки»: `1` товары (ЭСФ `100`), `2` работы/услуги (`101`), `3` прочая (`299`) | Классификация предмета поставки для ЭСФ. Не является признаком освобождения от НДС. |
| Тип поставки ЭСФ | `supply_kind` | «Типы поставки»: `1` облагаемая (`100`), `2` необлагаемая/без НДС (`101`), `3` импорт (`200`), `4` экспорт (`300`) | Налоговый режим ЭСФ; именно он должен управлять допустимостью НДС. |
| НДС | `vat_tax_code`, `vat_rate` в строках | «Налоги», `tax_kind=VAT`, период действия `valid_from`/`valid_to` | Рассчитывается от суммы без налогов. |
| Налог с продаж (НСП) | `sales_tax_code`, `sales_tax_rate` в строках | «Налоги», `tax_kind=SALES`, период действия | Рассчитывается от суммы без налогов. |

Источники в коде: `Services/InvoiceMetadataSeedService.cs` (посев кодов), `Views/Dialogs/InvoiceEditDialog.xaml.cs` (нормализация), `Services/InvoiceService.cs` (формула). В частности, «Тип поставки» `2` прямо назван «Необлагаемая / без НДС», а не «без всех налогов».

## Важное ограничение для НСП

В найденных Fox/ЭСФ-данных нет правила, согласно которому `supply_kind=2`, импорт или экспорт должны автоматически обнулять **НСП**. Следовательно, не следует без отдельного бизнес-правила связывать НСП с типом/видом поставки. НСП должен оставаться выбранным пользователем налогом из справочника; в самом справочнике для освобождения уже предусмотрена ставка `0` (`SALES_EXEMPT`).

Если бизнес подтвердит, например, что экспорт не облагается НСП, это нужно выразить отдельной строкой матрицы и явным правилом, а не скрытым условием в формуле.

## Целевая матрица поведения

| Тип поставки | НДС | НСП | Вид поставки |
|---|---|---|---|
| `1` — облагаемая | Разрешён; применяется выбранный действующий VAT-налог | Не менять, применять выбранный НСП | Не влияет на ставки |
| `2` — необлагаемая / без НДС | Обязательно 0%; не дать вручную вернуть платный НДС | Не менять; пользователь может выбрать НСП 0% или действующую ставку по режиму | Не влияет на ставки |
| `3` — импорт | Не подменять автоматически: правило ставки должно быть подтверждено бухгалтерией | Не менять | Не влияет на ставки |
| `4` — экспорт | Не подменять автоматически: правило ставки должно быть подтверждено бухгалтерией | Не менять | Не влияет на ставки |

Такое поведение устраняет подтверждённую ошибку (необлагаемая поставка с НДС), но не выдумывает правила для импорта, экспорта и НСП.

## Вносимые изменения

### 1. Подключить событие типа поставки

В `Views/Dialogs/InvoiceEditDialog.xaml` найдите текущий `SupplyKindCombo`:

```xml
<ComboBox x:Name="SupplyKindCombo" Height="28" DisplayMemberPath="DisplayName" SelectedValuePath="Value"/>
```

и **замените его целиком**:

```xml
<ComboBox x:Name="SupplyKindCombo" Height="28"
          DisplayMemberPath="DisplayName"
          SelectedValuePath="Value"
          SelectionChanged="OnSupplyKindChanged"/>
```

`DeliveryKindCombo` обработчик добавлять не нужно: его код `1/2/3` классифицирует товары/услуги, но в источнике нет зависимости от него для налогов.

### 2. Добавить константы и защитный флаг

В `Views/Dialogs/InvoiceEditDialog.xaml.cs`, рядом с остальными приватными полями (после `_isApplyingHeaderTaxValues`), вставьте:

```csharp
private bool _isApplyingSupplyTaxRule;

private const string TaxableSupplyKindCode = "1";
private const string NonTaxableSupplyKindCode = "2";
```

### 3. Добавить поиск нулевого НДС и применение правила

В тот же класс, сразу **после** метода `ApplySelectedHeaderTaxesToLines()`, вставьте следующий код. Он намеренно меняет только НДС и не подменяет НСП.

```csharp
private ReferenceOption? GetZeroVatTax()
{
    return VatTaxItems.FirstOrDefault(item => item.Rate == 0m);
}

private void ApplySupplyKindTaxRule()
{
    if (_isApplyingSupplyTaxRule || _isReadOnlyMode)
        return;

    var supplyKind = GetSelectedReferenceValue(SupplyKindCombo);
    if (supplyKind != NonTaxableSupplyKindCode)
        return;

    var zeroVat = GetZeroVatTax();
    if (zeroVat == null)
    {
        MessageBox.Show(
            "Для типа поставки «Необлагаемая / без НДС» в справочнике «Налоги» " +
            "должна быть активная ставка НДС 0%.",
            "Не настроен НДС 0%",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return;
    }

    _isApplyingSupplyTaxRule = true;
    try
    {
        // Шапка остаётся источником истины для всех строк — это соответствует
        // существующей модели диалога.
        HeaderVatTaxCombo.SelectedValue = zeroVat.Value;
        foreach (var line in _lines)
        {
            line.VatTaxCode = zeroVat.Value;
            line.VatRate = zeroVat.Rate;
        }
    }
    finally
    {
        _isApplyingSupplyTaxRule = false;
    }
}

private void OnSupplyKindChanged(object sender, SelectionChangedEventArgs e)
{
    if (!_isInitialized || _isReadOnlyMode || _isApplyingSupplyTaxRule)
        return;

    ApplySupplyKindTaxRule();
    RecalculateTotals();
}
```

### 4. Не позволять `RecalculateTotals()` вернуть платный НДС

В начале метода `RecalculateTotals()` найдите:

```csharp
private void RecalculateTotals()
{
    ApplySelectedHeaderTaxesToLines();
```

и замените на:

```csharp
private void RecalculateTotals()
{
    ApplySupplyKindTaxRule();
    ApplySelectedHeaderTaxesToLines();
```

Это необходимо, потому что пересчёт запускается из нескольких мест (изменение суммы, добавление/удаление строки, кнопка «Пересчитать»), а не только из обработчика переключателя.

### 5. Заблокировать выбор платного НДС для необлагаемой поставки

В начале существующего `OnHeaderVatTaxChanged`, перед `ApplySelectedHeaderTaxesToLines();`, вставьте:

```csharp
if (GetSelectedReferenceValue(SupplyKindCombo) == NonTaxableSupplyKindCode &&
    GetSelectedReferenceOption(HeaderVatTaxCombo)?.Rate > 0m)
{
    ApplySupplyKindTaxRule();
    return;
}
```

Итоговый метод будет выглядеть так:

```csharp
private void OnHeaderVatTaxChanged(object sender, SelectionChangedEventArgs e)
{
    if (_synchronizingHeaderTaxSelection || _isReadOnlyMode)
        return;

    if (GetSelectedReferenceValue(SupplyKindCombo) == NonTaxableSupplyKindCode &&
        GetSelectedReferenceOption(HeaderVatTaxCombo)?.Rate > 0m)
    {
        ApplySupplyKindTaxRule();
        return;
    }

    ApplySelectedHeaderTaxesToLines();
    RecalculateTotals();
}
```

### 6. Применить правило после загрузки старого документа

При открытии существующего документа `SupplyKindCombo` заполняется до строк и до `_isInitialized = true`, поэтому его XAML-событие не применит правило. В `InitializeAsync()` найдите участок:

```csharp
RecalculateTotals();
UpdateCurrencyPanelVisibility();
_isInitialized = true;
```

и замените его:

```csharp
_isInitialized = true;
ApplySupplyKindTaxRule();
RecalculateTotals();
UpdateCurrencyPanelVisibility();
```

Это обеспечивает, что сохранённый документ с `supply_kind = "2"` не откроется с ненулевым НДС. Если такая историческая комбинация должна лишь показываться без исправления, этот шаг не применять и вместо этого вывести предупреждение пользователю.

## Настройка данных до проверки

1. В каталоге **«Налоги»** создать/включить запись VAT со ставкой `0`, `tax_kind = VAT`, актуальным интервалом действия на дату документа. Её код не должен быть захардкожен в коде: пример выше ищет ставку по `Rate == 0m`.
2. Убедиться, что у НСП настроены отдельные записи: обычная ставка и при необходимости ставка `0` (`SALES_EXEMPT`).
3. Проверить, что в «Типах поставки» существуют коды `1`–`4`; в «Видах поставки» — `1`–`3`. Их устанавливает `InvoiceMetadataSeedService`.

## Контрольные сценарии

| Сценарий | Ожидаемый результат |
|---|---|
| Новая облагаемая поставка (`1`), НДС 12%, НСП 1,5%, база 100 | НДС 12, НСП 1,50, итого 113,50 |
| Изменить тип на необлагаемый (`2`) | НДС переключается на 0, НСП остаётся выбранным, итого 101,50 в приведённом примере |
| При типе `2` вручную выбрать НДС 12% | Выбор не закрепляется: снова выбирается VAT 0% |
| При типе `2` изменить НСП | Новый НСП применяется ко всем строкам, НДС остаётся 0% |
| Выбрать вид «Работы / услуги» (`delivery_kind=2`) | Ставки не меняются; меняется только классификация ЭСФ |
| Открыть старую ЭСФ с `supply_kind=2` | После шага 6 НДС становится 0% и итоги пересчитываются |
| Поменять дату документа | Списки налогов фильтруются по `valid_from`/`valid_to`; уже сохранённый код сохраняется через `EnsureTaxCodesAreAvailableAsync` |

## Что не менять без подтверждения

- Не менять формулу `InvoiceService.RecalculateLine`: она уже корректно считает налог от `AmountWithoutTax` и должна остаться единой для сохранения, отображения и проведения.
- Не добавлять зависимость от `DeliveryKindCombo` в расчёт: текущая модель ЭСФ не подтверждает такой зависимости.
- Не обнулять НСП при `supply_kind=2` автоматически. Это отдельный режим налога, а не следствие фразы «без НДС».
- Не хардкодить `SALES_TAX`, `VAT12` или конкретный код VAT 0%; выбирать запись из каталога по виду, периоду действия и ставке.

## После бизнес-подтверждения импорта и экспорта

Если будут утверждены правила для кодов `3`/`4`, расширить `ApplySupplyKindTaxRule()` отдельными `case`-ветками. Для каждого правила должны быть зафиксированы: разрешённые код и ставка VAT, разрешённые код и ставка НСП, дата начала действия и то, нужно ли массово менять уже введённые строки. До этого эти коды должны сохраняться, но не менять налоги автоматически.
