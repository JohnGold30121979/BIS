using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Виды визуального макета.
    ///
    /// Разделение введено потому, что отчёты бывают двух разных природ:
    /// список с плоской шапкой и печатный бланк с многоуровневой шапкой.
    /// Собирать их удобнее разными наборами средств, поэтому вид макета
    /// задаётся явно и определяет, что показывать в конструкторе.
    /// </summary>
    public static class ClosedXmlLayoutKinds
    {
        /// <summary>
        /// Простой макет: таблица с одной строкой шапки.
        /// Для журналов, реестров, ведомостей — обычных списков.
        /// </summary>
        public const string Simple = "Simple";

        /// <summary>
        /// Форматный макет: печатный бланк с многоуровневой шапкой,
        /// объединёнными ячейками и поворотом текста.
        /// Для счет-фактур, накладных и других форм с графами.
        /// </summary>
        public const string Formatted = "Formatted";
    }

    /// <summary>
    /// JSON-разметка визуального Excel-макета (ClosedXML).
    /// Хранится в <see cref="Report.ClosedXmlLayout"/> и интерпретируется
    /// <c>ClosedXmlReportRenderer</c>: строки, шапка, колонки, группы, итоги.
    /// Намеренно отделена от PrintFormTemplate (FRX/native), чтобы не смешивать модели.
    /// </summary>
    public class ClosedXmlReportLayout
    {
        public int Version { get; set; } = 1;

        public List<ClosedXmlSheet> Sheets { get; set; } = new();
    }

    public class ClosedXmlSheet
    {
        public string Name { get; set; } = "Лист1";

        /// <summary>
        /// Вид макета: <see cref="ClosedXmlLayoutKinds.Simple"/> или
        /// <see cref="ClosedXmlLayoutKinds.Formatted"/>.
        /// Простой вид — таблица с одной строкой шапки, форматный — бланк
        /// с многоуровневой шапкой.
        /// </summary>
        public string LayoutKind { get; set; } = ClosedXmlLayoutKinds.Simple;

        public ClosedXmlPageSetup PageSetup { get; set; } = new();

        public List<ClosedXmlBlock> Blocks { get; set; } = new();
    }

    public class ClosedXmlPageSetup
    {
        /// <summary>Portrait | Landscape.</summary>
        public string Orientation { get; set; } = "Landscape";

        public bool ShowGridLines { get; set; }

        public int FreezeRows { get; set; }

        /// <summary>0 — без ограничения по ширине.</summary>
        public int FitToPagesWide { get; set; } = 1;

        public string FontName { get; set; } = "Times New Roman";

        public double FontSize { get; set; } = 10;

        /// <summary>
        /// Верхний колонтитул: повторяется на каждой странице.
        ///
        /// Поддерживаются подстановки <c>{Organization}</c> — первичная организация,
        /// <c>{Page}</c> — номер листа и <c>{Pages}</c> — всего листов.
        /// Пусто — колонтитул не выводится.
        /// </summary>
        public string PageHeaderText { get; set; } = string.Empty;

        /// <summary>Нижний колонтитул. Подстановки те же, что в верхнем.</summary>
        public string PageFooterText { get; set; } = string.Empty;

        /// <summary>
        /// Выводить вверху справа наименование первичной организации и счётчик листов.
        ///
        /// Включено по умолчанию: так оформлен печатный бланк. Если задан
        /// <see cref="PageHeaderText"/>, он имеет приоритет.
        /// </summary>
        public bool ShowPageHeader { get; set; } = true;
    }

    /// <summary>
    /// Блок макета. Один тип на блок: Type = TitleRows | FreeRows | TabularSection | SignaturesRow | PageBreak.
    /// Заполнено только то поле, которое соответствует Type (остальные пустые).
    /// </summary>
    public class ClosedXmlBlock
    {
        public string Type { get; set; } = "TitleRows";

        /// <summary>Подпись блока в списке конструктора (не влияет на вывод).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Для Type = TitleRows | FreeRows.</summary>
        public List<ClosedXmlTextRow> Rows { get; set; } = new();

        /// <summary>Для Type = TabularSection.</summary>
        public ClosedXmlTableSection? Table { get; set; }

        /// <summary>Для Type = SignaturesRow.</summary>
        public List<ClosedXmlSignature> Signatures { get; set; } = new();

        [JsonIgnore]
        public string TypeLabel => Type switch
        {
            "TitleRows" => "Заголовок",
            "FreeRows" => "Свободные строки",
            "TabularSection" => "Таблица",
            "SignaturesRow" => "Подписи",
            "PageBreak" => "Разрыв страницы",
            _ => Type
        };
    }

    public class ClosedXmlTextRow
    {
        /// <summary>1-based номер строки внутри блока; 0 — автоинкремент.</summary>
        public int Row { get; set; }

        public List<ClosedXmlTextCell> Cells { get; set; } = new();
    }

    public class ClosedXmlTextCell
    {
        /// <summary>1-based колонка листа.</summary>
        public int Column { get; set; } = 1;

        /// <summary>Объединение до колонки включительно; 0 — не объединять.</summary>
        public int MergeToColumn { get; set; }

        public string Text { get; set; } = string.Empty;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>0 — размер листа.</summary>
        public double FontSize { get; set; }

        /// <summary>Left | Center | Right.</summary>
        public string Align { get; set; } = "Left";

        /// <summary>Цвет заливки #RRGGBB; пусто — без заливки.</summary>
        public string Fill { get; set; } = string.Empty;

        /// <summary>None | Outside | All.</summary>
        public string Border { get; set; } = "None";

        /// <summary>
        /// Взять значение из строки набора (1-based) вместо текста. 0 — не брать.
        /// Нужно, чтобы заголовок и период акта брались прямо из данных.
        /// </summary>
        public int TakeFromRow { get; set; }

        /// <summary>Имя колонки набора для <see cref="TakeFromRow"/>.</summary>
        public string TakeFromField { get; set; } = string.Empty;

        /// <summary>
        /// Поворот текста в градусах: 0 — обычный, 90 — снизу вверх,
        /// 270 — сверху вниз, 45 — по диагонали.
        ///
        /// Нужен для бланков: в форме СТ-1 заголовки граф стоят вертикально.
        /// </summary>
        public int TextRotation { get; set; }

        /// <summary>Перенос текста по словам. Нужен для длинных заголовков графов.</summary>
        public bool WrapText { get; set; }
    }

    /// <summary>
    /// Уровень шапки таблицы.
    ///
    /// Шапка бланка состоит из нескольких уровней: верхний объединяет графы
    /// («Сумма» над колонками «Дт» и «Кт»), нижний подписывает каждую колонку.
    /// Уровни рисуются сверху вниз в порядке списка.
    /// </summary>
    public class ClosedXmlHeaderLevel
    {
        /// <summary>Подпись уровня в списке конструктора.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Ячейки уровня: объединения и подписи.</summary>
        public List<ClosedXmlHeaderCell> Cells { get; set; } = new();
    }

    /// <summary>Ячейка уровня шапки: объединение по колонкам и подпись.</summary>
    public class ClosedXmlHeaderCell
    {
        /// <summary>1-based первая колонка объединения.</summary>
        public int Column { get; set; } = 1;

        /// <summary>1-based последняя колонка объединения; 0 или меньше — одна колонка.</summary>
        public int MergeToColumn { get; set; }

        /// <summary>
        /// Сколько уровней занимает ячейка по вертикали: 1 — только свой уровень.
        /// Нужно для графов бланка: подпись «Количество» стоит над двумя уровнями
        /// и занимает их целиком.
        /// </summary>
        public int RowSpan { get; set; } = 1;

        /// <summary>
        /// Подпись. Пустая строка — подставить заголовок колонки из этого объединения:
        /// так нижний уровень шапки заполняется автоматически.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        public bool Bold { get; set; } = true;

        /// <summary>Left | Center | Right.</summary>
        public string Align { get; set; } = "Center";

        /// <summary>Поворот текста в градусах: 0, 45, 90, 270.</summary>
        public int TextRotation { get; set; }

        /// <summary>Цвет заливки #RRGGBB; пусто — заливка шапки из настройки таблицы.</summary>
        public string Fill { get; set; } = string.Empty;
    }

    public class ClosedXmlTableSection
    {
        public List<ClosedXmlColumn> Columns { get; set; } = new();

        /// <summary>Цвет заливки шапки таблицы, #RRGGBB; пусто — без заливки.</summary>
        public string HeaderFill { get; set; } = "#D9E6F2";

        /// <summary>
        /// Уровни шапки сверху вниз.
        ///
        /// Пусто — шапка из одной строки: подписи берутся из колонок
        /// (<see cref="ClosedXmlColumn.Header"/>). Заполнено — шапка рисуется
        /// по уровням, а подписи колонок используются только как значения
        /// по умолчанию для пустых ячеек нижнего уровня.
        /// </summary>
        public List<ClosedXmlHeaderLevel> HeaderLevels { get; set; } = new();

        /// <summary>
        /// Высота одной строки шапки в пунктах. 0 — размер по умолчанию.
        /// Для вертикальных подписей нужна высокая строка.
        /// </summary>
        public double HeaderRowHeight { get; set; }

        public bool HeaderBold { get; set; } = true;

        public bool HeaderWrapText { get; set; } = true;

        /// <summary>Рамка строк данных: None | Outside | All.</summary>
        public string RowBorder { get; set; } = "All";

        /// <summary>Чередование строк, #RRGGBB; пусто — без чередования.</summary>
        public string AlternateFill { get; set; } = string.Empty;

        /// <summary>Группировки по полям (в порядке значимости).</summary>
        public List<ClosedXmlGroup> Groups { get; set; } = new();

        /// <summary>
        /// Правила выделения строк по содержимому первой колонки. Нужны для отчётов,
        /// у которых структура уже приходит данными: «АКТ СВЕРКИ», «Пара счетов»,
        /// «САЛЬДО НА …», «ИТОГО ОБОРОТОВ», строка задолженности.
        /// </summary>
        public List<ClosedXmlEmphasisRule> EmphasisRules { get; set; } = new();

        /// <summary>
        /// Строки тела, которые нужно пропустить: сравнение по началу первой непустой ячейки.
        /// Так из тела убираются строки, уже показанные в шапке («АКТ СВЕРКИ», «Период: …»).
        /// </summary>
        public List<string> SkipRowsStartingWith { get; set; } = new();

        /// <summary>Не выводить полностью пустые строки тела (как в программном акте сверки).</summary>
        public bool SkipEmptyRows { get; set; }

        /// <summary>Строка «ИТОГО» по всему разделу; null — не выводить.</summary>
        public ClosedXmlTotal? GrandTotal { get; set; }
    }

    public class ClosedXmlColumn
    {
        /// <summary>Имя колонки DataTable (как в источнике данных).</summary>
        public string Field { get; set; } = string.Empty;

        public string Header { get; set; } = string.Empty;

        public double Width { get; set; } = 14;

        /// <summary>Формат числа/даты Excel, например #,##0.00 или dd.MM.yyyy; пусто — авто.</summary>
        public string NumberFormat { get; set; } = string.Empty;

        /// <summary>Left | Center | Right.</summary>
        public string Align { get; set; } = "Left";

        public bool Bold { get; set; }

        /// <summary>Не выводить нулевые суммы (пустая ячейка вместо 0,00).</summary>
        public bool SuppressZero { get; set; }

        /// <summary>
        /// Поворот подписи колонки в градусах: 0, 45, 90, 270.
        /// Нужен для бланков, где заголовки граф стоят вертикально.
        /// </summary>
        public int TextRotation { get; set; }
    }

    /// <summary>Правило выделения строки по содержимому первой непустой ячейки.</summary>
    public class ClosedXmlEmphasisRule
    {
        /// <summary>StartsWith | Contains | Equals.</summary>
        public string Match { get; set; } = "StartsWith";

        /// <summary>Текст для сравнения.</summary>
        public string Text { get; set; } = string.Empty;

        public bool IgnoreCase { get; set; } = true;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>Center | Left | Right; пусто — не менять выравнивание.</summary>
        public string Align { get; set; } = string.Empty;

        /// <summary>Объединить метку с колонки 1 до этой колонки; 0 — не объединять.</summary>
        public int MergeLabelToColumn { get; set; }

        /// <summary>Заливка #RRGGBB; пусто — не менять.</summary>
        public string Fill { get; set; } = string.Empty;
    }

    public class ClosedXmlGroup
    {
        /// <summary>Поля группировки (колонки DataTable), в порядке значимости.</summary>
        public List<string> ByFields { get; set; } = new();

        public bool ShowHeader { get; set; } = true;

        /// <summary>Текст строки-заголовка группы; {value} подставляется значениями полей.</summary>
        public string HeaderFormat { get; set; } = "{value}";

        public bool HeaderBold { get; set; } = true;

        /// <summary>Подытог группы; null — не выводить.</summary>
        public ClosedXmlTotal? Subtotal { get; set; }

        /// <summary>
        /// Поле, значение которого добавляется к подписи подытога: «ИТОГО по 2».
        ///
        /// Пусто — берётся первое поле группировки.
        /// </summary>
        public string? SubtotalValueField { get; set; }
    }

    public class ClosedXmlTotal
    {
        public string Label { get; set; } = "ИТОГО";

        /// <summary>Поля (колонки DataTable), по которым считается сумма. Пусто — по всем числовым колонкам.</summary>
        public List<string> SumFields { get; set; } = new();

        public bool Bold { get; set; } = true;
    }

    public class ClosedXmlSignature
    {
        public string Text { get; set; } = string.Empty;

        /// <summary>1-based колонка листа.</summary>
        public int Column { get; set; } = 1;

        /// <summary>Объединение до колонки включительно; 0 — не объединять.</summary>
        public int MergeToColumn { get; set; }

        public bool Bold { get; set; }
    }
}
