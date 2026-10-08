using System;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    /// <summary>
    /// Последнее значение параметра отчёта, введённое конкретным пользователем.
    /// Используется для предзаполнения диалога параметров при следующем запуске.
    /// </summary>
    public class ReportUserSetting
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [MaxLength(120)]
        public string UserLogin { get; set; } = string.Empty;

        /// <summary>Имя параметра (ReportParameter.Name) без фигурных скобок.</summary>
        [MaxLength(120)]
        public string ParameterName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;
    }
}
