using System;
using System.Collections;
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

        /// <summary>
        /// Идентификатор сохранённой записи. Заполняется после успешного сохранения,
        /// чтобы вызывающий раздел выделил этот счёт-фактуру в списке.
        /// </summary>
        public Guid? SavedRecordId { get; private set; }
        private readonly ObservableCollection<EditableInvoiceLine> _lines = new();
        private List<Dictionary<string, object>> _accounts = new();
        // Строки справочника валют (включая отключённые): валюта, привязанная
        // к счёту («Валюта счета»), должна попадать в список выбора на форме.
        private List<Dictionary<string, object>> _currencyRows = new();

        private readonly Dictionary<string, ReferenceOption> _vatTaxesByCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReferenceOption> _salesTaxesByCode = new(StringComparer.OrdinalIgnoreCase);
        private string _selectedHeaderAccountCode = string.Empty;
        // Организации загружаются один раз при открытии диалога и нумеруются по порядку.
        // Пользователь вводит код (№) вручную либо выбирает запись через «?».
        private readonly List<OrganizationItem> _organizations = new();
        private Guid _selectedOrganizationId = Guid.Empty;
        private MetadataObject? _organizationsCatalog;
        // Пока поле «Организация» заполняется программно (загрузка записи, выбор через «?»),
        // обработчик TextChanged не должен сбрасывать сделанный выбор.
        private bool _isApplyingOrganization;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private TaxService? _taxService;
        private bool _isInitialized;
        private bool _isRecalculating;
        private bool _isPosted;
        private bool _synchronizingHeaderTaxSelection;
        private bool _isApplyingHeaderTaxValues;
        private bool _isInvoiceEditingEnabled = true;
        private bool _isApplyingCurrencyValues;
        private bool _isRestoringNormalWindowState;
        private bool _isSynchronizingHeaderAccount;
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
            DatePicker.SelectedDateChanged += OnDocumentDateChanged;
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
                    await LoadOrganizationsAsync(organizationsCatalog);

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
                // Налоги берутся из каталога «Налоги» через TaxService: вид налога (tax_kind)
                // и дата действия задаются справочником, а не строковыми константами в коде.
                _taxService = new TaxService(_metadataService);
                await LoadTaxItemsAsync();

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

                    // Ставки действуют в ограниченном периоде, поэтому список налогов
                    // перечитывается на дату счёта-фактуры, а не на текущую дату.
                    await LoadTaxItemsAsync();
                    await EnsureTaxCodesAreAvailableAsync(invoice.Lines, invoice.DocDate);
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

                    SelectOrganizationById(invoice.OrganizationId);

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
                    SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, GetTaxFallbackCode(VatTaxItems));
                    SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, GetTaxFallbackCode(SalesTaxItems));
                    SelectDefaultReference(SupplyKindCombo, SupplyKindCombo.Items.OfType<ReferenceOption>(), item => item.IsDefault, "1");
                }

                RecalculateTotals();
                UpdateCurrencyPanelVisibility();
                _isInitialized = true;
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
            OrganizationBox.IsReadOnly = true;
            OrganizationPickerButton.IsEnabled = false;
            OrganizationAddButton.IsEnabled = false;
            OrganizationEditButton.IsEnabled = false;
            PaymentKindCombo.IsEnabled = false;
            DeliveryKindCombo.IsEnabled = false;
            HeaderVatTaxCombo.IsEnabled = false;
            HeaderSalesTaxCombo.IsEnabled = false;
            SupplyKindCombo.IsEnabled = false;
            CurrencyCombo.IsEnabled = false;
            ExchangeRateBox.IsReadOnly = true;
            RequestExchangeRateButton.IsEnabled = false;
            CurrentRateButton.IsEnabled = false;

            HeaderAccountButton.IsEnabled = false;
            HeaderAccountAddButton.IsEnabled = false;
            HeaderAccountEditButton.IsEnabled = false;
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
            _currencyRows = rows;
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

        /// <summary>
        /// Загружает налоги из каталога «Налоги» на дату документа.
        /// Вид налога определяется колонкой tax_kind, а не строковыми константами в коде.
        /// </summary>
        private async Task LoadTaxItemsAsync()
        {
            VatTaxItems.Clear();
            SalesTaxItems.Clear();
            _vatTaxesByCode.Clear();
            _salesTaxesByCode.Clear();

            if (_taxService == null)
                return;

            var documentDate = DatePicker.SelectedDate ?? DateTime.Today;

            foreach (var tax in await _taxService.GetTaxesAsync(TaxKind.Vat, documentDate))
            {
                var option = ToReferenceOption(tax);
                VatTaxItems.Add(option);
                _vatTaxesByCode[option.Value] = option;
            }

            foreach (var tax in await _taxService.GetTaxesAsync(TaxKind.Sales, documentDate))
            {
                var option = ToReferenceOption(tax);
                SalesTaxItems.Add(option);
                _salesTaxesByCode[option.Value] = option;
            }
        }

        /// <summary>
        /// Добавляет в списки налоги, уже указанные в строках документа, даже если срок их
        /// действия на дату документа истёк: иначе сохранённые значения были бы потеряны.
        /// </summary>
        private async Task EnsureTaxCodesAreAvailableAsync(
            IEnumerable<InvoiceLineRow>? lines,
            DateTime documentDate)
        {
            if (lines == null || _taxService == null)
                return;

            foreach (var line in lines)
            {
                await EnsureTaxCodeIsAvailableAsync(line.VatTaxCode, TaxKind.Vat, documentDate);
                await EnsureTaxCodeIsAvailableAsync(line.SalesTaxCode, TaxKind.Sales, documentDate);
            }
        }

        private async Task EnsureTaxCodeIsAvailableAsync(
            string? code,
            TaxKind kind,
            DateTime documentDate)
        {
            if (string.IsNullOrWhiteSpace(code) || _taxService == null)
                return;

            var isVat = kind == TaxKind.Vat;
            var items = isVat ? VatTaxItems : SalesTaxItems;
            var map = isVat ? _vatTaxesByCode : _salesTaxesByCode;
            var normalized = code.Trim();

            if (map.ContainsKey(normalized))
                return;

            var tax = await _taxService.ResolveAsync(normalized, kind, documentDate);
            if (tax == null)
                return;

            var option = ToReferenceOption(tax);
            items.Add(option);
            map[option.Value] = option;
        }

        /// <summary>
        /// Перечитывает налоги при смене даты документа: ставки действуют в ограниченном периоде
        /// (колонки valid_from / valid_to справочника «Налоги»).
        /// </summary>
        private async void OnDocumentDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isReadOnlyMode || !_isInitialized || _taxService == null)
                return;

            var previousVatCode = GetSelectedReferenceValue(HeaderVatTaxCombo);
            var previousSalesTaxCode = GetSelectedReferenceValue(HeaderSalesTaxCombo);

            try
            {
                var documentDate = DatePicker.SelectedDate ?? DateTime.Today;

                await LoadTaxItemsAsync();
                await EnsureTaxCodesAreAvailableAsync(_lines, documentDate);

                RestoreOrSelectTaxDefault(HeaderVatTaxCombo, VatTaxItems, previousVatCode, item => item.IsDefaultVat);
                RestoreOrSelectTaxDefault(HeaderSalesTaxCombo, SalesTaxItems, previousSalesTaxCode, item => item.IsDefaultSalesTax);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка перечитывания налогов на дату документа: {ex.Message}");
            }
        }

        private static ReferenceOption ToReferenceOption(TaxOption tax)
        {
            return new ReferenceOption(
                tax.Code,
                BuildReferenceDisplayName(TaxService.CatalogName, tax.Name, tax.Rate),
                tax.Rate,
                tax.SortOrder,
                false,
                tax.IsDefaultVat,
                tax.IsDefaultSalesTax,
                tax.Code);
        }

        /// <summary>
        /// Сохраняет прежний выбор налога, если он ещё доступен, иначе выбирает налог по умолчанию.
        /// </summary>
        private static void RestoreOrSelectTaxDefault(
            ComboBox comboBox,
            IEnumerable<ReferenceOption> options,
            string? previousValue,
            Func<ReferenceOption, bool> isDefaultSelector)
        {
            var items = options.ToList();

            if (!string.IsNullOrWhiteSpace(previousValue) &&
                items.Any(item => item.Value.Equals(previousValue, StringComparison.OrdinalIgnoreCase)))
            {
                SelectStoredComboValue(comboBox, previousValue);
                return;
            }

            SelectDefaultReference(comboBox, items, isDefaultSelector, GetTaxFallbackCode(items));
        }

        /// <summary>
        /// Код налога для случая, когда в справочнике не задан флаг «По умолчанию».
        /// </summary>
        private static string GetTaxFallbackCode(IReadOnlyList<ReferenceOption> options)
        {
            return options.FirstOrDefault()?.Value ?? string.Empty;
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

            // Пока пользователь набирает код счёта в строке, панель валют не
            // переключается: смена видимости перестраивает колонки таблицы и
            // уводит фокус из грида на первый элемент формы. Панель обновится
            // по факту ухода из поля (OnLineAccountLostFocus) или выбора «?».
            if (e.PropertyName == nameof(EditableInvoiceLine.AccountCode) &&
                !IsEditingLineAccountCode())
            {
                UpdateCurrencyPanelVisibility();
            }

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

        /// <summary>
        /// Счёт строки не подставляется автоматически: его выбирает пользователь
        /// кнопкой «?» в строке либо вводит вручную.
        /// </summary>
        private string GetDefaultLineAccountCode()
        {
            return string.Empty;
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
                        SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, GetTaxFallbackCode(VatTaxItems));
                    else
                        SelectStoredComboValue(HeaderVatTaxCombo, selectedLine.VatTaxCode);

                    if (string.IsNullOrWhiteSpace(selectedLine.SalesTaxCode))
                        SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, GetTaxFallbackCode(SalesTaxItems));
                    else
                        SelectStoredComboValue(HeaderSalesTaxCombo, selectedLine.SalesTaxCode);

                    return;
                }

                SelectDefaultReference(HeaderVatTaxCombo, VatTaxItems, item => item.IsDefaultVat, GetTaxFallbackCode(VatTaxItems));
                SelectDefaultReference(HeaderSalesTaxCombo, SalesTaxItems, item => item.IsDefaultSalesTax, GetTaxFallbackCode(SalesTaxItems));
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

        /// <summary>
        /// Добавляет новый счёт в справочник «План счетов» и подставляет его
        /// в поле «Счет расчетов».
        /// </summary>
        private async void OnAddAccountClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var catalog = await TryGetChartOfAccountsCatalogAsync();
                if (catalog == null)
                    return;

                var dialog = new CatalogItemDialog(catalog, _metadataService);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        this, dialog, $"Добавление: {catalog.Name}") != true)
                {
                    return;
                }

                Cursor = Cursors.Wait;
                var createdId = await _metadataService.CreateDynamicRecordAsync(catalog.Id, dialog.ItemData);
                await ReloadAccountsAsync();

                SetHeaderAccount(FindAccountCodeById(createdId));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления счёта: {ex.Message}", "План счетов",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        /// <summary>
        /// Изменяет счёт, указанный в поле «Счет расчетов», через форму записи.
        /// </summary>
        private async void OnEditAccountClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedHeaderAccountCode))
            {
                MessageBox.Show(
                    "Сначала выберите или введите счёт расчётов.",
                    "Проверка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var catalog = await TryGetChartOfAccountsCatalogAsync();
                if (catalog == null)
                    return;

                var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
                var row = rows.FirstOrDefault(item =>
                    item.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var id) &&
                    id == FindAccountIdByCode(_selectedHeaderAccountCode));
                if (row == null)
                {
                    MessageBox.Show("Выбранный счёт не найден в справочнике.", catalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(catalog, _metadataService, row);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        this, dialog, $"Редактирование: {catalog.Name}") != true)
                {
                    return;
                }

                Cursor = Cursors.Wait;
                await _metadataService.UpdateDynamicRecordAsync(catalog.Id, FindAccountIdByCode(_selectedHeaderAccountCode), dialog.ItemData);
                await ReloadAccountsAsync();
                SetHeaderAccount(_selectedHeaderAccountCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения счёта: {ex.Message}", "План счетов",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private async Task<MetadataObject?> TryGetChartOfAccountsCatalogAsync()
        {
            var catalogs = await _metadataService.GetCatalogsAsync();
            var catalog = catalogs.FirstOrDefault(item =>
                item.ObjectType == "Catalog" &&
                item.Name.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase));

            if (catalog == null)
            {
                MessageBox.Show("Справочник «План счетов» не найден.", "План счетов",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return catalog;
        }

        private async Task ReloadAccountsAsync()
        {
            _accounts = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                _document.Id,
                _document.ObjectType);
            FillAccountItems(_accounts);
        }

        private Guid FindAccountIdByCode(string accountCode)
        {
            var match = _accounts.FirstOrDefault(row =>
                string.Equals(GetRowValue(row, "Код", "code"), accountCode?.Trim(), StringComparison.OrdinalIgnoreCase));

            return match != null && Guid.TryParse(match.GetValueOrDefault("Id")?.ToString(), out var id)
                ? id
                : Guid.Empty;
        }

        private string FindAccountCodeById(Guid accountId)
        {
            if (accountId == Guid.Empty)
                return string.Empty;

            var match = _accounts.FirstOrDefault(row =>
                Guid.TryParse(row.GetValueOrDefault("Id")?.ToString(), out var id) && id == accountId);

            return match == null ? string.Empty : GetRowValue(match, "Код", "code");
        }

        private void OnAccountCodePreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsAsciiDigits(e.Text);
        }

        private void OnAccountCodePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private void OnAccountCodePasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsAsciiDigits(text))
                e.CancelCommand();
        }

        private void OnAccountCodeTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSynchronizingHeaderAccount)
                return;

            _selectedHeaderAccountCode = string.Empty;
            UpdateCurrencyPanelVisibility();
        }

        private async Task<bool> TryResolveHeaderAccountAsync()
        {
            if (!string.IsNullOrWhiteSpace(_selectedHeaderAccountCode))
                return true;

            var code = AccountBox.Text?.Trim() ?? string.Empty;
            if (code.Length == 0 ||
                code.Length > 8 ||
                code.Any(character => character is < '0' or > '9'))
            {
                MessageBox.Show(
                    "Счет расчетов должен содержать от 1 до 8 цифр.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                AccountBox.Focus();
                AccountBox.SelectAll();
                return false;
            }

            var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                _document.Id,
                _document.ObjectType);
            var match = accountsData.FirstOrDefault(row =>
                row.TryGetValue("Код", out var codeValue) &&
                string.Equals(codeValue?.ToString()?.Trim(), code, StringComparison.Ordinal));

            var accountCode = match?.GetValueOrDefault("Код")?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                MessageBox.Show(
                    $"Счёт с кодом «{code}» не найден в плане счетов для этого модуля.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                AccountBox.Focus();
                AccountBox.SelectAll();
                return false;
            }

            SetHeaderAccount(accountCode);
            return true;
        }

        private static bool IsAsciiDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                if (character is < '0' or > '9')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Поле «Счет дохода»: только цифры, как и в «Счете расчетов».
        /// </summary>
        private void OnLineAccountPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsAsciiDigits(e.Text);
        }

        private void OnLineAccountPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private void OnLineAccountPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsAsciiDigits(text))
                e.CancelCommand();
        }

        /// <summary>
        /// После ввода кода вручную подставляет наименование счёта и проверяет,
        /// что такой счёт есть в плане счетов этого модуля.
        /// </summary>
        private void OnLineAccountLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox { DataContext: EditableInvoiceLine line })
                return;

            var code = line.AccountCode?.Trim() ?? string.Empty;
            if (code.Length == 0)
            {
                line.AccountDisplayName = string.Empty;
                return;
            }

            if (IsSameAccount(code, _selectedHeaderAccountCode))
            {
                MessageBox.Show(
                    $"{GetLineAccountLabel()} не должен совпадать со счетом расчетов. Укажите другой счёт.",
                    "Проверка счетов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                line.AccountCode = string.Empty;
                line.AccountDisplayName = string.Empty;
                return;
            }

            var account = _accounts.FirstOrDefault(row =>
                string.Equals(GetRowValue(row, "Код", "code"), code, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                MessageBox.Show(
                    $"Счёт с кодом «{code}» не найден в плане счетов для этого модуля.",
                    "Проверка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                line.AccountCode = string.Empty;
                line.AccountDisplayName = string.Empty;
                return;
            }

            line.AccountDisplayName = GetAccountDisplayName(code);
            UpdateCurrencyPanelVisibility();
            _ = ApplyAccountCurrencyAsync();
        }


        private void OnSelectLineAccountClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: EditableInvoiceLine line })
                return;

            SelectLineAccountAsync(line);
        }

        private async void SelectLineAccountAsync(EditableInvoiceLine line)
        {
            if (_isReadOnlyMode || _accounts.Count == 0)
                return;

            var selection = new AccountSelectionView(_accounts);

            if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(this, "Выбор счета", selection) == true && selection.SelectedAccount != null)
            {
                var accountCode = selection.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
                if (IsSameAccount(accountCode, _selectedHeaderAccountCode))
                {
                    MessageBox.Show(
                        $"{GetLineAccountLabel()} не должен совпадать со счетом расчетов. Выберите другой счёт.",
                        "Проверка счетов",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                line.AccountCode = accountCode;
                line.AccountDisplayName = GetAccountDisplayName(accountCode);
                UpdateCurrencyPanelVisibility();
                _ = ApplyAccountCurrencyAsync();

            }
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            AddNewInvoiceLine();
        }

        /// <summary>
        /// Клавиши в гриде строк счёт-фактуры:
        /// Enter — переход к следующей ячейке (колонке) строки,
        /// «+» (в т.ч. правый плюс клавиатуры) — новая строка.
        /// PreviewKeyDown срабатывает и когда фокус в TextBox активной ячейки,
        /// поэтому ввод работает одинаково в любой колонке.
        /// </summary>
        private void OnLinesGridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isReadOnlyMode || !LinesGrid.IsEnabled)
                return;

            if (e.Key is Key.Add or Key.OemPlus)
            {
                e.Handled = true;
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                AddNewInvoiceLine();
                return;
            }

            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MoveToNextLineCell();
        }

        /// <summary>
        /// Переводит фокус на следующую редактируемую ячейку строки, а с последней —
        /// на первую ячейку следующей строки. Вычисляемые колонки (НДС, НСП, итог)
        /// пропускаются: в них нет поля ввода.
        /// </summary>
        private void MoveToNextLineCell()
        {
            var rowIndex = LinesGrid.Items.IndexOf(LinesGrid.CurrentItem ?? LinesGrid.SelectedItem);
            if (rowIndex < 0)
                return;

            var columnIndex = LinesGrid.CurrentCell.Column?.DisplayIndex ?? 0;
            for (var step = columnIndex + 1; step < LinesGrid.Columns.Count; step++)
            {
                if (!IsEditableColumn(step))
                    continue;

                FocusLineCell(LinesGrid.Items[rowIndex], step);
                return;
            }

            for (var nextRow = rowIndex + 1; nextRow < LinesGrid.Items.Count; nextRow++)
            {
                var firstEditable = Enumerable.Range(0, LinesGrid.Columns.Count)
                    .FirstOrDefault(IsEditableColumn, -1);
                if (firstEditable < 0)
                    return;

                FocusLineCell(LinesGrid.Items[nextRow], firstEditable);
                return;
            }
        }

        /// <summary>
        /// Колонка доступна для ввода: не помечена как «только чтение» и не является
        /// вычисляемой (НДС, НСП, итог).
        /// </summary>
        private bool IsEditableColumn(int displayColumnIndex)
        {
            var column = LinesGrid.Columns
                .FirstOrDefault(current => current.DisplayIndex == displayColumnIndex);

            return column is { IsReadOnly: false, Visibility: Visibility.Visible };
        }

        /// <summary>
        /// Ставит фокус ячейки, а затем — фокус ввода (редактор) внутри неё.
        /// Одного BeginEdit недостаточно: клавиатурный фокус остаётся на DataGrid.
        /// </summary>
        private void FocusLineCell(object item, int displayColumnIndex, bool defer = false)
        {
            var column = LinesGrid.Columns
                .FirstOrDefault(current => current.DisplayIndex == displayColumnIndex);
            if (item == null || column == null)
                return;

            void Apply()
            {
                LinesGrid.SelectedItem = item;
                LinesGrid.CurrentCell = new DataGridCellInfo(item, column);
                LinesGrid.ScrollIntoView(item, column);
                LinesGrid.BeginEdit();
            }

            // Для новой строки ждём окончания текущей операции ввода,
            // для перехода по Enter переключаем ячейку сразу.
            if (defer)
                Dispatcher.BeginInvoke(new Action(Apply), System.Windows.Threading.DispatcherPriority.Background);
            else
                Apply();

            RequestEditorFocus(item, column);
        }

        /// <summary>
        /// Запрашивает фокус редактора ячейки. Контейнер ячейки и сам редактор
        /// создаются асинхронно, а DataGrid перехватывает фокус обратно при входе
        /// в режим правки, поэтому делаем несколько попыток с проверкой результата.
        /// </summary>
        private void RequestEditorFocus(object item, DataGridColumn column, int attempt = 0)
        {
            if (attempt > 12)
                return;

            Dispatcher.BeginInvoke(
                new Action(() => TryFocusEditor(item, column, attempt)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void TryFocusEditor(object item, DataGridColumn column, int attempt)
        {
            if (LinesGrid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
            {
                RequestEditorFocus(item, column, attempt + 1);
                return;
            }

            var cell = FindCell(row, column);
            if (cell == null)
            {
                RequestEditorFocus(item, column, attempt + 1);
                return;
            }

            // Редактор может появиться позже (DataGrid откладывает вход в режим
            // правки), поэтому вешаемся на загрузку ячейки и ставим фокус сразу,
            // как только TextBox появится в её дереве.
            if (attempt < 4)
            {
                cell.Loaded += OnFocusedCellLoaded;
            }

            if (!FocusFirstEditableElement(cell))
            {
                LinesGrid.BeginEdit();
                RequestEditorFocus(item, column, attempt + 1);
                return;
            }

            if (!IsKeyboardFocusInCell(cell))
                RequestEditorFocus(item, column, attempt + 1);
        }

        /// <summary>
        /// TextBox создан — передаём ему клавиатурный фокус.
        /// </summary>
        private void OnFocusedCellLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGridCell cell)
            {
                cell.Loaded -= OnFocusedCellLoaded;
                FocusFirstEditableElement(cell);
            }
        }

        private static bool IsKeyboardFocusInCell(DataGridCell cell)
        {
            if (cell.IsKeyboardFocusWithin)
                return true;

            return Keyboard.FocusedElement is DependencyObject focused &&
                   (ReferenceEquals(focused, cell) || IsVisualDescendant(cell, focused));
        }

        private static bool IsVisualDescendant(DependencyObject parent, DependencyObject candidate)
        {
            if (parent is not Visual visual)
                return false;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);
                if (ReferenceEquals(child, candidate) || IsVisualDescendant(child, candidate))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// DataGridRow.Cells и DataGridCellsPanel закрыты, поэтому контейнер ячейки
        /// берётся через ItemContainerGenerator панели ячеек по порядковому номеру
        /// среди видимых колонок.
        /// </summary>
        /// <summary>
        /// Контейнер ячейки ищется обходом визуального дерева строки: ячейки в нём
        /// расположены слева направо в порядке видимых колонок.
        /// </summary>
        private DataGridCell? FindCell(DataGridRow row, DataGridColumn column)
        {
            var targetIndex = GetVisibleColumnIndex(column);
            if (targetIndex < 0)
                return null;

            return FindCells(row)
                .ElementAtOrDefault(targetIndex);
        }

        private int GetVisibleColumnIndex(DataGridColumn column)
        {
            var visibleIndex = 0;
            foreach (var current in LinesGrid.Columns)
            {
                if (current.Visibility != Visibility.Visible)
                    continue;

                if (ReferenceEquals(current, column))
                    return visibleIndex;

                visibleIndex++;
            }

            return -1;
        }

        /// <summary>Все контейнеры ячеек строки в порядке следования колонок.</summary>
        private static IEnumerable<DataGridCell> FindCells(DependencyObject parent)
        {
            if (parent is not Visual visual)
                yield break;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);
                if (child is DataGridCell cell)
                    yield return cell;

                foreach (var nested in FindCells(child))
                    yield return nested;
            }
        }

        /// <summary>
        /// Ставит клавиатурный фокус на первый редактируемый элемент ячейки
        /// (обычно это вложенный TextBox) и выделяет его содержимое.
        /// </summary>
        private static bool FocusFirstEditableElement(DependencyObject parent)
        {
            if (parent is not Visual visual)
                return false;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
            {
                var child = VisualTreeHelper.GetChild(visual, index);
                if (child is TextBox textBox && !textBox.IsReadOnly && textBox.IsEnabled)
                {
                    textBox.Focus();
                    Keyboard.Focus(textBox);
                    textBox.SelectAll();
                    return true;
                }

                if (FocusFirstEditableElement(child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Вход в грид сразу ставит курсор в колонку «Наименование».
        /// </summary>
        private void OnLinesGridGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (_isReadOnlyMode || !LinesGrid.IsEnabled || LinesGrid.Items.Count == 0)
                return;

            // Реагируем только на вход фокуса в сам грид/строку, а не на переключение
            // фокуса между TextBox соседних ячеек.
            if (e.OriginalSource is not DataGrid && e.OriginalSource is not DataGridRow)
                return;

            var current = LinesGrid.CurrentCell;
            var columnIndex = current.Column?.DisplayIndex ?? -1;
            var rowIndex = current.Item == null ? -1 : LinesGrid.Items.IndexOf(current.Item);
            if (columnIndex == 0 && rowIndex >= 0)
                return;

            var row = current.Item as object ?? LinesGrid.Items[rowIndex >= 0 ? rowIndex : 0];
            FocusLineCell(row, 0);
        }

        private void OnDialogClosed(object? sender, EventArgs e)
        {
        }

        private void AddNewInvoiceLine()
        {
            var previous = _lines.LastOrDefault();
            // Счёт строки не наследуется и не подставляется: его выбирает пользователь.
            var accountCode = string.Empty;
            var defaultVat = GetDefaultReferenceOption(VatTaxItems, item => item.IsDefaultVat, GetTaxFallbackCode(VatTaxItems));
            var defaultSalesTax = GetDefaultReferenceOption(SalesTaxItems, item => item.IsDefaultSalesTax, GetTaxFallbackCode(SalesTaxItems));
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
            RecalculateLineCurrencyAmounts();
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

                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                if (!TryFocusFirstEmptyAmount(out var emptyAmountLine))
                {
                    MessageBox.Show(
                        $"В строке {emptyAmountLine?.LineNumber ?? 0} не указана сумма «Без налогов». " +
                        "Пустое поле означает 0 и не допускает сохранения.",
                        "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!await TryResolveHeaderAccountAsync())
                    return;

                if (!ValidateOrganizationInput())
                    return;

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
                SavedRecordId = await _invoiceService.SaveInvoiceAsync(document, _editId);
                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Пустая сумма «Без налогов» равна нулю: такую строку сохранить нельзя.
        /// Курсор ставится в её колонку суммы.
        /// </summary>
        private bool TryFocusFirstEmptyAmount(out EditableInvoiceLine? emptyLine)
        {
            emptyLine = _lines.FirstOrDefault(line => line.AmountWithoutTax == 0m);
            if (emptyLine == null)
                return true;

            var amountColumn = LinesGrid.Columns.FirstOrDefault(column =>
                column.Header?.ToString() == "Без налогов");
            if (amountColumn != null)
                FocusLineCell(emptyLine, amountColumn.DisplayIndex);

            return false;
        }

        private InvoiceDocument BuildDocumentFromForm(bool applyHeaderTaxes = true)
        {
            if (applyHeaderTaxes)
                ApplySelectedHeaderTaxesToLines();

            var organizationId = _selectedOrganizationId == Guid.Empty ? (Guid?)null : _selectedOrganizationId;
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

        //------------------------------------------------------------------------
        // --- Организация: ручной ввод кода (№) либо выбор через «?» ---

        /// <summary>
        /// Читает справочник «Организации» и присваивает записям порядковые номера:
        /// именно этот номер (или код организации) пользователь вводит вручную.
        /// </summary>
        private async Task LoadOrganizationsAsync(MetadataObject organizationsCatalog)
        {
            _organizationsCatalog = organizationsCatalog;
            await ReloadOrganizationsAsync();
        }

        /// <summary>
        /// Перечитывает справочник «Организации» и заново проставляет порядковые номера:
        /// после правки записи наименование меняется, а значит меняется и её номер.
        /// </summary>
        private async Task ReloadOrganizationsAsync()
        {
            if (_organizationsCatalog == null)
                return;

            var organizations = await _metadataService.GetCatalogDataAsync(_organizationsCatalog.Id);

            _organizations.Clear();
            _organizations.AddRange(organizations
                .Where(item => item.ContainsKey("Id") && Guid.TryParse(item["Id"]?.ToString(), out _))
                .Select(CreateOrganizationItem)
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase));

            for (var index = 0; index < _organizations.Count; index++)
                _organizations[index].Number = index + 1;
        }

        private async void OnSelectOrganizationClick(object sender, RoutedEventArgs e)
        {
            if (_organizations.Count == 0)
            {
                MessageBox.Show(
                    "Справочник «Организации» пуст. Добавьте записи в справочнике.",
                    "Организация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rows = _organizations
                .Select(item => new Dictionary<string, object>
                {
                    ["Код"] = item.Code,
                    ["Наименование"] = item.Name,
                    ["Полное наименование"] = item.FullName,
                    ["ИНН"] = item.Inn,
                    ["Id"] = item.Id
                })
                .ToList();

            var selection = new ReferenceSelectionDialog(rows, "Код", "Наименование")
            {
                Width = 1100,
                MinWidth = 800,
                Height = 520
            };
            // Как и в остальных формах: окно выбора позволяет добавить или изменить
            // запись справочника, не закрывая его.
            if (_organizationsCatalog != null)
                selection.ConfigureCatalogEditing(_organizationsCatalog, _metadataService);

            if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, selection, "Выбор организации") != true ||
                selection.SelectedItem == null ||
                !selection.SelectedItem.TryGetValue("Id", out var idValue) ||
                !Guid.TryParse(idValue?.ToString(), out var organizationId))
            {
                // Записи могли добавить/изменить прямо в окне выбора — обновляем список.
                await ReloadOrganizationsAsync();
                return;
            }

            await ReloadOrganizationsAsync();
            ApplyOrganizationSelection(_organizations.FirstOrDefault(item => item.Id == organizationId));
        }

        /// <summary>
        /// Добавляет новую организацию в справочник и сразу выбирает её.
        /// </summary>
        private async void OnAddOrganizationClick(object sender, RoutedEventArgs e)
        {
            if (_organizationsCatalog == null)
            {
                MessageBox.Show(
                    "Справочник «Организации» не найден.",
                    "Организация", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new CatalogItemDialog(_organizationsCatalog, _metadataService);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
                    return;

                Cursor = Cursors.Wait;
                var createdId = await _metadataService.CreateDynamicRecordAsync(
                    _organizationsCatalog.Id,
                    dialog.ItemData);

                await ReloadOrganizationsAsync();
                ApplyOrganizationSelection(_organizations.FirstOrDefault(item => item.Id == createdId));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        /// <summary>
        /// Изменяет выбранную запись справочника «Организации» через форму записи.
        /// </summary>
        private async void OnEditOrganizationClick(object sender, RoutedEventArgs e)
        {
            if (_organizationsCatalog == null)
            {
                MessageBox.Show(
                    "Справочник «Организации» не найден.",
                    "Организация", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedOrganizationId == Guid.Empty)
            {
                MessageBox.Show(
                    "Сначала выберите или введите организацию.",
                    "Организация", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Cursor = Cursors.Wait;
                var rows = await _metadataService.GetCatalogDataAsync(_organizationsCatalog.Id);
                var row = rows.FirstOrDefault(item =>
                    item.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var id) &&
                    id == _selectedOrganizationId);

                if (row == null)
                {
                    MessageBox.Show(
                        "Выбранная запись справочника не найдена.",
                        _organizationsCatalog.Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(_organizationsCatalog, _metadataService, row);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
                    return;

                await _metadataService.UpdateDynamicRecordAsync(
                    _organizationsCatalog.Id,
                    _selectedOrganizationId,
                    dialog.ItemData);

                await ReloadOrganizationsAsync();
                ApplyOrganizationSelection(_organizations.FirstOrDefault(item => item.Id == _selectedOrganizationId));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        /// <summary>
        /// Приводит введённое значение к записи справочника: сначала точное совпадение
        /// с кодом организации, затем — порядковый номер из списка «?».
        /// </summary>
        private bool TryParseOrganizationInput(out OrganizationItem? organization)
        {
            organization = null;
            var text = OrganizationBox?.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return false;

            // Поле уже содержит «Код - Наименование» (его подставляет сам диалог),
            // поэтому сравниваем и с полным отображаемым значением.
            organization = _organizations.FirstOrDefault(item =>
                string.Equals(item.DisplayName, text, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.Code) &&
                 string.Equals(item.Code.Trim(), text, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(item.Name) &&
                 string.Equals(item.Name.Trim(), text, StringComparison.OrdinalIgnoreCase)));

            if (organization != null)
                return true;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                organization = _organizations.FirstOrDefault(item => item.Number == number);

            return organization != null;
        }

        /// <summary>
        /// Подставляет в поле отображаемое значение организации без повторного
        /// срабатывания TextChanged.
        /// </summary>
        private void ApplyOrganizationText(OrganizationItem? organization)
        {
            _isApplyingOrganization = true;
            try
            {
                OrganizationBox.Text = organization == null
                    ? string.Empty
                    : organization.DisplayName;
                OrganizationBox.ToolTip = BuildOrganizationToolTip(organization);
            }
            finally
            {
                _isApplyingOrganization = false;
            }
        }

        /// <summary>
        /// Проверяет введённый код (№) перед сохранением. Пустое поле допустимо —
        /// тогда организация не задаётся (как и раньше при незаполненном списке).
        /// </summary>
        private bool ValidateOrganizationInput()
        {
            if (_selectedOrganizationId != Guid.Empty)
                return true;

            var text = OrganizationBox?.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return true;

            MessageBox.Show(
                $"Организация «{text}» не найдена в справочнике. Нажмите «?» и уточните код (№) записи.",
                "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
            OrganizationBox?.Focus();
            OrganizationBox?.SelectAll();
            return false;
        }
        /// <summary>Заполняет поле и состояние выбранной организацией.</summary>
        private void ApplyOrganizationSelection(OrganizationItem? organization)
        {
            if (organization == null)
                return;

            _selectedOrganizationId = organization.Id;
            ApplyOrganizationText(organization);
        }

        /// <summary>Показывает в поле организацию, сохранённую в документе.</summary>
        private void SelectOrganizationById(Guid? organizationId)
        {
            _selectedOrganizationId = organizationId ?? Guid.Empty;
            ApplyOrganizationText(_organizations.FirstOrDefault(item => item.Id == _selectedOrganizationId));
        }

        private static string BuildOrganizationToolTip(OrganizationItem? organization)
        {
            const string hint = "Введите код или № организации из списка «?» (до 8 цифр)";
            if (organization == null)
                return hint;

            var name = !string.IsNullOrWhiteSpace(organization.Name)
                ? organization.Name
                : organization.DisplayName;
            return string.IsNullOrWhiteSpace(organization.FullName)
                ? $"№{organization.Number}: {name}"
                : $"№{organization.Number}: {organization.FullName}";
        }

        private void OnOrganizationNumberGotFocus(object sender, RoutedEventArgs e)
        {
            // В поле уже подставлено «Код - Наименование»: выделяем его целиком,
            // чтобы новый ввод кода заменял прежнее значение целиком.
            if (sender is TextBox box && box.Text.Length > 0)
                box.SelectAll();
        }

        private void OnOrganizationNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsOrganizationNumberDigits(e.Text);
        }

        private void OnOrganizationNumberPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Пробел не приходит через PreviewTextInput — отсекаем отдельно.
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private void OnOrganizationNumberPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsOrganizationNumberDigits(text))
                e.CancelCommand();
        }

        /// <summary>
        /// Ручной ввод сразу приводит код (№) к записи справочника и подставляет
        /// в поле «Код - Наименование» — так видно, какая именно организация выбрана.
        /// </summary>
        private void OnOrganizationNumberTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingOrganization)
                return;

            if (TryParseOrganizationInput(out var organization) && organization != null)
            {
                _selectedOrganizationId = organization.Id;
                if (OrganizationBox != null)
                {
                    OrganizationBox.ToolTip = BuildOrganizationToolTip(organization);
                    if (!string.Equals(OrganizationBox.Text, organization.DisplayName, StringComparison.Ordinal))
                        ApplyOrganizationText(organization);
                }

                return;
            }

            _selectedOrganizationId = Guid.Empty;
            if (OrganizationBox != null)
                OrganizationBox.ToolTip = "Введите код или № организации из списка «?» (до 8 цифр)";
        }

        private static bool IsOrganizationNumberDigits(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            foreach (var character in text)
            {
                if (character is < '0' or > '9')
                    return false;
            }

            return true;
        }
        //------------------------------------------------------------------------

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

                // Ноль показываем пустым полем: значит, значение ещё не введено.
                if (amount == 0m)
                    return string.Empty;

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
            // Новая строка начинается с колонки «Наименование».
            FocusLineCell(line, 0, defer: true);
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
            var code = GetRowValue(row, "Код", "code", "Код организации", "organization_code");
            var name = GetRowValue(row, "Наименование", "name");
            if (string.IsNullOrWhiteSpace(name))
                name = ReferenceDisplayHelper.BuildDisplayValue(row, new MetadataField());

            var item = new OrganizationItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                Code = code,
                Name = name,
                FullName = GetRowValue(row, "Полное наименование", "full_name"),
                Inn = GetRowValue(row, "ИНН", "inn"),
                DisplayName = BuildCodeName(code, name)
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
            var normalizedCode = accountCode?.Trim() ?? string.Empty;
            _isSynchronizingHeaderAccount = true;
            try
            {
                AccountBox.Text = _isReadOnlyMode
                    ? GetAccountDisplayName(normalizedCode)
                    : normalizedCode;
            }
            finally
            {
                _isSynchronizingHeaderAccount = false;
            }

            _selectedHeaderAccountCode = normalizedCode;
            EnsureLineAccountsDoNotMatchHeader();
            UpdateCurrencyPanelVisibility();
        }

        private void UpdateCurrencyPanelVisibility()
        {
            var shouldShow = IsCurrencyAccount(_selectedHeaderAccountCode) ||
                             _lines.Any(line => IsCurrencyAccount(line.AccountCode)) ||
                             ResolveDocumentCurrencyId() != null;
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

                ClearLineCurrencyAmounts();
                return;
            }

            // Валюта привязана к счёту: подставляем её в список выбора и
            // подтягиваем курс из справочника курсов валют.
            _ = ApplyAccountCurrencyAsync();
            _ = ApplyExchangeRateFromCatalogAsync();
            RecalculateTotals();
        }

        /// <summary>
        /// Валюта счёта из поля «Валюта счета» плана счетов: сначала по
        /// идентификатору, а при его отсутствии — по коду валюты из подписи.
        /// </summary>
        private Guid? ResolveAccountCurrencyId(string? accountCode)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return null;

            var code = accountCode.Trim();
            var account = _accounts.FirstOrDefault(row =>
                string.Equals(GetRowValue(row, "Код", "code"), code, StringComparison.OrdinalIgnoreCase));
            if (account == null)
                return null;

            var currencyValue = GetRowValue(account, "Валюта счета", "account_currency_id");
            if (string.IsNullOrWhiteSpace(currencyValue))
                return null;

            if (Guid.TryParse(currencyValue, out var currencyId) && currencyId != Guid.Empty)
                return currencyId;

            // Ссылка может прийти подписью («USD - Доллар США») — ищем по коду.
            var currencyCode = currencyValue.Split(" - ")[0].Trim();
            var currencyRow = _currencyRows.FirstOrDefault(row =>
                string.Equals(GetRowValue(row, "Код", "code"), currencyCode, StringComparison.OrdinalIgnoreCase));
            var resolvedId = GetRowValue(currencyRow ?? new Dictionary<string, object>(), "Id");
            return Guid.TryParse(resolvedId, out var id) && id != Guid.Empty ? id : null;
        }

        /// <summary>Валюта документа: валюта счёта строки, иначе счёта расчётов.</summary>
        private Guid? ResolveDocumentCurrencyId()
        {
            foreach (var accountCode in _lines.Select(line => line.AccountCode).Append(_selectedHeaderAccountCode))
            {
                var currencyId = ResolveAccountCurrencyId(accountCode);
                if (currencyId != null)
                    return currencyId;
            }

            return null;
        }

        /// <summary>
        /// Добавляет валюту в список выбора, если её там нет: валюта может быть
        /// отключена в справочнике, но привязана к счёту.
        /// </summary>
        private void EnsureCurrencyOption(Guid currencyId)
        {
            if (FindCurrencyOption(currencyId) != null)
                return;

            var row = _currencyRows.FirstOrDefault(currency =>
                Guid.TryParse(GetRowValue(currency, "Id"), out var id) && id == currencyId);
            if (row == null)
                return;

            var code = GetRowValue(row, "Код", "code");
            var option = new ReferenceOption(
                currencyId.ToString(),
                BuildCodeName(code, GetRowValue(row, "Наименование", "name")),
                Code: code,
                IsDefault: GetBool(row, "Базовая", "is_base"));

            // У списка задан ItemsSource (представление коллекции), поэтому
            // элемент добавляется в исходный список, а не в Items.
            if (CurrencyCombo.ItemsSource is ICollectionView view)
            {
                if (view.SourceCollection is not IList source)
                    return;

                source.Add(option);
                view.Refresh();
                return;
            }

            CurrencyCombo.Items.Add(option);
        }


        private ReferenceOption? FindCurrencyOption(Guid currencyId)
        {
            return CurrencyCombo.Items
                .OfType<ReferenceOption>()
                .FirstOrDefault(item => Guid.TryParse(item.Value, out var id) && id == currencyId);
        }

        /// <summary>
        /// Подставляет валюту, привязанную к счёту документа, если она отличается
        /// от выбранной, и обновляет курс. Если у счёта своей валюты нет
        /// (ResolveDocumentCurrencyId() == null), выбор пользователя сохраняется.
        /// </summary>
        private async Task ApplyAccountCurrencyAsync()
        {
            var currencyId = ResolveDocumentCurrencyId();
            if (currencyId == null)
                return;

            EnsureCurrencyOption(currencyId.Value);
            if (TryGetSelectedCurrencyId(out var selectedCurrencyId) && selectedCurrencyId == currencyId.Value)
                return;

            try
            {
                _isApplyingCurrencyValues = true;
                SelectStoredComboValue(CurrencyCombo, currencyId.Value.ToString());
            }
            finally
            {
                _isApplyingCurrencyValues = false;
            }

            if (CurrencyCombo.SelectedItem == null)
                return;

            await ApplyExchangeRateFromCatalogAsync();
        }


        /// <summary>
        /// Кнопка «?» у курса: курс на дату документа из справочника курсов валют,
        /// при его отсутствии предлагается загрузить актуальные курсы НБКР.
        /// </summary>
        private async void OnRequestExchangeRateClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            if (!TryGetSelectedCurrencyId(out var currencyId))
            {
                MessageBox.Show("Сначала выберите валюту.", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rateDate = (DatePicker.SelectedDate ?? DateTime.Today).Date;
            var rate = await ResolveExchangeRateAsync(currencyId, rateDate, interactive: true);
            if (rate == null)
            {
                MessageBox.Show(
                    $"Курс валюты на {rateDate:dd/MM/yyyy} не найден. Введите курс вручную.",
                    "Курс валюты", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyExchangeRate(rate);
            ShowRateHint(rate);
        }

        /// <summary>Кнопка «Текущий курс»: последний известный курс валюты.</summary>

        private async void OnApplyCurrentRateClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            if (!TryGetSelectedCurrencyId(out var currencyId))
            {
                MessageBox.Show("Сначала выберите валюту.", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rate = IsBaseCurrencyCode()
                ? new CurrencyRateLookupResult(1m, DateTime.Today, "Базовая валюта")
                : await _metadataService.GetLatestCurrencyRateAsync(currencyId);
            if (rate == null)
                rate = await ResolveExchangeRateAsync(currencyId, DateTime.Today, interactive: true);

            if (rate == null)
            {
                MessageBox.Show(
                    "Текущий курс валюты не найден. Введите курс вручную или нажмите «?».",
                    "Курс валюты", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyExchangeRate(rate);
            ShowRateHint(rate);
        }

        private void ShowRateHint(CurrencyRateLookupResult rate)
        {
            CurrencyRateHintText.Text = string.IsNullOrWhiteSpace(rate.Source)
                ? rate.RateDate.ToString("dd/MM/yyyy")
                : $"{rate.Source} на {rate.RateDate:dd/MM/yyyy}";
        }

        private bool TryGetSelectedCurrencyId(out Guid currencyId)

        {
            currencyId = Guid.Empty;
            return CurrencyCombo.SelectedItem is ReferenceOption option &&
                   Guid.TryParse(option.Value, out currencyId) &&
                   currencyId != Guid.Empty;
        }

        private bool IsBaseCurrencyCode()
        {
            return CurrencyCombo.SelectedItem is ReferenceOption currency &&
                   currency.Code.Equals("KGS", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Курс валюты на заданную дату. При interactive=true и отсутствии
        /// курса предлагается загрузить актуальные курсы НБКР.
        /// </summary>
        private async Task<CurrencyRateLookupResult?> ResolveExchangeRateAsync(
            Guid currencyId,
            DateTime rateDate,
            bool interactive)
        {
            if (IsBaseCurrencyCode())
                return new CurrencyRateLookupResult(1m, rateDate, "Базовая валюта");

            var rate = await _metadataService.GetCurrencyRateForDateAsync(currencyId, rateDate);
            if (rate != null || !interactive)
                return rate;

            var answer = MessageBox.Show(
                $"Курс на {rateDate:dd/MM/yyyy} не найден в справочнике курсов валют. Загрузить актуальные курсы НБКР?",
                "Курс валюты",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return null;

            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                var results = await _metadataService.ImportOfficialCurrencyRatesAsync(rateDate.AddDays(-7), rateDate);
                var refreshed = await _metadataService.GetCurrencyRateForDateAsync(currencyId, rateDate);
                if (refreshed != null)
                    return refreshed;

                MessageBox.Show(
                    $"Курсы загружены (добавлено: {results.Sum(item => item.Imported)}, " +
                    $"пропущено: {results.Sum(item => item.Skipped)}), но курс на {rateDate:dd/MM/yyyy} отсутствует.",
                    "Курс валюты",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить курсы НБКР: {ex.Message}", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            finally
            {
                Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        private void ApplyExchangeRate(CurrencyRateLookupResult rate)
        {
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

        private void ClearLineCurrencyAmounts()
        {
            foreach (var line in _lines)
                line.AmountInCurrency = 0m;
        }

        /// <summary>
        /// Сумма каждой строки в валюте документа (колонка «В валюте»):
        /// итог строки, делённый на курс шапки.
        /// </summary>
        private void RecalculateLineCurrencyAmounts()
        {
            var exchangeRate = 0m;
            var hasRate = CurrencyPanel.Visibility == Visibility.Visible &&
                          TryReadDecimal(ExchangeRateBox.Text, out exchangeRate) &&
                          exchangeRate > 0;
            foreach (var line in _lines)

            {
                line.AmountInCurrency = hasRate
                    ? Math.Round(line.LineTotal / exchangeRate, 2, MidpointRounding.AwayFromZero)
                    : 0m;
            }
        }

        /// <summary>
        /// Идёт ли ввод кода счёта в строке: пока пользователь набирает код,
        /// панель валют не переключается, иначе фокус уходит из грида.
        /// </summary>
        private static bool IsEditingLineAccountCode()
        {
            // LineAccountBox объявлен внутри DataTemplate, поэтому у него нет
            // поля в сгенерированном классе — сверяемся по имени элемента.
            return Keyboard.FocusedElement is TextBox { Name: "LineAccountBox" };

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
            if (IsSameAccount(normalized, _selectedHeaderAccountCode))
                return string.Empty;
            return normalized;
        }

        private void EnsureLineAccountsDoNotMatchHeader()
        {
            foreach (var line in _lines.Where(line =>
                         !string.IsNullOrWhiteSpace(line.AccountCode) &&
                         IsSameAccount(line.AccountCode, _selectedHeaderAccountCode)))
            {
                // Счёт строки не подставляем автоматически — очищаем,
                // чтобы пользователь выбрал его явно.
                line.AccountCode = string.Empty;
                line.AccountDisplayName = string.Empty;
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

            /// <summary>Порядковый номер записи в списке «?» (нумерация с 1).</summary>
            public int Number { get; set; }

            /// <summary>Код организации из справочника (может быть пустым).</summary>
            public string Code { get; set; } = string.Empty;

            /// <summary>Наименование организации без кода.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Полное наименование организации.</summary>
            public string FullName { get; set; } = string.Empty;

            /// <summary>ИНН организации.</summary>
            public string Inn { get; set; } = string.Empty;
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

            /// <summary>Итог строки в валюте документа (колонка «В валюте»).</summary>
            private decimal _amountInCurrency;
            public decimal AmountInCurrency
            {
                get => _amountInCurrency;
                set
                {
                    if (_amountInCurrency == value)
                        return;

                    _amountInCurrency = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AmountInCurrency)));
                }
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



