using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BIS.ERP.Models
{
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
    }

    public class ClosedXmlTableSection
    {
        public List<ClosedXmlColumn> Columns { get; set; } = new();

        /// <summary>Цвет заливки шапки таблицы, #RRGGBB; пусто — без заливки.</summary>
        public string HeaderFill { get; set; } = "#D9E6F2";

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
