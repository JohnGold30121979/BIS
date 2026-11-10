# Поступление ТМЗ. Зависимости от вида операции

Источник сравнения — проект `E:\FOX\mat_vpf9_ECF`. В [zap_tdoc_txt.prg](<E:/FOX/mat_vpf9_ECF/zap_tdoc_txt.prg:9>) зарегистрированы формы покупки `fma1_11` и `fma1_11_09` и форма прихода из производства `fma1_12`. Для покупки сверялась версия с налогом с продаж. В Visual FoxPro формы открываются командами `MODIFY FORM "E:\FOX\mat_vpf9_ECF\fma1_11_09.scx"` и `MODIFY FORM "E:\FOX\mat_vpf9_ECF\fma1_12.scx"`. Код событий хранится в парных файлах SCT. Номера строк SCT служат ориентирами для текстового поиска.

| Операция | Что видно в FoxPro | Что делает BIS.ERP |
| --- | --- | --- |
| Приобретение ТМЗ | Поставщик, счёт, вид покупки, оплата, налоги, счёт-фактура. Дебет задаётся по материалу в строке | Сохраняет существующую шапку покупки, расчёт налогов и кредит из счёта поставщика. Распределение расходов и отдельные налоговые проводки FoxPro этой правкой не переносились |
| Приход из производства | Участок и МОЛ получателя в строке. Дебет и кредит отдельно по каждой строке. Пустой дебет заполняется из карточки материала | Покупные реквизиты скрыты. Налоги покупки при записи обнуляются. Дебет по умолчанию берётся из «Счёта хранения» материала, кредит вводится в строке. Оба счёта проверяются по плану счетов |
| Приход по акту | Отдельная форма с таким названием в проверенных исходниках не найдена. Форма инвентаризации отбирает расхождения фактического и учётного количества для печати | Операция BIS.ERP требует основание акта и счёт кредита. Дебет остаётся в строке. Поля покупки скрыты, её налоги не начисляются. Операция не создаётся автоматически из инвентаризации |

Вид операции определяется по стабильному коду записи справочника. В C# используются имена `PurchaseOperation`, `ProductionReceiptOperation` и `ReceiptByActOperation`. Ранее сохранённые коды и ссылки на справочник остаются действительными. Изменения формы применяются к документу «Поступление ТМЗ».

## FoxPro

### Приобретение

`fma1_11_09.SCT` содержит отдельные реквизиты покупки и налогов. В примере суммы транспорта, таможни и прочих расходов добавляются к соответствующим итогам при совпадении счетов. [Исходный расчёт](<E:/FOX/mat_vpf9_ECF/fma1_11_09.SCT:4730>).

```foxpro
IF thisform.sch_tr.value=thisform.debt.value
   REPLACE matdoc2.ssum WITH matdoc2.ssum+matdoc2.ssum_tr+matdoc2.ssum_tam+matdoc2.ssum_itd,;
           matdoc2.ssumnds WITH matdoc2.ssumnds+matdoc2.sndc_tr+matdoc2.sndc_tam+matdoc2.sndc_itd,;
           matdoc2.ssumnal WITH matdoc2.ssumnal+matdoc2.snal_tr+matdoc2.snal_tam+matdoc2.snal_itd,;
           matdoc2.ssumsum WITH matdoc2.ssum+matdoc2.ssumnds+matdoc2.ssumnal
```

### Приход из производства

Пустой дебет заполняется из справочника материалов. В таблице формы дебет и кредит привязаны к разным полям строки. При записи проверяются оба счёта. [Подстановка дебета](<E:/FOX/mat_vpf9_ECF/fma1_12.SCT:82>) · [Колонки счетов](<E:/FOX/mat_vpf9_ECF/fma1_12.SCT:1074>) · [Проверка при записи](<E:/FOX/mat_vpf9_ECF/fma1_12.SCT:630>).

```foxpro
IF EMPTY(SCH_DEB)
 REPL sch_dEB with DESGOD6.schet
ENDIF
```

```foxpro
LOCATE FOR EMPTY(sch_deb)
IF FOUND()
	MESSAGEBOX('Нет счета по дебету ',0,'Сообщение')
	RETURN .F.
ENDIF
loCATE FOR EMPTY(sch_cred)
IF FOUND()
	MESSAGEBOX('Нет счета по кредиту ',0,'Сообщение')
	RETURN .F.
ENDIF
```

### Инвентаризация как возможное основание акта

Форма `fma1_inv.scx` отбирает расхождения для печати. Этот код подтверждает вычисление расхождения, но сам по себе не подтверждает создание приходного документа. [Отбор расхождений](<E:/FOX/mat_vpf9_ECF/fma1_inv.SCT:1624>).

