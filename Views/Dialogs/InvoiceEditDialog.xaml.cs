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
        private readonly ObservableCollection<EditableInvoiceLine> _lines = new();
        private List<Dictionary<string, object>> _accounts = new();
        private bool _isPosted;

        public InvoiceEditDialog(
            MetadataObject document,
            MetadataService metadataService,
            InvoiceService invoiceService,
            Guid? editId = null)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _invoiceService = invoiceService;
            _editId = editId;
            DialogTitle.Text = editId.HasValue
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
                    _accounts = await _metadataService.GetCatalogDataAsync(accountsCatalog.Id);

                PaymentKindCombo.ItemsSource = new[] { "безналичными", "наличными", "бартер" };
                DeliveryKindCombo.ItemsSource = new[] { "Облагаемые", "Освобожденные", "Экспорт" };
                SupplyKindCombo.ItemsSource = new[] { "Облагаемые", "Освобожденные" };

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
                    AccountBox.Text = invoice.CounterpartyAccountCode;
                    BasisBox.Text = invoice.Basis;
                    PaymentKindCombo.Text = invoice.PaymentKind;
                    DeliveryKindCombo.Text = invoice.DeliveryKind;
                    SupplyKindCombo.Text = invoice.SupplyKind;

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
                        _lines.Add(new EditableInvoiceLine
                        {
                            Id = line.Id,
                            LineNumber = line.LineNumber,
                            Name = line.Name,
                            AccountCode = line.AccountCode,
                            AmountWithoutTax = line.AmountWithoutTax,
                            VatRate = line.VatRate,
                            VatAmount = line.VatAmount,
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
                    AccountBox.Text = InvoiceDocumentTypes.IsSales(_document.Name) ? "14100000" : "31100000";
                    PaymentKindCombo.SelectedIndex = 0;
                    DeliveryKindCombo.SelectedIndex = 0;
                    SupplyKindCombo.SelectedIndex = 0;
                }

                RecalculateTotals();
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
            EsfNumberBox.IsReadOnly = true;
            BasisBox.IsReadOnly = true;
            OrganizationCombo.IsEnabled = false;
            PaymentKindCombo.IsEnabled = false;
            DeliveryKindCombo.IsEnabled = false;
            SupplyKindCombo.IsEnabled = false;
            LinesGrid.IsReadOnly = true;
        }

        private void OnSelectAccountClick(object sender, RoutedEventArgs e)
        {
            if (_accounts.Count == 0)
                return;

            var dialog = new AccountSelectionDialog(_accounts);
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
            {
                AccountBox.Text = dialog.SelectedAccount.GetValueOrDefault("Код")?.ToString() ?? string.Empty;
            }
        }

        private void OnAddLineClick(object sender, RoutedEventArgs e)
        {
            _lines.Add(new EditableInvoiceLine
            {
                LineNumber = _lines.Count + 1,
                VatRate = 12
            });
            RecalculateTotals();
        }

        private void OnDeleteLineClick(object sender, RoutedEventArgs e)
        {
            if (LinesGrid.SelectedItem is not EditableInvoiceLine selected)
                return;

            _lines.Remove(selected);
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

        private void OnRecalculateClick(object sender, RoutedEventArgs e) => RecalculateTotals();

        private void RecalculateTotals()
        {
            foreach (var line in _lines)
                InvoiceService.RecalculateLine(line);

            LinesGrid.Items.Refresh();
            var document = BuildDocumentFromForm();
            InvoiceService.RecalculateTotals(document);
            TotalWithoutTaxText.Text = document.AmountWithoutTax.ToString("N2");
            TotalVatText.Text = document.VatTotal.ToString("N2");
            TotalSalesTaxText.Text = document.SalesTaxTotal.ToString("N2");
            TotalAmountText.Text = document.TotalAmount.ToString("N2");
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_isPosted)
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
                CounterpartyAccountCode = AccountBox.Text.Trim(),
                PaymentKind = PaymentKindCombo.Text?.Trim() ?? string.Empty,
                DeliveryKind = DeliveryKindCombo.Text?.Trim() ?? string.Empty,
                SupplyKind = SupplyKindCombo.Text?.Trim() ?? string.Empty,
                Basis = BasisBox.Text.Trim(),
                Lines = _lines.Select(line => new InvoiceLineRow
                {
                    Id = line.Id,
                    LineNumber = line.LineNumber,
                    Name = line.Name,
                    AccountCode = line.AccountCode,
                    AmountWithoutTax = line.AmountWithoutTax,
                    VatRate = line.VatRate,
                    VatAmount = line.VatAmount,
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
        }
    }
}
