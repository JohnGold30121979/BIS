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

namespace BIS.ERP.Views.Dialogs
{
    /// <summary>
    /// Окно документа ТМЗ по образцу счёта-фактуры: шапка, таблица строк, итоги.
    /// Работает для «Поступление ТМЗ», «Выбытие ТМЗ», «Внутреннее перемещение ТМЗ».
    /// </summary>
    public partial class InventoryDocumentDialog : Window
    {
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
        private readonly Dictionary<Guid, Dictionary<string, object>> _materialRows = new();
        private readonly Dictionary<Guid, string> _entryCodes = new();

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
            _recordId = recordId;
            _existing = existingData == null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(existingData, StringComparer.OrdinalIgnoreCase);

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

                var accounts = await BuildItemsAsync(catalogs, "План счетов", idAsCode: true,
                    display: row => $"{Read(row, "Код")} - {Read(row, "Наименование")}".Trim(' ', '-'));
                var organizations = await BuildItemsAsync(catalogs, "Организации",
                    display: row => Read(row, "Наименование", "Полное наименование"));
                var employees = await BuildItemsAsync(catalogs, "Сотрудники (Списочный состав)",
                    display: row => Read(row, "ФИО", "Наименование"));
                var supplyKinds = await BuildItemsAsync(catalogs, "Типы поставки",
                    display: row => Read(row, "Наименование"));
                var taxes = await BuildItemsAsync(catalogs, "Налоги",
                    display: row => Read(row, "Наименование"));
                var paymentKinds = await BuildItemsAsync(catalogs, "Виды оплаты",
                    display: row => Read(row, "Наименование"));
                var currencies = await BuildItemsAsync(catalogs, "Справочник валют",
                    display: row => Read(row, "Наименование", "Код"));
                var sites = await BuildItemsAsync(catalogs, "Участки",
                    display: row => Read(row, "Наименование"));
                var responsible = await BuildItemsAsync(catalogs, "МОЛ",
                    display: row => Read(row, "Наименование", "ФИО"));
                var materials = await BuildItemsAsync(catalogs, "Справочник материалов",
                    display: row => $"{Read(row, "Код")} - {Read(row, "Наименование материала", "Наименование")}".Trim(' ', '-'),
                    store: _materialRows);
                var entries = await BuildItemsAsync(catalogs, InventoryDocumentModel.EntryCatalogName,
                    display: row =>
                    {
                        var id = ReadGuid(row);
                        if (id.HasValue)
                            _entryCodes[id.Value] = Read(row, "code", "Код");
                        return $"{Read(row, "Код")} - {Read(row, "Наименование")}".Trim(' ', '-');
                    });

                BuildHeader(accounts, organizations, employees, supplyKinds, taxes, paymentKinds, currencies, sites, responsible, entries);

                MaterialColumn.ItemsSource = materials;
                AccountColumn.ItemsSource = accounts;
                SiteColumn.ItemsSource = sites;
                ResponsibleColumn.ItemsSource = responsible;

                ApplyExistingValues();

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

        private void BuildHeader(
            List<RefItem> accounts,
            List<RefItem> organizations,
            List<RefItem> employees,
            List<RefItem> supplyKinds,
            List<RefItem> taxes,
            List<RefItem> paymentKinds,
            List<RefItem> currencies,
            List<RefItem> sites,
            List<RefItem> responsible,
            List<RefItem> entries)
        {
            AddText("Номер", "doc_number", 110);
            AddDate("Дата", "doc_date", 140);
            AddCombo("Вид операции", "document_entry_id", entries, 240);
            AddText("№ пачки", "pack_number", 80);
            AddText("Серия/№ бланка", "tax_blank_number", 130);
            AddText("Номер ЭСФ", "esf_number", 130);
            AddCombo("Счет", "counterparty_account", accounts, 200);
            AddCombo("Организация", "organization_id", organizations, 240);
            AddCombo("Сотрудник", "employee_id", employees, 200);
            AddCombo("Вид покупки", "supply_kind", supplyKinds, 200);
            AddCombo("Вид НДС", "vat_type", taxes, 200);
            AddText("Ставка НДС, %", "vat_rate", 90);
            AddCombo("Вид оплаты", "payment_kind", paymentKinds, 170);
            AddCombo("Налог", "sales_tax_type", taxes, 200);
            AddText("Ставка налога, %", "sales_tax_rate", 90);
            AddCombo("Валюта", "currency_id", currencies, 150);
            AddText("Курс", "exchange_rate", 90);
            AddCombo("Участок-получатель", "destination_site_id", sites, 180);
            AddCombo("МОЛ-получатель", "destination_person_id", responsible, 180);
            AddText("Примечание", "basis", 420);
        }

        private void AddText(string label, string column, double width)
        {
            var box = new TextBox { Width = width, Height = 28, Padding = new Thickness(6, 3, 6, 3) };
            AddEditor(label, column, box);
        }

