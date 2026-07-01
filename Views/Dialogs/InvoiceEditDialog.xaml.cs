using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views;

namespace BIS.ERP.Views.Dialogs
{
    public partial class InvoiceEditDialog : Window
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
        private bool _isRecalculating;
        private bool _isPosted;

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
            Loaded += async (_, _) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                Cursor = System.Windows.Input.Cursors.Wait;
                await _invoiceService.EnsureSchemaAsync();
                var catalogs = await _metadataService.GetCatalogsAsync();
                var organizationsCatalog = catalogs.FirstOrDefault(item => item.Name == "Организации");
                if (organizationsCatalog != null)
                {
                    var organizations = await _metadataService.GetCatalogDataAsync(organizationsCatalog.Id);
                    OrganizationCombo.ItemsSource = organizations.Select(item => new OrganizationItem
                    {
                        Id = Guid.Parse(item["Id"].ToString()!),
                        DisplayName = ReferenceDisplayHelper.BuildDisplayValue(item, new MetadataField())
                    }).OrderBy(item => item.DisplayName).ToList();
                }

                var accountsCatalog = catalogs.FirstOrDefault(item => item.Name.StartsWith("План счетов"));
                if (accountsCatalog != null)
                {
                    _accounts = await _metadataService.GetCatalogDataAsync(accountsCatalog.Id);
                    FillAccountItems(_accounts);
                }

                PaymentKindCombo.ItemsSource = await LoadReferenceOptionsAsync(catalogs, "Виды оплаты");
                DeliveryKindCombo.ItemsSource = await LoadReferenceOptionsAsync(catalogs, "Виды поставки");
                SupplyKindCombo.ItemsSource = await LoadReferenceOptionsAsync(catalogs, "Типы поставки");
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
                    SetHeaderAccount(invoice.CounterpartyAccountCode);
                    BasisBox.Text = invoice.Basis;
                    SelectStoredComboValue(PaymentKindCombo, invoice.PaymentKind);
                    SelectStoredComboValue(DeliveryKindCombo, invoice.DeliveryKind);
                    SelectStoredComboValue(SupplyKindCombo, invoice.SupplyKind);