```foxpro
DO CASE 
   CASE CH2=1 && ИЗЛИШКИ или НЕДОСТАЧА
	       FILTR='(KOL_R-KOL_AV>0).or.(KOL_R-KOL_AV<0)' 
	  CASE CH2=2 && ВСЕ
        FILTR='.T.' 
ENDCASE

SELECT INVENT2
SCAN FOR &FILTR
```

## BIS.ERP. Что изменено

### Форма и проверка

До правки три операции строили общую шапку поставщика и налогов. При проведении кредит для всех трёх брался из шапки. Теперь `InventoryDocumentDialog.xaml.cs` переключает поля при выборе операции. Скрытые реквизиты покупки для производства и акта очищаются при записи. Действующие исторические суммы остаются видны при открытии документа и пересчитываются при смене операции или повторной записи без налогов покупки.


```csharp
private void UpdateReceiptOperation(bool recalculate = false)
{
    var showPurchaseFields = !IsReceiptWithoutPurchaseTaxes;
    SecondPartyCard.Visibility = showPurchaseFields ? Visibility.Visible : Visibility.Collapsed;
    SetPanelVisibility("supply_kind", showPurchaseFields);
    SupplierCard.Header = showPurchaseFields ? "Поставщик" : "Источник поступления";
    _referencePanels["counterparty_account"].Children.OfType<TextBlock>().First().Text =
        showPurchaseFields ? "Счет" : "Счёт кредита";
    SupplierCard.Visibility = IsProductionReceipt ? Visibility.Collapsed : Visibility.Visible;

    SetEditorVisibility("tax_blank_number", showPurchaseFields);
    SetEditorVisibility("esf_number", showPurchaseFields);
    SetEditorVisibility("basis", IsReceiptByAct);
    ReceiptCreditAccountColumn.Visibility = IsProductionReceipt ? Visibility.Visible : Visibility.Collapsed;
    ReceiptVatColumn.Visibility = SecondPartyCard.Visibility;
    ReceiptSalesTaxColumn.Visibility = SecondPartyCard.Visibility;
    ReceiptBaseAmountColumn.Header = showPurchaseFields ? "Без НДС" : "Сумма";

    if (recalculate)
    {
        SetReceiptLineTaxRates();
        UpdateAccountDependencies();
    }
}
```

```csharp
private void ValidateReceiptOperation()
{
    if (IsReceiptByAct)
    {
        if (_editors["basis"] is TextBox basis && string.IsNullOrWhiteSpace(basis.Text))
            throw new InvalidOperationException("Укажите основание прихода по акту.");

        if (FindAccountRow() == null)
            throw new InvalidOperationException("Выберите счёт кредита для прихода по акту.");
    }

    if (IsProductionReceipt)
    {
        var accountCodes = _accountRows.Select(account => Read(account, "Код", "code"))
            .Where(code => code.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in _lines)
        {
            line.CreditAccountCode = line.CreditAccountCode.Trim();
            if (!accountCodes.Contains(line.CreditAccountCode) || !accountCodes.Contains(line.AccountCode) ||
                string.Equals(line.CreditAccountCode, line.AccountCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Строка {line.LineNumber}: укажите разные счета дебета и кредита из плана счетов.");
        }
    }

    if (IsReceiptWithoutPurchaseTaxes)
        SetReceiptLineTaxRates();
}
```

### Запись строк и проведение

`InventoryDocumentLineService` читает и сохраняет `CreditAccountCode` в колонке `credit_account_code` только таблицы строк поступления. Миграция `2026.11.10.01` добавляет nullable-колонку существующей базе. Создание таблицы через `InventoryDocumentSchemaService` тоже включает её. Для старого производственного прихода при открытии пустые кредиты строк заполняются из прежнего счёта шапки. Сервис проведения также поддерживает этот счёт как резерв для сохранённых документов старого формата.

Было в `MetadataService.cs`:

```csharp
var credit = isIncoming ? headerAccount : lineAccount;
```

Стало:

```csharp
var credit = isIncoming ? headerAccount : lineAccount;
if (isProductionReceipt && !string.IsNullOrWhiteSpace(line.CreditAccountCode))
    credit = line.CreditAccountCode.Trim();
```

### Проверка на рабочей базе

1. В приобретении выбрать поставщика и налог, записать документ и сверить суммы и кредит из шапки.
2. В приходе из производства выбрать материал со счётом хранения. Ввести в двух строках разные счета кредита, записать и повторно открыть документ. Сверить счета в строках и в проводках.
3. Открыть производственный документ старого формата и проверить подстановку кредита из шапки. Затем записать и повторно открыть документ.
4. Для прихода по акту проверить отказ без основания и счёта кредита. После заполнения проверить сохранение основания и нулевые налоги.
5. Переключить операцию с покупки на производство и обратно. Сверить доступность полей, пересчёт и итоговые суммы.

Сборка проверена. Сценарии с рабочей базой и проведение вручную не запускались.
