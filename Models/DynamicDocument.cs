using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    // Шапка документа
    public class DynamicDocument
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(50)]
        public string Number { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [MaxLength(200)]
        public string SourceFile { get; set; } = string.Empty;

        public int TotalRows { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Строки документа
        public virtual ICollection<DynamicDocumentRow> Rows { get; set; } = new List<DynamicDocumentRow>();

        // Вычисляемые свойства для отображения в UI (не сохраняются в БД)
        [NotMapped]
        public string DisplayName => $"{Number} от {Date:dd.MM.yyyy HH:mm:ss}";

        [NotMapped]
        public string ShortInfo => $"{DocumentType} | {TotalRows} строк | {SourceFile}";

        [NotMapped]
        public string DisplayDate => Date.ToString("dd.MM.yyyy");
    }

    // Строка документа с динамическими полями
    public class DynamicDocumentRow
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid DocumentId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual DynamicDocument Document { get; set; }

        public int RowNumber { get; set; }

        // Все поля хранятся в JSONB (PostgreSQL)
        [Column(TypeName = "jsonb")]
        public string Data { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // Метаданные о структуре файла
    public class DbfMetadata
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(100)]
        public string FileName { get; set; } = string.Empty;

        [Column(TypeName = "jsonb")]
        public string Fields { get; set; } = "[]"; // JSON массив полей

        public int TotalRecords { get; set; }
        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    }
}