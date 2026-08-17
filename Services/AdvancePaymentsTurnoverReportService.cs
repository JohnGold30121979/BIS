using BIS.ERP.Data;
using BIS.ERP.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BIS.ERP.Services;

public sealed class AdvancePaymentsTurnoverReportService
{
    private readonly AppDbContext _context;

    public AdvancePaymentsTurnoverReportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> ExportExcelAsync(DateTime startDate, DateTime endDate)
    {
        startDate = startDate.Date;
        endDate = endDate.Date;
        if (endDate < startDate)
            throw new InvalidOperationException("Дата окончания отчета не может быть меньше даты начала.");

        var metadataService = new MetadataService(_context);
        var accountLookup = await LoadAccountLookupAsync(metadataService);
        var pairs = await LoadAccountPairsAsync(metadataService, accountLookup);
        if (pairs.Count == 0)
            throw new InvalidOperationException("В справочнике пар счетов нет активных строк с заполненными счетами.");

        var employeeFallback = await LoadAdvanceDocumentEmployeeFallbackAsync(metadataService);
        var postings = await new PostingService(_context).GetAllPostingsAsync(null, endDate);
        var lines = BuildReportLines(postings, pairs, employeeFallback, accountLookup, startDate, endDate);
        if (lines.Count == 0)
            throw new InvalidOperationException("За выбранный период нет проводок по парам счетов авансовых платежей.");

        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"BIS_AdvancePaymentsTurnover_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        BuildWorkbook(filePath, lines, accountLookup, startDate, endDate);
        return filePath;
    }

    private async Task<AccountLookup> LoadAccountLookupAsync(MetadataService metadataService)
    {
        var catalog = await _context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name.StartsWith("План счетов"));
        if (catalog == null)
            return new AccountLookup();

        var rows = await metadataService.GetCatalogDataAsync(catalog.Id);
        var lookup = new AccountLookup();
        foreach (var row in rows)
        {
            var id = GetString(row, "Id");
            var code = GetString(row, "Код", "code");
            var name = GetString(row, "Наименование", "name");
            if (string.IsNullOrWhiteSpace(code))
                continue;

            lookup.AccountNames[code] = name;
            lookup.ValueToCode[code] = code;
            if (!string.IsNullOrWhiteSpace(id))
                lookup.ValueToCode[id] = code;
            if (!string.IsNullOrWhiteSpace(name))
                lookup.ValueToCode[$"{code} - {name}"] = code;
        }

