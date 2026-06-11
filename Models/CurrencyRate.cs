using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    [Table("catalog_currency_rates")]
    public class CurrencyRate
    {
        [Key]
        public Guid Id { get; set; }

        [Column("rate_date")]
        public DateTime RateDate { get; set; }

        [Column("currency_id")]
        public Guid CurrencyId { get; set; }

        [Column("rate_nb")]
        public decimal RateNb { get; set; }

        [Column("rate_commercial")]
        public decimal RateCommercial { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("description")]
        public string? Description { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; }

        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("CurrencyId")]
        public virtual MetadataObject? Currency { get; set; }
    }
}