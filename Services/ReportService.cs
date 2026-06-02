using System.Collections.Generic;
using System.Data;
using System.Linq;
using Npgsql;
using BIS.ERP.Data;
using BIS.ERP.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text;
using System.Threading.Tasks;

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

                    await _context.Database.OpenConnectionAsync();
                    using var reader = await command.ExecuteReaderAsync();
                    dataTable.Load(reader);
                    await _context.Database.CloseConnectionAsync();
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

        // Экспорт в HTML

        // Генерация HTML отчета с шапкой и подвалом
        // Генерация HTML отчета с шапкой и подвалом
        public string GenerateReportHtml(DataTable dataTable, Report report)
        {
            var html = new StringBuilder();

            // Простые настройки
            string headerColor = "#2C3E50";
            bool showGridLines = true;
            bool alternateRowColor = true;

            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine($"<meta charset='UTF-8'>");
            html.AppendLine($"<title>{report.Name}</title>");
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
            <div class='report-title'>{report.Name}</div>
            <div class='report-subtitle'>{report.Description}</div>
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
                    html.AppendLine($"<th>{column.ColumnName}</th>");
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
                        html.AppendLine($"<td>{value}浏览");

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
        public string ExportToHtml(DataTable dataTable, Report report)
        {
            return GenerateReportHtml(dataTable, report);
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