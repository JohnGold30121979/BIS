using BIS.ERP.Models;
using System.Text.Json;

namespace BIS.ERP.Services;

public partial class MetadataService
{
    private async Task ProcessAdvanceReportAsync(
        MetadataObject document,
        Dictionary<string, object> recordData,
        Guid recordId,
        decimal amount)
    {
        var documentNumber = NormalizeLegacyDocumentNumber(GetStringValue(recordData, "doc_number", "Номер"));
        var postingDate = GetDateValue(recordData, "doc_date", "Дата") ?? DateTime.Today;
        var amountCurrency = GetDecimalValue(recordData, "amount_currency", "Сумма в валюте");
        var exchangeRate = GetDecimalValue(recordData, "exchange_rate", "Курс");
        var currencyId = GetStringValue(recordData, "currency_id", "Валюта");
        var organizationId = GetNullableGuid(recordData, "organization_id", "Организация");
        var employeeId = GetNullableGuid(recordData, "employee_id", "Сотрудник");
        var linePostings = await ResolveAdvanceExpensePostingLinesAsync(recordData);

        if (linePostings.Count > 0)
        {
            var total = linePostings.Sum(line => line.Amount);
            if (total <= 0)
                throw new InvalidOperationException("Для авансовых платежей сумма проведения должна быть больше нуля.");

            await EnsureDocumentFieldValueAsync(document.TableName, recordId, recordData, "amount", total);
            await EnsureDocumentFieldValueAsync(document.TableName, recordId, recordData, "accepted_amount", total);

            var baseDescription = BuildFinanceDocumentDescription("Авансовые платежи", recordData);
            foreach (var line in linePostings)
            {
                var descriptionParts = new[] { baseDescription, line.PairName, line.Description }
                    .Where(part => !string.IsNullOrWhiteSpace(part));
                await CreatePosting(
                    documentNumber,
                    line.PostingDate ?? postingDate,
                    line.ExpenseAccountCode,
                    line.CreditAccountCode,
                    line.Amount,
                    string.Join("; ", descriptionParts),
                    "Авансовые платежи",
                    line.AmountCurrency,
                    line.CurrencyId ?? currencyId,
                    organizationId,
                    employeeId);
            }

            await UpdateDocumentPostedStatus(document.TableName, recordId);
            return;
        }

        var acceptedAmount = GetDecimalValue(recordData, "accepted_amount", "Принято к учету");
        var postingAmount = acceptedAmount > 0 ? acceptedAmount : amount;

        if (postingAmount <= 0 && amountCurrency > 0 && exchangeRate > 0)
            postingAmount = Math.Round(amountCurrency * exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (amountCurrency <= 0 && postingAmount > 0 && exchangeRate > 0)
            amountCurrency = Math.Round(postingAmount / exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (postingAmount <= 0)
            throw new InvalidOperationException("Для авансовых платежей сумма проведения должна быть больше нуля.");

        var (debitAccount, creditAccount) = await ResolveAdvanceReportAccountsAsync(recordData);
        if (string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount))
        {
            throw new InvalidOperationException(
                "Для авансовых платежей укажите счета или заполните строки затрат с парой счетов и счетом расхода.");
        }

        var description = BuildFinanceDocumentDescription("Авансовые платежи", recordData);

        await EnsureDocumentFieldValueAsync(document.TableName, recordId, recordData, "amount", postingAmount);
        await CreatePosting(
            documentNumber,
            postingDate,
            debitAccount,
            creditAccount,
            postingAmount,
            description,
            "Авансовые платежи",
            amountCurrency,
            currencyId,
            organizationId,
            employeeId);
        await UpdateDocumentPostedStatus(document.TableName, recordId);
    }

    private async Task ProcessPayrollStatementAsync(
        MetadataObject document,
        Dictionary<string, object> recordData,
        Guid recordId,
        decimal amount)
    {
        var documentNumber = NormalizeLegacyDocumentNumber(GetStringValue(recordData, "doc_number", "Номер"));
        var postingDate = GetDateValue(recordData, "doc_date", "Дата") ?? DateTime.Today;
        var payableAmount = GetDecimalValue(recordData, "payable_amount", "К выплате");
        var postingAmount = payableAmount > 0 ? payableAmount : amount;
        var amountCurrency = GetDecimalValue(recordData, "amount_currency", "Сумма в валюте");
        var exchangeRate = GetDecimalValue(recordData, "exchange_rate", "Курс");

        if (postingAmount <= 0 && amountCurrency > 0 && exchangeRate > 0)
            postingAmount = Math.Round(amountCurrency * exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (amountCurrency <= 0 && postingAmount > 0 && exchangeRate > 0)
            amountCurrency = Math.Round(postingAmount / exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (postingAmount <= 0)
            throw new InvalidOperationException("Для платежной ведомости сумма к выплате должна быть больше нуля.");

        var debitAccount = await ResolveAccountCodeFromRecordAsync(recordData, "debit_account", "Счет дебета");
        var creditAccount = await ResolveAccountCodeFromRecordAsync(recordData, "payment_account", "Счет выплаты");
        if (string.IsNullOrWhiteSpace(creditAccount))
            creditAccount = await ResolveAccountCodeFromRecordAsync(recordData, "credit_account", "Счет кредита");

        if (string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount))
            throw new InvalidOperationException("Для платежной ведомости укажите счет дебета и счет выплаты.");

        var description = BuildFinanceDocumentDescription("Платежная ведомость", recordData);
        var currencyId = GetStringValue(recordData, "currency_id", "Валюта");

        await EnsureDocumentFieldValueAsync(document.TableName, recordId, recordData, "amount", postingAmount);
        await CreatePosting(
            documentNumber,
            postingDate,
            debitAccount,
            creditAccount,
            postingAmount,
            description,
            "Платежная ведомость",
            amountCurrency,
            currencyId);
        await UpdateDocumentPostedStatus(document.TableName, recordId);
    }

    private async Task ProcessExchangeRateDifferenceDocumentAsync(
        MetadataObject document,
        Dictionary<string, object> recordData,
        Guid recordId)
    {
        var periodStart = GetDateValue(recordData, "period_start_date", "Дата начала периода");
        var periodEnd = GetDateValue(recordData, "period_end_date", "Дата окончания периода", "calculation_date", "Дата расчета", "doc_date", "Дата")
            ?? DateTime.Today;
        var result = await new ExchangeRateDifferenceService(_context).CalculateForDateAsync(periodEnd, periodStart, replaceExistingCalculation: true);

        await UpdateRecordFieldAsync(document.TableName, recordId, "processed_balances", result.ProcessedBalances);
        await UpdateRecordFieldAsync(document.TableName, recordId, "created_postings", result.CreatedPostings);
        await UpdateRecordFieldAsync(document.TableName, recordId, "gain_amount", result.GainAmount);
        await UpdateRecordFieldAsync(document.TableName, recordId, "loss_amount", result.LossAmount);
        await UpdateDocumentPostedStatus(document.TableName, recordId);
    }

    private async Task<List<AdvanceExpensePostingLine>> ResolveAdvanceExpensePostingLinesAsync(
        Dictionary<string, object> recordData)
    {
        var json = GetStringValue(recordData, "expense_lines", "Строки затрат");
        if (string.IsNullOrWhiteSpace(json))
            return new List<AdvanceExpensePostingLine>();

        List<AdvanceExpenseLinePayload>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<List<AdvanceExpenseLinePayload>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return new List<AdvanceExpensePostingLine>();
        }

        if (payload == null || payload.Count == 0)
            return new List<AdvanceExpensePostingLine>();

        var advancePaymentRows = await GetAdvancePaymentPairsAsync();
        var result = new List<AdvanceExpensePostingLine>();
        for (var index = 0; index < payload.Count; index++)
        {
            var line = payload[index];
            var lineAmount = line.Amount;
            if (lineAmount <= 0 && line.AmountCurrency > 0 && line.ExchangeRate > 0)
                lineAmount = Math.Round(line.AmountCurrency * line.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            if (lineAmount <= 0)
                throw new InvalidOperationException($"В строке затрат {index + 1} сумма должна быть больше нуля.");

            var expenseAccount = await ResolveAccountCodeValueAsync(line.ExpenseAccount);
            if (string.IsNullOrWhiteSpace(expenseAccount))
                expenseAccount = await ResolveAccountCodeValueAsync(line.DebitAccount);

            var creditAccount = await ResolveAccountCodeValueAsync(line.CreditAccount);
            var pairRow = advancePaymentRows.FirstOrDefault(row =>
                line.PairId != Guid.Empty &&
                string.Equals(GetDictionaryValue(row, "Id"), line.PairId.ToString(), StringComparison.OrdinalIgnoreCase));
            if (pairRow != null)
            {
                if (string.IsNullOrWhiteSpace(creditAccount))
                    creditAccount = await ResolveAccountCodeValueAsync(GetDictionaryValue(pairRow, "credit_account", "Кредит"));
                if (string.IsNullOrWhiteSpace(creditAccount))
                    creditAccount = await ResolveAccountCodeValueAsync(GetDictionaryValue(pairRow, "debit_account", "Дебет"));
            }

            if (string.IsNullOrWhiteSpace(expenseAccount) || string.IsNullOrWhiteSpace(creditAccount))
                throw new InvalidOperationException($"В строке затрат {index + 1} не заполнены счет расхода или расчетный счет.");

            var pairName = !string.IsNullOrWhiteSpace(line.PairName)
                ? line.PairName
                : pairRow == null
                    ? string.Empty
                    : GetDictionaryValue(pairRow, "name", "Вид расчета", "Наименование");

            result.Add(new AdvanceExpensePostingLine(
                expenseAccount,
                creditAccount,
                lineAmount,
                pairName,
                line.Description,
                line.LineDate?.Date,
                line.AmountCurrency,
                line.CurrencyId == Guid.Empty ? null : line.CurrencyId.ToString(),
                line.ExchangeRate));
        }

        return result;
    }

    private async Task<(string DebitAccount, string CreditAccount)> ResolveAdvanceReportAccountsAsync(
        Dictionary<string, object> recordData)
    {
        var debitAccount = await ResolveAccountCodeFromRecordAsync(recordData, "debit_account", "Счет дебета");
        var creditAccount = await ResolveAccountCodeFromRecordAsync(recordData, "credit_account", "Счет кредита");

        if (!string.IsNullOrWhiteSpace(debitAccount) && !string.IsNullOrWhiteSpace(creditAccount))
            return (debitAccount, creditAccount);

        var advancePaymentIdText = GetStringValue(recordData, "advance_payment_id", "Вид авансового расчета");
        if (string.IsNullOrWhiteSpace(advancePaymentIdText))
            return (debitAccount, creditAccount);

        var advancePaymentRows = await GetAdvancePaymentPairsAsync();
        var selectedRow = advancePaymentRows.FirstOrDefault(row =>
            string.Equals(GetDictionaryValue(row, "Id"), advancePaymentIdText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetDictionaryValue(row, "code"), advancePaymentIdText, StringComparison.OrdinalIgnoreCase));
        if (selectedRow == null)
            return (debitAccount, creditAccount);

        if (string.IsNullOrWhiteSpace(debitAccount))
            debitAccount = await ResolveAccountCodeValueAsync(GetDictionaryValue(selectedRow, "debit_account", "Дебет"));
        if (string.IsNullOrWhiteSpace(creditAccount))
            creditAccount = await ResolveAccountCodeValueAsync(GetDictionaryValue(selectedRow, "credit_account", "Кредит"));

        return (debitAccount, creditAccount);
    }

    private async Task<string> ResolveAccountCodeFromRecordAsync(
        Dictionary<string, object> recordData,
        params string[] keys)
    {
        return await ResolveAccountCodeValueAsync(GetStringValue(recordData, keys));
    }

    private static string BuildFinanceDocumentDescription(
        string documentType,
        Dictionary<string, object> recordData)
    {
        var basis = GetStringValue(recordData, "basis", "Основание");
        var note = GetStringValue(recordData, "description", "Примечание");
        if (!string.IsNullOrWhiteSpace(basis) && !string.IsNullOrWhiteSpace(note))
            return $"{documentType}: {basis}; {note}";

        if (!string.IsNullOrWhiteSpace(basis))
            return $"{documentType}: {basis}";

        if (!string.IsNullOrWhiteSpace(note))
            return $"{documentType}: {note}";

        return documentType;
    }

    private static string GetDictionaryValue(
        IReadOnlyDictionary<string, object> row,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private sealed record AdvanceExpensePostingLine(
        string ExpenseAccountCode,
        string CreditAccountCode,
        decimal Amount,
        string PairName,
        string Description,
        DateTime? PostingDate,
        decimal AmountCurrency,
        string? CurrencyId,
        decimal ExchangeRate);

    private sealed class AdvanceExpenseLinePayload
    {
        public DateTime? LineDate { get; set; }
        public Guid PairId { get; set; }
        public string PairName { get; set; } = string.Empty;
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public string ExpenseAccount { get; set; } = string.Empty;
        public Guid CurrencyId { get; set; }
        public decimal AmountCurrency { get; set; }
        public decimal ExchangeRate { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }
}