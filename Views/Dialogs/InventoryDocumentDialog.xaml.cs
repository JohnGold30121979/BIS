using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views.Dialogs
{
    /// <summary>
    /// Окно документа ТМЗ по образцу счёта-фактуры: шапка, карточка «Поставщик»,
    /// налоги справа, таблица строк и итоги.
    /// </summary>
    public partial class InventoryDocumentDialog : Window
    {
        private const string DateMask = "dd/MM/yyyy";

        private sealed class RefItem
        {
            public object Id { get; init; } = Guid.Empty;
            public string Display { get; init; } = string.Empty;
        }

        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly InventoryDocumentLineService _lineService;
        private readonly InventoryDocumentModel.DocumentDefinition _definition;
        private readonly Guid? _recordId;
        private readonly Dictionary<string, object?> _existing;

        private readonly ObservableCollection<InventoryLineRow> _lines = new();
        private readonly Dictionary<string, Control> _editors = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Guid?> _references = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBox> _referenceBoxes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StackPanel> _referencePanels = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Dictionary<string, object>>> _rowsByColumn = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, Dictionary<string, object>> _materialRows = new();
        private readonly Dictionary<Guid, string> _entryOperationKinds = new();
        private ComboBox? _entryEditor;

        private readonly TaxService _taxService;
        private readonly Dictionary<Guid, decimal> _taxRates = new();
        private readonly Dictionary<Guid, bool> _vatDefaults = new();
        private readonly Dictionary<Guid, bool> _salesTaxDefaults = new();
        private Guid? _defaultPaymentId;
        private ComboBox? _vatCombo;
        private ComboBox? _salesTaxCombo;
        private ComboBox? _paymentCombo;
        private TextBox? _vatRateBox;
        private TextBox? _salesTaxRateBox;
        private DatePicker? _dateBox;
        private List<Dictionary<string, object>> _taxRows = new();

        private StackPanel? _currencyPanel;

        public Guid SavedRecordId { get; private set; }

        public InventoryDocumentDialog(
            MetadataObject document,
            MetadataService metadataService,
            AppDbContext context,
            Guid? recordId = null,
            Dictionary<string, object>? existingData = null)
        {
            InitializeComponent();

            _document = document;
            _metadataService = metadataService;
            _lineService = new InventoryDocumentLineService(context);
            _taxService = new TaxService(metadataService);
            _recordId = recordId;
            _existing = existingData == null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : existingData.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value, StringComparer.OrdinalIgnoreCase);

            _definition = InventoryDocumentModel.Documents.FirstOrDefault(item =>
                              string.Equals(item.Name, document.Name, StringComparison.OrdinalIgnoreCase))
                          ?? InventoryDocumentModel.Documents[0];

            Title = recordId.HasValue
                ? $"Документ ТМЗ: {document.Name}"
                : $"Новый документ ТМЗ: {document.Name}";

            LinesGrid.ItemsSource = _lines;
            Loaded += async (_, _) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var catalogs = await _metadataService.GetCatalogsAsync();

                var accounts = await LoadRowsAsync(catalogs, "План счетов");
                var organizations = await LoadRowsAsync(catalogs, "Организации");
                var employees = await LoadRowsAsync(catalogs, "Сотрудники (Списочный состав)");
                var supplyKinds = await LoadRowsAsync(catalogs, "Типы поставки");
                var taxRows = await LoadRowsAsync(catalogs, "Налоги");
                _taxRows = taxRows;
                var paymentKinds = await LoadRowsAsync(catalogs, "Виды оплаты");
                var currencies = await LoadRowsAsync(catalogs, "Справочник валют");
                var sites = await LoadRowsAsync(catalogs, "Участки");
                var responsible = await LoadRowsAsync(catalogs, "МОЛ");
                var materials = await LoadRowsAsync(catalogs, "Справочник материалов");
                var entries = await LoadRowsAsync(catalogs, InventoryDocumentModel.EntryCatalogName);

                BuildTopRow(entries);
                BuildSupplierCard(accounts, organizations, employees, supplyKinds, sites, responsible);

                var documentDate = GetDocumentDate();
                var vatItems = await BuildTaxItemsAsync(TaxKind.Vat, taxRows, documentDate);
                var salesItems = await BuildTaxItemsAsync(TaxKind.Sales, taxRows, documentDate);
                var paymentItems = BuildPaymentItems(paymentKinds);

                BuildTaxCard(vatItems, salesItems, paymentItems, currencies);

                MaterialColumn.ItemsSource = ToItems(materials, row =>
                {
                    var id = ReadGuid(row);
                    if (id.HasValue)
                        _materialRows[id.Value] = row;
                    return $"{Read(row, "Код")} - {Read(row, "Наименование материала", "Наименование")}".Trim(' ', '-');
                });
                AccountColumn.ItemsSource = ToItems(accounts, AccountDisplay, idAsCode: true);
                SiteColumn.ItemsSource = ToItems(sites, row => Read(row, "Наименование"));

                ApplyExistingValues();

                if (!_recordId.HasValue)
                    ApplyDefaultSelections();

                ApplyTaxRates();
                UpdateAccountDependencies();

                if (!_recordId.HasValue &&
                    _editors.TryGetValue("doc_number", out var numberEditor) &&
                    numberEditor is TextBox numberBox &&
                    string.IsNullOrWhiteSpace(numberBox.Text))
                {
                    numberBox.Text = await _metadataService.GetNextDocumentNumberAsync(_document);
                }

                await LoadLinesAsync();
                UpdateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // ---------- Шапка ----------

        private void BuildTopRow(List<Dictionary<string, object>> entries)
        {
            AddText(HeaderTopPanel, "Номер", "doc_number", 110);
            _dateBox = AddDateText(HeaderTopPanel, "Дата", "doc_date", 130);
            _dateBox.LostFocus += async (_, _) => await ReloadTaxesAsync();
            _entryEditor = AddCombo(HeaderTopPanel, "Вид операции", "document_entry_id", entries,
                row =>
                {
                    var id = ReadGuid(row);
                    if (id.HasValue)
                        _entryOperationKinds[id.Value] = Read(row, "operation_kind", "Код операции", "code", "Код");
                    return Read(row, "Наименование");
                }, 250);
            AddText(HeaderTopPanel, "№ пачки", "pack_number", 80);
            AddText(HeaderTopPanel, "Серия/№ бланка", "tax_blank_number", 130);
            AddText(HeaderTopPanel, "Номер ЭСФ", "esf_number", 150);
        }

        /// <summary>
        /// Перезагружает записи справочника «Ввод нового документа ТМЗ»
        /// (catalog_inventory_document_entries) из базы данных.
        /// Очищает кэш _entryCodes и обновляет ComboBox вида операции.
        /// </summary>
        private async Task RefreshEntriesAsync()
        {
            try
            {
                var catalogs = await _metadataService.GetCatalogsAsync();
                var entries = await LoadRowsAsync(catalogs, InventoryDocumentModel.EntryCatalogName);

                _entryOperationKinds.Clear();
                foreach (var row in entries)
                {
                    var id = ReadGuid(row);
                    if (id.HasValue)
                        _entryOperationKinds[id.Value] = Read(row, "operation_kind", "Код операции", "code", "Код");
                }

                if (_entryEditor != null)
                {
                    _entryEditor.ItemsSource = ToItems(entries, BuildEntryDisplay);
                    _entryEditor.SelectedItem = null;
                }

                _rowsByColumn["document_entry_id"] = entries;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось обновить справочник видов операций: {ex.Message}",
                    "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string BuildEntryDisplay(Dictionary<string, object> row)
        {
            var code = Read(row, "Код", "Код");
            var name = Read(row, "Наименование", "Наименование");
            return (code + " - " + name).Trim(' ', '-');
        }

        private void BuildSupplierCard(
            List<Dictionary<string, object>> accounts,
            List<Dictionary<string, object>> organizations,
            List<Dictionary<string, object>> employees,
            List<Dictionary<string, object>> supplyKinds,
            List<Dictionary<string, object>> sites,
            List<Dictionary<string, object>> responsible)
        {
            AddReference(SupplierPanel, "Счет", "counterparty_account", 260, accounts);
            AddReference(SupplierPanel, "Организация", "organization_id", 260, organizations);
            AddReference(SupplierPanel, "Сотрудник", "employee_id", 220, employees);
            AddReference(SupplierPanel, "Вид покупки", "supply_kind", 220, supplyKinds);

            // Организация и сотрудник показываются по привязкам счёта.
            SetPanelVisibility("organization_id", false);
            SetPanelVisibility("employee_id", false);
            SetPanelVisibility("supply_kind", true);

            // Для внутреннего перемещения нужны получатель и его МОЛ.
            if (string.Equals(_definition.Name, InventoryDocumentModel.MovementName, StringComparison.OrdinalIgnoreCase))
            {
                AddReference(SupplierPanel, "Участок-получатель", "destination_site_id", 220, sites);
                AddReference(SupplierPanel, "МОЛ-получатель", "destination_person_id", 220, responsible);
            }
        }

        private void BuildTaxCard(
            List<RefItem> vatItems,
            List<RefItem> salesItems,
            List<RefItem> paymentItems,
            List<Dictionary<string, object>> currencies)
        {
            _vatCombo = AddComboItems(TaxPanel, "Вид НДС", "vat_type", vatItems, 220);
            _vatRateBox = AddText(TaxPanel, "Ставка НДС, %", "vat_rate", 80);
            _paymentCombo = AddComboItems(TaxPanel, "Вид оплаты", "payment_kind", paymentItems, 180);
            _salesTaxCombo = AddComboItems(TaxPanel, "Налог", "sales_tax_type", salesItems, 220);
            _salesTaxRateBox = AddText(TaxPanel, "Ставка налога, %", "sales_tax_rate", 80);

            if (_vatCombo != null)
                _vatCombo.SelectionChanged += (_, _) => ApplyTaxRate(_vatCombo, _vatRateBox);
            if (_salesTaxCombo != null)
                _salesTaxCombo.SelectionChanged += (_, _) => ApplyTaxRate(_salesTaxCombo, _salesTaxRateBox);

            _currencyPanel = new StackPanel { Orientation = Orientation.Horizontal };
            AddCombo(_currencyPanel, "Валюта", "currency_id", currencies,
                row => Read(row, "Наименование", "Код"), 150);
            AddText(_currencyPanel, "Курс", "exchange_rate", 90);
            _currencyPanel.Visibility = Visibility.Collapsed;
            TaxPanel.Children.Add(_currencyPanel);
        }

        private ComboBox AddComboItems(Panel panel, string label, string column, List<RefItem> items, double width)
        {
            var combo = new ComboBox
            {
                Width = width,
                Height = 28,
                ItemsSource = items,
                DisplayMemberPath = "Display",
                SelectedValuePath = "Id"
            };
            AddEditor(panel, label, column, combo);
            return combo;
        }

        /// <summary>
        /// Налоги — через TaxService, как в счёте-фактуре: фильтр по виду (НДС/НСП)
        /// и дате документа. В RefItem попадают только налоги, найденные в каталоге.
        /// </summary>
        private async Task<List<RefItem>> BuildTaxItemsAsync(
            TaxKind kind,
            List<Dictionary<string, object>> taxRows,
            DateTime documentDate)
        {
            var options = await _taxService.GetTaxesAsync(kind, documentDate);

            var byCode = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in taxRows)
            {
                var id = ReadGuid(row);
                var code = Read(row, "Код", "code");
                if (id.HasValue && !string.IsNullOrWhiteSpace(code) && !byCode.ContainsKey(code))
                    byCode[code] = id.Value;
            }

            var result = new List<RefItem>();
            foreach (var option in options)
            {
                if (!byCode.TryGetValue(option.Code, out var id))
                    continue;

                _taxRates[id] = option.Rate;
                if (kind == TaxKind.Vat)
                    _vatDefaults[id] = option.IsDefaultVat;
                else
                    _salesTaxDefaults[id] = option.IsDefaultSalesTax;

                var display = option.Name.Contains('%')
                    ? option.Name
                    : $"{option.Name} ({option.Rate:N2}%)";

                result.Add(new RefItem { Id = id, Display = display });
            }

            return result;
        }

        /// <summary>
        /// Виды оплаты: по умолчанию — строка с is_default (как в счёте-фактуре),
        /// запасной вариант — «безналичный перевод».
        /// </summary>
        private List<RefItem> BuildPaymentItems(List<Dictionary<string, object>> rows)
        {
            var result = new List<RefItem>();

            foreach (var row in rows)
            {
                var id = ReadGuid(row);
                if (!id.HasValue)
                    continue;

                result.Add(new RefItem { Id = id.Value, Display = Read(row, "Наименование") });

                if (_defaultPaymentId == null && (IsTrue(row, "По умолчанию") || IsTrue(row, "is_default")))
                    _defaultPaymentId = id.Value;
            }

            if (_defaultPaymentId == null)
            {
                var fallback = result.FirstOrDefault(item =>
                    item.Display.Contains("безналич", StringComparison.OrdinalIgnoreCase));
                if (fallback != null && fallback.Id is Guid fallbackId)
                    _defaultPaymentId = fallbackId;
            }

            return result;
        }

        private void ApplyDefaultSelections()
        {
            if (_vatCombo != null)
            {
                var vatId = _vatDefaults.FirstOrDefault(pair => pair.Value).Key;
                if (_vatDefaults.ContainsKey(vatId))
                    _vatCombo.SelectedValue = vatId;
            }

            if (_salesTaxCombo != null)
            {
                var salesId = _salesTaxDefaults.FirstOrDefault(pair => pair.Value).Key;
                if (_salesTaxDefaults.ContainsKey(salesId))
                    _salesTaxCombo.SelectedValue = salesId;
            }

            if (_paymentCombo != null && _defaultPaymentId.HasValue)
                _paymentCombo.SelectedValue = _defaultPaymentId.Value;

            ApplyTaxRates();
        }

        private void ApplyTaxRates()
        {
            ApplyTaxRate(_vatCombo, _vatRateBox);
            ApplyTaxRate(_salesTaxCombo, _salesTaxRateBox);
        }

        private void ApplyTaxRate(ComboBox? combo, TextBox? rateBox)
        {
            if (combo?.SelectedValue is not Guid id || rateBox == null)
                return;

            if (_taxRates.TryGetValue(id, out var rate))
                rateBox.Text = rate.ToString("0.##", CultureInfo.CurrentCulture);
        }

        private DateTime GetDocumentDate() => _dateBox?.SelectedDate?.Date ?? DateTime.Today;

        /// <summary>Перечитывает налоги при смене даты: ставки действуют в ограниченном периоде.</summary>
        private async Task ReloadTaxesAsync()
        {
            if (_taxRows.Count == 0 || _vatCombo == null || _salesTaxCombo == null)
                return;

            try
            {
                var vatId = _vatCombo.SelectedValue is Guid vat ? vat : (Guid?)null;
                var salesId = _salesTaxCombo.SelectedValue is Guid sales ? sales : (Guid?)null;

                _taxRates.Clear();
                _vatDefaults.Clear();
                _salesTaxDefaults.Clear();

                var date = GetDocumentDate();
                var vatItems = await BuildTaxItemsAsync(TaxKind.Vat, _taxRows, date);
                var salesItems = await BuildTaxItemsAsync(TaxKind.Sales, _taxRows, date);

                _vatCombo.ItemsSource = vatItems;
                _salesTaxCombo.ItemsSource = salesItems;

                if (vatId.HasValue && vatItems.Any(item => item.Id is Guid id && id == vatId.Value))
                    _vatCombo.SelectedValue = vatId.Value;
                if (salesId.HasValue && salesItems.Any(item => item.Id is Guid id && id == salesId.Value))
                    _salesTaxCombo.SelectedValue = salesId.Value;

                ApplyTaxRates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Инвентаризация: ошибка перечитывания налогов: {ex.Message}");
            }
        }

        // ---------- Построители полей ----------

        private TextBox AddText(Panel panel, string label, string column, double width)
        {
            var box = new TextBox { Width = width, Height = 28, Padding = new Thickness(6, 3, 6, 3) };
            AddEditor(panel, label, column, box);
            return box;
        }

        private DatePicker AddDateText(Panel panel, string label, string column, double width)
        {
            // Дата — настоящий DatePicker: глобальный DatePickerInputMaskBehavior (App.xaml.cs)
            // сам даёт маску/шаблон DD/MM/YYYY и календарь. Ручную маску и NormalizeDateText не нужно.
            var datePicker = new DatePicker
            {
                Width = width,
                Height = 28,
                ToolTip = "Формат: ДД/ММ/ГГГГ"
            };

            AddEditor(panel, label, column, datePicker);
            return datePicker;
        }

        private ComboBox AddCombo(
            Panel panel,
            string label,
            string column,
            List<Dictionary<string, object>> rows,
            Func<Dictionary<string, object>, string> display,
            double width)
        {
            _rowsByColumn[column] = rows;
            var combo = new ComboBox
            {
                Width = width,
                Height = 28,
                ItemsSource = ToItems(rows, display),
                DisplayMemberPath = "Display",
                SelectedValuePath = "Id"
            };
            AddEditor(panel, label, column, combo);
            return combo;
        }

        private void AddReference(
            Panel panel,
            string label,
            string column,
            double width,
            List<Dictionary<string, object>> rows)
        {
            _rowsByColumn[column] = rows;

            var container = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            container.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });

            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new TextBox { Width = width, Height = 28, Padding = new Thickness(6, 3, 6, 3) };
            var pick = new Button
            {
                Content = "?",
                Width = 30,
                Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Выбрать из справочника"
            };

            pick.Click += async (_, _) => await PickReferenceAsync(column, box, rows);
            box.TextChanged += (_, _) =>
            {
                _references[column] = null;
                if (string.Equals(column, "counterparty_account", StringComparison.OrdinalIgnoreCase))
                    UpdateAccountDependencies();
            };

            line.Children.Add(box);
            line.Children.Add(pick);
            container.Children.Add(line);
            panel.Children.Add(container);

            _referenceBoxes[column] = box;
            _referencePanels[column] = container;
        }

        private void AddEditor(Panel panel, string label, string column, Control control)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            container.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            container.Children.Add(control);
            panel.Children.Add(container);
            _editors[column] = control;
        }

        private async Task PickReferenceAsync(
            string column,
            TextBox box,
            List<Dictionary<string, object>> rows)
        {
            if (rows.Count == 0)
                return;

            // Приводим строки к единым полям, чтобы диалог выбора показывал их одинаково.
            var pickRows = rows.Select(row => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = row.TryGetValue("Id", out var id) ? id : DBNull.Value,
                ["Код"] = Read(row, "Код", "code"),
                ["Наименование"] = Read(row, "Наименование", "ФИО", "Полное наименование", "Наименование материала")
            }).ToList();

            var dialog = new ReferenceSelectionDialog(pickRows, "Код", "Наименование");
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                    Window.GetWindow(this), dialog, "Выбор значения") != true ||
                dialog.SelectedItem == null)
            {
                return;
            }

            var selected = dialog.SelectedItem;
            var text = Read(selected, "Наименование");
            var code = Read(selected, "Код");
            box.Text = string.IsNullOrWhiteSpace(code) ? text : $"{code} - {text}".Trim(' ', '-');
            _references[column] = ReadGuid(selected);

            if (string.Equals(column, "counterparty_account", StringComparison.OrdinalIgnoreCase))
            {
                _references[column] = null;
                UpdateAccountDependencies();
            }
        }

        // ---------- Зависимости счёта ----------

        private Dictionary<string, object>? FindAccountRow()
        {
            if (!_referenceBoxes.TryGetValue("counterparty_account", out var box) ||
                !_rowsByColumn.TryGetValue("counterparty_account", out var rows))
            {
                return null;
            }

            var text = box.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return null;

            var code = text.Split(" - ")[0].Trim();
            return rows.FirstOrDefault(row =>
                string.Equals(Read(row, "Код"), code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(AccountDisplay(row), text, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateAccountDependencies()
        {
            var account = FindAccountRow();
            var linkOrganizations = account != null && IsTrue(account, "Связь с организациями");
            var linkEmployees = account != null && IsTrue(account, "Связь со списочным составом");
            var linkCurrency = account != null && IsTrue(account, "Связь с валютами");

            SetPanelVisibility("organization_id", linkOrganizations);
            SetPanelVisibility("employee_id", linkEmployees);
            if (_currencyPanel != null)
                _currencyPanel.Visibility = linkCurrency ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetPanelVisibility(string column, bool visible)
        {
            if (_referencePanels.TryGetValue(column, out var panel))
                panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- Значения ----------

        private void ApplyExistingValues()
        {
            if (_existing.Count == 0)
            {
                SetDateEditor("doc_date", DateTime.Today);
                return;
            }

            foreach (var field in _document.Fields)
            {
                if (!_existing.TryGetValue(field.Name, out var value) || value == null || value == DBNull.Value)
                    continue;

                if (_referenceBoxes.TryGetValue(field.DbColumnName, out var referenceBox))
                {
                    referenceBox.Text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                    continue;
                }

                if (!_editors.TryGetValue(field.DbColumnName, out var control))
                    continue;

                switch (control)
                {
                    case DatePicker datePicker when field.FieldType == "DateTime":
                        if (value is DateTime dateValue)
                            datePicker.SelectedDate = dateValue;
                        else if (DateTime.TryParse(value.ToString(), out var parsed))
                            datePicker.SelectedDate = parsed;
                        break;
                    case TextBox textBox when field.FieldType == "DateTime":
                        if (value is DateTime date)
                            textBox.Text = date.ToString(DateMask, CultureInfo.InvariantCulture);
                        else if (DateTime.TryParse(value.ToString(), out var parsedDate))
                            textBox.Text = parsedDate.ToString(DateMask, CultureInfo.InvariantCulture);
                        break;
                    case TextBox textBox:
                        textBox.Text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                        break;
                    case ComboBox comboBox:
                        comboBox.SelectedValue = value;
                        if (comboBox.SelectedValue == null && value is string text)
                        {
                            var match = comboBox.Items.OfType<RefItem>().FirstOrDefault(item =>
                                string.Equals(item.Display, text, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(item.Id?.ToString(), text, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                                comboBox.SelectedItem = match;
                        }
                        break;
                }
            }
        }

        private void SetDateEditor(string column, DateTime value)
        {
            if (_editors.TryGetValue(column, out var editor) && editor is DatePicker datePicker)
                datePicker.SelectedDate = value;
        }

        private async Task LoadLinesAsync()
        {
            _lines.Clear();
            if (!_recordId.HasValue)
            {
                AddLine();
                return;
            }

            var rows = await _lineService.GetLinesAsync(_definition.LinesTable, _definition.ParentColumn, _recordId.Value);
            foreach (var row in rows)
            {
                if (row.MaterialId.HasValue && _materialRows.TryGetValue(row.MaterialId.Value, out var material))
                    row.MaterialCode = Read(material, "Код", "code");
                _lines.Add(row);
            }

            if (_lines.Count == 0)
                AddLine();
        }

        // ---------- Строки ----------

        private void OnAddLineClick(object sender, RoutedEventArgs e) => AddLine();

        private void AddLine()
        {
            _lines.Add(new InventoryLineRow
            {
                VatRate = ReadDecimalEditor("vat_rate"),
                SalesTaxRate = ReadDecimalEditor("sales_tax_rate")
            });
        }

        private void OnDeleteLineClick(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is InventoryLineRow row)
            {
                _lines.Remove(row);
                UpdateTotals();
            }
        }

        private void OnRecalculateClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _lines)
                row.Recalculate();

            LinesGrid.Items.Refresh();
            UpdateTotals();
        }

       
        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (e.Row.Item is InventoryLineRow row)
                {
                    if (e.Column == MaterialColumn && row.MaterialId.HasValue &&
                        _materialRows.TryGetValue(row.MaterialId.Value, out var material))
                    {
                        row.MaterialName = Read(material, "Наименование материала", "Наименование");
                        row.MaterialCode = Read(material, "Код", "code");
                        var unit = Read(material, "Ед изм", "Единица");
                        if (!string.IsNullOrWhiteSpace(unit))
                            row.UnitName = unit;
                    }

                    row.Recalculate();
                    LinesGrid.Items.Refresh();
                    UpdateTotals();
                }
            }));
        }

        private void UpdateTotals()
        {
            InventoryDocumentLineService.RecalculateTotals(
                _lines, out var withoutTax, out var vat, out var salesTax, out var total);

            TotalWithoutTaxText.Text = withoutTax.ToString("N2");
            TotalVatText.Text = vat.ToString("N2");
            TotalSalesTaxText.Text = salesTax.ToString("N2");
            TotalAmountText.Text = total.ToString("N2");
        }

        // ---------- Сохранение ----------

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lines.Any(line => !line.MaterialId.HasValue || line.Quantity <= 0))
                {
                    MessageBox.Show("В каждой строке выберите материал и укажите количество.",
                        "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var data = BuildItemData();

                if (_recordId.HasValue)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _recordId.Value, data);
                    SavedRecordId = _recordId.Value;
                }
                else
                {
                    SavedRecordId = await _metadataService.CreateDynamicRecordAsync(_document.Id, data);
                }

                await _lineService.ReplaceLinesAsync(
                    _definition.LinesTable, _definition.ParentColumn, SavedRecordId, _lines.ToList());

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private Dictionary<string, object> BuildItemData()
        {
            var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in _document.Fields)
            {
                if (_referenceBoxes.TryGetValue(field.DbColumnName, out var referenceBox))
                {
                    var text = referenceBox.Text?.Trim() ?? string.Empty;
                    if (text.Length == 0)
                        continue;

                    if (_references.TryGetValue(field.DbColumnName, out var known) && known.HasValue)
                    {
                        data[field.Name] = known.Value;
                        continue;
                    }

                    var value = ResolveReference(field.DbColumnName, text);
                    if (value == null)
                        throw new InvalidOperationException($"Не найдено значение для поля «{field.Name}»: {text}");

                    data[field.Name] = value;
                    continue;
                }

                var editorValue = ReadEditorValue(field);
                if (editorValue != null)
                    data[field.Name] = editorValue;
            }

            InventoryDocumentLineService.RecalculateTotals(
                _lines, out var withoutTax, out var vat, out var salesTax, out var total);

            SetField(data, "amount_without_tax", withoutTax);
            SetField(data, "vat_total", vat);
            SetField(data, "sales_tax_total", salesTax);
            SetField(data, "amount", total);
            SetField(data, "amount_currency", total);

            if (_editors.TryGetValue("document_entry_id", out var entryEditor) &&
                entryEditor is ComboBox { SelectedValue: Guid entryId })
            {
                if (_entryOperationKinds.TryGetValue(entryId, out var operationKind))
                {
                    SetField(data, "operation_kind", operationKind);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Не выбран вид операции. Укажите вид документа ТМЗ из справочника «Ввод нового документа ТМЗ».");
                }
            }

            if (!_recordId.HasValue)
                SetField(data, "is_posted", false);

            return data;
        }

        private object? ResolveReference(string column, string text)
        {
            if (!_rowsByColumn.TryGetValue(column, out var rows))
                return null;

            var code = text.Split(" - ")[0].Trim();

            var row = rows.FirstOrDefault(item =>
                string.Equals(Read(item, "Код", "code"), code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Read(item, "Наименование", "ФИО", "Полное наименование", "Наименование материала"),
                    text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(AccountDisplay(item), text, StringComparison.OrdinalIgnoreCase));

            return row == null ? null : ReadGuid(row);
        }

        private object? ReadEditorValue(MetadataField field)
        {
            if (!_editors.TryGetValue(field.DbColumnName, out var control))
                return null;

            switch (control)
            {
                case DatePicker datePicker:
                    return datePicker.SelectedDate;
                case TextBox textBox:
                {
                    var text = textBox.Text?.Trim() ?? string.Empty;
                    if (text.Length == 0)
                        return null;

                    if (field.FieldType == "DateTime")
                    {
                        if (DateTime.TryParseExact(text, DateMask, CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var date))
                            return date;
                        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
                            return parsed;
                        throw new InvalidOperationException(
                            $"Дата в поле «{field.Name}» должна быть в формате ДД/ММ/ГГГГ.");
                    }

                    return field.FieldType switch
                    {
                        "Decimal" => decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d)
                            ? d
                            : decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var di) ? di : null,
                        "Int" => int.TryParse(text, out var i) ? i : null,
                        _ => text
                    };
                }
                case ComboBox comboBox:
                    return comboBox.SelectedValue;
                default:
                    return null;
            }
        }

        private void SetField(Dictionary<string, object> data, string column, object value)
        {
            var field = _document.Fields.FirstOrDefault(item =>
                string.Equals(item.DbColumnName, column, StringComparison.OrdinalIgnoreCase));
            if (field != null)
                data[field.Name] = value;
        }

        private decimal ReadDecimalEditor(string column)
        {
            if (_editors.TryGetValue(column, out var control) && control is TextBox box &&
                decimal.TryParse(box.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
            {
                return value;
            }

            return 0m;
        }

        // ---------- Справочники ----------

        private async Task<List<Dictionary<string, object>>> LoadRowsAsync(
            List<MetadataObject> catalogs,
            string catalogName)
        {
            var catalog = catalogs.FirstOrDefault(item =>
                string.Equals(item.Name, catalogName, StringComparison.OrdinalIgnoreCase));

            return catalog == null
                ? new List<Dictionary<string, object>>()
                : await _metadataService.GetCatalogDataAsync(catalog.Id);
        }

        private static List<RefItem> ToItems(
            List<Dictionary<string, object>> rows,
            Func<Dictionary<string, object>, string> display,
            bool idAsCode = false)
        {
            var result = new List<RefItem>();
            foreach (var row in rows)
            {
                var text = display(row) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (idAsCode)
                {
                    result.Add(new RefItem { Id = text.Split(" - ")[0], Display = text });
                    continue;
                }

                var id = ReadGuid(row);
                if (id.HasValue)
                    result.Add(new RefItem { Id = id.Value, Display = text });
            }

            return result.OrderBy(item => item.Display).ToList();
        }

        private static string AccountDisplay(Dictionary<string, object> row) =>
            $"{Read(row, "Код")} - {Read(row, "Наименование")}".Trim(' ', '-');

        private static bool IsTrue(Dictionary<string, object> row, string key)
        {
            if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                return false;

            if (value is bool flag)
                return flag;

            var text = value.ToString()?.Trim() ?? string.Empty;
            return text == "1" ||
                   text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string Read(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                {
                    var text = Convert.ToString(value, CultureInfo.CurrentCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }

            return string.Empty;
        }

        private static Guid? ReadGuid(Dictionary<string, object> row)
        {
            if (row.TryGetValue("Id", out var value) && value != null && value != DBNull.Value)
            {
                if (value is Guid guid)
                    return guid;
                if (Guid.TryParse(value.ToString(), out var parsed))
                    return parsed;
            }

            return null;
        }
    }
}
