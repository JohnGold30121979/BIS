using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class PostingService
    {
        private readonly AppDbContext _context;

        public PostingService(AppDbContext context)
        {
            _context = context;
        }

        // Вспомогательный метод для получения данных строки
        private Dictionary<string, object> GetRowData(DynamicDocumentRow row)
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

        // Получение всех проводок из динамических документов
        public async Task<List<PostingViewModel>> GetAllPostingsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var postings = new List<PostingViewModel>();

            // 1. Проводки из doc_postings
            try
            {
                var sql = @"
            SELECT 
                posting_date as Date,
                doc_number as DocumentNumber,
                COALESCE(document_type, 'Проводка') as DocumentType, 
                debit_account as DebitAccount,
                credit_account as CreditAccount,
                amount_kgs as Amount,
                0 as AmountCurrency,
                '' as Currency,
                description as Note,
                '' as Organization,
                '' as Employee,
                ""Id"" as Id
            FROM doc_postings
            WHERE is_active = true";

                if (startDate.HasValue)
                    sql += $"\n            AND DATE(posting_date) >= DATE('{startDate.Value:yyyy-MM-dd}')";
                if (endDate.HasValue)
                    sql += $"\n            AND DATE(posting_date) <= DATE('{endDate.Value:yyyy-MM-dd}')";

                var rows = await _context.Database.SqlQueryRaw<PostingSqlRow>(sql).ToListAsync();

                foreach (var row in rows)
                {
                    postings.Add(new PostingViewModel
                    {
                        Id = row.Id,
                        Date = row.Date,
                        DocumentNumber = row.DocumentNumber,
                        DocumentType = row.DocumentType,
                        DebitAccount = row.DebitAccount,
                        CreditAccount = row.CreditAccount,
                        Amount = row.Amount,
                        AmountCurrency = row.AmountCurrency,
                        Currency = row.Currency,
                        Note = row.Note,
                        Organization = row.Organization,
                        Employee = row.Employee,
                        Counterparty = "",
                        Site = "",
                        ResponsiblePerson = "",
                        DocumentId = null
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки из doc_postings: {ex.Message}");
            }

            // 2. Проводки из DynamicDocuments (DBF импорт)
            try
            {
                var dynamicDocs = await _context.DynamicDocuments
                    .Include(d => d.Rows)
                    .ToListAsync();

                foreach (var doc in dynamicDocs)
                {
                    if (startDate.HasValue && doc.Date < startDate.Value) continue;
                    if (endDate.HasValue && doc.Date > endDate.Value) continue;

                    foreach (var row in doc.Rows)
                    {
                        var rowData = GetRowData(row);

                        decimal amount = 0;
                        if (rowData.ContainsKey("SUMMA"))
                            amount = Convert.ToDecimal(rowData["SUMMA"] ?? 0);
                        else if (rowData.ContainsKey("Сумма"))
                            amount = Convert.ToDecimal(rowData["Сумма"] ?? 0);
                        else if (rowData.ContainsKey("AMOUNT"))
                            amount = Convert.ToDecimal(rowData["AMOUNT"] ?? 0);

                        decimal amountCurrency = 0;
                        if (rowData.ContainsKey("SUMMA_V"))
                            amountCurrency = Convert.ToDecimal(rowData["SUMMA_V"] ?? 0);
                        else if (rowData.ContainsKey("Сумма в валюте"))
                            amountCurrency = Convert.ToDecimal(rowData["Сумма в валюте"] ?? 0);

                        var debet = rowData.ContainsKey("DEBET") ? rowData["DEBET"]?.ToString() :
                                   (rowData.ContainsKey("Дебет") ? rowData["Дебет"]?.ToString() : "");
                        var credit = rowData.ContainsKey("KREDIT") ? rowData["KREDIT"]?.ToString() :
                                    (rowData.ContainsKey("Кредит") ? rowData["Кредит"]?.ToString() : "");

                        var note = rowData.ContainsKey("TEX") ? rowData["TEX"]?.ToString() :
                                  (rowData.ContainsKey("Примечание") ? rowData["Примечание"]?.ToString() : "");

                        var organization = rowData.ContainsKey("ORGANIZATION") ? rowData["ORGANIZATION"]?.ToString() :
                                          (rowData.ContainsKey("Организация") ? rowData["Организация"]?.ToString() : "");

                        var employee = rowData.ContainsKey("EMPLOYEE") ? rowData["EMPLOYEE"]?.ToString() :
                                      (rowData.ContainsKey("Сотрудник") ? rowData["Сотрудник"]?.ToString() : "");

                        if (!string.IsNullOrEmpty(debet) || !string.IsNullOrEmpty(credit))
                        {
                            postings.Add(new PostingViewModel
                            {
                                Id = Guid.NewGuid(),
                                Date = doc.Date,
                                DocumentNumber = doc.Number,
                                DocumentType = doc.DocumentType,
                                DebitAccount = debet ?? "",
                                CreditAccount = credit ?? "",
                                Amount = amount,
                                AmountCurrency = amountCurrency,
                                Note = note ?? "",
                                Organization = organization ?? "",
                                Employee = employee ?? "",
                                Currency = rowData.ContainsKey("Currency") ? rowData["Currency"]?.ToString() : "KGS",
                                Counterparty = "",
                                Site = "",
                                ResponsiblePerson = "",
                                DocumentId = doc.Id
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки из DynamicDocuments: {ex.Message}");
            }

            return postings.OrderByDescending(p => p.Date).ThenBy(p => p.DocumentNumber).ToList();
        }

        // Вспомогательный класс для SQL запроса
        private class PostingSqlRow
        {
            public DateTime Date { get; set; }
            public string DocumentNumber { get; set; }
            public string DocumentType { get; set; }
            public string DebitAccount { get; set; }
            public string CreditAccount { get; set; }
            public decimal Amount { get; set; }
            public decimal AmountCurrency { get; set; }
            public string Currency { get; set; }
            public string Note { get; set; }
            public string Organization { get; set; }
            public string Employee { get; set; }
            public Guid Id { get; set; }
        }

        // Получение оборотов по счету
        public async Task<DataTable> GetTurnoversByAccountAsync(string accountCode, DateTime startDate, DateTime endDate)
        {
            var postings = await GetAllPostingsAsync(startDate, endDate);

            var filteredPostings = postings.Where(p => p.DebitAccount == accountCode || p.CreditAccount == accountCode).ToList();

            var result = new DataTable();
            result.Columns.Add("Дата", typeof(DateTime));
            result.Columns.Add("Документ", typeof(string));
            result.Columns.Add("Корр.счет", typeof(string));
            result.Columns.Add("Дебет", typeof(decimal));
            result.Columns.Add("Кредит", typeof(decimal));
            result.Columns.Add("Содержание", typeof(string));

            decimal totalDebet = 0;
            decimal totalCredit = 0;

            foreach (var posting in filteredPostings)
            {
                if (posting.DebitAccount == accountCode)
                {
                    result.Rows.Add(
                        posting.Date,
                        $"{posting.DocumentType} №{posting.DocumentNumber}",
                        posting.CreditAccount,
                        posting.Amount,
                        0,
                        posting.Note
                    );
                    totalDebet += posting.Amount;
                }
                else if (posting.CreditAccount == accountCode)
                {
                    result.Rows.Add(
                        posting.Date,
                        $"{posting.DocumentType} №{posting.DocumentNumber}",
                        posting.DebitAccount,
                        0,
                        posting.Amount,
                        posting.Note
                    );
                    totalCredit += posting.Amount;
                }
            }

            result.Rows.Add(DateTime.Now, "ИТОГО:", "", totalDebet, totalCredit, "");
            return result;
        }

        // Получение сводных оборотов
        public async Task<DataTable> GetSummaryTurnoversAsync(DateTime startDate, DateTime endDate)
        {
            var postings = await GetAllPostingsAsync(startDate, endDate);

            var summary = postings
                .GroupBy(p => new { p.DebitAccount, p.CreditAccount })
                .Select(g => new
                {
                    DebitAccount = g.Key.DebitAccount,
                    CreditAccount = g.Key.CreditAccount,
                    Amount = g.Sum(p => p.Amount)
                })
                .OrderBy(g => g.DebitAccount)
                .ThenBy(g => g.CreditAccount)
                .ToList();

            var result = new DataTable();
            result.Columns.Add("Счет Дт", typeof(string));
            result.Columns.Add("Счет Кт", typeof(string));
            result.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in summary)
            {
                if (!string.IsNullOrEmpty(item.DebitAccount) && !string.IsNullOrEmpty(item.CreditAccount))
                {
                    result.Rows.Add(item.DebitAccount, item.CreditAccount, item.Amount);
                }
            }

            return result;
        }
    }
}