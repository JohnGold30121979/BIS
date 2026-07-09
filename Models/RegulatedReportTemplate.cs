using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class RegulatedReportTemplate
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(80)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(60)]
        public string Version { get; set; } = string.Empty;

        [MaxLength(260)]
        public string OriginalFileName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FileExtension { get; set; } = ".xlsx";

        [MaxLength(150)]
        public string MimeType { get; set; } = "application/octet-stream";

        public byte[] TemplateData { get; set; } = Array.Empty<byte>();

        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime? EffectiveFrom { get; set; }

        [MaxLength(64)]
        public string Sha256 { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [NotMapped]
        public long TemplateSizeBytes => TemplateData?.LongLength ?? 0;

        [NotMapped]
        public string TemplateSizeDisplay
        {
            get
            {
                var size = TemplateSizeBytes;
                if (size <= 0)
                    return "0 B";
                if (size < 1024)
                    return $"{size} B";
                if (size < 1024 * 1024)
                    return $"{size / 1024d:F1} KB";
                return $"{size / 1024d / 1024d:F2} MB";
            }
        }
    }

    public sealed class RegulatedReportTemplateDraft
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
