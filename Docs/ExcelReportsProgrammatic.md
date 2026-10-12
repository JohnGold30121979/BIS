# Программное формирование Excel-отчётов в BIS ERP

Документ описывает **по фактическому коду**, как в проекте BIS ERP формируются Excel-файлы (`.xlsx`):
какие движки используются, где лежат точки входа, как устроен каждый путь экспорта и какие есть ограничения.

Связанные документы: [`ReportingArchitecture.md`](ReportingArchitecture.md) (общая архитектура отчётности),
[`FoxProReportRules.md`](FoxProReportRules.md) (перенос FRX-форм),
[«Дорожная карта по системе отчётности.md»](Дорожная%20карта%20по%20системе%20отчётности.md) (планы рефакторинга).

---

## 0. Коротко

> В системе **два технологических способа** получить Excel: библиотека **ClosedXML 0.105.0** (основной, не требует установленного Excel)
> и **COM-мост через `Excel.Application`** — он используется только для заполнения официального налогового шаблона `STI-062_7`
> и требует установленного Microsoft Excel на машине пользователя.

ClosedXML-путь разветвлён на четыре движка вывода:

| Движок | Метод | Что делает |
| --- | --- | --- |
| Программный Excel | [`ReportService.ExportToExcel`](../Services/ReportService.cs) | Заголовок + дата + обычная таблица из `DataTable` |
| Нативный Excel (макет конструктора) | `PrintFormService.ExportReportTemplateExcel` → `BuildTemplateLayoutExcel` | Раскладка по координатам элементов нативного макета |
| **Визуальный ClosedXML-макет** | [`ClosedXmlReportRenderer.RenderToBytes`](../Services/ClosedXmlReportRenderer.cs) | **Интерпретация JSON-разметки (строки, шапка, колонки, группы, итоги) в книгу ClosedXML — см. §13** |
| FRX FoxPro Excel | тот же `ExportReportTemplateExcel` | Раскладка по координатам элементов FRX-макета, линии и рамки переносятся в ячейки |
| Программный акт сверки | `PrintFormService.ExportProgrammaticReconciliationActExcel` | Специализированная вёрстка акта сверки |

Плюс **узкоспециализированные ClosedXML-генераторы** (свой макет под конкретный отчёт, без общего движка):

- `AdvancePaymentsTurnoverReportService` — оборотная ведомость по парам счетов авансов;
- `VatTaxReportExportService.ExportFallbackWorkbook` — внутренний НДС-отчёт, когда официального шаблона в БД нет;
- `PostingsJournalView`, `AccountTurnoverSelectionDialog`, `CatalogDataView` — локальные экспорты прямо во View.

Общий конвейер: **данные собираются в `DataTable` → оформляются через ClosedXML/COM → файл либо возвращается `byte[]`, либо пишется на диск → открывается через `Process.Start`/Shell**.

---

## 1. Технологии и зависимости

| Компонент | Версия / деталь | Где объявлен |
| --- | --- | --- |
| `ClosedXML` | 0.105.0 | [`BIS.ERP.csproj`](../BIS.ERP.csproj), `PackageReference` |
| `Excel.Application` (COM) | Только для шаблона `STI-062_7`; определяется через `Type.GetTypeFromProgID("Excel.Application")` | [`Services/VatTaxReportExportService.cs`](../Services/VatTaxReportExportService.cs) |
| Формат вывода | Всегда `.xlsx` (для налогового шаблона расширение берётся из шаблона, запись идёт `SaveAs` через Excel) | — |

Импорт ClosedXML во всех Excel-сервисах: `using ClosedXML.Excel;`.

Ключевые используемые возможности ClosedXML:

- `new XLWorkbook()` → `workbook.Worksheets.Add(name)` → `workbook.SaveAs(stream|path)`;
- `worksheet.Cell(r, c).Value = ...`, `worksheet.Range(...).Merge()`;
- `InsertTable(DataTable)` — вставка таблицы данных одним вызовом;
- `Style` (шрифт, заливка, границы, выравнивание), `NumberFormat`, `DateFormat`;
- `Columns().AdjustToContents()` — автоширина;
- `PageSetup` (ориентация, поля, `FitToPages`), `SheetView.FreezeRows(n)`;
- `FormulaA1` — запись формул (используется в fallback-книге НДС).

---

## 2. Карта точек входа (UI → сервис)

### 2.1 Главный экран бухгалтерской отчётности

[`Views/AccountingReportsView.xaml.cs`](../Views/AccountingReportsView.xaml.cs), метод `OnExportExcelClick` (~строка 2307):

1. Проверяет, что отчёт сформирован (`_currentData`, `_currentReport`);
2. Читает выбранный формат из `ReportOutputFormatCombo` (`GetSelectedReportOutputFormat()`, по умолчанию `ProgramExcel`);
3. Маршрутизирует по тегу формата (см. §3);
4. Пишет `byte[]` во временный файл `BuildTemporaryExcelPath(...)` → `%TEMP%\BIS.ERP\ReportOutput\<Отчёт>_<SUFFIX>_<дата>.xlsx` и открывает его (`OpenGeneratedFile`).

