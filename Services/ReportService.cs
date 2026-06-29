using System.Collections.Generic;
using System.Data;
using System.Linq;
using Npgsql;
using BIS.ERP.Data;
using BIS.ERP.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BIS.ERP.Services
{
    public class ReportService
    {
        private readonly AppDbContext _context;

        public ReportService(AppDbContext context)
        {
            _context = context;
        }

        // Получение данных для отчета
        public async Task<DataTable> GetReportDataAsync(Report report, Dictionary<string, object> parameters = null)
        {
            var dataTable = new DataTable();
            dataTable.TableName = report?.Name ?? "Report";

            try
            {
                if (report == null)
                    throw new Exception("Отчет не выбран");

                if (!report.DataSourceId.HasValue)
                    throw new Exception($"Для отчета \"{report.Name}\" не выбран источник данных");

                if (report?.DataSourceId.HasValue == true)
                {
                    var catalog = await _context.MetadataObjects
                        .Include(c => c.Fields)
                        .FirstOrDefaultAsync(m => m.Id == report.DataSourceId);

                    if (catalog == null)
                    {
                        throw new Exception($"Справочник не найден");
                    }

                    var configuredFields = report.Fields ?? new List<ReportField>();
                    var selectedFields = configuredFields
                        .Where(field => field.IsVisible)
                        .OrderBy(field => field.Order)
                        .ToList();
                    if (selectedFields.Count == 0 && configuredFields.Count == 0)
                    {
                        selectedFields = catalog.Fields
                            .OrderBy(field => field.Order)
                            .Where(field => !string.Equals(field.DbColumnName, "Id", StringComparison.OrdinalIgnoreCase))
                            .Select((field, index) => new ReportField
                            {
                                FieldName = field.DbColumnName,
                                DisplayName = field.Name,
                                Order = index + 1,
                                IsVisible = true,
                                Width = 120
                            })
                            .ToList();
                    }

                    var selectColumns = new List<string>();

                    foreach (var field in selectedFields)
                    {
                        var metadataField = FindMetadataField(catalog, field.FieldName);
                        if (metadataField == null)
                            throw new Exception($"Поле '{field.FieldName}' отсутствует в источнике '{catalog.Name}'");

                        var source = QuoteIdentifier(metadataField.DbColumnName);
                        if (metadataField.FieldType == "Reference")
                            source = $"CAST({source} AS text)";

                        var displayName = string.IsNullOrWhiteSpace(field.DisplayName)
                            ? metadataField.Name
                            : field.DisplayName;
                        selectColumns.Add($"{source} AS {QuoteIdentifier(displayName)}");
                    }

                    if (selectColumns.Count == 0)
                        throw new Exception("В отчете не выбрано ни одного видимого поля");

                    using var command = _context.Database.GetDbConnection().CreateCommand();
                    var whereClauses = BuildFilterClauses(command, catalog, report.Filters);
                    var whereSql = whereClauses.Count == 0
                        ? string.Empty
                        : $" WHERE {string.Join(" AND ", whereClauses)}";
                    command.CommandText =
                        $"SELECT {string.Join(", ", selectColumns)} FROM {QuoteIdentifier(catalog.TableName)}{whereSql}";
                    command.CommandTimeout = 30;
                    var connectionOpened = false;

                    try
                    {
                        await _context.Database.OpenConnectionAsync();
                        connectionOpened = true;

                        using var reader = await command.ExecuteReaderAsync();
                        dataTable.Load(reader);
                    }
                    finally
                    {
                        if (connectionOpened)
                        {
                            await _context.Database.CloseConnectionAsync();
                        }
                    }

                    await ResolveReportReferencesAsync(dataTable, catalog, selectedFields);
                }
            } catch (PostgresException ex) 
            {
                System.Diagnostics.Debug.WriteLine($"=== POSTGRES ERROR ===");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.MessageText}");
                System.Diagnostics.Debug.WriteLine($"SqlState: {ex.SqlState}");
                System.Diagnostics.Debug.WriteLine($"Position: {ex.Position}");
                throw new Exception($"Ошибка PostgreSQL: {ex.MessageText}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка получения данных: {ex.Message}");
            }

            return dataTable;
        }

        private static MetadataField? FindMetadataField(MetadataObject source, string fieldName)
        {
            return source.Fields.FirstOrDefault(field =>
                field.DbColumnName.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        }

        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Пустой идентификатор базы данных");

            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }

        private static List<string> BuildFilterClauses(
            System.Data.Common.DbCommand command,
            MetadataObject source,
            IEnumerable<ReportFilter> filters)
        {
            var clauses = new List<string>();
            var index = 0;

            foreach (var filter in filters
                         .Where(filter => !string.IsNullOrWhiteSpace(filter.FieldName))
                         .OrderBy(filter => filter.Order))
            {
                var field = FindMetadataField(source, filter.FieldName);
                if (field == null)
                    throw new Exception($"Поле фильтра '{filter.FieldName}' отсутствует в источнике '{source.Name}'");

                var column = QuoteIdentifier(field.DbColumnName);
                var operation = (filter.Operation ?? "=").Trim();
                var parameterName = $"@filter{index++}";

                if (operation.Equals("Like", StringComparison.OrdinalIgnoreCase))
                {
                    AddParameter(command, parameterName, $"%{filter.Value}%");
                    clauses.Add($"CAST({column} AS text) ILIKE {parameterName}");
                    continue;
                }

                if (operation.Equals("Between", StringComparison.OrdinalIgnoreCase))
                {
                    var secondParameterName = $"@filter{index++}";
                    AddParameter(command, parameterName, ConvertFilterValue(filter.Value, field.FieldType));
                    AddParameter(command, secondParameterName, ConvertFilterValue(filter.Value2, field.FieldType));
                    clauses.Add($"{column} BETWEEN {parameterName} AND {secondParameterName}");
                    continue;
                }

                if (operation is not ("=" or "<" or ">" or "<=" or ">=" or "<>"))
                    throw new Exception($"Операция фильтра '{operation}' не поддерживается");

                AddParameter(command, parameterName, ConvertFilterValue(filter.Value, field.FieldType));
                clauses.Add($"{column} {operation} {parameterName}");
            }

            return clauses;
        }

        private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static object ConvertFilterValue(string value, string fieldType)
        {
            return fieldType switch
            {
                "Int" when int.TryParse(value, out var integer) => integer,
                "Decimal" when decimal.TryParse(value, out var number) => number,
                "DateTime" when DateTime.TryParse(value, out var date) => date,
                "Bool" when bool.TryParse(value, out var boolean) => boolean,
                _ => value ?? string.Empty
            };
        }

        private async Task ResolveReportReferencesAsync(
            DataTable table,
            MetadataObject source,
            IReadOnlyCollection<ReportField> reportFields)
        {
            var referenceFields = reportFields
                .Select(reportField => new
                {
                    ReportField = reportField,
                    MetadataField = FindMetadataField(source, reportField.FieldName)
                })
                .Where(item => item.MetadataField?.FieldType == "Reference")
                .ToList();

            if (referenceFields.Count == 0)
                return;

            var maps = await ReferenceDisplayHelper.LoadMapsAsync(source, new MetadataService(_context));
            foreach (var item in referenceFields)
            {
                var columnName = string.IsNullOrWhiteSpace(item.ReportField.DisplayName)
                    ? item.MetadataField!.Name
                    : item.ReportField.DisplayName;
                if (!table.Columns.Contains(columnName) || !maps.TryGetValue(item.MetadataField!.Name, out var map))
                    continue;

                table.Columns[columnName]!.ReadOnly = false;
                foreach (DataRow row in table.Rows)
                {
                    if (Guid.TryParse(row[columnName]?.ToString(), out var id) && map.TryGetValue(id, out var displayValue))
                        row[columnName] = displayValue;
                }
            }
        }

        // Вспомогательный метод проверки существования таблицы
        private async Task<bool> CheckTableExistsAsync(string tableName)
        {
            var sql = @"SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = @tableName)";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var param = command.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = tableName;
            command.Parameters.Add(param);

            await _context.Database.OpenConnectionAsync();
            var result = await command.ExecuteScalarAsync();
            await _context.Database.CloseConnectionAsync();

            return result != null && (bool)result;
        }

        private void ApplyFilters(DataTable dataTable, Report report)
        {
            if (!report.Filters.Any()) return;

            var filterExpression = new StringBuilder();
            foreach (var filter in report.Filters.OrderBy(f => f.Order))
            {
                if (filterExpression.Length > 0)
                    filterExpression.Append(" AND ");

                filterExpression.Append(GetFilterExpression(filter));
            }

            if (filterExpression.Length > 0)
            {
                dataTable.DefaultView.RowFilter = filterExpression.ToString();
            }
        }

        private string GetFilterExpression(ReportFilter filter)
        {
            return filter.Operation switch
            {
                "=" => $"[{filter.FieldName}] = '{filter.Value}'",
                "<" => $"[{filter.FieldName}] < {filter.Value}",
                ">" => $"[{filter.FieldName}] > {filter.Value}",
                "<=" => $"[{filter.FieldName}] <= {filter.Value}",
                ">=" => $"[{filter.FieldName}] >= {filter.Value}",
                "Like" => $"[{filter.FieldName}] LIKE '%{filter.Value}%'",
                "Between" => $"[{filter.FieldName}] BETWEEN {filter.Value} AND {filter.Value2}",
                _ => $"1=1"
            };
        }

        private void ApplyGroups(DataTable dataTable, Report report)
        {
            if (!report.Groups.Any()) return;

            // Группировка через DataTable.Select и агрегация
            var groupFields = report.Groups.OrderBy(g => g.Order).Select(g => g.FieldName).ToArray();
            var groupedData = dataTable.AsEnumerable()
                .GroupBy(r => string.Join("|", groupFields.Select(g => r[g])))
                .ToList();
        }

        private void SelectFields(DataTable dataTable, Report report)
        {
            var visibleFields = report.Fields.Where(f => f.IsVisible).OrderBy(f => f.Order).ToList();

            if (visibleFields.Any())
            {
                var columnsToRemove = dataTable.Columns.Cast<DataColumn>()
                    .Where(c => !visibleFields.Any(f => f.FieldName == c.ColumnName))
                    .ToList();

                foreach (var col in columnsToRemove)
                {
                    dataTable.Columns.Remove(col);
                }

                // Переименовываем колонки
                foreach (var field in visibleFields)
                {
                    if (dataTable.Columns.Contains(field.FieldName))
                    {
                        dataTable.Columns[field.FieldName].ColumnName = field.DisplayName;
                    }
                }
            }
        }

        // Экспорт в Excel
        public byte[] ExportToExcel(DataTable dataTable, Report report)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(report.Name);

            // Заголовок отчета
            var titleRow = worksheet.Cell(1, 1);
            titleRow.Value = report.Name;
            titleRow.Style.Font.Bold = true;
            titleRow.Style.Font.FontSize = 16;

            // Дата формирования
            worksheet.Cell(2, 1).Value = $"Дата формирования: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";

            // Таблица данных
            var table = worksheet.Cell(4, 1).InsertTable(dataTable);
            table.Theme = XLTableTheme.TableStyleMedium2;

            // Автоширина колонок
            worksheet.Columns().AdjustToContents();

            using var stream = new System.IO.MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] ExportToPdf(DataTable dataTable, Report report)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var organization = GetPrimaryOrganization();
            var generatedAt = DateTime.Now;

            return QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(report.PageOrientation == "Landscape" ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(Math.Max(8, report.LeftMargin), Unit.Millimetre);
                    page.DefaultTextStyle(x => x
                        .FontSize(report.FontSize > 0 ? report.FontSize : 9)
                        .FontFamily(string.IsNullOrWhiteSpace(report.FontName) ? "Arial" : report.FontName));

                    if (report.ShowHeader)
                    {
                        page.Header().Column(column =>
                        {
                            var title = report.ReportType == "InvoiceMaterialsKg"
                                ? "СЧЕТ-ФАКТУРА"
                                : string.IsNullOrWhiteSpace(report.TitleText) ? report.Name : report.TitleText;
                            column.Item().Text(title).Bold().FontSize(16);

                            if (!string.IsNullOrWhiteSpace(report.SubtitleText))
                                column.Item().Text(report.SubtitleText).FontSize(11);
                            if (!string.IsNullOrWhiteSpace(report.HeaderText))
                                column.Item().PaddingTop(4).Text(report.HeaderText);

                            column.Item().PaddingTop(8).Text(organization.DisplayName).Bold();
                            if (!string.IsNullOrWhiteSpace(organization.Inn) || !string.IsNullOrWhiteSpace(organization.Okpo))
                                column.Item().Text($"ИНН: {organization.Inn}  ОКПО: {organization.Okpo}");
                            column.Item().PaddingBottom(8).LineHorizontal(1).LineColor(report.HeaderColor);
                        });
                    }

                    page.Content().Column(column =>
                    {
                        if (!string.IsNullOrWhiteSpace(report.Description))
                            column.Item().PaddingBottom(8).Text(report.Description);

                        column.Item().Table(table =>
                        {
                            var columns = dataTable.Columns.Cast<DataColumn>().ToList();
                            table.ColumnsDefinition(definition =>
                            {
                                foreach (var dataColumn in columns)
                                {
                                    var field = report.Fields.FirstOrDefault(reportField =>
                                        reportField.DisplayName == dataColumn.ColumnName);
                                    definition.RelativeColumn(Math.Max(40, field?.Width ?? 100));
                                }
                            });

                            table.Header(header =>
                            {
                                foreach (var dataColumn in columns)
                                {
                                    header.Cell()
                                        .Element(cell => ConfigurePdfCell(cell, report, true, false))
                                        .Text(dataColumn.ColumnName)
                                        .FontColor(Colors.White)
                                        .Bold();
                                }
                            });

                            for (var rowIndex = 0; rowIndex < dataTable.Rows.Count; rowIndex++)
                            {
                                var row = dataTable.Rows[rowIndex];
                                foreach (var dataColumn in columns)
                                {
                                    table.Cell()
                                        .Element(cell => ConfigurePdfCell(
                                            cell, report, false, report.AlternateRowColors && rowIndex % 2 == 1))
                                        .Text(FormatReportValue(row[dataColumn]));
                                }
                            }
                        });

                        if (report.ShowGrandTotal)
                        {
                            foreach (var total in CalculateReportTotals(dataTable, report))
                                column.Item().AlignRight().PaddingTop(4).Text(total).Bold();
                        }

                        if (!string.IsNullOrWhiteSpace(report.SummaryText))
                            column.Item().PaddingTop(12).Text(report.SummaryText);

                        if (report.ReportType == "InvoiceMaterialsKg")
                        {
                            column.Item().PaddingTop(20).Row(row =>
                            {
                                row.RelativeItem().Text($"Руководитель: {organization.Director}");
                                row.RelativeItem().Text($"Главный бухгалтер: {organization.ChiefAccountant}");
                            });
                        }
                    });

                    if (report.ShowFooter || report.ShowPageNumbers)
                    {
                        page.Footer().Row(row =>
                        {
                            row.RelativeItem().Text(report.FooterText ?? string.Empty);
                            row.RelativeItem().AlignRight().Text(text =>
                            {
                                text.Span($"Сформировано: {generatedAt:dd.MM.yyyy HH:mm}");
                                if (report.ShowPageNumbers)
                                {
                                    text.Span("  Стр. ");
                                    text.CurrentPageNumber();
                                    text.Span(" из ");
                                    text.TotalPages();
                                }
                            });
                        });
                    }
                });
            }).GeneratePdf();
        }

        private static IContainer ConfigurePdfCell(
            IContainer container,
            Report report,
            bool isHeader,
            bool isAlternate)
        {
            var result = container.Padding(4);
            if (report.ShowGridLines)
                result = result.Border(0.5f).BorderColor(Colors.Grey.Lighten1);
            if (isHeader)
                result = result.Background(report.HeaderColor);
            else if (isAlternate)
                result = result.Background(report.AlternateRowColor);
            return result;
        }

        private static IEnumerable<string> CalculateReportTotals(DataTable table, Report report)
        {
            foreach (var field in report.Fields.Where(field =>
                         field.IsVisible && !string.IsNullOrWhiteSpace(field.AggregateType)))
            {
                var columnName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.FieldName : field.DisplayName;
                if (!table.Columns.Contains(columnName))
                    continue;

                var values = table.Rows.Cast<DataRow>()
                    .Select(row => row[columnName])
                    .Where(value => value != null && value != DBNull.Value)
                    .ToList();
                var numericValues = values
                    .Select(value => decimal.TryParse(value.ToString(), out var number) ? (decimal?)number : null)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToList();

                var total = field.AggregateType switch
                {
                    "Count" => values.Count.ToString(),
                    "Sum" when numericValues.Count > 0 => numericValues.Sum().ToString("N2"),
                    "Average" when numericValues.Count > 0 => numericValues.Average().ToString("N2"),
                    "Min" when numericValues.Count > 0 => numericValues.Min().ToString("N2"),
                    "Max" when numericValues.Count > 0 => numericValues.Max().ToString("N2"),
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(total))
                    yield return $"{columnName} ({field.AggregateType}): {total}";
            }
        }

        // Экспорт в HTML

        // Генерация HTML отчета с шапкой и подвалом
        // Генерация HTML отчета с шапкой и подвалом
        public string GenerateReportHtml(DataTable dataTable, Report report)
        {
            if (report.ReportType == "InvoiceMaterialsKg")
            {
                return GenerateMaterialsInvoiceHtml(dataTable, report);
            }

            var html = new StringBuilder();

            // Простые настройки           
            bool showGridLines = true;          

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine($"<meta charset='UTF-8'>");
            html.AppendLine($"<title>{EncodeHtml(report.Name)}</title>");
            html.AppendLine(@"
        <style>
            body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 10pt; margin: 20mm; }
            .report-header { background-color: #2C3E50; color: white; padding: 15px; margin-bottom: 20px; }
            .report-title { font-size: 20pt; font-weight: bold; }
            .report-subtitle { font-size: 12pt; opacity: 0.8; margin-top: 5px; }
            .report-footer { background-color: #F8F9FA; padding: 15px; margin-top: 20px; font-size: 9pt; }
            table { border-collapse: collapse; width: 100%; margin-top: 20px; }
            th { background-color: #2C3E50; color: white; padding: 10px; text-align: left; }
            td { padding: 8px; " + (showGridLines ? "border: 1px solid #ddd;" : "border: none;") + @" }
            tr:nth-child(even) { background-color: #F8F9FA; }
            .page-number { text-align: right; font-size: 9pt; color: #666; margin-top: 10px; }
            .total-row { font-weight: bold; background-color: #F0F4F8; text-align: right; margin-top: 10px; padding: 10px; }
        </style>
    ");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            // Шапка
            html.AppendLine($@"
        <div class='report-header'>
            <div class='report-title'>{EncodeHtml(report.Name)}</div>
            <div class='report-subtitle'>{EncodeHtml(report.Description)}</div>
            <div class='report-date'>Дата: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</div>
        </div>
    ");

            // Таблица
            if (dataTable.Rows.Count > 0)
            {
                html.AppendLine("<table>");
                html.AppendLine("<thead><tr>");
                foreach (DataColumn column in dataTable.Columns)
                {
                    html.AppendLine($"<th>{EncodeHtml(column.ColumnName)}</th>");
                }
                html.AppendLine("</tr></thead>");

                html.AppendLine("<tbody>");
                decimal total = 0;
                foreach (DataRow row in dataTable.Rows)
                {
                    html.AppendLine("<tr>");
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        var value = row[column];
                        html.AppendLine($"<td>{EncodeHtml(value)}</td>");

                        if (column.DataType == typeof(decimal) || column.DataType == typeof(int))
                        {
                            if (decimal.TryParse(value?.ToString(), out decimal num))
                                total += num;
                        }
                    }
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</tbody>");
                html.AppendLine("</table>");

                // Итоговая строка
                if (total > 0)
                {
                    html.AppendLine($@"<div class='total-row'>Итого: {total:N2}</div>");
                }
            }
            else
            {
                html.AppendLine("<p style='text-align: center; color: #666;'>Нет данных для отображения</p>");
            }

            // Подвал
            html.AppendLine($@"
        <div class='report-footer'>
            <div>© BIS ERP - Система управления предприятием</div>
            <div class='page-number'>Страница 1 из 1</div>
        </div>
    ");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private static string EncodeHtml(object? value)
        {
            return WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);
        }

        public string ExportToHtml(DataTable dataTable, Report report)
        {
            return GenerateReportHtml(dataTable, report);
        }

        private string GenerateMaterialsInvoiceHtml(DataTable dataTable, Report report)
        {
            var organization = GetPrimaryOrganization();
            var html = new StringBuilder();

            html.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            html.AppendLine($"<title>{EncodeHtml(report.Name)}</title>");
            html.AppendLine(@"
                <style>
                    body { font-family: Arial, sans-serif; font-size: 10pt; margin: 18mm; color: #111; }
                    .title { text-align: center; font-size: 18pt; font-weight: bold; margin-bottom: 3mm; }
                    .subtitle { text-align: center; font-size: 11pt; margin-bottom: 8mm; }
                    .org { margin-bottom: 6mm; line-height: 1.45; }
                    table { border-collapse: collapse; width: 100%; }
                    th, td { border: 1px solid #333; padding: 5px; vertical-align: top; }
                    th { background: #f0f0f0; text-align: center; }
                    .signatures { display: flex; justify-content: space-between; margin-top: 18mm; }
                    .note { margin-top: 8mm; font-size: 8pt; color: #555; }
                </style>");
            html.AppendLine("</head><body>");
            html.AppendLine("<div class='title'>СЧЕТ-ФАКТУРА</div>");
            html.AppendLine("<div class='subtitle'>на материалы</div>");
            html.AppendLine("<div class='org'>");
            html.AppendLine($"<strong>Поставщик:</strong> {EncodeHtml(organization.DisplayName)}<br>");
            html.AppendLine($"<strong>ИНН:</strong> {EncodeHtml(organization.Inn)} &nbsp; <strong>ОКПО:</strong> {EncodeHtml(organization.Okpo)}<br>");
            html.AppendLine($"<strong>Адрес:</strong> {EncodeHtml(organization.Address)}<br>");
            html.AppendLine($"<strong>Банк:</strong> {EncodeHtml(organization.BankName)} &nbsp; <strong>Счет:</strong> {EncodeHtml(organization.BankAccount)} &nbsp; <strong>БИК:</strong> {EncodeHtml(organization.Bic)}");
            html.AppendLine("</div>");

            html.AppendLine("<table><thead><tr>");
            foreach (DataColumn column in dataTable.Columns)
                html.AppendLine($"<th>{EncodeHtml(column.ColumnName)}</th>");
            html.AppendLine("</tr></thead><tbody>");

            foreach (DataRow row in dataTable.Rows)
            {
                html.AppendLine("<tr>");
                foreach (DataColumn column in dataTable.Columns)
                    html.AppendLine($"<td>{EncodeHtml(FormatReportValue(row[column]))}</td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table>");
            html.AppendLine("<div class='signatures'>");
            html.AppendLine($"<div>Руководитель: {EncodeHtml(organization.Director)} __________________</div>");
            html.AppendLine($"<div>Главный бухгалтер: {EncodeHtml(organization.ChiefAccountant)} __________________</div>");
            html.AppendLine("</div>");
            html.AppendLine("<div class='note'>Форма является настраиваемым шаблоном BIS ERP для печатных форм КР. Проверьте реквизиты организации перед печатью.</div>");
            html.AppendLine("</body></html>");

            return html.ToString();
        }

        private ReportOrganizationInfo GetPrimaryOrganization()
        {
            var catalog = _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefault(m => m.ObjectType == "Catalog" && m.Name == "Организации");

            if (catalog == null)
                return ReportOrganizationInfo.Empty;

            var sql = $@"
                SELECT *
                FROM ""{catalog.TableName}""
                ORDER BY COALESCE(""is_primary"", false) DESC, ""code"", ""CreatedAt""
                LIMIT 1";

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            var opened = false;
            try
            {
                if (_context.Database.GetDbConnection().State != ConnectionState.Open)
                {
                    _context.Database.OpenConnection();
                    opened = true;
                }

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return ReportOrganizationInfo.Empty;

                string GetString(string column)
                {
                    try
                    {
                        var ordinal = reader.GetOrdinal(column);
                        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal)?.ToString() ?? string.Empty;
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }

                return new ReportOrganizationInfo
                {
                    Name = GetString("name"),
                    FullName = GetString("full_name"),
                    Inn = GetString("inn"),
                    Okpo = GetString("okpo"),
                    Address = !string.IsNullOrWhiteSpace(GetString("legal_address")) ? GetString("legal_address") : GetString("actual_address"),
                    BankName = GetString("bank_name"),
                    BankAccount = GetString("bank_account"),
                    Bic = GetString("bic"),
                    Director = GetString("director"),
                    ChiefAccountant = GetString("chief_accountant")
                };
            }
            finally
            {
                if (opened)
                    _context.Database.CloseConnection();
            }
        }

        private static string FormatReportValue(object value)
        {
            return value switch
            {
                null => string.Empty,
                DBNull => string.Empty,
                DateTime date => date.ToString("dd.MM.yyyy"),
                decimal number => number.ToString("N2"),
                double number => number.ToString("N2"),
                float number => number.ToString("N2"),
                _ => value.ToString() ?? string.Empty
            };
        }

        private sealed class ReportOrganizationInfo
        {
            public static ReportOrganizationInfo Empty { get; } = new()
            {
                Name = "Основное предприятие",
                FullName = "Основное предприятие"
            };

            public string Name { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Inn { get; set; } = string.Empty;
            public string Okpo { get; set; } = string.Empty;
            public string Address { get; set; } = string.Empty;
            public string BankName { get; set; } = string.Empty;
            public string BankAccount { get; set; } = string.Empty;
            public string Bic { get; set; } = string.Empty;
            public string Director { get; set; } = string.Empty;
            public string ChiefAccountant { get; set; } = string.Empty;

            public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Name : FullName;
        }

        // Сохранение отчета      
        public async Task<Report> SaveReportAsync(Report report)
        {
            try
            {
                await new PrintFormService(_context).EnsureSchemaAsync();
                if (string.IsNullOrWhiteSpace(report.Code))
                    report.Code = $"report.{Guid.NewGuid():N}";
                if (report.Id == Guid.Empty)
                {
                    // Новый отчет
                    report.Id = Guid.NewGuid();
                    report.CreatedAt = DateTime.UtcNow;
                    report.UpdatedAt = DateTime.UtcNow;

                    // Сохраняем поля
                    foreach (var field in report.Fields)
                    {
                        if (field.Id == Guid.Empty)
                            field.Id = Guid.NewGuid();
                        field.ReportId = report.Id;
                    }

                    // Сохраняем фильтры
                    foreach (var filter in report.Filters)
                    {
                        if (filter.Id == Guid.Empty)
                            filter.Id = Guid.NewGuid();
                        filter.ReportId = report.Id;
                    }

                    foreach (var mapping in report.ElementMappings)
                    {
                        if (mapping.Id == Guid.Empty)
                            mapping.Id = Guid.NewGuid();
                        mapping.ReportId = report.Id;
                    }

                    await _context.Reports.AddAsync(report);
                }
                else
                {
                    // Существующий отчет - обновляем
                    var existingReport = await _context.Reports
                        .Include(r => r.Fields)
                        .Include(r => r.Filters)
                        .Include(r => r.ElementMappings)
                        .FirstOrDefaultAsync(r => r.Id == report.Id);

                    if (existingReport != null)
                    {
                        existingReport.Name = report.Name;
                        existingReport.Description = report.Description;
                        existingReport.DataSourceType = report.DataSourceType;
                        existingReport.DataSourceId = report.DataSourceId;
                        existingReport.ReportType = report.ReportType;
                        existingReport.Icon = report.Icon;
                        CopyPrintFormSettings(report, existingReport);
                        existingReport.Order = report.Order;
                        CopyReportLayout(report, existingReport);
                        existingReport.UpdatedAt = DateTime.UtcNow;

                        // Обновляем поля
                        _context.ReportFields.RemoveRange(existingReport.Fields);
                        foreach (var field in report.Fields)
                        {
                            field.Id = Guid.NewGuid();
                            field.ReportId = report.Id;
                            existingReport.Fields.Add(field);
                        }

                        // Обновляем фильтры
                        _context.ReportFilters.RemoveRange(existingReport.Filters);
                        foreach (var filter in report.Filters)
                        {
                            filter.Id = Guid.NewGuid();
                            filter.ReportId = report.Id;
                            existingReport.Filters.Add(filter);
                        }

                        _context.ReportElementMappings.RemoveRange(existingReport.ElementMappings);
                        foreach (var mapping in report.ElementMappings)
                        {
                            mapping.Id = Guid.NewGuid();
                            mapping.ReportId = report.Id;
                            existingReport.ElementMappings.Add(mapping);
                        }

                        _context.Reports.Update(existingReport);
                    }
                    else
                    {
                        // Отчет не найден - добавляем как новый
                        report.Id = Guid.NewGuid();
                        report.CreatedAt = DateTime.UtcNow;
                        report.UpdatedAt = DateTime.UtcNow;
                        foreach (var field in report.Fields)
                        {
                            field.Id = field.Id == Guid.Empty ? Guid.NewGuid() : field.Id;
                            field.ReportId = report.Id;
                        }
                        foreach (var filter in report.Filters)
                        {
                            filter.Id = filter.Id == Guid.Empty ? Guid.NewGuid() : filter.Id;
                            filter.ReportId = report.Id;
                        }
                        foreach (var mapping in report.ElementMappings)
                        {
                            mapping.Id = mapping.Id == Guid.Empty ? Guid.NewGuid() : mapping.Id;
                            mapping.ReportId = report.Id;
                        }
                        await _context.Reports.AddAsync(report);
                    }
                }

                await _context.SaveChangesAsync();
                if (report.IsPrintForm && report.IsDefault && report.DataSourceId.HasValue)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE ""Reports"" SET ""IsDefault"" = false
                        WHERE ""DataSourceId"" = {report.DataSourceId.Value}
                          AND ""IsPrintForm"" = true AND ""Id"" <> {report.Id}");
                }
                return report;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка сохранения отчета: {ex.Message}");
            }
        }

        // Получение всех отчетов
        public async Task<List<Report>> GetReportsAsync()
        {
            await new PrintFormService(_context).EnsureSchemaAsync();
            return await _context.Set<Report>()
                .Include(r => r.Fields)
                .Include(r => r.Filters)
                .Include(r => r.Groups)
                .Include(r => r.ElementMappings)
                .OrderBy(r => r.Order)
                .ToListAsync();
        }

        public async Task<List<Report>> GetReportHeadersAsync(bool includePrintForms = true)
        {
            await new PrintFormService(_context).EnsureSchemaAsync();
            var query = _context.Set<Report>().AsNoTracking();
            if (!includePrintForms)
                query = query.Where(report => !report.IsPrintForm);

            return await SelectReportHeaders(query)
                .OrderBy(report => report.IsPrintForm ? 1 : 0)
                .ThenBy(report => report.Name)
                .ToListAsync();
        }

        public async Task<List<Report>> GetNavigationReportsAsync()
        {
            var query = _context.Set<Report>().AsNoTracking()
                .Where(report => report.IsActive && !report.IsPrintForm);

            return await SelectReportHeaders(query).OrderBy(report => report.Name).ToListAsync();
        }

        public async Task<Report?> GetReportAsync(Guid reportId)
        {
            await new PrintFormService(_context).EnsureSchemaAsync();
            return await _context.Set<Report>()
                .Include(r => r.Fields)
                .Include(r => r.Filters)
                .Include(r => r.Groups)
                .Include(r => r.ElementMappings)
                .Include(r => r.HeadersFooters)
                .FirstOrDefaultAsync(report => report.Id == reportId);
        }

        private static IQueryable<Report> SelectReportHeaders(IQueryable<Report> query)
        {
            return query.Select(report => new Report
            {
                Id = report.Id,
                Name = report.Name,
                Description = report.Description,
                DataSourceType = report.DataSourceType,
                DataSourceId = report.DataSourceId,
                ReportType = report.ReportType,
                Icon = report.Icon,
                Code = report.Code,
                IsActive = report.IsActive,
                IsPrintForm = report.IsPrintForm,
                IsDefault = report.IsDefault,
                SourceFormat = report.SourceFormat,
                TemplateVersion = report.TemplateVersion,
                Order = report.Order,
                CreatedAt = report.CreatedAt,
                UpdatedAt = report.UpdatedAt,
                PageTitle = report.PageTitle,
                PageOrientation = report.PageOrientation,
                PageWidth = report.PageWidth,
                PageHeight = report.PageHeight,
                FontName = report.FontName,
                FontSize = report.FontSize,
                TitleText = report.TitleText,
                SubtitleText = report.SubtitleText,
                SummaryText = report.SummaryText,
                HeaderColor = report.HeaderColor
            });
        }


        // Добавьте этот метод в класс ReportService (после GetReportsAsync или перед DeleteReportAsync)       
        public async Task UpdateReportAsync(Report report)
        {
            try
            {
                await new PrintFormService(_context).EnsureSchemaAsync();
                var existingReport = await _context.Reports
                    .Include(r => r.Fields)
                    .Include(r => r.Filters)
                    .Include(r => r.ElementMappings)
                    .FirstOrDefaultAsync(r => r.Id == report.Id);

                if (existingReport != null)
                {
                    existingReport.Name = report.Name;
                    existingReport.Description = report.Description;
                    existingReport.Icon = report.Icon;
                    CopyPrintFormSettings(report, existingReport);
                    existingReport.DataSourceType = report.DataSourceType;
                    existingReport.DataSourceId = report.DataSourceId;
                    existingReport.ReportType = report.ReportType;
                    existingReport.Order = report.Order;
                    CopyReportLayout(report, existingReport);
                    existingReport.UpdatedAt = DateTime.UtcNow;

                    _context.ReportElementMappings.RemoveRange(existingReport.ElementMappings);
                    foreach (var mapping in report.ElementMappings)
                    {
                        mapping.Id = Guid.NewGuid();
                        mapping.ReportId = report.Id;
                        existingReport.ElementMappings.Add(mapping);
                    }

                    _context.Reports.Update(existingReport);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    throw new Exception($"Отчет с ID {report.Id} не найден");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обновления отчета: {ex.Message}");
            }
        }

        private static void CopyReportLayout(Report source, Report target)
        {
            target.TitleText = source.TitleText;
            target.SubtitleText = source.SubtitleText;
            target.HeaderText = source.HeaderText;
            target.FooterText = source.FooterText;
            target.SummaryText = source.SummaryText;
            target.PageOrientation = source.PageOrientation;
            target.PageWidth = source.PageWidth;
            target.PageHeight = source.PageHeight;
            target.LeftMargin = source.LeftMargin;
            target.RightMargin = source.RightMargin;
            target.TopMargin = source.TopMargin;
            target.BottomMargin = source.BottomMargin;
            target.FontName = source.FontName;
            target.FontSize = source.FontSize;
            target.ShowHeader = source.ShowHeader;
            target.ShowFooter = source.ShowFooter;
            target.ShowPageNumbers = source.ShowPageNumbers;
            target.ShowGridLines = source.ShowGridLines;
            target.AlternateRowColors = source.AlternateRowColors;
            target.AlternateRowColor = source.AlternateRowColor;
            target.ShowGrandTotal = source.ShowGrandTotal;
            target.HeaderColor = source.HeaderColor;
        }

        private static void CopyPrintFormSettings(Report source, Report target)
        {
            target.Code = source.Code;
            target.IsActive = source.IsActive;
            target.IsPrintForm = source.IsPrintForm;
            target.IsDefault = source.IsDefault;
            target.SourceFormat = source.SourceFormat;
            target.TemplateVersion = source.TemplateVersion;
            target.Template = source.Template;
            target.Settings = source.Settings;
        }
        // Удаление отчета
        // Удаление отчета
        public async Task DeleteReportAsync(Guid reportId)
        {
            var report = await _context.Reports
                .Include(r => r.Fields)
                .Include(r => r.Filters)
                .Include(r => r.ElementMappings)
                .FirstOrDefaultAsync(r => r.Id == reportId);

            if (report != null)
            {
                _context.ReportFields.RemoveRange(report.Fields);
                _context.ReportFilters.RemoveRange(report.Filters);
                _context.ReportElementMappings.RemoveRange(report.ElementMappings);
                _context.Reports.Remove(report);
                await _context.SaveChangesAsync();
            }
        }
    }
}
