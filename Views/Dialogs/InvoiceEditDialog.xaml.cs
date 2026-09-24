using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;

namespace BIS.ERP.Views.Dialogs
{
    public partial class InvoiceEditDialog : Window, INotifyPropertyChanged
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly InvoiceService _invoiceService;
        private readonly Guid? _editId;
        private readonly bool _isReadOnlyMode;
        private readonly ObservableCollection<EditableInvoiceLine> _lines = new();
        private List<Dictionary<string, object>> _accounts = new();
        private readonly Dictionary<string, ReferenceOption> _vatTaxesByCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReferenceOption> _salesTaxesByCode = new(StringComparer.OrdinalIgnoreCase);
        private string _selectedHeaderAccountCode = string.Empty;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isRecalculating;
        private bool _isPosted;
        private bool _synchronizingHeaderTaxSelection;
        private bool _isApplyingHeaderTaxValues;
        private bool _isInvoiceEditingEnabled = true;
        private bool _isApplyingCurrencyValues;
        private bool _isRestoringNormalWindowState;
        private const string DefaultSalesLineAccount = "61100000";
        private const string DefaultPurchaseLineAccount = "16100000";
        private const int AmountFractionDigits = 2;
        private static readonly char[] DecimalSeparators = { ',', '.' };
        private static readonly NumberFormatInfo AmountNumberFormat = CreateAmountNumberFormat();

        /// <summary>
        /// Конвертер редактируемой ячейки суммы: разряды — пробелом, дробная часть — запятой.
        /// Дробные значения сохраняются как введены (без принудительных «,00»), чтобы их можно было набирать.
        /// </summary>
        public static readonly IValueConverter AmountFieldConverter =
            new AmountTextConverter(AmountNumberFormat, forceDecimals: false);