Важные детали маршрутизации:

- `dataSnapshot = _currentData.Copy()` и `reportSnapshot = CloneReportForBackgroundPreview(_currentReport)` — **снапшоты** для безопасной работы в `Task.Run` (UI-поток не блокируется, а исходные объекты не изменяются фоновым потоком);
- перед FRX/нативным экспортом досеваются и читаются активные правила распознавания полей: `FoxProReportFieldRuleService.SeedDefaultRulesAsync()` → `GetRulesAsync(includeInactive: false)`;
- если для FRX-формата нет FRX-шаблона или для нативного формата нет нативного макета — показывается понятное сообщение, а не падение.

### 2.2 Прочие экраны

| Экспорт | Файл | Путь вывода |
| --- | --- | --- |
| Предпросмотр отчёта | [`Views/ReportPreviewWindow.xaml.cs`](../Views/ReportPreviewWindow.xaml.cs) `OnExportExcelClick` | Есть FRX-шаблон → `PrintFormService.ExportReportTemplateExcel`, иначе → `ReportService.ExportToExcel`; файл в `%TEMP%\BIS.ERP\ExcelPreview\` |
| Кассовые документы (ККМ) | [`Views/CashOrderWorkView.xaml.cs`](../Views/CashOrderWorkView.xaml.cs) (~стр. 2006) | `ExportReportTemplateExcel` при `PrintFormOutputFormat.Excel`, далее `PrintFormOutputFileService.SaveAndOpenAsync` |
| Печатная форма документа/счета-фактуры | `CashOrderWorkView` (~2509), [`Views/PaymentOrderWorkView.xaml.cs`](../Views/PaymentOrderWorkView.xaml.cs) (~707), [`Views/InvoiceWorkView.xaml.cs`](../Views/InvoiceWorkView.xaml.cs) (~375) | `PrintFormService.ExportDocumentExcelAsync` / `ExportInvoiceDocumentExcelAsync` |
| Оборотно-сальдовая ведомость по авансам | [`Views/FinanceDocumentWorkView.xaml.cs`](../Views/FinanceDocumentWorkView.xaml.cs) `OnExportTurnoverExcelClick` | `AdvancePaymentsTurnoverReportService.ExportExcelAsync` → путь к файлу → `Process.Start` |
| Журнал проводок | [`Views/PostingsJournalView.xaml.cs`](../Views/PostingsJournalView.xaml.cs) `ExportJournalReportExcel` | Локальный ClosedXML-генератор, 4 вида отчёта (Полный/Краткий/По статье/По субсчету) |
| Обороты по счету | [`Views/Dialogs/AccountTurnoverSelectionDialog.xaml.cs`](../Views/Dialogs/AccountTurnoverSelectionDialog.xaml.cs) `ExportExcel` | Локальный ClosedXML-генератор |
| Выгрузка справочника | [`Views/CatalogDataView.xaml.cs`](../Views/CatalogDataView.xaml.cs) `ExportToExcel` | `SaveFileDialog` → `InsertTable(_dataTable)` + стилизованная шапка |
| Налоговый отчёт по НДС | `AccountingReportsView.OnExportVatClick` (~2380) | `VatTaxReportExportService.ExportMonthlyVatReportAsync` |

Общий паттерн локальных генераторов во View: `XLWorkbook` → лист → шапка (объединённые ячейки, шрифт 14) → данные → `RangeUsed().Style.Border` → `Columns().AdjustToContents()` → `SaveAs(path)` → `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`. Весь тяжёлый код обёрнут в `await Task.Run(...)`, ошибки пишутся через `SystemLogService.Error`.

---

## 3. Форматы вывода и их маршрутизация

Формат выбирается в `ComboBox` (`ReportOutputFormatCombo`), тег элемента — строка. Распознавание в [`AccountingReportsView.xaml.cs`](../Views/AccountingReportsView.xaml.cs) (~стр. 1569–1594):

| Тег (Tag) | Проверка | Движок |
| --- | --- | --- |
| `ProgramExcel` | `IsProgrammaticExcelFormat` | `ReportService.ExportToExcel` (ClosedXML, таблица) |
| `NativeExcel` | `IsNativeExcelFormat` | `PrintFormService.ExportReportTemplateExcel` по нативному макету (`SourceFormat = Native`) |
| `ClosedXmlExcel` | `IsClosedXmlExcelFormat` | `ClosedXmlReportRenderer.RenderToBytes` по JSON-разметке `Report.ClosedXmlLayout` |
| `FoxProExcel` / `ExcelFoxPro` | `IsFoxProExcelFormat` | `PrintFormService.ExportReportTemplateExcel` по FRX-шаблону (`SourceFormat = FoxProFRX` или `ReportType = FoxProLayout`) |
| `TaxExcel` | `IsTaxExcelFormat` | `VatTaxReportExportService` (COM Excel + шаблон из БД) |
| `ProgramPdf`, `NativePdf`, `FoxProPdf` | соответствующие `Is*PdfFormat` | QuestPDF — вне scope этого документа |

Определение шаблона отчёта:

```csharp
HasFoxProTemplate(report)  // Template заполнен && (SourceFormat == "FoxProFRX" || ReportType == "FoxProLayout")
HasNativeTemplate(report)  // Template заполнен && SourceFormat == "Native"
```

Для акта сверки в программном формате предусмотрен отдельный случай: `IsReconciliationActReport(...)` → `ExportProgrammaticReconciliationActExcel`.


---

## 4. Движок 1. Программный Excel — `ReportService.ExportToExcel`

Файл: [`Services/ReportService.cs`](../Services/ReportService.cs) (~стр. 1039–1078).

```csharp
public byte[] ExportToExcel(DataTable dataTable, Report report)
{
    using var workbook = new XLWorkbook();
    var worksheet = workbook.Worksheets.Add(BuildSafeExcelWorksheetName(report.Name));
    worksheet.ShowGridLines = report.ShowGridLines;

    var titleRow = worksheet.Cell(1, 1);
    titleRow.Value = report.Name;                 // A1: имя отчёта, жирный, 16 pt
    titleRow.Style.Font.Bold = true;
    titleRow.Style.Font.FontSize = 16;

    worksheet.Cell(2, 1).Value = $"Дата формирования: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

    var table = worksheet.Cell(4, 1).InsertTable(dataTable);   // данные с A4
    table.Theme = report.ShowGridLines ? XLTableTheme.TableStyleMedium2 : XLTableTheme.None;

    worksheet.Columns().AdjustToContents();

    using var stream = new MemoryStream();
    workbook.SaveAs(stream);
    return stream.ToArray();
}
```

Поведение:

- **Структура листа**: строка 1 — заголовок отчёта; строка 2 — дата формирования; строка 4 — начало таблицы (шапка таблицы + данные автоматом ClosedXML);
- `DataTable` поступает уже «подрезанным»: в `ReportService` перед экспортом выполняются `SelectFields` (видимые колонки переименовываются в `DisplayName`) и прочие шаги конвейера `GetReportDataAsync`;
- Результат — `byte[]` в памяти; файл пишет вызывающий код;
- Имя листа санитизируется `BuildSafeExcelWorksheetName`: запрещённые символы `[ ] : * ? / \` и управляющие символы заменяются на пробел, обрезка до 31 символа, пустое имя → `Report`. Эта же функция продублирована в `PrintFormService` (дубликат кода, не общий хелпер).


---

## 5. Движок 2. Нативный / FRX Excel — `PrintFormService`

Файл: [`Services/PrintFormService.cs`](../Services/PrintFormService.cs) (~3600 строк; Excel-часть: строки ~1029–1055, ~2629–2731, ~3468–3596).

### 5.1 Публичный вход

```csharp
public byte[] ExportReportTemplateExcel(DataTable dataTable, Report report)
    // → подтягивает активные FoxPro-правила из БД и вызывает перегрузку ниже

