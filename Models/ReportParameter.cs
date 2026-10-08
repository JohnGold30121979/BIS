using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Определение параметра отчёта. Значение параметра задаётся пользователем при
    /// формировании отчёта, а в фильтре отчёта на него ссылаются токеном {Имя}.
    /// </summary>
    public class ReportParameter
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual Report Report { get; set; }

        /// <summary>Имя токена без фигурных скобок, например "PeriodStart".</summary>
        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Подпись в диалоге ввода, например "Дата начала периода".</summary>
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        /// <summary>Date | String | Decimal | Int | Bool</summary>
        [MaxLength(40)]
        public string ParameterType { get; set; } = ReportParameterTypes.Date;

        /// <summary>
        /// Значение по умолчанию: литерал или одно из слов "сегодня",
        /// "начала месяца", "конца месяца", "начала года", "конца года", "пусто".
        /// </summary>
        [MaxLength(300)]
        public string DefaultValue { get; set; } = string.Empty;

        public bool IsRequired { get; set; } = true;

        /// <summary>
        /// Имя справочника-источника для типа Reference. Заполняется автоматически
        /// из MetadataField.ReferenceCatalog того поля, на которое ссылается фильтр.
        /// </summary>
        [MaxLength(200)]
        public string ReferenceSource { get; set; } = string.Empty;

        public int Order { get; set; }

        /// <summary>Подпись для UI: заголовок, при отсутствии — имя в скобках.</summary>
        [NotMapped]
        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(Title) ? $"{{{Name}}}" : Title;

        [NotMapped]
        public string TypeDisplay => ReportParameterTypes.GetDisplayName(ParameterType);
    }

    public static class ReportParameterTypes
    {
        public const string Date = "Date";
        public const string String = "String";
        public const string Decimal = "Decimal";
        public const string Int = "Int";
        public const string Bool = "Bool";
        public const string Reference = "Reference";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Date, String, Decimal, Int, Bool, Reference
        };

        public static string GetDisplayName(string? parameterType) => parameterType switch
        {
            Int => "Целое",
            Decimal => "Число",
            Bool => "Флажок",
            String => "Строка",
            Reference => "Ссылка",
            _ => "Дата"
        };
    }
}