        private void AddDate(string label, string column, double width)
        {
            var picker = new DatePicker { Width = width, Height = 28 };
            AddEditor(label, column, picker);
        }

        private void AddCombo(string label, string column, List<RefItem> items, double width)
        {
            var combo = new ComboBox
            {
                Width = width,
                Height = 28,
                ItemsSource = items,
                DisplayMemberPath = "Display",
                SelectedValuePath = "Id"
            };
            AddEditor(label, column, combo);
        }

        private void AddEditor(string label, string column, Control control)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            panel.Children.Add(control);
            HeaderPanel.Children.Add(panel);
            _editors[column] = control;
        }

        private async Task<List<RefItem>> BuildItemsAsync(
            List<MetadataObject> catalogs,
            string catalogName,
            Func<Dictionary<string, object>, string> display,
            bool idAsCode = false,
            Dictionary<Guid, Dictionary<string, object>>? store = null)
        {
            var result = new List<RefItem>();
            var catalog = catalogs.FirstOrDefault(item =>
                string.Equals(item.Name, catalogName, StringComparison.OrdinalIgnoreCase));
            if (catalog == null)
                return result;

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            foreach (var row in rows)
            {
                var text = display(row) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    text = Read(row, "Код", "code");

                if (idAsCode)
                {
                    result.Add(new RefItem { Id = text.Split(" - ")[0], Display = text });
                    continue;
                }

                var id = ReadGuid(row);
                if (!id.HasValue)
                    continue;

                if (store != null)
                    store[id.Value] = row;

                result.Add(new RefItem { Id = id.Value, Display = text });
            }

            return result.OrderBy(item => item.Display).ToList();
        }

        private void ApplyExistingValues()
        {
            if (_existing.Count == 0)
            {
                if (_editors.TryGetValue("doc_date", out var dateEditor) && dateEditor is DatePicker picker)
                    picker.SelectedDate = DateTime.Today;
                return;
            }

            foreach (var field in _document.Fields)
            {
                if (!_editors.TryGetValue(field.DbColumnName, out var control))
                    continue;

                if (!_existing.TryGetValue(field.Name, out var value) || value == null || value == DBNull.Value)
                    continue;

                switch (control)
                {
                    case TextBox textBox:
                        textBox.Text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
                        break;
                    case DatePicker datePicker:
                        if (value is DateTime date)
                            datePicker.SelectedDate = date;
                        else if (DateTime.TryParse(value.ToString(), out var parsed))
                            datePicker.SelectedDate = parsed;
                        break;
                    case ComboBox comboBox:
                        comboBox.SelectedValue = value;
                        if (comboBox.SelectedValue == null && value is string text)
                        {
                            var match = comboBox.Items.OfType<RefItem>()
                                .FirstOrDefault(item => string.Equals(item.Id?.ToString(), text, StringComparison.OrdinalIgnoreCase));
                            comboBox.SelectedItem = match;
                        }
                        break;
                }
            }
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
                _lines.Add(row);

            if (_lines.Count == 0)
                AddLine();
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e) => AddLine();

        private void AddLine()
        {
            var row = new InventoryLineRow
            {
                VatRate = ReadDecimalEditor("vat_rate"),
                SalesTaxRate = ReadDecimalEditor("sales_tax_rate")
            };
            _lines.Add(row);
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
                var value = ReadEditorValue(field);
                if (value != null)
                    data[field.Name] = value;
            }

            InventoryDocumentLineService.RecalculateTotals(
                _lines, out var withoutTax, out var vat, out var salesTax, out var total);

            SetField(data, "amount_without_tax", withoutTax);
            SetField(data, "vat_total", vat);
            SetField(data, "sales_tax_total", salesTax);
            SetField(data, "amount", total);
            SetField(data, "amount_currency", total);

            if (_editors.TryGetValue("document_entry_id", out var entryEditor) &&
                entryEditor is ComboBox { SelectedValue: Guid entryId } &&
                _entryCodes.TryGetValue(entryId, out var code))
            {
                SetField(data, "operation_kind", code);
            }

            if (!_recordId.HasValue)
                SetField(data, "is_posted", false);

            return data;
        }

        private object? ReadEditorValue(MetadataField field)
        {
            if (!_editors.TryGetValue(field.DbColumnName, out var control))
                return null;

            switch (control)
            {
                case TextBox textBox:
                {
                    var text = textBox.Text?.Trim() ?? string.Empty;
                    if (text.Length == 0)
                        return null;

                    return field.FieldType switch
                    {
                        "Decimal" => decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d)
                            ? d
                            : decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var di) ? di : null,
                        "Int" => int.TryParse(text, out var i) ? i : null,
                        _ => text
                    };
                }
                case DatePicker datePicker:
                    return datePicker.SelectedDate;
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