public byte[] ExportReportTemplateExcel(DataTable, Report,
    IReadOnlyCollection<FoxProReportFieldRule>? rules)

public byte[] ExportReportTemplateExcel(DataTable, Report,
    IReadOnlyCollection<FoxProReportFieldRule>? rules,
    IReadOnlyDictionary<string, object>? reportParameters)
```

Если у отчёта пустой `report.Template`, создаётся пустой нативный макет (`CreateBlankNativeTemplate()` сериализуется в JSON). Далее:

1. `BuildReportPreviewData(...)` — готовит данные предпросмотра с применением FoxPro-правил полей и параметров отчёта (`CashOrderPrintData`);
2. `PrepareTemplateForReportRendering(...)` — приводит макет к виду для рендеринга (`FrxRecognitionProfileService.PrepareForRendering`), а для FoxPro-шаблонов «упаковывает» его: `BuildPackedReconciliationFrxTemplate` / `BuildPackedTabularFrxTemplate` / `BuildPackedStaticFrxTemplate`;
3. `BuildTemplateLayoutExcel(...)` — непосредственно сборка книги ClosedXML.

### 5.2 Постраничный вывод (`BuildTemplateLayoutExcel`)

Ключевое решение (закреплено комментарием в коде): **разбивка на страницы выполняется той же логикой, что и для PDF** (`SplitTemplateIntoPages`). Раньше Excel умещал отчёт на один лист с фиксированным числом строк, из-за чего длинные отчёты обрезались. Теперь **каждый лист Excel = одна страница отчёта**, имя листа — `Отчёт` или `Отчёт (N)`.

Алгоритм на страницу:

```csharp
var pages = SplitTemplateIntoPages(layoutTemplate);          // общая с PDF пагинация
var paperSize = GetExcelPaperSize(report, layoutTemplate);   // пропорции A4 (портрет/альбом)

