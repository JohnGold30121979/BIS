using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class AccountingSetupView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;
        private readonly AccountingPeriodService _periodService;
        private readonly ObservableCollection<OpeningBalanceEditorRow> _openings = new();
        private readonly ObservableCollection<ReportLineEditorRow> _reportLines = new();

        public AccountingSetupView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _metadataService = new MetadataService(context);
            _periodService = new AccountingPeriodService(context);
            OpeningGrid.ItemsSource = _openings;
            ReportLinesGrid.ItemsSource = _reportLines;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            await _periodService.EnsureSchemaAsync();
            await LoadOpeningsAsync();
            await LoadLinesAsync();
        }

        private async Task LoadOpeningsAsync()
        {
            var accountNames = (await LoadAccountsAsync())
                .Where(row => row.ContainsKey("Код"))
                .GroupBy(row => row["Код"]?.ToString() ?? string.Empty)
                .ToDictionary(group => group.Key, group => group.First().GetValueOrDefault("Наименование")?.ToString() ?? string.Empty);
            var records = await _context.AccountOpeningBalances.AsNoTracking()
                .OrderBy(item => item.BalanceDate).ThenBy(item => item.AccountCode).ToListAsync();
            _openings.Clear();
            foreach (var item in records)
                _openings.Add(new OpeningBalanceEditorRow
                {
                    Id = item.Id, BalanceDate = item.BalanceDate.Date, AccountCode = item.AccountCode,
                    AccountName = accountNames.GetValueOrDefault(item.AccountCode, item.AccountCode),
                    Debit = item.Debit, Credit = item.Credit,
                    IsSystemGenerated = item.IsSystemGenerated,
                    SourceDescription = item.IsSystemGenerated
                        ? "Автоперенос закрытия периода"
                        : "Ручной ввод"
                });
        }

        private async void OnAddOpeningClick(object sender, RoutedEventArgs e)
        {
            var accounts = await LoadAccountsAsync();
            var dialog = new AccountSelectionDialog(accounts) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;
            _openings.Add(new OpeningBalanceEditorRow
            {
                AccountCode = dialog.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty,
                AccountName = dialog.SelectedAccount.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty,
                BalanceDate = new DateTime(DateTime.Today.Year, 1, 1)
            });
        }

        private async void OnDeleteOpeningClick(object sender, RoutedEventArgs e)
        {
            if (OpeningGrid.SelectedItem is not OpeningBalanceEditorRow row)
                return;
            if (row.IsSystemGenerated)
            {
                MessageBox.Show(
                    "Системный остаток создается при закрытии периода. Для изменения откройте исходный период и закройте его заново.",
                    "Начальные остатки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            if (row.Id != Guid.Empty)
            {
                var entity = await _context.AccountOpeningBalances.FindAsync(row.Id);
                if (entity != null)
                {
                    await _periodService.EnsureOpeningBalanceCanBeModifiedAsync(entity.BalanceDate);
                    _context.AccountOpeningBalances.Remove(entity);
                    await _context.SaveChangesAsync();
                }
            }
            _openings.Remove(row);
        }

        private async void OnSaveOpeningsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var row in _openings.Where(item => !item.IsSystemGenerated))
                {
                    if (string.IsNullOrWhiteSpace(row.AccountCode))
                        throw new InvalidOperationException("Для каждой строки должен быть выбран счет.");
                    if (row.Debit != 0 && row.Credit != 0)
                        throw new InvalidOperationException($"По счету {row.AccountCode} остаток указывается только по одной стороне.");
                    var entity = row.Id == Guid.Empty ? new AccountOpeningBalance() :
                        await _context.AccountOpeningBalances.FindAsync(row.Id) ?? new AccountOpeningBalance();
                    await _periodService.EnsureOpeningBalanceCanBeModifiedAsync(row.BalanceDate);
                    if (row.Id != Guid.Empty)
                        await _periodService.EnsureOpeningBalanceCanBeModifiedAsync(entity.BalanceDate);
                    entity.BalanceDate = DateTime.SpecifyKind(row.BalanceDate.Date, DateTimeKind.Utc);
                    entity.AccountCode = row.AccountCode;
                    entity.Debit = row.Debit;
                    entity.Credit = row.Credit;
                    entity.UpdatedAt = DateTime.UtcNow;
                    if (row.Id == Guid.Empty)
                        await _context.AccountOpeningBalances.AddAsync(entity);
                }
                await _context.SaveChangesAsync();
                await LoadOpeningsAsync();
                StatusText.Text = "Начальные остатки сохранены";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Начальные остатки", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnReportCodeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await LoadLinesAsync();
        }

        private async Task LoadLinesAsync()
        {
            await _periodService.EnsureSchemaAsync();
            var code = CurrentReportCode;
            var lines = await _context.FinancialReportLines.AsNoTracking()
                .Where(line => line.ReportCode == code).OrderBy(line => line.SortOrder).ToListAsync();
            var ids = lines.Select(line => line.Id).ToList();
            var links = await _context.FinancialReportLineAccounts.AsNoTracking()
                .Where(link => ids.Contains(link.LineId)).ToListAsync();
            _reportLines.Clear();
            foreach (var line in lines)
                _reportLines.Add(new ReportLineEditorRow
                {
                    Id = line.Id, LineCode = line.LineCode, SectionCode = line.SectionCode, Name = line.Name,
                    SortOrder = line.SortOrder, Sign = line.Sign, IsTotal = line.IsTotal,
                    AccountCodes = string.Join(", ", links.Where(link => link.LineId == line.Id).Select(link => link.AccountCode))
                });
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e) => _reportLines.Add(new ReportLineEditorRow
        {
            SortOrder = _reportLines.Count == 0 ? 10 : _reportLines.Max(line => line.SortOrder) + 10,
            Sign = 1
        });

        private async void OnDeleteLineClick(object sender, RoutedEventArgs e)
        {
            if (ReportLinesGrid.SelectedItem is not ReportLineEditorRow row)
                return;
            if (row.Id != Guid.Empty)
            {
                var links = await _context.FinancialReportLineAccounts.Where(link => link.LineId == row.Id).ToListAsync();
                _context.FinancialReportLineAccounts.RemoveRange(links);
                var entity = await _context.FinancialReportLines.FindAsync(row.Id);
                if (entity != null) _context.FinancialReportLines.Remove(entity);
                await _context.SaveChangesAsync();
            }
            _reportLines.Remove(row);
        }

        private async void OnSaveLinesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var row in _reportLines)
                {
                    if (string.IsNullOrWhiteSpace(row.LineCode) || string.IsNullOrWhiteSpace(row.Name))
                        throw new InvalidOperationException("Заполните код и наименование каждой строки.");
                    var entity = row.Id == Guid.Empty ? new FinancialReportLine() :
                        await _context.FinancialReportLines.FindAsync(row.Id) ?? new FinancialReportLine();
                    entity.ReportCode = CurrentReportCode;
                    entity.LineCode = row.LineCode.Trim();
                    entity.SectionCode = row.SectionCode.Trim();
                    entity.Name = row.Name.Trim();
                    entity.SortOrder = row.SortOrder;
                    entity.Sign = row.Sign == -1 ? -1 : 1;
                    entity.IsTotal = row.IsTotal;
                    entity.IsActive = true;
                    if (row.Id == Guid.Empty)
                    {
                        await _context.FinancialReportLines.AddAsync(entity);
                        await _context.SaveChangesAsync();
                        row.Id = entity.Id;
                    }
                    var oldLinks = await _context.FinancialReportLineAccounts.Where(link => link.LineId == entity.Id).ToListAsync();
                    _context.FinancialReportLineAccounts.RemoveRange(oldLinks);
                    var codes = row.AccountCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct();
                    await _context.FinancialReportLineAccounts.AddRangeAsync(codes.Select(accountCode =>
                        new FinancialReportLineAccount { LineId = entity.Id, AccountCode = accountCode }));
                }
                await _context.SaveChangesAsync();
                await LoadLinesAsync();
                StatusText.Text = "Настройка строк отчетности сохранена";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Настройка отчетности", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string CurrentReportCode => (ReportCodeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Balance";

        private async Task<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>> LoadAccountsAsync()
        {
            var metadata = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");
            return metadata == null ? new() : await _metadataService.GetCatalogDataAsync(metadata.Id);
        }

        private void OnOpeningGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is not OpeningBalanceEditorRow { IsSystemGenerated: true })
                return;

            e.Cancel = true;
            StatusText.Text = "Автоматически перенесенные остатки редактируются через закрытие и повторное открытие периода.";
        }
    }

    public class OpeningBalanceEditorRow
    {
        public Guid Id { get; set; }
        public DateTime BalanceDate { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string SourceDescription { get; set; } = "Ручной ввод";
        public bool IsSystemGenerated { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class ReportLineEditorRow
    {
        public Guid Id { get; set; }
        public string LineCode { get; set; } = string.Empty;
        public string SectionCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public int Sign { get; set; } = 1;
        public bool IsTotal { get; set; }
        public string AccountCodes { get; set; } = string.Empty;
    }
}
