using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class MetadataObject
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string TableName { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string ObjectType { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        public string Icon { get; set; } = "📚";

        public int Order { get; set; } = 0;

        public bool IsSystem { get; set; } = false;

        public Guid? ParentId { get; set; }

        // Делаем nullable
        public Guid? MetadataConfigId { get; set; }

        [ForeignKey("MetadataConfigId")]
        public virtual MetadataConfiguration? MetadataConfig { get; set; }

        public virtual ICollection<MetadataField> Fields { get; set; } = new List<MetadataField>();
    }

    public class MetadataField
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string DbColumnName { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string FieldType { get; set; } = string.Empty;

        public int Length { get; set; } = 0;
        public int Precision { get; set; } = 18;
        public int Scale { get; set; } = 2;

        public bool IsRequired { get; set; } = false;
        public bool IsUnique { get; set; } = false;
        public int Order { get; set; } = 0;

        public Guid MetadataObjectId { get; set; }

        [ForeignKey("MetadataObjectId")]
        public virtual MetadataObject MetadataObject { get; set; }
    }

    public class MetadataConfiguration
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid InfoBaseId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0";
        public bool IsInitialized { get; set; } = false;
    }
}