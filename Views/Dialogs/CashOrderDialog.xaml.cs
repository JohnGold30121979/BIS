using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BIS.ERP.Views
{
    public partial class CashOrderDialog : Window
    {
        private const string CashOrderReceiptKind = "Receipt";
        private const string CashOrderPaymentKind = "Payment";
        private const string CashOrderReceiptDocumentType = "РџСЂРёС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";
        private const string CashOrderPaymentDocumentType = "Р Р°СЃС…РѕРґРЅС‹Р№ РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";

        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private string _selectedCorrAccountCode = string.Empty;
        private Guid _selectedCashDeskId;
        private string _selectedCashDeskCode = string.Empty;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private List<CashDeskItem> _cashDesks = new();
        private MetadataObject? _cashDeskCatalog;
        private readonly ObservableCollection<CashPostingPreviewRow> _postingPreviewRows = new();
        private string _orderKind = CashOrderPaymentKind;

        // Р”Р»СЏ СЃРѕС‚СЂСѓРґРЅРёРєР°
        private Guid _selectedEmployeeId = Guid.Empty;
        private string _selectedEmployeeName = string.Empty;

        public CashOrderDialog(MetadataObject document, MetadataService metadataService)
            : this(document, metadataService, CashOrderPaymentKind)
        {
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, string orderKind)
        {
            InitializeComponent();
            PostingsPreviewGrid.ItemsSource = _postingPreviewRows;
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            _orderKind = NormalizeOrderKind(orderKind, document.Name);
            DialogTitle.Text = BuildDialogTitle();
            DatePicker.SelectedDate = DateTime.Today;

            ContentRendered += async (s, e) => await InitializeAsync();
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            PostingsPreviewGrid.ItemsSource = _postingPreviewRows;
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = "Р РµРґР°РєС‚РёСЂРѕРІР°РЅРёРµ: РєР°СЃСЃРѕРІС‹Р№ РѕСЂРґРµСЂ";

            ContentRendered += async (s, e) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isDataLoaded || _isLoading) return;
            _isLoading = true;

            try
            {
                this.Cursor = Cursors.Wait;

                var data = await Task.Run(async () => await LoadAllDataAsync());

                await Dispatcher.InvokeAsync(() =>
                {
                    // Р—Р°РїРѕР»РЅСЏРµРј ComboBox (РєСЂРѕРјРµ СЃРѕС‚СЂСѓРґРЅРёРєР°)
                    _cashDeskCatalog = data.CashDeskCatalog;
                    if (data.CashDesks != null)
                    {
                        _cashDesks = data.CashDesks;
                        ReferenceComboBoxSearchHelper.Attach(CashDeskCombo, _cashDesks);
                    }
                    if (data.Organizations != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(OrganizationCombo, data.Organizations);
                        if (data.OrganizationCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                OrganizationCombo,
                                _metadataService,
                                data.OrganizationCatalog,
                                this,
                                items => data.Organizations = items,
                                "РљРѕРґ РѕСЂРіР°РЅРёР·Р°С†РёРё",
                                "РќР°РёРјРµРЅРѕРІР°РЅРёРµ");
                        }
                    }
                    if (data.Currencies != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(CurrencyCombo, data.Currencies);
                        if (data.CurrencyCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                CurrencyCombo,
                                _metadataService,
                                data.CurrencyCatalog,
                                this,
                                items => data.Currencies = items,
                                "РљРѕРґ",
                                "РќР°РёРјРµРЅРѕРІР°РЅРёРµ");
                        }
                    }
                    if (data.Materials != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(MaterialCombo, data.Materials);
                        if (data.MaterialCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                MaterialCombo,
                                _metadataService,
                                data.MaterialCatalog,
                                this,
                                items => data.Materials = items,
                                "РљРѕРґ",
                                "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РјР°С‚РµСЂРёР°Р»Р°");
                        }
                    }

                    _accountAnalytics = data.AccountAnalytics;


                    // Р“РµРЅРµСЂРёСЂСѓРµРј РЅРѕРјРµСЂ
                    if (!editId.HasValue)
                    {
                        NumberBox.Text = data.DocumentNumber;

                        if (data.CashDesks != null && data.CashDesks.Any())
                        {
                            CashDeskCombo.SelectedItem = data.CashDesks.First();
                            _selectedCashDeskId = data.CashDesks.First().Id;
                            _selectedCashDeskCode = data.CashDesks.First().AccountCode;
                            CashDeskAccountBox.Text = _selectedCashDeskCode;
                            RefreshPostingPreview();
                        }
                    }
                    else if (data.Record != null)
                    {
                        _orderKind = ResolveOrderKind(data.Record, _document.Name);
                        DialogTitle.Text = BuildDialogTitle();
                        // Р—Р°РїРѕР»РЅСЏРµРј РґР°РЅРЅС‹Рµ РґР»СЏ СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёСЏ
                        var rawNumber = data.Record.ContainsKey("РќРѕРјРµСЂ") ? data.Record["РќРѕРјРµСЂ"]?.ToString() :
                                       (data.Record.ContainsKey("doc_number") ? data.Record["doc_number"]?.ToString() : "");
                        NumberBox.Text = MetadataService.NormalizeLegacyDocumentNumber(rawNumber);

                        if (data.Record.ContainsKey("Р”Р°С‚Р°") && data.Record["Р”Р°С‚Р°"] is DateTime dt)
                            DatePicker.SelectedDate = dt;
                        if (data.Record.ContainsKey("РЎСѓРјРјР°"))
                            AmountBox.Text = data.Record["РЎСѓРјРјР°"].ToString();
                        if (data.Record.ContainsKey("РћСЃРЅРѕРІР°РЅРёРµ"))
                            BasisBox.Text = data.Record["РћСЃРЅРѕРІР°РЅРёРµ"].ToString();
                        if (data.Record.ContainsKey("РџСЂРёРјРµС‡Р°РЅРёРµ"))
                            DescriptionBox.Text = data.Record["РџСЂРёРјРµС‡Р°РЅРёРµ"].ToString();

                        // Р—Р°РіСЂСѓР¶Р°РµРј РєР°СЃСЃСѓ
                        if (data.Record.TryGetValue("РљР°СЃСЃР°", out var cashValue))
                            SelectComboByRecordValue(CashDeskCombo, data.Record, "РљР°СЃСЃР°");

                        // Р—Р°РіСЂСѓР¶Р°РµРј РєРѕСЂСЂРµСЃРїРѕРЅРґРёСЂСѓСЋС‰РёР№ СЃС‡РµС‚
                        if (data.Record.TryGetValue("РљРѕСЂСЂ. СЃС‡РµС‚", out var accountValue))
                            ApplySelectedCorrAccount(accountValue);

                        // Р—Р°РіСЂСѓР¶Р°РµРј РѕСЂРіР°РЅРёР·Р°С†РёСЋ
                        SelectComboByRecordValue(OrganizationCombo, data.Record, "РћСЂРіР°РЅРёР·Р°С†РёСЏ");

                        // Р—Р°РіСЂСѓР¶Р°РµРј РІР°Р»СЋС‚Сѓ
                        SelectComboByRecordValue(CurrencyCombo, data.Record, "Р’Р°Р»СЋС‚Р°");

                        // Р—Р°РіСЂСѓР¶Р°РµРј СЃРѕС‚СЂСѓРґРЅРёРєР° (С‡РµСЂРµР· РѕС‚РґРµР»СЊРЅС‹Р№ РјРµС‚РѕРґ)
                        if (data.Record.TryGetValue("РЎРѕС‚СЂСѓРґРЅРёРє", out var employeeValue) && Guid.TryParse(employeeValue?.ToString(), out var empId))
                        {
                            _selectedEmployeeId = empId;
                            var emp = data.Employees?.FirstOrDefault(e => e.Id == empId);
                            EmployeeNameBox.Text = emp != null ? emp.DisplayName : employeeValue.ToString();
                            _selectedEmployeeName = emp?.DisplayName ?? employeeValue.ToString();
                        }

                        // Р—Р°РіСЂСѓР¶Р°РµРј РјР°С‚РµСЂРёР°Р»
                        SelectComboByRecordValue(MaterialCombo, data.Record, "РњР°С‚РµСЂРёР°Р»");
                    }

                    UpdateAccountControlledFieldsVisibility();
                    AmountBox.Focus();
                    AmountBox.SelectAll();
                });

                await UpdateDialogTitleWithOpenDayAsync();
                _isDataLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° Р·Р°РіСЂСѓР·РєРё: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
                _isLoading = false;
            }
        }

        private async Task<DialogData> LoadAllDataAsync()
        {
            var result = new DialogData();
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

            // РљР°СЃСЃС‹
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "РљР°СЃСЃС‹");
            result.CashDeskCatalog = cashDesks;
            if (cashDesks != null)
                result.CashDesks = await LoadCashDeskItemsAsync(cashDesks, result.AccountAnalytics);

            // РћСЂРіР°РЅРёР·Р°С†РёРё
            var orgs = allCatalogs.FirstOrDefault(c => c.Name == "РћСЂРіР°РЅРёР·Р°С†РёРё");
            result.OrganizationCatalog = orgs;
            if (orgs != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgs.Id);
                result.Organizations = data
                    .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                    .Select(row => CreateReferenceItem(row, "РљРѕРґ", "РќР°РёРјРµРЅРѕРІР°РЅРёРµ"))
                    .ToList();
            }

            // Р’Р°Р»СЋС‚С‹
            result.CurrencyCatalog = allCatalogs.FirstOrDefault(c => c.Name == "РЎРїСЂР°РІРѕС‡РЅРёРє РІР°Р»СЋС‚");
            result.Currencies = await LoadReferenceItemsAsync(allCatalogs, "РЎРїСЂР°РІРѕС‡РЅРёРє РІР°Р»СЋС‚", "РљРѕРґ", "РќР°РёРјРµРЅРѕРІР°РЅРёРµ");
            // РЎРѕС‚СЂСѓРґРЅРёРєРё (РґР»СЏ РґРёР°Р»РѕРіР° РІС‹Р±РѕСЂР°)
            result.EmployeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)");
            result.Employees = await LoadReferenceItemsAsync(allCatalogs, "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)", "РўР°Р±РµР»СЊРЅС‹Р№ РЅРѕРјРµСЂ", "Р¤РРћ");
            // РњР°С‚РµСЂРёР°Р»С‹
            result.MaterialCatalog = allCatalogs.FirstOrDefault(c => c.Name == "РЎРїСЂР°РІРѕС‡РЅРёРє РјР°С‚РµСЂРёР°Р»РѕРІ");
            result.Materials = await LoadReferenceItemsAsync(allCatalogs, "РЎРїСЂР°РІРѕС‡РЅРёРє РјР°С‚РµСЂРёР°Р»РѕРІ", "РљРѕРґ", "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РјР°С‚РµСЂРёР°Р»Р°");

            // Р”Р»СЏ РџРљРћ Рё Р РљРћ РЅРѕРјРµСЂР° СЃС‡РёС‚Р°СЋС‚СЃСЏ СЂР°Р·РґРµР»СЊРЅРѕ, С…РѕС‚СЏ Р·Р°РїРёСЃРё С…СЂР°РЅСЏС‚СЃСЏ РІ РѕР±С‰РµР№ С‚Р°Р±Р»РёС†Рµ.
            if (!_editId.HasValue)
            {
                try
                {
                    result.DocumentNumber = await _metadataService.GetNextCashOrderDocumentNumberAsync(_orderKind);
                }
                catch
                {
                    result.DocumentNumber = MetadataService.GenerateFallbackDocumentNumber();
                }
            }

            // Р•СЃР»Рё СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёРµ, Р·Р°РіСЂСѓР¶Р°РµРј Р·Р°РїРёСЃСЊ
            if (_editId.HasValue)
            {
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                result.Record = data.FirstOrDefault(r => r["Id"].ToString() == _editId.Value.ToString());
            }

            return result;
        }

        private class DialogData
        {
            public List<CashDeskItem> CashDesks { get; set; } = new();
            public List<ReferenceItem> Organizations { get; set; } = new();
            public List<ReferenceItem> Currencies { get; set; } = new();
            public List<ReferenceItem> Employees { get; set; } = new();
            public List<ReferenceItem> Materials { get; set; } = new();
            public MetadataObject? CashDeskCatalog { get; set; }
            public MetadataObject? OrganizationCatalog { get; set; }
            public MetadataObject? CurrencyCatalog { get; set; }
            public MetadataObject? EmployeeCatalog { get; set; }
            public MetadataObject? MaterialCatalog { get; set; }
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
            public string DocumentNumber { get; set; } = string.Empty;
            public Dictionary<string, object>? Record { get; set; }
        }

        private async Task<List<CashDeskItem>> LoadCashDeskItemsAsync(
            MetadataObject cashDeskCatalog,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
            return rows
                .Where(row => row.TryGetValue("Id", out var id) && Guid.TryParse(id?.ToString(), out _))
                .Select(row => CreateCashDeskItem(row, accountAnalytics))
                .ToList();
        }

        private static CashDeskItem CreateCashDeskItem(
            Dictionary<string, object> row,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountCode = ResolveCashDeskAccountCode(
                GetRowString(row, "РЎС‡РµС‚", "РЎС‡РµС‚ РєР°СЃСЃС‹", "РљРѕРґ", "code"),
                accountAnalytics);

            var item = new CashDeskItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = GetRowString(
                    row,
                    "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РєР°СЃСЃС‹",
                    "РќР°РёРјРµРЅРѕРІР°РЅРёРµ",
                    "name",
                    "РљРѕРґ") ?? "РљР°СЃСЃР°",
                AccountCode = accountCode,
                CashNumber = GetRowString(row, "РќРѕРјРµСЂ РєР°СЃСЃС‹", "cash_number") ?? string.Empty,
                CurrencyName = GetRowString(row, "Р’Р°Р»СЋС‚Р°", "currency_id") ?? string.Empty
            };

            foreach (var value in row.Values)
            {
                var text = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(text))
                    item.LookupKeys.Add(text);
            }

            if (!string.IsNullOrWhiteSpace(item.DisplayNameWithAccount))
                item.LookupKeys.Add(item.DisplayNameWithAccount);

            return item;
        }

        private async Task<MetadataObject?> GetCashDeskCatalogAsync()
        {
            if (_cashDeskCatalog != null)
                return _cashDeskCatalog;

            var allCatalogs = await _metadataService.GetCatalogsAsync();
            _cashDeskCatalog = allCatalogs.FirstOrDefault(catalog => catalog.Name == "РљР°СЃСЃС‹");

            if (_cashDeskCatalog == null)
            {
                MessageBox.Show("РЎРїСЂР°РІРѕС‡РЅРёРє РєР°СЃСЃ РЅРµ РЅР°Р№РґРµРЅ.", "РљР°СЃСЃС‹",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return _cashDeskCatalog;
        }

        private async Task ReloadCashDesksAsync(Guid? selectedId = null)
        {
            var cashDeskCatalog = await GetCashDeskCatalogAsync();
            if (cashDeskCatalog == null)
                return;

            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _cashDesks = await LoadCashDeskItemsAsync(cashDeskCatalog, _accountAnalytics);
            ReferenceComboBoxSearchHelper.Attach(CashDeskCombo, _cashDesks);

            if (selectedId.HasValue)
            {
                CashDeskCombo.SelectedItem = _cashDesks.FirstOrDefault(item => item.Id == selectedId.Value);
            }
            else if (_selectedCashDeskId != Guid.Empty)
            {
                CashDeskCombo.SelectedItem = _cashDesks.FirstOrDefault(item => item.Id == _selectedCashDeskId);
            }

            if (CashDeskCombo.SelectedItem is not CashDeskItem)
            {
                _selectedCashDeskId = Guid.Empty;
                _selectedCashDeskCode = string.Empty;
                CashDeskAccountBox.Text = string.Empty;
                RefreshPostingPreview();
            }
        }

        private async Task ApplyCashDeskByIdAsync(Guid cashDeskId)
        {
            await ReloadCashDesksAsync(cashDeskId);
            if (CashDeskCombo.SelectedItem is not CashDeskItem)
            {
                MessageBox.Show("РљР°СЃСЃР° СЃРѕС…СЂР°РЅРµРЅР°, РЅРѕ РЅРµ РЅР°Р№РґРµРЅР° РїРѕСЃР»Рµ РѕР±РЅРѕРІР»РµРЅРёСЏ СЃРїРёСЃРєР°.", "РљР°СЃСЃС‹",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await UpdateDialogTitleWithOpenDayAsync();
        }

        private async void SelectCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(cashDeskCatalog, _metadataService);
                if (rows.Count == 0)
                {
                    MessageBox.Show("Р’ СЃРїСЂР°РІРѕС‡РЅРёРєРµ РєР°СЃСЃ РЅРµС‚ РґР°РЅРЅС‹С…. Р”РѕР±Р°РІСЊС‚Рµ РєР°СЃСЃСѓ РєРЅРѕРїРєРѕР№ '+'.", "РљР°СЃСЃС‹",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(rows, "РќР°РёРјРµРЅРѕРІР°РЅРёРµ РєР°СЃСЃС‹", "РЎС‡РµС‚", referenceMaps)
                {
                    Owner = this,
                    Title = "Р’С‹Р±РѕСЂ: РљР°СЃСЃС‹"
                };

                if (dialog.ShowDialog() == true &&
                    dialog.SelectedItem != null &&
                    dialog.SelectedItem.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var selectedId))
                {
                    await ApplyCashDeskByIdAsync(selectedId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё РІС‹Р±РѕСЂРµ РєР°СЃСЃС‹: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void AddCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                var createdId = await _metadataService.CreateDynamicRecordAsync(cashDeskCatalog.Id, dialog.ItemData);
                await ApplyCashDeskByIdAsync(createdId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё РґРѕР±Р°РІР»РµРЅРёРё РєР°СЃСЃС‹: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void EditCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CashDeskCombo.SelectedItem is not CashDeskItem selected)
                {
                    MessageBox.Show("РЎРЅР°С‡Р°Р»Р° РІС‹Р±РµСЂРёС‚Рµ РєР°СЃСЃСѓ.", "РљР°СЃСЃС‹",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
                var cashDesk = rows.FirstOrDefault(row =>
                    row.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var id) &&
                    id == selected.Id);

                if (cashDesk == null)
                {
                    MessageBox.Show("Р’С‹Р±СЂР°РЅРЅР°СЏ РєР°СЃСЃР° РЅРµ РЅР°Р№РґРµРЅР° РІ СЃРїСЂР°РІРѕС‡РЅРёРєРµ.", "РљР°СЃСЃС‹",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService, cashDesk)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _metadataService.UpdateDynamicRecordAsync(cashDeskCatalog.Id, selected.Id, dialog.ItemData);
                await ApplyCashDeskByIdAsync(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёРё РєР°СЃСЃС‹: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }
        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("Р”Р»СЏ СЌС‚РѕРіРѕ РјРѕРґСѓР»СЏ РЅРµС‚ РґРѕСЃС‚СѓРїРЅС‹С… СЃС‡РµС‚РѕРІ РІ РїР»Р°РЅРµ СЃС‡РµС‚РѕРІ.", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new AccountSelectionDialog(accountsData);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    var accountCode = dialog.SelectedAccount.ContainsKey("РљРѕРґ") ? dialog.SelectedAccount["РљРѕРґ"].ToString() : "";
                    var accountName = dialog.SelectedAccount.ContainsKey("РќР°РёРјРµРЅРѕРІР°РЅРёРµ") ? dialog.SelectedAccount["РќР°РёРјРµРЅРѕРІР°РЅРёРµ"].ToString() : "";

                    CorrAccountBox.Text = $"{accountCode} - {accountName}";

                    if (dialog.SelectedAccount.ContainsKey("Id"))
                    {
                        _selectedCorrAccountId = Guid.Parse(dialog.SelectedAccount["Id"].ToString());
                    }

                    _selectedCorrAccountCode = accountCode;

                    UpdateAccountControlledFieldsVisibility();
                    RefreshPostingPreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё РІС‹Р±РѕСЂРµ СЃС‡РµС‚Р°: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text);
                if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(c => !char.IsDigit(c)))
                {
                    MessageBox.Show("РќРѕРјРµСЂ РґРѕРєСѓРјРµРЅС‚Р° РґРѕР»Р¶РµРЅ СЃРѕРґРµСЂР¶Р°С‚СЊ С‚РѕР»СЊРєРѕ С†РёС„СЂС‹.", "РџСЂРѕРІРµСЂРєР°",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    NumberBox.Focus();
                    return;
                }

                NumberBox.Text = documentNumber;

                var amount = decimal.TryParse(AmountBox.Text, out var parsedAmount) ? parsedAmount : 0;
                if (amount <= 0)
                {
                    MessageBox.Show("РЎСѓРјРјР° РєР°СЃСЃРѕРІРѕРіРѕ РѕСЂРґРµСЂР° РґРѕР»Р¶РЅР° Р±С‹С‚СЊ Р±РѕР»СЊС€Рµ РЅСѓР»СЏ.", "РџСЂРѕРІРµСЂРєР°",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    AmountBox.Focus();
                    return;
                }

                // РџР РћР’Р•Р РљРђ Р—РђРџРћР›РќР•РќРРЇ Р’РЎР•РҐ РђРљРўРР’РќР«РҐ РџРћР›Р•Р™
                if (OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem == null)
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РѕСЂРіР°РЅРёР·Р°С†РёСЋ!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    OrganizationCombo.Focus();
                    return;
                }

                if (EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId == Guid.Empty)
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ СЃРѕС‚СЂСѓРґРЅРёРєР°!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem == null)
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РІР°Р»СЋС‚Сѓ!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CurrencyCombo.Focus();
                    return;
                }

                if (MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem == null)
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РјР°С‚РµСЂРёР°Р»!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    MaterialCombo.Focus();
                    return;
                }

                // РџРѕР»СѓС‡Р°РµРј РєР°СЃСЃСѓ
                string cashDeskId = string.Empty;
                string cashDeskCode = _selectedCashDeskCode;

                if (CashDeskCombo.SelectedItem is CashDeskItem cashDesk)
                {
                    cashDeskId = cashDesk.Id.ToString();
                    cashDeskCode = cashDesk.AccountCode;
                    _selectedCashDeskId = cashDesk.Id;
                    _selectedCashDeskCode = cashDesk.AccountCode;
                }
                else
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РєР°СЃСЃСѓ.", "РћС€РёР±РєР°",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(cashDeskCode))
                {
                    MessageBox.Show("РЈ РІС‹Р±СЂР°РЅРЅРѕР№ РєР°СЃСЃС‹ РЅРµ СѓРєР°Р·Р°РЅ СЃС‡РµС‚. РћС‚РєСЂРѕР№С‚Рµ СЃРїСЂР°РІРѕС‡РЅРёРє РєР°СЃСЃ Рё Р·Р°РїРѕР»РЅРёС‚Рµ РїРѕР»Рµ \"РЎС‡РµС‚\".", "РћС€РёР±РєР°",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                var documentDate = (DatePicker.SelectedDate ?? DateTime.Today).Date;
                if (!await EnsureCashDayAllowsSaveAsync(_selectedCashDeskId, CashDeskCombo.Text, documentDate))
                    return;

                // РџРѕР»СѓС‡Р°РµРј РєРѕСЂСЂРµСЃРїРѕРЅРґРёСЂСѓСЋС‰РёР№ СЃС‡РµС‚
                string corrAccountId = _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId.ToString() : string.Empty;
                string corrAccountCode = _selectedCorrAccountCode;

                if (string.IsNullOrEmpty(corrAccountCode))
                {
                    MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РєРѕСЂСЂРµСЃРїРѕРЅРґРёСЂСѓСЋС‰РёР№ СЃС‡РµС‚ (РєРЅРѕРїРєР° '?').", "РћС€РёР±РєР°",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // РћРїСЂРµРґРµР»СЏРµРј РґРµР±РµС‚ Рё РєСЂРµРґРёС‚
                var (debitAccount, creditAccount) = BuildPostingAccounts(cashDeskCode, corrAccountCode);

                if (debitAccount == creditAccount)
                {
                    MessageBox.Show("Р”РµР±РµС‚ Рё РєСЂРµРґРёС‚ РЅРµ РјРѕРіСѓС‚ Р±С‹С‚СЊ РѕРґРёРЅР°РєРѕРІС‹РјРё!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                // Р’Р°Р»СЋС‚Р°
                bool isCurrencyEnabled = IsCurrencyEnabledForAccount(corrAccountCode);
                decimal amountInCurrency = isCurrencyEnabled ? amount : 0;
                var counterpartyOrganizationId =
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem organization
                        ? organization.Id.ToString()
                        : string.Empty;
                var primaryOrganizationId = await _metadataService.GetPrimaryOrganizationIdAsync();

                var itemData = new Dictionary<string, object>
                {
                    ["РќРѕРјРµСЂ"] = documentNumber,
                    ["Р”Р°С‚Р°"] = documentDate,
                    ["РўРёРї РљРћ"] = _orderKind,
                    ["РЎСѓРјРјР°"] = amount,
                    ["РћСЃРЅРѕРІР°РЅРёРµ"] = BasisBox.Text,
                    ["РџСЂРёРјРµС‡Р°РЅРёРµ"] = DescriptionBox.Text,
                    ["РџСЂРѕРІРµРґС‘РЅ"] = false,
                    ["РљР°СЃСЃР°"] = cashDeskId,
                    ["РљРѕСЂСЂ. СЃС‡РµС‚"] = corrAccountId,
                    ["Р”РµР±РµС‚"] = debitAccount,
                    ["РљСЂРµРґРёС‚"] = creditAccount,
                    ["РЎСѓРјРјР° РІ РІР°Р»СЋС‚Рµ"] = amountInCurrency
                };

                // Р—Р°РїРѕР»РЅСЏРµРј РѕСЃС‚Р°Р»СЊРЅС‹Рµ РїРѕР»СЏ (С‚РѕР»СЊРєРѕ РµСЃР»Рё РѕРЅРё РІРёРґРёРјС‹, РёРЅР°С‡Рµ РЅРµ СЃРѕС…СЂР°РЅСЏРµРј)
                SetFieldValueIfExists(itemData, "РћСЂРіР°РЅРёР·Р°С†РёСЏ", counterpartyOrganizationId);
                SetFieldValueIfExists(itemData, "РџРµСЂРІРёС‡РЅР°СЏ РѕСЂРіР°РЅРёР·Р°С†РёСЏ", primaryOrganizationId);
                SetFieldValueIfExists(itemData, "РћСЂРіР°РЅРёР·Р°С†РёСЏ Р‘", counterpartyOrganizationId);

                SetFieldValueIfExists(itemData, "Р’Р°Р»СЋС‚Р°",
                    CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "РЎРѕС‚СЂСѓРґРЅРёРє",
                    EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId != Guid.Empty
                        ? _selectedEmployeeId.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "РњР°С‚РµСЂРёР°Р»",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "РљРѕРЅС‚СЂР°РіРµРЅС‚", string.Empty);

                // РЎРѕС…СЂР°РЅСЏРµРј РґРѕРєСѓРјРµРЅС‚.
                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° СЃРѕС…СЂР°РЅРµРЅРёСЏ: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async Task<bool> EnsureCashDayAllowsSaveAsync(Guid cashDeskId, string cashDeskName, DateTime documentDate)
        {
            const string caption = "РџСЂРѕРІРµСЂРєР° РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ";
            if (cashDeskId == Guid.Empty)
            {
                MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РєР°СЃСЃСѓ.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                CashDeskCombo.Focus();
                return false;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);

                if (await cashDayService.IsDayClosedAsync(cashDeskId, documentDate))
                {
                    MessageBox.Show($"РљР°СЃСЃРѕРІС‹Р№ РґРµРЅСЊ {documentDate:dd.MM.yyyy} РїРѕ РєР°СЃСЃРµ \"{cashDeskName}\" Р·Р°РєСЂС‹С‚. РЎРѕР·РґР°РЅРёРµ Рё РёР·РјРµРЅРµРЅРёРµ РґРѕРєСѓРјРµРЅС‚РѕРІ РІ Р·Р°РєСЂС‹С‚РѕРј РґРЅРµ Р·Р°РїСЂРµС‰РµРЅС‹.",
                        caption,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                return true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРѕРІРµСЂРєРё РєР°СЃСЃРѕРІРѕРіРѕ РґРЅСЏ: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private string BuildDialogTitle()
        {
            var action = _editId.HasValue ? "Р РµРґР°РєС‚РёСЂРѕРІР°РЅРёРµ" : "Р”РѕР±Р°РІР»РµРЅРёРµ";
            return $"{action}: {GetOrderKindDisplay(_orderKind)} РљРћ";
        }

        private async Task UpdateDialogTitleWithOpenDayAsync()
        {
            if (DialogTitle == null)
                return;

            var title = BuildDialogTitle();
            if (_selectedCashDeskId == Guid.Empty)
            {
                DialogTitle.Text = $"{title} | РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ: РєР°СЃСЃР° РЅРµ РІС‹Р±СЂР°РЅР°";
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDates = await cashDayService.GetOpenDayDatesAsync(_selectedCashDeskId);
                var openDate = openDates.OrderByDescending(date => date).FirstOrDefault();
                var openDayText = openDate == default ? "РЅРµ РѕС‚РєСЂС‹С‚" : openDate.ToString("dd.MM.yyyy");
                DialogTitle.Text = $"{title} | РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ: {openDayText}";
            }
            catch
            {
                DialogTitle.Text = $"{title} | РѕС‚РєСЂС‹С‚С‹Р№ РґРµРЅСЊ: РЅРµ РѕРїСЂРµРґРµР»РµРЅ";
            }
        }

        private static string CurrentUserNameForAudit() =>
            string.IsNullOrWhiteSpace(Environment.UserName) ? "user" : Environment.UserName;

        private bool IsCurrencyEnabledForAccount(string accountCode)
        {
            if (string.IsNullOrEmpty(accountCode))
                return false;

            try
            {
                var settings = _accountAnalytics.GetSettingsByCode(accountCode);
                if (settings == null)
                    return false;

                var currencyDefinition = _accountAnalytics.Definitions
                    .FirstOrDefault(d => d.Code == "currencies");
                if (currencyDefinition == null)
                    return false;

                return settings.Allows(currencyDefinition);
            }
            catch
            {
                return false;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AllowNumberEditCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var canEdit = AllowNumberEditCheckBox.IsChecked == true;
            NumberBox.IsReadOnly = !canEdit;
            NumberBox.Background = canEdit ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightGray;
            if (canEdit)
            {
                NumberBox.Focus();
                NumberBox.SelectAll();
            }
        }

        private void ApplySelectedCorrAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedCorrAccountId = account.Id;
            _selectedCorrAccountCode = account.Code;
            CorrAccountBox.Text = account.DisplayName;
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var settings = _selectedCorrAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedCorrAccountId);

            SetAccountControlledFieldVisibility(
                OrganizationLabel,
                OrganizationCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "РћСЂРіР°РЅРёР·Р°С†РёСЏ",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "РћСЂРіР°РЅРёР·Р°С†РёРё",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledPanelVisibility(
                CurrencyPanel,
                CurrencyCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Р’Р°Р»СЋС‚Р°",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "РЎРїСЂР°РІРѕС‡РЅРёРє РІР°Р»СЋС‚",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            // Р”Р»СЏ СЃРѕС‚СЂСѓРґРЅРёРєР° вЂ“ РїСЂРѕСЃС‚Рѕ РїРѕРєР°Р·С‹РІР°РµРј/СЃРєСЂС‹РІР°РµРј РїР°РЅРµР»СЊ, ComboBox РЅРµС‚
            EmployeePanel.Visibility = AccountAnalyticsRules.ShouldShowField(
                "РЎРѕС‚СЂСѓРґРЅРёРє",
                new[] { settings },
                _accountAnalytics.Definitions,
                "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false)
                ? Visibility.Visible : Visibility.Collapsed;

            SetAccountControlledPanelVisibility(
                MaterialPanel,
                MaterialCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "РњР°С‚РµСЂРёР°Р»",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "РЎРїСЂР°РІРѕС‡РЅРёРє РјР°С‚РµСЂРёР°Р»РѕРІ",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
        }

        private void SetFieldValueIfExists(Dictionary<string, object> itemData, string fieldName, object value)
        {
            if (_document.Fields.Any(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                itemData[fieldName] = value;
        }

        private async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            List<MetadataObject> catalogs,
            string catalogName,
            string firstDisplayField,
            string secondDisplayField)
        {
            var catalog = catalogs.FirstOrDefault(c => c.Name == catalogName);
            if (catalog == null)
                return new List<ReferenceItem>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                .Select(row => CreateReferenceItem(row, firstDisplayField, secondDisplayField))
                .ToList();
        }

        private static ReferenceItem CreateReferenceItem(
            Dictionary<string, object> row,
            string firstDisplayField,
            string secondDisplayField)
        {
            var item = new ReferenceItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = BuildDisplayName(row, firstDisplayField, secondDisplayField)
            };

            foreach (var value in row.Values)
            {
                var text = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(text))
                    item.LookupKeys.Add(text);
            }

            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                item.LookupKeys.Add(item.DisplayName);

            return item;
        }

        private static string BuildDisplayName(Dictionary<string, object> row, string firstField, string secondField)
        {
            var first = row.GetValueOrDefault(firstField)?.ToString() ??
                        row.GetValueOrDefault(firstField.Replace(" ", "_"))?.ToString() ??
                        string.Empty;
            var second = row.GetValueOrDefault(secondField)?.ToString() ??
                         row.GetValueOrDefault(secondField.Replace(" ", "_"))?.ToString() ??
                         string.Empty;

            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return $"{first} - {second}";

            return first.Length > 0
                ? first
                : second.Length > 0
                    ? second
                    : row.GetValueOrDefault("РќР°РёРјРµРЅРѕРІР°РЅРёРµ")?.ToString() ??
                      row.GetValueOrDefault("name")?.ToString() ??
                      row.GetValueOrDefault("Id")?.ToString() ??
                      string.Empty;
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? normalized[..separatorIndex].Trim() : normalized;
        }

        private static string? GetRowString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        public static string ResolveCashDeskAccountCode(
            string? accountCode,
            AccountAnalyticsRegistry accountAnalytics)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return string.Empty;

            var account = accountAnalytics.FindAccount(accountCode);
            return account?.Code ?? NormalizeAccountCodeText(accountCode);
        }

        private static string NormalizeAccountCodeText(string accountCode)
        {
            var text = accountCode.Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        private static void SelectComboByRecordValue(ComboBox comboBox, Dictionary<string, object> record, string fieldName)
        {
            if (!record.TryGetValue(fieldName, out var value) || !Guid.TryParse(value?.ToString(), out var id))
                return;

            comboBox.SelectedItem = comboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == id);
        }

        private static void SetAccountControlledFieldVisibility(
            FrameworkElement label,
            ComboBox comboBox,
            bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            comboBox.Visibility = visibility;

            if (!isVisible)
                comboBox.SelectedItem = null;
        }

        private static void SetAccountControlledPanelVisibility(
            FrameworkElement panel,
            ComboBox comboBox,
            bool isVisible)
        {
            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && comboBox != null)
                comboBox.SelectedItem = null;
        }

        private async void SelectEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)");

                if (employeeCatalog == null)
                {
                    MessageBox.Show("РЎРїСЂР°РІРѕС‡РЅРёРє СЃРѕС‚СЂСѓРґРЅРёРєРѕРІ РЅРµ РЅР°Р№РґРµРЅ!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);

                if (employeesData == null || employeesData.Count == 0)
                {
                    MessageBox.Show("Р’ СЃРїСЂР°РІРѕС‡РЅРёРєРµ СЃРѕС‚СЂСѓРґРЅРёРєРѕРІ РЅРµС‚ РґР°РЅРЅС‹С…!", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(employeesData, "РўР°Р±РµР»СЊРЅС‹Р№ РЅРѕРјРµСЂ", "Р¤РРћ");
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                {
                    var employee = dialog.SelectedItem;
                    var displayName = $"{employee.GetValueOrDefault("РўР°Р±РµР»СЊРЅС‹Р№ РЅРѕРјРµСЂ")} - {employee.GetValueOrDefault("Р¤РРћ")}";

                    EmployeeNameBox.Text = displayName;
                    _selectedEmployeeId = Guid.Parse(employee["Id"].ToString());
                    _selectedEmployeeName = employee.GetValueOrDefault("Р¤РРћ")?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё РІС‹Р±РѕСЂРµ СЃРѕС‚СЂСѓРґРЅРёРєР°: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void AddEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var employeeCatalog = await GetEmployeeCatalogAsync();
                if (employeeCatalog == null)
                    return;

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                var createdId = await _metadataService.CreateDynamicRecordAsync(employeeCatalog.Id, dialog.ItemData);
                await ApplyEmployeeByIdAsync(employeeCatalog, createdId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё РґРѕР±Р°РІР»РµРЅРёРё СЃРѕС‚СЂСѓРґРЅРёРєР°: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void EditEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedEmployeeId == Guid.Empty)
                {
                    MessageBox.Show("РЎРЅР°С‡Р°Р»Р° РІС‹Р±РµСЂРёС‚Рµ СЃРѕС‚СЂСѓРґРЅРёРєР°.", "РЎРѕС‚СЂСѓРґРЅРёРєРё",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                this.Cursor = Cursors.Wait;

                var employeeCatalog = await GetEmployeeCatalogAsync();
                if (employeeCatalog == null)
                    return;

                var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
                var employee = employeesData.FirstOrDefault(row =>
                    row.TryGetValue("Id", out var value) &&
                    Guid.TryParse(value?.ToString(), out var id) &&
                    id == _selectedEmployeeId);

                if (employee == null)
                {
                    MessageBox.Show("Р’С‹Р±СЂР°РЅРЅС‹Р№ СЃРѕС‚СЂСѓРґРЅРёРє РЅРµ РЅР°Р№РґРµРЅ РІ СЃРїСЂР°РІРѕС‡РЅРёРєРµ.", "РЎРѕС‚СЂСѓРґРЅРёРєРё",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService, employee)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _metadataService.UpdateDynamicRecordAsync(employeeCatalog.Id, _selectedEmployeeId, dialog.ItemData);
                await ApplyEmployeeByIdAsync(employeeCatalog, _selectedEmployeeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"РћС€РёР±РєР° РїСЂРё СЂРµРґР°РєС‚РёСЂРѕРІР°РЅРёРё СЃРѕС‚СЂСѓРґРЅРёРєР°: {ex.Message}", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async Task<MetadataObject?> GetEmployeeCatalogAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "РЎРѕС‚СЂСѓРґРЅРёРєРё (РЎРїРёСЃРѕС‡РЅС‹Р№ СЃРѕСЃС‚Р°РІ)");

            if (employeeCatalog == null)
            {
                MessageBox.Show("РЎРїСЂР°РІРѕС‡РЅРёРє СЃРѕС‚СЂСѓРґРЅРёРєРѕРІ РЅРµ РЅР°Р№РґРµРЅ!", "РћС€РёР±РєР°",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return employeeCatalog;
        }

        private async Task ApplyEmployeeByIdAsync(MetadataObject employeeCatalog, Guid employeeId)
        {
            var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
            var employee = employeesData.FirstOrDefault(row =>
                row.TryGetValue("Id", out var value) &&
                Guid.TryParse(value?.ToString(), out var id) &&
                id == employeeId);

            if (employee == null)
                return;

            var displayName = BuildEmployeeDisplayName(employee);
            EmployeeNameBox.Text = displayName;
            _selectedEmployeeId = employeeId;
            _selectedEmployeeName = employee.GetValueOrDefault("Р¤РРћ")?.ToString()
                                    ?? employee.GetValueOrDefault("full_name")?.ToString()
                                    ?? displayName;
        }

        private static string BuildEmployeeDisplayName(Dictionary<string, object> employee)
        {
            var personnelNumber = employee.GetValueOrDefault("РўР°Р±РµР»СЊРЅС‹Р№ РЅРѕРјРµСЂ")?.ToString()
                                  ?? employee.GetValueOrDefault("personnel_number")?.ToString()
                                  ?? string.Empty;
            var fullName = employee.GetValueOrDefault("Р¤РРћ")?.ToString()
                           ?? employee.GetValueOrDefault("full_name")?.ToString()
                           ?? employee.GetValueOrDefault("РќР°РёРјРµРЅРѕРІР°РЅРёРµ")?.ToString()
                           ?? employee.GetValueOrDefault("name")?.ToString()
                           ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(personnelNumber) && !string.IsNullOrWhiteSpace(fullName))
                return $"{personnelNumber} - {fullName}";

            return !string.IsNullOrWhiteSpace(fullName)
                ? fullName
                : personnelNumber;
        }
        private async void CashDeskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CashDeskCombo.SelectedItem is CashDeskItem selected)
            {
                _selectedCashDeskId = selected.Id;
                _selectedCashDeskCode = selected.AccountCode;
                CashDeskAccountBox.Text = _selectedCashDeskCode;
                RefreshPostingPreview();
            }
            else
            {
                _selectedCashDeskId = Guid.Empty;
                _selectedCashDeskCode = string.Empty;
                CashDeskAccountBox.Text = string.Empty;
                RefreshPostingPreview();
            }

            await UpdateDialogTitleWithOpenDayAsync();
        }
        private static bool IsReceiptOrder(string orderKind)
            => orderKind.Equals(CashOrderReceiptKind, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeOrderKind(string? value, string documentName)
        {
            var rawKind = value ?? string.Empty;
            if (rawKind.Contains("РїСЂРёС…РѕРґ", StringComparison.OrdinalIgnoreCase) ||
                rawKind.Equals(CashOrderReceiptKind, StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals(CashOrderReceiptDocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return CashOrderReceiptKind;
            }

            if (rawKind.Contains("СЂР°СЃС…РѕРґ", StringComparison.OrdinalIgnoreCase) ||
                rawKind.Equals(CashOrderPaymentKind, StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals(CashOrderPaymentDocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return CashOrderPaymentKind;
            }

            return CashOrderPaymentKind;
        }

        private static string ResolveOrderKind(Dictionary<string, object> record, string documentName)
        {
            return NormalizeOrderKind(
                GetRowString(record, "РўРёРї РљРћ", "order_kind", "cash_order_kind", "РўРёРї", "document_type"),
                documentName);
        }

        private static string GetOrderKindDisplay(string orderKind)
            => IsReceiptOrder(orderKind) ? "РџСЂРёС…РѕРґРЅС‹Р№" : "Р Р°СЃС…РѕРґРЅС‹Р№";

        private (string DebitAccount, string CreditAccount) BuildPostingAccounts(string cashDeskCode, string corrAccountCode)
            => IsReceiptOrder(_orderKind)
                ? (cashDeskCode, corrAccountCode)
                : (corrAccountCode, cashDeskCode);

        private decimal TryReadAmount()
            => decimal.TryParse(AmountBox?.Text, out var parsedAmount) ? parsedAmount : 0m;

        private void OnPostingPreviewChanged(object sender, EventArgs e)
        {
            RefreshPostingPreview();
        }

        private void RefreshPostingPreview()
        {
            if (PostingsPreviewGrid == null || PostingPreviewHint == null)
                return;

            _postingPreviewRows.Clear();
            var amount = TryReadAmount();
            var (debitAccount, creditAccount) = BuildPostingAccounts(_selectedCashDeskCode, _selectedCorrAccountCode);
            var isReceipt = IsReceiptOrder(_orderKind);

            _postingPreviewRows.Add(new CashPostingPreviewRow
            {
                Mark = isReceipt ? "РџСЂРёС…РѕРґ" : "Р Р°СЃС…РѕРґ",
                Debit = string.IsNullOrWhiteSpace(debitAccount) ? "РЅРµ РІС‹Р±СЂР°РЅ" : debitAccount,
                Credit = string.IsNullOrWhiteSpace(creditAccount) ? "РЅРµ РІС‹Р±СЂР°РЅ" : creditAccount,
                Amount = amount.ToString("N2"),
                Note = string.IsNullOrWhiteSpace(BasisBox?.Text) ? DescriptionBox?.Text ?? string.Empty : BasisBox.Text
            });

            PostingPreviewHint.Text = isReceipt
                ? "РџСЂРёС…РѕРґРЅС‹Р№ РѕСЂРґРµСЂ: РґРµР±РµС‚СѓРµС‚СЃСЏ СЃС‡РµС‚ РєР°СЃСЃС‹, РєСЂРµРґРёС‚СѓРµС‚СЃСЏ РєРѕСЂСЂРµСЃРїРѕРЅРґРёСЂСѓСЋС‰РёР№ СЃС‡РµС‚."
                : "Р Р°СЃС…РѕРґРЅС‹Р№ РѕСЂРґРµСЂ: РґРµР±РµС‚СѓРµС‚СЃСЏ РєРѕСЂСЂРµСЃРїРѕРЅРґРёСЂСѓСЋС‰РёР№ СЃС‡РµС‚, РєСЂРµРґРёС‚СѓРµС‚СЃСЏ СЃС‡РµС‚ РєР°СЃСЃС‹.";
        }
    }

    public class CashPostingPreviewRow
    {
        public string Mark { get; set; } = string.Empty;
        public string Debit { get; set; } = string.Empty;
        public string Credit { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
    public class CashDeskItem : ReferenceItem
    {
        public string AccountCode { get; set; } = string.Empty;
        public string CashNumber { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string DisplayNameWithAccount =>
            string.IsNullOrWhiteSpace(AccountCode)
                ? DisplayName
                : $"{DisplayName} (СЃС‡РµС‚ {AccountCode})";
    }
}







