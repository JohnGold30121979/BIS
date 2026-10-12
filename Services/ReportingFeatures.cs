namespace BIS.ERP.Services
{
    /// <summary>
    /// Переключатели механизмов формирования отчётов.
    ///
    /// FRX отключён на время перехода на ClosedXML: два генератора мешали работе,
    /// и в интерфейсе оставались лишние ветки. Код FRX сохранён полностью —
    /// чтобы вернуть его, достаточно установить <see cref="FrxEnabled"/> в true.
    ///
    /// Что отключается при <c>FrxEnabled = false</c>:
    /// <list type="bullet">
    ///   <item>варианты FRX в списках выбора отчётов;</item>
    ///   <item>форматы вывода «FRX FoxPro Excel» и «FRX FoxPro PDF»;</item>
    ///   <item>импорт FRX-макетов и способ макета «FRX» в конструкторе;</item>
    ///   <item>вкладка «FRX поля» в конструкторе.</item>
    /// </list>
    ///
    /// Что продолжает работать: сами отчёты в базе, макеты, парсер и служба печати.
    /// </summary>
    public static class ReportingFeatures
    {
        /// <summary>
        /// Формирование отчётов по макетам FoxPro FRX.
        /// Отключено на время перехода на ClosedXML.
        /// </summary>
        public const bool FrxEnabled = false;

        /// <summary>
        /// Формирование отчётов по визуальному макету ClosedXML.
        /// </summary>
        public const bool ClosedXmlEnabled = true;

        /// <summary>Признак отчёта с макетом FoxPro FRX.</summary>
        public static bool IsFrxReport(Models.Report? report) =>
            report != null &&
            (string.Equals(report.SourceFormat, "FoxProFRX", System.StringComparison.OrdinalIgnoreCase) ||
             string.Equals(report.ReportType, "FoxProLayout", System.StringComparison.OrdinalIgnoreCase));

        /// <summary>Формат вывода относится к FRX.</summary>
        public static bool IsFrxFormat(string? format) =>
            string.Equals(format, "FoxProExcel", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "ExcelFoxPro", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "FoxProPdf", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "PdfFoxPro", System.StringComparison.OrdinalIgnoreCase);
    }
}
