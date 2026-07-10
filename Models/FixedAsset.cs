using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models;

// Legacy compatibility entity from the early static schema.
// New fixed-asset business logic should use the metadata-driven catalog "Основные средства".
public class FixedAsset
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string InventoryNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? ResponsiblePerson { get; set; }

    public DateTime AcquisitionDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal InitialCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ResidualValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? DepreciationRate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? InfoBaseId { get; set; }

    [ForeignKey("InfoBaseId")]
    public virtual InfoBase? InfoBase { get; set; }
}
