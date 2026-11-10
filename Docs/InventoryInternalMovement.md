# Внутреннее перемещение ТМЗ

В шапке формы две равные карточки по образцу эталона. Слева «Поставщик» (откуда передаются ТМЗ), справа «Потребитель» (куда передаются ТМЗ). В каждой карточке есть «Участок» и «МОЛ» с ручным вводом и кнопкой выбора «?».

## Изменения в коде

- `Views/Dialogs/InventoryDocumentDialog.xaml` даёт имена правой карточке и её колонке. Для внутреннего перемещения код меняет заголовок правой карточки на «Потребитель» и выравнивает ширину обеих колонок. Блок «Реквизиты проведения» удалён из формы перемещения.
- `IsInternalMovement` в `InventoryDocumentDialog.xaml.cs` проверяет таблицу определения документа (`doc_inventory_movement`). Условие применяется к операциям «Передача в подотчет» и «Автоматическая передача». Поступление и выбытие идут через прежние `BuildSupplierCard` и `BuildTaxCard`.
- `BuildMovementCards` использует общий `AddReference`. Справочники «Участки» и «МОЛ» доступны с обеих сторон. При выборе участка список также показывает закреплённого МОЛ. МОЛ выбирается из списка выбранного участка. Если МОЛ один, он подставляется при выборе участка.
- `LoadMovementSource` восстанавливает значения поставщика из `site_id` и `responsible_person_id` строк `doc_inventory_movement_lines`. Поле шапки заполняется, если у всех строк один и тот же заданный идентификатор. При разных значениях соответствующее поле шапки остаётся пустым, а старые значения видны в строках.
- `ApplyMovementParties` проверяет участок и МОЛ каждой стороны и переносит поставщика во все строки. Участок и МОЛ в строках отображаются для сверки. Если у старого документа в строках разные отправители, перед повторной записью нужно выбрать общего поставщика для всех строк.
- `ApplyExistingValues` и `BuildItemData` читают и записывают потребителя в существующие `destination_site_id` и `destination_person_id` таблицы `doc_inventory_movement`. При записи оба поля обязательны и проверяются по справочникам. Запись шапки и строк использует существующие методы `CreateDynamicRecordAsync` и `UpdateDynamicRecordAsync`.

Схема БД сохраняется. Налоговые поля убраны из шапки перемещения. Исторические суммы строк сохраняются.

## Ручная проверка

1. Создать внутреннее перемещение и указать участки и МОЛ обеих сторон. Добавить две строки материалов и записать документ.
2. Открыть документ повторно и сверить получателя в шапке и отправителя в обеих строках.
3. Открыть старый документ с разными участками отправления по строкам и проверить, что общий участок пуст, а значения строк сохранены.
4. Проверить передачу между разными МОЛ одного участка. Проверить автоподстановку при одном МОЛ, список при нескольких МОЛ и отказ в записи при попытке ввести МОЛ другого участка.
5. Открыть поступление и выбытие и проверить привычные карточки поставщика и налогов.

## Счета по данным FoxPro

В форме `fma1_31.SCT` (проект `E:\FOX\mat_vpf9_ECF`) при подборе остатка код устанавливает `sch_cred = NAL_Z.schet`. Если `sch_deb` пуст, код присваивает ему `sch_cred`. Форма FoxPro также имеет отдельные поля обоих счетов в строке. Проверка совпадения в FoxPro сравнивает счёт, участок, МОЛ и заказ.

В BIS.ERP колонка строки называется «Счёт ТМЗ». Она хранит существующий `account_code`. Для нового перемещения `MetadataService` использует его и для кредита, и для дебета. Для старых документов с сохранённым `counterparty_account` сервис использует его как дебет для совместимости. Одинаковые счета допустимы, если заданный участок или МОЛ получателя отличается от отправителя в строке. Выбор двух разных счетов в новой строке и автоматический выбор счёта по остатку FoxPro в этой правке не реализованы. Проводки сохраняются в существующий журнал, а участки и МОЛ хранятся в документе.

