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
                if (report?.DataSourceType == "Catalog" && report.DataSourceId.HasValue)
                {
                    var catalog = await _context.MetadataObjects
                        .Include(c => c.Fields)
                        .FirstOrDefaultAsync(m => m.Id == report.DataSourceId);

                    if (catalog == null)
                    {
                        throw new Exception($"Справочник не найден");
                    }

                    // Формируем SELECT
                    var selectedFields = report.Fields.Where(f => f.IsVisible).ToList();
                    var selectColumns = new List<string>();

                    foreach (var field in selectedFields)
                    {
                        // Здесь field.FieldName = "kod" (латиница)
                        var dbColumnName = field.FieldName;
                        var displayName = field.DisplayName ?? field.FieldName;
                        selectColumns.Add($"\"{dbColumnName}\" AS \"{displayName}\"");
                    }

                    string fieldsSql = selectColumns.Any() ? string.Join(", ", selectColumns) : "*";
                    var sql = $"SELECT {fieldsSql} FROM \"{catalog.TableName}\" LIMIT 5000";

                    // ОТЛАДКА - выводим SQL в окно Output
                    System.Diagnostics.Debug.WriteLine("=== SQL QUERY ===");
                    System.Diagnostics.Debug.WriteLine(sql);
                    System.Diagnostics.Debug.WriteLine("================");

                    using var command = _context.Database.GetDbConnection().CreateCommand();
                    command.CommandText = sql;
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

            var organization = GetPrimaryOrganizationAsync().GetAwaiter().GetResult();
            var generatedAt = DateTime.Now;

            return QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(column =>
                    {
                        if (report.ReportType == "InvoiceMaterialsKg")
                        {
                            column.Item().AlignCenter().Text("СЧЕТ-ФАКТУРА").Bold().FontSize(16);
                            column.Item().AlignCenter().Text("на материалы").FontSize(11);
                        }
                        else
                        {
                            column.Item().Text(report.Name).Bold().FontSize(16);
                        }

                        column.Item().PaddingTop(8).Text(organization.DisplayName).Bold();
                        column.Item().Text($"ИНН: {organization.Inn}  ОКПО: {organization.Okpo}");
                        column.Item().Text($"Адрес: {organization.Address}");
                        column.Item().Text($"Банк: {organization.BankName}  Счет: {organization.BankAccount}  БИК: {organization.Bic}");
                        column.Item().PaddingBottom(8).LineHorizontal(1);
                    });

                    page.Content().Column(column =>
                    {
                        if (!string.IsNullOrWhiteSpace(report.Description))
                            column.Item().PaddingBottom(8).Text(report.Description);

                        column.Item().Table(table =>
                        {
                            var columns = dataTable.Columns.Cast<DataColumn>().ToList();
                            table.ColumnsDefinition(definition =>
                            {
                                foreach (var _ in columns)
                                    definition.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                foreach (var dataColumn in columns)
                                {
                                    header.Cell()
                                        .Background(Colors.Grey.Lighten2)
                                        .Border(1)
                                        .Padding(4)
                                        .Text(dataColumn.ColumnName)
                                        .Bold();
                                }
                            });

                            foreach (DataRow row in dataTable.Rows)
                            {
                                foreach (var dataColumn in columns)
                                {
                                    table.Cell()
                                        .Border(1)
                                        .Padding(4)
                                        .Text(FormatReportValue(row[dataColumn]));
                                }
                            }
                        });

                        if (report.ReportType == "InvoiceMaterialsKg")
                        {
                            column.Item().PaddingTop(20).Row(row =>
                            {
                                row.RelativeItem().Text($"Руководитель: {organization.Director}");
                                row.RelativeItem().Text($"Главный бухгалтер: {organization.ChiefAccountant}");
                            });
                        }
                    });

                    page.Footer().AlignRight().Text(text =>
                    {
                        text.Span($"Сформировано: {generatedAt:dd.MM.yyyy HH:mm}  Стр. ");
                        text.CurrentPageNumber();
                        text.Span(" из ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
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
            var organization = GetPrimaryOrganizationAsync().GetAwaiter().GetResult();
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

        private async Task<ReportOrganizationInfo> GetPrimaryOrganizationAsync()
        {
            var catalog = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.ObjectType == "Catalog" && m.Name == "Организации");

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
                    await _context.Database.OpenConnectionAsync();
                    opened = true;
                }

                using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
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
                    await _context.Database.CloseConnectionAsync();
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

                    await _context.Reports.AddAsync(report);
                }
                else
                {
                    // Существующий отчет - обновляем
                    var existingReport = await _context.Reports
                        .Include(r => r.Fields)
                        .Include(r => r.Filters)
                        .FirstOrDefaultAsync(r => r.Id == report.Id);

                    if (existingReport != null)
                    {
                        existingReport.Name = report.Name;
                        existingReport.Description = report.Description;
                        existingReport.DataSourceType = report.DataSourceType;
                        existingReport.DataSourceId = report.DataSourceId;
                        existingReport.Icon = report.Icon;
                        existingReport.Order = report.Order;
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

                        _context.Reports.Update(existingReport);
                    }
                    else
                    {
                        // Отчет не найден - добавляем как новый
                        report.Id = Guid.NewGuid();
                        report.CreatedAt = DateTime.UtcNow;
                        report.UpdatedAt = DateTime.UtcNow;
                        await _context.Reports.AddAsync(report);
                    }
                }

                await _context.SaveChangesAsync();
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
            return await _context.Set<Report>()
                .Include(r => r.Fields)
                .Include(r => r.Filters)
                .Include(r => r.Groups)
                .OrderBy(r => r.Order)
                .ToListAsync();
        }


        // Добавьте этот метод в класс ReportService (после GetReportsAsync или перед DeleteReportAsync)       
        public async Task UpdateReportAsync(Report report)
        {
            try
            {
                var existingReport = await _context.Reports
                    .Include(r => r.Fields)
                    .Include(r => r.Filters)
                    .FirstOrDefaultAsync(r => r.Id == report.Id);

                if (existingReport != null)
                {
                    existingReport.Name = report.Name;
                    existingReport.Description = report.Description;
                    existingReport.Icon = report.Icon;
                    existingReport.DataSourceType = report.DataSourceType;
                    existingReport.DataSourceId = report.DataSourceId;
                    existingReport.Order = report.Order;
                    existingReport.UpdatedAt = DateTime.UtcNow;

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
        // Удаление отчета
        // Удаление отчета
        public async Task DeleteReportAsync(Guid reportId)
        {
            var report = await _context.Reports
                .Include(r => r.Fields)
                .Include(r => r.Filters)
                .FirstOrDefaultAsync(r => r.Id == reportId);

            if (report != null)
            {
                _context.ReportFields.RemoveRange(report.Fields);
                _context.ReportFilters.RemoveRange(report.Filters);
                _context.Reports.Remove(report);
                await _context.SaveChangesAsync();
            }
        }
    }
}
