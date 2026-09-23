using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CatalogDataView : UserControl
    {
        private readonly MetadataObject _catalog;
        private readonly MetadataService _metadataService;
        private DataTable _dataTable;
        private Dictionary<string, Dictionary<string, string>> _referenceCache;
        private Dictionary<string, MetadataObject> _catalogsDict;
        private readonly Dictionary<Guid, Dictionary<string, object>> _rawRowsById = new();
        private static readonly IValueConverter AccountTypeConverter = new AccountTypeDisplayConverter();
        private static readonly IValueConverter YesNoConverter = new BooleanYesNoDisplayConverter();
        private static readonly IValueConverter LinkFlagConverter = new BooleanPlusDisplayConverter();
        private static readonly IValueConverter ClosingModuleConverter = new ClosingModuleDisplayConverter();
        private static readonly IValueConverter PrintModeConverter = new ChartOfAccountsModeDisplayConverter("Признак печати");
        private static readonly IValueConverter BalanceModeConverter = new ChartOfAccountsModeDisplayConverter("Сохранять остатки");

        private sealed class CatalogDetailFieldView
        {
            public string Field { get; init; } = string.Empty;
            public string Value { get; init; } = string.Empty;
        }

        public CatalogDataView(MetadataObject catalog, MetadataService metadataService)
        {
            InitializeComponent();
            _catalog = catalog;
            _metadataService = metadataService;
            _referenceCache = new Dictionary<string, Dictionary<string, string>>();

            TitleText.Text = $"{catalog.Icon} {catalog.Name}";
            DescriptionText.Text = catalog.Description;
            SearchPanel.Visibility = Visibility.Visible;
            SearchBox.ToolTip = IsChartOfAccountsCatalog
                ? "Поиск по коду и наименованию счета"
                : "Поиск по всем видимым колонкам справочника";
            ImportDbfButton.Visibility = CanImportDbf ? Visibility.Visible : Visibility.Collapsed;
            ImportDbfButton.Content = IsPaymentClassificationCatalog
                ? "📥 Загрузить классификацию"
                : "📥 Загрузить DBF";

            if (IsFixedAssetsCatalog)
            {
                FixedAssetDatePanel.Visibility = Visibility.Visible;
                FixedAssetAsOfDatePicker.SelectedDate = DateTime.Today;
                SearchBox.ToolTip = "Поиск по карточкам основных средств на выбранную дату";
            }

            if (IsCurrencyRatesCatalog)
            {
                CurrencyRateFilterPanel.Visibility = Visibility.Visible;
                var today = DateTime.Today;
                CurrencyRatePeriodStartPicker.SelectedDate = new DateTime(today.Year, today.Month, 1);
                CurrencyRatePeriodEndPicker.SelectedDate = today;
                SearchBox.ToolTip = "Поиск по истории курсов валют";
            }
        }

        private bool IsChartOfAccountsCatalog =>
            string.Equals(_catalog.Name, "План счетов", StringComparison.OrdinalIgnoreCase);

        private bool IsPaymentClassificationCatalog =>
            string.Equals(_catalog.Name, "Классификация платежей", StringComparison.OrdinalIgnoreCase);

        private bool CanImportDbf => IsChartOfAccountsCatalog || IsPaymentClassificationCatalog;

        private bool IsAdvancePaymentsCatalog =>
            string.Equals(_catalog.Name, "Пары счетов", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.Name, "Авансовые платежи", StringComparison.OrdinalIgnoreCase);

        private bool IsFixedAssetsCatalog =>
            string.Equals(_catalog.Name, "Основные средства", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_assets", StringComparison.OrdinalIgnoreCase);

        private bool IsCurrencyRatesCatalog =>
            string.Equals(_catalog.TableName, "catalog_currency_rates", StringComparison.OrdinalIgnoreCase);

        private bool IsCurrenciesCatalog =>
            string.Equals(_catalog.Name, "Справочник валют", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_currencies", StringComparison.OrdinalIgnoreCase);

        private bool IsOrganizationsCatalog =>
            string.Equals(_catalog.Name, "Организации", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_organizations", StringComparison.OrdinalIgnoreCase);

        private bool IsBankAccountsCatalog =>
            string.Equals(_catalog.Name, "Расчетные счета организаций", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_catalog.TableName, "catalog_bank_accounts", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Служебные колонки «Дата создания»/«Дата изменения» (добавляются кодом
        /// в LoadData(), а не метаданными) не показываем в основных гридах этих
        /// справочников — иначе они занимают место и дублируют информацию карточки.
        /// </summary>
        private bool HideServiceDateColumnsInMainGrid =>
            IsFixedAssetsCatalog || IsOrganizationsCatalog || IsBankAccountsCatalog;

        private MetadataObject? _bankAccountsCatalog;
        private Dictionary<string, Dictionary<string, string>>? _bankAccountsReferenceMaps;
        private int _organizationAccountsRequestId;
        private string _currentOrganizationId = string.Empty;
        private List<Dictionary<string, object>> _organizationAccountRows = new();

        // Панель организации: стартовая высота и минимально допустимая высота.
        // Стартовая высота подобрана так, чтобы грид «Расчётные счета организации»
        // показывал несколько строк сразу (см. дефект: грид счетов показывал одну
        // строку, хотя счетов у организации несколько).
        private const double OrganizationPanelDefaultHeight = 380;
        private const double OrganizationPanelMinHeight = 240;
        private const double OrganizationListMinHeight = 120;
        private bool _organizationPanelHeightInitialized;

        private Border? OrganizationDetailsPanelControl => FindName("OrganizationDetailsPanel") as Border;

        private DataGrid? OrganizationDetailsGridControl => FindName("OrganizationDetailsGrid") as DataGrid;

        private TextBlock? OrganizationListHeaderControl => FindName("OrganizationListHeaderText") as TextBlock;

        /// <summary>
        /// Заголовок группы с таблицей: для справочника «Организации» — имя выбранной
        /// организации, для остальных справочников — название справочника.
        /// </summary>
        private void UpdateOrganizationListHeader(DataRowView? selectedRow)
        {
            var header = OrganizationListHeaderControl;
            if (header == null)
                return;

            if (!IsOrganizationsCatalog)
            {
                header.Text = _catalog.Name;
                return;
            }

            var name = selectedRow == null ? string.Empty : GetOrganizationRowName(selectedRow);
            header.Text = string.IsNullOrWhiteSpace(name)
                ? "Организации"
                : $"Организация: {name}";
        }

        private static string GetOrganizationRowName(DataRowView row)
        {
            foreach (var key in new[] { "Наименование", "name", "Полное наименование", "full_name", "Код", "code" })
            {
                if (!row.Row.Table.Columns.Contains(key))
                    continue;

                var value = Convert.ToString(row[key], CultureInfo.CurrentCulture)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private void SetOrganizationDetailsSource(IEnumerable<CatalogDetailFieldView>? details)
        {
            var grid = OrganizationDetailsGridControl;
            if (grid == null)
                return;

            var fields = details?.ToList();
            if (fields == null || fields.Count == 0)
            {
                grid.ItemsSource = null;
                return;
            }

            // Обычный грид: колонки — реквизиты организации, строка — их значения
            var table = new DataTable();
            foreach (var field in fields)
            {
                var columnName = GetOrganizationDetailColumnName(field.Field);
                if (!table.Columns.Contains(columnName))
                    table.Columns.Add(columnName, typeof(string));
            }

            var row = table.NewRow();
            foreach (var field in fields)
            {
                var columnName = GetOrganizationDetailColumnName(field.Field);
                if (table.Columns.Contains(columnName))
                    row[columnName] = field.Value ?? string.Empty;
            }

            table.Rows.Add(row);
            grid.ItemsSource = table.DefaultView;
        }

        private static string GetOrganizationDetailColumnName(string? fieldName) =>
            string.IsNullOrWhiteSpace(fieldName) ? "Реквизит" : fieldName.Trim();

        private void SetOrganizationDetailsVisibility(Visibility visibility)
        {
            if (OrganizationDetailsPanelControl is { } panel)
                panel.Visibility = visibility;

            var isVisible = visibility == Visibility.Visible;

            if (FindName("OrganizationPanelSplitter") is GridSplitter splitter)
                splitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            // Строки 3 (сплиттер) и 4 (панель организации) корневой сетки:
            // при показе панель получает высоту, при скрытии — строки схлопываются
            if (RootLayout.RowDefinitions.Count > 4)
            {
                var panelRow = RootLayout.RowDefinitions[4];

                RootLayout.RowDefinitions[3].Height = new GridLength(isVisible ? 6 : 0);

                if (!isVisible)
                {
                    panelRow.Height = new GridLength(0);
                }
                else if (!_organizationPanelHeightInitialized)
                {
                    // Первый показ — стартовая высота панели, ограниченная окном.
                    _organizationPanelHeightInitialized = true;

                    var panelHeight = Math.Min(
                        OrganizationPanelDefaultHeight,
                        GetOrganizationPanelAvailableHeight());

                    panelRow.Height = new GridLength(
                        Math.Max(OrganizationPanelMinHeight, panelHeight),
                        GridUnitType.Pixel);
                }
                // При последующих показах высоту панели НЕ перезаписываем:
                // её задаёт пользователь сплиттером (строка 3 корневой сетки).
            }
        }

        /// <summary>
        /// Максимальная высота панели организации, при которой строка списка
        /// организаций (минимум 120px) и статус-бар остаются видимыми целиком.
        /// </summary>
        private double GetOrganizationPanelAvailableHeight()
        {
            var total = RootLayout.ActualHeight;
            if (total <= 0 || double.IsNaN(total) || double.IsInfinity(total))
                return OrganizationPanelDefaultHeight;

            var fixedRows = 0d;
            for (var i = 0; i < RootLayout.RowDefinitions.Count; i++)
            {
                // 2 — звёздная строка списка организаций (учитываем её минимум),
                // 4 — сама панель организации.
                if (i == 2 || i == 4)
                    continue;

                var row = RootLayout.RowDefinitions[i];
                var actual = row.ActualHeight;
                if (actual > 0 && !double.IsNaN(actual) && !double.IsInfinity(actual))
                    fixedRows += actual;
                else if (row.Height.IsAbsolute)
                    fixedRows += row.Height.Value;
            }

            return Math.Max(OrganizationPanelMinHeight, total - fixedRows - OrganizationListMinHeight);
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = DataGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
            if (IsCurrencyRatesCatalog && ActivateRateButton != null)
                ActivateRateButton.IsEnabled = hasSelection;

            UpdateOrganizationDetailsPanel();
        }

        private async Task LoadCurrencyRateFilterAsync()
        {
            var items = new List<BIS.ERP.Models.ReferenceItem>
            {
                new() { Id = Guid.Empty, DisplayName = "Все валюты" }
            };

            if (_catalogsDict != null &&
                _catalogsDict.TryGetValue("Справочник валют", out var currencyCatalog))
            {
                var rows = await _metadataService.GetCatalogDataAsync(currencyCatalog.Id);
                foreach (var row in rows)
                {
                    if (!Guid.TryParse(GetRowId(row).ToString(), out var id))
                        continue;

                    var code = Convert.ToString(row.GetValueOrDefault("Код") ?? row.GetValueOrDefault("code")) ?? string.Empty;
                    var name = Convert.ToString(row.GetValueOrDefault("Наименование") ?? row.GetValueOrDefault("name")) ?? string.Empty;
                    var displayName = string.IsNullOrWhiteSpace(code) ? name : $"{code} - {name}";
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = id.ToString();
                    items.Add(new BIS.ERP.Models.ReferenceItem { Id = id, DisplayName = displayName });
                }
            }

            var previousSelection = TryReadGuid(CurrencyRateFilterCombo.SelectedValue, out var selectedCurrencyId)
                ? selectedCurrencyId
                : Guid.Empty;
            CurrencyRateFilterCombo.ItemsSource = items;
            CurrencyRateFilterCombo.SelectedValue = items.Any(item => item.Id == previousSelection)
                ? previousSelection
                : Guid.Empty;
        }

        private void CurrencyRateFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrencyRateFilter();
        }

        private void CurrencyRatePeriod_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrencyRateFilter();
        }

        private void CurrencyRateFindButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateCurrencyRatePeriod())
                return;

            ApplyCurrencyRateFilter();
        }

        private async void OnUpdateNbkrRatesClick(object sender, RoutedEventArgs e)
        {
            if (!IsCurrencyRatesCatalog || !ValidateCurrencyRatePeriod())
                return;

            var startDate = CurrencyRatePeriodStartPicker.SelectedDate!.Value.Date;
            var endDate = CurrencyRatePeriodEndPicker.SelectedDate!.Value.Date;

            try
            {
                UpdateNbkrRatesButton.IsEnabled = false;
                StatusText.Text = $"Загрузка курсов НБКР за {startDate:dd/MM/yyyy}-{endDate:dd/MM/yyyy}...";

                var results = await _metadataService.ImportOfficialCurrencyRatesAsync(startDate, endDate);
                var imported = results.Sum(item => item.Imported);
                var skipped = results.Sum(item => item.Skipped);

                await LoadData();
                StatusText.Text = $"Загружено курсов НБКР: {imported}; пропущено валют: {skipped}";
                MessageBox.Show(
                    $"Загрузка курсов НБКР завершена.\nЗагружено строк: {imported}\nПропущено валют: {skipped}",
                    "Курсы валют",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка обновления курсов НБКР";
                MessageBox.Show($"Не удалось обновить курсы НБКР: {ex.Message}", "Курсы валют",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateNbkrRatesButton.IsEnabled = true;
            }
        }

        private bool ValidateCurrencyRatePeriod()
        {
            var startDate = CurrencyRatePeriodStartPicker.SelectedDate?.Date;
            var endDate = CurrencyRatePeriodEndPicker.SelectedDate?.Date;
            if (!startDate.HasValue || !endDate.HasValue)
            {
                MessageBox.Show("Укажите период загрузки курсов валют.", "Курсы валют",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (startDate.Value > endDate.Value)
            {
                MessageBox.Show("Дата начала периода больше даты окончания.", "Курсы валют",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void ApplyCurrencyRateFilter()
        {
            if (!IsCurrencyRatesCatalog || _dataTable == null || !_dataTable.Columns.Contains("CurrencyFilterFlag"))
                return;

            var selectedId = TryReadGuid(CurrencyRateFilterCombo.SelectedValue, out var selectedCurrencyId)
                ? selectedCurrencyId
                : Guid.Empty;
            var selectedDisplayName = (CurrencyRateFilterCombo.SelectedItem as BIS.ERP.Models.ReferenceItem)?.DisplayName ?? string.Empty;
            var selectedCode = NormalizeReferenceKey(selectedDisplayName);
            var startDate = CurrencyRatePeriodStartPicker?.SelectedDate?.Date;
            var endDate = CurrencyRatePeriodEndPicker?.SelectedDate?.Date;

            foreach (DataRow row in _dataTable.Rows)
            {
                var rowId = TryReadGuid(row["Id"], out var parsedId) ? parsedId : Guid.Empty;
                var matchesCurrency = selectedId == Guid.Empty;
                var matchesDate = !startDate.HasValue && !endDate.HasValue;

                if (_rawRowsById.TryGetValue(rowId, out var raw))
                {
                    if (!matchesCurrency)
                        matchesCurrency = MatchesCurrencyRateCurrency(raw, row, selectedId, selectedDisplayName, selectedCode);

                    if (TryGetCurrencyRateDate(raw, row, out var rateDate))
                    {
                        matchesDate = (!startDate.HasValue || rateDate.Date >= startDate.Value) &&
                                      (!endDate.HasValue || rateDate.Date <= endDate.Value);
                    }
                }
                else
                {
                    if (!matchesCurrency)
                        matchesCurrency = MatchesCurrencyRateCurrency(null, row, selectedId, selectedDisplayName, selectedCode);

                    if (TryGetCurrencyRateDate(null, row, out var rateDate))
                    {
                        matchesDate = (!startDate.HasValue || rateDate.Date >= startDate.Value) &&
                                      (!endDate.HasValue || rateDate.Date <= endDate.Value);
                    }
                }

                row["CurrencyFilterFlag"] = matchesCurrency && matchesDate;
            }

            ApplySearchFilter();
        }
        private static bool TryReadGuid(object value, out Guid guid)
        {
            if (value is Guid typedGuid)
            {
                guid = typedGuid;
                return true;
            }

            return Guid.TryParse(Convert.ToString(value), out guid);
        }

        private static bool TryGetDate(IReadOnlyDictionary<string, object> row, string key, out DateTime date)
        {
            date = default;
            if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                return false;

            if (value is DateTime dateTime)
            {
                date = dateTime;
                return true;
            }

            return DateTime.TryParse(value.ToString(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ||
                   DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static bool TryGetCurrencyRateDate(
            IReadOnlyDictionary<string, object>? raw,
            DataRow row,
            out DateTime date)
        {
            date = default;

            foreach (var key in new[] { "rate_date", "Дата", "date" })
            {
                if (raw != null && TryGetDate(raw, key, out date))
                    return true;

                if (row.Table.Columns.Contains(key) && TryParseDateValue(row[key], out date))
                    return true;
            }

            return false;
        }

        private static bool MatchesCurrencyRateCurrency(
            IReadOnlyDictionary<string, object>? raw,
            DataRow row,
            Guid selectedId,
            string selectedDisplayName,
            string selectedCode)
        {
            foreach (var key in new[] { "currency_id", "Валюта", "currency" })
            {
                if (raw != null &&
                    raw.TryGetValue(key, out var rawValue) &&
                    IsCurrencyValueMatch(rawValue, selectedId, selectedDisplayName, selectedCode))
                {
                    return true;
                }

                if (row.Table.Columns.Contains(key) &&
                    IsCurrencyValueMatch(row[key], selectedId, selectedDisplayName, selectedCode))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCurrencyValueMatch(
            object value,
            Guid selectedId,
            string selectedDisplayName,
            string selectedCode)
        {
            if (value == null || value == DBNull.Value)
                return false;

            if (TryReadGuid(value, out var currencyId) && currencyId == selectedId)
                return true;

            var text = value.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.Equals(selectedId.ToString(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(selectedDisplayName) &&
                text.Equals(selectedDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedText = NormalizeReferenceKey(text);
            return !string.IsNullOrWhiteSpace(selectedCode) &&
                   (normalizedText.Equals(selectedCode, StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith($"{selectedCode} - ", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseDateValue(object value, out DateTime date)
        {
            date = default;
            if (value == null || value == DBNull.Value)
                return false;

            if (value is DateTime dateTime)
            {
                date = dateTime;
                return true;
            }

            return DateTime.TryParse(value.ToString(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ||
                   DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private async void OnActivateRateClick(object sender, RoutedEventArgs e)
        {
            if (DataGrid.SelectedItem is not DataRowView rowView || rowView.Row["Id"] is not Guid rowId)
                return;
            if (!_rawRowsById.TryGetValue(rowId, out var raw) ||
                !TryGetCurrencyId(raw, out var currencyId))
                return;

            try
            {
                StatusText.Text = "Применение курса...";
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var connection = context.Database.GetDbConnection();
                var shouldClose = connection.State != System.Data.ConnectionState.Open;
                if (shouldClose)
                    await connection.OpenAsync();

                try
                {
                    var tableName = QuoteSqlIdentifier(_catalog.TableName);

                    await using (var deactivate = connection.CreateCommand())
                    {
                        deactivate.CommandText = $@"UPDATE {tableName}
                            SET ""is_active"" = false, ""UpdatedAt"" = NOW()
                            WHERE currency_id::text = @currencyId AND ""Id"" <> @rowId;";
                        deactivate.Parameters.Add(new Npgsql.NpgsqlParameter("currencyId", currencyId.ToString()));
                        deactivate.Parameters.Add(new Npgsql.NpgsqlParameter("rowId", rowId));
                        await deactivate.ExecuteNonQueryAsync();
                    }

                    await using (var activate = connection.CreateCommand())
                    {
                        activate.CommandText = $@"UPDATE {tableName}
                            SET ""is_active"" = true, ""UpdatedAt"" = NOW()
                            WHERE ""Id"" = @rowId;";
                        activate.Parameters.Add(new Npgsql.NpgsqlParameter("rowId", rowId));
                        await activate.ExecuteNonQueryAsync();
                    }
                }
                finally
                {
                    if (shouldClose)
                        await connection.CloseAsync();
                }

                StatusText.Text = "Курс сделан активным.";
                await LoadData();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка";
                MessageBox.Show($"Не удалось сделать курс активным: {ex.Message}", "Курсы валют",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string QuoteSqlIdentifier(string identifier) =>
            "\"" + (identifier ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private async Task<Dictionary<Guid, decimal>> LoadLatestCurrencyRatesAsync(IEnumerable<Dictionary<string, object>> currencyRows)
        {
            var rates = new Dictionary<Guid, decimal>();
            foreach (var row in currencyRows)
            {
                var rowId = GetRowId(row);
                if (rowId == Guid.Empty)
                    continue;

                var latestRate = await _metadataService.GetLatestCurrencyRateAsync(rowId);
                if (latestRate?.Rate > 0m)
                    rates[rowId] = latestRate.Rate;
            }

            return rates;
        }

        private static object GetCatalogFieldValue(IReadOnlyDictionary<string, object> row, MetadataField field)
        {
            if (row.TryGetValue(field.Name, out var byName))
                return byName ?? DBNull.Value;

            if (!string.IsNullOrWhiteSpace(field.DbColumnName) && row.TryGetValue(field.DbColumnName, out var byColumn))
                return byColumn ?? DBNull.Value;

            return DBNull.Value;
        }

        private static bool IsCurrencyRateField(MetadataField field)
        {
            return string.Equals(field.Name, "Курс", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(field.DbColumnName, "rate", StringComparison.OrdinalIgnoreCase);
        }
        private static bool TryGetCurrencyId(IReadOnlyDictionary<string, object> raw, out Guid currencyId)
        {
            foreach (var key in new[] { "currency_id", "Валюта", "currency" })
            {
                if (raw.TryGetValue(key, out var value) && TryReadGuid(value, out currencyId))
                    return true;
            }

            currencyId = Guid.Empty;
            return false;
        }
        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка данных...";
                ProgressText.Text = "⏳ Загрузка...";

                var data = await _metadataService.GetCatalogDataAsync(_catalog.Id);
                if (IsAdvancePaymentsCatalog)
                    data = SortRowsByNumericCode(data).ToList();
                if (IsCurrencyRatesCatalog)
                    data = data
                        .OrderByDescending(row => row.GetValueOrDefault("rate_date") as DateTime? ?? DateTime.MinValue)
                        .ToList();

                _rawRowsById.Clear();

                // Загружаем все справочники один раз
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                _catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                // Та же защита, что и в DynamicDocumentWorkView: строим таблицу
                // локально и привязываем атомарно (лог 09:49 — рассинхрон ItemsGrid).
                var dataTable = new DataTable();
                dataTable.TableName = _catalog.Name;
                var visibleFields = GetUniqueCatalogFields();

                // Добавляем колонки
                dataTable.Columns.Add("Id", typeof(Guid));
                foreach (var field in visibleFields)
                {
                    var columnType = GetColumnType(field.FieldType);
                    dataTable.Columns.Add(field.Name, columnType);
                }

                dataTable.Columns.Add("Дата создания", typeof(DateTime));
                dataTable.Columns.Add("Дата изменения", typeof(DateTime));
                if (IsCurrencyRatesCatalog)
                    dataTable.Columns.Add("CurrencyFilterFlag", typeof(bool));

                // Загружаем данные справочников для подстановки имен (универсально)
                await LoadReferenceDataAsync();

                if (IsCurrencyRatesCatalog)
                    await LoadCurrencyRateFilterAsync();

                var latestCurrencyRates = IsCurrenciesCatalog
                    ? await LoadLatestCurrencyRatesAsync(data)
                    : new Dictionary<Guid, decimal>();

                // Добавляем строки
                foreach (var row in data)
                {
                    var dataRow = dataTable.NewRow();
                    var rowId = GetRowId(row);
                    dataRow["Id"] = rowId;

                    var rawCopy = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in row)
                        rawCopy[pair.Key] = pair.Value;
                    _rawRowsById[rowId] = rawCopy;

                    foreach (var field in visibleFields)
                    {
                        var rawValue = GetCatalogFieldValue(row, field);
                        if (IsCurrenciesCatalog &&
                            IsCurrencyRateField(field) &&
                            latestCurrencyRates.TryGetValue(rowId, out var latestRate))
                        {
                            rawValue = latestRate;
                            rawCopy[field.Name] = latestRate;
                        }

                        // Если поле ссылается на справочник - подставляем DisplayName
                        if (!string.IsNullOrEmpty(field.ReferenceCatalog) && _referenceCache.TryGetValue(field.Name, out var dict))
                        {
                            var key = NormalizeReferenceKey(rawValue);
                            dataRow[field.Name] = !string.IsNullOrWhiteSpace(key) && dict.TryGetValue(key, out var displayValue)
                                ? displayValue
                                : rawValue;
                        }
                        else
                        {
                            dataRow[field.Name] = rawValue;
                        }
                    }

                    dataRow["Дата создания"] = row.ContainsKey("CreatedAt") ? row["CreatedAt"] : DateTime.Now;
                    dataRow["Дата изменения"] = row.ContainsKey("UpdatedAt") ? row["UpdatedAt"] : DateTime.Now;
                    if (IsCurrencyRatesCatalog)
                        dataRow["CurrencyFilterFlag"] = true;

                    dataTable.Rows.Add(dataRow);
                }

                // Атомарная привязка: публикуем готовую таблицу одной заменой.
                _dataTable = dataTable;
                DataGrid.ItemsSource = null;

                // Настраиваем DataGrid
                DataGrid.Columns.Clear();

                foreach (var field in visibleFields)
                {
                    DataGrid.Columns.Add(CreateDataGridColumn(field));
                }

                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = CreateColumnHeader("Дата создания"),
                    Binding = new System.Windows.Data.Binding("Дата создания"),
                    Width = IsChartOfAccountsCatalog || IsAdvancePaymentsCatalog ? 110 : 130,
                    Visibility = HideServiceDateColumnsInMainGrid ? Visibility.Collapsed : Visibility.Visible,
                    ElementStyle = CreateCellTextStyle()
                });

                DataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = CreateColumnHeader("Дата изменения"),
                    Binding = new System.Windows.Data.Binding("Дата изменения"),
                    Width = IsChartOfAccountsCatalog || IsAdvancePaymentsCatalog ? 110 : 130,
                    Visibility = HideServiceDateColumnsInMainGrid ? Visibility.Collapsed : Visibility.Visible,
                    ElementStyle = CreateCellTextStyle()
                });

                DataGrid.ItemsSource = _dataTable.DefaultView;

                ApplyOrganizationGridPresentation();
                UpdateOrganizationDetailsPanel();

                ApplyCurrencyRateFilter();

                StatusText.Text = $"📊 Загружено записей: {_dataTable.Rows.Count}";
                ProgressText.Text = "";
                EditButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                ProgressText.Text = "";
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private static IEnumerable<Dictionary<string, object>> SortRowsByNumericCode(IEnumerable<Dictionary<string, object>> rows)
        {
            return rows
                .OrderBy(row => TryGetNumericCode(row, out var _) ? 0 : 1)
                .ThenBy(row => TryGetNumericCode(row, out var code) ? code : int.MaxValue)
                .ThenBy(row => GetCodeText(row), StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryGetNumericCode(IReadOnlyDictionary<string, object> row, out int code)
        {
            code = 0;
            return int.TryParse(GetCodeText(row), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        }

        private static string GetCodeText(IReadOnlyDictionary<string, object> row)
        {
            if (row.TryGetValue("Код", out var localized) && localized != null && localized != DBNull.Value)
                return localized.ToString()?.Trim() ?? string.Empty;

            if (row.TryGetValue("code", out var raw) && raw != null && raw != DBNull.Value)
                return raw.ToString()?.Trim() ?? string.Empty;

            return string.Empty;
        }
        /// <summary>
        /// Универсальная загрузка данных для всех Reference полей
        /// </summary>
        private async Task LoadReferenceDataAsync()
        {
            _referenceCache.Clear();

            var fields = GetReferenceFieldsForDisplay().ToList();
            if (_catalogsDict == null || fields.Count == 0)
                return;

            // ПАРАЛЛЕЛЬНЫЙ GetCatalogDataAsync НА ОБЩЕМ DbContext ДАВАЛ
            // "A command is already in progress" (лог 09:43). Грузим строго
            // последовательно — это справочные данные для сетки.
            // Группируем поля по целевому каталогу и фильтруем отсутствующие справочники
            var groups = fields
                .GroupBy(f => f.ReferenceCatalog, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    _catalogsDict.TryGetValue(g.Key, out var refCatalog);
                    return (CatalogName: g.Key, RefCatalog: refCatalog, Fields: g.ToList());
                })
                .Where(t => t.RefCatalog != null)
                .ToList();

            // Загружаем данные справочников последовательно и формируем словарь отображения
            foreach (var grp in groups)
            {
                var refData = await _metadataService.GetCatalogDataAsync(grp.RefCatalog.Id);

                // Для каждого поля, ссылочного на этот каталог, формируем свой словарь отображения
                var perFieldDicts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in grp.Fields)
                {
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in refData)
                    {
                        var displayValue = GetDisplayValueFromRow(row, field, grp.CatalogName);
                        foreach (var key in GetReferenceLookupKeys(row))
                        {
                            if (!dict.ContainsKey(key))
                                dict[key] = displayValue;
                        }
                    }

                    perFieldDicts[field.Name] = dict;
                }

                foreach (var kv in perFieldDicts)
                    _referenceCache[kv.Key] = kv.Value;
            }
        }

        private IEnumerable<MetadataField> GetReferenceFieldsForDisplay()
        {
            return _catalog.Fields
                .Where(f => !string.IsNullOrEmpty(f.ReferenceCatalog))
                .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(f => f.Order).First());
        }
        private List<MetadataField> GetUniqueCatalogFields()
        {
            var allowedColumns = GetAllowedCatalogColumns();
            var result = new List<MetadataField>();
            var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in _catalog.Fields.OrderBy(f => f.Order))
            {
                if (allowedColumns != null &&
                    (string.IsNullOrWhiteSpace(field.DbColumnName) || !allowedColumns.Contains(field.DbColumnName)))
                {
                    continue;
                }

                var hasDuplicateColumn = !string.IsNullOrWhiteSpace(field.DbColumnName) &&
                                         !usedColumns.Add(field.DbColumnName);
                var hasDuplicateName = !string.IsNullOrWhiteSpace(field.Name) &&
                                       !usedNames.Add(field.Name);

                if (hasDuplicateColumn || hasDuplicateName)
                    continue;

                result.Add(field);
            }

            if (IsFixedAssetsCatalog)
            {
                result = result
                    .OrderBy(GetFixedAssetListOrder)
                    .ThenBy(field => field.Order <= 0 ? int.MaxValue : field.Order)
                    .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return result;
        }

        private static int GetFixedAssetListOrder(MetadataField field)
        {
            var key = field.DbColumnName?.Trim();
            return key switch
            {
                "code" => 1,
                "name" => 2,
                "inventory_number" => 3,
                "manufacture_year" => 4,
                "asset_group" => 5,
                "acquisition_date" => 6,
                "depreciation_start_date" => 7,
                "disposal_date" => 8,
                "carrying_amount" => 9,
                "initial_cost" => 10,
                "site_id" => 11,
                _ => 1000
            };
        }

        private HashSet<string>? GetAllowedCatalogColumns()
        {
            if (IsChartOfAccountsCatalog)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "account_type",
                    "description",
                    "level",
                    "is_active",
                    "closing_module_code",
                    "analytic_group",
                    "print_mode",
                    "balance_mode",
                    "link_organizations",
                    "link_employees",
                    "link_currencies",
                    "link_personal_accounts",
                    "link_materials",
                    "link_construction_objects",
                    "link_sites",
                    "tax_code",
                    "account_currency_id"
                };
            }

            if (IsAdvancePaymentsCatalog)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "use_organizations",
                    "use_personnel",
                    "use_currency",
                    "module_code",
                    "debit_account",
                    "credit_account",
                    "use_settlements",
                    "generate_postings",
                    "use_internal_settlements",
                    "is_active",
                    "description"
                };
            }

            if (IsFixedAssetsCatalog)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "code",
                    "name",
                    "inventory_number",
                    "manufacture_year",
                    "asset_group",
                    "acquisition_date",
                    "depreciation_start_date",
                    "disposal_date",
                    "carrying_amount",
                    "initial_cost",
                    "site_id"
                };
            }

            return null;
        }
        private static IEnumerable<string> GetReferenceLookupKeys(Dictionary<string, object> row)
        {
            foreach (var keyName in new[] { "Id", "Код", "code", "Code", "Счет", "account_code" })
            {
                var key = NormalizeReferenceKey(row.GetValueOrDefault(keyName));
                if (!string.IsNullOrWhiteSpace(key))
                    yield return key;
            }
        }

        private static string NormalizeReferenceKey(object value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            var text = value.ToString()?.Trim() ?? string.Empty;
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        /// <summary>
        /// Универсальное получение отображаемого значения по шаблону из метаданных
        /// </summary>
        private string GetDisplayValueFromRow(Dictionary<string, object> row, MetadataField field, string catalogName)
        {
            // Если есть шаблон - используем его
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var fld in fieldNames)
                {
                    var fieldKey = fld.Trim();
                    var value = "";

                    if (row.ContainsKey(fieldKey))
                        value = row[fieldKey]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.Replace(" ", "_")))
                        value = row[fieldKey.Replace(" ", "_")]?.ToString() ?? "";
                    else if (row.ContainsKey(fieldKey.ToLower()))
                        value = row[fieldKey.ToLower()]?.ToString() ?? "";

                    result = result.Replace($"{{{fieldKey}}}", value);
                    result = result.Replace($"{{{fieldKey.Replace(" ", "_")}}}", value);
                }

                return result;
            }

            // === БЕЗ ШАБЛОНА - автоопределение ===

            // Для поля "Табельный номер" в справочнике МОЛ
            if (field.Name == "Табельный номер" && catalogName == "Сотрудники (Списочный состав)")
            {
                if (row.ContainsKey("Табельный номер"))
                    return row["Табельный номер"]?.ToString() ?? "";
                if (row.ContainsKey("personnel_number"))
                    return row["personnel_number"]?.ToString() ?? "";
                if (row.ContainsKey("Код"))
                    return row["Код"]?.ToString() ?? "";
                if (row.ContainsKey("code"))
                    return row["code"]?.ToString() ?? "";
            }

            // Приоритеты полей для отображения
            var priorityFields = new[]
            {
                "Наименование", "name", "Name",
                "ФИО", "full_name", "FullName",
                "Наименование вида", "Наименование категории",
                "site_name", "Наименование участка",
                "Код", "code", "Code"
            };

            foreach (var priority in priorityFields)
            {
                if (row.ContainsKey(priority) && row[priority] != null)
                    return row[priority].ToString();
            }

            // Ищем любое строковое поле
            foreach (var key in row.Keys)
            {
                if (key != "Id" && key != "CreatedAt" && key != "UpdatedAt" && row[key] is string strVal && !string.IsNullOrEmpty(strVal))
                    return strVal;
            }

            return row.ContainsKey("Id") ? row["Id"].ToString() : "Без имени";
        }

        private Type GetColumnType(string fieldType)
        {
            return fieldType switch
            {
                "Int" => typeof(int),
                "Decimal" => typeof(decimal),
                "DateTime" => typeof(DateTime),
                "Bool" => typeof(bool),
                _ => typeof(string)
            };
        }

        private DataGridTextColumn CreateDataGridColumn(MetadataField field)
        {
            return new DataGridTextColumn
            {
                Header = CreateColumnHeader(field.Name),
                Binding = CreateColumnBinding(field),
                Width = GetColumnWidth(field),
                MinWidth = GetColumnMinWidth(field),
                Visibility = IsCatalogFieldVisibleInMainGrid(field) ? Visibility.Visible : Visibility.Collapsed,
                CanUserResize = true,
                ElementStyle = CreateCellTextStyle(GetColumnTextAlignment(field))
            };
        }

        private bool IsCatalogFieldVisibleInMainGrid(MetadataField field)
        {
            if (IsOrganizationsCatalog)
                return IsOrganizationMainColumn(field.Name, field.DbColumnName);

            // Справочник «Расчетные счета организаций»: служебные реквизиты (даты,
            // «Активен») скрываем тем же фильтром, что и панель «Дополнительные
            // поля организации» — метод ShouldShowOrganizationDetailColumn общий.
            if (IsBankAccountsCatalog)
                return !IsCatalogServiceColumn(field.Name) &&
                       !IsCatalogServiceColumn(field.DbColumnName);

            return true;
        }

        private static bool TryGetOrganizationColumnWidth(MetadataField field, out double width)
        {
            var key = GetOrganizationColumnKey(field);
            width = key switch
            {
                "код" or "code" => 80,
                "наименование" or "name" => 360,
                "полноенаименование" or "полноенаменование" or "fullname" => 460,
                "инн" or "inn" or "taxid" or "taxnumber" => 170,
                _ => 0
            };

            return width > 0;
        }

        private static bool TryGetOrganizationColumnMinWidth(MetadataField field, out double minWidth)
        {
            var key = GetOrganizationColumnKey(field);
            minWidth = key switch
            {
                "код" or "code" => 64,
                "наименование" or "name" => 240,
                "полноенаименование" or "полноенаменование" or "fullname" => 300,
                "инн" or "inn" or "taxid" or "taxnumber" => 120,
                _ => 0
            };

            return minWidth > 0;
        }

        private static string GetOrganizationColumnKey(MetadataField field)
        {
            var byName = NormalizeOrganizationColumnKey(field.Name);
            if (IsOrganizationMainKey(byName))
                return byName;

            return NormalizeOrganizationColumnKey(field.DbColumnName);
        }

        private static bool IsOrganizationMainColumn(string? fieldName, string? dbColumnName = null)
        {
            return IsOrganizationMainKey(NormalizeOrganizationColumnKey(fieldName)) ||
                   IsOrganizationMainKey(NormalizeOrganizationColumnKey(dbColumnName));
        }

        private static bool IsOrganizationMainKey(string key)
        {
            return key is "код" or "code" or
                   "наименование" or "name" or
                   "полноенаименование" or "полноенаменование" or "fullname" or
                   "инн" or "inn" or "taxid" or "taxnumber";
        }

        private static string NormalizeOrganizationColumnKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim().ToLowerInvariant().Replace('ё', 'е');
            return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        }

        private void ApplyOrganizationGridPresentation()
        {
            UpdateOrganizationListHeader(DataGrid.SelectedItem as DataRowView);

            if (!IsOrganizationsCatalog)
            {
                SetOrganizationDetailsVisibility(Visibility.Collapsed);
                SetOrganizationDetailsSource(null);
                return;
            }

            foreach (var column in DataGrid.Columns)
                column.CanUserResize = true;
        }

        private void UpdateOrganizationDetailsPanel()
        {
            if (!IsOrganizationsCatalog)
            {
                ClearOrganizationAccounts();
                UpdateOrganizationListHeader(null);
                return;
            }

            if (DataGrid.SelectedItem is not DataRowView selectedRow || _dataTable == null)
            {
                SetOrganizationDetailsSource(null);
                SetOrganizationDetailsVisibility(Visibility.Collapsed);
                ClearOrganizationAccounts();
                UpdateOrganizationListHeader(null);
                return;
            }

            var details = _dataTable.Columns
                .Cast<DataColumn>()
                .Where(column => ShouldShowOrganizationDetailColumn(column.ColumnName))
                .Select(column => new CatalogDetailFieldView
                {
                    Field = column.ColumnName,
                    Value = FormatOrganizationDetailValue(selectedRow[column.ColumnName])
                })
                .ToList();

            SetOrganizationDetailsSource(details);
            SetOrganizationDetailsVisibility(details.Count > 0 ? Visibility.Visible : Visibility.Collapsed);

            UpdateOrganizationListHeader(selectedRow);
            _ = RefreshOrganizationAccountsAsync(selectedRow);
        }

        private DataGrid? OrganizationAccountsGridControl => FindName("OrganizationAccountsGrid") as DataGrid;

        private TextBlock? OrganizationAccountsSummaryControl => FindName("OrganizationAccountsSummary") as TextBlock;

        private void ClearOrganizationAccounts()
        {
            _organizationAccountsRequestId++;
            _currentOrganizationId = string.Empty;
            _organizationAccountRows.Clear();
            if (OrganizationAccountsGridControl is { } grid)
                grid.ItemsSource = null;
            if (OrganizationAccountsSummaryControl is { } summary)
                summary.Text = string.Empty;
            UpdateOrganizationAccountButtons();
        }

        /// <summary>
        /// Показывает расчётные счета выбранной организации из справочника
        /// «Расчетные счета организаций» (связь через реквизит «Организация»).
        /// </summary>
        private async Task RefreshOrganizationAccountsAsync(DataRowView selectedRow)
        {
            _currentOrganizationId = selectedRow.Row.Table.Columns.Contains("Id")
                ? Convert.ToString(selectedRow["Id"], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty
                : string.Empty;

            await RefreshOrganizationAccountsAsync(_currentOrganizationId);
        }

        private async Task RefreshOrganizationAccountsAsync(string organizationId)
        {
            var requestId = ++_organizationAccountsRequestId;
            var grid = OrganizationAccountsGridControl;
            var summary = OrganizationAccountsSummaryControl;

            if (grid == null)
                return;

            grid.ItemsSource = null;
            _organizationAccountRows.Clear();
            UpdateOrganizationAccountButtons();

            if (string.IsNullOrWhiteSpace(organizationId))
            {
                if (summary != null)
                    summary.Text = string.Empty;
                return;
            }

            try
            {
                var accountsCatalog = await GetBankAccountsCatalogAsync();
                if (accountsCatalog == null)
                {
                    if (summary != null)
                        summary.Text = "Справочник «Расчетные счета организаций» не найден";
                    return;
                }

                var rows = await _metadataService.GetCatalogDataAsync(accountsCatalog.Id);
                var referenceMaps = await GetBankAccountsReferenceMapsAsync(accountsCatalog);

                // Пока грузились данные, пользователь мог выбрать другую организацию
                if (requestId != _organizationAccountsRequestId)
                    return;

                var organizationAccounts = rows
                    .Where(row => IsOrganizationAccountRow(row, organizationId))
                    .ToList();

                _organizationAccountRows = organizationAccounts;

                var table = new DataTable();
                table.Columns.Add("Id", typeof(Guid));
                table.Columns.Add("Счёт", typeof(string));
                table.Columns.Add("Банк", typeof(string));
                table.Columns.Add("БИК", typeof(string));
                table.Columns.Add("Валюта", typeof(string));

                foreach (var row in organizationAccounts)
                {
                    table.Rows.Add(
                        TryReadGuid(GetAccountRawValue(row, "Id"), out var accountId) ? accountId : Guid.Empty,
                        GetAccountCellText(row, "Счет", "account_number"),
                        GetAccountReferenceText(referenceMaps, "Банк", "bank_id", row),
                        GetAccountCellText(row, "БИК", "bic"),
                        GetAccountReferenceText(referenceMaps, "Валюта", "currency_id", row));
                }

                grid.ItemsSource = table.DefaultView;
                UpdateOrganizationAccountButtons();

                if (summary != null)
                {
                    summary.Text = organizationAccounts.Count == 0
                        ? "Счета не добавлены. Добавьте их в справочнике «Расчетные счета организаций»."
                        : $"Всего счетов: {organizationAccounts.Count}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Не удалось загрузить расчётные счета организации: {ex.Message}");
                if (summary != null)
                    summary.Text = "Не удалось загрузить расчётные счета организации";
            }
        }

        private void OrganizationAccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateOrganizationAccountButtons();

        private void UpdateOrganizationAccountButtons()
        {
            var hasSelection = OrganizationAccountsGridControl?.SelectedItem != null;
            if (FindName("OrganizationAccountEditButton") is Button editButton)
                editButton.IsEnabled = hasSelection;
            if (FindName("OrganizationAccountDeleteButton") is Button deleteButton)
                deleteButton.IsEnabled = hasSelection;
        }

        private (Guid Id, Dictionary<string, object>? Row) GetSelectedOrganizationAccount()
        {
            if (OrganizationAccountsGridControl?.SelectedItem is not DataRowView selectedRow)
                return (Guid.Empty, null);

            if (!selectedRow.Row.Table.Columns.Contains("Id") ||
                !TryReadGuid(selectedRow["Id"], out var accountId) ||
                accountId == Guid.Empty)
            {
                return (Guid.Empty, null);
            }

            var raw = _organizationAccountRows.FirstOrDefault(row =>
                TryReadGuid(GetAccountRawValue(row, "Id"), out var id) && id == accountId);

            return (accountId, raw);
        }

        private async void OrganizationAccountAddButton_Click(object sender, RoutedEventArgs e)
        {
            var accountsCatalog = await GetBankAccountsCatalogAsync();
            if (accountsCatalog == null)
            {
                MessageBox.Show("Справочник «Расчетные счета организаций» не найден.", "Расчётные счета",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var initialData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_currentOrganizationId))
            {
                var organizationField = accountsCatalog.Fields.FirstOrDefault(field =>
                    field.DbColumnName.Equals("organization_id", StringComparison.OrdinalIgnoreCase));
                initialData[organizationField?.Name ?? "Организация"] = _currentOrganizationId;
            }

            var dialog = new CatalogItemDialog(accountsCatalog, _metadataService, initialData, isNewRecord: true)
            {
                Owner = Window.GetWindow(this)
            };

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this), dialog, "Добавление: Расчётный счёт") != true)
            {
                return;
            }

            try
            {
                StatusText.Text = "💾 Сохранение...";
                ProgressText.Text = "⏳ Сохранение...";
                await _metadataService.AddCatalogItemAsync(accountsCatalog.Id, dialog.ItemData);
                await RefreshOrganizationAccountsAsync(_currentOrganizationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressText.Text = string.Empty;
                StatusText.Text = "✅ Готово";
            }
        }

        private async void OrganizationAccountEditButton_Click(object sender, RoutedEventArgs e)
        {
            var (accountId, rawRow) = GetSelectedOrganizationAccount();
            if (accountId == Guid.Empty)
                return;

            var accountsCatalog = await GetBankAccountsCatalogAsync();
            if (accountsCatalog == null)
                return;

            var existingData = rawRow != null
                ? new Dictionary<string, object>(rawRow, StringComparer.OrdinalIgnoreCase)
                : null;

            var dialog = new CatalogItemDialog(accountsCatalog, _metadataService, existingData)
            {
                Owner = Window.GetWindow(this)
            };

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this), dialog, "Редактирование: Расчётный счёт") != true)
            {
                return;
            }

            try
            {
                StatusText.Text = "💾 Обновление...";
                ProgressText.Text = "⏳ Обновление...";
                await _metadataService.UpdateDynamicRecordAsync(accountsCatalog.Id, accountId, dialog.ItemData);
                await RefreshOrganizationAccountsAsync(_currentOrganizationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressText.Text = string.Empty;
                StatusText.Text = "✅ Готово";
            }
        }

        private async void OrganizationAccountDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var (accountId, rawRow) = GetSelectedOrganizationAccount();
            if (accountId == Guid.Empty)
                return;

            var accountsCatalog = await GetBankAccountsCatalogAsync();
            if (accountsCatalog == null)
                return;

            var accountNumber = rawRow != null
                ? GetAccountCellText(rawRow, "Счет", "account_number")
                : string.Empty;

            var question = string.IsNullOrWhiteSpace(accountNumber)
                ? "Удалить выбранный расчётный счёт?"
                : $"Удалить расчётный счёт '{accountNumber}'?";

            if (MessageBox.Show($"{question}\nВосстановление будет невозможно!", "Подтверждение удаления",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                StatusText.Text = "🗑️ Удаление...";
                ProgressText.Text = "⏳ Удаление...";
                await _metadataService.DeleteDynamicRecordAsync(accountsCatalog.Id, accountId);
                await RefreshOrganizationAccountsAsync(_currentOrganizationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ProgressText.Text = string.Empty;
                StatusText.Text = "✅ Готово";
            }
        }

        private async Task<MetadataObject?> GetBankAccountsCatalogAsync()
        {
            if (_bankAccountsCatalog != null)
                return _bankAccountsCatalog;

            if (_catalogsDict != null &&
                _catalogsDict.TryGetValue("Расчетные счета организаций", out var fromDictionary))
            {
                _bankAccountsCatalog = fromDictionary;
                return _bankAccountsCatalog;
            }

            _bankAccountsCatalog = await _metadataService.GetCatalogByNameAsync("Расчетные счета организаций");
            return _bankAccountsCatalog;
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> GetBankAccountsReferenceMapsAsync(
            MetadataObject accountsCatalog)
        {
            if (_bankAccountsReferenceMaps != null)
                return _bankAccountsReferenceMaps;

            var maps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var referenceFields = accountsCatalog.Fields
                .Where(field => !string.IsNullOrEmpty(field.ReferenceCatalog))
                .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(field => field.Order).First())
                .ToList();

            foreach (var field in referenceFields)
            {
                MetadataObject? referenceCatalog = null;
                if (_catalogsDict != null)
                    _catalogsDict.TryGetValue(field.ReferenceCatalog!, out referenceCatalog);

                referenceCatalog ??= await _metadataService.GetCatalogByNameAsync(field.ReferenceCatalog!);
                if (referenceCatalog == null)
                    continue;

                var rows = await _metadataService.GetCatalogDataAsync(referenceCatalog.Id);
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    var displayValue = GetDisplayValueFromRow(row, field, referenceCatalog.Name);
                    foreach (var key in GetReferenceLookupKeys(row))
                    {
                        if (!map.ContainsKey(key))
                            map[key] = displayValue;
                    }
                }

                maps[field.Name] = map;
            }

            _bankAccountsReferenceMaps = maps;
            return maps;
        }

        private static bool IsOrganizationAccountRow(Dictionary<string, object> row, string organizationId)
        {
            var rawValue = GetAccountRawValue(row, "organization_id", "Организация", "organization");
            return rawValue != null &&
                   TryReadGuid(rawValue, out var id) &&
                   string.Equals(id.ToString(), organizationId, StringComparison.OrdinalIgnoreCase);
        }

        private static object? GetAccountRawValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value;
            }

            return null;
        }

        private static string GetAccountCellText(Dictionary<string, object> row, params string[] keys) =>
            FormatOrganizationDetailValue(GetAccountRawValue(row, keys));

        private static string GetAccountReferenceText(
            Dictionary<string, Dictionary<string, string>> maps,
            string fieldName,
            string dbColumnName,
            Dictionary<string, object> row)
        {
            if (!maps.TryGetValue(fieldName, out var map))
                return string.Empty;

            var key = NormalizeReferenceKey(GetAccountRawValue(row, fieldName, dbColumnName));
            return !string.IsNullOrWhiteSpace(key) && map.TryGetValue(key, out var displayValue)
                ? displayValue
                : string.Empty;
        }

        /// <summary>
        /// Общий фильтр служебных (технических) колонок справочника: первичный ключ,
        /// служебные флаги UI и системные реквизиты «Дата создания», «Дата изменения»,
        /// «Активен»/«Активна». Применяется и панелью «Дополнительные поля организации»,
        /// и основным гридом справочника «Расчетные счета организаций».
        /// </summary>
        private static bool IsCatalogServiceColumn(string? columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
                return false;

            var name = columnName.Trim();

            return string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "CurrencyFilterFlag", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Дата создания", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Дата изменения", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Активен", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Активна", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "is_active", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Показывать ли колонку в панели «Дополнительные поля организации»:
        /// служебные колонки (см. <see cref="IsCatalogServiceColumn"/>) и основные
        /// реквизиты самой таблицы организации (Код, Наименование, ИНН) не показываем —
        /// они уже есть в основном гриде.
        /// </summary>
        private static bool ShouldShowOrganizationDetailColumn(string columnName)
        {
            if (IsCatalogServiceColumn(columnName))
                return false;

            return !IsOrganizationMainColumn(columnName);
        }

        private static string FormatOrganizationDetailValue(object? value)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            return value switch
            {
                bool flag => flag ? "Да" : "Нет",
                DateTime date => date.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture),
                DateTimeOffset date => date.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture),
                decimal number => number.ToString("N2", CultureInfo.CurrentCulture),
                double number => number.ToString("N2", CultureInfo.CurrentCulture),
                float number => number.ToString("N2", CultureInfo.CurrentCulture),
                _ => Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty
            };
        }
        private object CreateColumnHeader(string fieldName)
        {
            if (!IsChartOfAccountsCatalog && !IsAdvancePaymentsCatalog && !IsFixedAssetsCatalog)
                return fieldName;

            return new TextBlock
            {
                Text = IsFixedAssetsCatalog
                    ? GetFixedAssetColumnHeader(fieldName)
                    : IsAdvancePaymentsCatalog
                        ? GetAdvancePaymentsColumnHeader(fieldName)
                        : GetChartOfAccountsColumnHeader(fieldName),
                ToolTip = fieldName,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 14,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(2, 2, 2, 2)
            };
        }
        private DataGridLength GetColumnWidth(MetadataField field)
        {
            if (IsOrganizationsCatalog && TryGetOrganizationColumnWidth(field, out var organizationWidth))
                return new DataGridLength(organizationWidth, DataGridLengthUnitType.Pixel);

            if (IsAdvancePaymentsCatalog)
            {
                var advanceWidth = field.Name switch
                {
                    "Код" => 56,
                    "Орг" => 48,
                    "Таб №" => 54,
                    "Валюта" => 62,
                    "Остаток брать из модуля" => 78,
                    "Дебет" => 112,
                    "Кредит" => 112,
                    "Участвует во взаиморасчетах" => 74,
                    "Формировать проводки авансовых платежей" => 74,
                    "Участвует во внутренних взаиморасчетах" => 78,
                    "Вид расчета" => 220,
                    "Активен" => 62,
                    _ => field.FieldType == "Bool" ? 56 : 105
                };

                return new DataGridLength(advanceWidth, DataGridLengthUnitType.Pixel);
            }

            if (IsFixedAssetsCatalog)
            {
                var assetWidth = field.DbColumnName switch
                {
                    "code" => 64,
                    "name" => 210,
                    "inventory_number" => 92,
                    "manufacture_year" => 92,
                    "asset_group" => 150,
                    "acquisition_date" => 106,
                    "depreciation_start_date" => 116,
                    "disposal_date" => 106,
                    "carrying_amount" => 128,
                    "initial_cost" => 124,
                    "site_id" => 150,
                    _ => field.FieldType == "Decimal" ? 112 : 120
                };

                return new DataGridLength(assetWidth, DataGridLengthUnitType.Pixel);
            }

            if (!IsChartOfAccountsCatalog)
                return new DataGridLength(1, DataGridLengthUnitType.Star);

            var width = field.Name switch
            {
                "Код" => 70,
                "Наименование" => 165,
                "Тип счета" => 86,
                "Описание" => 130,
                "Уровень" => 42,
                "Активен" => 52,
                "Закрывает модуль" => 70,
                "Группа аналитических статей" => 82,
                "Признак печати" => 64,
                "Сохранять остатки" => 72,
                "Связь с организациями" => 42,
                "Связь со списочным составом" => 42,
                "Связь с валютами" => 42,
                "Связь с лицевыми счетами" => 48,
                "Связь с материалами" => 48,
                "Связь с объектами строительства" => 54,
                "Связь с участками" => 46,
                "Код налога" => 46,
                "Валюта счета" => 82,
                _ => field.FieldType == "Bool" ? 42 : 86
            };

            return new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }
        private double GetColumnMinWidth(MetadataField field)
        {
            if (IsOrganizationsCatalog && TryGetOrganizationColumnMinWidth(field, out var organizationMinWidth))
                return organizationMinWidth;

            if (IsAdvancePaymentsCatalog)
                return field.FieldType == "Bool" ? 40 : 62;

            if (IsFixedAssetsCatalog)
                return field.FieldType == "Bool" ? 40 : 70;

            if (!IsChartOfAccountsCatalog)
                return 88;

            return field.Name switch
            {
                "Код" => 56,
                "Наименование" => 118,
                "Тип счета" => 72,
                "Описание" => 92,
                "Уровень" => 36,
                "Активен" => 44,
                "Закрывает модуль" => 60,
                "Группа аналитических статей" => 70,
                "Признак печати" => 54,
                "Сохранять остатки" => 60,
                "Код налога" => 46,
                "Валюта счета" => 68,
                _ => field.FieldType == "Bool" ? 36 : 50
            };
        }
        private TextAlignment GetColumnTextAlignment(MetadataField field)
        {
            if (IsAdvancePaymentsCatalog)
                return field.FieldType == "Bool" || field.Name == "Код"
                    ? TextAlignment.Center
                    : TextAlignment.Left;

            if (IsFixedAssetsCatalog)
            {
                if (field.FieldType == "Decimal")
                    return TextAlignment.Right;

                return field.FieldType == "DateTime" ||
                       field.DbColumnName is "code" or "inventory_number" or "manufacture_year"
                    ? TextAlignment.Center
                    : TextAlignment.Left;
            }

            if (!IsChartOfAccountsCatalog)
                return TextAlignment.Left;

            return field.FieldType == "Bool" || field.Name == "Уровень"
                ? TextAlignment.Center
                : TextAlignment.Left;
        }
        private Style CreateCellTextStyle(TextAlignment textAlignment = TextAlignment.Left)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(4, 0, 4, 0)));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, textAlignment));
            return style;
        }

        private string GetFixedAssetColumnHeader(string fieldName)
        {
            return fieldName switch
            {
                "Инвентарный номер" => "Инв. номер",
                "Год выпуска оборудования" => "Год\nвыпуска",
                "Группа ОС" => "Группа",
                "Дата начала амортизации" => "Дата нач. износа",
                "Остаточная стоимость" => "Перерасчетная стоимость",
                "Первоначальная стоимость" => "Начальная стоимость",
                "Дата создания" => "Дата\nсоздания",
                "Дата изменения" => "Дата\nизменения",
                _ => fieldName
            };
        }
        private string GetChartOfAccountsColumnHeader(string fieldName)
        {
            return fieldName switch
            {
                "Тип счета" => "Тип\nсчета",
                "Закрывает модуль" => "Закрывает\nмодуль",
                "Группа аналитических статей" => "Группа\nаналитики",
                "Признак печати" => "Признак\nпечати",
                "Сохранять остатки" => "Сохр.\nостатки",
                "Связь с организациями" => "Связь\nс орг.",
                "Связь со списочным составом" => "Таб.\n№",
                "Связь с валютами" => "Связь\nс вал.",
                "Связь с лицевыми счетами" => "Лиц.\nсчета",
                "Связь с материалами" => "Матер.\nучет",
                "Связь с объектами строительства" => "Объекты\nстр-ва",
                "Связь с участками" => "Участки",
                "Код налога" => "Код\nналога",
                "Валюта счета" => "Валюта\nсчета",
                "Дата создания" => "Дата\nсоздания",
                "Дата изменения" => "Дата\nизменения",
                _ => fieldName
            };
        }

        private string GetAdvancePaymentsColumnHeader(string fieldName)
        {
            return fieldName switch
            {
                "Остаток брать из модуля" => "Модуль",
                "Участвует во взаиморасчетах" => "Разн. расч.",
                "Формировать проводки авансовых платежей" => "Анал. плат.",
                "Участвует во внутренних взаиморасчетах" => "Внут. расч.",
                "Дата создания" => "Создан",
                "Дата изменения" => "Изменен",
                _ => fieldName
            };
        }

        private Binding CreateColumnBinding(MetadataField field)
        {
            var binding = new Binding(field.Name);

            if (IsChartOfAccountsCatalog && field.Name == "Тип счета")
            {
                binding.Converter = AccountTypeConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Закрывает модуль")
            {
                binding.Converter = ClosingModuleConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Признак печати")
            {
                binding.Converter = PrintModeConverter;
            }
            else if (IsChartOfAccountsCatalog && field.Name == "Сохранять остатки")
            {
                binding.Converter = BalanceModeConverter;
            }
            else if (IsAdvancePaymentsCatalog && field.FieldType == "Bool" && field.Name != "Активен")
            {
                binding.Converter = LinkFlagConverter;
            }
            else if (field.FieldType == "Bool" && (!IsChartOfAccountsCatalog || field.Name == "Активен"))
            {
                binding.Converter = YesNoConverter;
            }
            else if (IsChartOfAccountsCatalog && field.FieldType == "Bool")
            {
                binding.Converter = LinkFlagConverter;
            }

            if (field.FieldType == "DateTime")
                binding.StringFormat = "dd/MM/yyyy";

            return binding;
        }
        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (_dataTable?.DefaultView == null)
                return;

            var searchText = SearchBox.Text?.Trim();
            if (IsChartOfAccountsCatalog && !string.IsNullOrWhiteSpace(searchText))
            {
                ApplyChartOfAccountsSearchFilter(searchText);
                StatusText.Text = $"🔍 Найдено счетов: {_dataTable.DefaultView.Count}";
                return;
            }

            var filterParts = new List<string>();
            if (IsCurrencyRatesCatalog)
                filterParts.Add("CurrencyFilterFlag = true");
            if (IsFixedAssetsCatalog)
            {
                var dateFilter = BuildFixedAssetAsOfFilter();
                if (!string.IsNullOrWhiteSpace(dateFilter))
                    filterParts.Add(dateFilter);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                if (IsChartOfAccountsCatalog)
                {
                    ApplyChartOfAccountsSearchFilter(searchText);
                    StatusText.Text = $"🔍 Найдено счетов: {_dataTable.DefaultView.Count}";
                    return;
                }

                var genericFilter = BuildGenericSearchFilter(searchText);
                if (!string.IsNullOrWhiteSpace(genericFilter))
                    filterParts.Add($"({genericFilter})");
            }

            _dataTable.DefaultView.RowFilter = string.Join(" AND ", filterParts);

            StatusText.Text = IsFixedAssetsCatalog
                ? $"📊 Карточек ОС на {GetFixedAssetAsOfDate():dd/MM/yyyy}: {_dataTable.DefaultView.Count}"
                : string.IsNullOrWhiteSpace(searchText)
                    ? $"📊 Загружено записей: {_dataTable.Rows.Count}"
                    : $"🔍 Найдено записей: {_dataTable.DefaultView.Count}";
        }

        private string BuildGenericSearchFilter(string searchText)
        {
            var escapedValue = EscapeRowFilterValue(searchText);
            var filterParts = _dataTable.Columns
                .Cast<DataColumn>()
                .Where(column => !column.ColumnName.Equals("Id", StringComparison.OrdinalIgnoreCase))
                .Where(column => !column.ColumnName.StartsWith("__", StringComparison.OrdinalIgnoreCase))
                .Select(column => $"CONVERT([{EscapeColumnName(column.ColumnName)}], 'System.String') LIKE '%{escapedValue}%'")
                .ToList();

            return string.Join(" OR ", filterParts);
        }

        private string? BuildFixedAssetAsOfFilter()
        {
            if (_dataTable == null || !_dataTable.Columns.Contains("Дата приобретения"))
                return null;

            var cutoff = GetFixedAssetAsOfDate().ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            return $"([{EscapeColumnName("Дата приобретения")}] IS NULL OR [{EscapeColumnName("Дата приобретения")}] <= #{cutoff}#)";
        }
        private void ApplyChartOfAccountsSearchFilter(string searchText)
        {
            const string markerColumn = "__search_match";
            if (!_dataTable.Columns.Contains(markerColumn))
                _dataTable.Columns.Add(markerColumn, typeof(bool));

            foreach (DataRow row in _dataTable.Rows)
                row[markerColumn] = MatchesChartOfAccountsSearch(row, searchText);

            _dataTable.DefaultView.RowFilter = $"[{markerColumn}] = true";
        }

        private static bool MatchesChartOfAccountsSearch(DataRow row, string searchText)
        {
            var code = GetRowText(row, "Код", "code");
            var name = GetRowText(row, "Наименование", "name");
            var type = GetRowText(row, "Тип счета", "account_type");
            var digitSearch = ExtractDigits(searchText);

            if (!string.IsNullOrEmpty(digitSearch))
                return ExtractDigits(code).StartsWith(digitSearch, StringComparison.Ordinal);

            return name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   type.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   code.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRowText(DataRow row, params string[] columns)
        {
            foreach (var column in columns)
            {
                if (row.Table.Columns.Contains(column))
                    return row[column]?.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string ExtractDigits(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static bool ContainsDigitsInOrder(string valueDigits, string searchDigits)
        {
            if (string.IsNullOrEmpty(searchDigits))
                return true;

            var searchIndex = 0;
            foreach (var digit in valueDigits)
            {
                if (digit != searchDigits[searchIndex])
                    continue;

                searchIndex++;
                if (searchIndex == searchDigits.Length)
                    return true;
            }

            return false;
        }
        private static string EscapeColumnName(string value) =>
            value.Replace("]", "]]");

        private static string EscapeRowFilterValue(string value)
        {
            return value
                .Replace("'", "''")
                .Replace("[", "[[]")
                .Replace("]", "[]]")
                .Replace("%", "[%]")
                .Replace("*", "[*]");
        }

        private DateTime GetFixedAssetAsOfDate()
        {
            return FixedAssetAsOfDatePicker?.SelectedDate?.Date ?? DateTime.Today;
        }

        private static Guid GetRowId(Dictionary<string, object> row)
        {
            foreach (var key in new[] { "Id", "id" })
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (value is Guid guid)
                    return guid;

                if (Guid.TryParse(value.ToString(), out guid))
                    return guid;
            }

            return Guid.NewGuid();
        }

        private void FixedAssetAsOfDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsFixedAssetsCatalog || _dataTable?.DefaultView == null)
                return;

            ApplySearchFilter();
            DataGrid.Items.Refresh();
        }

        private void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (!IsFixedAssetsCatalog)
                return;

            if (e.Row.Item is not DataRowView view)
                return;

            e.Row.ClearValue(Control.ForegroundProperty);
            e.Row.ClearValue(Control.BackgroundProperty);

            if (!IsFixedAssetDisposedAsOf(view))
                return;

            e.Row.Foreground = Brushes.Gray;
            e.Row.Background = new SolidColorBrush(Color.FromRgb(242, 244, 246));
        }

        private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataGrid.SelectedItem is not DataRowView)
                return;

            if (IsFixedAssetsCatalog)
            {
                OpenFixedAssetDetailsDialog();
                e.Handled = true;
                return;
            }

            OnEditClick(sender, e);
        }

        private void OpenFixedAssetDetailsDialog()
        {
            if (DataGrid.SelectedItem is not DataRowView selectedRow)
                return;

            var id = (Guid)selectedRow["Id"];
            var existingData = GetExistingDataForEdit(id, selectedRow);

            var dialog = new FixedAssetDetailsDialog(
                _catalog,
                _metadataService,
                FixedAssetCardMode.Details,
                existingData,
                id,
                GetFixedAssetAsOfDate())
            {
                Owner = Window.GetWindow(this)
            };

            dialog.ShowDialog();
        }

        private Dictionary<string, string> BuildFixedAssetDisplayValues(
            IReadOnlyDictionary<string, object> record,
            DataRowView selectedRow)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in _catalog.Fields.OrderBy(f => f.Order).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                    continue;

                var rawValue = GetRawFixedAssetValue(record, selectedRow, field);
                values[field.Name] = FormatFixedAssetDetailValue(rawValue, field);
            }

            return values;
        }

        private static object? GetRawFixedAssetValue(
            IReadOnlyDictionary<string, object> record,
            DataRowView selectedRow,
            MetadataField field)
        {
            if (record.TryGetValue(field.Name, out var byName))
                return byName;

            if (!string.IsNullOrWhiteSpace(field.DbColumnName) && record.TryGetValue(field.DbColumnName, out var byColumn))
                return byColumn;

            if (selectedRow.Row.Table.Columns.Contains(field.Name))
                return selectedRow[field.Name];

            return null;
        }

        private string FormatFixedAssetDetailValue(object? value, MetadataField field)
        {
            if (value == null || value == DBNull.Value)
                return string.Empty;

            if (field.FieldType == "Reference" && _referenceCache.TryGetValue(field.Name, out var lookup))
            {
                var key = NormalizeReferenceKey(value);
                if (!string.IsNullOrWhiteSpace(key) && lookup.TryGetValue(key, out var displayValue))
                    return displayValue;
            }

            if (field.Name == "Класс ОС")
                return FormatFixedAssetClass(value);

            if (value is DateTime dateTime)
                return dateTime.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

            if (value is DateTimeOffset dateTimeOffset)
                return dateTimeOffset.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);

            if (field.FieldType == "Decimal")
                return FormatFixedAssetNumber(value);

            if (value is bool boolValue)
                return boolValue ? "Да" : "Нет";

            return Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
        }

        private static string FormatFixedAssetNumber(object value)
        {
            if (value is decimal decimalValue)
                return decimalValue.ToString("N2", CultureInfo.CurrentCulture);

            if (value is double doubleValue)
                return doubleValue.ToString("N2", CultureInfo.CurrentCulture);

            if (value is float floatValue)
                return floatValue.ToString("N2", CultureInfo.CurrentCulture);

            var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim();
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var parsed) ||
                decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed.ToString("N2", CultureInfo.CurrentCulture);
            }

            return text ?? string.Empty;
        }

        private static string FormatFixedAssetClass(object value)
        {
            var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim();
            return text switch
            {
                "1" => "Стационарный",
                "2" => "Подвижной",
                _ => text ?? string.Empty
            };
        }
        private bool IsFixedAssetDisposedAsOf(DataRowView view)
        {
            if (!TryGetDate(view, "Дата выбытия", out var disposalDate))
                return false;

            return disposalDate.Date <= GetFixedAssetAsOfDate();
        }

        private static bool TryGetDate(DataRowView view, string columnName, out DateTime date)
        {
            date = default;
            if (!view.Row.Table.Columns.Contains(columnName))
                return false;

            var value = view[columnName];
            if (value == null || value == DBNull.Value)
                return false;

            if (value is DateTime dateTime)
            {
                date = dateTime;
                return true;
            }

            return DateTime.TryParse(value.ToString(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date) ||
                   DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private Dictionary<string, object> GetExistingDataForEdit(Guid id, DataRowView selectedRow)
        {
            var existingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (_rawRowsById.TryGetValue(id, out var rawRow))
            {
                foreach (var pair in rawRow)
                {
                    if (pair.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                        pair.Key.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase) ||
                        pair.Key.Equals("UpdatedAt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    existingData[pair.Key] = pair.Value;
                }
            }

            foreach (DataColumn column in _dataTable.Columns)
            {
                if (column.ColumnName == "Id" ||
                    column.ColumnName == "Дата создания" ||
                    column.ColumnName == "Дата изменения" ||
                    existingData.ContainsKey(column.ColumnName))
                {
                    continue;
                }

                var value = selectedRow[column.ColumnName];
                if (value != DBNull.Value)
                    existingData[column.ColumnName] = value;
            }

            return existingData;
        }
        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            if (IsFixedAssetsCatalog)
            {
                var fixedAssetDialog = new FixedAssetDetailsDialog(
                    _catalog,
                    _metadataService,
                    FixedAssetCardMode.Create,
                    asOfDate: GetFixedAssetAsOfDate())
                {
                    Owner = Window.GetWindow(this)
                };

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        fixedAssetDialog,
                        "Добавление: Основное средство") == true)
                {
                    try
                    {
                        StatusText.Text = "💾 Сохранение...";
                        ProgressText.Text = "⏳ Сохранение...";
                        await _metadataService.CreateDynamicRecordAsync(_catalog.Id, fixedAssetDialog.ItemData);
                        await LoadData();
                        MessageBox.Show("Запись успешно добавлена!", "Успех",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        ProgressText.Text = "";
                        StatusText.Text = "✅ Готово";
                    }
                }

                return;
            }

            var dialog = new CatalogItemDialog(_catalog, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    $"Добавление: {_catalog.Name}") == true)
            {
                try
                {
                    StatusText.Text = "💾 Сохранение...";
                    ProgressText.Text = "⏳ Сохранение...";
                    await _metadataService.AddCatalogItemAsync(_catalog.Id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись успешно добавлена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ProgressText.Text = "";
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null) return;

            var id = (Guid)selectedRow["Id"];
            var existingData = GetExistingDataForEdit(id, selectedRow);

            if (IsFixedAssetsCatalog)
            {
                var fixedAssetDialog = new FixedAssetDetailsDialog(
                    _catalog,
                    _metadataService,
                    FixedAssetCardMode.Edit,
                    existingData,
                    id,
                    GetFixedAssetAsOfDate())
                {
                    Owner = Window.GetWindow(this)
                };

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this),
                        fixedAssetDialog,
                        "Редактирование: Основное средство") == true)
                {
                    try
                    {
                        StatusText.Text = "💾 Обновление...";
                        ProgressText.Text = "⏳ Обновление...";
                        await _metadataService.UpdateDynamicRecordAsync(_catalog.Id, id, fixedAssetDialog.ItemData);
                        await LoadData();
                        MessageBox.Show("Запись успешно обновлена!", "Успех",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        ProgressText.Text = "";
                        StatusText.Text = "✅ Готово";
                    }
                }

                return;
            }

            var dialog = new CatalogItemDialog(_catalog, _metadataService, existingData);
            dialog.Owner = Window.GetWindow(this);

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this),
                    dialog,
                    $"Редактирование: {_catalog.Name}") == true)
            {
                try
                {
                    StatusText.Text = "💾 Обновление...";
                    ProgressText.Text = "⏳ Обновление...";
                    await _metadataService.UpdateDynamicRecordAsync(_catalog.Id, id, dialog.ItemData);
                    await LoadData();
                    MessageBox.Show("Запись успешно обновлена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка обновления: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ProgressText.Text = "";
                    StatusText.Text = "✅ Готово";
                }
            }
        }
        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selectedRow = DataGrid.SelectedItem as DataRowView;
            if (selectedRow == null) return;

            var id = (Guid)selectedRow["Id"];
            var firstField = _catalog.Fields.FirstOrDefault();
            var name = firstField != null && selectedRow[firstField.Name] != null
                ? selectedRow[firstField.Name].ToString()
                : id.ToString();

            var result = MessageBox.Show($"Удалить запись '{name}'?\nВосстановление будет невозможно!",
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StatusText.Text = "🗑️ Удаление...";
                    ProgressText.Text = "⏳ Удаление...";
                    await _metadataService.DeleteDynamicRecordAsync(_catalog.Id, id);
                    await LoadData();
                    MessageBox.Show("Запись успешно удалена!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ProgressText.Text = "";
                    StatusText.Text = "✅ Готово";
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_dataTable == null || _dataTable.Rows.Count == 0)
                {
                    MessageBox.Show("Нет данных для экспорта", "Информация",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDialog = new SaveFileDialog
                {
                    Title = "Сохранить Excel файл",
                    Filter = "Excel файлы (*.xlsx)|*.xlsx",
                    DefaultExt = "xlsx",
                    FileName = $"{_catalog.Name}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    ProgressText.Text = "⏳ Экспорт...";
                    StatusText.Text = "Подготовка данных...";

                    await Task.Run(() => ExportToExcel(saveDialog.FileName));

                    ProgressText.Text = "";
                    StatusText.Text = $"✅ Экспорт завершен! Сохранено: {saveDialog.FileName}";

                    var result = MessageBox.Show($"Данные успешно экспортированы!\n\nФайл: {saveDialog.FileName}\n\nОткрыть файл?",
                        "Экспорт завершен", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = saveDialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressText.Text = "";
                StatusText.Text = "❌ Ошибка экспорта";
            }
        }

        private async void OnImportDbfClick(object sender, RoutedEventArgs e)
        {
            if (!CanImportDbf)
                return;

            var openDialog = CreateDbfOpenDialog();

            if (openDialog.ShowDialog() != true)
                return;

            try
            {
                ProgressText.Text = "⏳ Импорт DBF...";
                StatusText.Text = IsPaymentClassificationCatalog
                    ? "Загрузка классификации платежей из Fox DBF..."
                    : "Загрузка плана счетов из Fox DBF...";

                if (IsPaymentClassificationCatalog)
                {
                    var result = await _metadataService.ImportPaymentClassificationsFromDbfAsync(openDialog.FileName);
                    await LoadData();

                    MessageBox.Show(
                        BuildPaymentClassificationImportMessage(result),
                        "Импорт классификации платежей",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    var result = await _metadataService.ImportChartOfAccountsFromDbfAsync(openDialog.FileName);
                    await LoadData();

                    MessageBox.Show(
                        BuildChartOfAccountsImportMessage(result),
                        "Импорт плана счетов",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка загрузки DBF: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ProgressText.Text = string.Empty;
                StatusText.Text = "✅ Готово";
            }
        }

        private OpenFileDialog CreateDbfOpenDialog()
        {
            if (IsPaymentClassificationCatalog)
            {
                return new OpenFileDialog
                {
                    Title = "Выберите DBF файл классификации платежей",
                    Filter = "DBF файлы (*.dbf)|*.dbf|Все файлы (*.*)|*.*",
                    FileName = "VID_PL.DBF"
                };
            }

            return new OpenFileDialog
            {
                Title = "Выберите DBF файл плана счетов",
                Filter = "Файл плана счетов (BUXSCH.DBF)|BUXSCH.DBF|DBF файлы (*.dbf)|*.dbf|Все файлы (*.*)|*.*",
                FileName = "BUXSCH.DBF"
            };
        }

        private void ExportToExcel(string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(_catalog.Name);

            worksheet.Cell(1, 1).InsertTable(_dataTable);

            var headerRange = worksheet.Range(1, 1, 1, _dataTable.Columns.Count);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#2C3E50");
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            worksheet.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        }

        private static string BuildChartOfAccountsImportMessage(ChartOfAccountsDbfImportResult result)
        {
            var mappedFields = string.Join(Environment.NewLine,
                result.FieldMappings.Select(item => $"• {item.SourceField} -> {item.TargetField}"));

            var ignoredFields = result.IgnoredSourceFields.Count == 0
                ? "нет"
                : string.Join(", ", result.IgnoredSourceFields);

            return
                $"Файл: {result.SourcePath}{Environment.NewLine}{Environment.NewLine}" +
                $"Обработано записей: {result.SourceRecordCount}{Environment.NewLine}" +
                $"Уникальных счетов: {result.LoadedAccountsCount}{Environment.NewLine}" +
                $"Добавлено: {result.InsertedCount}{Environment.NewLine}" +
                $"Обновлено: {result.UpdatedCount}{Environment.NewLine}" +
                $"Переведено в неактивные: {result.DeactivatedCount}{Environment.NewLine}" +
                $"Повторов кодов в источнике: {result.DuplicateSourceCodesCount}{Environment.NewLine}{Environment.NewLine}" +
                $"Разобранные поля DBF:{Environment.NewLine}{mappedFields}{Environment.NewLine}{Environment.NewLine}" +
                $"Поля Fox без прямой загрузки в текущий набор реквизитов:{Environment.NewLine}{ignoredFields}";
        }

        private static string BuildPaymentClassificationImportMessage(PaymentClassificationDbfImportResult result)
        {
            var mappedFields = string.Join(Environment.NewLine,
                result.FieldMappings.Select(item => $"• {item.SourceField} -> {item.TargetField}"));

            var ignoredFields = result.IgnoredSourceFields.Count == 0
                ? "нет"
                : string.Join(", ", result.IgnoredSourceFields);

            return
                $"Файл: {result.SourcePath}{Environment.NewLine}{Environment.NewLine}" +
                $"Обработано записей: {result.SourceRecordCount}{Environment.NewLine}" +
                $"Загружено кодов платежей: {result.LoadedItemsCount}{Environment.NewLine}" +
                $"Добавлено: {result.InsertedCount}{Environment.NewLine}" +
                $"Обновлено: {result.UpdatedCount}{Environment.NewLine}" +
                $"Переведено в неактивные: {result.DeactivatedCount}{Environment.NewLine}" +
                $"Повторов кодов в источнике: {result.DuplicateSourceCodesCount}{Environment.NewLine}{Environment.NewLine}" +
                $"Разобранные поля DBF:{Environment.NewLine}{mappedFields}{Environment.NewLine}{Environment.NewLine}" +
                $"Поля Fox без прямой загрузки в текущий набор реквизитов:{Environment.NewLine}{ignoredFields}";
        }

        private sealed class AccountTypeDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return LocalizationService.DisplayValue(value);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class BooleanPlusDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value switch
                {
                    true => "+",
                    false => string.Empty,
                    _ => string.Empty
                };
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class BooleanYesNoDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return LocalizationService.DisplayValue(value);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ClosingModuleDisplayConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return ChartOfAccountsSelectionMetadata.NormalizeModuleDisplayName(value?.ToString());
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class ChartOfAccountsModeDisplayConverter : IValueConverter
        {
            private readonly string _fieldName;

            public ChartOfAccountsModeDisplayConverter(string fieldName)
            {
                _fieldName = fieldName;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return ChartOfAccountsSelectionMetadata.GetModeDisplay(_fieldName, value?.ToString());
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}