## Время загрузки и лог

В присланном замере главное окно готово через 80,4 с от запуска, в том числе окно входа с вводом заняло 11,8 с. Замеры после открытия рабочего окна отдельные: журнал перемещения 28,9 с (из них 15,4 с на данные), карточка 15,1 с. Одного запуска недостаточно для сравнения с предыдущей версией.

- Карточка перемещения пропускает справочники организаций, типов поставки, налогов и видов оплаты. Аналитика счетов теперь замеряется отдельно.
- Журнал пропускает справочник для пустого ссылочного поля, читает каждый нужный справочник один раз за загрузку и отдельно замеряет аналитику счетов и чтение справочников.
- При повторной проверке кассовых отчётов PostgreSQL получает фильтр по кодам и именам отчётов, а также по именам и типам их источников. Поиск по имени сохраняет поддержку ранее созданных отчётов.
- `StartupProfiler.BeginRun` пишет начальную строку в прежнем формате. `SystemLogService` добавляет пустую строку перед ней, если файл за день уже содержит записи. Разделитель и строка запуска дописываются вместе под блокировкой логгера.

Для проверки проведения создать перемещение с положительной суммой и одним счётом ТМЗ, изменить участок или МОЛ получателя и проверить пару счетов в проводке. Для проверки лога запустить приложение дважды в один день и сверить пустую строку перед второй записью «Вход в запуск приложения».

## Код до и после

Ниже приведены изменённые фрагменты. Для формы «было» относится к первой версии до снятия блока «Реквизиты проведения».

### Шапка перемещения

Было в `InventoryDocumentDialog.xaml`:

```xml
<GroupBox x:Name="MovementAccountCard" Header="Реквизиты проведения"
          Margin="0,10,0,0" Padding="8" Visibility="Collapsed">
    <StackPanel x:Name="MovementAccountPanel"/>
</GroupBox>
```

Было в `BuildMovementCards`:

```csharp
MovementAccountCard.Visibility = Visibility.Visible;
AddReference(MovementAccountPanel, "Счет", "counterparty_account", accounts);
AddReference(MovementAccountPanel, "Организация", "organization_id", organizations);
AddReference(MovementAccountPanel, "Сотрудник", "employee_id", employees);
```

Стало. Блок XAML удалён. В двух карточках используются поля поставщика и потребителя, а счёт отображается в строках.

```csharp
SecondPartyCard.Header = "Потребитель";
SecondPartyColumn.Width = new GridLength(1, GridUnitType.Star);
AddReference(SupplierPanel, "Участок", "site_id", sites);
AddReference(SupplierPanel, "МОЛ", "responsible_person_id", responsible);
```

Далее в том же методе после настройки редактирования строк:

```csharp
AddReference(TaxPanel, "Участок", "destination_site_id", sites);
AddReference(TaxPanel, "МОЛ", "destination_person_id", responsible);

AccountColumn.Header = "Счёт ТМЗ";
```

### Счета проводки

Было в `MetadataService.cs`:

```csharp
var debit = isIncoming ? lineAccount : headerAccount;
var credit = isIncoming ? headerAccount : lineAccount;

if (debit.Length == 0 || credit.Length == 0 ||
    string.Equals(debit, credit, StringComparison.OrdinalIgnoreCase))
{
    skipped++;
    continue;
}
```

Стало:

```csharp
var debit = isIncoming || (isMovement && headerAccount.Length == 0) ? lineAccount : headerAccount;
var credit = isIncoming ? headerAccount : lineAccount;

// Внутреннее перемещение может идти по одному счёту при смене участка или МОЛ.
var changesCustody = isMovement &&
    (destinationSite.HasValue && destinationSite != line.SiteId ||
     destinationPerson.HasValue && destinationPerson != line.ResponsiblePersonId);
if (debit.Length == 0 || credit.Length == 0 ||
    (string.Equals(debit, credit, StringComparison.OrdinalIgnoreCase) && !changesCustody))
{
    skipped++;
    continue;
}
```

