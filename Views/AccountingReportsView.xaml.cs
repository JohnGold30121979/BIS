using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Views
{
    public partial class AccountingReportsView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly BalanceService _balanceService;
        private readonly OrganizationBalanceService _organizationBalanceService;
        private readonly ReportService _reportService;
        private readonly AccountingPeriodService _periodService;
        private readonly PostingService _postingService;
        private readonly MetadataService _metadataService;
        private DataTable? _currentData;
        private Report? _currentReport;
        private AccountingPeriod? _currentPeriod;
        private List<AccountingPeriodModuleStatus> _currentModuleStates = new();
        private bool _reconciliationVariantsLoaded;
        private Guid? _selectedOrganizationId;
        private string? _selectedOrganizationName;
        private IReadOnlyList<OrganizationSelectionItem> _organizationSelectionItems = Array.Empty<OrganizationSelectionItem>();

        public AccountingReportsView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _balanceService = new BalanceService(context);
            _organizationBalanceService = new OrganizationBalanceService(context);
            _reportService = new ReportService(context);
            _periodService = new AccountingPeriodService(context);
            _postingService = new PostingService(context);
            _metadataService = new MetadataService(context);
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
            EndDatePicker.SelectedDate = DateTime.Today;
            Loaded += async (_, _) =>
            {
                await LoadReconciliationReportVariantsAsync();
                await LoadOrganizationsAsync();
            };
            UpdateReconciliationVariantVisibility();
        }

        private void OnOrganizationSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OrganizationCombo.SelectedItem is OrganizationSelectionItem org && !org.IsAll)
            {
                _selectedOrganizationId = org.Id;
                _selectedOrganizationName = org.Name;
                return;
            }

            _selectedOrganizationId = null;
            _selectedOrganizationName = null;
        }

        private void OnClearOrganizationClick(object sender, RoutedEventArgs e)
        {
            OrganizationCombo.SelectedIndex = OrganizationCombo.Items.Count > 0 ? 0 : -1;
            _selectedOrganizationId = null;
            _selectedOrganizationName = null;
        }
        private async void OnSelectOrganizationClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Загрузка организаций...";

                var organizations = (await LoadOrganizationSelectionItemsAsync(false)).ToList();
                if (organizations.Count == 0)
                {
                    MessageBox.Show(
                        "В справочнике организаций нет записей. Проверьте catalog_organizations или системный справочник Organizations.",
                        "Организации", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var rows = organizations.Select(org => new Dictionary<string, object>
                {
                    ["Id"] = org.Id ?? Guid.Empty,
                    ["Код"] = org.Code,
                    ["Наименование"] = org.Name,
                    ["Полное наименование"] = org.FullName,
                    ["Активна"] = LocalizationService.DisplayValue(org.IsActive),
                    ["Первичная"] = LocalizationService.DisplayValue(org.IsPrimary)
                }).ToList();

                var dialog = new ReferenceSelectionDialog(rows, "Код", "Наименование")
                {
                    Owner = Window.GetWindow(this),
                    Title = $"Выбор: Организации ({organizations.Count})"
                };
                if (dialog.ShowDialog() != true || dialog.SelectedItem == null)
                    return;

                var selectedOrg = TryReadGuid(dialog.SelectedItem, out var selectedId, "Id")
                    ? organizations.FirstOrDefault(org => org.Id == selectedId)
                    : null;
                if (selectedOrg == null)
                {
                    var selectedCode = ReadReportString(dialog.SelectedItem, "Код", "code");
                    var selectedName = ReadReportString(dialog.SelectedItem, "Наименование", "name");
                    selectedOrg = organizations.FirstOrDefault(org =>
                        org.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase) &&
                        org.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
                }
                if (selectedOrg == null)
                    return;

                if (_organizationSelectionItems.All(item => item.Id != selectedOrg.Id))
                    await LoadOrganizationsAsync();

                OrganizationCombo.SelectedItem = _organizationSelectionItems.FirstOrDefault(item => item.Id == selectedOrg.Id) ?? selectedOrg;
                _selectedOrganizationId = selectedOrg.Id;
                _selectedOrganizationName = selectedOrg.Name;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе организации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
                StatusText.Text = "Выберите отчет и период";
            }
        }
        private async Task LoadOrganizationsAsync()
        {
            try
            {
                _organizationSelectionItems = await LoadOrganizationSelectionItemsAsync(true);
                OrganizationCombo.ItemsSource = _organizationSelectionItems;
                OrganizationCombo.SelectedIndex = _organizationSelectionItems.Count > 0 ? 0 : -1;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки организаций: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<IReadOnlyList<OrganizationSelectionItem>> LoadOrganizationSelectionItemsAsync(bool includeAll)
        {
            var organizations = new List<OrganizationSelectionItem>();
            if (includeAll)
                organizations.Add(OrganizationSelectionItem.CreateAll());

            try
            {
                var metadataRows = await LoadMetadataOrganizationsAsync();
                organizations.AddRange(metadataRows.Select(BuildOrganizationSelectionItem)
                    .Where(org => org.Id.HasValue || !string.IsNullOrWhiteSpace(org.Name)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки catalog_organizations: {ex.Message}");
            }

            if (organizations.Count == (includeAll ? 1 : 0))
                organizations.AddRange(await LoadEfOrganizationsAsync());

            return DeduplicateOrganizationSelectionItems(organizations)
                .OrderByDescending(org => org.IsAll)
                .ThenByDescending(org => org.IsPrimary)
                .ThenBy(org => org.Code, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(org => org.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private async Task<List<Dictionary<string, object>>> LoadMetadataOrganizationsAsync()
        {
            var organizationCatalog = await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "Организации");
            return organizationCatalog == null
                ? new List<Dictionary<string, object>>()
                : await _metadataService.GetCatalogDataAsync(organizationCatalog.Id);
        }

        private async Task<List<OrganizationSelectionItem>> LoadEfOrganizationsAsync()
        {
            try
            {
                return await _context.Organizations.AsNoTracking()
                    .OrderByDescending(org => org.IsActive)
                    .ThenBy(org => org.Code)
                    .Select(org => new OrganizationSelectionItem
                    {
                        Id = org.Id,
                        Code = org.Code ?? string.Empty,
                        Name = org.Name ?? string.Empty,
                        FullName = org.FullName ?? string.Empty,
                        IsActive = org.IsActive,
                        IsPrimary = false
                    }).ToListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки Organizations: {ex.Message}");
                return new List<OrganizationSelectionItem>();
            }
        }

        private static OrganizationSelectionItem BuildOrganizationSelectionItem(Dictionary<string, object> row)
        {
            var id = TryReadGuid(row, out var organizationId, "Id") ? organizationId : (Guid?)null;
            var code = ReadReportString(row, "Код", "code");
            var name = ReadReportString(row, "Наименование", "name");
            var fullName = ReadReportString(row, "Полное наименование", "full_name");
            if (string.IsNullOrWhiteSpace(name))
                name = string.IsNullOrWhiteSpace(fullName) ? code : fullName;

            return new OrganizationSelectionItem
            {
                Id = id,
                Code = code,
                Name = name,
                FullName = fullName,
                IsActive = !TryGetReportValue(row, out var activeValue, "Активна", "is_active") || ReadReportBoolValue(activeValue, true),
                IsPrimary = TryGetReportValue(row, out var primaryValue, "Первичная", "Первичная организация", "is_primary") && ReadReportBoolValue(primaryValue, false)
            };
        }

        private static IReadOnlyList<OrganizationSelectionItem> DeduplicateOrganizationSelectionItems(IEnumerable<OrganizationSelectionItem> organizations)
        {
            return organizations
                .Where(org => org.IsAll || org.Id.HasValue || !string.IsNullOrWhiteSpace(org.Name))
                .GroupBy(org => org.Id.HasValue ? $"id:{org.Id}" : $"name:{org.Code}:{org.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        public void SelectReportType(string reportType)
        {
            foreach (var item in ReportTypeCombo.Items.OfType<ComboBoxItem>())
            {
                if (!string.Equals(item.Tag?.ToString(), reportType, StringComparison.OrdinalIgnoreCase))
                    continue;

                ReportTypeCombo.SelectedItem = item;
                break;
            }

            UpdateReconciliationVariantVisibility();
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IsEnabled = false;
                StatusText.Text = "Формирование отчета...";
                var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
                var end = EndDatePicker.SelectedDate ?? DateTime.Today;
                if (end < start)
                    throw new Exception("Дата окончания периода не может быть раньше даты начала");

                await _periodService.EnsureSchemaAsync();

                (_currentData, _currentReport) = reportType switch
                {
                    "GeneralLedger" => await BuildGeneralLedgerAsync(start.Year),
                    "FinancialPosition" => await BuildFinancialPositionAsync(end),
                    "FinancialResults" => await BuildFinancialResultsAsync(start, end),
                    "PurchaseSalesJournal" => await BuildPurchaseSalesJournalAsync(start, end),
                    "OrganizationBalances" => await BuildOrganizationBalancesAsync(start, end),
                    "PeriodCollection" => await BuildPeriodCollectionAsync(start, end),
                    "PaymentOrderRegister" => await BuildPaymentOrderRegisterAsync(start, end),
                    "OrganizationReconciliation" => await BuildOrganizationReconciliationAsync(start, end),
                    _ => await BuildTrialBalanceAsync(start, end)
                };

                ReportGrid.ItemsSource = _currentData.DefaultView;
                SearchBox.Clear();
                await UpdatePeriodStateAsync(start, end);
                StatusText.Text = $"Сформировано строк: {_currentData.Rows.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка формирования отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка формирования";
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnCollectPeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.CollectAsync(start, end);
                await UpdatePeriodStateAsync(start, end);
                StatusText.Text = "Информация за периода собрана и контрольные остатки сохранены";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Сбор периода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnTogglePeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.FindAsync(start, end);
                if (_currentPeriod == null)
                    throw new InvalidOperationException("Сначала выполните сбор информации за период.");

                if (_currentPeriod.IsLocked)
                {
                    if (MessageBox.Show("Открыть период для изменения документов?", "Учетный период",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    await _periodService.ReopenAsync(_currentPeriod.Id);
                }
                else
                {
                    if (MessageBox.Show(
                            "Выполнить итоговое закрытие баланса? После этого документы с датами выбранного периода нельзя будет изменять.",
                            "Итоговое закрытие баланса", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    await _periodService.CloseAsync(_currentPeriod.Id);
                }
                await UpdatePeriodStateAsync(start, end);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Учетный период", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task UpdatePeriodStateAsync(DateTime start, DateTime end)
        {
            _currentPeriod = await _periodService.FindAsync(start, end);
            PeriodStatusText.Text = _currentPeriod == null
                ? "Период не собран"
                : _currentPeriod.IsLocked
                    ? "Статус: итоговый баланс закрыт"
                    : $"Статус: {LocalizationService.DisplayValue(_currentPeriod.Status)}";
            PeriodStateButton.Content = _currentPeriod?.IsLocked == true ? "Открыть баланс" : "Закрыть баланс";
            await LoadPeriodModuleStatesAsync();
        }

        private async Task LoadPeriodModuleStatesAsync()
        {
            _currentModuleStates.Clear();
            PeriodModuleCombo.ItemsSource = null;

            if (_currentPeriod == null)
            {
                PeriodModuleCombo.IsEnabled = false;
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = "Модули периода недоступны";
                return;
            }

            _currentModuleStates = await _periodService.GetModuleStatusesAsync(_currentPeriod.Id);
            PeriodModuleCombo.ItemsSource = _currentModuleStates;
            if (_currentModuleStates.Count == 0)
            {
                PeriodModuleCombo.IsEnabled = false;
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = _currentPeriod.IsLocked
                    ? "Рабочих модулей нет; итоговый баланс закрыт."
                    : "Рабочих модулей для закрытия нет. Можно выполнить итоговое закрытие баланса.";
                return;
            }

            PeriodModuleCombo.IsEnabled = _currentModuleStates.Count > 0 && _currentPeriod.IsLocked == false;
            PeriodModuleCombo.SelectedItem = _currentModuleStates.FirstOrDefault(item => !item.IsClosed) ??
                                             _currentModuleStates.FirstOrDefault();
            UpdateSelectedModuleState();
        }

        private void OnPeriodModuleSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedModuleState();
        }

        private void UpdateSelectedModuleState()
        {
            if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
            {
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = "Модули периода недоступны";
                return;
            }

            CloseModuleButton.IsEnabled = !_currentPeriod.IsLocked && !module.IsClosed && module.ParticipatesInPeriodClose;
            ReopenModuleButton.IsEnabled = !_currentPeriod.IsLocked && module.IsClosed;

            if (!module.ParticipatesInPeriodClose)
            {
                PeriodModuleStatusText.Text = $"{module.ModuleName}: не участвует в закрытии периода";
                return;
            }

            var dependencyText = module.RequirePreviousModulesClosed
                ? "закрывается по очереди"
                : "может закрываться отдельно";
            PeriodModuleStatusText.Text = $"{module.ModuleName}: {module.StateCaption}, {dependencyText}";
        }

        private async void OnCloseModuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
                    return;

                await _periodService.CloseModuleAsync(_currentPeriod.Id, module.ModuleId);
                await UpdatePeriodStateAsync(_currentPeriod.StartDate, _currentPeriod.EndDate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Закрытие модуля", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnReopenModuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
                    return;

                await _periodService.ReopenModuleAsync(_currentPeriod.Id, module.ModuleId);
                await UpdatePeriodStateAsync(_currentPeriod.StartDate, _currentPeriod.EndDate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Открытие модуля", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private (DateTime Start, DateTime End) GetPeriod()
        {
            var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            var end = EndDatePicker.SelectedDate ?? DateTime.Today;
            if (end < start)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала");
            return (start, end);
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentData == null)
                return;
            var search = SearchBox.Text.Trim().Replace("'", "''").Replace("[", "[[]");
            _currentData.DefaultView.RowFilter = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : string.Join(" OR ", _currentData.Columns.Cast<DataColumn>()
                    .Where(column => column.DataType == typeof(string))
                    .Select(column => $"CONVERT([{column.ColumnName}], 'System.String') LIKE '%{search}%'"));
        }

        private void OnReportGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ReportGrid.SelectedItem is not DataRowView row || !row.Row.Table.Columns.Contains("Счет"))
                return;
            var code = row["Счет"]?.ToString();
            if (string.IsNullOrWhiteSpace(code) || code == "ИТОГО")
                return;
            var name = row.Row.Table.Columns.Contains("Наименование") ? row["Наименование"]?.ToString() ?? code : code;
            var (start, end) = GetPeriod();
            new TurnoversDialog(code, name, start, end, _postingService) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private async Task<(DataTable, Report)> BuildTrialBalanceAsync(DateTime start, DateTime end)
        {
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Оборотно-сальдовая ведомость");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            foreach (var name in new[] { "Сальдо нач. Дт", "Сальдо нач. Кт", "Оборот Дт", "Оборот Кт", "Сальдо кон. Дт", "Сальдо кон. Кт" })
                table.Columns.Add(name, typeof(decimal));

            foreach (var item in balances)
                table.Rows.Add(item.AccountCode, item.AccountName, item.OpeningDebit, item.OpeningCredit,
                    item.TurnoverDebit, item.TurnoverCredit, item.ClosingDebit, item.ClosingCredit);

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Оборотно-сальдовая ведомость за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true));
        }

        private async Task<(DataTable, Report)> BuildPaymentOrderRegisterAsync(DateTime start, DateTime end)
        {
            var rows = await LoadPaymentOrderReportRowsAsync(start, end);
            var table = new DataTable("Реестр платежных поручений");
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Тип", typeof(string));
            table.Columns.Add("Наш счет", typeof(string));
            table.Columns.Add("Корр. счет", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));
            table.Columns.Add("Валюта", typeof(string));
            table.Columns.Add("Сумма в валюте", typeof(decimal));
            table.Columns.Add("Курс", typeof(decimal));
            table.Columns.Add("Классификация платежа", typeof(string));
            table.Columns.Add("Назначение платежа", typeof(string));
            table.Columns.Add("Проведен", typeof(string));
            table.Columns.Add("Примечание", typeof(string));

            foreach (var row in rows)
            {
                table.Rows.Add(row.Date, row.Number, row.OrderType, row.OurAccount, row.CorrespondentAccount,
                    row.Organization, row.Amount, row.Currency, row.AmountCurrency, row.ExchangeRate,
                    row.PaymentClassification, row.Purpose, LocalizationService.DisplayValue(row.IsPosted), row.Note);
            }

            table.Rows.Add(DBNull.Value, "ИТОГО", string.Empty, string.Empty, string.Empty, string.Empty,
                rows.Sum(row => row.Amount), string.Empty, rows.Sum(row => row.AmountCurrency),
                0m, string.Empty, string.Empty, string.Empty, string.Empty);

            var report = CreateReport(table,
                $"Реестр платежных поручений за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true);
            report.ShowGrandTotal = true;
            report.SummaryText = "Отчет строится по документам платежных поручений и показывает только документы выбранного периода.";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildOrganizationReconciliationAsync(DateTime start, DateTime end)
        {
            var variant = GetSelectedReconciliationReportVariant();
            var selectedOrganizationId = _selectedOrganizationId.GetValueOrDefault();
            var scoped = selectedOrganizationId != Guid.Empty;
            Guid? selectedFilterId = scoped ? selectedOrganizationId : null;
            var selected = scoped ? _organizationSelectionItems.FirstOrDefault(org => org.Id == selectedOrganizationId) : null;
            var filterName = !string.IsNullOrWhiteSpace(selected?.Name) ? selected!.Name : NormalizeOrganizationName(_selectedOrganizationName);
            var titleName = scoped ? ResolveOrganizationTitle(selectedFilterId, filterName) : "Все организации";

            var calc = await _organizationBalanceService.CalculateAsync(start, end);
            var pairs = calc.Rows
                .Where(row => !row.IsOrganizationTotal && MatchesSelectedOrganization(row, selectedFilterId, filterName))
                .OrderBy(row => row.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(row => row.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CounterAccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totals = calc.Rows
                .Where(row => row.IsOrganizationTotal && MatchesSelectedOrganization(row, selectedFilterId, filterName))
                .ToList();
            var movements = await LoadReconciliationMovementRowsAsync(selectedFilterId, start, end);

            var table = CreateReconciliationTable();
            AddReconciliationRow(table, scoped ? $"АКТ СВЕРКИ между нашей организацией и \"{titleName}\"" : "АКТЫ СВЕРКИ ПО ВСЕМ ОРГАНИЗАЦИЯМ");
            AddReconciliationRow(table, $"Период: {start:dd.MM.yyyy} - {end:dd.MM.yyyy}");
            AddReconciliationRow(table, string.Empty);

            if (pairs.Count == 0)
            {
                AddReconciliationRow(table, scoped
                    ? "Данных по выбранной организации и активным парам счетов за период не найдено."
                    : "Данных по организациям и активным парам счетов за период не найдено.");
            }
            else if (scoped)
            {
                var total = totals.FirstOrDefault(row => MatchesSelectedOrganization(row, selectedFilterId, filterName));
                AddReconciliationSection(table, titleName, pairs, total, movements, start, end, false);
            }
            else
            {
                foreach (var group in pairs.GroupBy(row => new { row.OrganizationId, Name = NormalizeOrganizationName(row.OrganizationName) }).OrderBy(group => group.Key.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var groupName = string.IsNullOrWhiteSpace(group.Key.Name) ? "Без организации" : group.Key.Name;
                    var groupTitle = ResolveOrganizationTitle(group.Key.OrganizationId, groupName);
                    var total = totals.FirstOrDefault(row => SameOrganization(row, group.Key.OrganizationId, groupName));
                    var groupMovements = movements.Where(movement => MatchesMovementOrganization(movement, group.Key.OrganizationId, groupName)).ToList();
                    AddReconciliationSection(table, groupTitle, group.ToList(), total, groupMovements, start, end, true);
                    AddReconciliationRow(table, string.Empty);
                }
            }

            var balance = scoped
                ? totals.FirstOrDefault(row => MatchesSelectedOrganization(row, selectedFilterId, filterName))?.Balance ?? pairs.Sum(row => row.Balance)
                : totals.Sum(row => row.Balance);
            var summary = scoped ? BuildDebtSummary(titleName, balance) : BuildAllOrganizationsDebtSummary(totals.Count, balance);
            AddReconciliationRow(table, summary);

            var titlePrefix = variant?.IsStandard == false ? variant.DisplayName : "Акт сверки";
            var report = CreateReport(table, scoped ? $"{titlePrefix}: {titleName} на {end:dd.MM.yyyy}" : $"{titlePrefix}: все организации на {end:dd.MM.yyyy}", true);
            report.ReportType = "ReconciliationAct";
            report.SubtitleText = $"Период: {start:dd.MM.yyyy} - {end:dd.MM.yyyy}";
            report.ShowGrandTotal = false;
            report.AlternateRowColors = false;
            report.FontSize = 8;
            report.SummaryText = summary;
            report.FooterSignature = "Руководитель ____________________    Главный бухгалтер ____________________";
            if (calc.Warnings.Count > 0)
                report.FooterText = string.Join(Environment.NewLine, calc.Warnings);
            if (variant?.IsStandard == false)
            {
                var variantText = $"Выбран вариант печатной формы из метаданных: {variant.DisplayName}.";
                report.FooterText = string.IsNullOrWhiteSpace(report.FooterText) ? variantText : report.FooterText + Environment.NewLine + variantText;
            }

            SetReportFieldWidth(report, "Наименование материала, вид операции", 280);
            SetReportFieldWidth(report, "Дебет", 75);
            SetReportFieldWidth(report, "Кредит", 75);
            SetReportFieldWidth(report, "Сумма Дт", 90, "Right", "N2");
            SetReportFieldWidth(report, "Сумма Кт", 90, "Right", "N2");
            SetReportFieldWidth(report, "N докум", 70);
            SetReportFieldWidth(report, "Дата", 70);
            SetReportFieldWidth(report, "Модуль", 45);
            await ApplyReconciliationVariantLayoutAsync(report, variant);
            return (table, report);
        }
        private async Task ApplyReconciliationVariantLayoutAsync(Report report, ReconciliationReportVariant? variant)
        {
            if (variant?.ReportId == null)
                return;

            var templateReport = await _reportService.GetReportAsync(variant.ReportId.Value);
            if (templateReport == null || string.IsNullOrWhiteSpace(templateReport.Template))
                return;

            report.SourceFormat = templateReport.SourceFormat;
            report.Template = templateReport.Template;
            report.TemplateVersion = templateReport.TemplateVersion;
            report.PageOrientation = templateReport.PageOrientation;
            report.PageWidth = templateReport.PageWidth;
            report.PageHeight = templateReport.PageHeight;
            report.LeftMargin = templateReport.LeftMargin;
            report.RightMargin = templateReport.RightMargin;
            report.TopMargin = templateReport.TopMargin;
            report.BottomMargin = templateReport.BottomMargin;
            report.FontName = templateReport.FontName;
            report.ShowHeader = templateReport.ShowHeader;
            report.ShowFooter = templateReport.ShowFooter;
            report.ShowPageNumbers = templateReport.ShowPageNumbers;
            report.ShowGridLines = templateReport.ShowGridLines;
            report.AlternateRowColors = templateReport.AlternateRowColors;
            report.AlternateRowColor = templateReport.AlternateRowColor;
            report.HeaderColor = templateReport.HeaderColor;
            report.ElementMappings = templateReport.ElementMappings.Select(mapping => new ReportElementMapping
            {
                ReportId = report.Id,
                ElementOrder = mapping.ElementOrder,
                ElementType = mapping.ElementType,
                ElementText = mapping.ElementText,
                ElementExpression = mapping.ElementExpression,
                BandType = mapping.BandType,
                Left = mapping.Left,
                Top = mapping.Top,
                Width = mapping.Width,
                Height = mapping.Height,
                FontName = mapping.FontName,
                FontSize = mapping.FontSize,
                Bold = mapping.Bold,
                Italic = mapping.Italic,
                Alignment = mapping.Alignment,
                Order = mapping.Order,
                MappedFieldName = mapping.MappedFieldName,
                MappedDisplayName = mapping.MappedDisplayName,
                DataSource = mapping.DataSource,
                FormatString = mapping.FormatString,
                IsVisible = mapping.IsVisible,
                CustomText = mapping.CustomText
            }).ToList();
        }
        private async Task LoadReconciliationReportVariantsAsync()
        {
            if (_reconciliationVariantsLoaded)
                return;

            _reconciliationVariantsLoaded = true;
            var variants = new List<ReconciliationReportVariant>
            {
                new()
                {
                    DisplayName = "Акт сверки",
                    Description = "Стандартный расчетный вариант BIS ERP.",
                    IsStandard = true
                }
            };
            var variantsLoadError = string.Empty;

            try
            {
                var reports = await _context.Reports.AsNoTracking()
                    .Where(report =>
                        report.IsActive &&
                        (EF.Functions.Like(report.Code, "standard.frx.finance.reconciliation.%") ||
                         report.SourceFormat == "FoxProFRX" ||
                         report.ReportType == "FoxProLayout"))
                    .OrderBy(report => report.Order)
                    .ThenBy(report => report.Name)
                    .Select(report => new
                    {
                        report.Id,
                        report.Name,
                        report.Description,
                        report.Code,
                        report.SourceFormat,
                        report.ReportType,
                        report.Template
                    })
                    .ToListAsync();

                var candidateReports = reports
                    .Where(report => ReportClassificationService.IsReconciliationReport(new Report
                    {
                        Id = report.Id,
                        Name = report.Name,
                        Description = report.Description,
                        Code = report.Code,
                        SourceFormat = report.SourceFormat,
                        ReportType = report.ReportType,
                        Template = report.Template
                    }))
                    .ToList();
                if (candidateReports.Count == 0)
                    candidateReports = reports
                        .Where(report =>
                            string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                foreach (var report in candidateReports)
                {
                    var displayName = CleanReconciliationVariantName(report.Name);
                    if (variants.Any(item => item.ReportId == report.Id || item.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    variants.Add(new ReconciliationReportVariant
                    {
                        ReportId = report.Id,
                        DisplayName = displayName,
                        Description = report.Description,
                        Code = report.Code,
                        IsStandard = false
                    });
                }
            }
            catch (Exception ex)
            {
                variantsLoadError = $"FoxPro report templates для акта сверки не загружены: {ex.Message}";
            }

            ReconciliationVariantCombo.ItemsSource = variants;
            ReconciliationVariantCombo.SelectedIndex = 0;
            ReconciliationVariantHint.Text = !string.IsNullOrWhiteSpace(variantsLoadError)
                ? variantsLoadError
                : variants.Count == 1
                    ? "FoxPro report templates для акта сверки в метаданных не найдены"
                    : $"Доступно вариантов: {variants.Count}";
            UpdateReconciliationVariantVisibility();
        }

        private void OnReportTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateReconciliationVariantVisibility();
        }

        private void UpdateReconciliationVariantVisibility()
        {
            if (ReconciliationVariantPanel == null || ReportTypeCombo == null)
                return;

            ReconciliationVariantPanel.Visibility = IsOrganizationReconciliationSelected()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private bool IsOrganizationReconciliationSelected() =>
            string.Equals((ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                "OrganizationReconciliation", StringComparison.OrdinalIgnoreCase);

        private ReconciliationReportVariant? GetSelectedReconciliationReportVariant() =>
            ReconciliationVariantCombo?.SelectedItem as ReconciliationReportVariant;

        private static string CleanReconciliationVariantName(string name) =>
            (name ?? string.Empty)
                .Replace(" (FRX FoxPro)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(" (FoxPro report template)", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        private async Task<List<PaymentOrderReportRow>> LoadPaymentOrderReportRowsAsync(DateTime? start, DateTime end)
        {
            var document = await _context.MetadataObjects
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.ObjectType == "Document" && item.Name == "Платежное поручение");
            if (document == null)
                return new List<PaymentOrderReportRow>();

            var rows = await _metadataService.GetCatalogDataAsync(document.Id);
            var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(document, _metadataService);
            var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            var result = new List<PaymentOrderReportRow>();

            foreach (var row in rows)
            {
                var date = ReadReportDate(row, "Дата", "doc_date");
                if (!date.HasValue || date.Value.Date > end.Date)
                    continue;
                if (start.HasValue && date.Value.Date < start.Value.Date)
                    continue;

                var correspondentRaw = ReadReportString(row, "Корр. счет", "correspondent_account", "Корр счет", "Коррсчет");
                var correspondentAccount = ResolveReportReference(row, referenceMaps, "Корр. счет", "correspondent_account", "Корр счет", "Коррсчет");
                var analyticAccount = accountAnalytics.FindAccount(correspondentRaw);
                if (analyticAccount != null)
                    correspondentAccount = analyticAccount.DisplayName;

                result.Add(new PaymentOrderReportRow
                {
                    Date = date.Value,
                    Number = ReadReportString(row, "Номер", "doc_number"),
                    OrderType = ReadReportString(row, "Тип", "order_type"),
                    OurAccount = ResolveReportReference(row, referenceMaps, "Наш счет", "our_account_id"),
                    CorrespondentAccount = correspondentAccount,
                    Organization = ResolveReportReference(row, referenceMaps, "Организация", "organization_id"),
                    Amount = ReadReportDecimal(row, "Сумма", "amount"),
                    Currency = ResolveReportReference(row, referenceMaps, "Валюта", "currency_id"),
                    AmountCurrency = ReadReportDecimal(row, "Сумма в валюте", "amount_currency"),
                    ExchangeRate = ReadReportDecimal(row, "Курс", "exchange_rate"),
                    PaymentClassification = ResolveReportReference(row, referenceMaps, "Классификация платежа", "payment_classification_id"),
                    Purpose = ReadReportString(row, "Назначение платежа", "purpose"),
                    Note = ReadReportString(row, "Примечание", "description"),
                    IsPosted = ReadReportBool(row, "Проведён", "Проведен", "is_posted")
                });
            }

            return result
                .OrderBy(row => row.Date)
                .ThenBy(row => row.Number, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string ResolveReportReference(
            Dictionary<string, object> row,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;

                var text = value.ToString() ?? string.Empty;
                if (Guid.TryParse(text, out var id))
                {
                    foreach (var mapKey in keys)
                    {
                        if (referenceMaps.TryGetValue(mapKey, out var map) && map.TryGetValue(id, out var display))
                            return display;
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        private static string ReadReportString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (TryGetReportValue(row, out var value, key) && value != null && value != DBNull.Value)
                    return value.ToString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        private static DateTime? ReadReportDate(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is DateTime date)
                    return date;
                if (value is DateTimeOffset dateTimeOffset)
                    return dateTimeOffset.LocalDateTime;
                if (DateTime.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static decimal ReadReportDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is decimal decimalValue)
                    return decimalValue;
                if (value is double doubleValue)
                    return Convert.ToDecimal(doubleValue);
                if (value is int intValue)
                    return Convert.ToDecimal(intValue);
                if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                    decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return 0m;
        }

        private static bool ReadReportBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is bool boolValue)
                    return boolValue;
                if (bool.TryParse(value.ToString(), out var parsed))
                    return parsed;
                if (int.TryParse(value.ToString(), out var intValue))
                    return intValue != 0;
            }

            return false;
        }

        private static bool TryGetReportValue(Dictionary<string, object> row, out object? value, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out value))
                    return true;

                var match = row.FirstOrDefault(pair =>
                    pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (!match.Equals(default(KeyValuePair<string, object>)))
                {
                    value = match.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private async Task<List<ReconciliationMovementRow>> LoadReconciliationMovementRowsAsync(Guid? organizationId, DateTime start, DateTime end)
        {
            var rows = new List<ReconciliationMovementRow>();
            try
            {
                await _context.Database.ExecuteSqlRawAsync(@"
                    DO $$
                    BEGIN
                        IF to_regclass('public.doc_postings') IS NOT NULL THEN
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS module_code varchar(50);
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS amount_currency numeric(18,2) NOT NULL DEFAULT 0;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS currency_id text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS organization_id uuid;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS description text;
                            ALTER TABLE doc_postings ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        END IF;
                    END $$;");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                return rows;
            }

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
                SELECT p.posting_date, p.doc_number, COALESCE(p.document_type, 'Проводка') AS document_type,
                       COALESCE(p.module_code, '') AS module_code,
                       p.debit_account, p.credit_account, COALESCE(p.amount_kgs, 0) AS amount_kgs,
                       COALESCE(p.description, '') AS description,
                       CASE
                           WHEN COALESCE(p.organization_id::text, '') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                           THEN p.organization_id::text
                           ELSE NULL
                       END AS organization_id_text,
                       COALESCE(NULLIF(o.""name"", ''), 'Без организации') AS organization_name
                FROM doc_postings p
                LEFT JOIN catalog_organizations o ON p.organization_id::text = o.""Id""::text
                WHERE COALESCE(p.is_active, true) = true
                  AND (COALESCE(@organizationId, '') = '' OR p.organization_id::text = @organizationId)
                  AND p.posting_date >= @startDate
                  AND p.posting_date < @endDateExclusive
                ORDER BY organization_name, p.posting_date, p.doc_number, p.debit_account, p.credit_account";
            command.Parameters.Add(new NpgsqlParameter("@organizationId", organizationId.HasValue && organizationId.Value != Guid.Empty ? organizationId.Value.ToString() : string.Empty));
            command.Parameters.Add(new NpgsqlParameter("@startDate", DateTime.SpecifyKind(start.Date, DateTimeKind.Utc)));
            command.Parameters.Add(new NpgsqlParameter("@endDateExclusive", DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc)));

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var postingDate = reader["posting_date"] is DateTime date ? date : Convert.ToDateTime(reader["posting_date"], CultureInfo.InvariantCulture);
                    rows.Add(new ReconciliationMovementRow
                    {
                        Date = postingDate,
                        OrganizationId = Guid.TryParse(reader["organization_id_text"]?.ToString(), out var movementOrganizationId) ? movementOrganizationId : null,
                        OrganizationName = reader["organization_name"]?.ToString() ?? "Без организации",
                        DocumentNumber = MetadataService.NormalizeLegacyDocumentNumber(reader["doc_number"]?.ToString()),
                        DocumentType = reader["document_type"]?.ToString() ?? string.Empty,
                        ModuleCode = reader["module_code"]?.ToString() ?? string.Empty,
                        DebitAccount = reader["debit_account"]?.ToString() ?? string.Empty,
                        CreditAccount = reader["credit_account"]?.ToString() ?? string.Empty,
                        Amount = ReadDbDecimal(reader["amount_kgs"]),
                        Description = reader["description"]?.ToString() ?? string.Empty
                    });
                }
            }
            catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703")
            {
                rows.Clear();
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return rows;
        }
        private static DataTable CreateReconciliationTable()
        {
            var table = new DataTable("Акт сверки");
            table.Columns.Add("Наименование материала, вид операции", typeof(string));
            table.Columns.Add("Дебет", typeof(string));
            table.Columns.Add("Кредит", typeof(string));
            table.Columns.Add("Сумма Дт", typeof(decimal));
            table.Columns.Add("Сумма Кт", typeof(decimal));
            table.Columns.Add("N докум", typeof(string));
            table.Columns.Add("Дата", typeof(string));
            table.Columns.Add("Модуль", typeof(string));
            return table;
        }

        private static void AddReconciliationSection(DataTable table, string organizationTitle, IReadOnlyList<OrganizationBalanceRow> pairs, OrganizationBalanceRow? total, IReadOnlyList<ReconciliationMovementRow> movements, DateTime start, DateTime end, bool showOrganizationHeader)
        {
            if (showOrganizationHeader)
                AddReconciliationRow(table, $"Организация: {organizationTitle}");

            var firstPair = true;
            foreach (var pair in pairs)
            {
                if (!firstPair)
                    AddReconciliationRow(table, string.Empty);
                firstPair = false;

                var pairName = string.IsNullOrWhiteSpace(pair.AccountPairName) ? pair.CounterAccountName : pair.AccountPairName;
                AddReconciliationRow(table, $"Пара счетов : {pair.AccountCode} - {pair.CounterAccountCode} ({pairName})");
                AddReconciliationRow(table, $"САЛЬДО НА {start:dd.MM.yyyy}", debitAmount: NonZeroAmount(pair.OpeningDebit), creditAmount: NonZeroAmount(pair.OpeningCredit));

                var pairMovements = movements
                    .Where(movement => AccountInPair(movement.DebitAccount, pair) || AccountInPair(movement.CreditAccount, pair))
                    .OrderBy(movement => movement.Date)
                    .ThenBy(movement => movement.DocumentNumber, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (pairMovements.Count == 0 && (pair.TurnoverDebit != 0 || pair.TurnoverCredit != 0))
                    AddReconciliationRow(table, "Движения найдены в остатках, но детализация проводок недоступна.");

                foreach (var movement in pairMovements)
                {
                    AddReconciliationRow(
                        table,
                        BuildMovementDescription(movement),
                        movement.DebitAccount,
                        movement.CreditAccount,
                        AccountInPair(movement.DebitAccount, pair) ? (decimal?)movement.Amount : null,
                        AccountInPair(movement.CreditAccount, pair) ? (decimal?)movement.Amount : null,
                        movement.DocumentNumber,
                        movement.Date.ToString("dd.MM.yyyy"),
                        movement.ModuleCode);
                }

                AddReconciliationRow(table, "ИТОГО ОБОРОТОВ", debitAmount: NonZeroAmount(pair.TurnoverDebit), creditAmount: NonZeroAmount(pair.TurnoverCredit));
                AddReconciliationRow(table, $"САЛЬДО НА {end:dd.MM.yyyy}", debitAmount: NonZeroAmount(pair.ClosingDebit), creditAmount: NonZeroAmount(pair.ClosingCredit));
            }

            if (total != null && pairs.Count > 1)
            {
                AddReconciliationRow(table, string.Empty);
                AddReconciliationRow(table, "ИТОГО ОБОРОТОВ ПО ОРГАНИЗАЦИИ", debitAmount: NonZeroAmount(total.TurnoverDebit), creditAmount: NonZeroAmount(total.TurnoverCredit));
                AddReconciliationRow(table, $"САЛЬДО НА {end:dd.MM.yyyy} ПО ОРГАНИЗАЦИИ", debitAmount: NonZeroAmount(total.ClosingDebit), creditAmount: NonZeroAmount(total.ClosingCredit));
            }

            AddReconciliationRow(table, BuildDebtSummary(organizationTitle, total?.Balance ?? pairs.Sum(row => row.Balance)));
        }

        private static void AddReconciliationRow(DataTable table, string operation, string debit = "", string credit = "", decimal? debitAmount = null, decimal? creditAmount = null, string documentNumber = "", string date = "", string moduleCode = "")
        {
            var row = table.NewRow();
            row["Наименование материала, вид операции"] = operation;
            row["Дебет"] = debit;
            row["Кредит"] = credit;
            row["Сумма Дт"] = debitAmount.HasValue ? debitAmount.Value : DBNull.Value;
            row["Сумма Кт"] = creditAmount.HasValue ? creditAmount.Value : DBNull.Value;
            row["N докум"] = documentNumber;
            row["Дата"] = date;
            row["Модуль"] = moduleCode;
            table.Rows.Add(row);
        }

        private static void SetReportFieldWidth(Report report, string fieldName, int width, string alignment = "Left", string format = "")
        {
            var field = report.Fields.FirstOrDefault(item => item.FieldName == fieldName);
            if (field == null)
                return;
            field.Width = width;
            field.Alignment = alignment;
            field.Format = format;
        }
        private string ResolveOrganizationTitle(Guid? organizationId, string? organizationName)
        {
            var organization = organizationId.HasValue && organizationId.Value != Guid.Empty
                ? _organizationSelectionItems.FirstOrDefault(item => item.Id == organizationId.Value)
                : null;
            if (organization != null)
                return string.IsNullOrWhiteSpace(organization.FullName) ? organization.Name : organization.FullName;

            var normalized = NormalizeOrganizationName(organizationName);
            return string.IsNullOrWhiteSpace(normalized) ? "Без организации" : normalized;
        }

        private static bool SameOrganization(OrganizationBalanceRow row, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return row.OrganizationId.HasValue && row.OrganizationId.Value == organizationId.Value;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return rowName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool MatchesMovementOrganization(ReconciliationMovementRow movement, Guid? organizationId, string? organizationName)
        {
            if (organizationId.HasValue && organizationId.Value != Guid.Empty)
                return movement.OrganizationId.HasValue && movement.OrganizationId.Value == organizationId.Value;

            var movementName = NormalizeOrganizationName(movement.OrganizationName);
            var filterName = NormalizeOrganizationName(organizationName);
            return movementName.Equals(string.IsNullOrWhiteSpace(filterName) ? "Без организации" : filterName,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool MatchesSelectedOrganization(OrganizationBalanceRow row, Guid? selectedId, string? selectedName)
        {
            if (!selectedId.HasValue || selectedId.Value == Guid.Empty)
                return true;
            if (row.OrganizationId.HasValue && row.OrganizationId.Value == selectedId.Value)
                return true;

            var rowName = NormalizeOrganizationName(row.OrganizationName);
            var filterName = NormalizeOrganizationName(selectedName);
            return !string.IsNullOrWhiteSpace(filterName) &&
                   rowName.Equals(filterName, StringComparison.CurrentCultureIgnoreCase);
        }

        private static string NormalizeOrganizationName(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex >= 0 ? text.Substring(separatorIndex + 3).Trim() : text;
        }

        private static bool AccountInPair(string accountCode, OrganizationBalanceRow pair)
        {
            return SameAccount(accountCode, pair.AccountCode) || SameAccount(accountCode, pair.CounterAccountCode);
        }

        private static bool SameAccount(string left, string right) =>
            left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);

        private static decimal? NonZeroAmount(decimal value) => value == 0m ? null : value;

        private static string BuildMovementDescription(ReconciliationMovementRow movement)
        {
            if (!string.IsNullOrWhiteSpace(movement.Description))
                return movement.Description;
            return string.IsNullOrWhiteSpace(movement.DocumentType) ? "Проводка" : movement.DocumentType;
        }

        private static string BuildDebtSummary(string organizationName, decimal balance)
        {
            if (balance > 0m)
                return $"Задолженность \"{organizationName}\" перед нами составляет {balance:N2}";
            if (balance < 0m)
                return $"Наша задолженность перед \"{organizationName}\" составляет {Math.Abs(balance):N2}";
            return $"Задолженность с \"{organizationName}\" отсутствует.";
        }

        private static string BuildAllOrganizationsDebtSummary(int organizationCount, decimal balance)
        {
            if (balance > 0m)
                return $"Сформировано актов по организациям: {organizationCount}. Итоговая задолженность организаций перед нами составляет {balance:N2}";
            if (balance < 0m)
                return $"Сформировано актов по организациям: {organizationCount}. Итоговая наша задолженность перед организациями составляет {Math.Abs(balance):N2}";
            return $"Сформировано актов по организациям: {organizationCount}. Итоговая задолженность отсутствует.";
        }

        private static bool HasFoxProTemplate(Report report) =>
            !string.IsNullOrWhiteSpace(report.Template) &&
            (string.Equals(report.SourceFormat, "FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(report.ReportType, "FoxProLayout", StringComparison.OrdinalIgnoreCase));

        private bool ShouldUseFoxProExcelLayout(Report report) =>
            HasFoxProTemplate(report) &&
            string.Equals(GetSelectedReportOutputFormat(), "ExcelFoxPro", StringComparison.OrdinalIgnoreCase);

        private string GetSelectedReportOutputFormat() =>
            (ReportOutputFormatCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ExcelNative";

        private static bool TryReadGuid(Dictionary<string, object> row, out Guid id, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!TryGetReportValue(row, out var value, key) || value == null || value == DBNull.Value)
                    continue;
                if (value is Guid guid)
                {
                    id = guid;
                    return true;
                }
                if (Guid.TryParse(value.ToString(), out id))
                    return true;
            }

            id = Guid.Empty;
            return false;
        }

        private static bool ReadReportBoolValue(object? value, bool defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;
            if (value is bool boolValue)
                return boolValue;
            if (bool.TryParse(value.ToString(), out var parsedBool))
                return parsedBool;
            if (int.TryParse(value.ToString(), out var intValue))
                return intValue != 0;
            var text = value.ToString()?.Trim() ?? string.Empty;
            if (text.Equals("да", StringComparison.OrdinalIgnoreCase) || text.Equals("истина", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.Equals("нет", StringComparison.OrdinalIgnoreCase) || text.Equals("ложь", StringComparison.OrdinalIgnoreCase))
                return false;
            return defaultValue;
        }

        private static decimal ReadDbDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;
            if (value is decimal decimalValue)
                return decimalValue;
            if (value is double doubleValue)
                return Convert.ToDecimal(doubleValue);
            if (value is float floatValue)
                return Convert.ToDecimal(floatValue);
            if (value is int intValue)
                return intValue;
            if (value is long longValue)
                return longValue;
            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                   decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out parsed)
                ? parsed
                : 0m;
        }
        private sealed class OrganizationSelectionItem
        {
            public Guid? Id { get; init; }
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public bool IsActive { get; init; } = true;
            public bool IsPrimary { get; init; }
            public bool IsAll => !Id.HasValue || Id.Value == Guid.Empty;
            public string DisplayName
            {
                get
                {
                    if (IsAll)
                        return "Все организации";
                    var name = string.IsNullOrWhiteSpace(Name) ? FullName : Name;
                    var display = string.IsNullOrWhiteSpace(Code) ? name : $"{Code} - {name}";
                    return IsActive ? display : $"{display} (неактивна)";
                }
            }

            public static OrganizationSelectionItem CreateAll() => new()
            {
                Id = Guid.Empty,
                Name = "Все организации"
            };
        }
        private sealed class ReconciliationMovementRow
        {
            public DateTime Date { get; init; }
            public Guid? OrganizationId { get; init; }
            public string OrganizationName { get; init; } = string.Empty;
            public string DocumentNumber { get; init; } = string.Empty;
            public string DocumentType { get; init; } = string.Empty;
            public string ModuleCode { get; init; } = string.Empty;
            public string DebitAccount { get; init; } = string.Empty;
            public string CreditAccount { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public string Description { get; init; } = string.Empty;
        }
        private sealed class ReconciliationReportVariant
        {
            public Guid? ReportId { get; init; }
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public bool IsStandard { get; init; }
        }

        private sealed class PaymentOrderReportRow
        {
            public DateTime Date { get; init; }
            public string Number { get; init; } = string.Empty;
            public string OrderType { get; init; } = string.Empty;
            public string OurAccount { get; init; } = string.Empty;
            public string CorrespondentAccount { get; init; } = string.Empty;
            public string Organization { get; init; } = string.Empty;
            public decimal Amount { get; init; }
            public string Currency { get; init; } = string.Empty;
            public decimal AmountCurrency { get; init; }
            public decimal ExchangeRate { get; init; }
            public string PaymentClassification { get; init; } = string.Empty;
            public string Purpose { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
            public bool IsPosted { get; init; }

            public bool IsIncoming =>
                OrderType.Contains("вход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("приход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("поступ", StringComparison.OrdinalIgnoreCase);

            public bool IsOutgoing =>
                OrderType.Contains("исход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("расход", StringComparison.OrdinalIgnoreCase) ||
                OrderType.Contains("оплат", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(DataTable, Report)> BuildGeneralLedgerAsync(int year)
        {
            var ledger = await _balanceService.GetGeneralLedgerAsync(year);
            var table = new DataTable("Главная книга");
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сальдо нач. Дт", typeof(decimal));
            table.Columns.Add("Сальдо нач. Кт", typeof(decimal));
            for (var month = 1; month <= 12; month++)
            {
                table.Columns.Add($"{month:00} Дт", typeof(decimal));
                table.Columns.Add($"{month:00} Кт", typeof(decimal));
            }
            table.Columns.Add("Год Дт", typeof(decimal));
            table.Columns.Add("Год Кт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Дт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Кт", typeof(decimal));

            foreach (var item in ledger)
            {
                var values = new object[table.Columns.Count];
                values[0] = item.AccountCode;
                values[1] = item.AccountName;
                values[2] = item.OpeningDebit;
                values[3] = item.OpeningCredit;
                for (var month = 1; month <= 12; month++)
                {
                    values[2 + month * 2] = item.MonthlyTurnoverDebit[month];
                    values[3 + month * 2] = item.MonthlyTurnoverCredit[month];
                }
                values[28] = item.YearTurnoverDebit;
                values[29] = item.YearTurnoverCredit;
                values[30] = item.ClosingDebit;
                values[31] = item.ClosingCredit;
                table.Rows.Add(values);
            }

            AddTotalsRow(table, 2);
            return (table, CreateReport(table, $"Главная книга за {year} год", true));
        }

        private async Task<(DataTable, Report)> BuildFinancialPositionAsync(DateTime date)
        {
            var balance = await _balanceService.GetEnterpriseBalanceAsync(date);
            var table = new DataTable("Баланс предприятия");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in balance.Assets)
                table.Rows.Add("Активы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Активы", string.Empty, "Итого активы", balance.TotalAssets);
            foreach (var item in balance.Liabilities)
                table.Rows.Add("Обязательства", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Обязательства", string.Empty, "Итого обязательства", balance.TotalLiabilities);
            foreach (var item in balance.Equity)
                table.Rows.Add("Капитал", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Капитал", string.Empty, "Итого капитал", balance.TotalEquity);
            table.Rows.Add("Контроль", string.Empty, "Расхождение баланса", balance.Difference);

            var report = CreateReport(table, $"Баланс предприятия на {date:dd.MM.yyyy}", false);
            report.SummaryText = balance.IsBalanced
                ? "Контроль пройден: активы равны обязательствам и капиталу."
                : $"Контроль не пройден. Расхождение: {balance.Difference:N2}";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildFinancialResultsAsync(DateTime start, DateTime end)
        {
            var results = await _balanceService.GetFinancialResultsAsync(start, end);
            var table = new DataTable("Финансовые результаты");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Наименование", typeof(string));
            table.Columns.Add("Сумма", typeof(decimal));

            foreach (var item in results.Income)
                table.Rows.Add("Доходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Доходы", string.Empty, "Итого доходы", results.TotalIncome);
            foreach (var item in results.Expenses)
                table.Rows.Add("Расходы", item.AccountCode, item.AccountName, item.Amount);
            table.Rows.Add("Расходы", string.Empty, "Итого расходы", results.TotalExpenses);
            table.Rows.Add("Результат", string.Empty,
                results.ProfitOrLoss >= 0 ? "Прибыль" : "Убыток", results.ProfitOrLoss);

            return (table, CreateReport(table,
                $"Финансовые результаты за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", false));
        }

        private async Task<(DataTable, Report)> BuildPurchaseSalesJournalAsync(DateTime start, DateTime end)
        {
            var entries = await _balanceService.GetPurchaseSalesJournalAsync(start, end);
            var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
            var utcEnd = DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc);
            var savedEntries = await _context.TaxJournalRecords.AsNoTracking()
                .Where(record => record.Date >= utcStart && record.Date < utcEnd)
                .OrderBy(record => record.Date).ThenBy(record => record.DocumentNumber).ToListAsync();
            var table = new DataTable("Журнал закупок и продаж");
            table.Columns.Add("Раздел", typeof(string));
            table.Columns.Add("Дата", typeof(DateTime));
            table.Columns.Add("Номер", typeof(string));
            table.Columns.Add("Документ", typeof(string));
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Модуль", typeof(string));
            table.Columns.Add("Бланк", typeof(string));
            table.Columns.Add("Ставка налога", typeof(string));
            table.Columns.Add("Сумма без налога", typeof(decimal));
            table.Columns.Add("НДС", typeof(decimal));
            table.Columns.Add("Налог с продаж", typeof(decimal));
            table.Columns.Add("Налог", typeof(decimal));
            table.Columns.Add("Всего", typeof(decimal));
            table.Columns.Add("Проведен", typeof(string));
            table.Columns.Add("Примечание", typeof(string));

            if (savedEntries.Count > 0)
            {
                foreach (var entry in savedEntries)
                    table.Rows.Add(LocalizationService.DisplayValue(entry.JournalType), entry.Date,
                        entry.DocumentNumber, entry.DocumentType, entry.Organization, string.Empty, string.Empty, entry.TaxType,
                        entry.AmountWithoutTax, 0m, 0m, entry.TaxAmount, entry.TotalAmount, LocalizationService.DisplayValue(true), string.Empty);
            }
            else
            {
                foreach (var entry in entries)
                    table.Rows.Add(entry.Section, entry.Date, entry.DocumentNumber, entry.DocumentType,
                        entry.Organization, entry.ModuleCode, entry.TaxBlankNumber, entry.TaxType, entry.AmountWithoutTax,
                        entry.VatAmount, entry.SalesTaxAmount, entry.VatAmount + entry.SalesTaxAmount, entry.Amount,
                        LocalizationService.DisplayValue(entry.IsPosted), entry.Note);
            }

            var report = CreateReport(table,
                $"Журнал закупок и продаж за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", true);
            report.ShowGrandTotal = true;
            var amountField = report.Fields.First(field => field.FieldName == "Всего");
            amountField.AggregateType = "Sum";
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildOrganizationBalancesAsync(DateTime start, DateTime end)
        {
            var calculation = await _organizationBalanceService.CalculateAsync(start, end);
            var table = new DataTable("Сальдо по организациям");
            table.Columns.Add("Организация", typeof(string));
            table.Columns.Add("Вид расчета", typeof(string));
            table.Columns.Add("Счет", typeof(string));
            table.Columns.Add("Счет корр.", typeof(string));
            table.Columns.Add("Сальдо нач. Дт", typeof(decimal));
            table.Columns.Add("Сальдо нач. Кт", typeof(decimal));
            table.Columns.Add("Оборот Дт", typeof(decimal));
            table.Columns.Add("Оборот Кт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Дт", typeof(decimal));
            table.Columns.Add("Сальдо кон. Кт", typeof(decimal));
            table.Columns.Add("Сальдо", typeof(decimal));
            table.Columns.Add("Модуль", typeof(string));

            var rowsToAdd = calculation.Rows.AsEnumerable();
            if (_selectedOrganizationId.HasValue && _selectedOrganizationId.Value != Guid.Empty)
                rowsToAdd = rowsToAdd.Where(row => MatchesSelectedOrganization(row, _selectedOrganizationId, _selectedOrganizationName));

            foreach (var row in rowsToAdd)
            {
                table.Rows.Add(
                    row.OrganizationName,
                    row.IsOrganizationTotal ? "ИТОГО" : row.AccountPairName,
                    row.AccountCode,
                    row.CounterAccountCode,
                    row.OpeningDebit,
                    row.OpeningCredit,
                    row.TurnoverDebit,
                    row.TurnoverCredit,
                    row.ClosingDebit,
                    row.ClosingCredit,
                    row.Balance,
                    row.ModuleCode);
            }

            var titleSuffix = _selectedOrganizationId.HasValue
                ? $", фильтр: {_selectedOrganizationName}"
                : string.Empty;
            var report = CreateReport(table,
                $"Сальдо по организациям за {start:dd.MM.yyyy} - {end:dd.MM.yyyy}{titleSuffix}", true);
            report.SummaryText = calculation.Warnings.Count == 0
                ? "Расчет выполнен по активным парам счетов справочника авансовых платежей."
                : string.Join(Environment.NewLine, calculation.Warnings);
            return (table, report);
        }

        private async Task<(DataTable, Report)> BuildPeriodCollectionAsync(DateTime start, DateTime end)
        {
            var collection = await _balanceService.CollectPeriodInformationAsync(start, end);
            var balances = await _balanceService.GetTurnoverBalanceAsync(start, end);
            var table = new DataTable("Сбор информации за периода");
            table.Columns.Add("Показатель", typeof(string));
            table.Columns.Add("Всего", typeof(int));
            table.Columns.Add("Проведено", typeof(int));
            table.Columns.Add("Не проведено", typeof(int));
            table.Columns.Add("Сумма документов", typeof(decimal));
            table.Columns.Add("Дебет", typeof(decimal));
            table.Columns.Add("Кредит", typeof(decimal));
            table.Columns.Add("Разница", typeof(decimal));

            foreach (var item in collection.Documents)
                table.Rows.Add(item.DocumentType, item.DocumentCount, item.PostedCount, item.UnpostedCount, item.Amount, 0m, 0m, 0m);
            var openingDebit = balances.Sum(item => item.OpeningDebit);
            var openingCredit = balances.Sum(item => item.OpeningCredit);
            var closingDebit = balances.Sum(item => item.ClosingDebit);
            var closingCredit = balances.Sum(item => item.ClosingCredit);
            table.Rows.Add("КОНТРОЛЬ НАЧАЛЬНОГО САЛЬДО", 0, 0, 0, 0m,
                openingDebit, openingCredit, openingDebit - openingCredit);
            table.Rows.Add("БУХГАЛТЕРСКИЕ ПРОВОДКИ", collection.PostingCount,
                collection.PostingCount, 0, collection.DebitTurnover,
                collection.DebitTurnover, collection.CreditTurnover, collection.Difference);
            table.Rows.Add("КОНТРОЛЬ КАРТОЧЕК ОС", collection.Warnings.Count,
                0, collection.Warnings.Count, 0m, 0m, 0m, 0m);
            table.Rows.Add("КОНТРОЛЬ КОНЕЧНОГО САЛЬДО", 0, 0, 0, 0m,
                closingDebit, closingCredit, closingDebit - closingCredit);

            var report = CreateReport(table,
                $"Сбор информации за периода {start:dd.MM.yyyy} - {end:dd.MM.yyyy}", false);
            var isBalanced = collection.IsBalanced && Math.Abs(openingDebit - openingCredit) < 0.01m &&
                             Math.Abs(closingDebit - closingCredit) < 0.01m;
            report.SummaryText = isBalanced
                ? "Контроль пройден: начальное сальдо, обороты и конечное сальдо сбалансированы."
                : "Контроль не пройден: обнаружено расхождение дебета и кредита.";
            if (collection.Warnings.Count > 0)
                report.SummaryText += Environment.NewLine + string.Join(Environment.NewLine, collection.Warnings);
            return (table, report);
        }

        private static Report CreateReport(DataTable table, string title, bool landscape)
        {
            var report = new Report
            {
                Name = title,
                TitleText = title,
                PageOrientation = landscape ? "Landscape" : "Portrait",
                FontName = "Arial",
                FontSize = table.Columns.Count > 12 ? 7 : 9,
                ShowGrandTotal = false,
                ShowGridLines = true,
                AlternateRowColors = true
            };

            var order = 1;
            foreach (DataColumn column in table.Columns)
            {
                report.Fields.Add(new ReportField
                {
                    FieldName = column.ColumnName,
                    DisplayName = column.ColumnName,
                    Order = order++,
                    Width = column.DataType == typeof(string) ? 160 : 85,
                    IsVisible = true
                });
            }
            return report;
        }

        private static void AddTotalsRow(DataTable table, int firstNumericColumn)
        {
            var row = table.NewRow();
            row[0] = "ИТОГО";
            for (var index = firstNumericColumn; index < table.Columns.Count; index++)
                row[index] = table.Rows.Cast<DataRow>().Sum(item => Convert.ToDecimal(item[index]));
            table.Rows.Add(row);
        }

        private void OnOpenReportClick(object sender, RoutedEventArgs e)
        {
            var format = GetSelectedReportOutputFormat();
            if (string.Equals(format, "TaxExcel", StringComparison.OrdinalIgnoreCase))
            {
                OnExportVatClick(sender, e);
                return;
            }

            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "Отчет",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.Equals(format, "PdfFoxPro", StringComparison.OrdinalIgnoreCase))
            {
                OnExportFrxPdfClick(sender, e);
                return;
            }

            OnExportExcelClick(sender, e);
        }

        private static Report CloneReportForBackgroundPreview(Report source)
        {
            var report = new Report
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                DataSourceType = source.DataSourceType,
                DataSourceId = source.DataSourceId,
                ReportType = source.ReportType,
                Template = source.Template,
                Settings = source.Settings,
                Icon = source.Icon,
                Code = source.Code,
                IsActive = source.IsActive,
                IsPrintForm = source.IsPrintForm,
                IsDefault = source.IsDefault,
                SourceFormat = source.SourceFormat,
                TemplateVersion = source.TemplateVersion,
                Order = source.Order,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt,
                PageTitle = source.PageTitle,
                PageOrientation = source.PageOrientation,
                PageWidth = source.PageWidth,
                PageHeight = source.PageHeight,
                LeftMargin = source.LeftMargin,
                RightMargin = source.RightMargin,
                TopMargin = source.TopMargin,
                BottomMargin = source.BottomMargin,
                FontName = source.FontName,
                FontSize = source.FontSize,
                ShowHeader = source.ShowHeader,
                ShowFooter = source.ShowFooter,
                ShowPageNumbers = source.ShowPageNumbers,
                ShowGridLines = source.ShowGridLines,
                AlternateRowColor = source.AlternateRowColor,
                HeaderTitle = source.HeaderTitle,
                HeaderSubtitle = source.HeaderSubtitle,
                HeaderLogo = source.HeaderLogo,
                HeaderText = source.HeaderText,
                FooterText = source.FooterText,
                FooterTotalText = source.FooterTotalText,
                FooterSignature = source.FooterSignature,
                TitleText = source.TitleText,
                SubtitleText = source.SubtitleText,
                SummaryText = source.SummaryText,
                AlternateRowColors = source.AlternateRowColors,
                ShowGrandTotal = source.ShowGrandTotal,
                HeaderColor = source.HeaderColor
            };

            report.Fields = source.Fields.Select(field => new ReportField
            {
                Id = field.Id,
                ReportId = field.ReportId,
                FieldName = field.FieldName,
                DisplayName = field.DisplayName,
                AggregateType = field.AggregateType,
                Order = field.Order,
                Width = field.Width,
                Alignment = field.Alignment,
                Format = field.Format,
                IsVisible = field.IsVisible
            }).ToList();

            report.ElementMappings = source.ElementMappings.Select(mapping => new ReportElementMapping
            {
                Id = mapping.Id,
                ReportId = mapping.ReportId,
                ElementOrder = mapping.ElementOrder,
                ElementType = mapping.ElementType,
                ElementText = mapping.ElementText,
                ElementExpression = mapping.ElementExpression,
                BandType = mapping.BandType,
                Left = mapping.Left,
                Top = mapping.Top,
                Width = mapping.Width,
                Height = mapping.Height,
                FontName = mapping.FontName,
                FontSize = mapping.FontSize,
                Bold = mapping.Bold,
                Italic = mapping.Italic,
                Alignment = mapping.Alignment,
                Order = mapping.Order,
                MappedFieldName = mapping.MappedFieldName,
                MappedDisplayName = mapping.MappedDisplayName,
                DataSource = mapping.DataSource,
                FormatString = mapping.FormatString,
                IsVisible = mapping.IsVisible,
                CustomText = mapping.CustomText
            }).ToList();

            return report;
        }

        private async void OnExportFrxPdfClick(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentReport == null)
            {
                MessageBox.Show("Сначала сформируйте отчет.", "PDF по FRX-макету",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!HasFoxProTemplate(_currentReport))
            {
                MessageBox.Show(
                    "Для выбранного отчета не подключен FRX-шаблон. Выберите вариант акта сверки с FRX в конфигураторе.",
                    "PDF по FRX-макету", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsEnabled = false;
                StatusText.Text = "Открытие PDF по старому FRX-макету FoxPro...";

                var dataSnapshot = _currentData.Copy();
                var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                var ruleService = new FoxProReportFieldRuleService(_context);
                await ruleService.SeedDefaultRulesAsync();
                var rules = await ruleService.GetRulesAsync(includeInactive: false);
                var printFormService = new PrintFormService(_context);
                var pdfTask = Task.Run(() => printFormService.ExportReportTemplatePreview(dataSnapshot, reportSnapshot, rules));
                var completedTask = await Task.WhenAny(pdfTask, Task.Delay(TimeSpan.FromSeconds(20)));
                if (completedTask != pdfTask)
                {
                    _ = pdfTask.ContinueWith(task =>
                    {
                        if (task.Exception != null)
                            System.Diagnostics.Debug.WriteLine(task.Exception.GetBaseException().Message);
                    }, TaskContinuationOptions.OnlyOnFaulted);

                    StatusText.Text = "FRX PDF обрабатывается слишком долго.";
                    MessageBox.Show(
                        "FRX-макет обрабатывается слишком долго. Операция остановлена, приложение не будет ждать бесконечно.",
                        "PDF по FRX-макету", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var outputPath = BuildTemporaryReportPath($"{_currentReport.Name}_FRX", "pdf");
                File.WriteAllBytes(outputPath, await pdfTask);
                OpenGeneratedFile(outputPath);
                StatusText.Text = $"PDF по FRX-макету открыт: {Path.GetFileName(outputPath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия PDF по FRX";
                MessageBox.Show($"Ошибка открытия PDF по FRX-макету: {ex.Message}", "PDF по FRX-макету",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }
        private async void OnExportExcelClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentData == null || _currentReport == null)
                {
                    MessageBox.Show("Сначала сформируйте отчет.", "Excel",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var requestedFoxProLayout = string.Equals(GetSelectedReportOutputFormat(), "ExcelFoxPro", StringComparison.OrdinalIgnoreCase);
                if (requestedFoxProLayout && !HasFoxProTemplate(_currentReport))
                {
                    MessageBox.Show(
                        "Для выбранного отчета не подключен FRX-шаблон. Выберите вариант акта сверки с FRX в конфигураторе или откройте 'Excel таблица'.",
                        "Excel по FRX-макету", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                IsEnabled = false;
                var useFoxProExcelLayout = requestedFoxProLayout;
                StatusText.Text = useFoxProExcelLayout
                    ? "Открытие Excel по старому FRX-макету FoxPro..."
                    : "Открытие Excel-таблицы...";

                var excelBaseName = useFoxProExcelLayout ? $"{_currentReport.Name}_FRX" : _currentReport.Name;
                var outputPath = BuildTemporaryExcelPath(excelBaseName, "xlsx");
                byte[] excelBytes;
                if (useFoxProExcelLayout)
                {
                    var dataSnapshot = _currentData.Copy();
                    var reportSnapshot = CloneReportForBackgroundPreview(_currentReport);
                    var ruleService = new FoxProReportFieldRuleService(_context);
                    await ruleService.SeedDefaultRulesAsync();
                    var rules = await ruleService.GetRulesAsync(includeInactive: false);
                    var printFormService = new PrintFormService(_context);
                    excelBytes = await Task.Run(() =>
                        printFormService.ExportReportTemplateExcel(dataSnapshot, reportSnapshot, rules));
                }
                else
                {
                    excelBytes = await Task.Run(() => _reportService.ExportToExcel(_currentData, _currentReport));
                }

                File.WriteAllBytes(outputPath, excelBytes);
                OpenGeneratedFile(outputPath);
                StatusText.Text = useFoxProExcelLayout
                    ? $"Excel по FRX-макету открыт: {Path.GetFileName(outputPath)}"
                    : $"Excel-таблица открыта: {Path.GetFileName(outputPath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия Excel";
                MessageBox.Show($"Ошибка открытия бухгалтерского отчета в Excel: {ex.Message}", "Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void OnExportVatClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                if (start.Year != end.Year || start.Month != end.Month)
                {
                    if (MessageBox.Show(
                            "Для налогового отчета обычно выбирается один календарный месяц. Продолжить выгрузку для выбранного диапазона?",
                            "Tax Excel",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                var templateService = new RegulatedReportTemplateService(_context);
                var activeTemplate = await templateService.GetActiveTemplateAsync(VatTaxReportExportService.OfficialTemplateCode);
                var templateExtension = activeTemplate?.FileExtension;
                if (string.IsNullOrWhiteSpace(templateExtension))
                    templateExtension = Path.GetExtension(activeTemplate?.OriginalFileName);

                var defaultExtension = string.Equals(templateExtension, ".xls", StringComparison.OrdinalIgnoreCase)
                    ? "xls"
                    : "xlsx";
                var outputPath = BuildTemporaryExcelPath($"STI062_tax_{start:yyyyMM}", defaultExtension);

                IsEnabled = false;
                StatusText.Text = activeTemplate == null
                    ? "Формирование внутреннего налогового Excel-отчета..."
                    : $"Заполнение шаблона {activeTemplate.Code} из базы данных...";

                var exportService = new VatTaxReportExportService(_context);
                var exportResult = await exportService.ExportMonthlyVatReportAsync(start, end, outputPath);
                OpenGeneratedFile(outputPath);

                StatusText.Text = exportResult.UsedOfficialTemplate
                    ? $"Налоговый Excel открыт. Шаблон: {exportResult.TemplateCode}."
                    : "Налоговый Excel открыт. Активный официальный шаблон не найден, использован внутренний отчет.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка открытия налогового Excel";
                MessageBox.Show($"Ошибка открытия налогового Excel: {ex.Message}", "Tax Excel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private static string BuildTemporaryExcelPath(string baseName, string extension) =>
            BuildTemporaryReportPath(baseName, extension);

        private static string BuildTemporaryReportPath(string baseName, string extension)
        {
            var safeName = SanitizeFileName(baseName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "report";

            var normalizedExtension = (extension ?? "xlsx").Trim().TrimStart('.');
            if (string.IsNullOrWhiteSpace(normalizedExtension))
                normalizedExtension = "xlsx";

            var directory = Path.Combine(Path.GetTempPath(), "BIS.ERP", "ReportOutput");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalizedExtension}");
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = (value ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(chars).Trim(' ', '.', '_');
        }

        private static void OpenGeneratedFile(string filePath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
    }
}
