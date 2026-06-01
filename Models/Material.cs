using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models;

public class Material
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string Unit { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [MaxLength(100)]
    public string? Warehouse { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal? MinStock { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? InfoBaseId { get; set; }

    [ForeignKey("InfoBaseId")]
    public virtual InfoBase? InfoBase { get; set; }
}