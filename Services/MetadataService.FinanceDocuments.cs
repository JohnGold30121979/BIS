using BIS.ERP.Models;

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
        var acceptedAmount = GetDecimalValue(recordData, "accepted_amount", "Принято к учету");
        var postingAmount = acceptedAmount > 0 ? acceptedAmount : amount;
        var amountCurrency = GetDecimalValue(recordData, "amount_currency", "Сумма в валюте");
        var exchangeRate = GetDecimalValue(recordData, "exchange_rate", "Курс");

        if (postingAmount <= 0 && amountCurrency > 0 && exchangeRate > 0)
            postingAmount = Math.Round(amountCurrency * exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (amountCurrency <= 0 && postingAmount > 0 && exchangeRate > 0)
            amountCurrency = Math.Round(postingAmount / exchangeRate, 2, MidpointRounding.AwayFromZero);
        if (postingAmount <= 0)
            throw new InvalidOperationException("Для авансового отчета сумма проведения должна быть больше нуля.");

        var (debitAccount, creditAccount) = await ResolveAdvanceReportAccountsAsync(recordData);
        if (string.IsNullOrWhiteSpace(debitAccount) || string.IsNullOrWhiteSpace(creditAccount))
        {
            throw new InvalidOperationException(
                "Для авансового отчета укажите счета дебета и кредита или выберите вид авансового расчета с заполненной парой счетов.");
        }

        var description = BuildFinanceDocumentDescription("Авансовый отчет", recordData);
        var currencyId = GetStringValue(recordData, "currency_id", "Валюта");

        await EnsureDocumentFieldValueAsync(document.TableName, recordId, recordData, "amount", postingAmount);
        await CreatePosting(
            documentNumber,
            postingDate,
            debitAccount,
            creditAccount,
            postingAmount,
            description,
            "Авансовый отчет",
            amountCurrency,
            currencyId);
        await UpdateDocumentPostedStatus(document.TableName, recordId);
    }

    private async Task ProcessPowerOfAttorneyAsync(
        MetadataObject document,
        Dictionary<string, object> recordData,
        Guid recordId)
    {
        if (!TryGetGuid(recordData, out _, "representative_id", "Представитель"))
            throw new InvalidOperationException("Для доверенности выберите представителя.");

        var validUntil = GetDateValue(recordData, "valid_until", "Срок действия");
        if (!validUntil.HasValue)
            throw new InvalidOperationException("Для доверенности укажите срок действия.");

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
}