                    if (invoice.OrganizationId.HasValue)
                    {
                        foreach (OrganizationItem item in OrganizationCombo.Items)
                        {
                            if (item.Id == invoice.OrganizationId.Value)
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

                    AllPostingsButton.IsEnabled = true;
                    if (_isPosted)
                        DisableEditing();
                }
                else
                {
                    DatePicker.SelectedDate = DateTime.Today;
                    NumberBox.Text = await _invoiceService.GenerateDocumentNumberAsync();
                    SetHeaderAccount(GetDefaultAccountCode());
                    SelectComboValue(PaymentKindCombo, "TRANSFER");
                    SelectComboValue(DeliveryKindCombo, "TAXABLE");
                    SelectComboValue(SupplyKindCombo, "TAXABLE");
                }

                RecalculateTotals();
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

        private void DisableEditing()
        {
            NumberBox.IsReadOnly = true;
            DatePicker.IsEnabled = false;
            EsfNumberBox.IsReadOnly = true;
            BasisBox.IsReadOnly = true;
            OrganizationCombo.IsEnabled = false;
            PaymentKindCombo.IsEnabled = false;
            DeliveryKindCombo.IsEnabled = false;
            SupplyKindCombo.IsEnabled = false;
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
                    BuildReferenceDisplayName(GetRowValue(row, "Наименование", "name"), GetDecimal(row, "Ставка", "rate")),
                    GetDecimal(row, "Ставка", "rate")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(item => item.DisplayName)
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
                         item.Value.StartsWith("NDS", StringComparison.OrdinalIgnoreCase) ||
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

            if (e.PropertyName == nameof(EditableInvoiceLine.VatTaxCode))
                ApplyTaxRate(line, line.VatTaxCode, _vatTaxesByCode, isVat: true);
            if (e.PropertyName == nameof(EditableInvoiceLine.SalesTaxCode))
                ApplyTaxRate(line, line.SalesTaxCode, _salesTaxesByCode, isVat: false);

            if (e.PropertyName is nameof(EditableInvoiceLine.AmountWithoutTax)
                or nameof(EditableInvoiceLine.VatRate)
                or nameof(EditableInvoiceLine.SalesTaxRate)
                or nameof(EditableInvoiceLine.VatTaxCode)
                or nameof(EditableInvoiceLine.SalesTaxCode))
            {
                RecalculateTotals();
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

        private string GetDefaultAccountCode()
        {
            var preferred = InvoiceDocumentTypes.IsSales(_document.Name) ? "14100000" : "31100000";
            return AccountItems.Any(item => item.Value.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                ? preferred
                : AccountItems.FirstOrDefault()?.Value ?? preferred;
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

        private void OnSelectAccountClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            if (_accounts.Count == 0)
                return;

            var dialog = new AccountSelectionDialog(_accounts);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
            {
                SetHeaderAccount(dialog.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty);
            }
        }

        private void OnSelectLineAccountClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode || _isPosted || sender is not Button { Tag: EditableInvoiceLine line } || _accounts.Count == 0)
                return;

            var dialog = new AccountSelectionDialog(_accounts)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
            {
                var accountCode = dialog.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
                line.AccountCode = accountCode;
                line.AccountDisplayName = GetAccountDisplayName(accountCode);
            }
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode)
                return;

            var previous = _lines.LastOrDefault();
            var line = new EditableInvoiceLine
            {
                LineNumber = _lines.Count + 1,
                Name = previous?.Name ?? string.Empty,
                AccountCode = previous?.AccountCode ?? string.Empty,
                AccountDisplayName = previous?.AccountDisplayName ?? string.Empty,
                VatTaxCode = previous?.VatTaxCode
                             ?? VatTaxItems.FirstOrDefault(item => item.Value.Equals("NDS12", StringComparison.OrdinalIgnoreCase))?.Value
                             ?? VatTaxItems.FirstOrDefault()?.Value
                             ?? string.Empty,
                VatRate = previous?.VatRate
                          ?? VatTaxItems.FirstOrDefault(item => item.Value.Equals("NDS12", StringComparison.OrdinalIgnoreCase))?.Rate
                          ?? VatTaxItems.FirstOrDefault()?.Rate
                          ?? 0,
                SalesTaxCode = previous?.SalesTaxCode
                               ?? SalesTaxItems.FirstOrDefault(item => item.Value.Equals("WITHOUT_TAX", StringComparison.OrdinalIgnoreCase))?.Value
                               ?? SalesTaxItems.FirstOrDefault()?.Value
                               ?? string.Empty,
                SalesTaxRate = previous?.SalesTaxRate
                               ?? SalesTaxItems.FirstOrDefault(item => item.Value.Equals("WITHOUT_TAX", StringComparison.OrdinalIgnoreCase))?.Rate
                               ?? SalesTaxItems.FirstOrDefault()?.Rate
                               ?? 0
            };
            AddLine(line);
            RecalculateTotals();
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
        }

        private void OnLineSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = LinesGrid.SelectedItem != null;
            DeleteLineButton.IsEnabled = hasSelection && !_isPosted;
            LinePostingsButton.IsEnabled = hasSelection && _editId.HasValue && _isPosted;
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
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OnRecalculateClick(object sender, RoutedEventArgs e)
        {
            if (!_isReadOnlyMode)
                RecalculateTotals();
        }

        private void RecalculateTotals()
        {
            foreach (var line in _lines)
            {
                InvoiceService.RecalculateLine(line);
                line.NotifyCalculatedProperties();
            }

            var document = BuildDocumentFromForm();
            InvoiceService.RecalculateTotals(document);
            TotalWithoutTaxText.Text = document.AmountWithoutTax.ToString("N2");
            TotalVatText.Text = document.VatTotal.ToString("N2");
            TotalSalesTaxText.Text = document.SalesTaxTotal.ToString("N2");
            TotalAmountText.Text = document.TotalAmount.ToString("N2");
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isReadOnlyMode || _isPosted)
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

                var document = BuildDocumentFromForm();
                InvoiceService.RecalculateTotals(document);
                await _invoiceService.SaveInvoiceAsync(document, _editId);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private InvoiceDocument BuildDocumentFromForm()
        {
            var organizationId = (OrganizationCombo.SelectedItem as OrganizationItem)?.Id;
            return new InvoiceDocument
            {
                DocNumber = MetadataService.NormalizeLegacyDocumentNumber(NumberBox.Text),
                DocDate = DatePicker.SelectedDate ?? DateTime.Today,
                EsfNumber = EsfNumberBox.Text.Trim(),
                OrganizationId = organizationId,
                CounterpartyAccountCode = string.IsNullOrWhiteSpace(_selectedHeaderAccountCode)
                    ? AccountBox.Text.Trim()
                    : _selectedHeaderAccountCode,
                PaymentKind = GetSelectedReferenceValue(PaymentKindCombo),
                DeliveryKind = GetSelectedReferenceValue(DeliveryKindCombo),
                SupplyKind = GetSelectedReferenceValue(SupplyKindCombo),
                Basis = BasisBox.Text.Trim(),
                Lines = _lines.Select(line => new InvoiceLineRow
                {
                    Id = line.Id,
                    LineNumber = line.LineNumber,
                    Name = line.Name,
                    AccountCode = line.AccountCode,
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
            DialogResult = false;
            Close();
        }

        private static string GetSelectedReferenceValue(ComboBox comboBox)
        {
            return comboBox.SelectedValue?.ToString()
                   ?? (comboBox.SelectedItem as ReferenceOption)?.Value
                   ?? comboBox.Text?.Trim()
                   ?? string.Empty;
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
            return decimal.TryParse(GetRowValue(row, keys), out var value) ? value : 0;
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

        private static string BuildReferenceDisplayName(string name, decimal rate)
        {
            if (string.IsNullOrWhiteSpace(name))
                return rate > 0 ? $"{rate:N2}%" : string.Empty;
            if (rate > 0 && !name.Contains('%'))
                return $"{name} ({rate:N2}%)";
            return name;
        }

        private void SetHeaderAccount(string accountCode)
        {
            _selectedHeaderAccountCode = accountCode?.Trim() ?? string.Empty;
            AccountBox.Text = GetAccountDisplayName(_selectedHeaderAccountCode);
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

        public sealed record ReferenceOption(string Value, string DisplayName, decimal Rate = 0);

        private sealed class OrganizationItem
        {
            public Guid Id { get; init; }
            public string DisplayName { get; init; } = string.Empty;
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
