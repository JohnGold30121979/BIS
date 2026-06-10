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
        [MaxLength(200)]
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

        // Новые поля для универсальности
        public bool UsePostings { get; set; } = false;      // Использует проводки
        public bool UseBalances { get; set; } = false;      // Использует балансы (для ОС, счетов)
        public bool UseMovements { get; set; } = false;     // Использует движения
        public string? BalanceTable { get; set; }           // Таблица для балансов
        public string? MovementTable { get; set; }          // Таблица для движений

        // Связи с другими объектами
        public string? ReferenceFields { get; set; }        // JSON: {"AssetField":"FixedAssetId", "AccountField":"AccountId"}

        public virtual ICollection<MetadataCalculation> Calculations { get; set; } = new List<MetadataCalculation>();
        public virtual ICollection<MetadataPostingRule> PostingRules { get; set; } = new List<MetadataPostingRule>();
    }

    // Правила расчета (для амортизации, итогов и т.д.)
    public class MetadataCalculation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MetadataObjectId { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string TargetField { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CalculationType { get; set; } = string.Empty; // Depreciation, Sum, Average, Formula

        [MaxLength(500)]
        public string Formula { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? SourceFields { get; set; } // JSON массив полей-источников

        public bool IsAuto { get; set; } = false; // Автоматический расчет
        public int ExecutionOrder { get; set; } = 0;

        [ForeignKey("MetadataObjectId")]
        public virtual MetadataObject? MetadataObject { get; set; }
    }

    // Правила проводок (для документов)
    public class MetadataPostingRule
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MetadataObjectId { get; set; }

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string DebitAccountExpression { get; set; } = string.Empty;

        [MaxLength(200)]
        public string CreditAccountExpression { get; set; } = string.Empty;

        [MaxLength(200)]
        public string AmountExpression { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Condition { get; set; }

        public int Order { get; set; } = 0;

        [ForeignKey("MetadataObjectId")]
        public virtual MetadataObject? MetadataObject { get; set; }
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

        [MaxLength(50)]
        public string? ReferenceCatalog { get; set; } 

        public string? Formula { get; set; }

        public string? DisplayPattern { get; set; }      // "{code} - {name}"
        public string? DisplayFields { get; set; }       // "code,name"
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