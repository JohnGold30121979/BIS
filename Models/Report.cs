using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class Report
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string DataSourceType { get; set; } = "Catalog";

        public Guid? DataSourceId { get; set; }

        public string ReportType { get; set; } = "Table";

        public string Template { get; set; } = string.Empty;

        public string Settings { get; set; } = "{}";

        public string Icon { get; set; } = "📊";

        [MaxLength(100)]
        public string Code { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public bool IsPrintForm { get; set; }
        public bool IsDefault { get; set; }

        [MaxLength(30)]
        public string SourceFormat { get; set; } = "Native";

        public int TemplateVersion { get; set; } = 1;

        public int Order { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Настройки отчета
        public string PageTitle { get; set; } = string.Empty;
        public string PageOrientation { get; set; } = "Portrait"; // Portrait, Landscape
        public int PageWidth { get; set; } = 210;
        public int PageHeight { get; set; } = 297;
        public int LeftMargin { get; set; } = 20;
        public int RightMargin { get; set; } = 20;
        public int TopMargin { get; set; } = 20;
        public int BottomMargin { get; set; } = 20;
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 10;
        public bool ShowHeader { get; set; } = true;
        public bool ShowFooter { get; set; } = true;
        public bool ShowPageNumbers { get; set; } = true;
        public bool ShowGridLines { get; set; } = true;        
        public string AlternateRowColor { get; set; } = "#F8F9FA";

        // Текст шапки и подвала
        public string HeaderTitle { get; set; } = string.Empty;
        public string HeaderSubtitle { get; set; } = string.Empty;
        public string HeaderLogo { get; set; } = string.Empty;
        public string HeaderText { get; set; } = string.Empty;
        public string FooterText { get; set; } = string.Empty;
        public string FooterTotalText { get; set; } = string.Empty;
        public string FooterSignature { get; set; } = string.Empty;

        public virtual ICollection<ReportField> Fields { get; set; } = new List<ReportField>();
        public virtual ICollection<ReportFilter> Filters { get; set; } = new List<ReportFilter>();
        public virtual ICollection<ReportGroup> Groups { get; set; } = new List<ReportGroup>();
        public virtual ICollection<ReportHeaderFooter> HeadersFooters { get; set; } = new List<ReportHeaderFooter>();

        public string TitleText { get; set; } = string.Empty;
        public string SubtitleText { get; set; } = string.Empty;
        public string SummaryText { get; set; } = string.Empty;
        public bool AlternateRowColors { get; set; } = true;
        public bool ShowGrandTotal { get; set; } = true;
        public string HeaderColor { get; set; } = "#2C3E50";

        [NotMapped]
        public string AvailabilityDisplay => IsActive ? "Доступен" : "Отключен";
    }

    public class ReportField
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual Report Report { get; set; }

        public string FieldName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AggregateType { get; set; } = string.Empty;
        public int Order { get; set; } = 0;
        public int Width { get; set; } = 100;
        public string Alignment { get; set; } = "Left";
        public string Format { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
    }

    public class ReportFilter
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual Report Report { get; set; }

        public string FieldName { get; set; } = string.Empty;
        public string Operation { get; set; } = "=";
        public string Value { get; set; } = string.Empty;
        public string Value2 { get; set; } = string.Empty;
        public int Order { get; set; } = 0;
    }

    public class ReportGroup
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual Report Report { get; set; }

        public string FieldName { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public string Footer { get; set; } = string.Empty;
        public int Order { get; set; } = 0;
        public bool ShowHeader { get; set; } = true;
        public bool ShowFooter { get; set; } = true;
        public bool PageBreak { get; set; } = false;
    }

    public class ReportHeaderFooter
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ReportId { get; set; }

        [ForeignKey("ReportId")]
        public virtual Report Report { get; set; }

        public string SectionType { get; set; } = "Header"; // Header, Footer, PageHeader, PageFooter
        public int Height { get; set; } = 50;
        public string Alignment { get; set; } = "Left";
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 12;
        public bool IsBold { get; set; } = false;
        public string Content { get; set; } = string.Empty;
        public int Order { get; set; } = 0;
    }

    public class ReportSettings
    {
        public string PageTitle { get; set; } = string.Empty;
        public string PageOrientation { get; set; } = "Portrait"; // Portrait, Landscape
        public int PageWidth { get; set; } = 210; // мм
        public int PageHeight { get; set; } = 297; // мм
        public int LeftMargin { get; set; } = 20;
        public int RightMargin { get; set; } = 20;
        public int TopMargin { get; set; } = 20;
        public int BottomMargin { get; set; } = 20;
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 10;
        public bool ShowHeader { get; set; } = true;
        public bool ShowFooter { get; set; } = true;
        public bool ShowPageNumbers { get; set; } = true;
        public bool ShowGridLines { get; set; } = true;
        public string HeaderColor { get; set; } = "#2C3E50";
        public string AlternateRowColor { get; set; } = "#F8F9FA";
    }
}
