using System.Reflection;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Единый источник версии продукта во время работы программы.
    ///
    /// Значения читаются из метаданных сборки, которые проставляет MSBuild
    /// при каждой сборке (см. Directory.Build.props). Источник версии —
    /// файл version.json в корне проекта.
    ///
    /// Формат версии — Semantic Versioning: Major.Minor.Patch
    /// <list type="bullet">
    ///   <item><c>Major</c> — несовместимые изменения, миграция данных.</item>
    ///   <item><c>Minor</c> — новая функциональность без ломки совместимости.</item>
    ///   <item><c>Patch</c> — исправления; увеличивается автоматически при каждой сборке.</item>
    /// </list>
    /// </summary>
    public static class AppVersion
    {
        private const string VersionKey = "ProductVersion";
        private const string BuildKey = "ProductBuild";
        private const string InformationalKey = "ProductInformational";

        /// <summary>Версия в виде «1.1.7». Основное значение для показа и сравнения.</summary>
        public static string Semantic { get; } = ReadMetadata(VersionKey, "0.0.0");

        /// <summary>Номер сборки, увеличивается при каждой компиляции.</summary>
        public static int Build { get; } = ReadBuildNumber();

        /// <summary>Полная версия в виде «1.1.7.12».</summary>
        public static string Full { get; } = $"{Semantic}.{Build}";

        /// <summary>Время сборки в UTC. Пусто, если значение не проставлено.</summary>
        public static string BuildUtc { get; } = ReadBuildUtc();

        /// <summary>Время сборки в местном времени, вид «09.10.2026 14:02». Для показа.</summary>
        public static string BuildLocalText { get; } = FormatBuildLocal();

        /// <summary>Подпись для интерфейса: «1.1.7 (сборка 12 от 09.10.2026 14:02)».</summary>
        public static string Display =>
            string.IsNullOrWhiteSpace(BuildLocalText)
                ? Semantic
                : $"{Semantic} (сборка {Build} от {BuildLocalText})";

        /// <summary>Короткая подпись для шапки окна: «1.1.7 (сборка 12)».</summary>
        public static string DisplayShort => $"{Semantic} (сборка {Build})";

        /// <summary>Строка для протокола и выгрузки: «BIS ERP 1.1.7.12».</summary>
        public static string ProductText => $"BIS ERP {Full}";

        private static string ReadMetadata(string key, string fallback)
        {
            var value = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
                ?.Value;

            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int ReadBuildNumber()
        {
            var value = ReadMetadata(BuildKey, string.Empty);
            return int.TryParse(value, out var build) ? build : 0;
        }

        private static string ReadBuildUtc()
        {
            // Строка версии имеет вид «1.1.7+2026-10-09T08:02:11Z#12».
            var informational = ReadMetadata(InformationalKey, string.Empty);
            var plusIndex = informational.IndexOf('+');
            if (plusIndex < 0)
                return string.Empty;

            var tail = informational[(plusIndex + 1)..];
            var hashIndex = tail.IndexOf('#');
            return (hashIndex >= 0 ? tail[..hashIndex] : tail).Trim();
        }

        private static string FormatBuildLocal()
        {
            if (string.IsNullOrWhiteSpace(BuildUtc))
                return string.Empty;

            return DateTime.TryParse(
                BuildUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var utc)
                ? utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : string.Empty;
        }
    }
}
