using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class FrxToReportConverter
    {
        private readonly FoxExpressionParser _parser;

        public FrxToReportConverter()
        {
            _parser = new FoxExpressionParser();
        }

        public async Task<Report> ConvertToStandardReport(FrxReport frxReport, MetadataService metadataService)
        {
            var report = new Report
            {
                Id = Guid.NewGuid(),
                Name = frxReport.Name,
                Description = frxReport.Description ?? $"Конвертирован из FRX: {frxReport.OriginalFileName}",
                Icon = "📊",
                ReportType = frxReport.ReportType,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Конвертируем банды в настройки
            var headerBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Header");
            var footerBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Footer");
            var detailBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Detail");

            if (headerBand != null)
            {
                report.HeaderTitle = string.Join(" | ", headerBand.Fields.Select(f => f.FieldName));
                report.HeaderColor = "#2C3E50";
            }

            if (footerBand != null)
            {
                report.FooterText = string.Join(" | ", footerBand.Fields.Select(f => f.FieldName));
            }

            // Конвертируем поля
            int order = 0;
            foreach (var frxField in frxReport.Fields.Where(f => f.IsVisible))
            {
                report.Fields.Add(new ReportField
                {
                    Id = Guid.NewGuid(),
                    FieldName = frxField.FieldName,
                    DisplayName = frxField.FieldName,
                    Width = frxField.Width,
                    Alignment = frxField.Alignment.ToLower(),
                    Order = order++,
                    IsVisible = true
                });
            }

            return report;
        }

        public async Task<string> GenerateHtmlFromFrx(FrxReport frxReport, DataTable data)
        {
            var html = new StringBuilder();

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset='UTF-8'>");
            html.AppendLine($"<title>{frxReport.Name}</title>");
            html.AppendLine(@"
                <style>
                    body { font-family: 'Courier New', monospace; margin: 0; padding: 20px; }
                    @media print {
                        body { margin: 0; padding: 0; }
                        .page-break { page-break-before: always; }
                    }
                    .page-header { margin-bottom: 20px; }
                    .report-header { background-color: #f0f0f0; font-weight: bold; border-bottom: 1px solid #000; }
                    .report-detail { }
                    .report-footer { border-top: 1px solid #000; margin-top: 20px; }
                    .report-summary { background-color: #e0e0e0; font-weight: bold; margin-top: 10px; }
                    .field { position: relative; display: inline-block; overflow: hidden; white-space: nowrap; }
                    .total { text-align: right; font-weight: bold; }
                </style>
            ");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            // Page Header
            var pageHeader = frxReport.Bands.FirstOrDefault(b => b.BandType == "PageHeader");
            if (pageHeader != null)
            {
                html.AppendLine("<div class='page-header'>");
                html.AppendLine(RenderBand(pageHeader, data, null));
                html.AppendLine("</div>");
            }

            // Header
            var headerBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Header");
            if (headerBand != null)
            {
                html.AppendLine("<div class='report-header'>");
                html.AppendLine(RenderBand(headerBand, data, null));
                html.AppendLine("</div>");
            }

            // Detail
            var detailBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Detail");
            if (detailBand != null)
            {
                html.AppendLine("<div class='report-detail'>");
                int rowNum = 0;
                foreach (DataRow row in data.Rows)
                {
                    var rowClass = rowNum % 2 == 0 ? "even" : "odd";
                    html.AppendLine($"<div class='row {rowClass}'>");
                    html.AppendLine(RenderBand(detailBand, data, row));
                    html.AppendLine("</div>");
                    rowNum++;
                }
                html.AppendLine("</div>");
            }

            // Footer
            var footerBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Footer");
            if (footerBand != null)
            {
                html.AppendLine("<div class='report-footer'>");
                html.AppendLine(RenderBand(footerBand, data, null));
                html.AppendLine("</div>");
            }

            // Summary
            var summaryBand = frxReport.Bands.FirstOrDefault(b => b.BandType == "Summary");
            if (summaryBand != null)
            {
                html.AppendLine("<div class='report-summary'>");
                html.AppendLine(RenderBand(summaryBand, data, null));
                html.AppendLine("</div>");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private string RenderBand(FrxBand band, DataTable data, DataRow currentRow)
        {
            var sb = new StringBuilder();

            foreach (var field in band.Fields.OrderBy(f => f.Order))
            {
                var value = GetFieldValue(field, currentRow);
                var style = $"left: {field.Left}px; width: {field.Width}px; text-align: {field.Alignment.ToLower()}; " +
                           $"font-family: {field.FontName}; font-size: {field.FontSize}pt; {(field.FontBold ? "font-weight: bold;" : "")}";

                sb.AppendLine($"<div class='field' style='{style}'>{value}</div>");
            }

            return sb.ToString();
        }

        private string GetFieldValue(FrxField field, DataRow row)
        {
            if (row != null && row.Table.Columns.Contains(field.FieldName))
            {
                return row[field.FieldName]?.ToString() ?? "";
            }

            if (!string.IsNullOrEmpty(field.Expression))
            {
                return _parser.Evaluate(field.Expression, row)?.ToString() ?? "";
            }

            return field.FieldName;
        }
    }
}