foreach (var page in pages)                                  // лист на каждую страницу
{
    var worksheet = workbook.Worksheets.Add(...);
    ConfigureFrxExcelWorksheet(worksheet, report, landscape);

    var maxColumns = landscape ? 88 : 66;                    // «плотность» сетки ячеек
    var maxRows    = Math.Max(8, (int)Math.Round(paperSize.Height / (paperSize.Width / maxColumns)));
    var columnScale = (maxColumns - 1d) / paperSize.Width;   // координаты макета → ячейки
    var rowScale    = (maxRows - 1d) / paperSize.Height;

    var pageTop = page.Elements.Min(item => item.Top) - ...; // смещение страницы: элементы идут
                                                             // от начала документа, а лист начинается сверху
    worksheet.Columns(1, maxColumns).Width = landscape ? 1.45 : 1.65;
    worksheet.Rows(1, maxRows).Height = 9;

    foreach (var element in page.Elements.OrderBy(item => item.Order))
    {
        var startRow    = ToExcelIndex(element.Top - pageTop, rowScale, maxRows);
        var startColumn = ToExcelIndex(element.Left, columnScale, maxColumns);
        // Box → ApplyFrxExcelBox, Line → ApplyFrxExcelLine, Text → WriteFrxExcelText
    }
}
```


Служебные методы переноса элементов FRX/native в ячейки:

| Метод | Назначение |
| --- | --- |
| `ToExcelIndex(coord, scale, max, min)` | Координата макета → индекс строки/колонки, с клампом в границы |
| `ApplyFrxExcelBox` | Элемент `Box` → внешняя тонкая рамка `Range` |
| `ApplyFrxExcelLine` | Элемент `Line` → верхняя (горизонтальная линия) или левая (вертикальная) граница |
| `ApplyFrxExcelGridSegment` | Линии/рамки, свёрстанные в `FrxGridSegments` (`BuildFrxGridSegments` с прилипанием к осям по допуску), рисуются границами по сегментам |
| `WriteFrxExcelText` | Текст элемента: шрифт (5–11 pt, `ShrinkToFit = true`, `WrapText = false`), жирный/курсив, выравнивание `Left/Center/Right` |
| `ConfigureFrxExcelWorksheet` | Лист: без линий сетки, шрифт отчёта (`report.FontName`, по умолчанию Arial), `PageSetup`: ориентация, поля 0.25, `FitToPages(1, 0)`, центрирование по горизонтали |
| `GetExcelPaperSize` | Определяет ширину/высоту из пропорций макета: ширина > высоты → альбомная ориентация; ориентир — соотношение сторон A4 (297/210) |

### 5.3 Программный акт сверки

`ExportProgrammaticReconciliationActExcel` → `BuildReconciliationActExcel` (~стр. 1140):

- Лист: Times New Roman 9 pt, без линий сетки, колонка 1 шириной 44, остальные 12;
- Заголовок «АКТ СВЕРКИ» (merge A:H, по центру), затем заголовок акта и период — изыскиваются по строкам `DataTable` через `ReadReconciliationOperation` (первые строки, начинающиеся с «АКТ» / «Период»);
- Шапка таблицы: 8 фиксированных колонок, заливка `#D9E6F2`, тонкие рамки;
- Строки через `WriteReconciliationExcelRow`: важные строки («САЛЬДО», «ИТОГО», «Пара счетов») — жирные, суммы форматируются `FormatReconciliationAmount`, остальные колонки по центру;
- В конце — строка итоговой задолженности (merge), `FreezeRows(4)`, альбомная ориентация, `FitToPages(1, 0)`.


---

## 6. Движок 3. Налоговый Excel по НДС — `VatTaxReportExportService`

Файл: [`Services/VatTaxReportExportService.cs`](../Services/VatTaxReportExportService.cs) (~830 строк).
Код шаблона: `OfficialTemplateCode = "STI-062_7"`.

### 6.1 Конвейер `ExportMonthlyVatReportAsync`

```
BalanceService.GetPurchaseSalesJournalAsync(start, end)   ← журнал покупок/продаж
        ↓
BuildWorkbookData(...)                                    ← VatWorkbookData: организация, продажи, закупки
        ↓
RegulatedReportTemplateService.GetActiveTemplateAsync("STI-062_7")
        ├── шаблон найден и TemplateData не пуст  →  ExportUsingExcelTemplate  (COM Excel)
        └── шаблона нет                          →  ExportFallbackWorkbook    (ClosedXML)
```

### 6.2 Режим официального шаблона (COM Excel)

`ExportUsingExcelTemplate`:

1. `Type.GetTypeFromProgID("Excel.Application")` — если Excel не установлен, выбрасывается `InvalidOperationException` с понятным текстом;
2. `Activator.CreateInstance(excelType)` → `excelApp.Visible = false`, `DisplayAlerts = false`;
3. `Workbooks.Open(tempTemplatePath)` — шаблон сначала пишется во временный каталог (см. §6.4);
4. Заполнение:
   - `FillSummarySheet` — ищет лист по ключевым словам (`sti-062`, `062`, `титул`, `общ`, `свод`, `main`, `лист1`), значения пишет **вблизи подписей** через `WriteValueNearLabel` (наименование организации, ИНН, ОКПО, адрес, руководитель, главный бухгалтер, период, месяц, год);
   - `FillRegisterSheet` для продаж и закупок — см. §6.3;
