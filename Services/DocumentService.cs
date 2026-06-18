using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class DocumentService
    {
        private readonly AppDbContext _context;

        public DocumentService(AppDbContext context)
        {
            _context = context;
        }

        // Получение всех документов
        public async Task<List<DynamicDocument>> GetDocumentsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== GetDocumentsAsync START ===");

                var result = await _context.Set<DynamicDocument>()
                    .Include(d => d.Rows)
                    .OrderByDescending(d => d.Date)
                    .ToListAsync();

                System.Diagnostics.Debug.WriteLine($"GetDocumentsAsync: найдено {result.Count} документов");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDocumentsAsync ERROR: {ex.Message}");
                throw new Exception($"Ошибка получения документов: {ex.Message}");
            }
        }

        // Получение документа по ID
        public async Task<DynamicDocument> GetDocumentByIdAsync(Guid id)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== GetDocumentByIdAsync: {id} ===");

                var document = await _context.Set<DynamicDocument>()
                    .Include(d => d.Rows)
                    .FirstOrDefaultAsync(d => d.Id == id);

                if (document == null)
                    System.Diagnostics.Debug.WriteLine($"Документ {id} не найден");
                else
                    System.Diagnostics.Debug.WriteLine($"Документ найден: {document.Number}, строк: {document.Rows?.Count ?? 0}");

                return document;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDocumentByIdAsync ERROR: {ex.Message}");
                throw new Exception($"Ошибка получения документа: {ex.Message}");
            }
        }

        // Создание документа из DBF данных
        // Создание документа из DBF данных
        // Создание документа из DBF данных
        public async Task<DynamicDocument> CreateDocumentFromDbfAsync(DbfParseResult dbfData, string documentNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== CreateDocumentFromDbfAsync START ===");
                System.Diagnostics.Debug.WriteLine($"Document Number: {documentNumber}");
                System.Diagnostics.Debug.WriteLine($"Table Name: {dbfData.TableName}");
                System.Diagnostics.Debug.WriteLine($"Rows count: {dbfData.Rows.Count}");
                System.Diagnostics.Debug.WriteLine($"Fields count: {dbfData.Fields.Count}");

                var document = new DynamicDocument
                {
                    Id = Guid.NewGuid(),
                    Number = documentNumber,
                    Date = DateTime.UtcNow,
                    DocumentType = dbfData.TableName,
                    SourceFile = dbfData.TableName,
                    TotalRows = dbfData.Rows.Count,
                    CreatedAt = DateTime.UtcNow,
                    Rows = new List<DynamicDocumentRow>()
                };

                int rowNum = 1;
                foreach (var row in dbfData.Rows)
                {
                    // ОЧИСТКА ДАННЫХ: Удаляем NULL символы и другие проблемы
                    var cleanedRow = CleanDictionaryForJson(row);

                    var dataJson = JsonSerializer.Serialize(cleanedRow, new JsonSerializerOptions
                    {
                        WriteIndented = false,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Разрешает больше символов
                    });

                    // Дополнительная очистка JSON строки
                    dataJson = CleanJsonString(dataJson);

                    var documentRow = new DynamicDocumentRow
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        RowNumber = rowNum++,
                        Data = dataJson,
                        CreatedAt = DateTime.UtcNow
                    };

                    document.Rows.Add(documentRow);
                }

                System.Diagnostics.Debug.WriteLine($"Saving to database...");

                await _context.Set<DynamicDocument>().AddAsync(document);
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ успешно создан! ID: {document.Id}");
                return document;
            }
            catch (DbUpdateException ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== DbUpdateException DETAILS ===");
                System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");

                if (ex.InnerException is PostgresException pgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"PostgreSQL Error: {pgEx.SqlState} - {pgEx.MessageText}");
                }

                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateDocumentFromDbfAsync ERROR: {ex.Message}");
                throw;
            }
        }

        // Метод очистки словаря от проблемных символов
        private Dictionary<string, object> CleanDictionaryForJson(Dictionary<string, object> row)
        {
            var cleaned = new Dictionary<string, object>();

            foreach (var kvp in row)
            {
                if (kvp.Value == null)
                {
                    cleaned[kvp.Key] = null;
                    continue;
                }

                if (kvp.Value is string str)
                {
                    // Удаляем NULL символы и другие управляющие символы
                    str = CleanStringForJson(str);
                    cleaned[kvp.Key] = str;
                }
                else if (kvp.Value is decimal dec)
                {
                    // Очищаем decimal значения
                    cleaned[kvp.Key] = dec;
                }
                else if (kvp.Value is int || kvp.Value is long || kvp.Value is short)
                {
                    cleaned[kvp.Key] = kvp.Value;
                }
                else if (kvp.Value is bool boolVal)
                {
                    cleaned[kvp.Key] = boolVal;
                }
                else
                {
                    // Для других типов преобразуем в строку и очищаем
                    var strVal = kvp.Value.ToString();
                    cleaned[kvp.Key] = CleanStringForJson(strVal);
                }
            }

            return cleaned;
        }

        // Метод очистки строки от недопустимых символов для JSON
        private string CleanStringForJson(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Удаляем символ NULL (\u0000)
            var cleaned = input.Replace("\u0000", "");

            // Удаляем другие проблемные Unicode символы
            cleaned = cleaned.Replace("\u0001", "");
            cleaned = cleaned.Replace("\u0002", "");
            cleaned = cleaned.Replace("\u0003", "");
            cleaned = cleaned.Replace("\u0004", "");
            cleaned = cleaned.Replace("\u0005", "");
            cleaned = cleaned.Replace("\u0006", "");
            cleaned = cleaned.Replace("\u0007", "");
            cleaned = cleaned.Replace("\u0008", "");
            cleaned = cleaned.Replace("\u0009", " "); // TAB заменяем на пробел
            cleaned = cleaned.Replace("\u000B", "");
            cleaned = cleaned.Replace("\u000C", "");
            cleaned = cleaned.Replace("\u000E", "");
            cleaned = cleaned.Replace("\u000F", "");

            // Удаляем символы с кодами 0x10-0x1F
            for (int i = 0x10; i <= 0x1F; i++)
            {
                cleaned = cleaned.Replace(((char)i).ToString(), "");
            }

            // Удаляем символ замены (U+FFFD)
            cleaned = cleaned.Replace("\uFFFD", "");

            return cleaned;
        }

        // Дополнительная очистка JSON строки
        private string CleanJsonString(string json)
        {
            if (string.IsNullOrEmpty(json))
                return "{}";

            // Удаляем все вхождения \u0000
            json = json.Replace("\\u0000", "");
            json = json.Replace("\\u0001", "");
            json = json.Replace("\\u0002", "");
            json = json.Replace("\\u0003", "");
            json = json.Replace("\\u0004", "");
            json = json.Replace("\\u0005", "");
            json = json.Replace("\\u0006", "");
            json = json.Replace("\\u0007", "");
            json = json.Replace("\\u0008", "");
            json = json.Replace("\\u0009", " ");
            json = json.Replace("\\u000B", "");
            json = json.Replace("\\u000C", "");
            json = json.Replace("\\u000E", "");
            json = json.Replace("\\u000F", "");

            // Удаляем символы с кодами 0x10-0x1F
            for (int i = 0x10; i <= 0x1F; i++)
            {
                json = json.Replace($"\\u{i:X4}", "");
            }

            json = json.Replace("\\uFFFD", "");

            return json;
        }

        // Получение данных строки в виде словаря
        public Dictionary<string, object> GetRowData(DynamicDocumentRow row)
        {
            if (string.IsNullOrEmpty(row.Data))
                return new Dictionary<string, object>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(row.Data)
                       ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRowData ERROR: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        public Task<List<string>> GetAllFieldNamesAsync(DynamicDocument document)
        {
            var allFields = new HashSet<string>();

            foreach (var row in document.Rows.OrderBy(r => r.RowNumber))
            {
                foreach (var key in GetRowData(row).Keys)
                {
                    allFields.Add(key);
                }
            }

            return Task.FromResult(allFields.OrderBy(f => f).ToList());
        }

        // Обновление документа
        public async Task<DynamicDocument> UpdateDocumentAsync(DynamicDocument document)
        {
            try
            {                
                _context.Set<DynamicDocument>().Update(document);
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ обновлен");
                return document;
            }
            catch (DbUpdateException ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                throw new Exception($"Ошибка БД при обновлении документа: {innerMessage}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка обновления документа: {ex.Message}");
            }
        }
        // Получение количества документов
        public async Task<int> GetDocumentsCountAsync()
        {
            try
            {
                return await _context.Set<DynamicDocument>().CountAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDocumentsCountAsync ERROR: {ex.Message}");
                return 0;
            }
        }
        // Добавьте этот метод в DocumentService.cs
        public async Task<Dictionary<string, object>> GetRowDataAsync(DynamicDocumentRow row)
        {
            if (string.IsNullOrEmpty(row.Data))
                return new Dictionary<string, object>();

            try
            {
                return await Task.FromResult(JsonSerializer.Deserialize<Dictionary<string, object>>(row.Data)
                       ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRowDataAsync ERROR: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }
        // Удаление документа
        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            try
            {
                var document = await GetDocumentByIdAsync(documentId);
                if (document == null) return false;

                _context.Set<DynamicDocument>().Remove(document);
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ удален");
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка удаления документа: {ex.Message}");
            }
        }
    }
}