        return lookup;
    }

    private async Task<List<AccountPair>> LoadAccountPairsAsync(MetadataService metadataService, AccountLookup accountLookup)
    {
        var rows = await metadataService.GetAdvancePaymentPairsAsync();
        return rows
            .Where(IsActiveRow)
            .Select(row =>
            {
                var debit = ResolveAccountCode(GetString(row, "debit_account", "Дебет"), accountLookup);
                var credit = ResolveAccountCode(GetString(row, "credit_account", "Кредит"), accountLookup);
                var name = GetString(row, "name", "Вид расчета", "Наименование");
                return string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit)
                    ? null
                    : new AccountPair(debit, credit, name);
            })
            .Where(pair => pair != null)
            .Cast<AccountPair>()
            .DistinctBy(pair => pair.Key)
            .OrderBy(pair => pair.DebitAccount, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.CreditAccount, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<string, string>> LoadAdvanceDocumentEmployeeFallbackAsync(MetadataService metadataService)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var employeeMap = await LoadReferenceMapAsync(metadataService, "Сотрудники (Списочный состав)");
        var document = await _context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.ObjectType == "Document" &&
                (item.Name == "Авансовые платежи" || item.Name == "Авансовый отчет"));
        if (document == null)
            return result;

        var rows = await metadataService.GetCatalogDataAsync(document.Id);
        foreach (var row in rows)
        {
            var number = MetadataService.NormalizeLegacyDocumentNumber(GetString(row, "doc_number", "Номер"));
            var date = GetDate(row, "doc_date", "Дата");
            var employeeValue = GetString(row, "employee_id", "Сотрудник");
            var employee = ResolveReference(employeeValue, employeeMap);
            if (string.IsNullOrWhiteSpace(number) || date == null || string.IsNullOrWhiteSpace(employee))
                continue;

            result[BuildDocumentKey("Авансовые платежи", number, date.Value)] = employee;
            result[BuildDocumentKey("Авансовый отчет", number, date.Value)] = employee;
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> LoadReferenceMapAsync(MetadataService metadataService, string catalogName)
    {
        var catalog = await _context.MetadataObjects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == catalogName);
        if (catalog == null)
            return new Dictionary<Guid, string>();

        var rows = await metadataService.GetCatalogDataAsync(catalog.Id);
        var displayField = new MetadataField();
        return rows
            .Where(row => Guid.TryParse(GetString(row, "Id"), out _))
            .ToDictionary(
                row => Guid.Parse(GetString(row, "Id")),
                row => ReferenceDisplayHelper.BuildDisplayValue(row, displayField));
    }

    private List<AdvanceTurnoverLine> BuildReportLines(
        IReadOnlyCollection<PostingViewModel> postings,
        IReadOnlyCollection<AccountPair> pairs,
        IReadOnlyDictionary<string, string> employeeFallback,
        AccountLookup accountLookup,
        DateTime startDate,
        DateTime endDate)
    {
        var result = new List<AdvanceTurnoverLine>();
        foreach (var posting in postings.Where(item => item.IsActive && item.Amount != 0m && item.Date.Date <= endDate))
        {
            var debit = ResolveAccountCode(posting.DebitAccount, accountLookup);
            var credit = ResolveAccountCode(posting.CreditAccount, accountLookup);
            if (string.IsNullOrWhiteSpace(debit) || string.IsNullOrWhiteSpace(credit))
                continue;

            foreach (var pair in pairs)
            {
                AddLineIfPairAccount(result, pair, posting, debit, credit, isDebitSide: true, employeeFallback);
                AddLineIfPairAccount(result, pair, posting, credit, debit, isDebitSide: false, employeeFallback);
            }
        }

        return result;
    }

    private static void AddLineIfPairAccount(
        List<AdvanceTurnoverLine> result,
        AccountPair pair,
        PostingViewModel posting,
        string account,
        string correspondentAccount,
        bool isDebitSide,
        IReadOnlyDictionary<string, string> employeeFallback)
    {
        if (!pair.Contains(account))
            return;

        var employee = posting.Employee;
        if (string.IsNullOrWhiteSpace(employee) &&
            employeeFallback.TryGetValue(BuildDocumentKey(posting.DocumentType, posting.DocumentNumber, posting.Date), out var fallback))
        {
            employee = fallback;
        }

        if (string.IsNullOrWhiteSpace(employee))
            employee = "Без сотрудника";

        result.Add(new AdvanceTurnoverLine(
            pair,
            posting.Date.Date,
            MetadataService.NormalizeLegacyDocumentNumber(posting.DocumentNumber),
            account,
            correspondentAccount,
            isDebitSide ? posting.Amount : 0m,
            isDebitSide ? 0m : posting.Amount,
            posting.Note,
            string.IsNullOrWhiteSpace(posting.ModuleName) ? "Финансы" : posting.ModuleName,
            employee));
    }

    private static void BuildWorkbook(
        string filePath,
        IReadOnlyCollection<AdvanceTurnoverLine> lines,
        AccountLookup accountLookup,
        DateTime startDate,
        DateTime endDate)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Оборотная ведомость");
        ConfigureSheet(sheet);

        var row = 1;
        foreach (var pairGroup in lines
            .GroupBy(line => line.Pair.Key)
            .OrderBy(group => group.First().Pair.DebitAccount, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.First().Pair.CreditAccount, StringComparer.OrdinalIgnoreCase))
        {
            row = WritePairSection(sheet, row, pairGroup.First().Pair, pairGroup.ToList(), accountLookup, startDate, endDate);
            row += 2;
        }

        sheet.Columns().AdjustToContents(7, 45);
        sheet.Column(7).Width = Math.Max(sheet.Column(7).Width, 34);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        workbook.SaveAs(filePath);
    }

    private static int WritePairSection(
        IXLWorksheet sheet,
        int row,
        AccountPair pair,
        IReadOnlyCollection<AdvanceTurnoverLine> pairLines,
        AccountLookup accountLookup,
        DateTime startDate,
        DateTime endDate)
    {
        sheet.Cell(row, 8).Value = "ОсОО \"Comtec-Soft\"";
        sheet.Cell(row, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        row++;

        MergeTitle(sheet, row++, "Оборотная ведомость синтетического и аналитического учета");
        var pairTitle = $"по паре счетов {pair.DebitAccount} - {pair.CreditAccount}";
        if (!string.IsNullOrWhiteSpace(pair.Name))
            pairTitle += $" ({pair.Name.ToUpperInvariant()})";
        pairTitle += $" с {startDate:dd.MM.yyyy} по {endDate:dd.MM.yyyy}";
        MergeTitle(sheet, row++, pairTitle);

        sheet.Cell(row, 6).Value = DateTime.Today.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
        sheet.Cell(row, 8).Value = "Лист 1";
        sheet.Range(row, 1, row, 8).Style.Font.Italic = true;
        row++;

        WriteHeader(sheet, row);
        row += 2;

        var totalOpening = 0m;
        var totalDebit = 0m;
        var totalCredit = 0m;

        foreach (var employeeGroup in pairLines
            .GroupBy(line => line.AnalyticDisplay)
            .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var opening = employeeGroup
                .Where(line => line.Date < startDate)
                .Sum(line => line.DebitTurnover - line.CreditTurnover);
            var currentLines = employeeGroup
                .Where(line => line.Date >= startDate && line.Date <= endDate)
                .OrderBy(line => line.Date)
                .ThenBy(line => line.DocumentNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(line => line.Account, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var debit = currentLines.Sum(line => line.DebitTurnover);
            var credit = currentLines.Sum(line => line.CreditTurnover);
            var ending = opening + debit - credit;
            if (opening == 0m && debit == 0m && credit == 0m && ending == 0m)
                continue;

            totalOpening += opening;
            totalDebit += debit;
            totalCredit += credit;

            sheet.Cell(row, 2).Value = employeeGroup.Key;
            sheet.Range(row, 1, row, 8).Style.Font.Bold = true;
            row++;

            row = WriteBalanceRow(sheet, row, $"САЛЬДО НА {startDate:dd.MM.yyyy}", opening);
            foreach (var line in currentLines)
                row = WriteDetailRow(sheet, row, line);

            row = WriteTurnoverRow(sheet, row, $"ИТОГО ПО {BuildEmployeeTotalCaption(employeeGroup.Key)}", debit, credit);
            row = WriteBalanceRow(sheet, row, "САЛЬДО НА НАЧАЛО", opening);
            row = WriteTurnoverRow(sheet, row, "ОБОРОТЫ", debit, credit);
            row = WriteBalanceRow(sheet, row, "САЛЬДО НА КОНЕЦ", ending);
            row++;
        }

        var totalEnding = totalOpening + totalDebit - totalCredit;
        row++;
        sheet.Cell(row, 2).Value = "ИТОГ";
        sheet.Range(row, 1, row, 8).Style.Font.Bold = true;
        row++;
        row = WriteBalanceRow(sheet, row, "САЛЬДО НА НАЧАЛО", totalOpening);
        row = WriteTurnoverRow(sheet, row, "ОБОРОТЫ", totalDebit, totalCredit);
        row = WriteBalanceRow(sheet, row, "САЛЬДО НА КОНЕЦ", totalEnding);
        row++;

        row = WriteAccountSummary(sheet, row, pairLines.Where(line => line.Date >= startDate && line.Date <= endDate).ToList(), accountLookup);
        return row;
    }

    private static void ConfigureSheet(IXLWorksheet sheet)
    {
        sheet.Style.Font.FontName = "Times New Roman";
        sheet.Style.Font.FontSize = 10;
        sheet.Column(1).Width = 11;
        sheet.Column(2).Width = 11;
        sheet.Column(3).Width = 12;
        sheet.Column(4).Width = 12;
        sheet.Column(5).Width = 14;
        sheet.Column(6).Width = 14;
        sheet.Column(7).Width = 38;
        sheet.Column(8).Width = 10;
    }

    private static void MergeTitle(IXLWorksheet sheet, int row, string text)
    {
        sheet.Range(row, 1, row, 8).Merge();
        sheet.Cell(row, 1).Value = text;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        sheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void WriteHeader(IXLWorksheet sheet, int row)
    {
        sheet.Cell(row, 1).Value = "Дата";
        sheet.Cell(row, 2).Value = "Документ";
        sheet.Cell(row, 3).Value = "Счет";
        sheet.Cell(row, 4).Value = "Корр. счет";
        sheet.Range(row, 5, row, 6).Merge();
        sheet.Cell(row, 5).Value = "ОБОРОТЫ - сом";
        sheet.Cell(row, 7).Value = "Содержание";
        sheet.Cell(row, 8).Value = "Модуль";
        sheet.Cell(row + 1, 5).Value = "Дебет";
        sheet.Cell(row + 1, 6).Value = "Кредит";

        sheet.Range(row, 1, row + 1, 8).Style.Font.Bold = true;
        sheet.Range(row, 1, row + 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(row, 1, row + 1, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Range(row, 1, row + 1, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(row, 1, row + 1, 8).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static int WriteDetailRow(IXLWorksheet sheet, int row, AdvanceTurnoverLine line)
    {
        sheet.Cell(row, 1).Value = line.Date.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
        sheet.Cell(row, 2).Value = line.DocumentNumber;
        sheet.Cell(row, 3).Value = line.Account;
        sheet.Cell(row, 4).Value = line.CorrespondentAccount;
        SetAmount(sheet.Cell(row, 5), line.DebitTurnover);
        SetAmount(sheet.Cell(row, 6), line.CreditTurnover);
        sheet.Cell(row, 7).Value = line.Description;
        sheet.Cell(row, 8).Value = line.ModuleName;
        ApplyTableRowStyle(sheet.Range(row, 1, row, 8));
        return row + 1;
    }

    private static int WriteTurnoverRow(IXLWorksheet sheet, int row, string caption, decimal debit, decimal credit)
    {
        sheet.Range(row, 1, row, 4).Merge();
        sheet.Cell(row, 1).Value = caption;
        SetAmount(sheet.Cell(row, 5), debit);
        SetAmount(sheet.Cell(row, 6), credit);
        sheet.Range(row, 1, row, 8).Style.Font.Bold = true;
        ApplyTableRowStyle(sheet.Range(row, 1, row, 8));
        return row + 1;
    }

    private static int WriteBalanceRow(IXLWorksheet sheet, int row, string caption, decimal balance)
    {
        sheet.Range(row, 1, row, 4).Merge();
        sheet.Cell(row, 1).Value = caption;
        if (balance >= 0)
            SetAmount(sheet.Cell(row, 5), balance);
        else
            SetAmount(sheet.Cell(row, 6), Math.Abs(balance));
        sheet.Range(row, 1, row, 8).Style.Font.Bold = true;
        sheet.Range(row, 1, row, 8).Style.Font.Italic = caption.StartsWith("САЛЬДО", StringComparison.OrdinalIgnoreCase);
        ApplyTableRowStyle(sheet.Range(row, 1, row, 8));
        return row + 1;
    }

    private static int WriteAccountSummary(
        IXLWorksheet sheet,
        int row,
        IReadOnlyCollection<AdvanceTurnoverLine> currentLines,
        AccountLookup accountLookup)
    {
        var summary = currentLines
            .GroupBy(line => new { line.Account, line.CorrespondentAccount })
            .Select(group => new
            {
                group.Key.Account,
                group.Key.CorrespondentAccount,
                Debit = group.Sum(line => line.DebitTurnover),
                Credit = group.Sum(line => line.CreditTurnover)
            })
            .Where(item => item.Debit != 0m || item.Credit != 0m)
            .OrderBy(item => item.Account, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CorrespondentAccount, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in summary)
        {
            sheet.Cell(row, 3).Value = item.Account;
            sheet.Cell(row, 4).Value = item.CorrespondentAccount;
            SetAmount(sheet.Cell(row, 5), item.Debit);
            SetAmount(sheet.Cell(row, 6), item.Credit);
            sheet.Cell(row, 7).Value = accountLookup.AccountNames.TryGetValue(item.CorrespondentAccount, out var name)
                ? name
                : string.Empty;
            ApplyTableRowStyle(sheet.Range(row, 1, row, 8));
            row++;
        }

        return row;
    }

    private static void ApplyTableRowStyle(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetAmount(IXLCell cell, decimal value)
    {
        if (value == 0m)
            return;

        cell.Value = value;
        cell.Style.NumberFormat.Format = "#,##0.00";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
    }

    private static bool IsActiveRow(IReadOnlyDictionary<string, object> row)
    {
        var value = GetString(row, "is_active", "Активен");
        return string.IsNullOrWhiteSpace(value) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("да", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAccountCode(string? value, AccountLookup lookup)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (lookup.ValueToCode.TryGetValue(trimmed, out var code))
            return code;

        var match = Regex.Match(trimmed, @"\d{3,}");
        return match.Success ? match.Value : trimmed;
    }

    private static string ResolveReference(string value, IReadOnlyDictionary<Guid, string> map)
    {
        return Guid.TryParse(value, out var id) && map.TryGetValue(id, out var displayName)
            ? displayName
            : value;
    }

    private static string BuildDocumentKey(string? documentType, string? documentNumber, DateTime date)
    {
        return $"{documentType?.Trim()}|{MetadataService.NormalizeLegacyDocumentNumber(documentNumber)}|{date.Date:yyyy-MM-dd}";
    }

    private static string BuildEmployeeTotalCaption(string employee)
    {
        var separatorIndex = employee.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex > 0)
            return $"ТАБ.Н {employee[..separatorIndex]}";
        return "СОТРУДНИКУ";
    }

    private static string GetString(IReadOnlyDictionary<string, object> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                return value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static DateTime? GetDate(IReadOnlyDictionary<string, object> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                continue;

            if (value is DateTime date)
                return date.Date;
            if (DateTime.TryParse(value.ToString(), out var parsed))
                return parsed.Date;
        }

        return null;
    }

    private sealed class AccountLookup
    {
        public Dictionary<string, string> ValueToCode { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AccountNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record AccountPair(string DebitAccount, string CreditAccount, string Name)
    {
        public string Key => $"{DebitAccount}|{CreditAccount}";

        public bool Contains(string account)
        {
            return DebitAccount.Equals(account, StringComparison.OrdinalIgnoreCase) ||
                   CreditAccount.Equals(account, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record AdvanceTurnoverLine(
        AccountPair Pair,
        DateTime Date,
        string DocumentNumber,
        string Account,
        string CorrespondentAccount,
        decimal DebitTurnover,
        decimal CreditTurnover,
        string Description,
        string ModuleName,
        string AnalyticDisplay);
}
