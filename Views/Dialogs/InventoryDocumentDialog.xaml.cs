using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        private List<Dictionary<string, object>> _materials = new();
        private List<Dictionary<string, object>> _currencyRows = new();
        private List<Dictionary<string, object>> _accountRows = new();
        private List<Dictionary<string, object>> _sites = new();
        private MetadataObject? _materialsCatalog;
        // Защита от двойного открытия окна выбора (мышь + Click).
        private bool _pickerOpening;
        // Какая кнопка «?» нажата в таблице: ждём отпускания мыши.
        private (InventoryLineRow Row, string Kind)? _pendingPick;
        // Валюта, подставленная из счёта («Валюта счета» в плане счетов).
        private Guid? _accountCurrencyId;
        // Первый проход после загрузки: курс из документа не перетираем.
        private bool _currencySynced;
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

            SaveButton.Content = recordId.HasValue ? "Запись" : "Добавить";

            LinesGrid.ItemsSource = _lines;

            // Клик по «?» в ячейке DataGrid съедает на выделение строки — слушаем
            // туннелирующие события с handledEventsToo и открываем окно уже на отпускании кнопки.
            LinesGrid.AddHandler(PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnLinesGridPreviewMouseDown), handledEventsToo: true);
            LinesGrid.AddHandler(PreviewMouseUpEvent,
                new MouseButtonEventHandler(OnLinesGridPreviewMouseUp), handledEventsToo: true);

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
                var materials = await LoadRowsAsync(catalogs, "Справочник материалов", "Материалы");
                _materialsCatalog = catalogs.FirstOrDefault(item =>
                    string.Equals(item.Name, "Справочник материалов", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, "Материалы", StringComparison.OrdinalIgnoreCase));
                var entries = await LoadRowsAsync(catalogs, InventoryDocumentModel.EntryCatalogName);

                BuildTopRow(FilterEntriesForDocument(entries));
                BuildSupplierCard(accounts, organizations, employees, supplyKinds, sites, responsible);

                var documentDate = GetDocumentDate();
                var vatItems = await BuildTaxItemsAsync(TaxKind.Vat, taxRows, documentDate);
                var salesItems = await BuildTaxItemsAsync(TaxKind.Sales, taxRows, documentDate);
                var paymentItems = BuildPaymentItems(paymentKinds);

                BuildTaxCard(vatItems, salesItems, paymentItems);
                BuildCurrencyCard(currencies);

                _materials = materials;
                _currencyRows = currencies;
                foreach (var material in materials)
                {
                    var materialId = ReadGuid(material);
                    if (materialId.HasValue)
                        _materialRows[materialId.Value] = material;
                }

                _accountRows = accounts;
                _sites = sites;

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
                FocusFirstEntryCell(_lines.FirstOrDefault());
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
            _rowsByColumn["document_entry_id"] = entries;
            _entryEditor = AddComboItems(HeaderTopPanel, "Вид операции", "document_entry_id",
                BuildEntryItems(entries), 250);
            AddText(HeaderTopPanel, "№ пачки", "pack_number", 80);
            AddText(HeaderTopPanel, "Серия/№ бланка", "tax_blank_number", 130);
            AddText(HeaderTopPanel, "Номер ЭСФ", "esf_number", 150);
        }


        //Исправлено document_entry_id вид операции только наименование. код убран
        private string BuildEntryDisplay(Dictionary<string, object> row)
        {            
            var name = Read(row, "Наименование", "Наименование");
            return (name).Trim(' ', '-');
        }

        /// <summary>
        /// Виды операций только для этого документа: в «Поступление ТМЗ» попадают
        /// записи справочника с movement_kind = Incoming (выбытие — Outgoing,
        /// перемещение — Movement).
        /// </summary>
        private List<Dictionary<string, object>> FilterEntriesForDocument(List<Dictionary<string, object>> entries)
        {
            var movementKind = InventoryDocumentModel.MovementKindForDocument(_definition.Name);

            // Неизвестный документ — фильтровать не по чему, показываем всё как раньше.
            if (string.IsNullOrWhiteSpace(movementKind))
                return entries;

            var filtered = entries
                .Where(row => EntryBelongsToDocument(row, movementKind))
                .ToList();

            // У открытого документа вид операции мог быть выбран до появления фильтра —
            // сохраняем выбранное значение в списке, иначе оно потерялось бы при записи.
            var savedId = ReadSavedEntryId();
            if (savedId.HasValue && filtered.All(row => ReadGuid(row) != savedId.Value))
            {
                var saved = entries.FirstOrDefault(row => ReadGuid(row) == savedId.Value);
                if (saved != null)
                    filtered.Add(saved);
            }

            return filtered;
        }

        private bool EntryBelongsToDocument(Dictionary<string, object> row, string movementKind)
        {
            var rowMovementKind = Read(row, "Вид движения", "movement_kind");
            if (rowMovementKind.Length > 0)
            {
                return string.Equals(NormalizeMovementKind(rowMovementKind), movementKind,
                    StringComparison.OrdinalIgnoreCase);
            }

            // Старые записи справочника могли сохранить только документ — сверяем по нему.
            var rowDocument = Read(row, "Документ ТМЗ", "document_name");
            return string.Equals(rowDocument, _definition.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Вид движения в записях справочника встречается и кодом (Incoming), и по-русски
        /// (Приход) — приводим к коду, чтобы фильтр не зависел от способа ввода.
        /// </summary>
        private static string NormalizeMovementKind(string value)
        {
            var text = value.Trim();

            if (text.StartsWith("Incoming", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Приход", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Поступление", StringComparison.OrdinalIgnoreCase))
                return "Incoming";

            if (text.StartsWith("Outgoing", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Расход", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Выбытие", StringComparison.OrdinalIgnoreCase))
                return "Outgoing";

            if (text.StartsWith("Movement", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Перемещение", StringComparison.OrdinalIgnoreCase))
                return "Movement";

            return text;
        }

        private Guid? ReadSavedEntryId()
        {
            if (!_existing.TryGetValue("Вид операции", out var value) || value == null || value == DBNull.Value)
                return null;

            if (value is Guid guid)
                return guid;

            return Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
        }

        /// <summary>Элементы списка «Вид операции»: только наименование, в порядке справочника.</summary>
        private List<RefItem> BuildEntryItems(List<Dictionary<string, object>> entries)
        {
            var items = new List<RefItem>();

            foreach (var row in entries
                         .OrderBy(EntrySortOrder)
                         .ThenBy(item => Read(item, "Наименование", "name"), StringComparer.CurrentCulture))
            {
                var id = ReadGuid(row);
                if (!id.HasValue)
                    continue;

                _entryOperationKinds[id.Value] = Read(row, "operation_kind", "Код операции", "code", "Код");
                items.Add(new RefItem { Id = id.Value, Display = BuildEntryDisplay(row) });
            }

            return items;
        }

        private static int EntrySortOrder(Dictionary<string, object> row)
        {
            var text = Read(row, "Порядок", "sort_order");
            return int.TryParse(text, out var order) ? order : int.MaxValue;
        }

        private void BuildSupplierCard(
            List<Dictionary<string, object>> accounts,
            List<Dictionary<string, object>> organizations,
            List<Dictionary<string, object>> employees,
            List<Dictionary<string, object>> supplyKinds,
            List<Dictionary<string, object>> sites,
            List<Dictionary<string, object>> responsible)
        {
            AddReference(SupplierPanel, "Счет", "counterparty_account", accounts);
            AddReference(SupplierPanel, "Организация", "organization_id", organizations);
            AddReference(SupplierPanel, "Сотрудник", "employee_id", employees);
            AddReference(SupplierPanel, "Вид покупки", "supply_kind", supplyKinds);

            // Организация и сотрудник показываются по привязкам счёта.
            SetPanelVisibility("organization_id", false);
            SetPanelVisibility("employee_id", false);
            SetPanelVisibility("supply_kind", true);

            // Для внутреннего перемещения нужны получатель и его МОЛ.
            if (string.Equals(_definition.Name, InventoryDocumentModel.MovementName, StringComparison.OrdinalIgnoreCase))
            {
                AddReference(SupplierPanel, "Участок-получатель", "destination_site_id", sites);
                AddReference(SupplierPanel, "МОЛ-получатель", "destination_person_id", responsible);
            }
        }

        private void BuildTaxCard(
            List<RefItem> vatItems,
            List<RefItem> salesItems,
            List<RefItem> paymentItems)
        {
            // Каждый контрол налогов — отдельная строка на всю ширину блока, как в «Поставщике».
            _vatCombo = AddComboItems(TaxPanel, "Вид НДС", "vat_type", vatItems, 220);
            StretchControl(_vatCombo);
            _vatRateBox = AddText(TaxPanel, "Ставка НДС, %", "vat_rate", 80);
            StretchControl(_vatRateBox);
            _paymentCombo = AddComboItems(TaxPanel, "Вид оплаты", "payment_kind", paymentItems, 180);
            StretchControl(_paymentCombo);
            _salesTaxCombo = AddComboItems(TaxPanel, "Налог", "sales_tax_type", salesItems, 220);
            StretchControl(_salesTaxCombo);
            _salesTaxRateBox = AddText(TaxPanel, "Ставка налога, %", "sales_tax_rate", 80);
            StretchControl(_salesTaxRateBox);

            if (_vatCombo != null)
                _vatCombo.SelectionChanged += (_, _) => ApplyTaxRate(_vatCombo, _vatRateBox);
            if (_salesTaxCombo != null)
                _salesTaxCombo.SelectionChanged += (_, _) => ApplyTaxRate(_salesTaxCombo, _salesTaxRateBox);
        }

        /// <summary>
        /// Валюта — как в счёте-фактуре: отдельный блок под «Поставщиком» и налогами.
        /// Виден только для счёта с признаком «Связь с валютами».
        /// </summary>
        private void BuildCurrencyCard(List<Dictionary<string, object>> currencies)
        {
            CurrencyCombo.ItemsSource = ToItems(currencies, row => Read(row, "Наименование", "Код"));
            CurrencyCombo.SelectionChanged += (_, _) => UpdateTotals();
            ExchangeRateBox.TextChanged += (_, _) => UpdateTotals();

            _editors["currency_id"] = CurrencyCombo;
            _editors["exchange_rate"] = ExchangeRateBox;

            CurrencyCard.Visibility = Visibility.Collapsed;
        }

        /// <summary>Контрол в отдельной строке карточки — на всю её ширину.</summary>
        private static void StretchControl(Control control)
        {
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
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

        private void AddReference(
            Panel panel,
            string label,
            string column,
            List<Dictionary<string, object>> rows)
        {
            _rowsByColumn[column] = rows;

            // Каждый контрол карточки «Поставщик» — отдельная строка: подпись сверху,
            // поле растянуто на всю ширину карточки, кнопка выбора — справа.
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            container.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });

            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox
            {
                Height = 28,
                Padding = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Счёт — восьмизначный код из цифр: ручной ввод без букв и не длиннее 8 знаков.
            if (string.Equals(column, "counterparty_account", StringComparison.OrdinalIgnoreCase))
            {
                box.MaxLength = 8;
                box.ToolTip = "Только цифры, до 8 знаков";
                AttachDigitsOnlyInput(box);
            }

            var pick = new Button
            {
                Content = "?",
                Width = 34,
                Height = 28,
                Padding = new Thickness(0), // в теме у Button Padding 14,7 — при узкой кнопке знак «?» не помещался
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
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
            Grid.SetColumn(pick, 1);
            line.Children.Add(pick);
            container.Children.Add(line);
            panel.Children.Add(container);

            _referenceBoxes[column] = box;
            _referencePanels[column] = container;
        }

        /// <summary>
        /// Ручной ввод кода счёта: только цифры. Восемь знаков ограничивает MaxLength,
        /// клавиатура и вставка из буфера проверяются здесь.
        /// </summary>
        private static void AttachDigitsOnlyInput(TextBox box)
        {
            box.PreviewTextInput += (_, args) =>
            {
                if (!IsAsciiDigits(args.Text))
                {
                    args.Handled = true;
                    return;
                }

                // После выбора из справочника в поле стоит «код - наименование» (или GUID из базы):
                // первая же набранная цифра начинает код заново, иначе MaxLength не даст печатать.
                if (box.Text.Length > box.MaxLength ||
                    box.Text.Contains(" - ", StringComparison.Ordinal))
                    box.Clear();
            };

            box.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Space)
                    args.Handled = true;
            };

            DataObject.AddPastingHandler(box, (_, args) =>
            {
                var text = args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true)
                    ? args.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty
                    : string.Empty;

                if (!IsAsciiDigits(text))
                    args.CancelCommand();
            });
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
            var isAccount = string.Equals(column, "counterparty_account", StringComparison.OrdinalIgnoreCase);

            var pickRows = rows.Select(row =>
            {
                var pick = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = row.TryGetValue("Id", out var id) ? id : DBNull.Value,
                    ["Код"] = Read(row, "Код", "code"),
                    ["Наименование"] = Read(row, "Наименование", "ФИО", "Полное наименование", "Наименование материала")
                };

                // У плана счетов в окне выбора нужны ещё «Тип счета» и «Активен».
                if (isAccount)
                {
                    pick["Тип счета"] = Read(row, "Тип счета", "account_type");
                    pick["Активен"] = row.GetValueOrDefault("Активен") ?? row.GetValueOrDefault("is_active") ?? DBNull.Value;
                }

                return pick;
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
            CurrencyCard.Visibility = linkCurrency ? Visibility.Visible : Visibility.Collapsed;

            // Валюта привязана к счёту: берём её из «Валюты счёта» плана счетов.
            if (linkCurrency && account != null)
                _ = ApplyAccountCurrencyAsync(account);
            else
                ResetAccountCurrency();
        }

        /// <summary>Счёт без валютной привязки — выбор валюты остаётся пользователю.</summary>
        private void ResetAccountCurrency()
        {
            _accountCurrencyId = null;
            _currencySynced = true;
            CurrencyCombo.IsEnabled = true;
            CurrencyCombo.ToolTip = null;
        }

        /// <summary>Валюта счёта: «Валюта счета» / `account_currency_id` из плана счетов.</summary>
        private Guid? ResolveAccountCurrencyId(Dictionary<string, object> account)
        {
            if (!account.TryGetValue("Валюта счета", out var value) || value == null || value == DBNull.Value)
                account.TryGetValue("account_currency_id", out value);

            if (value == null || value == DBNull.Value)
                return null;

            if (value is Guid direct)
                return direct == Guid.Empty ? null : direct;

            var text = Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
            if (text.Length == 0)
                return null;

            if (Guid.TryParse(text, out var parsed))
                return parsed == Guid.Empty ? null : parsed;

            // Ссылка может прийти подписью «USD - Доллар США» — ищем по коду валюты.
            var code = text.Split(" - ")[0].Trim();
            var row = _currencyRows.FirstOrDefault(item =>
                string.Equals(Read(item, "Код", "code"), code, StringComparison.OrdinalIgnoreCase));

            return row == null ? null : ReadGuid(row);
        }

        /// <summary>
        /// Подставляет валюту, закреплённую за счётом, и блокирует выбор: в документе
        /// она уже известна и не должна расходиться со счётом. Курс подтягивается
        /// из справочника курсов валют на дату документа.
        /// </summary>
        private async Task ApplyAccountCurrencyAsync(Dictionary<string, object> account)
        {
            var currencyId = ResolveAccountCurrencyId(account);
            if (!currencyId.HasValue)
            {
                // У валютного счёта валюта не заполнена — оставляем ручной выбор.
                _accountCurrencyId = null;
                _currencySynced = true;
                CurrencyCombo.IsEnabled = true;
                CurrencyCombo.ToolTip = "У счёта не заполнена «Валюта счета» — выберите валюту вручную";
                return;
            }

            if (_accountCurrencyId == currencyId.Value)
            {
                _currencySynced = true;
                return;
            }

            // В списке могут быть только активные валюты — проверяем, что валюта счёта есть.
            var known = CurrencyCombo.Items.OfType<RefItem>().Any(item => item.Id is Guid id && id == currencyId.Value);
            if (!known)
            {
                _currencySynced = true;
                return;
            }

            // При первой загрузке курс уже пришёл из документа — не перетираем его.
            var keepDocumentRate = !_currencySynced && !string.IsNullOrWhiteSpace(ExchangeRateBox.Text);

            CurrencyCombo.SelectedValue = currencyId.Value;
            CurrencyCombo.IsEnabled = false;
            CurrencyCombo.ToolTip = "Валюта задаётся в плане счетов («Валюта счета»)";
            _accountCurrencyId = currencyId.Value;
            _currencySynced = true;

            await ApplyRateFromCatalogAsync(currencyId.Value, overwrite: !keepDocumentRate);
        }

        /// <summary>Курс на дату документа из справочника курсов валют; базовая валюта — 1.</summary>
        private async Task ApplyRateFromCatalogAsync(Guid currencyId, bool overwrite)
        {
            if (!overwrite && !string.IsNullOrWhiteSpace(ExchangeRateBox.Text))
                return;

            try
            {
                var rate = IsBaseCurrency(currencyId)
                    ? 1m
                    : (await _metadataService.GetCurrencyRateForDateAsync(currencyId, GetDocumentDate()))?.Rate;

                if (rate is > 0)
                {
                    ExchangeRateBox.Text = rate.Value.ToString("0.####", CultureInfo.CurrentCulture);
                    UpdateTotals();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Документ ТМЗ: курс валюты не получен: {ex.Message}");
            }
        }

        private bool IsBaseCurrency(Guid currencyId)
        {
            var row = _currencyRows.FirstOrDefault(item => ReadGuid(item) == currencyId);
            if (row == null)
                return false;

            return IsTrue(row, "Базовая") || IsTrue(row, "is_base") ||
                   string.Equals(Read(row, "Код", "code"), "KGS", StringComparison.OrdinalIgnoreCase);
        }

        private void SetPanelVisibility(string column, bool visible)
        {
            if (_referencePanels.TryGetValue(column, out var panel))
                panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- Курс валюты ----------

        /// <summary>Кнопка «?» у курса: окно курсов валют — история, выбор, загрузка курсов НБКР.</summary>
        private void OnRequestExchangeRateClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedCurrencyId(out var currencyId))
            {
                MessageBox.Show("Сначала выберите валюту.", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rateDate = GetDocumentDate();
            var currencyName = (CurrencyCombo.SelectedItem as RefItem)?.Display ?? string.Empty;
            var dialog = new CurrencyRatesDialog(_metadataService, currencyId, currencyName, rateDate);

            var owner = Window.GetWindow(this);
            if (owner != null && !ReferenceEquals(owner, this) && owner.IsVisible)
                dialog.Owner = owner;
            else if (Application.Current?.MainWindow is { IsVisible: true } mainWindow)
                dialog.Owner = mainWindow;

            if (dialog.ShowDialog() != true || dialog.SelectedRate is not decimal rate)
                return;

            ExchangeRateBox.Text = rate.ToString("0.####", CultureInfo.CurrentCulture);
            UpdateTotals();
        }

        /// <summary>Кнопка «Текущий курс»: последний известный курс валюты.</summary>
        private async void OnCurrentRateClick(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedCurrencyId(out var currencyId))
            {
                MessageBox.Show("Сначала выберите валюту.", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Cursor = Cursors.Wait;

                // Опрос нового курса: тянем свежие официальные курсы НБКР, затем берём курс из справочника.
                try
                {
                    await _metadataService.ImportLatestOfficialCurrencyRatesAsync();
                }
                catch (Exception importError)
                {
                    System.Diagnostics.Debug.WriteLine($"Документ ТМЗ: курсы НБКР не загружены: {importError.Message}");
                }

                var rate = await _metadataService.GetLatestCurrencyRateAsync(currencyId);
                if (rate == null)
                {
                    MessageBox.Show(
                        "Курс валюты не найден. Введите курс вручную или выберите в окне курсов по кнопке «?».",
                        "Курс валюты", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ExchangeRateBox.Text = rate.Rate.ToString("0.####", CultureInfo.CurrentCulture);
                UpdateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Курс валюты", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private bool TryGetSelectedCurrencyId(out Guid currencyId)
        {
            if (CurrencyCombo.SelectedValue is Guid id && id != Guid.Empty)
            {
                currencyId = id;
                return true;
            }

            currencyId = Guid.Empty;
            return false;
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

                row.MaterialText = FormatMaterialText(row.MaterialCode, row.MaterialName);
                row.AccountText = FormatLoadedAccountText(row.AccountCode);
                row.SiteText = FormatLoadedSiteText(row.SiteId);
                row.PropertyChanged += OnLinePropertyChanged;
                _lines.Add(row);
            }

            if (_lines.Count == 0)
                AddLine();

            RenumberLines();
        }

        // ---------- Строки ----------

        private void OnAddLineClick(object sender, RoutedEventArgs e)
        {
            AddLine();
            FocusFirstEntryCell(_lines[^1]);
        }

        private void AddLine()
        {
            var line = new InventoryLineRow
            {
                VatRate = ReadDecimalEditor("vat_rate"),
                SalesTaxRate = ReadDecimalEditor("sales_tax_rate")
            };

            line.PropertyChanged += OnLinePropertyChanged;
            _lines.Add(line);

            RenumberLines();
        }

        /// <summary>Номера строк в таблице — как в счёте-фактуре: 1, 2, 3…</summary>
        private void RenumberLines()
        {
            for (var index = 0; index < _lines.Count; index++)
                _lines[index].LineNumber = index + 1;
        }

        private void OnDeleteLineClick(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is InventoryLineRow row)
            {
                _lines.Remove(row);
                RenumberLines();
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
                   // LinesGrid.Items.Refresh();
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

            // «Сумма в валюте» — как в счёте-фактуре: сумма документа по курсу шапки.
            var rate = ReadDecimalEditor("exchange_rate");
            AmountCurrencyBox.Text = rate > 0
                ? Math.Round(total / rate, 2, MidpointRounding.AwayFromZero).ToString("N2")
                : "0,00";
        }

        /// <summary>
        /// Количество, цена и ставки пересчитывают строку и итоги сразу при вводе:
        /// не надо ждать окончания редактирования ячейки.
        /// </summary>
        private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not InventoryLineRow row)
                return;

            if (e.PropertyName is not (nameof(InventoryLineRow.Quantity) or nameof(InventoryLineRow.Price)
                or nameof(InventoryLineRow.VatRate) or nameof(InventoryLineRow.SalesTaxRate)))
            {
                return;
            }

            row.Recalculate();
            UpdateTotals();
        }

        // ---------- Материал в строках ----------

        /// <summary>
        /// Клик по «?» в ячейке: DataGrid перехватывает первый клик на выделение строки,
        /// поэтому запоминаем кнопку и открываем справочник на отпускании мыши.
        /// </summary>
        private void OnLinesGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _pendingPick = null;

            if (e.ChangedButton != MouseButton.Left ||
                FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not { } button ||
                button.DataContext is not InventoryLineRow row)
            {
                return;
            }

            var kind = button.Tag as string;
            if (kind is not ("material" or "account" or "site"))
                return;

            _pendingPick = (row, kind);
            e.Handled = true;
        }

        private void OnLinesGridPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _pendingPick is not { } pending)
                return;

            _pendingPick = null;
            e.Handled = true;

            OpenPickerAfterClick(() => OpenRowPickerAsync(pending.Row, pending.Kind));
        }

        private Task OpenRowPickerAsync(InventoryLineRow row, string kind) => kind switch
        {
            "material" => OpenMaterialPickerAsync(row),
            "account" => OpenAccountPickerAsync(row),
            "site" => OpenSitePickerAsync(row),
            _ => Task.CompletedTask
        };

        /// <summary>Кнопки «?» в ячейках — те же действия с клавиатуры.</summary>
        private async void OnPickMaterialClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is InventoryLineRow row)
                await OpenMaterialPickerAsync(row);
        }

        private async void OnPickAccountClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is InventoryLineRow row)
                await OpenAccountPickerAsync(row);
        }

        private async void OnPickSiteClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is InventoryLineRow row)
                await OpenSitePickerAsync(row);
        }

        /// <summary>
        /// Окно выбора открываем модально поверх документа: так оно гарантированно
        /// появляется, а не уходит отдельной вкладкой в рабочую область.
        /// </summary>
        private void OpenPickerAfterClick(Func<Task> open)
        {
            if (_pickerOpening)
                return;

            _pickerOpening = true;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await open();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _pickerOpening = false;
                }
            }), DispatcherPriority.Input);
        }

        /// <summary>
        /// Справочник открывается вкладкой в MDI-области, как в остальных документах;
        /// если рабочая область недоступна, MdiDialogService покажет модальное окно.
        /// </summary>
        private async Task<bool> ShowPickerAsync(ReferenceSelectionDialog dialog, string title)
        {
            if (await MdiDialogService.ShowInWorkspaceForResultAsync(Window.GetWindow(this), dialog, title) != true)
                return false;

            return dialog.SelectedItem != null;
        }

        private void OnAccountTextLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.DataContext is InventoryLineRow row)
                ApplyAccountText(row, box.Text);
        }

        private async Task OpenAccountPickerAsync(InventoryLineRow row)
        {
            var pickRows = BuildAccountPickRows();
            if (pickRows.Count == 0)
            {
                MessageBox.Show("План счетов пуст или не найден.", "Документ ТМЗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new ReferenceSelectionDialog(pickRows, "Код", "Наименование");
            dialog.SetSearchText(AccountSearchText(row.AccountText));

            if (await ShowPickerAsync(dialog, "Выбор счёта"))
                ApplyAccount(row, dialog.SelectedItem);
        }

        private async Task OpenSitePickerAsync(InventoryLineRow row)
        {
            var pickRows = BuildSitePickRows();
            if (pickRows.Count == 0)
            {
                MessageBox.Show("Справочник участков пуст или не найден.", "Документ ТМЗ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new ReferenceSelectionDialog(pickRows, "Наименование");
            dialog.SetSearchText(row.SiteText);

            if (await ShowPickerAsync(dialog, "Выбор участка"))
                ApplySite(row, dialog.SelectedItem);
        }

        /// <summary>Строки окна выбора участка — справочник «Участки».</summary>
        private List<Dictionary<string, object>> BuildSitePickRows()
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var site in _sites)
            {
                result.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = site.TryGetValue("Id", out var id) ? id : DBNull.Value,
                    ["Наименование"] = Read(site, "Наименование", "name")
                });
            }

            return result;
        }

        private void ApplySite(InventoryLineRow row, Dictionary<string, object> picked)
        {
            row.SiteId = ReadGuid(picked);
            row.SiteText = Read(picked, "Наименование", "name");
        }

        private void OnSiteTextLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.DataContext is InventoryLineRow row)
                ApplySiteText(row, box.Text);
        }

        /// <summary>Ручной ввод в ячейке «Участок»: наименование сопоставляется со справочником.</summary>
        private void ApplySiteText(InventoryLineRow row, string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                row.SiteId = null;
                row.SiteText = string.Empty;
                return;
            }

            var site = _sites.FirstOrDefault(item =>
                string.Equals(Read(item, "Наименование", "name"), value, StringComparison.OrdinalIgnoreCase));

            if (site == null)
            {
                var matches = _sites
                    .Where(item => Read(item, "Наименование", "name")
                        .Contains(value, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                site = matches.Count == 1 ? matches[0] : null;
            }

            if (site == null)
            {
                // Не нашли участок — возвращаем последнее корректное значение.
                row.SiteText = FormatLoadedSiteText(row.SiteId);
                return;
            }

            ApplySite(row, site);
        }

        private string FormatLoadedSiteText(Guid? siteId)
        {
            if (!siteId.HasValue)
                return string.Empty;

            var site = _sites.FirstOrDefault(item => ReadGuid(item) == siteId.Value);
            return site == null ? string.Empty : Read(site, "Наименование", "name");
        }

        /// <summary>Строки окна выбора счёта: код, наименование и тип — как в шапке документа.</summary>
        private List<Dictionary<string, object>> BuildAccountPickRows()
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var account in _accountRows)
            {
                result.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = account.TryGetValue("Id", out var id) ? id : DBNull.Value,
                    ["Код"] = Read(account, "Код", "code"),
                    ["Наименование"] = Read(account, "Наименование", "name"),
                    ["Тип счета"] = Read(account, "Тип счета", "account_type")
                });
            }

            return result;
        }

        private void ApplyAccount(InventoryLineRow row, Dictionary<string, object> picked)
        {
            var code = Read(picked, "Код", "code");
            row.AccountCode = code;
            row.AccountText = FormatAccountText(code, Read(picked, "Наименование", "name"));
        }

        /// <summary>Ручной ввод в ячейке «Дебет»: код счёта сопоставляется с планом счетов.</summary>
        private void ApplyAccountText(InventoryLineRow row, string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            var code = value.Split(" - ")[0].Trim();

            var account = code.Length == 0
                ? null
                : _accountRows.FirstOrDefault(item =>
                    string.Equals(Read(item, "Код", "code"), code, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                // Не нашли счёт — возвращаем последний корректный код без мусора.
                row.AccountText = FormatAccountText(row.AccountCode, string.Empty);
                return;
            }

            row.AccountCode = Read(account, "Код", "code");
            row.AccountText = FormatAccountText(row.AccountCode, Read(account, "Наименование", "name"));
        }

        private string FormatLoadedAccountText(string code)
        {
            var account = _accountRows.FirstOrDefault(item =>
                string.Equals(Read(item, "Код", "code"), code?.Trim(), StringComparison.OrdinalIgnoreCase));

            return FormatAccountText(code, account == null ? string.Empty : Read(account, "Наименование", "name"));
        }

        private static string FormatAccountText(string? code, string? name)
        {
            var accountCode = code?.Trim() ?? string.Empty;
            var accountName = name?.Trim() ?? string.Empty;

            if (accountCode.Length == 0)
                return accountName;
            if (accountName.Length == 0)
                return accountCode;

            return $"{accountCode} - {accountName}";
        }

        private static string AccountSearchText(string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            var separator = value.IndexOf(" - ", StringComparison.Ordinal);
            return separator > 0 && separator + 3 < value.Length ? value[(separator + 3)..] : value;
        }

        /// <summary>Уход из ячейки: введённый текст сопоставляется со справочником.</summary>
        private void OnMaterialTextLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.DataContext is InventoryLineRow row)
                ApplyMaterialText(row, box.Text);
        }

        private async Task OpenMaterialPickerAsync(InventoryLineRow row)
        {
            var pickRows = BuildMaterialPickRows();
            var dialog = new ReferenceSelectionDialog(pickRows, "Код", "Наименование");

            // Справочник может быть пустым — в окне выбора доступны «Добавить» и «Изменить».
            if (_materialsCatalog != null)
                dialog.ConfigureCatalogEditing(_materialsCatalog, _metadataService);

            dialog.SetSearchText(MaterialSearchText(row.MaterialText));

            var picked = await ShowPickerAsync(dialog, "Выбор материала");

            // В окне могли добавить новую запись — обновляем кэш справочника.
            await ReloadMaterialsAsync();

            if (picked)
                ApplyMaterial(row, dialog.SelectedItem);
        }

        /// <summary>Перечитывает справочник материалов после добавления записи в окне выбора.</summary>
        private async Task ReloadMaterialsAsync()
        {
            if (_materialsCatalog == null)
                return;

            try
            {
                var rows = await _metadataService.GetCatalogDataAsync(_materialsCatalog.Id);
                _materials = rows;
                _materialRows.Clear();

                foreach (var material in rows)
                {
                    var id = ReadGuid(material);
                    if (id.HasValue)
                        _materialRows[id.Value] = material;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Документ ТМЗ: справочник материалов не перечитан: {ex.Message}");
            }
        }

        /// <summary>Строки окна выбора: код, наименование и единица измерения — как у плана счетов.</summary>
        private List<Dictionary<string, object>> BuildMaterialPickRows()
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var material in _materials)
            {
                result.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = material.TryGetValue("Id", out var id) ? id : DBNull.Value,
                    ["Код"] = Read(material, "Код", "code"),
                    ["Наименование"] = Read(material, "Наименование материала", "Наименование"),
                    ["Ед. изм."] = Read(material, "Ед изм", "Единица")
                });
            }

            return result;
        }

        /// <summary>
        /// Сопоставляет введённый текст со справочником: точный код, точное наименование,
        /// затем единственное совпадение по началу кода или части наименования.
        /// </summary>
        private bool ApplyMaterialText(InventoryLineRow row, string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                row.MaterialId = null;
                row.MaterialCode = string.Empty;
                row.MaterialName = string.Empty;
                row.MaterialText = string.Empty;
                row.Recalculate();
                UpdateTotals();
                return true;
            }

            var material = ResolveMaterial(value);
            if (material == null)
                return false;

            ApplyMaterial(row, material);
            return true;
        }

        private Dictionary<string, object>? ResolveMaterial(string text)
        {
            var value = text.Trim();
            var separator = value.IndexOf(" - ", StringComparison.Ordinal);
            var code = separator > 0 ? value[..separator].Trim() : value;
            var name = separator > 0 ? value[(separator + 3)..].Trim() : value;

            var exact = _materials.FirstOrDefault(row =>
                string.Equals(Read(row, "Код", "code"), code, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Read(row, "Наименование материала", "Наименование"), value, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Read(row, "Наименование материала", "Наименование"), name, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                return exact;

            var matches = _materials.Where(row =>
                Read(row, "Код", "code").StartsWith(code, StringComparison.OrdinalIgnoreCase) ||
                Read(row, "Наименование материала", "Наименование").Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private void ApplyMaterial(InventoryLineRow row, Dictionary<string, object> picked)
        {
            var id = ReadGuid(picked);
            var material = id.HasValue && _materialRows.TryGetValue(id.Value, out var source) ? source : picked;

            row.MaterialId = id;
            row.MaterialCode = Read(material, "Код", "code");
            row.MaterialName = Read(material, "Наименование материала", "Наименование");
            row.MaterialText = FormatMaterialText(row.MaterialCode, row.MaterialName);

            var unit = Read(material, "Ед изм", "Единица");
            if (unit.Length > 0)
                row.UnitName = unit;

            row.Recalculate();
            UpdateTotals();
        }

        private static string FormatMaterialText(string? code, string? name)
        {
            var materialCode = code?.Trim() ?? string.Empty;
            var materialName = name?.Trim() ?? string.Empty;

            if (materialCode.Length == 0)
                return materialName;
            if (materialName.Length == 0)
                return materialCode;

            return $"{materialCode} - {materialName}";
        }

        /// <summary>Текст поиска: из «Код - Наименование» берём наименование.</summary>
        private static string MaterialSearchText(string? text)
        {
            var value = text?.Trim() ?? string.Empty;
            var separator = value.IndexOf(" - ", StringComparison.Ordinal);
            return separator > 0 && separator + 3 < value.Length ? value[(separator + 3)..] : value;
        }

        // ---------- Навигация по строкам ----------

        /// <summary>
        /// Enter в таблице: переход к следующей колонке, после последней — на следующую
        /// строку, а в конце последней строки — создание новой.
        /// </summary>
        private void OnLinesGridPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
                return;

            e.Handled = true;

            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            MoveToNextEntryCell();
        }

        private void MoveToNextEntryCell()
        {
            var column = LinesGrid.CurrentCell.Column;
            if (column == null || LinesGrid.CurrentCell.Item is not InventoryLineRow row)
                return;

            var index = LinesGrid.Columns.IndexOf(column);
            for (var next = index + 1; next < LinesGrid.Columns.Count; next++)
            {
                var candidate = LinesGrid.Columns[next];
                if (candidate.IsReadOnly || candidate.Visibility != Visibility.Visible)
                    continue;

                FocusCell(row, candidate);
                return;
            }

            // Колонки закончились: идём на следующую строку, а в конце — создаём новую.
            var rowIndex = _lines.IndexOf(row);
            if (rowIndex >= 0 && rowIndex + 1 < _lines.Count)
            {
                FocusFirstEntryCell(_lines[rowIndex + 1]);
                return;
            }

            AddLine();
            FocusFirstEntryCell(_lines[^1]);
        }

        /// <summary>Ставит курсор в первую вводимую колонку строки.</summary>
        private void FocusFirstEntryCell(InventoryLineRow? row)
        {
            if (row == null)
                return;

            foreach (var column in LinesGrid.Columns)
            {
                if (column.IsReadOnly || column.Visibility != Visibility.Visible)
                    continue;

                FocusCell(row, column);
                return;
            }
        }

        /// <summary>
        /// Ставит курсор в ячейку и открывает её редактор: у шаблонных колонок — поле
        /// внутри шаблона, у текстовых — режим редактирования DataGrid.
        /// </summary>
        private void FocusCell(InventoryLineRow row, DataGridColumn column)
        {
            LinesGrid.SelectedItem = row;
            LinesGrid.CurrentCell = new DataGridCellInfo(row, column);
            LinesGrid.ScrollIntoView(row, column);
            LinesGrid.UpdateLayout();
            LinesGrid.Focus();

            if (!column.IsReadOnly)
                LinesGrid.BeginEdit();

            LinesGrid.Dispatcher.BeginInvoke(
                new Action(() => FocusCellEditor(row, column, 0)), DispatcherPriority.Loaded);
        }

        private void FocusCellEditor(InventoryLineRow row, DataGridColumn column, int attempt)
        {
            var cell = FindCell(row, column);
            if (cell == null)
            {
                if (attempt < 3)
                {
                    LinesGrid.Dispatcher.BeginInvoke(
                        new Action(() => FocusCellEditor(row, column, attempt + 1)), DispatcherPriority.Background);
                    return;
                }

                LinesGrid.Focus();
                if (!column.IsReadOnly)
                    LinesGrid.BeginEdit();

                return;
            }

            cell.Focus();

            var textBox = FindVisualChild<TextBox>(cell);
            if (textBox != null)
            {
                textBox.Focus();
                textBox.SelectAll();
                return;
            }

            FindVisualChild<ComboBox>(cell)?.Focus();
        }

        private DataGridCell? FindCell(InventoryLineRow row, DataGridColumn column)
        {
            if (LinesGrid.ItemContainerGenerator.ContainerFromItem(row) is not DataGridRow container)
                return null;

            var index = LinesGrid.Columns.IndexOf(column);
            var presenter = FindVisualChild<DataGridCellsPresenter>(container);
            if (presenter == null || index < 0)
                return null;

            return presenter.ItemContainerGenerator.ContainerFromIndex(index) as DataGridCell;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed)
                    return typed;

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static T? FindVisualAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        // ---------- Сохранение ----------

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Пустые строки (их создаёт Enter в конце таблицы) не сохраняем.
                foreach (var empty in _lines
                             .Where(line => !line.MaterialId.HasValue && line.Quantity == 0 && line.Price == 0)
                             .ToList())
                {
                    _lines.Remove(empty);
                }

                if (_lines.Count == 0)
                {
                    MessageBox.Show("Добавьте хотя бы одну строку с материалом и количеством.",
                        "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

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

                MdiDialogService.CloseWithResult(this, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Документ ТМЗ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => MdiDialogService.CloseWithResult(this, false);

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

            // «Сумма в валюте» — как в счёте-фактуре: сумма документа по курсу шапки.
            var rate = CurrencyCard.Visibility == Visibility.Visible ? ReadDecimalEditor("exchange_rate") : 0m;
            SetField(data, "amount_currency", rate > 0
                ? Math.Round(total / rate, 2, MidpointRounding.AwayFromZero)
                : total);

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
                        $"Не выбран вид операции. Укажите вид документа ТМЗ из справочника «{InventoryDocumentModel.EntryCatalogName}».");
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
            params string[] catalogNames)
        {
            var catalog = catalogs.FirstOrDefault(item => catalogNames.Any(name =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));

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