5. `excelApp.CalculateFull()` — пересчёт формул шаблона;
6. `workbook.SaveAs(outputPath)`, `Close(false)`, `Quit()`;
7. `finally`: `ReleaseComObject` для workbook/workbooks/app + тройной `GC.Collect/WaitForPendingFinalizers` — защита от «зомби»-процессов Excel.

Результат — `VatReportExportResult(UsedOfficialTemplate, TemplateCode, TemplateName, OriginalFileName)`, по нему UI показывает, каким шаблоном сформирован отчёт.

### 6.3 Умное заполнение листов реестра

`FillRegisterSheet(workbook, rows, SheetKind.Sales|Purchases)` — код, устойчивый к разным версиям официального шаблона:

1. Поиск листа по ключевым словам (продажи: `продаж`, `sales`, `реестрпродаж`, `приложение7`, `лист2`; закупки: `закуп`, `purchase`, ...). Если листа нет — создаётся и получает fallback-развёртку (`CreateFallbackRegisterLayout`: заголовок + 10 стандартных колонок);
2. `DetectRegisterHeader(sheet)` — сканирует первые строки UsedRange, для каждой колонки пытается распознать **роль** по заголовку (`TryDetectColumnRole`): `Date`, `Organization`, `Description`, `DocumentNumber`, `AmountWithoutTax`, `ExemptAmount`, `FixedAmount`, `SalesTaxAmount`, `VatAmount`, `TotalAmount`, `TaxType`, `Note`. Заголовок может быть двухстрочным (сканируется пара строк). Шапка принимается, если «оценка» ≥ 3 (есть дата + контрагент/описание + налог + сумма);
3. Определяется строка данных (`header.HeaderRow + HeaderHeight`), строка подвала (`FindFooterRow`), диапазон очистки — старые данные **очищаются** (`ClearContents`), но подвал сохраняется;
4. Данные пишутся по ролям (`WriteRowValue`), поэтому колонка НДС найдётся и как «НДС», и как «VAT»;
5. Если футер найден — в него записываются `=SUM(колонкаStart:колонкаEnd)` для всех денежных ролей (`WriteFooterFormulas`, имя колонки строится `ToExcelColumnName`).

### 6.4 Работа со шаблоном как с временными файлами

- Шаблон хранится в БД (`RegulatedReportTemplates.TemplateData bytea`), на время экспорта пишется в `%TEMP%\BIS.ERP\RegulatedReportTemplates\<guid>\`;
- `CleanupOldTemplateTempDirectories` удаляет каталоги старше 2 суток; после экспорта каталог удаляется `DeleteDirectorySafe` (3 попытки с паузой 150 мс) — `finally` гарантирует очистку даже при ошибке;
- расширение файла берётся из `template.FileExtension`, иначе из `OriginalFileName`, по умолчанию `.xls`.

### 6.5 Fallback-книга на ClosedXML

`ExportFallbackWorkbook(data, outputPath)` — когда официального шаблона в БД нет:

- Три листа: `Свод НДС`, `Продажи`, `Закупки`;
- Реестры: заголовок в A1, шапка в строке 3 (10 колонок: Дата, Организация, Документ, Номер, Сумма без налога, Налог с продаж, НДС, Всего, Ставка налога, Примечание), данные со строки 4, автоширина;
- Сводка — с **формулами** `FormulaA1`: `COUNTA(Продажи!A:A)-1`, `SUM(Продажи!E:H)`, аналогично по закупкам, и итог «НДС к уплате» `=B10-B16`.


---

## 7. Специализированный ClosedXML-генератор: оборотная ведомость по авансам

Файл: [`Services/AdvancePaymentsTurnoverReportService.cs`](../Services/AdvancePaymentsTurnoverReportService.cs).

`ExportExcelAsync(startDate, endDate)`:

1. Валидирует период; загружает справочник «План счетов» (`AccountLookup`: код → наименование, нормализация значений «код», «код - наименование», `Id`), пары счетов (`GetAdvancePaymentPairsAsync`) и все проводки до `endDate`;
2. `BuildReportLines` — строит строки ведомости (дебет/кредит по датам, сотрудник из документа или fallback, аналитика);
3. `BuildWorkbook(filePath, ...)`:
   - один лист «Оборотная ведомость», Times New Roman 10 pt, фиксированные ширины 8 колонок (`ConfigureSheet`);
   - для каждой пары счетов — блок: «ОсОО "Comtec-Soft"» (правый край), объединённый заголовок `MergeTitle` («Оборотная ведомость синтетического и аналитического учета» / «по паре счетов X - Y ... с dd по dd»), дата и «Лист 1», шапка, далее по сотрудникам (`GroupBy(AnalyticDisplay)`): `САЛЬДО НА <дату>`, строки детализации, `ИТОГО ПО ...`, `САЛЬДО НА НАЧАЛО` / `ОБОРОТЫ` / `САЛЬДО НА КОНЕЦ`;
   - в конце — общий блок «ИТОГ» и сводка по счетам (`WriteAccountSummary`);
   - пустые группы (все нули) пропускаются;
   - `AdjustToContents(7, 45)` + принудительная ширина колонки 7 ≥ 34; альбомная ориентация, `FitToPages(1, 0)`;
   - `workbook.SaveAs(filePath)` — метод возвращает **путь** к файлу (не `byte[]`), файл открывает вызывающий код.

---

## 8. Локальные генераторы во View (ClosedXML)

| Где | Метод | Особенности |
| --- | --- | --- |
| [`PostingsJournalView.xaml.cs`](../Views/PostingsJournalView.xaml.cs) `ExportJournalReportExcel` | 4 вида отчёта (`JournalReportKind`: Full/Brief/ByArticle/BySubaccount) | Шапка 3 строки (merge A:H), данные, `WriteJournalTotals`, рамки по `RangeUsed`, `FreezeRows(5)`, автоширина |
| [`AccountTurnoverSelectionDialog.xaml.cs`](../Views/Dialogs/AccountTurnoverSelectionDialog.xaml.cs) `ExportExcel` | 4 вида (`AccountTurnoverReportKind`), выбор стороны Дт/Кт | Итоги `rows.Sum(...)` с форматом `#,##0.00`, рамки по `RangeUsed` |
| [`CatalogDataView.xaml.cs`](../Views/CatalogDataView.xaml.cs) `ExportToExcel` | Выгрузка справочника | `InsertTable(_dataTable)` в A1, шапка: белый жирный текст на `#2C3E50`, автоширина; путь выбирается через `SaveFileDialog` |

