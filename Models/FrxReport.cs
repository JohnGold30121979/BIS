using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class FrxReport
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;
        public byte[] FrxData { get; set; } // Бинарные данные FRX файла
        public string FrxXml { get; set; } // XML представление FRX

        public string ReportType { get; set; } = "Standard";
        public string Icon { get; set; } = "📄";
        public int Order { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<FrxBand> Bands { get; set; } = new List<FrxBand>();
        public virtual ICollection<FrxField> Fields { get; set; } = new List<FrxField>();
        public virtual ICollection<FrxExpression> Expressions { get; set; } = new List<FrxExpression>();
    }

    public class FrxBand
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual FrxReport Report { get; set; }

        public string BandType { get; set; } = "Detail"; // PageHeader, Header, Detail, Footer, PageFooter, Summary
        public int Height { get; set; } = 50;
        public int Top { get; set; } = 0;
        public int Left { get; set; } = 0;
        public int Width { get; set; } = 100;
        public int Order { get; set; } = 0;
        public bool IsVisible { get; set; } = true;
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 10;
        public bool FontBold { get; set; } = false;
        public string BackgroundColor { get; set; } = "#FFFFFF";

        public virtual ICollection<FrxField> Fields { get; set; } = new List<FrxField>();
    }

    public class FrxField
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BandId { get; set; }

        [ForeignKey("BandId")]
        public virtual FrxBand Band { get; set; }

        public string FieldName { get; set; } = string.Empty;
        public string FieldType { get; set; } = "Text"; // Text, Line, Box, Picture
        public string DataSource { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public int Left { get; set; } = 0;
        public int Top { get; set; } = 0;
        public int Width { get; set; } = 100;
        public int Height { get; set; } = 20;
        public string Format { get; set; } = string.Empty;
        public string Alignment { get; set; } = "Left";
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 10;
        public bool FontBold { get; set; } = false;
        public string BorderStyle { get; set; } = "None";
        public bool IsVisible { get; set; } = true;
        public int Order { get; set; } = 0;
    }

    public class FrxExpression
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual FrxReport Report { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public string ResultType { get; set; } = "String";
        public int Order { get; set; } = 0;
    }
}