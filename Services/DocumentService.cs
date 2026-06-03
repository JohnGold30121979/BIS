using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Models;
using BIS.ERP.Data;

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
        public async Task<List<Document>> GetDocumentsAsync(Guid? infoBaseId = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== GetDocumentsAsync START ===");

                var query = _context.Set<Document>().AsQueryable();
                if (infoBaseId.HasValue)
                    query = query.Where(d => d.InfoBaseId == infoBaseId);

                var result = await query.OrderByDescending(d => d.Date).ToListAsync();

                System.Diagnostics.Debug.WriteLine($"GetDocumentsAsync: найдено {result.Count} документов");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDocumentsAsync ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw new Exception($"Ошибка получения документов: {ex.Message}");
            }
        }

        // Получение документа по ID
        public async Task<Document> GetDocumentByIdAsync(Guid id)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== GetDocumentByIdAsync: {id} ===");

                var document = await _context.Set<Document>().FirstOrDefaultAsync(d => d.Id == id);

                if (document == null)
                    System.Diagnostics.Debug.WriteLine($"Документ {id} не найден");
                else
                    System.Diagnostics.Debug.WriteLine($"Документ найден: {document.Number}");

                return document;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetDocumentByIdAsync ERROR: {ex.Message}");
                throw new Exception($"Ошибка получения документа: {ex.Message}");
            }
        }

        // Создание документа
        public async Task<Document> CreateDocumentAsync(Document document)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== CreateDocumentAsync START ===");
                System.Diagnostics.Debug.WriteLine($"Document Number: {document.Number}");
                System.Diagnostics.Debug.WriteLine($"Document Type: {document.DocumentType}");
                System.Diagnostics.Debug.WriteLine($"Document Amount: {document.Amount}");

                // Валидация
                if (document == null)
                    throw new Exception("Документ не может быть null");

                if (string.IsNullOrEmpty(document.Number))
                {
                    document.Number = DateTime.Now.ToString("yyyyMMddHHmmss");
                    System.Diagnostics.Debug.WriteLine($"Сгенерирован номер: {document.Number}");
                }

                if (document.Date == DateTime.MinValue)
                    document.Date = DateTime.UtcNow;

                if (string.IsNullOrEmpty(document.DocumentType))
                    document.DocumentType = "Operation";

                document.Id = Guid.NewGuid();
                document.CreatedAt = DateTime.UtcNow;
                document.IsPosted = false;

                System.Diagnostics.Debug.WriteLine($"Document Id: {document.Id}");

                // Проверка существования таблицы
                var tableExists = await CheckTableExistsAsync();
                if (!tableExists)
                {
                    throw new Exception("Таблица Documents не существует в базе данных");
                }

                System.Diagnostics.Debug.WriteLine($"Добавление документа в контекст...");
                await _context.Set<Document>().AddAsync(document);

                System.Diagnostics.Debug.WriteLine($"Сохранение изменений...");
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ успешно создан!");
                return document;
            }
            catch (DbUpdateException ex)
            {
                System.Diagnostics.Debug.WriteLine($"DbUpdateException: {ex.Message}");
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                System.Diagnostics.Debug.WriteLine($"Inner exception: {innerMessage}");
                throw new Exception($"Ошибка БД при сохранении документа: {innerMessage}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateDocumentAsync ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw new Exception($"Ошибка создания документа: {ex.Message}");
            }
        }

        // Обновление документа
        public async Task<Document> UpdateDocumentAsync(Document document)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== UpdateDocumentAsync: {document.Id} ===");

                document.UpdatedAt = DateTime.UtcNow;
                _context.Set<Document>().Update(document);
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

        // Проведение документа
        public async Task<bool> PostDocumentAsync(Guid documentId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== PostDocumentAsync: {documentId} ===");

                var document = await GetDocumentByIdAsync(documentId);
                if (document == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Документ {documentId} не найден");
                    return false;
                }

                document.IsPosted = true;
                document.UpdatedAt = DateTime.UtcNow;

                // Создаем движения
                var movement = new DocumentMovement
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    Debit = document.Amount,
                    Credit = 0,
                    Amount = document.Amount,
                    MovementDate = document.Date
                };

                await _context.Set<DocumentMovement>().AddAsync(movement);
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ проведен");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PostDocumentAsync ERROR: {ex.Message}");
                throw new Exception($"Ошибка проведения документа: {ex.Message}");
            }
        }

        // Удаление документа
        public async Task<bool> DeleteDocumentAsync(Guid documentId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"=== DeleteDocumentAsync: {documentId} ===");

                var document = await GetDocumentByIdAsync(documentId);
                if (document == null) return false;

                document.IsDeleted = true;
                await _context.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"Документ помечен как удаленный");
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка удаления документа: {ex.Message}");
            }
        }

        // Проверка существования таблицы Documents
        private async Task<bool> CheckTableExistsAsync()
        {
            try
            {
                var sql = @"SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_name = 'Documents'
                )";

                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql;

                await _context.Database.OpenConnectionAsync();
                var result = await command.ExecuteScalarAsync();
                await _context.Database.CloseConnectionAsync();

                var exists = result != null && (bool)result;
                System.Diagnostics.Debug.WriteLine($"Таблица Documents существует: {exists}");
                return exists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckTableExistsAsync ERROR: {ex.Message}");
                return false;
            }
        }
    }
}