Шаблон имён/путей общих хелперов во View:

- `BuildTemporaryExcelPath(baseName, ext)` → `%TEMP%\BIS.ERP\ReportOutput\` (или `ExcelPreview\`), имя санитизируется `SanitizeFileName` (недопустимые символы пути → `_`), суффикс `yyyyMMdd_HHmmss`;
- [`Services/PrintFormOutputFileService.cs`](../Services/PrintFormOutputFileService.cs) — единый сервис сохранения и открытия (`SaveAndOpenAsync`, `GetDisplayName` для Excel/PDF).

---

## 9. Регламентированные шаблоны в БД

Файл: [`Services/RegulatedReportTemplateService.cs`](../Services/RegulatedReportTemplateService.cs); модель — [`Models/RegulatedReportTemplate.cs`](../Models/RegulatedReportTemplate.cs).

Таблица `RegulatedReportTemplates` создаётся/мигрируется сервисом самостоятельно (`EnsureSchemaAsync`, raw SQL `CREATE TABLE IF NOT EXISTS` + `ADD COLUMN IF NOT EXISTS`, дедупликация по ключу connection string через статический `HashSet`):

| Колонка | Назначение |
| --- | --- |
| `Code`, `Version` | Уникальная пара (индекс `IX_RegulatedReportTemplates_Code_Version`) |
| `TemplateData` (`bytea`) | Сам xlsx/xls-файл |
| `FileExtension`, `MimeType`, `OriginalFileName` | Метаданные файла |
| `IsActive`, `UpdatedAt` | Активный шаблон = максимальный `UpdatedAt` среди активных с данным `Code` |
| `Sha256`, `Description`, `EffectiveFrom` | Контрольная сумма, описание, дата ввода |

API: `GetTemplatesAsync`, `GetTemplateAsync(id)`, `GetActiveTemplateAsync(code)`, `SaveTemplateAsync(filePath, draft)` (чтение файла, вычисление SHA-256, upsert по Code+Version), `ExportTemplateCopyAsync(id, outputPath)`.

Единственный потребитель в рантайме — `VatTaxReportExportService` с кодом `STI-062_7` (посев описания шаблона — в `RegulatedReportTemplateService`, ~стр. 243).


---

## 10. Потоки, фоновое выполнение и надёжность

- **UI не блокируется**: весь ClosedXML-рендер вызывается через `await Task.Run(() => ...)`; курсор/статус переключаются (`Mouse.OverrideCursor`, `StatusText`);
- **Снапшоты данных**: `DataTable.Copy()` + клонирование `Report` (`CloneReportForBackgroundPreview`) — фоновый поток не трогает объекты, привязанные к UI;
- **Открытие файла** — только после полной записи: `File.WriteAllBytes` / `workbook.SaveAs` → `Process.Start(UseShellExecute = true)`;
- **Ошибки** логируются `SystemLogService.Error(...)` и показываются в `MessageBox`; для налогового экспорта ошибка Excel/COM гасится в `try/finally` освобождением COM-объектов;
- **Очистка temp**: каталоги шаблонов удаляются в `finally`, старые — при следующем экспорте.

---

## 11. Известные ограничения (по коду и дорожной карте)

1. **COM-зависимость налогового шаблона.** `ExportUsingExcelTemplate` требует установленного Microsoft Excel; риск зависших COM-объектов/процесса Excel. По дорожной карте (РП-2.4) запланирован перенос на Open XML SDK / ClosedXML — тогда останется логика распознавания шапки, но исчезнет требование Excel. Файл: `Services/VatTaxReportExportService.cs`;
2. **Размазанность выбора формата.** Логика выбора движка дублируется в `AccountingReportsView`, `ReportPreviewWindow` и конструкторе (РП-3.2 — единое перечисление форматов);
3. **Монолит `PrintFormService`** (~3600 строк): посадка шаблонов, упаковка, PDF и Excel рендеры в одном классе (план — разделение на `PrintFormTemplateFactory/Packer/PdfRenderer/ExcelRenderer`);
4. **Дублирование `BuildSafeExcelWorksheetName`** в `ReportService` и `PrintFormService` (и схожие `SanitizeFileName` во View);
5. **Синхронные методы** ClosedXML-рендера вызываются из UI-кода только через `Task.Run` — самого `async` в рендерах нет;
6. Локальные генераторы во View (`PostingsJournalView`, `AccountTurnoverSelectionDialog`, `CatalogDataView`) не переиспользуют общий сервис — при изменении стиля Excel-шаблонов править придётся в нескольких местах.

---

## 12. Как добавить новый Excel-экспорт

1. **Если данные уже есть в `DataTable` и достаточно таблицы** — используйте `ReportService.ExportToExcel` (заголовок/дата/таблица/автоширина уже сделаны);
2. **Если нужен точный макет** — создайте отчёт с FRX- или нативным макетом и выводите через `PrintFormService.ExportReportTemplateExcel` (постранично, синхронно с PDF);
3. **Если нужен отдельный регламентированный шаблон** — положите xlsx в `RegulatedReportTemplates` (`SaveTemplateAsync` с кодом), а заполнение реализуйте по образцу `VatTaxReportExportService` (предпочтительно на ClosedXML, чтобы убрать COM);
4. **Если нужен самодостаточный отчёт** — новый `XxxReportService` по образцу `AdvancePaymentsTurnoverReportService`: сбор данных из EF → `BuildWorkbook` на ClosedXML → `SaveAs(path)` → открытие во View через `Task.Run`;
5. Не забудьте: безопасное имя листа (≤31 символ, без `[]:*?/\`), автоширину/`PageSetup` (`FitToPages(1,0)` — печать в ширину листа), формат сумм `#,##0.00` и дат, и `try/catch` с `SystemLogService`.

