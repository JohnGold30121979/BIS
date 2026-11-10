# Справочник материалов в FoxPro и BIS.ERP

Проверены форма `fm_des.scx`, её код `fm_des.SCT` и заголовки таблиц FoxPro. Справочник `DESGOD6.DBF` имеет одинаковый набор полей в копиях [BUXMAT1](<E:/FOX/BASE/BUXMAT1/DESGOD6.DBF>) и [BUXMAT](<E:/FOX/Базы/BUXMAT/DESGOD6.DBF>). Для просмотра формы в FoxPro используется команда `MODIFY FORM "E:\FOX\mat_vpf9_ECF\fm_des.scx"`.

## Что хранится в справочнике

| Назначение | FoxPro | BIS.ERP, таблица `catalog_materials` |
| --- | --- | --- |
| Идентификатор материала | `KOD_MAT`, числовой код | Внутренний `Id` типа UUID и уникальный строковый `code`. Документы ссылаются на UUID |
| Название | `NAME_MAT` и `NAME_MATP`, два разных поля | Короткое название `name`. Добавлено полное название `full_name` |
| Единица измерения и номенклатурный номер | `ED_IZMER`, `NOM_NOMER` | `unit`, `article`. Изменены подписи, сохранённые значения остаются в прежних колонках |
| Вид материала | `KOD_VID` и сохранённое название `NAME_VID` | `material_type_id`, ссылка на «Виды материалов» |
| Вместимость | `VMEST` | `capacity` |
| Счета хранения, дохода и себестоимости | `SCHET`, `SCH_DOX`, `SCH_SEB` | `storage_account`, `income_account`, `cost_account`. Хранятся коды счетов, выбор идёт из плана счетов |
| Классификация товаров и услуг | Код товара `KOD_TNVED`, код услуги `KOD_GKED`, код позиции классификатора `KOD_MAT_ECF` в форме | Код товара уже хранится в `tnved_code`. Добавлены `economic_activity_service_code` и `product_or_service_classification_code`. Коды хранятся строками, включая ведущие нули |

В FoxPro при выборе позиции из `TN_VED3` форма копирует её коды и служебный признак `P_USL`. В BIS.ERP новые коды доступны для ручного ввода в блоке классификации. Связь с аналогом `TN_VED3`, вычисление признака услуги и перенос вспомогательных полей старой системы в эту правку не входят.

## Остатки и документы

FoxPro хранит остатки отдельно от карточки материала. В [nal_z.DBF](<E:/FOX/mat_vpf9_ECF/nal_z.DBF>) есть дата, код материала, участок, табельный номер, счёт, количество, стоимость и цена. [nal_z.prg](<E:/FOX/mat_vpf9_ECF/nal_z.prg:44>) увеличивает количество по стороне дебета и уменьшает по стороне кредита.

В BIS.ERP карточка материала также описывает номенклатуру. В строках документов отдельно сохраняются ссылка на материал, отображаемые название и единица измерения, количество и цена. Эта правка касается справочника и его чтения в документах. Количественный регистр остатков и импорт данных FoxPro не добавлялись.

## Код FoxPro

Кнопка записи `Form2.SAVE.Click` переносит значения карточки в переменные перед сохранением. [fm_des.SCT](<E:/FOX/mat_vpf9_ECF/fm_des.SCT:230>)

```foxpro
m.name_mat = thisform.name_mat.value
m.name_matp = thisform.name_matp.value
m.ed_izmer = thisform.ed_izmer.Value
m.nom_nomer = thisform.nom_nomer.Value
```

Выбор позиции классификатора заполняет коды. [fm_des.SCT](<E:/FOX/mat_vpf9_ECF/fm_des.SCT:396>)

```foxpro
CASE AKT1=9
thisform.kod_mat_ecf.value=tn_ved3.kod
thisform.kod_gked.value=tn_ved3.kod_gked
thisform.kod_tnved.value=tn_ved3.kod_tnved
thisform.p_usl.value=tn_ved3.p_usl
```

## Код BIS.ERP

`GetMaterialFields` описывает поля для новой базы. На существующей базе `EnsureMaterialCatalogStructureAsync` сопоставляет поля по физическому имени колонки, обновляет подписи и добавляет отсутствующие колонки. UUID, коды материалов и ссылки из документов сохраняются. Новые колонки допускают пустые значения, поэтому обновление не требует заполнения старых карточек. [Описание полей](<D:/C#/BIS.ERP/BIS.ERP/Services/MetadataService.Fields.cs:349>) · [Обновление справочника](<D:/C#/BIS.ERP/BIS.ERP/Services/MetadataService.Catalogs.cs:509>)

```csharp
new MetadataField
{
    Id = Guid.NewGuid(),
    Name = "Полное наименование материала",
    DbColumnName = "full_name",
    FieldType = "String",
    Length = 500,
    IsRequired = false,
    Order = 3,
    MetadataObjectId = metadataObjectId
},
```

При записи карточки пустое полное название заполняется из короткого. Явно введённое полное название сохраняется. [CatalogItemDialog.xaml.cs](<D:/C#/BIS.ERP/BIS.ERP/Views/CatalogItemDialog.xaml.cs:191>)

```csharp
if (IsMaterialsCatalog())
{
    var fullNameField = _catalog.Fields.FirstOrDefault(field => field.DbColumnName == "full_name");
    if (fullNameField != null && string.IsNullOrWhiteSpace(GetItemText(fullNameField.Name)))
        _itemData[fullNameField.Name] = GetItemText("Наименование материала", "name");
    return;
}
```

В карточке коды классификаторов сгруппированы по физическим именам полей. [CatalogItemDialog.xaml.cs](<D:/C#/BIS.ERP/BIS.ERP/Views/CatalogItemDialog.xaml.cs:1664>)

```csharp
private static bool IsInternationalCodeField(MetadataField field) =>
    field.DbColumnName is "tnved_code" or "economic_activity_service_code" or "product_or_service_classification_code";
```

В документе ТМЗ раньше читались подписи «Ед изм» и «Единица». Теперь чтение поддерживает новую подпись, физическую колонку и обе старые подписи. Списки полей предустановленных отчётов также используют новые подписи.

```csharp
var unit = Read(material, "Единица измерения", "unit", "Ед изм", "Единица");
```

## Проверка

Сборка прошла. Для ручной проверки на существующей базе нужно открыть старую карточку, сверить единицу измерения, номенклатурный номер и счета, затем сохранить материал с полным названием и классификационными кодами. После повторного открытия проверить коды с ведущими нулями и подстановку единицы измерения при выборе материала в документе. Эти действия на рабочей базе не выполнялись.