        /// <summary>
        /// Конвертер вычисляемых колонок строк (НДС, НСП, итог): та же разбивка разрядов, но всегда 2 знака.
        /// </summary>
        public static readonly IValueConverter AmountDisplayConverter =
            new AmountTextConverter(AmountNumberFormat, forceDecimals: true);

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsInvoiceEditingEnabled
        {
            get => _isInvoiceEditingEnabled;
            private set
            {
                if (_isInvoiceEditingEnabled == value)
                    return;
                _isInvoiceEditingEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInvoiceEditingEnabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAmountInputReadOnly)));
            }
        }

        /// <summary>
        /// Режим только для чтения для поля суммы строки: обычный DataGrid.IsReadOnly
        /// не срабатывает для TextBox внутри DataGridTemplateColumn.
        /// </summary>
        public bool IsAmountInputReadOnly => !_isInvoiceEditingEnabled;

        public ObservableCollection<ReferenceOption> AccountItems { get; } = new();
        public ObservableCollection<ReferenceOption> VatTaxItems { get; } = new();
        public ObservableCollection<ReferenceOption> SalesTaxItems { get; } = new();

        public InvoiceEditDialog(
            MetadataObject document,
            MetadataService metadataService,
            InvoiceService invoiceService,
            Guid? editId = null,
            bool isReadOnly = false)
        {
            InitializeComponent();
            _document = document;
            ApplyAccountFieldLabels();
            _metadataService = metadataService;
            _invoiceService = invoiceService;
            _editId = editId;
            _isReadOnlyMode = isReadOnly;
            DataContext = this;
            DialogTitle.Text = isReadOnly && editId.HasValue
                ? $"Просмотр: {document.Name}"
                : editId.HasValue
                ? $"Редактирование: {document.Name}"
                : $"Новый документ: {document.Name}";
            LinesGrid.ItemsSource = _lines;
            StateChanged += OnWindowStateChanged;
            InputManager.Current.PreProcessInput += OnInvoiceDialogPreProcessInput;
            Closed += OnDialogClosed;
            Loaded += async (_, _) => await InitializeAsync();
        }


        private void ApplyAccountFieldLabels()
        {
            HeaderAccountLabel.Text = "Счет расчетов";
            LineAccountColumn.Header = GetLineAccountLabel();
        }

        private string GetLineAccountLabel()
        {
            return InvoiceDocumentTypes.IsSales(_document.Name)
                ? "Счет дохода"
                : "Счет операции";
        }
        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_isRestoringNormalWindowState || WindowState != WindowState.Maximized)
                return;

            var restoreWidth = RestoreBounds.Width > 0 && !double.IsNaN(RestoreBounds.Width)
                ? RestoreBounds.Width
                : Width;
            var restoreHeight = RestoreBounds.Height > 0 && !double.IsNaN(RestoreBounds.Height)
                ? RestoreBounds.Height
                : Height;

            try
            {
                _isRestoringNormalWindowState = true;
                WindowState = WindowState.Normal;
                Width = Math.Max(MinWidth, restoreWidth);
                Height = Math.Max(MinHeight, restoreHeight);
                CenterWindowInCurrentContext();
            }
            finally
            {
                _isRestoringNormalWindowState = false;
            }
        }

        private void CenterWindowInCurrentContext()
        {
            var workArea = SystemParameters.WorkArea;
            var windowWidth = Width > 0 && !double.IsNaN(Width) ? Width : ActualWidth;
            var windowHeight = Height > 0 && !double.IsNaN(Height) ? Height : ActualHeight;
            if (windowWidth <= 0 || double.IsNaN(windowWidth))
                windowWidth = MinWidth;
            if (windowHeight <= 0 || double.IsNaN(windowHeight))
                windowHeight = MinHeight;

            var left = workArea.Left + (workArea.Width - windowWidth) / 2;
            var top = workArea.Top + (workArea.Height - windowHeight) / 2;

            if (Owner is { IsVisible: true } && Owner.WindowState != WindowState.Maximized)
            {
                var ownerWidth = Owner.ActualWidth > 0 && !double.IsNaN(Owner.ActualWidth) ? Owner.ActualWidth : Owner.Width;
                var ownerHeight = Owner.ActualHeight > 0 && !double.IsNaN(Owner.ActualHeight) ? Owner.ActualHeight : Owner.Height;
                if (ownerWidth > 0 && ownerHeight > 0 && !double.IsNaN(Owner.Left) && !double.IsNaN(Owner.Top))
                {
                    left = Owner.Left + (ownerWidth - windowWidth) / 2;
                    top = Owner.Top + (ownerHeight - windowHeight) / 2;
                }
            }

            Left = ClampToWorkArea(left, workArea.Left, workArea.Right - windowWidth);
            Top = ClampToWorkArea(top, workArea.Top, workArea.Bottom - windowHeight);
        }

        private static double ClampToWorkArea(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return min;

            if (max < min)
                return min;

            return Math.Min(Math.Max(value, min), max);
        }
        private async Task InitializeAsync()
        {
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                await _invoiceService.EnsureSchemaAsync();
                var catalogs = await _metadataService.GetCatalogsAsync();
                _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                var assignedModuleName = await ResolveAssignedModuleNameAsync();
                var organizationsCatalog = catalogs.FirstOrDefault(item => item.Name == "Организации");
                if (organizationsCatalog != null)
                {
                    var organizations = await _metadataService.GetCatalogDataAsync(organizationsCatalog.Id);
                    var organizationItems = organizations
                        .Where(item => item.ContainsKey("Id") && Guid.TryParse(item["Id"]?.ToString(), out _))
                        .Select(CreateOrganizationItem)
                        .OrderBy(item => item.DisplayName)
                        .ToList();
                    ReferenceComboBoxSearchHelper.Attach(OrganizationCombo, organizationItems);
                    ReferencePickerControlFactory.AttachEditor(
                        OrganizationCombo,
                        _metadataService,
                        organizationsCatalog,
                        this,
                        items => { },
                        "Код организации",
                        "Наименование");
                }

                var accountsCatalog = catalogs.FirstOrDefault(item => item.Name.StartsWith("План счетов"));
                if (accountsCatalog != null)
                {
                    _accounts = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                        _document.Id,
                        _document.ObjectType);
                    FillAccountItems(_accounts);
                }

                ReferenceComboBoxSearchHelper.Attach(PaymentKindCombo, await LoadReferenceOptionsAsync(catalogs, "Виды оплаты"));
                ReferenceComboBoxSearchHelper.Attach(DeliveryKindCombo, await LoadReferenceOptionsAsync(catalogs, "Виды поставки"));
                ReferenceComboBoxSearchHelper.Attach(SupplyKindCombo, await LoadReferenceOptionsAsync(catalogs, "Типы поставки"));
                ReferenceComboBoxSearchHelper.Attach(CurrencyCombo, await LoadCurrencyOptionsAsync(catalogs));
                await LoadTaxItemsAsync(catalogs);

                if (_editId.HasValue)
                {
                    var invoice = await _invoiceService.GetInvoiceAsync(_editId.Value);
                    if (invoice == null)
                    {
                        MessageBox.Show("Документ не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        Close();
                        return;
                    }

                    _isPosted = invoice.IsPosted;
                    NumberBox.Text = invoice.DocNumber;
                    DatePicker.SelectedDate = invoice.DocDate;
                    EsfNumberBox.Text = invoice.EsfNumber;
                    TaxBlankNumberBox.Text = invoice.TaxBlankNumber;
                    ModuleCodeBox.Text = string.IsNullOrWhiteSpace(invoice.ModuleCode)
                        ? assignedModuleName
                        : invoice.ModuleCode;
                    SetHeaderAccount(invoice.CounterpartyAccountCode);
                    BasisBox.Text = invoice.Basis;
                    SelectStoredComboValue(PaymentKindCombo, invoice.PaymentKind);
                    SelectStoredComboValue(DeliveryKindCombo, NormalizeLegacyDeliveryKind(invoice.DeliveryKind));
                    SelectStoredComboValue(SupplyKindCombo, NormalizeLegacySupplyKind(invoice.SupplyKind));
                    if (invoice.CurrencyId.HasValue)
                        SelectStoredComboValue(CurrencyCombo, invoice.CurrencyId.Value.ToString());
                    ExchangeRateBox.Text = invoice.ExchangeRate > 0 ? invoice.ExchangeRate.ToString("0.####", CultureInfo.CurrentCulture) : string.Empty;
                    AmountCurrencyBox.Text = invoice.AmountCurrency > 0 ? invoice.AmountCurrency.ToString("N2", CultureInfo.CurrentCulture) : string.Empty;

                    if (invoice.OrganizationId.HasValue)
                    {
                        foreach (var item in OrganizationCombo.Items)
                        {
                            if (GetOrganizationItemId(item) == invoice.OrganizationId.Value)
                            {
                                OrganizationCombo.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    foreach (var line in invoice.Lines)
                    {
                        AddLine(new EditableInvoiceLine
                        {
                            Id = line.Id,
                            LineNumber = line.LineNumber,
                            Name = line.Name,
                            UnitName = line.UnitName,
                            Quantity = line.Quantity <= 0 ? 1m : line.Quantity,
                            AccountCode = line.AccountCode,
                            AccountDisplayName = GetAccountDisplayName(line.AccountCode),
                            VatTaxCode = ResolveTaxCode(line.VatTaxCode, line.VatRate, VatTaxItems),
                            AmountWithoutTax = line.AmountWithoutTax,
                            VatRate = line.VatRate,
                            VatAmount = line.VatAmount,
                            SalesTaxCode = ResolveTaxCode(line.SalesTaxCode, line.SalesTaxRate, SalesTaxItems),
                            SalesTaxRate = line.SalesTaxRate,
                            SalesTaxAmount = line.SalesTaxAmount
                        });
                    }

                    SyncHeaderTaxControls();
                    AllPostingsButton.IsEnabled = true;
                }
                else
                {
                    DatePicker.SelectedDate = DateTime.Today;
                    NumberBox.Text = await _invoiceService.GenerateDocumentNumberAsync();
                    ModuleCodeBox.Text = assignedModuleName;
                    SelectDefaultReference(PaymentKindCombo, PaymentKindCombo.Items.OfType<ReferenceOption>(), item => item.IsDefault, "3");
                    SelectDefaultReference(DeliveryKindCombo, DeliveryKindCombo.Items.OfType<ReferenceOption>(), item => item.IsDefault, "1");
                    SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, "НДС12");
                    SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, "WITHOUT_TAX");
                    SelectDefaultReference(SupplyKindCombo, SupplyKindCombo.Items.OfType<ReferenceOption>(), item => item.IsDefault, "1");
                }

                RecalculateTotals();
                UpdateCurrencyPanelVisibility();
                if (_isReadOnlyMode)
                    DisableReadOnlyMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private async Task<string> ResolveAssignedModuleNameAsync()
        {
            try
            {
                var assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(
                    _document.Id,
                    _document.ObjectType);
                if (!string.IsNullOrWhiteSpace(assignedModuleName))
                    return assignedModuleName.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка определения модуля документа {_document.Name}: {ex.Message}");
            }

            return InvoiceDocumentTypes.IsSales(_document.Name) || InvoiceDocumentTypes.IsPurchase(_document.Name)
                ? "Финансы"
                : string.Empty;
        }

        private void DisableEditing()
        {
            IsInvoiceEditingEnabled = false;
            NumberBox.IsReadOnly = true;
            DatePicker.IsEnabled = false;
            EsfNumberBox.IsReadOnly = true;
            TaxBlankNumberBox.IsReadOnly = true;
            ModuleCodeBox.IsReadOnly = true;
            BasisBox.IsReadOnly = true;
            OrganizationCombo.IsEnabled = false;
            PaymentKindCombo.IsEnabled = false;
            DeliveryKindCombo.IsEnabled = false;
            HeaderVatTaxCombo.IsEnabled = false;
            HeaderSalesTaxCombo.IsEnabled = false;
            SupplyKindCombo.IsEnabled = false;
            CurrencyCombo.IsEnabled = false;
            ExchangeRateBox.IsReadOnly = true;
            HeaderAccountButton.IsEnabled = false;
            LinesGrid.IsReadOnly = true;
            AddLineButton.IsEnabled = false;
            DeleteLineButton.IsEnabled = false;
            RecalculateButton.IsEnabled = false;
            SaveButton.Content = "Закрыть";
        }

        private void DisableReadOnlyMode()
        {
            DisableEditing();
            AddLineButton.Visibility = Visibility.Collapsed;
            DeleteLineButton.Visibility = Visibility.Collapsed;
            RecalculateButton.Visibility = Visibility.Collapsed;
            SaveButton.Content = "Закрыть";
            CancelButton.Visibility = Visibility.Collapsed;
            ModeHintText.Visibility = Visibility.Visible;
        }

        private void FillAccountItems(IEnumerable<Dictionary<string, object>> accounts)
        {
            AccountItems.Clear();
            foreach (var account in accounts
                         .Select(row => new ReferenceOption(
                             GetRowValue(row, "Код", "code"),
                             BuildCodeName(GetRowValue(row, "Код", "code"), GetRowValue(row, "Наименование", "name"))))
                         .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                         .OrderBy(item => item.Value))
            {
                AccountItems.Add(account);
            }
        }

        private async Task<List<ReferenceOption>> LoadReferenceOptionsAsync(
            IEnumerable<MetadataObject> catalogs,
            string catalogName)
        {
            var catalog = catalogs.FirstOrDefault(item => item.Name.Equals(catalogName, StringComparison.OrdinalIgnoreCase));
            if (catalog == null)
                return new List<ReferenceOption>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(IsActiveRow)
                .Select(row => new ReferenceOption(
                    GetRowValue(row, "Код", "code"),
                    BuildReferenceDisplayName(
                        catalogName,
                        GetRowValue(row, "Наименование", "name"),
                        GetDecimal(row, "Ставка", "rate")),
                    GetDecimal(row, "Ставка", "rate"),
                    GetInt(row, "Порядок", "sort_order"),
                    GetBool(row, "По умолчанию", "is_default"),
                    GetBool(row, "По умолчанию для НДС", "is_default_vat"),
                    GetBool(row, "По умолчанию для налога с продаж", "is_default_sales_tax")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(item => item.SortOrder ?? int.MaxValue)
                .ThenBy(item => item.DisplayName)
                .ToList();
        }

        private async Task<List<ReferenceOption>> LoadCurrencyOptionsAsync(IEnumerable<MetadataObject> catalogs)
        {
            var catalog = catalogs.FirstOrDefault(item => item.Name.Equals("Справочник валют", StringComparison.OrdinalIgnoreCase));
            if (catalog == null)
                return new List<ReferenceOption>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(IsActiveRow)
                .Where(row => Guid.TryParse(GetRowValue(row, "Id"), out _))
                .Select(row =>
                {
                    var code = GetRowValue(row, "Код", "code");
                    var name = GetRowValue(row, "Наименование", "name");
                    return new ReferenceOption(
                        GetRowValue(row, "Id"),
                        BuildCodeName(code, name),
                        Code: code,
                        IsDefault: GetBool(row, "Базовая", "is_base"));
                })
                .OrderByDescending(item => item.IsDefault)
                .ThenBy(item => item.Code)
                .ToList();
        }

        private async Task LoadTaxItemsAsync(IEnumerable<MetadataObject> catalogs)
        {
            VatTaxItems.Clear();
            SalesTaxItems.Clear();
            _vatTaxesByCode.Clear();
            _salesTaxesByCode.Clear();

            var taxes = await LoadReferenceOptionsAsync(catalogs, "Налоги");
            foreach (var tax in taxes.Where(item =>
                         item.Value.StartsWith("НДС", StringComparison.OrdinalIgnoreCase) ||
                         item.DisplayName.Contains("НДС", StringComparison.OrdinalIgnoreCase) ||
                         item.Value.Equals("WITHOUT_TAX", StringComparison.OrdinalIgnoreCase)))
            {
                VatTaxItems.Add(tax);
                _vatTaxesByCode[tax.Value] = tax;
            }

            foreach (var tax in taxes.Where(item =>
                         item.Value.Equals("SALES_TAX", StringComparison.OrdinalIgnoreCase) ||
                         item.DisplayName.Contains("продаж", StringComparison.OrdinalIgnoreCase) ||
                         item.Value.Equals("WITHOUT_TAX", StringComparison.OrdinalIgnoreCase)))
            {
                SalesTaxItems.Add(tax);
                _salesTaxesByCode[tax.Value] = tax;
            }
        }

        private void AddLine(EditableInvoiceLine line)
        {
            if (string.IsNullOrWhiteSpace(line.AccountDisplayName))
                line.AccountDisplayName = GetAccountDisplayName(line.AccountCode);

            line.PropertyChanged += OnEditableLineChanged;
            _lines.Add(line);
        }

        private void OnEditableLineChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isRecalculating || sender is not EditableInvoiceLine line)
                return;

            if (!_isApplyingHeaderTaxValues)
            {
                if (e.PropertyName == nameof(EditableInvoiceLine.VatTaxCode))
                    ApplyTaxRate(line, line.VatTaxCode, _vatTaxesByCode, isVat: true);
                if (e.PropertyName == nameof(EditableInvoiceLine.SalesTaxCode))
                    ApplyTaxRate(line, line.SalesTaxCode, _salesTaxesByCode, isVat: false);
            }

            if (!_isApplyingHeaderTaxValues &&
                e.PropertyName is nameof(EditableInvoiceLine.AmountWithoutTax)
                    or nameof(EditableInvoiceLine.VatRate)
                    or nameof(EditableInvoiceLine.SalesTaxRate)
                    or nameof(EditableInvoiceLine.VatTaxCode)
                    or nameof(EditableInvoiceLine.SalesTaxCode))
            {
                RecalculateTotals();
            }

            if (e.PropertyName == nameof(EditableInvoiceLine.AccountCode))
                UpdateCurrencyPanelVisibility();
        }

        private void ApplyTaxRate(
            EditableInvoiceLine line,
            string taxCode,
            IReadOnlyDictionary<string, ReferenceOption> taxes,
            bool isVat)
        {
            if (!taxes.TryGetValue(taxCode ?? string.Empty, out var tax))
                return;

            _isRecalculating = true;
            try
            {
                if (isVat)
                    line.VatRate = tax.Rate;
                else
                    line.SalesTaxRate = tax.Rate;
            }
            finally
            {
                _isRecalculating = false;
            }
        }

        private string GetDefaultLineAccountCode()
        {
            var preferred = InvoiceDocumentTypes.IsSales(_document.Name)
                ? DefaultSalesLineAccount
                : DefaultPurchaseLineAccount;
            return !IsSameAccount(preferred, _selectedHeaderAccountCode) &&
                   AccountItems.Any(item => item.Value.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                ? preferred
                : AccountItems.FirstOrDefault(item => !IsSameAccount(item.Value, _selectedHeaderAccountCode))?.Value
                  ?? preferred;
        }

        private static string ResolveTaxCode(string storedCode, decimal rate, IEnumerable<ReferenceOption> options)
        {
            if (!string.IsNullOrWhiteSpace(storedCode) &&
                options.Any(item => item.Value.Equals(storedCode, StringComparison.OrdinalIgnoreCase)))
                return storedCode;

            return options.FirstOrDefault(item => item.Rate == rate)?.Value
                   ?? options.FirstOrDefault(item => item.Rate == 0)?.Value
                   ?? string.Empty;
        }

        private static void SelectComboValue(ComboBox comboBox, string preferredValue)
        {
            comboBox.SelectedValue = preferredValue;
            if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        private static void SelectDefaultReference(
            ComboBox comboBox,
            IEnumerable<ReferenceOption> options,
            Func<ReferenceOption, bool> isDefaultSelector,
            string fallbackValue)
        {
            var selected = GetDefaultReferenceOption(options, isDefaultSelector, fallbackValue);
            if (selected != null)
            {
                comboBox.SelectedValue = selected.Value;
                if (comboBox.SelectedIndex >= 0)
                    return;
            }

            SelectComboValue(comboBox, fallbackValue);
        }

        private static ReferenceOption? GetDefaultReferenceOption(
            IEnumerable<ReferenceOption> options,
            Func<ReferenceOption, bool> isDefaultSelector,
            string fallbackValue)
        {
            var optionList = options.ToList();
            return optionList.FirstOrDefault(isDefaultSelector)
                   ?? optionList.FirstOrDefault(item => item.Value.Equals(fallbackValue, StringComparison.OrdinalIgnoreCase))
                   ?? optionList.FirstOrDefault();
        }

        private static void SelectStoredComboValue(ComboBox comboBox, string storedValue)
        {
            if (string.IsNullOrWhiteSpace(storedValue))
            {
                if (comboBox.Items.Count > 0)
                    comboBox.SelectedIndex = 0;
                return;
            }

            foreach (var item in comboBox.Items.OfType<ReferenceOption>())
            {
                if (item.Value.Equals(storedValue, StringComparison.OrdinalIgnoreCase) ||
                    item.DisplayName.Contains(storedValue, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedValue = storedValue;
        }

        private void SyncHeaderTaxControls(EditableInvoiceLine? line = null)
        {
            var selectedLine = line ?? LinesGrid.SelectedItem as EditableInvoiceLine ?? _lines.FirstOrDefault();
            _synchronizingHeaderTaxSelection = true;
            try
            {
                if (selectedLine != null)
                {
                    if (string.IsNullOrWhiteSpace(selectedLine.VatTaxCode))
                        SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, "НДС12");
                    else
                        SelectStoredComboValue(HeaderVatTaxCombo, selectedLine.VatTaxCode);

                    if (string.IsNullOrWhiteSpace(selectedLine.SalesTaxCode))
                        SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, "WITHOUT_TAX");
                    else
                        SelectStoredComboValue(HeaderSalesTaxCombo, selectedLine.SalesTaxCode);

                    return;
                }

                SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, "НДС12");
                SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, "WITHOUT_TAX");
            }
            finally
            {
                _synchronizingHeaderTaxSelection = false;
            }
        }

        private static ReferenceOption? GetSelectedReferenceOption(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ReferenceOption selected)
                return selected;

            var selectedValue = comboBox.SelectedValue?.ToString();
            return string.IsNullOrWhiteSpace(selectedValue)
                ? null
                : comboBox.Items
                    .OfType<ReferenceOption>()
                    .FirstOrDefault(item => item.Value.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplySelectedHeaderTaxesToLines()
        {
            var selectedVatTax = GetSelectedReferenceOption(HeaderVatTaxCombo);
            var selectedSalesTax = GetSelectedReferenceOption(HeaderSalesTaxCombo);
            if (selectedVatTax == null && selectedSalesTax == null)
                return;

            _isApplyingHeaderTaxValues = true;
            try
            {
                foreach (var line in _lines)
                {
                    if (selectedVatTax != null)
                    {
                        line.VatTaxCode = selectedVatTax.Value;
                        line.VatRate = selectedVatTax.Rate;
                    }

                    if (selectedSalesTax != null)
                    {
                        line.SalesTaxCode = selectedSalesTax.Value;
                        line.SalesTaxRate = selectedSalesTax.Rate;
                    }
                }
            }
            finally
            {
                _isApplyingHeaderTaxValues = false;
            }
        }
        private static string NormalizeLegacyDeliveryKind(string storedValue)
        {
            return storedValue?.Trim().ToUpperInvariant() switch
            {
                "1" or "2" or "3" => storedValue.Trim(),
                "GOODS" => "1",
                "SERVICE" or "SAMOVIVOZ" => "2",
                "OTHER" => "3",
                "OPT" or "ROZN" or "IMP" or "EXPORT" or
                "REMNANTS_2009" or "ZERO_SUPPLY" or "EXEMPT_SUPPLY" or
                "TAXABLE_SUPPLY" or "NON_TAXABLE_SUPPLY" or
                "STANDARD" or "EXPRESS" or "TAXABLE" => "1",
                _ => storedValue
            };
        }

        private static string NormalizeLegacySupplyKind(string storedValue)
        {
            return storedValue?.Trim().ToUpperInvariant() switch
            {
                "1" or "2" or "3" or "4" => storedValue.Trim(),
                "EXEMPT" or "WITHOUT_TAX" or "NON_TAXABLE_SUPPLY" or "EXEMPT_SUPPLY" or "ZERO_SUPPLY" => "2",
                "IMP" or "IMPORT" => "3",
                "EXPORT" => "4",
                "TAXABLE" or "TAXABLE_SUPPLY" or "STANDARD" or "EXPRESS" or "GOODS" or "SERVICE" or "OTHER" or "SAMOVIVOZ" or "OPT" or "ROZN" or "REMNANTS_2009" => "1",
                _ => string.IsNullOrWhiteSpace(storedValue) ? string.Empty : "1"
            };
        }

        private async void OnSelectAccountClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            if (_accounts.Count == 0)
                return;

            var selection = new AccountSelectionView(_accounts);
            if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(this, "Выбор счета", selection) == true && selection.SelectedAccount != null)
            {
                SetHeaderAccount(selection.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty);
            }
        }

        private async void OnSelectLineAccountClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode || sender is not Button { Tag: EditableInvoiceLine line } || _accounts.Count == 0)
                return;

            var selection = new AccountSelectionView(_accounts);

            if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(this, "Выбор счета", selection) == true && selection.SelectedAccount != null)
            {
                var accountCode = selection.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
                if (IsSameAccount(accountCode, _selectedHeaderAccountCode))
                {
                    accountCode = GetDefaultLineAccountCode();
                    MessageBox.Show(
                        $"{GetLineAccountLabel()} не должен совпадать со счетом расчетов. Подставлен счет по умолчанию.",
                        "Проверка счетов",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                line.AccountCode = accountCode;
                line.AccountDisplayName = GetAccountDisplayName(accountCode);
                UpdateCurrencyPanelVisibility();
            }
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            AddNewInvoiceLine();
        }

        /// <summary>
        /// Перехватывает клавиши до стандартной цепочки WPF-событий.
        /// DataGrid и TextBox активной ячейки могут обработать Enter или «+» раньше PreviewKeyDown.
        /// Событие подключено к InputManager, но срабатывает только при фокусе в LinesGrid.
        /// </summary>
        private void OnInvoiceDialogPreProcessInput(object sender, PreProcessInputEventArgs e)
        {
            if (_isReadOnlyMode ||
                !LinesGrid.IsEnabled ||
                e.StagingItem.Input is not KeyEventArgs keyArgs ||
                keyArgs.Key is not (Key.Add or Key.OemPlus or Key.Enter) ||
                !IsKeyboardFocusInLinesGrid())
            {
                return;
            }

            keyArgs.Handled = true;
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            AddNewInvoiceLine();
        }

        private bool IsKeyboardFocusInLinesGrid()
        {
            return IsLoaded &&
                   LinesGrid.IsVisible &&
                   LinesGrid.IsKeyboardFocusWithin;
        }

        private void OnDialogClosed(object? sender, EventArgs e)
        {
            InputManager.Current.PreProcessInput -= OnInvoiceDialogPreProcessInput;
        }

        private void AddNewInvoiceLine()
        {
            var previous = _lines.LastOrDefault();
            var accountCode = ResolveLineAccountCode(previous?.AccountCode);
            var defaultVat = GetDefaultReferenceOption(VatTaxItems, item => item.IsDefaultVat, "НДС12");
            var defaultSalesTax = GetDefaultReferenceOption(SalesTaxItems, item => item.IsDefaultSalesTax, "WITHOUT_TAX");
            var selectedHeaderVat = GetSelectedReferenceOption(HeaderVatTaxCombo);
            var selectedHeaderSalesTax = GetSelectedReferenceOption(HeaderSalesTaxCombo);
            var line = new EditableInvoiceLine
            {
                LineNumber = _lines.Count + 1,
                Name = previous?.Name ?? string.Empty,
                UnitName = previous?.UnitName ?? string.Empty,
                Quantity = previous?.Quantity > 0 ? previous.Quantity : 1m,
                AccountCode = accountCode,
                AccountDisplayName = GetAccountDisplayName(accountCode),
                VatTaxCode = selectedHeaderVat?.Value
                             ?? previous?.VatTaxCode
                             ?? defaultVat?.Value
                             ?? string.Empty,
                VatRate = selectedHeaderVat?.Rate
                          ?? previous?.VatRate
                          ?? defaultVat?.Rate
                          ?? 0,
                SalesTaxCode = selectedHeaderSalesTax?.Value
                               ?? previous?.SalesTaxCode
                               ?? defaultSalesTax?.Value
                               ?? string.Empty,
                SalesTaxRate = selectedHeaderSalesTax?.Rate
                               ?? previous?.SalesTaxRate
                               ?? defaultSalesTax?.Rate
                               ?? 0
            };
            AddLine(line);
            RecalculateTotals();
            UpdateCurrencyPanelVisibility();
            FocusAmountCell(line);
        }

        private void OnDeleteLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            if (LinesGrid.SelectedItem is not EditableInvoiceLine selected)
                return;

            _lines.Remove(selected);
            selected.PropertyChanged -= OnEditableLineChanged;
            var lineNumber = 1;
            foreach (var line in _lines)
                line.LineNumber = lineNumber++;
            RecalculateTotals();
            UpdateCurrencyPanelVisibility();
        }

        private void OnLineSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = LinesGrid.SelectedItem != null;
            DeleteLineButton.IsEnabled = hasSelection && !_isReadOnlyMode;
            LinePostingsButton.IsEnabled = hasSelection && _editId.HasValue && _isPosted;
        }

        private void OnHeaderVatTaxChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_synchronizingHeaderTaxSelection || _isReadOnlyMode)
                return;

            ApplySelectedHeaderTaxesToLines();
            RecalculateTotals();
        }

        private void OnHeaderSalesTaxChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_synchronizingHeaderTaxSelection || _isReadOnlyMode)
                return;

            ApplySelectedHeaderTaxesToLines();
            RecalculateTotals();
        }

        private async void OnLinePostingsClick(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is not EditableInvoiceLine selected || !_editId.HasValue)
                return;

            await ShowPostingsAsync(noteContains: $"строка {selected.LineNumber}:");
        }

        private async void OnAllPostingsClick(object sender, RoutedEventArgs e)
        {
            await ShowPostingsAsync();
        }

        private async Task ShowPostingsAsync(string? noteContains = null)
        {
            if (!_editId.HasValue)
                return;

            var invoice = await _invoiceService.GetInvoiceAsync(_editId.Value);
            if (invoice == null)
                return;

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var postingService = new PostingService(context);
            var postings = await postingService.GetPostingsByDocumentAsync(
                _document.Name, invoice.DocNumber, invoice.DocDate);

            if (!string.IsNullOrWhiteSpace(noteContains))
            {
                postings = postings
                    .Where(item => item.Note.Contains(noteContains, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var dialog = new DocumentPostingsDialog(
                noteContains == null ? _document.Name : $"{_document.Name} (строка)",
                invoice.DocNumber,
                postings);
            MdiDialogService.ShowInWorkspaceOrDialog(this, dialog, dialog.Title, null, fillWorkspace: true);
        }

        private void OnRecalculateClick(object sender, RoutedEventArgs e)
        {
            if (!_isReadOnlyMode)
                RecalculateTotals();
        }

        private void RecalculateTotals()
        {
            ApplySelectedHeaderTaxesToLines();

            foreach (var line in _lines)
            {
                InvoiceService.RecalculateLine(line);
                line.NotifyCalculatedProperties();
            }

            var document = BuildDocumentFromForm(applyHeaderTaxes: false);
            InvoiceService.RecalculateTotals(document);
            TotalWithoutTaxText.Text = document.AmountWithoutTax.ToString("N2");
            TotalVatText.Text = document.VatTotal.ToString("N2");
            TotalSalesTaxText.Text = document.SalesTaxTotal.ToString("N2");
            TotalAmountText.Text = document.TotalAmount.ToString("N2");
            RecalculateCurrencyAmount(document.TotalAmount);
        }

        private async void OnCurrencySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingCurrencyValues)
                return;

            await ApplyExchangeRateFromCatalogAsync();
        }

        private void OnCurrencyValueChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingCurrencyValues)
                return;

            RecalculateTotals();
        }

        private async Task ApplyExchangeRateFromCatalogAsync()
        {
            if (CurrencyPanel.Visibility != Visibility.Visible ||
                CurrencyCombo.SelectedItem is not ReferenceOption currency ||
                !Guid.TryParse(currency.Value, out var currencyId))
            {
                return;
            }

            var rate = currency.Code.Equals("KGS", StringComparison.OrdinalIgnoreCase)
                ? new CurrencyRateLookupResult(1m, DatePicker.SelectedDate ?? DateTime.Today, "Базовая валюта")
                : await _metadataService.GetCurrencyRateForDateAsync(currencyId, DatePicker.SelectedDate ?? DateTime.Today);
            if (rate == null)
                return;

            try
            {
                _isApplyingCurrencyValues = true;
                ExchangeRateBox.Text = rate.Rate.ToString("0.####", CultureInfo.CurrentCulture);
            }
            finally
            {
                _isApplyingCurrencyValues = false;
            }

            RecalculateTotals();
        }

        private void RecalculateCurrencyAmount(decimal totalAmount)
        {
            if (CurrencyPanel.Visibility != Visibility.Visible ||
                !TryReadDecimal(ExchangeRateBox.Text, out var exchangeRate) ||
                exchangeRate <= 0)
            {
                AmountCurrencyBox.Text = string.Empty;
                return;
            }

            var amountCurrency = Math.Round(totalAmount / exchangeRate, 2, MidpointRounding.AwayFromZero);
            AmountCurrencyBox.Text = amountCurrency.ToString("N2", CultureInfo.CurrentCulture);
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
            {
                Close();
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(NumberBox.Text))
                {
                    MessageBox.Show("Укажите номер документа.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!DatePicker.SelectedDate.HasValue)
                {
                    MessageBox.Show("Укажите дату документа.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_lines.Count == 0)
                {
                    MessageBox.Show("Добавьте хотя бы одну строку.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrencyPanel.Visibility == Visibility.Visible)
                {
                    if (CurrencyCombo.SelectedItem is not ReferenceOption)
                    {
                        MessageBox.Show("Для валютных счетов выберите валюту.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!TryReadDecimal(ExchangeRateBox.Text, out var exchangeRate) || exchangeRate <= 0)
                    {
                        MessageBox.Show("Для валютных счетов укажите курс больше нуля.", "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var document = BuildDocumentFromForm();
                InvoiceService.RecalculateTotals(document);
                await _invoiceService.SaveInvoiceAsync(document, _editId);
                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private InvoiceDocument BuildDocumentFromForm(bool applyHeaderTaxes = true)
        {
            if (applyHeaderTaxes)
                ApplySelectedHeaderTaxesToLines();

            var organizationId = GetOrganizationItemId(OrganizationCombo.SelectedItem);
            Guid? currencyId = null;
            if (CurrencyPanel.Visibility == Visibility.Visible &&
                CurrencyCombo.SelectedItem is ReferenceOption selectedCurrency &&
                Guid.TryParse(selectedCurrency.Value, out var parsedCurrencyId))
            {
                currencyId = parsedCurrencyId;
            }

            var useCurrency = currencyId.HasValue;
            var exchangeRate = useCurrency && TryReadDecimal(ExchangeRateBox.Text, out var parsedRate)
                ? parsedRate
                : 0m;
            var amountCurrency = useCurrency && TryReadDecimal(AmountCurrencyBox.Text, out var parsedCurrencyAmount)
                ? parsedCurrencyAmount
                : 0m;

            return new InvoiceDocument
            {
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text),
                DocDate = DatePicker.SelectedDate ?? DateTime.Today,
                EsfNumber = EsfNumberBox.Text.Trim(),
                TaxBlankNumber = TaxBlankNumberBox.Text.Trim(),
                ModuleCode = ModuleCodeBox.Text.Trim(),
                OrganizationId = organizationId,
                CounterpartyAccountCode = string.IsNullOrWhiteSpace(_selectedHeaderAccountCode)
                    ? AccountBox.Text.Trim()
                    : _selectedHeaderAccountCode,
                PaymentKind = GetSelectedReferenceValue(PaymentKindCombo),
                DeliveryKind = GetSelectedReferenceValue(DeliveryKindCombo),
                SupplyKind = GetSelectedReferenceValue(SupplyKindCombo),
                CurrencyId = currencyId,
                ExchangeRate = useCurrency ? exchangeRate : 0m,
                AmountCurrency = useCurrency ? amountCurrency : 0m,
                Basis = BasisBox.Text.Trim(),
                Lines = _lines.Select(line => new InvoiceLineRow
                {
                    Id = line.Id,
                    LineNumber = line.LineNumber,
                    Name = line.Name,
                    UnitName = line.UnitName,
                    Quantity = line.Quantity <= 0 ? 1m : line.Quantity,
                    AccountCode = ResolveLineAccountCode(line.AccountCode),
                    VatTaxCode = line.VatTaxCode,
                    AmountWithoutTax = line.AmountWithoutTax,
                    VatRate = line.VatRate,
                    VatAmount = line.VatAmount,
                    SalesTaxCode = line.SalesTaxCode,
                    SalesTaxRate = line.SalesTaxRate,
                    SalesTaxAmount = line.SalesTaxAmount
                }).ToList()
            };
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        private static string GetSelectedReferenceValue(ComboBox comboBox)
        {
            return comboBox.SelectedValue?.ToString()
                   ?? (comboBox.SelectedItem as ReferenceOption)?.Value
                   ?? comboBox.Text?.Trim()
                   ?? string.Empty;
        }

        private static Guid? GetOrganizationItemId(object? item)
        {
            return item switch
            {
                OrganizationItem organization => organization.Id,
                ReferenceItem reference => reference.Id == Guid.Empty ? null : reference.Id,
                _ => null
            };
        }
        private static string GetRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                var value = pair.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return string.Empty;
        }

        private static decimal GetDecimal(Dictionary<string, object> row, params string[] keys)
        {
            return TryReadDecimal(GetRowValue(row, keys), out var value) ? value : 0;
        }

        private static bool TryReadDecimal(string? text, out decimal value)
        {
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
                return true;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static int? GetInt(Dictionary<string, object> row, params string[] keys)
        {
            return int.TryParse(GetRowValue(row, keys), out var value) ? value : null;
        }

        private static bool GetBool(Dictionary<string, object> row, params string[] keys)
        {
            var value = GetRowValue(row, keys);
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsActiveRow(Dictionary<string, object> row)
        {
            var value = GetRowValue(row, "Активен", "is_active");
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildCodeName(string code, string name)
        {
            if (string.IsNullOrWhiteSpace(code))
                return name;
            if (string.IsNullOrWhiteSpace(name))
                return code;
            return $"{code} - {name}";
        }

        private static NumberFormatInfo CreateAmountNumberFormat()
        {
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            format.NumberGroupSeparator = " ";   // разбивка разрядов — пробелом
            format.NumberGroupSizes = new[] { 3 };
            format.NumberDecimalSeparator = ","; // дробная часть — запятой
            format.NumberDecimalDigits = AmountFractionDigits;
            format.NegativeSign = "-";
            return format;
        }

        private static bool IsDecimalSeparator(char value) => Array.IndexOf(DecimalSeparators, value) >= 0;

        /// <summary>
        /// Разбор суммы из текста: пробелы (в т.ч. неразрывные) — разделители разрядов и игнорируются,
        /// «,» и «.» равнозначны как разделитель дробной части.
        /// </summary>
        private static bool TryParseAmount(string? text, out decimal amount)
        {
            amount = 0m;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var builder = new StringBuilder(text.Length);
            var hasSeparator = false;
            foreach (var symbol in text)
            {
                if (char.IsDigit(symbol))
                {
                    builder.Append(symbol);
                    continue;
                }

                if (char.IsWhiteSpace(symbol))
                    continue;

                if (IsDecimalSeparator(symbol))
                {
                    if (hasSeparator)
                        return false;
                    hasSeparator = true;
                    builder.Append('.');
                    continue;
                }

                return false;
            }

            var normalized = builder.ToString().TrimEnd('.');
            if (normalized.Length == 0)
                return true; // введён только разделитель — считаем пустым значением

            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        private sealed class AmountTextConverter : IValueConverter
        {
            private readonly NumberFormatInfo _format;
            private readonly string _pattern;

            public AmountTextConverter(NumberFormatInfo format, bool forceDecimals)
            {
                _format = format;
                _pattern = forceDecimals ? $"N{AmountFractionDigits}" : "#,##0.##";
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var amount = value switch
                {
                    decimal decimalValue => decimalValue,
                    double doubleValue => (decimal)doubleValue,
                    int intValue => intValue,
                    _ => 0m
                };

                return amount.ToString(_pattern, _format);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return TryParseAmount(value?.ToString(), out var amount) ? amount : Binding.DoNothing;
            }
        }

        private void OnAmountPreviewTextInput(
         object sender,
         TextCompositionEventArgs e)
            {
                if (sender is not TextBox box)
                    return;

                e.Handled = !CanAcceptAmountInput(
                    box.Text ?? string.Empty,
                    e.Text);
        }

        private void OnAmountGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box)
                return;

            // WPF не поддерживает DataObject.Pasting как событие в XAML.
            // Подписка выполняется в коде, когда TextBox уже находится
            // в дереве визуальных элементов.
            DataObject.RemovePastingHandler(box, OnAmountPasting);
            DataObject.AddPastingHandler(box, OnAmountPasting);

            box.SelectAll();
        }

        private void OnAmountLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box)
                return;

            DataObject.RemovePastingHandler(box, OnAmountPasting);

            var binding = box.GetBindingExpression(TextBox.TextProperty);
            if (binding == null)
                return;

            // Разбираем введённое значение только после завершения ввода.
            // Это позволяет свободно набрать, например, «1234,56».
            binding.UpdateSource();

            if (binding.Status == BindingStatus.Active)
                binding.UpdateTarget();
        }

        private void OnAmountPasting(object sender, DataObjectPastingEventArgs e)
        {
            var pastedText = e.DataObject.GetDataPresent(DataFormats.Text)
             ? e.DataObject.GetData(DataFormats.Text) as string
             : null;

            if (!CanAcceptAmountPaste(pastedText))
            {
                e.CancelCommand();
                return;
            }

            // Вставляем только текст, без дополнительных форматов буфера.
            e.DataObject = new DataObject(DataFormats.Text, pastedText);
        }

        /// <summary>Разрешены только цифры и один разделитель дробной части (не более 2 знаков после него).</summary>
        private static bool CanAcceptAmountInput(string currentText, string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var separatorIndex = currentText.IndexOfAny(DecimalSeparators);
            var hasSeparator = separatorIndex >= 0;
            var fractionDigits = hasSeparator ? currentText.Length - separatorIndex - 1 : 0;

            foreach (var symbol in input)
            {
                if (char.IsDigit(symbol))
                {
                    if (hasSeparator)
                    {
                        if (fractionDigits >= AmountFractionDigits)
                            return false;
                        fractionDigits++;
                    }

                    continue;
                }

                if (IsDecimalSeparator(symbol))
                {
                    if (hasSeparator)
                        return false;
                    hasSeparator = true;
                    fractionDigits = 0;
                    continue;
                }

                return false; // буквы, пробелы, знаки — запрещены
            }

            return true;
        }

        private static bool CanAcceptAmountPaste(string? pastedText)
        {
            if (string.IsNullOrWhiteSpace(pastedText))
                return false;

            var digits = 0;
            var separators = 0;
            foreach (var symbol in pastedText)
            {
                if (char.IsDigit(symbol))
                {
                    digits++;
                    continue;
                }

                if (char.IsWhiteSpace(symbol))
                    continue; // пробелы как разделители разрядов при вставке «1 234,56»

                if (IsDecimalSeparator(symbol))
                {
                    separators++;
                    continue;
                }

                return false;
            }

            return digits > 0 && separators <= 1 && TryParseAmount(pastedText, out _);
        }

        private void FocusAmountCell(EditableInvoiceLine line)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LinesGrid.SelectedItem = line;
                LinesGrid.CurrentCell = new DataGridCellInfo(line, LinesGrid.Columns[2]);
                LinesGrid.ScrollIntoView(line, LinesGrid.Columns[2]);
                LinesGrid.BeginEdit();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static string BuildReferenceDisplayName(string catalogName, string name, decimal rate)
        {
            if (string.IsNullOrWhiteSpace(name))
                return !string.Equals(catalogName, "Налоги", StringComparison.OrdinalIgnoreCase) && rate == 0
                    ? string.Empty
                    : $"{rate:N2}%";

            var shouldShowRate = string.Equals(catalogName, "Налоги", StringComparison.OrdinalIgnoreCase) || rate != 0;
            if (shouldShowRate && !name.Contains('%'))
                return $"{name} ({rate:N2}%)";
            return name;
        }

        private static OrganizationItem CreateOrganizationItem(Dictionary<string, object> row)
        {
            var item = new OrganizationItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = BuildCodeName(
                    GetRowValue(row, "Код", "code", "Код организации", "organization_code"),
                    ReferenceDisplayHelper.BuildDisplayValue(row, new MetadataField()))
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

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? normalized[..separatorIndex].Trim() : normalized;
        }

        private void SetHeaderAccount(string accountCode)
        {
            _selectedHeaderAccountCode = accountCode?.Trim() ?? string.Empty;
            AccountBox.Text = GetAccountDisplayName(_selectedHeaderAccountCode);
            EnsureLineAccountsDoNotMatchHeader();
            UpdateCurrencyPanelVisibility();
        }

        private void UpdateCurrencyPanelVisibility()
        {
            var shouldShow = IsCurrencyAccount(_selectedHeaderAccountCode) ||
                             _lines.Any(line => IsCurrencyAccount(line.AccountCode));
            var visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            if (CurrencyPanel.Visibility == visibility)
                return;

            CurrencyPanel.Visibility = visibility;
            if (!shouldShow)
            {
                try
                {
                    _isApplyingCurrencyValues = true;
                    CurrencyCombo.SelectedItem = null;
                    ExchangeRateBox.Text = string.Empty;
                    AmountCurrencyBox.Text = string.Empty;
                }
                finally
                {
                    _isApplyingCurrencyValues = false;
                }
                return;
            }

            _ = ApplyExchangeRateFromCatalogAsync();
            RecalculateTotals();
        }

        private bool IsCurrencyAccount(string? accountCode)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return false;

            var settings = _accountAnalytics.GetSettingsByCode(accountCode);
            return AccountAnalyticsRules.ShouldShowField(
                "Валюта",
                new[] { settings },
                _accountAnalytics.Definitions,
                "Справочник валют",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);
        }

        private string GetAccountDisplayName(string accountCode)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return string.Empty;

            var account = _accounts.FirstOrDefault(row =>
                string.Equals(GetRowValue(row, "Код", "code"), accountCode, StringComparison.OrdinalIgnoreCase));

            if (account == null)
                return accountCode;

            return BuildCodeName(
                GetRowValue(account, "Код", "code"),
                GetRowValue(account, "Наименование", "name"));
        }

        private string ResolveLineAccountCode(string? accountCode)
        {
            var normalized = accountCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized) || IsSameAccount(normalized, _selectedHeaderAccountCode))
                return GetDefaultLineAccountCode();
            return normalized;
        }

        private void EnsureLineAccountsDoNotMatchHeader()
        {
            foreach (var line in _lines.Where(line =>
                         string.IsNullOrWhiteSpace(line.AccountCode) ||
                         IsSameAccount(line.AccountCode, _selectedHeaderAccountCode)))
            {
                var accountCode = GetDefaultLineAccountCode();
                line.AccountCode = accountCode;
                line.AccountDisplayName = GetAccountDisplayName(accountCode);
            }
        }

        private static bool IsSameAccount(string? left, string? right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   left.Trim().Equals(right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public sealed record ReferenceOption(
            string Value,
            string DisplayName,
            decimal Rate = 0,
            int? SortOrder = null,
            bool IsDefault = false,
            bool IsDefaultVat = false,
            bool IsDefaultSalesTax = false,
            string Code = "");

        private sealed class OrganizationItem : ReferenceItem
        {
            public new HashSet<string> LookupKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class EditableInvoiceLine : InvoiceLineRow, INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public new string Name
            {
                get => base.Name;
                set { base.Name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
            }

            public new string AccountCode
            {
                get => base.AccountCode;
                set { base.AccountCode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountCode))); }
            }

            private string _accountDisplayName = string.Empty;
            public string AccountDisplayName
            {
                get => _accountDisplayName;
                set { _accountDisplayName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountDisplayName))); }
            }

            public new string VatTaxCode
            {
                get => base.VatTaxCode;
                set { base.VatTaxCode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VatTaxCode))); }
            }

            public new decimal AmountWithoutTax
            {
                get => base.AmountWithoutTax;
                set { base.AmountWithoutTax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountWithoutTax))); }
            }

            public new decimal VatRate
            {
                get => base.VatRate;
                set { base.VatRate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VatRate))); }
            }

            public new decimal VatAmount
            {
                get => base.VatAmount;
                set { base.VatAmount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VatAmount))); }
            }

            public new string SalesTaxCode
            {
                get => base.SalesTaxCode;
                set { base.SalesTaxCode = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalesTaxCode))); }
            }

            public new decimal SalesTaxRate
            {
                get => base.SalesTaxRate;
                set { base.SalesTaxRate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalesTaxRate))); }
            }

            public new decimal SalesTaxAmount
            {
                get => base.SalesTaxAmount;
                set { base.SalesTaxAmount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalesTaxAmount))); }
            }

            public new decimal LineTotal
            {
                get => base.LineTotal;
                set { base.LineTotal = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal))); }
            }

            public void NotifyCalculatedProperties()
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VatAmount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SalesTaxAmount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal)));
            }
        }
    }
}