### Загрузка карточки и журнала

Было в `InitializeAsync` карточки. Справочники для шапки загружались для всех видов ТМЗ.

```csharp
var organizations = await LoadRowsAsync(catalogs, "Организации");
var supplyKinds = await LoadRowsAsync(catalogs, "Типы поставки");
var taxRows = await LoadRowsAsync(catalogs, "Налоги");
_taxRows = taxRows;
var paymentKinds = await LoadRowsAsync(catalogs, "Виды оплаты");
```

Стало. Эти запросы выполняются только в ветке поступления и выбытия.

```csharp
if (IsInternalMovement)
{
    BuildMovementCards(sites);
}
else
{
    var organizations = await LoadRowsAsync(catalogs, "Организации");
    var supplyKinds = await LoadRowsAsync(catalogs, "Типы поставки");
    var taxRows = await LoadRowsAsync(catalogs, "Налоги");
    var paymentKinds = await LoadRowsAsync(catalogs, "Виды оплаты");
    _taxRows = taxRows;
    BuildSupplierCard(accounts, organizations, employees, supplyKinds);
    var documentDate = GetDocumentDate();
    var vatItems = await BuildTaxItemsAsync(TaxKind.Vat, taxRows, documentDate);
    var salesItems = await BuildTaxItemsAsync(TaxKind.Sales, taxRows, documentDate);
    BuildTaxCard(vatItems, salesItems, BuildPaymentItems(paymentKinds));
}
```

Было в `DynamicDocumentWorkView.LoadDataCore`. Для каждого ссылочного поля выполнялось чтение справочника.

```csharp
var catalogData = await metadata.ReadCatalogDataAsync(catalog);
```

Стало. Сначала проверяется, есть ли заполненные ссылки. Для нужного справочника повторно используются строки, уже прочитанные при загрузке этого журнала.

```csharp
if (!data.Any(row => row.TryGetValue(field.Name, out var value) &&
    value is not null and not DBNull && !string.IsNullOrWhiteSpace(value.ToString())))
    continue;
```

```csharp
if (!catalogRows.TryGetValue(catalog.Id, out var catalogData))
{
    catalogData = await StartupProfiler.MeasureAsync($"Журнал документов: справочник {catalog.Name}",
        () => metadata.ReadCatalogDataAsync(catalog));
    catalogRows.Add(catalog.Id, catalogData);
}
```

### Проверка кассовых отчётов при запуске

Было в `EnsureCashOrderReportsAsync`:

```csharp
var reports = await _context.Reports.ToListAsync();
var sources = await _context.MetadataObjects.AsNoTracking().Include(item => item.Fields).ToListAsync();
```

Стало:

```csharp
var codes = definitions.Select(item => item.Code).ToArray();
var names = definitions.Select(item => item.Name).ToArray();
var sourceNames = definitions.Select(item => item.SourceName).ToArray();
var sourceTypes = definitions.Select(item => item.SourceObjectType).ToArray();
var reports = await _context.Reports
    .Where(item => codes.Contains(item.Code) || names.Contains(item.Name))
    .ToListAsync();
var sources = await _context.MetadataObjects.AsNoTracking()
    .Where(item => sourceNames.Contains(item.Name) && sourceTypes.Contains(item.ObjectType))
    .Include(item => item.Fields)
    .ToListAsync();
```

### Разделитель в системном логе

Было в `App.OnStartup`:

```csharp
StartupProfiler.Mark("Вход в запуск приложения");
```

Стало:

```csharp
StartupProfiler.BeginRun();
```

Новый метод в `StartupProfiler`:

```csharp
public static void BeginRun()
{
    Total.Restart();
    SystemLogService.WriteStartupEntry("⏱ Вход в запуск приложения: 0 мс (с запуска 0 мс)");
}
```

Было внутри блокировки в `SystemLogService.Write`:

```csharp
File.AppendAllText(MainLogFilePath, line, Encoding.UTF8);
```

Стало. Флаг `separateRun` передаёт только `WriteStartupEntry`.

```csharp
var path = MainLogFilePath;
if (separateRun && File.Exists(path) && new FileInfo(path).Length > 0)
    line = Environment.NewLine + line;

File.AppendAllText(path, line, Encoding.UTF8);
```

## Где смотреть исходник FoxPro

- Проект `E:\FOX\mat_vpf9_ECF`. В [zap_tdoc_txt.prg](<E:/FOX/mat_vpf9_ECF/zap_tdoc_txt.prg:136>) операция **«Передача в подотчет»** зарегистрирована с именем формы **`fma1_31`**. В описании прямо указано, что в шапке выбираются поставщик и потребитель.
- Открыть в Visual FoxPro командой `MODIFY FORM "E:\FOX\mat_vpf9_ECF\fma1_31.scx"`. Файл [fma1_31.scx](<E:/FOX/mat_vpf9_ECF/fma1_31.scx>) содержит форму, а парный [fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT>) — её мемо-поля с исходным кодом. Для просмотра событий удобнее конструктор формы. Указанные ниже строки SCT — ориентиры текстового поиска в бинарном файле.
- **`Formset2.Form1.Activate`**. Искать `sch_cred with NAL_Z.schet` в [fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:121>). В ветке `AKT2=2` выполняются следующие присваивания.

```foxpro
if AKT2=2
   repl cena WITH nal_z.cena_ed,;
      sch_cred with NAL_Z.schet,sum with nal_z.cena_ed*kol,;
      c_zakaz WITH nal_z.k_zakaz,nc_zakaz WITH nal_z.n_zakaz
   if empty(sch_deb)
      repl sch_deb with sch_cred
   endif
endif
```

- **`Formset2.Form1.Grid1`**. У колонки `Column9` привязка `matsw2.sch_deb`, у `Column10` — `matsw2.sch_cred`. Это два отдельных поля строки. [Привязки колонок в fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:974>).
- **`Formset2.Form1.Command3.Click`**. Обработчик записи проверяет пустые счета и ищет строку, в которой одновременно совпадают дебет и кредит, участок, МОЛ и заказ. [Проверка в fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:396>).

```foxpro
LOCATE FOR sch_deb=sch_cred.AND.d_uch=c_uch.and.d_tnm=c_tnm.and.d_zakaz=c_zakaz
```

## Связь участка и МОЛ в передаче под отчёт

| Правило | FoxPro | BIS.ERP после правки |
| --- | --- | --- |
| Выбор участка | Валидация в справочнике `spr_uch` | Участок разрешается в UUID справочника |
| Выбор МОЛ | По составному ключу участка и табельного номера в `spr_mol` | Кнопка «?» показывает МОЛ выбранного участка. Ручной ввод ищет однозначное совпадение по номеру или ФИО в пределах участка |
| Смена участка | Единственный МОЛ подставляется автоматически | Единственный МОЛ подставляется. При нескольких сохраняется выбранный МОЛ, если он относится к участку, иначе поле очищается |
| Запись документа | Обе пары записываются в каждую строку `MATSW2` | Обе пары проверяются до записи. Отправитель записывается в каждую строку, получатель — в шапку документа |
| Подбор по наличию | `NAL_Z` фильтруется по участку и МОЛ поставщика | Подбор пока использует справочник материалов. В проекте нет готового количественного регистра остатков ТМЗ по участку и МОЛ для эквивалента `NAL_Z` |

### FoxPro

В `Formset2.Form1.KOD_UCHP.Valid` поиск единственного МОЛ идёт по индексу `UCH_TNM`. Фрагмент [fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:1620>).

