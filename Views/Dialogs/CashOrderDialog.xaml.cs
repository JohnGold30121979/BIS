using BIS.ERP.Models;
using BIS.ERP.Services;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
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
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private bool _isDataLoaded = false;
        private bool _isLoading = false;

        public CashOrderDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            DialogTitle.Text = $"Добавление: {document.Name}";
            DatePicker.SelectedDate = DateTime.Today;

            this.ContentRendered += async (s, e) => await InitializeAsync();
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = $"Редактирование: {document.Name}";

            this.ContentRendered += async (s, e) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isDataLoaded || _isLoading) return;
            _isLoading = true;

            try
            {
                System.Diagnostics.Debug.WriteLine("=== InitializeAsync START ===");
                this.Cursor = Cursors.Wait;             

                // Загружаем данные в фоновом потоке
                var data = await Task.Run(async () => await LoadAllDataAsync());

                System.Diagnostics.Debug.WriteLine("2. LoadReferenceDataAsync завершён");

                // Обновляем UI в основном потоке
                await Dispatcher.InvokeAsync(() =>
                {
                    System.Diagnostics.Debug.WriteLine("3. Обновление UI...");

                    // Заполняем ComboBox
                    if (data.CashDesks != null)
                        CashDeskCombo.ItemsSource = data.CashDesks;
                    if (data.Organizations != null)
                        OrganizationCombo.ItemsSource = data.Organizations;
                    if (data.Contractors != null)
                        ContractorCombo.ItemsSource = data.Contractors;

                    // Генерируем номер
                    if (!editId.HasValue)
                    {
                        NumberBox.Text = data.DocumentNumber;
                    }
                    else if (data.Record != null)
                    {
                        // Заполняем данные для редактирования
                        var rawNumber = data.Record.ContainsKey("Номер") ? data.Record["Номер"]?.ToString() :
                                       (data.Record.ContainsKey("doc_number") ? data.Record["doc_number"]?.ToString() : "");
                        NumberBox.Text = MetadataService.NormalizeLegacyDocumentNumber(rawNumber);

                        if (data.Record.ContainsKey("Дата") && data.Record["Дата"] is DateTime dt)
                            DatePicker.SelectedDate = dt;
                        if (data.Record.ContainsKey("Сумма"))
                            AmountBox.Text = data.Record["Сумма"].ToString();
                        if (data.Record.ContainsKey("Основание"))
                            BasisBox.Text = data.Record["Основание"].ToString();
                        if (data.Record.ContainsKey("Примечание"))
                            DescriptionBox.Text = data.Record["Примечание"].ToString();
                    }

                    System.Diagnostics.Debug.WriteLine("4. UI обновлён");
                });

                _isDataLoaded = true;               
                System.Diagnostics.Debug.WriteLine("=== InitializeAsync COMPLETED ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"=== InitializeAsync ERROR: {ex.Message} ===");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");               
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
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
            System.Diagnostics.Debug.WriteLine("1. Начинаем LoadReferenceDataAsync...");

            var result = new DialogData();
            var allCatalogs = await _metadataService.GetCatalogsAsync();

            // Загружаем кассы
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "Кассы");
            if (cashDesks != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(cashDesks.Id);
                result.CashDesks = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                }).ToList();
            }

            // Загружаем организации
            var orgs = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
            if (orgs != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgs.Id);
                result.Organizations = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                }).ToList();
            }

            // Загружаем контрагентов
            var contractors = allCatalogs.FirstOrDefault(c => c.Name == "Контрагенты");
            if (contractors != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(contractors.Id);
                result.Contractors = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                }).ToList();
            }

            // Генерируем номер
            try
            {
                result.DocumentNumber = await _metadataService.GetNextDocumentNumberAsync(_document.Name);
            }
            catch
            {
                result.DocumentNumber = MetadataService.GenerateFallbackDocumentNumber();
            }

            // Если редактирование, загружаем запись
            if (_editId.HasValue)
            {
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                result.Record = data.FirstOrDefault(r => r["Id"].ToString() == _editId.Value.ToString());
            }

            System.Diagnostics.Debug.WriteLine("2. LoadReferenceDataAsync завершён");
            return result;
        }

        private class DialogData
        {
            public List<ReferenceItem> CashDesks { get; set; }
            public List<ReferenceItem> Organizations { get; set; }
            public List<ReferenceItem> Contractors { get; set; }
            public string DocumentNumber { get; set; }
            public Dictionary<string, object> Record { get; set; }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var chartCatalog = allCatalogs.FirstOrDefault(c => c.Name.StartsWith("План счетов"));

                if (chartCatalog == null)
                {
                    MessageBox.Show("План счетов не найден!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("В плане счетов нет данных!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new AccountSelectionDialog(accountsData);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    var accountCode = dialog.SelectedAccount.ContainsKey("Код") ? dialog.SelectedAccount["Код"].ToString() : "";
                    var accountName = dialog.SelectedAccount.ContainsKey("Наименование") ? dialog.SelectedAccount["Наименование"].ToString() : "";

                    CorrAccountBox.Text = $"{accountCode} - {accountName}";

                    if (dialog.SelectedAccount.ContainsKey("Id"))
                    {
                        _selectedCorrAccountId = Guid.Parse(dialog.SelectedAccount["Id"].ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе счета: {ex.Message}", "Ошибка",
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
                    MessageBox.Show("Номер документа должен содержать только цифры.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    NumberBox.Focus();
                    return;
                }

                NumberBox.Text = documentNumber;

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Сумма"] = decimal.TryParse(AmountBox.Text, out var amount) ? amount : 0,
                    ["Основание"] = BasisBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false
                };

                if (CashDeskCombo.SelectedItem is ReferenceItem cashDesk)
                    itemData["Касса"] = cashDesk.Id.ToString();
                if (OrganizationCombo.SelectedItem is ReferenceItem org)
                    itemData["Организация"] = org.Id.ToString();
                if (ContractorCombo.SelectedItem is ReferenceItem contractor)
                    itemData["Контрагент"] = contractor.Id.ToString();
                if (_selectedCorrAccountId != Guid.Empty)
                    itemData["Корр. счет"] = _selectedCorrAccountId.ToString();

                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