---

## 13. Визуальный ClosedXML-макет (отдельная ветка от FRX/native)

> **Пошаговая инструкция для пользователя:** [`ClosedXml_инструкция_по_конструктору.md`](ClosedXml_инструкция_по_конструктору.md) —
> откуда берётся набор данных, как заполнять блоки, разбор примера «акт сверки».

Это **второй, независимый способ** визуально собрать Excel-отчёт. Он не использует
`PrintFormTemplate` и координатную «печатную» модель: разметка описывает **структуру
листа** (заголовки, таблица с колонками, группы, итоги, подписи), а ClosedXML сам
раскладывает ячейки. Смысл разделения — не смешивать FRX/PDF-парадигму с Excel-свойствами.

| Слой | Файл | Роль |
| --- | --- | --- |
| Модель разметки | [`Models/ClosedXmlReportLayout.cs`](../Models/ClosedXmlReportLayout.cs) | `ClosedXmlReportLayout` → `Sheets` → `Blocks` |
| Хранилище | [`Models/Report.cs`](../Models/Report.cs) | `Report.ClosedXmlLayout` (JSON-строка) |
| Схема БД | [`Services/RuntimeSchemaFixService.cs`](../Services/RuntimeSchemaFixService.cs) | `ALTER TABLE "Reports" ADD COLUMN IF NOT EXISTS "ClosedXmlLayout" text` |
| Интерпретатор | [`Services/ClosedXmlReportRenderer.cs`](../Services/ClosedXmlReportRenderer.cs) | JSON → `XLWorkbook` |
| Шаблоны макетов | [`Services/ClosedXmlLayoutTemplates.cs`](../Services/ClosedXmlLayoutTemplates.cs) | Встроенный макет акта сверки и колонки оборотной ведомости |
| Конструктор | [`Views/ReportDesignerWindow.xaml`](../Views/ReportDesignerWindow.xaml) | вкладка «📊 ClosedXML макет» |
| Вывод | [`Views/AccountingReportsView.xaml.cs`](../Views/AccountingReportsView.xaml.cs) | формат `ClosedXmlExcel` |

### 13.1 Типы блоков

`ClosedXmlBlock.Type` определяет, какое поле блока заполнено:

| Type | Поле | Что выводит |
| --- | --- | --- |
| `TitleRows` | `Rows` | Свободные строки текста (заголовок, период, реквизиты) |
| `FreeRows` | `Rows` | То же, но семантически «произвольные строки» |
| `TabularSection` | `Table` | Шапка + строки данных, группы, подытоги, общий итог |
| `SignaturesRow` | `Signatures` | Подписи в заданных колонках |
| `PageBreak` | — | Горизонтальный разрыв страницы |

Ячейка текста (`ClosedXmlTextCell`) несёт `Column`, `MergeToColumn`, `Text`, `Bold`,
`Italic`, `FontSize`, `Align`, `Fill` (`#RRGGBB`), `Border` (`None|Outside|All`).
В тексте поддерживаются параметры отчёта: `{param.DateFrom}`.

### 13.2 Таблица, группы и итоги

