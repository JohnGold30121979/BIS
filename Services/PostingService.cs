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

        // Получение всех проводок из разных источников
        public async Task<List<PostingViewModel>> GetAllPostingsAsync(DateTime? startDate = null, DateTime? endDate = null)
        {
            var postings = new List<PostingViewModel>();

            // 1. Проводки из DynamicDocuments (импортированные DBF)
            var dynamicDocs = await _context.DynamicDocuments
                .Include(d => d.Rows)
                .ToListAsync();

            foreach (var doc in dynamicDocs)
            {
                foreach (var row in doc.Rows)
                {
                    var rowData = GetRowData(row);

                    // Проверяем наличие полей DEBET и KREDIT
                    var hasDebet = rowData.ContainsKey("DEBET") || rowData.ContainsKey("Дебет");
                    var hasCredit = rowData.ContainsKey("KREDIT") || rowData.ContainsKey("Кредит");

                    if (hasDebet && hasCredit)
                    {
                        // Пытаемся найти сумму в разных вариантах
                        decimal amount = 0;
                        if (rowData.ContainsKey("SUMMA"))
                            amount = Convert.ToDecimal(rowData["SUMMA"] ?? 0);
                        else if (rowData.ContainsKey("Сумма"))
                            amount = Convert.ToDecimal(rowData["Сумма"] ?? 0);
                        else if (rowData.ContainsKey("AMOUNT"))
                            amount = Convert.ToDecimal(rowData["AMOUNT"] ?? 0);

                        // Сумма в валюте
                        decimal amountCurrency = 0;
                        if (rowData.ContainsKey("SUMMA_V"))
                            amountCurrency = Convert.ToDecimal(rowData["SUMMA_V"] ?? 0);
                        else if (rowData.ContainsKey("Сумма в валюте"))
                            amountCurrency = Convert.ToDecimal(rowData["Сумма в валюте"] ?? 0);

                        // Счета
                        var debet = rowData.ContainsKey("DEBET") ? rowData["DEBET"]?.ToString() :
                                   (rowData.ContainsKey("Дебет") ? rowData["Дебет"]?.ToString() : "");
                        var credit = rowData.ContainsKey("KREDIT") ? rowData["KREDIT"]?.ToString() :
                                    (rowData.ContainsKey("Кредит") ? rowData["Кредит"]?.ToString() : "");

                        // Примечание
                        var note = rowData.ContainsKey("TEX") ? rowData["TEX"]?.ToString() :
                                  (rowData.ContainsKey("Примечание") ? rowData["Примечание"]?.ToString() : "");

                        // Организация
                        var organization = rowData.ContainsKey("KONTRAGENT") ? rowData["KONTRAGENT"]?.ToString() :
                                          (rowData.ContainsKey("Организация") ? rowData["Организация"]?.ToString() : "");

                        // Сотрудник
                        var employee = rowData.ContainsKey("SOTRUDNIK") ? rowData["SOTRUDNIK"]?.ToString() :
                                      (rowData.ContainsKey("Сотрудник") ? rowData["Сотрудник"]?.ToString() : "");

                        postings.Add(new PostingViewModel
                        {
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
                            Currency = rowData.ContainsKey("Валюта") ? rowData["Валюта"]?.ToString() : "KGS"
                        });
                    }
                }
            }

            // 2. Проводки из DynamicPostings (если есть таблица)
            // TODO: Добавить таблицу проводок когда появится

            // Фильтрация по дате
            if (startDate.HasValue)
                postings = postings.Where(p => p.Date >= startDate.Value).ToList();
            if (endDate.HasValue)
                postings = postings.Where(p => p.Date <= endDate.Value).ToList();

            return postings.OrderByDescending(p => p.Date).ThenBy(p => p.DocumentNumber).ToList();
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

            // Итоговая строка
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

        // Получение сальдо по счету на дату
        public async Task<decimal> GetBalanceAsync(string accountCode, DateTime date)
        {
            var postings = await GetAllPostingsAsync(null, date);

            decimal debitTotal = postings.Where(p => p.DebitAccount == accountCode).Sum(p => p.Amount);
            decimal creditTotal = postings.Where(p => p.CreditAccount == accountCode).Sum(p => p.Amount);

            return debitTotal - creditTotal;
        }

        // Получение всех счетов, участвующих в проводках
        public async Task<List<string>> GetAllAccountsAsync()
        {
            var postings = await GetAllPostingsAsync();

            var accounts = new HashSet<string>();
            foreach (var posting in postings)
            {
                if (!string.IsNullOrEmpty(posting.DebitAccount))
                    accounts.Add(posting.DebitAccount);
                if (!string.IsNullOrEmpty(posting.CreditAccount))
                    accounts.Add(posting.CreditAccount);
            }

            return accounts.OrderBy(a => a).ToList();
        }
    }
}