```foxpro
sele spr_mol
     SET ORDER TO TAG UCH_TNM
     seek STR(m.kod_UCH,4)
	 coun rest while kod_uch=m.kod_uch to i
	 m.tnm=0
	 m.fio=''
	 do case
	 case i=1
      seek STR(m.kod_UCH,4)
	  scat memv fiel tnm,fio
```

В `Formset2.Form1.MOLP.Valid` принадлежность МОЛ проверяется составным ключом. Для получателя аналогичная проверка находится в `Formset2.Form1.mol.Valid`. Фрагмент [fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:1724>).

```foxpro
case !seek(STR(m.kod_UCH,4)+STR(m.TNM,5))
     messagebox('═хЄ Єръюую ╠╬╦ !')
     thisform.molp.value=thisform.text12.value
     retu
```

Фильтр наличия в форме. [fma1_31.SCT](<E:/FOX/mat_vpf9_ECF/fma1_31.SCT:682>)

```foxpro
uch1=o.KOD_UCHP.VALUE
mol1=o.molP.VALUE
filtnal='nal_z.uch='+ALLTRIM(str(uch1,4))+'.and.nal_z.tnm='+ALLTRIM(str(mol1,5))
```

### C# в BIS.ERP

До правки обе карточки создавались независимыми вызовами `AddReference`. При записи `ApplyMovementSource` разносил по строкам заполненные поля поставщика. Принадлежность МОЛ участку не проверялась.

Теперь [InventoryDocumentDialog.xaml.cs](<D:/C#/BIS.ERP/BIS.ERP/Views/Dialogs/InventoryDocumentDialog.xaml.cs>) строит обе карточки через `BuildMovementParty`. Используются имена `SourceSiteColumn`, `SourcePersonColumn`, `DestinationSiteColumn` и `DestinationPersonColumn`. Они ссылаются на существующие столбцы BIS.ERP. Мнемоники FoxPro выше приведены для навигации по эталону.

```csharp
private void UpdateMovementPartyPerson(string siteColumn, string personColumn)
{
    var siteId = ResolveMovementReference(siteColumn);
    var currentPersonId = ResolveMovementReference(personColumn);
    var sitePersons = _responsiblePersons.Where(person => siteId.HasValue && person.SiteId == siteId).ToList();
    var person = sitePersons.Count == 1
        ? sitePersons[0]
        : sitePersons.FirstOrDefault(option => option.Id == currentPersonId);
    _referenceBoxes[personColumn].Text = person?.Display ?? string.Empty;
    _references[personColumn] = person?.Id;
}
```

```csharp
private (Guid SiteId, Guid PersonId) ReadMovementParty(string label, string siteColumn, string personColumn)
{
    if (ResolveMovementReference(siteColumn) is not Guid siteId)
        throw new InvalidOperationException($"«{label}»: выберите участок из справочника.");

    if (ResolveMovementReference(personColumn) is not Guid personId ||
        !_responsiblePersons.Any(person => person.Id == personId && person.SiteId == siteId))
        throw new InvalidOperationException($"«{label}»: выберите МОЛ указанного участка.");

    return (siteId, personId);
}
```

```csharp
private void ApplyMovementParties()
{
    var source = ReadMovementParty("Поставщик", SourceSiteColumn, SourcePersonColumn);
    var destination = ReadMovementParty("Потребитель", DestinationSiteColumn, DestinationPersonColumn);
    _references[DestinationSiteColumn] = destination.SiteId;
    _references[DestinationPersonColumn] = destination.PersonId;

    foreach (var line in _lines)
    {
        line.SiteId = source.SiteId;
        line.SiteText = FormatLoadedSiteText(source.SiteId);
        line.ResponsiblePersonId = source.PersonId;
        line.ResponsiblePersonText = FormatLoadedResponsibleText(source.PersonId);
    }
}
```
