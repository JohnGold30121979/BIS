using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views.Dialogs
{
    public partial class DynamicDocumentItemDialog : Window
    {
        private readonly MetadataObject _metadata;
        private readonly Guid? _editId;
        private readonly bool _isReadOnly;
        private readonly Dictionary<string, Control> _fieldControls = new();
        private readonly Dictionary<string, FrameworkElement> _fieldPanels = new();
        private readonly Dictionary<string, MetadataField> _fieldsByName = new();
        private readonly MetadataService _metadataService;
        private readonly Dictionary<string, object>? _initialData;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private Dictionary<string, object>? _existingData;
        private string? _assignedModuleName;
        private Grid? _fixedAssetLineGrid;
        private Dictionary<string, MetadataObject>? _fixedAssetMovementCatalogsDict;
        private readonly List<FixedAssetLineUiRow> _fixedAssetLineRows = new();
        private FixedAssetLineUiRow? _activeFixedAssetLineRow;
        private CheckBox? _fixedAssetUseLatestCurrencyRateCheckBox;
        private bool _fixedAssetApplyingLatestCurrencyRate;

        private static readonly string[][] FixedAssetLineFieldAliases =
        {
            new[] { "Основное средство", "fixed_asset_id", "asset_id" },
            new[] { "Без НДС", "amount_without_vat" },
            new[] { "НДС", "vat_amount" },
            new[] { "% НДС", "vat_rate" },
            new[] { "Налог с продаж", "sales_tax_amount" },
            new[] { "Сумма", "amount" },
            new[] { "Сумма в валюте", "amount_currency", "foreign_amount" }
        };

        public Dictionary<string, object> ItemData { get; private set; } = new();

        private sealed class FixedAssetLineUiRow
        {
            public int RowIndex { get; set; }
            public Dictionary<string, Control> Controls { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, FrameworkElement> Panels { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public DynamicDocumentItemDialog(
            MetadataObject metadata,
            MetadataService metadataService,
            Guid? editId = null,
            bool isReadOnly = false,
            Dictionary<string, object>? initialData = null)
        {
            InitializeComponent();
            _metadata = metadata;
            _metadataService = metadataService;
            _editId = editId;
            _isReadOnly = isReadOnly;
            _initialData = initialData;
            Title = $"{(isReadOnly ? "Просмотр" : editId.HasValue ? "Редактирование" : "Добавление")}: {metadata.Name}";

            if (_isReadOnly)
            {
                SaveButton.Visibility = Visibility.Collapsed;
                CancelButton.Content = "Закрыть";
            }

            Loaded += async (s, e) => await BuildFormAsync();
        }

        private async Task BuildFormAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var catalogsDict = allCatalogs.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _existingData = await LoadExistingDataAsync();
            _assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(_metadata.Id, _metadata.ObjectType);

            FieldsPanel.Children.Clear();
            _fieldControls.Clear();
            _fieldPanels.Clear();
            _fieldsByName.Clear();

            if (IsFixedAssetMovementDocument())
            {
                await BuildFixedAssetMovementFormAsync(catalogsDict);
                await ApplyFixedAssetMovementDefaultsAsync(catalogsDict);
                UpdateAccountControlledFieldsVisibility();
                return;
            }

            foreach (var field in _metadata.Fields.OrderBy(f => f.Order))
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

                panel.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                var inputControl = await CreateControlAsync(field, catalogsDict);
                if (_isReadOnly)
                    ApplyReadOnly(inputControl);

                if (_metadata.ObjectType == "Document" &&
                    MetadataService.IsDocumentNumberFieldName(field.Name) &&
                    inputControl is TextBox numberTextBox &&
                    !_editId.HasValue)
                {
                    numberTextBox.IsReadOnly = true;
                    numberTextBox.Background = Brushes.LightGray;

                    try
                    {
                        numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata);
                    }
                    catch
                    {
                        numberTextBox.Text = MetadataService.GenerateFallbackDocumentNumber();
                    }
                }

                panel.Children.Add(inputControl);
                FieldsPanel.Children.Add(panel);
                _fieldControls[field.Name] = inputControl;
                _fieldPanels[field.Name] = panel;
                _fieldsByName[field.Name] = field;
            }

            UpdateAccountControlledFieldsVisibility();
        }

        private List<Dictionary<string, object>> _taxCatalogRows = new();
        private bool _recalculatingMovementTotals;

        /// <summary>
        /// Загружает строки справочника «Налоги» для выпадающих списков налогов
        /// документа движения ОС (как в счете-фактуре).
        /// </summary>
        private async Task LoadTaxCatalogRowsAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            _taxCatalogRows.Clear();
            try
            {
                if (!catalogsDict.TryGetValue("Налоги", out var catalog))
                    return;
                var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
                _taxCatalogRows = rows ?? new();
            }
            catch
            {
                // Справочник налогов недоступен — комбобоксы останутся пустыми.
            }
        }


        private bool IsFixedAssetMovementDocument()
        {
            return _metadata.ObjectType == "Document" &&
                   string.Equals(_metadata.Name, "Учет движения ОС", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Для нового документа движения ОС подставляет активные элементы по умолчанию:
        /// активный вид оплаты, активный налог для НДС и активный налог с продаж.
        /// Для редактируемого документа значения восстанавливаются из сохраненной записи.
        /// </summary>
        private async Task ApplyFixedAssetMovementDefaultsAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            if (_editId.HasValue)
                return;

            // Вид оплаты: активный элемент справочника «Виды оплаты».
            if (_fieldControls.TryGetValue("Вид оплаты", out var paymentControl))
            {
                if (paymentControl is ReferencePickerControl paymentPicker &&
                    paymentPicker.ComboBox.SelectedItem == null)
                {
                    var defaultId = await GetDefaultCatalogRowIdAsync(catalogsDict, "Виды оплаты");
                    if (defaultId.HasValue)
                    {
                        paymentPicker.SelectedReferenceItem = paymentPicker.ComboBox.Items
                            .OfType<BIS.ERP.Models.ReferenceItem>()
                            .FirstOrDefault(item => item.Id == defaultId.Value);
                    }
                }
                else if (paymentControl is ComboBox paymentCombo && paymentCombo.SelectedItem == null)
                {
                    var defaultId = await GetDefaultCatalogRowIdAsync(catalogsDict, "Виды оплаты");
                    if (defaultId.HasValue)
                    {
                        paymentCombo.SelectedItem = paymentCombo.Items
                            .OfType<BIS.ERP.Models.ReferenceItem>()
                            .FirstOrDefault(item => item.Id == defaultId.Value);
                    }
                }
            }

            await ApplyDefaultFixedAssetBaseCurrencyAsync(catalogsDict);

            // Налоги: активный по умолчанию для НДС / налога с продаж.
            if (_fieldControls.TryGetValue("Вид НДС", out var vatControl) &&
                vatControl is ComboBox vatCombo && vatCombo.SelectedItem == null)
            {
                var defaultVat = _taxCatalogRows.FirstOrDefault(row =>
                    TryGetTaxRowBool(row, "is_default_vat", "По умолчанию для НДС"));
                if (defaultVat != null &&
                    Guid.TryParse(ReadFixedAssetMovementText(defaultVat, "Id"), out var vatId))
                {
                    vatCombo.SelectedItem = vatCombo.Items
                        .OfType<BIS.ERP.Models.ReferenceItem>()
                        .FirstOrDefault(item => item.Id == vatId);
                }
            }

            if (_fieldControls.TryGetValue("Вид налога с продаж", out var salesControl) &&
                salesControl is ComboBox salesCombo && salesCombo.SelectedItem == null)

            {
                var defaultSales = _taxCatalogRows.FirstOrDefault(row =>
                    TryGetTaxRowBool(row, "is_default_sales_tax", "По умолчанию для налога с продаж"));
                if (defaultSales != null &&
                    Guid.TryParse(ReadFixedAssetMovementText(defaultSales, "Id"), out var salesId))
                {
                    salesCombo.SelectedItem = salesCombo.Items
                        .OfType<BIS.ERP.Models.ReferenceItem>()
                        .FirstOrDefault(item => item.Id == salesId);
                }
            }

            RecalculateFixedAssetMovementTotals();
        }

        /// <summary>
        /// Возвращает Id первой активной (is_default = true) строки справочника.
        /// </summary>
        private async Task<Guid?> GetDefaultCatalogRowIdAsync(
            Dictionary<string, MetadataObject> catalogsDict,
            string catalogName)
        {
            if (!catalogsDict.TryGetValue(catalogName, out var catalog))
                return null;

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var activeRow = rows
                .FirstOrDefault(row =>
                    TryGetTaxRowBool(row, "is_default", "По умолчанию") &&
                    Guid.TryParse(ReadFixedAssetMovementText(row, "Id"), out _));
            return activeRow != null
                ? Guid.Parse(ReadFixedAssetMovementText(activeRow, "Id"))
                : rows.FirstOrDefault(row =>
                        Guid.TryParse(ReadFixedAssetMovementText(row, "Id"), out _))
                    is { } fallback &&
                  Guid.TryParse(ReadFixedAssetMovementText(fallback, "Id"), out var fallbackId)
                    ? fallbackId
                    : null;
        }

        private async Task ApplyDefaultFixedAssetBaseCurrencyAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            var field = FindDialogField("Валюта", "currency_id");
            if (field == null || !_fieldControls.TryGetValue(field.Name, out var control))
                return;

            //Возвращаем гуид базовой валюты
            var defaultId = await GetBaseCurrencyCatalogRowIdAsync(catalogsDict);
            if (!defaultId.HasValue)
                return;

            if (control is ReferencePickerControl picker && picker.ComboBox.SelectedItem == null)
            {
                picker.SelectedReferenceItem = picker.ComboBox.Items
                    .OfType<BIS.ERP.Models.ReferenceItem>()
                    .FirstOrDefault(item => item.Id == defaultId.Value);
            }
            else if (control is ComboBox comboBox && comboBox.SelectedItem == null)
            {
                comboBox.SelectedItem = comboBox.Items
                    .OfType<BIS.ERP.Models.ReferenceItem>()
                    .FirstOrDefault(item => item.Id == defaultId.Value);
            }
        }

        // Взятие базовай валюты из справочника "Справочник валюты"
        private async Task<Guid?> GetBaseCurrencyCatalogRowIdAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            var catalog = catalogsDict.Values.FirstOrDefault(item =>
                string.Equals(item.Name, "Справочник валют", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.TableName, "catalog_currencies", StringComparison.OrdinalIgnoreCase));
            if (catalog == null)
                return null;

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            var baseRow = rows.FirstOrDefault(row =>
                TryGetTaxRowBool(row, "is_base", "Базовая") &&
                Guid.TryParse(ReadFixedAssetMovementText(row, "Id"), out _));

            return baseRow != null && Guid.TryParse(ReadFixedAssetMovementText(baseRow, "Id"), out var baseId)
                ? baseId
                : null;
        }

        private async Task BuildFixedAssetMovementFormAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            await LoadTaxCatalogRowsAsync(catalogsDict);
            Width = Math.Max(Width, 1040);
            Height = Math.Max(Height, 720);
            MinWidth = Math.Max(MinWidth, 860);
            MinHeight = Math.Max(MinHeight, 620);

            FieldsPanel.MaxWidth = double.PositiveInfinity;
            FieldsPanel.Width = double.NaN;
            FieldsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            FieldsPanel.Children.Add(await CreateFixedAssetMovementHeaderAsync(catalogsDict));

            var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var documentGrid = CreateThreeColumnGrid(2);
            await AddFieldToGridAsync(documentGrid, 0, 0, catalogsDict, usedFields, "Дата");
            await AddFieldToGridAsync(documentGrid, 0, 1, catalogsDict, usedFields, "Номер");
            await AddFieldToGridAsync(documentGrid, 0, 2, catalogsDict, usedFields, "Серия/№ бланка");
            await AddHiddenFieldAsync(catalogsDict, usedFields, "Вид документа ОС");
            await AddFieldToGridAsync(documentGrid, 1, 0, catalogsDict, usedFields, "№ счет-фактуры", "invoice_number");
            FieldsPanel.Children.Add(CreateSection("Документ", documentGrid));

            var detailsGrid = CreateTwoPaneGrid();

            var settlementGrid = CreateTwoColumnGrid(2);
            await AddFieldToGridAsync(settlementGrid, 0, 0, catalogsDict, usedFields, "Счет расчетов");
            await AddFieldToGridAsync(
                settlementGrid,
                0,
                1,
                catalogsDict,
                usedFields,
                "Организация",
                "organization_id",
                "primary_organization_id",
                "Поставщик",
                "supplier_id",
                "Контрагент",
                "contractor_id");
            await AddFieldToGridAsync(settlementGrid, 1, 0, catalogsDict, usedFields, "Вид покупки");
            var settlementSection = CreateSection("Поставщик и расчеты", settlementGrid);
            settlementSection.Margin = new Thickness(0, 0, 6, 12);
            Grid.SetColumn(settlementSection, 0);
            detailsGrid.Children.Add(settlementSection);

            var taxGrid = CreateTwoColumnGrid(3);
            await AddFieldToGridAsync(taxGrid, 0, 0, catalogsDict, usedFields, "Вид НДС");
            await AddFieldToGridAsync(taxGrid, 0, 1, catalogsDict, usedFields, "Вид оплаты");
            await AddFieldToGridAsync(taxGrid, 1, 0, catalogsDict, usedFields, "Вид налога с продаж");
            await AddFieldToGridAsync(taxGrid, 1, 1, catalogsDict, usedFields, "Валюта");
            await AddFixedAssetExchangeRateFieldToGridAsync(taxGrid, 2, 0, catalogsDict, usedFields);
            AttachFixedAssetCurrencyRateHandler();
            var taxSection = CreateSection("Налоги и валюта", taxGrid);
            taxSection.Margin = new Thickness(6, 0, 0, 12);
            Grid.SetColumn(taxSection, 1);
            detailsGrid.Children.Add(taxSection);
            FieldsPanel.Children.Add(detailsGrid);

            var lineGrid = CreateFixedAssetLineGrid();
            _fixedAssetLineGrid = lineGrid;
            _fixedAssetMovementCatalogsDict = catalogsDict;
            _fixedAssetLineRows.Clear();
            _activeFixedAssetLineRow = null;
            MarkFieldAsUsed(usedFields, "Счет операции", "operation_account");
            await AddFixedAssetLineRowAsync(lineGrid, catalogsDict, usedFields);
            FieldsPanel.Children.Add(CreateSection("Строка основного средства", CreateFixedAssetLineArea(lineGrid)));

            var postingGrid = CreateTwoColumnGrid(3);
            await AddFieldToGridAsync(postingGrid, 0, 0, catalogsDict, usedFields, "Счет дебета");
            await AddFieldToGridAsync(postingGrid, 0, 1, catalogsDict, usedFields, "Счет кредита");
            var basisPanel = await CreateOptionalFixedAssetBasisPanelAsync(catalogsDict, usedFields);
            if (basisPanel != null)
            {
                Grid.SetRow(basisPanel, 1);
                Grid.SetColumn(basisPanel, 0);
                postingGrid.Children.Add(basisPanel);
            }
            await AddFieldToGridAsync(postingGrid, 1, 1, catalogsDict, usedFields, "Примечание");
            await AddFieldToGridAsync(postingGrid, 2, 0, catalogsDict, usedFields, "Проведен");
            FieldsPanel.Children.Add(CreateSection("Проводка и примечание", postingGrid));

            var remainingFields = _metadata.Fields
                .OrderBy(field => field.Order)
                .Where(field => !usedFields.Contains(field.Name))
                .ToList();

            if (remainingFields.Count == 0)
                return;

            var additionalGrid = CreateTwoColumnGrid((remainingFields.Count + 1) / 2);
            for (var index = 0; index < remainingFields.Count; index++)
            {
                await AddFieldByMetadataToGridAsync(
                    additionalGrid,
                    index / 2,
                    index % 2,
                    remainingFields[index],
                    catalogsDict,
                    usedFields);
            }

            FieldsPanel.Children.Add(CreateSection("Дополнительно", additionalGrid));
        }

        private async Task<Border> CreateFixedAssetMovementHeaderAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            var title = _editId.HasValue
                ? "Редактирование документа движения ОС"
                : "Новый документ движения ОС";

            var movementType = await ResolveFixedAssetMovementTypeCaptionAsync(catalogsDict);
            var subtitle = string.IsNullOrWhiteSpace(movementType)
                ? "Сначала выбран вид ввода, далее заполняются реквизиты документа."
                : $"Вид документа: {movementType}";

            return new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(238, 247, 255),
                    Color.FromRgb(249, 252, 255),
                    0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 225, 242)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 20,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(14, 45, 77))
                        },
                        new TextBlock
                        {
                            Text = subtitle,
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(72, 99, 124))
                        }
                    }
                }
            };
        }

        private async Task<string?> ResolveFixedAssetMovementTypeCaptionAsync(Dictionary<string, MetadataObject> catalogsDict)
        {
            var explicitTitle = ReadFixedAssetMovementText(_initialData, "_FixedAssetMovementTypeTitle");
            if (!string.IsNullOrWhiteSpace(explicitTitle))
                return explicitTitle;

            var rawValue = ReadFixedAssetMovementText(_existingData, "Вид документа ОС", "asset_document_entry_id");
            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            if (catalogsDict.TryGetValue("Ввод нового документа ОС", out var refCatalog))
            {
                var rows = await _metadataService.GetCatalogDataAsync(refCatalog.Id);
                var selected = rows.FirstOrDefault(row =>
                    ReadFixedAssetMovementText(row, "Id").Equals(rawValue, StringComparison.OrdinalIgnoreCase));

                if (selected != null)
                {
                    var caption = BuildFixedAssetMovementTypeCaption(selected);
                    if (!string.IsNullOrWhiteSpace(caption))
                        return caption;
                }
            }

            return Guid.TryParse(rawValue, out _) ? null : rawValue;
        }

        private static string BuildFixedAssetMovementTypeCaption(Dictionary<string, object> row)
        {
            var code = ReadFixedAssetMovementText(row, "Код", "code");
            var name = ReadFixedAssetMovementText(row, "Наименование", "name");

            if (string.IsNullOrWhiteSpace(code))
                return name;
            if (string.IsNullOrWhiteSpace(name) || string.Equals(code, name, StringComparison.OrdinalIgnoreCase))
                return code;

            return $"{code} - {name}";
        }

        private static string ReadFixedAssetMovementText(Dictionary<string, object>? row, params string[] keys)
        {
            if (row == null)
                return string.Empty;

            foreach (var key in keys)
            {
                if (!row.TryGetValue(key, out var value) || value == null || value == DBNull.Value)
                    continue;

                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return string.Empty;
        }

        private ComboBox CreateFixedAssetTaxComboBox(MetadataField field, object? currentValue)
        {
            var isVatCombo = string.Equals(field.DbColumnName, "vat_type_id", StringComparison.OrdinalIgnoreCase);
            var items = new List<BIS.ERP.Models.ReferenceItem>();
            foreach (var row in _taxCatalogRows)
            {
                var idText = ReadFixedAssetMovementText(row, "Id");
                if (!Guid.TryParse(idText, out var id))
                    continue;

                var name = ReadFixedAssetMovementText(row, "Наименование", "name");
                var caption = string.IsNullOrWhiteSpace(name)
                    ? ReadFixedAssetMovementText(row, "Код", "code")
                    : name;

                var rate = TryGetTaxRowDecimal(row, "Ставка", "rate");
                if (rate > 0m)
                    caption += $" ({rate:0.##}%)";

                items.Add(new BIS.ERP.Models.ReferenceItem { Id = id, DisplayName = caption });
            }

            var comboBox = new ComboBox
            {
                Height = 30,
                Name = GetSafeControlName(field.Name),
                DisplayMemberPath = nameof(BIS.ERP.Models.ReferenceItem.DisplayName),
                SelectedValuePath = nameof(BIS.ERP.Models.ReferenceItem.Id),
                MinWidth = 200,
                ItemsSource = items,
                Tag = isVatCombo ? "vat" : "sales_tax"
            };

            // Восстанавливаем сохраненное значение либо выбираем налог по умолчанию,
            // как в финансах («По умолчанию для НДС» / «По умолчанию для налога с продаж»).
            Guid.TryParse(currentValue?.ToString(), out var selectedId);
            var selected = selectedId != Guid.Empty
                ? items.FirstOrDefault(item => item.Id == selectedId)
                : null;

            if (selected == null && !_isReadOnly && !_editId.HasValue)
            {
                var defaultRow = _taxCatalogRows.FirstOrDefault(row =>
                    TryGetTaxRowBool(row, isVatCombo ? "is_default_vat" : "___none",
                        isVatCombo ? "По умолчанию для НДС" : "По умолчанию для налога с продаж"));
                if (defaultRow != null &&
                    Guid.TryParse(ReadFixedAssetMovementText(defaultRow, "Id"), out var defaultId))
                {
                    selected = items.FirstOrDefault(item => item.Id == defaultId);
                }
            }

            comboBox.SelectedItem = selected;

            // Как в счете-фактуре: выбор налога автоматически проставляет ставку
            // и пересчитывает суммы (НДС / налог с продаж / итог).
            comboBox.SelectionChanged += (_, _) => RecalculateFixedAssetMovementTotals();
            return comboBox;
        }

        private void RecalculateFixedAssetMovementTotals()
        {
            if (_recalculatingMovementTotals)
                return;

            _recalculatingMovementTotals = true;
            try
            {
                var baseAmount = GetMovementDecimal("Без НДС", "amount_without_vat");
                var vatRate = GetSelectedTaxRate("Вид НДС", "vat_type_id");
                var salesTaxRate = GetSelectedTaxRate("Вид налога с продаж", "sales_tax_type_id");
                var vatAmount = decimal.Round(baseAmount * vatRate / 100m, 2, MidpointRounding.AwayFromZero);
                var salesTaxAmount = decimal.Round(baseAmount * salesTaxRate / 100m, 2, MidpointRounding.AwayFromZero);
                var totalAmount = decimal.Round(baseAmount + vatAmount + salesTaxAmount, 2, MidpointRounding.AwayFromZero);

                SetMovementDecimal("% НДС", "vat_rate", vatRate);
                SetMovementDecimal("НДС", "vat_amount", vatAmount);
                SetMovementDecimal("Налог с продаж", "sales_tax_amount", salesTaxAmount);
                SetMovementDecimal("Итого", "total_amount", totalAmount);
                SetMovementDecimal("Сумма", "amount", totalAmount);

                var exchangeRate = GetMovementDecimal("Курс валюты", "exchange_rate");
                if (exchangeRate > 0m)
                {
                    SetMovementDecimal("Сумма в валюте", "amount_currency",
                        decimal.Round(totalAmount / exchangeRate, 2, MidpointRounding.AwayFromZero));
                }
                else
                {
                    SetMovementDecimal("Сумма в валюте", "amount_currency", 0m);
                }
            }
            finally
            {
                _recalculatingMovementTotals = false;
            }
        }

        private decimal GetSelectedTaxRate(params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var match = _fieldsByName.FirstOrDefault(pair =>
                    string.Equals(pair.Key, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Value.DbColumnName, alias, StringComparison.OrdinalIgnoreCase));
                if (match.Value != null &&
                    _fieldControls.TryGetValue(match.Key, out var control) &&
                    control is ComboBox combo)
                {
                    return GetSelectedTaxRate(combo);
                }
            }

            return 0m;
        }

        private void AttachFixedAssetCurrencyRateHandler()
        {
            var currencyControl = FindFieldControl("Валюта", "currency_id");
            if (currencyControl is ReferencePickerControl picker)
            {
                picker.ComboBox.SelectionChanged += async (_, _) =>
                {
                    if (_fixedAssetUseLatestCurrencyRateCheckBox?.IsChecked == true)
                        await ApplyFixedAssetLatestCurrencyRateAsync();
                };
            }
            else if (currencyControl is ComboBox comboBox)
            {
                comboBox.SelectionChanged += async (_, _) =>
                {
                    if (_fixedAssetUseLatestCurrencyRateCheckBox?.IsChecked == true)
                        await ApplyFixedAssetLatestCurrencyRateAsync();
                };
            }

            var dateControl = FindFieldControl("Дата", "doc_date", "date");
            if (dateControl is DatePicker datePicker)
            {
                datePicker.SelectedDateChanged += async (_, _) =>
                {
                    if (_fixedAssetUseLatestCurrencyRateCheckBox?.IsChecked == true)
                        await ApplyFixedAssetLatestCurrencyRateAsync();
                };
            }
        }

        private async Task ApplyFixedAssetLatestCurrencyRateAsync()
        {
            if (_fixedAssetApplyingLatestCurrencyRate ||
                _fixedAssetUseLatestCurrencyRateCheckBox?.IsChecked != true)
                return;

            _fixedAssetApplyingLatestCurrencyRate = true;
            try
            {
                if (!TryGetSelectedFixedAssetCurrencyId(out var currencyId))
                {
                    SetMovementDecimal("Курс валюты", "exchange_rate", 0m);
                    RecalculateFixedAssetMovementTotals();
                    return;
                }

                var latestRate = await _metadataService.GetLatestCurrencyRateAsync(currencyId, GetFixedAssetMovementRateDate());
                SetMovementDecimal("Курс валюты", "exchange_rate", latestRate?.Rate ?? 0m);
                RecalculateFixedAssetMovementTotals();
            }
            finally
            {
                _fixedAssetApplyingLatestCurrencyRate = false;
            }
        }

        private bool TryGetSelectedFixedAssetCurrencyId(out Guid currencyId)
        {
            currencyId = Guid.Empty;
            var field = FindDialogField("Валюта", "currency_id");
            if (field == null || !_fieldControls.TryGetValue(field.Name, out var control))
                return false;

            if (control is ReferencePickerControl picker &&
                picker.SelectedReferenceItem is BIS.ERP.Models.ReferenceItem pickerItem &&
                pickerItem.Id != Guid.Empty)
            {
                currencyId = pickerItem.Id;
                return true;
            }

            if (control is ComboBox comboBox &&
                comboBox.SelectedItem is BIS.ERP.Models.ReferenceItem comboItem &&
                comboItem.Id != Guid.Empty)
            {
                currencyId = comboItem.Id;
                return true;
            }

            var value = GetValueFromControl(control, field)?.ToString();
            return Guid.TryParse(value, out currencyId) && currencyId != Guid.Empty;
        }

        private DateTime GetFixedAssetMovementRateDate()
        {
            var field = FindDialogField("Дата", "doc_date", "date");
            if (field != null && _fieldControls.TryGetValue(field.Name, out var control))
            {
                if (control is DatePicker datePicker && datePicker.SelectedDate.HasValue)
                    return datePicker.SelectedDate.Value.Date;

                if (control is TextBox textBox &&
                    (DateTime.TryParse(textBox.Text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var localDate) ||
                     DateTime.TryParse(textBox.Text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out localDate)))
                    return localDate.Date;
            }

            return DateTime.Today;
        }

        private Control? FindFieldControl(params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field != null && _fieldControls.TryGetValue(field.Name, out var fieldControl))
                return fieldControl;

            foreach (var alias in aliases)
            {
                if (_fieldControls.TryGetValue(alias, out var control))
                    return control;
            }

            return null;
        }
        private void AttachFixedAssetMovementRecalculation(MetadataField field, Control inputControl)
        {
            if (!IsFixedAssetMovementDocument() || _isReadOnly || inputControl is not TextBox box)
                return;

            if (!IsFixedAssetMovementRecalculationSource(field))
                return;

            box.TextChanged += (_, _) =>
            {
                ActivateFixedAssetLineRowForControl(inputControl);
                RecalculateFixedAssetMovementTotals();
            };
        }

        private static bool IsFixedAssetMovementRecalculationSource(MetadataField field)
        {
            return MatchesFixedAssetMovementField(field,
                "Без НДС", "amount_without_vat",
                "Курс валюты", "exchange_rate");
        }

        private static bool MatchesFixedAssetMovementField(MetadataField field, params string[] aliases)
        {
            return aliases.Any(alias =>
                string.Equals(field.Name, alias, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(field.DbColumnName, alias, StringComparison.OrdinalIgnoreCase));
        }

        private decimal GetSelectedTaxRate(ComboBox combo)
        {
            if (combo.SelectedItem is not BIS.ERP.Models.ReferenceItem item || item.Id == Guid.Empty)
                return 0m;

            var row = _taxCatalogRows.FirstOrDefault(candidate =>
                ReadFixedAssetMovementText(candidate, "Id")
                    .Equals(item.Id.ToString(), StringComparison.OrdinalIgnoreCase));
            return row != null ? TryGetTaxRowDecimal(row, "Ставка", "rate") : 0m;
        }

        private decimal GetMovementDecimal(params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var match = _fieldsByName.FirstOrDefault(pair =>
                    string.Equals(pair.Key, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Value.DbColumnName, alias, StringComparison.OrdinalIgnoreCase));
                if (match.Value != null &&
                    _fieldControls.TryGetValue(match.Key, out var control) &&
                    control is TextBox box &&
                    decimal.TryParse(box.Text.Replace(',', '.'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                    return value;
            }

            return 0m;
        }

        private void SetMovementDecimal(string fieldName, string columnAlias, decimal value)
        {
            var match = _fieldsByName.FirstOrDefault(pair =>
                string.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Value.DbColumnName, columnAlias, StringComparison.OrdinalIgnoreCase));
            if (match.Value == null || !_fieldControls.TryGetValue(match.Key, out var control))
                return;

            if (control is TextBox box)
            {
                var text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(box.Text, text, StringComparison.Ordinal))
                    box.Text = text;
            }
        }

        private static bool TryGetTaxRowBool(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (key == "___none") continue;
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is bool flag) return flag;
                if (bool.TryParse(pair.Value?.ToString(), out var parsed)) return parsed;
            }

            return false;
        }

        private static decimal TryGetTaxRowDecimal(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                var pair = row.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is decimal dec) return dec;
                if (decimal.TryParse(pair.Value?.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return 0m;
        }

        private static Border CreateSection(string title, UIElement content)
        {
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 225, 235)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(26, 58, 88)),
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        content
                    }
                }
            };
        }

        private static Grid CreateTwoColumnGrid(int rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var row = 0; row < rows; row++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            return grid;
        }

        private static Grid CreateThreeColumnGrid(int rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var row = 0; row < rows; row++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            return grid;
        }

        private static Grid CreateTwoPaneGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static Grid CreateFixedAssetLineGrid()
        {
            var grid = new Grid { MinWidth = 930 };
            foreach (var width in new[] { 260d, 115d, 105d, 70d, 140d, 115d, 125d })
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headers = new[]
            {
                "Наименование ОС", "Без НДС", "НДС", "%", "Налог с продаж", "Итого", "В валюте"
            };

            for (var index = 0; index < headers.Length; index++)
            {
                var header = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(213, 225, 236)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = new SolidColorBrush(Color.FromRgb(231, 240, 248)),
                    Padding = new Thickness(6, 5, 6, 5),
                    Child = new TextBlock
                    {
                        Text = headers[index],
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, index);
                grid.Children.Add(header);
            }

            return grid;
        }

        private Grid CreateFixedAssetLineArea(Grid lineGrid)
        {
            var container = new Grid { MinHeight = 165 };
            container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(135), MinHeight = 95 });
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = lineGrid
            };
            Grid.SetRow(scrollViewer, 0);
            container.Children.Add(scrollViewer);

            var splitter = new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(226, 236, 245)),
                ResizeDirection = GridResizeDirection.Rows
            };
            Grid.SetRow(splitter, 1);
            container.Children.Add(splitter);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };

            buttonsPanel.Children.Add(CreateFixedAssetLineButton("+ Добавить запись", OnAddFixedAssetLineClick, "#2BAE66"));
            buttonsPanel.Children.Add(CreateFixedAssetLineButton("- Удалить запись", OnDeleteFixedAssetLineClick, "#E74C3C"));

            Grid.SetRow(buttonsPanel, 2);
            container.Children.Add(buttonsPanel);
            return container;
        }

        private Button CreateFixedAssetLineButton(string text, RoutedEventHandler clickHandler, string color)
        {
            var button = new Button
            {
                Content = text,
                Width = 150,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                IsEnabled = !_isReadOnly
            };

            button.Click += clickHandler;
            return button;
        }

        private async void OnAddFixedAssetLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
                return;

            if (_fixedAssetLineGrid == null || _fixedAssetMovementCatalogsDict == null)
                return;

            await AddFixedAssetLineRowAsync(_fixedAssetLineGrid, _fixedAssetMovementCatalogsDict, null);
            FocusFixedAssetLineStart();
        }

        private void OnDeleteFixedAssetLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
                return;

            if (_fixedAssetLineGrid == null || _fixedAssetLineRows.Count == 0)
                return;

            var rowToDelete = _activeFixedAssetLineRow ?? _fixedAssetLineRows.Last();
            if (_fixedAssetLineRows.Count > 1)
            {
                RemoveFixedAssetLineRow(rowToDelete);
                var rowToActivate = _fixedAssetLineRows.LastOrDefault() ?? _fixedAssetLineRows.FirstOrDefault();
                ActivateFixedAssetLineRow(rowToActivate);
                RecalculateFixedAssetMovementTotals();
                FocusFixedAssetLineStart();
                return;
            }

            foreach (var aliases in FixedAssetLineFieldAliases)
            {
                var field = FindDialogField(aliases);
                if (field != null && rowToDelete.Controls.TryGetValue(field.Name, out var control))
                    ClearControl(control);
            }

            RecalculateFixedAssetMovementTotals();
            FocusFixedAssetLineStart();
        }

        private async Task AddFixedAssetLineRowAsync(
            Grid lineGrid,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string>? usedFields)
        {
            var isAdditionalRow = _fixedAssetLineRows.Count > 0;
            var rowIndex = _fixedAssetLineRows.Count == 0 ? 1 : lineGrid.RowDefinitions.Count;
            while (lineGrid.RowDefinitions.Count <= rowIndex)
                lineGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var row = new FixedAssetLineUiRow { RowIndex = rowIndex };
            for (var column = 0; column < FixedAssetLineFieldAliases.Length; column++)
            {
                var field = FindDialogField(FixedAssetLineFieldAliases[column]);
                if (field == null)
                    continue;

                var panel = await CreateFieldPanelAsync(field, catalogsDict, showLabel: false);
                if (_fieldControls.TryGetValue(field.Name, out var control))
                {
                    if (control is FrameworkElement controlElement)
                        controlElement.Name = $"{GetSafeControlName(field.Name)}_{rowIndex}_{column}";

                    row.Controls[field.Name] = control;
                    row.Panels[field.Name] = panel;
                    AttachFixedAssetLineRowActivation(row, control, panel);
                }

                Grid.SetRow(panel, rowIndex);
                Grid.SetColumn(panel, column);
                lineGrid.Children.Add(panel);
                usedFields?.Add(field.Name);
            }

            _fixedAssetLineRows.Add(row);
            ActivateFixedAssetLineRow(row);
            if (isAdditionalRow)
                ClearFixedAssetLineRow(row);

            RecalculateFixedAssetMovementTotals();
        }

        private void AttachFixedAssetLineRowActivation(
            FixedAssetLineUiRow row,
            Control control,
            FrameworkElement panel)
        {
            panel.GotFocus += (_, _) => ActivateFixedAssetLineRow(row);
            control.GotFocus += (_, _) => ActivateFixedAssetLineRow(row);
            control.GotKeyboardFocus += (_, _) => ActivateFixedAssetLineRow(row);

            if (control is ComboBox comboBox)
                comboBox.SelectionChanged += (_, _) => ActivateFixedAssetLineRow(row);
            if (control is TextBox textBox)
                textBox.TextChanged += (_, _) => ActivateFixedAssetLineRow(row);
        }

        private void ActivateFixedAssetLineRowForControl(Control control)
        {
            var row = _fixedAssetLineRows.FirstOrDefault(item =>
                item.Controls.Values.Any(value => ReferenceEquals(value, control)));
            if (row != null)
                ActivateFixedAssetLineRow(row);
        }

        private void ActivateFixedAssetLineRow(FixedAssetLineUiRow? row)
        {
            if (row == null)
                return;

            _activeFixedAssetLineRow = row;
            foreach (var pair in row.Controls)
                _fieldControls[pair.Key] = pair.Value;
            foreach (var pair in row.Panels)
                _fieldPanels[pair.Key] = pair.Value;
        }

        private void RemoveFixedAssetLineRow(FixedAssetLineUiRow row)
        {
            if (_fixedAssetLineGrid == null)
                return;

            foreach (var panel in row.Panels.Values)
                _fixedAssetLineGrid.Children.Remove(panel);

            _fixedAssetLineRows.Remove(row);
            if (row.RowIndex >= 0 && row.RowIndex < _fixedAssetLineGrid.RowDefinitions.Count)
                _fixedAssetLineGrid.RowDefinitions.RemoveAt(row.RowIndex);

            foreach (var remainingRow in _fixedAssetLineRows.Where(item => item.RowIndex > row.RowIndex))
            {
                remainingRow.RowIndex--;
                foreach (var panel in remainingRow.Panels.Values)
                    Grid.SetRow(panel, remainingRow.RowIndex);
            }
        }

        private void ClearFixedAssetLineRow(FixedAssetLineUiRow row)
        {
            foreach (var control in row.Controls.Values)
                ClearControl(control);
        }

        private void FocusFixedAssetLineStart()
        {
            var field = FindDialogField("Основное средство", "fixed_asset_id", "asset_id");
            if (field == null)
                return;

            if (_activeFixedAssetLineRow?.Controls.TryGetValue(field.Name, out var rowControl) == true)
            {
                if (rowControl is FrameworkElement element)
                    element.BringIntoView();
                rowControl.Focus();
                return;
            }

            if (_fieldControls.TryGetValue(field.Name, out var control))
                control.Focus();
        }

        private async Task<bool> AddFieldToGridAsync(
            Grid grid,
            int row,
            int column,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields,
            params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field == null)
                return false;

            return await AddFieldByMetadataToGridAsync(grid, row, column, field, catalogsDict, usedFields);
        }

        private async Task<bool> AddCompactFieldToGridAsync(
            Grid grid,
            int row,
            int column,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields,
            params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field == null)
                return false;

            return await AddFieldByMetadataToGridAsync(grid, row, column, field, catalogsDict, usedFields, showLabel: false);
        }

        private bool MarkFieldAsUsed(ISet<string> usedFields, params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field == null)
                return false;

            usedFields.Add(field.Name);
            return true;
        }

        private async Task<FrameworkElement?> CreateOptionalFixedAssetBasisPanelAsync(
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields)
        {
            var field = FindDialogField("Основание", "basis");
            if (field == null || _fieldControls.ContainsKey(field.Name))
                return null;

            var fieldPanel = await CreateFieldPanelAsync(field, catalogsDict);
            fieldPanel.Margin = new Thickness(0, 6, 10, 0);
            usedFields.Add(field.Name);

            var hasBasisValue = !string.IsNullOrWhiteSpace(ReadFixedAssetMovementText(_existingData, field.Name, field.DbColumnName));
            fieldPanel.Visibility = hasBasisValue ? Visibility.Visible : Visibility.Collapsed;

            var checkBox = new CheckBox
            {
                Content = "Показать основание",
                IsChecked = hasBasisValue,
                IsEnabled = !_isReadOnly,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0)
            };

            checkBox.Checked += (_, _) => fieldPanel.Visibility = Visibility.Visible;
            checkBox.Unchecked += (_, _) =>
            {
                fieldPanel.Visibility = Visibility.Collapsed;
                if (_fieldControls.TryGetValue(field.Name, out var control))
                    ClearControl(control);
            };

            return new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 10),
                Children = { checkBox, fieldPanel }
            };
        }
        private async Task<bool> AddFixedAssetExchangeRateFieldToGridAsync(
            Grid grid,
            int row,
            int column,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields)
        {
            var field = FindDialogField("Курс валюты", "exchange_rate");
            if (field == null || _fieldControls.ContainsKey(field.Name))
                return false;

            var panel = await CreateFieldPanelAsync(field, catalogsDict);
            if (!_isReadOnly)
            {
                _fixedAssetUseLatestCurrencyRateCheckBox = new CheckBox
                {
                    Content = "Взять последний курс",
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _fixedAssetUseLatestCurrencyRateCheckBox.Checked += async (_, _) => await ApplyFixedAssetLatestCurrencyRateAsync();
                _fixedAssetUseLatestCurrencyRateCheckBox.Unchecked += (_, _) =>
                {
                    SetMovementDecimal("Курс валюты", "exchange_rate", 0m);
                    RecalculateFixedAssetMovementTotals();
                };
                panel.Children.Add(_fixedAssetUseLatestCurrencyRateCheckBox);
            }

            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            usedFields.Add(field.Name);
            return true;
        }
        private async Task<bool> AddFieldByMetadataToGridAsync(
            Grid grid,
            int row,
            int column,
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields,
            bool showLabel = true)
        {
            if (_fieldControls.ContainsKey(field.Name))
                return false;

            var panel = await CreateFieldPanelAsync(field, catalogsDict, showLabel);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
            usedFields.Add(field.Name);
            return true;
        }

        private async Task<bool> AddHiddenFieldAsync(
            Dictionary<string, MetadataObject> catalogsDict,
            ISet<string> usedFields,
            params string[] aliases)
        {
            var field = FindDialogField(aliases);
            if (field == null || _fieldControls.ContainsKey(field.Name))
                return false;

            var panel = await CreateFieldPanelAsync(field, catalogsDict);
            panel.Visibility = Visibility.Collapsed;
            _fieldPanels[field.Name] = panel;
            usedFields.Add(field.Name);
            return true;
        }

        private async Task<StackPanel> CreateFieldPanelAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            bool showLabel = true)
        {
            var panel = new StackPanel
            {
                Margin = showLabel
                    ? new Thickness(0, 0, 10, 10)
                    : new Thickness(3, 2, 3, 4)
            };

            if (showLabel)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = field.Name,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            var inputControl = await CreateControlAsync(field, catalogsDict);
            if (_isReadOnly)
                ApplyReadOnly(inputControl);

            AttachFixedAssetMovementRecalculation(field, inputControl);

            if (!showLabel && inputControl is FrameworkElement inputElement)
            {
                inputElement.MinHeight = 30;
                inputElement.Margin = new Thickness(0);
            }

            if (_metadata.ObjectType == "Document" &&
                MetadataService.IsDocumentNumberFieldName(field.Name) &&
                inputControl is TextBox numberTextBox &&
                !_editId.HasValue)
            {
                numberTextBox.IsReadOnly = true;
                numberTextBox.Background = Brushes.LightGray;

                try
                {
                    numberTextBox.Text = await _metadataService.GetNextDocumentNumberAsync(_metadata);
                }
                catch
                {
                    numberTextBox.Text = MetadataService.GenerateFallbackDocumentNumber();
                }
            }

            panel.Children.Add(inputControl);
            _fieldControls[field.Name] = inputControl;
            _fieldPanels[field.Name] = panel;
            _fieldsByName[field.Name] = field;
            return panel;
        }

        private MetadataField? FindDialogField(params string[] aliases)
        {
            foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                var field = _metadata.Fields.FirstOrDefault(item =>
                    string.Equals(item.Name, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DbColumnName, alias, StringComparison.OrdinalIgnoreCase));

                if (field != null)
                    return field;
            }

            return null;
        }
        private async Task<Dictionary<string, object>?> LoadExistingDataAsync()
        {
            if (!_editId.HasValue)
                return _initialData == null
                    ? null
                    : new Dictionary<string, object>(_initialData, StringComparer.OrdinalIgnoreCase);

            var data = await _metadataService.GetCatalogDataAsync(_metadata.Id);
            return data.FirstOrDefault(row =>
                row.TryGetValue("Id", out var id) && id?.ToString() == _editId.Value.ToString());
        }

        private async Task<Control> CreateControlAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict)
        {
            var safeName = GetSafeControlName(field.Name);
            var currentValue = _existingData?.GetValueOrDefault(field.Name);

            if (AccountAnalyticsRules.IsAccountSelectorField(field) && _accountAnalytics.Accounts.Count > 0)
            {
                return AccountPickerControlFactory.Create(
                    _accountAnalytics,
                    currentValue,
                    this,
                    UpdateAccountControlledFieldsVisibility,
                    _assignedModuleName);
            }

            // Для документа движения ОС налоговые поля «Вид НДС» и «Вид налога с продаж»
            // отображаются как простые выпадающие списки из справочника «Налоги» (как в счете-фактуре).
            if (IsFixedAssetMovementDocument() &&
                (string.Equals(field.DbColumnName, "vat_type_id", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(field.DbColumnName, "sales_tax_type_id", StringComparison.OrdinalIgnoreCase)))
            {
                return CreateFixedAssetTaxComboBox(field, currentValue);
            }

            if (!string.IsNullOrEmpty(field.ReferenceCatalog))
                return await CreateReferenceControlAsync(field, catalogsDict, safeName, currentValue);

            if (!string.IsNullOrEmpty(field.Formula))
            {
                return new TextBox
                {
                    Height = 30,
                    Name = safeName,
                    IsReadOnly = true,
                    Background = Brushes.LightGray,
                    Text = currentValue?.ToString() ?? string.Empty
                };
            }

            return CreateRegularControl(field, safeName, currentValue);
        }

        private async Task<Control> CreateReferenceControlAsync(
            MetadataField field,
            Dictionary<string, MetadataObject> catalogsDict,
            string safeName,
            object? currentValue)
        {
            if (!catalogsDict.TryGetValue(field.ReferenceCatalog, out var refCatalog))
            {
                var comboBox = new ComboBox
                {
                    Height = 30,
                    Name = safeName,
                    DisplayMemberPath = "DisplayName",
                    SelectedValuePath = "Id",
                    MinWidth = 200
                };

                comboBox.ItemsSource = new List<BIS.ERP.Models.ReferenceItem>
                {
                    new BIS.ERP.Models.ReferenceItem
                    {
                        Id = Guid.Empty,
                        DisplayName = $"Справочник '{field.ReferenceCatalog}' не найден"
                    }
                };
                return comboBox;
            }

            return await ReferencePickerControlFactory.CreateAsync(
                _metadataService,
                field,
                refCatalog,
                currentValue,
                this,
                UpdateAccountControlledFieldsVisibility);
        }

        private static IEnumerable<string> GetReferenceLookupKeys(
            Dictionary<string, object> row,
            string displayName)
        {
            foreach (var keyName in new[]
                     {
                         "Id", "Код", "code", "Code", "Счет", "account_code",
                         "Код организации", "organization_code",
                         "Наименование", "name", "ФИО", "full_name"
                     })
            {
                if (!row.TryGetValue(keyName, out var value))
                    continue;

                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }

            if (!string.IsNullOrWhiteSpace(displayName))
                yield return displayName.Trim();

            var displayKey = NormalizeReferenceLookupKey(displayName);
            if (!string.IsNullOrWhiteSpace(displayKey))
                yield return displayKey;

            foreach (var value in row.Values)
            {
                var normalized = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(normalized))
                    yield return normalized;
            }
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? normalized[..separatorIndex].Trim()
                : normalized;
        }

        private Control CreateRegularControl(MetadataField field, string safeName, object? currentValue)
        {
            switch (field.FieldType)
            {
                case "DateTime":
                    var datePicker = new DatePicker { Height = 30, Name = safeName };
                    if (currentValue is DateTime dt)
                        datePicker.SelectedDate = dt;
                    else if (!_editId.HasValue)
                        datePicker.SelectedDate = DateTime.Today;
                    return datePicker;

                case "Bool":
                    var checkBox = new CheckBox
                    {
                        Content = "Да",
                        Name = safeName,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (currentValue is bool boolValue)
                        checkBox.IsChecked = boolValue;
                    else if (bool.TryParse(currentValue?.ToString(), out var parsedBool))
                        checkBox.IsChecked = parsedBool;
                    return checkBox;

                default:
                    return new TextBox
                    {
                        Height = 30,
                        Name = safeName,
                        Text = currentValue?.ToString() ??
                               (!_editId.HasValue && field.FieldType == "Decimal" ? "0" : string.Empty)
                    };
            }
        }

        private string GetSafeControlName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "control";
            return new string(fieldName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        private string GetDisplayValue(Dictionary<string, object> row, MetadataField field)
        {
            if (!string.IsNullOrEmpty(field.DisplayPattern))
            {
                var result = field.DisplayPattern;
                var fieldNames = field.DisplayFields?.Split(',') ?? Array.Empty<string>();

                foreach (var displayField in fieldNames.Select(name => name.Trim()))
                {
                    var value = row.GetValueOrDefault(displayField)?.ToString() ??
                                row.GetValueOrDefault(displayField.Replace(" ", "_"))?.ToString() ??
                                string.Empty;

                    result = result.Replace($"{{{displayField}}}", value);
                    result = result.Replace($"{{{displayField.Replace(" ", "_")}}}", value);
                }

                return result;
            }

            var priorityFields = new[]
            {
                "Наименование", "name", "ФИО", "full_name",
                "Наименование материала", "Код", "code", "Счет"
            };

            foreach (var priority in priorityFields)
            {
                if (row.TryGetValue(priority, out var value) && value != null)
                    return value.ToString() ?? string.Empty;
            }

            return row.GetValueOrDefault("Id")?.ToString() ?? "Без имени";
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var selectedSettings = _fieldControls
                .Where(pair => _fieldsByName.TryGetValue(pair.Key, out var field) &&
                               AccountAnalyticsRules.IsAccountSelectorField(field))
                .Select(pair => AccountPickerControlFactory.GetSelectedAccount(pair.Value))
                .Where(account => account != null)
                .Select(account => _accountAnalytics.GetSettings(account))
                .ToList();

            foreach (var field in _metadata.Fields)
            {
                if (field.FieldType != "Reference" ||
                    AccountAnalyticsRules.IsAccountSelectorField(field))
                {
                    continue;
                }

                var keepUnmappedReferencesVisible = IsFixedAssetMovementDocument();
                if (!AccountAnalyticsRules.IsAccountControlledField(field, _accountAnalytics.Definitions))
                {
                    SetFieldVisibility(field.Name, keepUnmappedReferencesVisible);
                    continue;
                }

                SetFieldVisibility(
                    field.Name,
                    AccountAnalyticsRules.ShouldShowField(
                        field,
                        selectedSettings,
                        _accountAnalytics.Definitions,
                        showWhenNoAccountSelected: keepUnmappedReferencesVisible,
                        showUnmappedFields: keepUnmappedReferencesVisible));
            }
        }

        private void SetFieldVisibility(string fieldName, bool isVisible)
        {
            if (!_fieldPanels.TryGetValue(fieldName, out var panel))
                return;

            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && _fieldControls.TryGetValue(fieldName, out var control))
                ClearControl(control);
        }

        private static void ClearControl(Control control)
        {
            switch (control)
            {
                case ComboBox comboBox:
                    comboBox.SelectedItem = null;
                    break;
                case ReferencePickerControl referencePicker:
                    referencePicker.ClearSelection();
                    break;
                case TextBox textBox:
                    textBox.Clear();
                    break;
                case CheckBox checkBox:
                    checkBox.IsChecked = false;
                    break;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnly)
            {
                Close();
                return;
            }

            try
            {
                ItemData.Clear();

                foreach (var field in _metadata.Fields.OrderBy(f => f.Order))
                {
                    if (!_fieldControls.TryGetValue(field.Name, out var control))
                        continue;

                    object? value;

                    if (_fieldPanels.TryGetValue(field.Name, out var panel) &&
                        panel.Visibility == Visibility.Collapsed)
                    {
                        value = IsFixedAssetMovementDocument() &&
                                string.Equals(field.Name, "Вид документа ОС", StringComparison.OrdinalIgnoreCase)
                            ? GetValueFromControl(control, field)
                            : AccountAnalyticsRules.GetEmptyValue(field);
                    }
                    else
                    {
                        value = GetValueFromControl(control, field);
                    }

                    if (_metadata.ObjectType == "Document" &&
                        MetadataService.IsDocumentNumberFieldName(field.Name))
                    {
                        var documentNumber = MetadataService.NormalizeLegacyDocumentNumber(value?.ToString());
                        if (string.IsNullOrWhiteSpace(documentNumber) || documentNumber.Any(c => !char.IsDigit(c)))
                        {
                            MessageBox.Show("Номер документа должен содержать только цифры.", "Проверка",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            control.Focus();
                            return;
                        }

                        value = documentNumber;
                    }

                    ItemData[field.Name] = value ?? AccountAnalyticsRules.GetEmptyValue(field);
                }

                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private object? GetValueFromControl(Control control, MetadataField field)
        {
            switch (control)
            {
                case UserControl when AccountPickerControlFactory.GetSelectedAccount(control) != null:
                    return AccountPickerControlFactory.GetSelectedAccountValue(field, control);
                case ReferencePickerControl referencePicker when referencePicker.SelectedReferenceItem is BIS.ERP.Models.ReferenceItem selectedReference:
                    return selectedReference.Id == Guid.Empty ? string.Empty : selectedReference.Id.ToString();
                case ReferencePickerControl:
                    return string.Empty;
                case ComboBox comboBox when comboBox.SelectedItem is AccountReferenceItem account:
                    return AccountAnalyticsRules.GetAccountValueForField(field, account);
                case ComboBox comboBox when comboBox.SelectedItem is BIS.ERP.Models.ReferenceItem selectedItem:
                    return selectedItem.Id == Guid.Empty ? string.Empty : selectedItem.Id.ToString();
                case ComboBox:
                    return string.Empty;
                case DatePicker datePicker:
                    return datePicker.SelectedDate;
                case CheckBox checkBox:
                    return checkBox.IsChecked ?? false;
                case TextBox textBox:
                    return ParseFieldValue(textBox.Text, field.FieldType);
                default:
                    return null;
            }
        }

        private object? ParseFieldValue(string text, string fieldType)
        {
            if (string.IsNullOrWhiteSpace(text))
                return AccountAnalyticsRules.GetEmptyValue(new MetadataField { FieldType = fieldType });

            try
            {
                return fieldType switch
                {
                    "Int" => int.Parse(text),
                    "Decimal" => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                    _ => text
                };
            }
            catch
            {
                return AccountAnalyticsRules.GetEmptyValue(new MetadataField { FieldType = fieldType });
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        private static void ApplyReadOnly(Control control)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.IsReadOnly = true;
                    textBox.Background = Brushes.LightGray;
                    break;
                case ComboBox comboBox:
                    comboBox.IsEnabled = false;
                    break;
                case DatePicker datePicker:
                    datePicker.IsEnabled = false;
                    break;
                case CheckBox checkBox:
                    checkBox.IsEnabled = false;
                    break;
                default:
                    control.IsEnabled = false;
                    break;
            }
        }
    }
}