`ClosedXmlTableSection` содержит `Columns` (`Field`, `Header`, `Width`, `NumberFormat`,
`Align`, `Bold`), оформление (`HeaderFill`, `HeaderBold`, `HeaderWrapText`, `RowBorder`,
`AlternateFill`), `Groups` и `GrandTotal`.

- `Field` сопоставляется с колонкой `DataTable`; помощник `PrepareData` добавляет
  колонки-двойники, поэтому поле находится и по имени источника, и по `DisplayName`
  (после `SelectFields` колонки переименованы в подпись);
- `Groups[].ByFields` — порядок значимости; `HeaderFormat` (`{value}`) задаёт текст
  строки-заголовка группы; `Subtotal` даёт строку подытога;
- итоги пишутся **формулами** `SUM(...)` только по строкам данных: заголовки групп и
  строки подытогов в сумму не попадают;
- соседние строки сжимаются в непрерывные диапазоны (`SUM(C5:C75,C78:C149)`), иначе
  на длинном отчёте формула превысила бы предельные 8192 символа Excel.

### 13.3 Предпросмотр и вывод

- В конструкторе: кнопка «📊 Предпросмотр ClosedXML» — берёт данные через
  `GetReportDataAsync`, рендерит и открывает xlsx;
- В списке отчётов: формат вывода **«ClosedXML Excel (визуальный макет)»** — если у
  отчёта не сохранён макет, выводится понятное сообщение, а не падение;
- JSON разметки хранится в `Report.ClosedXmlLayout` и показывается в конструкторе
  в режиме чтения.

### 13.4 Известные ограничения

1. Конструктор редактирует **один лист** (модель поддерживает несколько, UI — нет);
2. `PrintFormService` и его PDF-ветка не затрагиваются: новые поля только дополняют
   модель, поэтому PDF-рендер работает как раньше;
3. `AdvancePaymentsTurnoverReportService` (жёстко зашитая оборотная ведомость) пока
   остаётся отдельным генератором и **не переведён** на визуальный макет: нужны
   повторяемые многорядные шапки блоков, многострочные «хвосты» групп
   (`ИТОГО ПО …`, `САЛЬДО НА НАЧАЛО`, `ОБОРОТЫ`, `САЛЬДО НА КОНЕЦ`) и сальдо
   с арифметикой вне `SUM` (`начало + Д − К`);
4. **Многорядная шапка таблицы** не выражается: шапка — одна строка заголовков;
5. Отрицательные суммы не переносятся в другую колонку (в ведомости по авансам
   отрицательное сальдо уходит в «Кредит» как `Math.Abs`);
6. Таблица всегда пишется с колонки 1.

### 13.5 Акт сверки: программный отчёт доступен и визуально

Акт сверки (`ReportType = ReconciliationAct`) даёт **плоский** DataTable, в котором
структура уже содержится строками: `ReconciliationActBuilder` пишет
`АКТ СВЕРКИ …`, `Период: …`, `Пара счетов : X - Y (…)`, `САЛЬДО НА <дата>`,
строки операций, `ИТОГО ОБОРОТОВ` и итоговую строку задолженности.

Поэтому визуальному макету не нужны группировки — достаточно правил выделения:

| Механизм модели | Зачем |
| --- | --- |
| `ClosedXmlEmphasisRule` (`EmphasisRules`) | Жирный/курсив/выравнивание/заливка строки по содержимому первой ячейки; `MergeLabelToColumn` объединяет метку влево, если справа пусто |
| `ClosedXmlTableSection.SkipRowsStartingWith` | Убирает из тела строки, уже показанные в шапке (`АКТ`, `Период`) |
| `ClosedXmlColumn.SuppressZero` | «0 не печатать» — вместо `0,00` пустая ячейка, как в программном акте |
| `ClosedXmlTextCell.TakeFromRow` / `TakeFromField` | Текстовый блок берёт значение прямо из строки набора: так заголовок и период акта попадают в шапку без параметров |

Встроенный макет — `ClosedXmlLayoutTemplates.CreateReconciliationAct`; он подставляется
автоматически, если у отчёта нет сохранённого `ClosedXmlLayout`:

- конструктор: `ReportDesignerWindow.LoadClosedXmlLayoutFromReport`;
- вывод: `AccountingReportsView.OnExportExcelClick` (формат `ClosedXmlExcel`).

Инструкция для пользователя — [`ClosedXml_инструкция_по_конструктору.md`](ClosedXml_инструкция_по_конструктору.md).

---

*Документ составлен по состоянию кода: `Services/ReportService.cs`, `Services/PrintFormService.cs`, `Services/VatTaxReportExportService.cs`, `Services/AdvancePaymentsTurnoverReportService.cs`, `Services/RegulatedReportTemplateService.cs`, `Services/ClosedXmlReportRenderer.cs`, `Services/ClosedXmlLayoutTemplates.cs`, `Views/AccountingReportsView.xaml.cs`, `Views/ReportPreviewWindow.xaml.cs`, `Views/ReportDesignerWindow.xaml.cs`, `Views/PostingsJournalView.xaml.cs`, `Views/Dialogs/AccountTurnoverSelectionDialog.xaml.cs`, `Views/CatalogDataView.xaml.cs`.*

