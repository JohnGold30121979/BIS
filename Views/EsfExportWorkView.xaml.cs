using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class EsfExportWorkView : UserControl
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;
        private InvoiceService? _salesInvoiceService;
        private InvoiceEsfExchangeService? _exchangeService;
        private bool _isBulkSelectionUpdating;

        public ObservableCollection<EsfExportRow> Rows { get; } = new();

        public EsfExportWorkView(AppDbContext context)
        {
            InitializeComponent();
            _context = context;
            _metadataService = new MetadataService(context);
            ExportGrid.ItemsSource = Rows;

            EndDatePicker.SelectedDate = DateTime.Today;
            StartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            Loaded += async (_, _) => await LoadRowsAsync();
        }

        private async Task LoadRowsAsync()
        {
            try
            {
                StatusText.Text = "Загрузка данных...";
                SummaryText.Text = "Загрузка данных для XML ЭСФ...";
                Rows.Clear();

                var startDate = (StartDatePicker.SelectedDate ?? DateTime.MinValue).Date;
                var endDate = (EndDatePicker.SelectedDate ?? DateTime.MaxValue).Date;
                var onlyNotExported = OnlyNotExportedCheckBox.IsChecked == true;

                if (InvoicesSourceCheckBox.IsChecked == true)
                {
                    var document = await FindSalesInvoiceDocumentAsync();
                    if (document == null)
                    {
                        SummaryText.Text = "Документ выписки счет-фактур не найден в метаданных.";
                        StatusText.Text = "Нет документа для выгрузки ЭСФ";
                    }
                    else
                    {
                        _salesInvoiceService = new InvoiceService(_context);
                        _salesInvoiceService.Configure(document);
                        _exchangeService = new InvoiceEsfExchangeService(_context, _salesInvoiceService);
                        var invoiceModuleName = await ResolveDocumentModuleNameAsync(document);

                        var invoices = await _salesInvoiceService.GetInvoicesAsync();
                        foreach (var invoice in invoices
                                     .Where(item => item.DocDate.Date >= startDate && item.DocDate.Date <= endDate)
                                     .Where(item => !onlyNotExported || !item.IsEsfExported)
                                     .OrderBy(item => item.DocDate)
                                     .ThenBy(item => item.DocNumber))
                        {
                            AddRow(EsfExportRow.FromInvoice(invoice, invoiceModuleName));
                        }
                    }
                }

                if (PostingsSourceCheckBox.IsChecked == true)
                {
                    var postingService = new PostingService(_context);
                    var postings = await postingService.GetAllPostingsAsync(startDate, endDate);
                    foreach (var posting in postings
                                 .Where(item => !InvoiceDocumentTypes.IsSales(item.DocumentType) &&
                                                !InvoiceDocumentTypes.IsPurchase(item.DocumentType))
                                 .OrderBy(item => item.Date)
                                 .ThenBy(item => item.DocumentNumber))
                    {
                        AddRow(EsfExportRow.FromPosting(posting));
                    }
                }

                UpdateSummary();
                StatusText.Text = "Готово";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки";
                MessageBox.Show($"Ошибка загрузки выгрузки ЭСФ: {ex.Message}", "Выгрузка ЭСФ",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<MetadataObject?> FindSalesInvoiceDocumentAsync()
        {
            var document = await _context.MetadataObjects
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Document" &&
                    (item.TableName == "doc_sales_invoice" ||
                     item.Name == InvoiceDocumentTypes.SalesIssue));

            if (document != null)
                return document;

            var allObjects = await _metadataService.GetAllMetadataObjectsAsync();
            return allObjects.FirstOrDefault(item =>
                item.ObjectType == "Document" &&
                (item.TableName == "doc_sales_invoice" ||
                 item.Name == InvoiceDocumentTypes.SalesIssue));
        }

        private async Task<string> ResolveDocumentModuleNameAsync(MetadataObject document)
        {
            var assignedModuleName = await _metadataService.GetAssignedModuleNameAsync(document.Id, document.ObjectType);
            return string.IsNullOrWhiteSpace(assignedModuleName)
                ? "Финансы"
                : assignedModuleName.Trim();
        }

        private void AddRow(EsfExportRow row)
        {
            row.IsSelected = AllRowsCheckBox.IsChecked == true;
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadRowsAsync();

        private async void OnSourceChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
                await LoadRowsAsync();
        }

        private void OnAllRowsChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _isBulkSelectionUpdating)
                return;

            _isBulkSelectionUpdating = true;
            var isSelected = AllRowsCheckBox.IsChecked == true;
            foreach (var row in Rows)
                row.IsSelected = isSelected;
            _isBulkSelectionUpdating = false;
            UpdateSummary();
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(EsfExportRow.IsSelected) || _isBulkSelectionUpdating)
                return;

            _isBulkSelectionUpdating = true;
            AllRowsCheckBox.IsChecked = Rows.Count > 0 && Rows.All(row => row.IsSelected);
            _isBulkSelectionUpdating = false;
            UpdateSummary();
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (_exchangeService == null)
            {
                MessageBox.Show("Сервис выгрузки ЭСФ не готов. Обновите список.", "Выгрузка ЭСФ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedRows = Rows.Where(row => row.IsSelected).ToList();
            if (selectedRows.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы одну счет-фактуру для выгрузки.", "Выгрузка ЭСФ",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var unsupportedRows = selectedRows.Where(row => !row.IsExportable).ToList();
            if (unsupportedRows.Count > 0)
            {
                MessageBox.Show(
                    "XML ЭСФ сейчас формируется только по счет-фактурам. Снимите выбор с других проводок или оставьте источник 'Счет-фактуры'.",
                    "Выгрузка ЭСФ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var defaultFileName = selectedRows.Count == 1
                ? $"esf_{selectedRows[0].DocNumber}_{selectedRows[0].DocDate:yyyyMMdd}.xml"
                : $"esf_selected_{DateTime.Today:yyyyMMdd}.xml";

            var saveDialog = new SaveFileDialog
            {
                Filter = "XML файлы (*.xml)|*.xml",
                FileName = defaultFileName,
                AddExtension = true,
                DefaultExt = ".xml"
            };

            if (saveDialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            try
            {
                StatusText.Text = "Формирование XML ЭСФ...";
                var result = await _exchangeService.ExportSelectedInvoicesAsync(
                    selectedRows.Select(row => row.InvoiceId!.Value).ToArray(),
                    saveDialog.FileName);

                await LoadRowsAsync();
                StatusText.Text = $"XML выгружен: {result.ExportedCount}";
                MessageBox.Show(
                    $"XML выгружен успешно.\nДокументов: {result.ExportedCount}\nФайл: {result.OutputPath}",
                    "Выгрузка ЭСФ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка выгрузки ЭСФ";
                MessageBox.Show(ex.Message, "Выгрузка ЭСФ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            var selectedCount = Rows.Count(row => row.IsSelected);
            var invoiceCount = Rows.Count(row => row.IsExportable);
            var postingCount = Rows.Count(row => !row.IsExportable);
            var totalAmount = Rows.Sum(row => row.TotalAmount);
            var selectedAmount = Rows.Where(row => row.IsSelected).Sum(row => row.TotalAmount);
            SummaryText.Text = $"Счет-фактур: {invoiceCount}; других проводок: {postingCount}; выбрано: {selectedCount}; сумма списка: {totalAmount:N2}; сумма выбранных: {selectedAmount:N2}";
        }
    }

    public sealed class EsfExportRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid? InvoiceId { get; set; }
        public bool IsExportable { get; set; }
        public string SourceName { get; set; } = "Счет-фактура";
        public string DocNumber { get; set; } = string.Empty;
        public DateTime DocDate { get; set; }
        public string ModuleCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string TaxStatusDisplay { get; set; } = string.Empty;
        public string EsfNumber { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string Basis { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public static EsfExportRow FromInvoice(InvoiceListRow invoice, string fallbackModuleName) => new()
        {
            InvoiceId = invoice.Id,
            IsExportable = true,
            SourceName = "Счет-фактура",
            DocNumber = invoice.DocNumber,
            DocDate = invoice.DocDate,
            ModuleCode = ResolveModuleDisplay(invoice.ModuleCode, fallbackModuleName),
            TotalAmount = invoice.TotalAmount,
            TaxStatusDisplay = invoice.TaxStatusDisplay,
            EsfNumber = invoice.EsfNumber,
            OrganizationName = invoice.OrganizationName,
            Basis = invoice.Basis
        };

        public static EsfExportRow FromPosting(PostingViewModel posting) => new()
        {
            IsExportable = false,
            SourceName = "Проводка",
            DocNumber = posting.DocumentNumber,
            DocDate = posting.Date,
            ModuleCode = ResolveModuleDisplay(posting.ModuleCode, posting.ModuleName),
            TotalAmount = posting.Amount,
            TaxStatusDisplay = "Не выгружается",
            OrganizationName = posting.Organization,
            Basis = string.IsNullOrWhiteSpace(posting.Note) ? posting.DocumentType : posting.Note
        };

        private static string ResolveModuleDisplay(string? moduleValue, string? fallbackModuleName)
        {
            var value = string.IsNullOrWhiteSpace(moduleValue)
                ? fallbackModuleName
                : moduleValue;

            if (string.IsNullOrWhiteSpace(value))
                return "Финансы";

            value = value.Trim();
            return value.Equals(ModuleMetadataService.FinanceCode, StringComparison.OrdinalIgnoreCase)
                ? "Финансы"
                : value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}