using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.ObjectModel;
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
        private const string CashOrderReceiptKind = "Receipt";
        private const string CashOrderPaymentKind = "Payment";
        private const string CashOrderReceiptDocumentType = "Приходный кассовый ордер";
        private const string CashOrderPaymentDocumentType = "Расходный кассовый ордер";

        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;
        private string _selectedCorrAccountCode = string.Empty;
        private Guid _selectedCashDeskId;
        private string _selectedCashDeskCode = string.Empty;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private List<CashDeskItem> _cashDesks = new();
        private MetadataObject? _cashDeskCatalog;
        private readonly ObservableCollection<CashPostingPreviewRow> _postingPreviewRows = new();
        private string _orderKind = CashOrderPaymentKind;

        // Для сотрудника
        private Guid _selectedEmployeeId = Guid.Empty;
        private string _selectedEmployeeName = string.Empty;

        public CashOrderDialog(MetadataObject document, MetadataService metadataService)
            : this(document, metadataService, CashOrderPaymentKind)
        {
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, string orderKind)
        {
            InitializeComponent();
            PostingsPreviewGrid.ItemsSource = _postingPreviewRows;
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            _orderKind = NormalizeOrderKind(orderKind, document.Name);
            DialogTitle.Text = $"Добавление: {GetOrderKindDisplay(_orderKind)} КО";
            DatePicker.SelectedDate = DateTime.Today;

            ContentRendered += async (s, e) => await InitializeAsync();
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            PostingsPreviewGrid.ItemsSource = _postingPreviewRows;
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = "Редактирование: кассовый ордер";

            ContentRendered += async (s, e) => await InitializeAsync(editId);
        }

        private async Task InitializeAsync(Guid? editId = null)
        {
            if (_isDataLoaded || _isLoading) return;
            _isLoading = true;

            try
            {
                this.Cursor = Cursors.Wait;

                var data = await Task.Run(async () => await LoadAllDataAsync());

                await Dispatcher.InvokeAsync(() =>
                {
                    // Заполняем ComboBox (кроме сотрудника)
                    _cashDeskCatalog = data.CashDeskCatalog;
                    if (data.CashDesks != null)
                    {
                        _cashDesks = data.CashDesks;
                        ReferenceComboBoxSearchHelper.Attach(CashDeskCombo, _cashDesks);
                    }
                    if (data.Organizations != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(OrganizationCombo, data.Organizations);
                        if (data.OrganizationCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                OrganizationCombo,
                                _metadataService,
                                data.OrganizationCatalog,
                                this,
                                items => data.Organizations = items,
                                "Код организации",
                                "Наименование");
                        }
                    }
                    if (data.Currencies != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(CurrencyCombo, data.Currencies);
                        if (data.CurrencyCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                CurrencyCombo,
                                _metadataService,
                                data.CurrencyCatalog,
                                this,
                                items => data.Currencies = items,
                                "Код",
                                "Наименование");
                        }
                    }
                    if (data.Materials != null)
                    {
                        ReferenceComboBoxSearchHelper.Attach(MaterialCombo, data.Materials);
                        if (data.MaterialCatalog != null)
                        {
                            ReferencePickerControlFactory.AttachEditor(
                                MaterialCombo,
                                _metadataService,
                                data.MaterialCatalog,
                                this,
                                items => data.Materials = items,
                                "Код",
                                "Наименование материала");
                        }
                    }

                    _accountAnalytics = data.AccountAnalytics;


                    // Генерируем номер
                    if (!editId.HasValue)
                    {
                        NumberBox.Text = data.DocumentNumber;

                        if (data.CashDesks != null && data.CashDesks.Any())
                        {
                            CashDeskCombo.SelectedItem = data.CashDesks.First();
                            _selectedCashDeskId = data.CashDesks.First().Id;
                            _selectedCashDeskCode = data.CashDesks.First().AccountCode;
                            CashDeskAccountBox.Text = _selectedCashDeskCode;
                            RefreshPostingPreview();
                        }
                    }
                    else if (data.Record != null)
                    {
                        _orderKind = ResolveOrderKind(data.Record, _document.Name);
                        DialogTitle.Text = $"Редактирование: {GetOrderKindDisplay(_orderKind)} КО";
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

                        // Загружаем кассу
                        if (data.Record.TryGetValue("Касса", out var cashValue))
                            SelectComboByRecordValue(CashDeskCombo, data.Record, "Касса");

                        // Загружаем корреспондирующий счет
                        if (data.Record.TryGetValue("Корр. счет", out var accountValue))
                            ApplySelectedCorrAccount(accountValue);

                        // Загружаем организацию
                        SelectComboByRecordValue(OrganizationCombo, data.Record, "Организация");

                        // Загружаем валюту
                        SelectComboByRecordValue(CurrencyCombo, data.Record, "Валюта");

                        // Загружаем сотрудника (через отдельный метод)
                        if (data.Record.TryGetValue("Сотрудник", out var employeeValue) && Guid.TryParse(employeeValue?.ToString(), out var empId))
                        {
                            _selectedEmployeeId = empId;
                            var emp = data.Employees?.FirstOrDefault(e => e.Id == empId);
                            EmployeeNameBox.Text = emp != null ? emp.DisplayName : employeeValue.ToString();
                            _selectedEmployeeName = emp?.DisplayName ?? employeeValue.ToString();
                        }

                        // Загружаем материал
                        SelectComboByRecordValue(MaterialCombo, data.Record, "Материал");
                    }

                    UpdateAccountControlledFieldsVisibility();
                    AmountBox.Focus();
                    AmountBox.SelectAll();
                });

                _isDataLoaded = true;
            }
            catch (Exception ex)
            {
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
            var result = new DialogData();
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            result.AccountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);

            // Кассы
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "Кассы");
            result.CashDeskCatalog = cashDesks;
            if (cashDesks != null)
                result.CashDesks = await LoadCashDeskItemsAsync(cashDesks, result.AccountAnalytics);

            // Организации
            var orgs = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
            result.OrganizationCatalog = orgs;
            if (orgs != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgs.Id);
                result.Organizations = data
                    .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                    .Select(row => CreateReferenceItem(row, "Код", "Наименование"))
                    .ToList();
            }

            // Валюты
            result.CurrencyCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Справочник валют");
            result.Currencies = await LoadReferenceItemsAsync(allCatalogs, "Справочник валют", "Код", "Наименование");
            // Сотрудники (для диалога выбора)
            result.EmployeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");
            result.Employees = await LoadReferenceItemsAsync(allCatalogs, "Сотрудники (Списочный состав)", "Табельный номер", "ФИО");
            // Материалы
            result.MaterialCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Справочник материалов");
            result.Materials = await LoadReferenceItemsAsync(allCatalogs, "Справочник материалов", "Код", "Наименование материала");

            // Для ПКО и РКО номера считаются раздельно, хотя записи хранятся в общей таблице.
            if (!_editId.HasValue)
            {
                try
                {
                    result.DocumentNumber = await _metadataService.GetNextCashOrderDocumentNumberAsync(_orderKind);
                }
                catch
                {
                    result.DocumentNumber = MetadataService.GenerateFallbackDocumentNumber();
                }
            }

            // Если редактирование, загружаем запись
            if (_editId.HasValue)
            {
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                result.Record = data.FirstOrDefault(r => r["Id"].ToString() == _editId.Value.ToString());
            }

            return result;
        }

        private class DialogData
        {
            public List<CashDeskItem> CashDesks { get; set; } = new();
            public List<ReferenceItem> Organizations { get; set; } = new();
            public List<ReferenceItem> Currencies { get; set; } = new();
            public List<ReferenceItem> Employees { get; set; } = new();
            public List<ReferenceItem> Materials { get; set; } = new();
            public MetadataObject? CashDeskCatalog { get; set; }
            public MetadataObject? OrganizationCatalog { get; set; }
            public MetadataObject? CurrencyCatalog { get; set; }
            public MetadataObject? EmployeeCatalog { get; set; }
            public MetadataObject? MaterialCatalog { get; set; }
            public AccountAnalyticsRegistry AccountAnalytics { get; set; } = new();
            public string DocumentNumber { get; set; } = string.Empty;
            public Dictionary<string, object>? Record { get; set; }
        }

        private async Task<List<CashDeskItem>> LoadCashDeskItemsAsync(
            MetadataObject cashDeskCatalog,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
            return rows
                .Where(row => row.TryGetValue("Id", out var id) && Guid.TryParse(id?.ToString(), out _))
                .Select(row => CreateCashDeskItem(row, accountAnalytics))
                .ToList();
        }

        private static CashDeskItem CreateCashDeskItem(
            Dictionary<string, object> row,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountCode = ResolveCashDeskAccountCode(
                GetRowString(row, "Счет", "Счет кассы", "Код", "code"),
                accountAnalytics);

            var item = new CashDeskItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = GetRowString(
                    row,
                    "Наименование кассы",
                    "Наименование",
                    "name",
                    "Код") ?? "Касса",
                AccountCode = accountCode,
                CashNumber = GetRowString(row, "Номер кассы", "cash_number") ?? string.Empty,
                CurrencyName = GetRowString(row, "Валюта", "currency_id") ?? string.Empty
            };

            foreach (var value in row.Values)
            {
                var text = NormalizeReferenceLookupKey(value?.ToString());
                if (!string.IsNullOrWhiteSpace(text))
                    item.LookupKeys.Add(text);
            }

            if (!string.IsNullOrWhiteSpace(item.DisplayNameWithAccount))
                item.LookupKeys.Add(item.DisplayNameWithAccount);

            return item;
        }

        private async Task<MetadataObject?> GetCashDeskCatalogAsync()
        {
            if (_cashDeskCatalog != null)
                return _cashDeskCatalog;

            var allCatalogs = await _metadataService.GetCatalogsAsync();
            _cashDeskCatalog = allCatalogs.FirstOrDefault(catalog => catalog.Name == "Кассы");

            if (_cashDeskCatalog == null)
            {
                MessageBox.Show("Справочник касс не найден.", "Кассы",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return _cashDeskCatalog;
        }

        private async Task ReloadCashDesksAsync(Guid? selectedId = null)
        {
            var cashDeskCatalog = await GetCashDeskCatalogAsync();
            if (cashDeskCatalog == null)
                return;

            _accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
            _cashDesks = await LoadCashDeskItemsAsync(cashDeskCatalog, _accountAnalytics);
            ReferenceComboBoxSearchHelper.Attach(CashDeskCombo, _cashDesks);

            if (selectedId.HasValue)
            {
                CashDeskCombo.SelectedItem = _cashDesks.FirstOrDefault(item => item.Id == selectedId.Value);
            }
            else if (_selectedCashDeskId != Guid.Empty)
            {
                CashDeskCombo.SelectedItem = _cashDesks.FirstOrDefault(item => item.Id == _selectedCashDeskId);
            }

            if (CashDeskCombo.SelectedItem is not CashDeskItem)
            {
                _selectedCashDeskId = Guid.Empty;
                _selectedCashDeskCode = string.Empty;
                CashDeskAccountBox.Text = string.Empty;
                RefreshPostingPreview();
            }
        }

        private async Task ApplyCashDeskByIdAsync(Guid cashDeskId)
        {
            await ReloadCashDesksAsync(cashDeskId);
            if (CashDeskCombo.SelectedItem is not CashDeskItem)
            {
                MessageBox.Show("Касса сохранена, но не найдена после обновления списка.", "Кассы",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void SelectCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
                if (rows.Count == 0)
                {
                    MessageBox.Show("В справочнике касс нет данных. Добавьте кассу кнопкой '+'.", "Кассы",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(rows, "Наименование кассы", "Счет")
                {
                    Owner = this,
                    Title = "Выбор: Кассы"
                };

                if (dialog.ShowDialog() == true &&
                    dialog.SelectedItem != null &&
                    dialog.SelectedItem.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var selectedId))
                {
                    await ApplyCashDeskByIdAsync(selectedId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе кассы: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void AddCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                var createdId = await _metadataService.CreateDynamicRecordAsync(cashDeskCatalog.Id, dialog.ItemData);
                await ApplyCashDeskByIdAsync(createdId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении кассы: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void EditCashDesk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CashDeskCombo.SelectedItem is not CashDeskItem selected)
                {
                    MessageBox.Show("Сначала выберите кассу.", "Кассы",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Cursor = Cursors.Wait;
                var cashDeskCatalog = await GetCashDeskCatalogAsync();
                if (cashDeskCatalog == null)
                    return;

                var rows = await _metadataService.GetCatalogDataAsync(cashDeskCatalog.Id);
                var cashDesk = rows.FirstOrDefault(row =>
                    row.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var id) &&
                    id == selected.Id);

                if (cashDesk == null)
                {
                    MessageBox.Show("Выбранная касса не найдена в справочнике.", "Кассы",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService, cashDesk)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _metadataService.UpdateDynamicRecordAsync(cashDeskCatalog.Id, selected.Id, dialog.ItemData);
                await ApplyCashDeskByIdAsync(selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при редактировании кассы: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }
        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("Для этого модуля нет доступных счетов в плане счетов.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                    _selectedCorrAccountCode = accountCode;

                    UpdateAccountControlledFieldsVisibility();
                    RefreshPostingPreview();
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

                var amount = decimal.TryParse(AmountBox.Text, out var parsedAmount) ? parsedAmount : 0;
                if (amount <= 0)
                {
                    MessageBox.Show("Сумма кассового ордера должна быть больше нуля.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    AmountBox.Focus();
                    return;
                }

                // ПРОВЕРКА ЗАПОЛНЕНИЯ ВСЕХ АКТИВНЫХ ПОЛЕЙ
                if (OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите организацию!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    OrganizationCombo.Focus();
                    return;
                }

                if (EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId == Guid.Empty)
                {
                    MessageBox.Show("Выберите сотрудника!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите валюту!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CurrencyCombo.Focus();
                    return;
                }

                if (MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem == null)
                {
                    MessageBox.Show("Выберите материал!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    MaterialCombo.Focus();
                    return;
                }

                // Получаем кассу
                string cashDeskId = string.Empty;
                string cashDeskCode = _selectedCashDeskCode;

                if (CashDeskCombo.SelectedItem is CashDeskItem cashDesk)
                {
                    cashDeskId = cashDesk.Id.ToString();
                    cashDeskCode = cashDesk.AccountCode;
                    _selectedCashDeskId = cashDesk.Id;
                    _selectedCashDeskCode = cashDesk.AccountCode;
                }
                else
                {
                    MessageBox.Show("Выберите кассу.", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(cashDeskCode))
                {
                    MessageBox.Show("У выбранной кассы не указан счет. Откройте справочник касс и заполните поле \"Счет\".", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    CashDeskCombo.Focus();
                    return;
                }

                // Получаем корреспондирующий счет
                string corrAccountId = _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId.ToString() : string.Empty;
                string corrAccountCode = _selectedCorrAccountCode;

                if (string.IsNullOrEmpty(corrAccountCode))
                {
                    MessageBox.Show("Выберите корреспондирующий счет (кнопка '?').", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Определяем дебет и кредит
                var (debitAccount, creditAccount) = BuildPostingAccounts(cashDeskCode, corrAccountCode);

                if (debitAccount == creditAccount)
                {
                    MessageBox.Show("Дебет и кредит не могут быть одинаковыми!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                // Валюта
                bool isCurrencyEnabled = IsCurrencyEnabledForAccount(corrAccountCode);
                decimal amountInCurrency = isCurrencyEnabled ? amount : 0;

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = DatePicker.SelectedDate ?? DateTime.Today,
                    ["Тип КО"] = _orderKind,
                    ["Сумма"] = amount,
                    ["Основание"] = BasisBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = false,
                    ["Касса"] = cashDeskId,
                    ["Корр. счет"] = corrAccountId,
                    ["Дебет"] = debitAccount,
                    ["Кредит"] = creditAccount,
                    ["Сумма в валюте"] = amountInCurrency
                };

                // Заполняем остальные поля (только если они видимы, иначе не сохраняем)
                SetFieldValueIfExists(itemData, "Организация",
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem org
                        ? org.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Валюта",
                    CurrencyPanel.Visibility == Visibility.Visible && CurrencyCombo.SelectedItem is ReferenceItem currency
                        ? currency.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Сотрудник",
                    EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId != Guid.Empty
                        ? _selectedEmployeeId.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Материал",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Контрагент", string.Empty);

                // Сохраняем документ.
                if (_editId.HasValue)
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                else
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private bool IsCurrencyEnabledForAccount(string accountCode)
        {
            if (string.IsNullOrEmpty(accountCode))
                return false;

            try
            {
                var settings = _accountAnalytics.GetSettingsByCode(accountCode);
                if (settings == null)
                    return false;

                var currencyDefinition = _accountAnalytics.Definitions
                    .FirstOrDefault(d => d.Code == "currencies");
                if (currencyDefinition == null)
                    return false;

                return settings.Allows(currencyDefinition);
            }
            catch
            {
                return false;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AllowNumberEditCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var canEdit = AllowNumberEditCheckBox.IsChecked == true;
            NumberBox.IsReadOnly = !canEdit;
            NumberBox.Background = canEdit ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.LightGray;
            if (canEdit)
            {
                NumberBox.Focus();
                NumberBox.SelectAll();
            }
        }

        private void ApplySelectedCorrAccount(object accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return;

            _selectedCorrAccountId = account.Id;
            _selectedCorrAccountCode = account.Code;
            CorrAccountBox.Text = account.DisplayName;
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var settings = _selectedCorrAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedCorrAccountId);

            SetAccountControlledFieldVisibility(
                OrganizationLabel,
                OrganizationCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Организация",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Организации",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            SetAccountControlledPanelVisibility(
                CurrencyPanel,
                CurrencyCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Валюта",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Справочник валют",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));

            // Для сотрудника – просто показываем/скрываем панель, ComboBox нет
            EmployeePanel.Visibility = AccountAnalyticsRules.ShouldShowField(
                "Сотрудник",
                new[] { settings },
                _accountAnalytics.Definitions,
                "Сотрудники (Списочный состав)",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false)
                ? Visibility.Visible : Visibility.Collapsed;

            SetAccountControlledPanelVisibility(
                MaterialPanel,
                MaterialCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Материал",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Справочник материалов",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false));
        }

        private void SetFieldValueIfExists(Dictionary<string, object> itemData, string fieldName, object value)
        {
            if (_document.Fields.Any(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
                itemData[fieldName] = value;
        }

        private async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            List<MetadataObject> catalogs,
            string catalogName,
            string firstDisplayField,
            string secondDisplayField)
        {
            var catalog = catalogs.FirstOrDefault(c => c.Name == catalogName);
            if (catalog == null)
                return new List<ReferenceItem>();

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => row.ContainsKey("Id") && Guid.TryParse(row["Id"]?.ToString(), out _))
                .Select(row => CreateReferenceItem(row, firstDisplayField, secondDisplayField))
                .ToList();
        }

        private static ReferenceItem CreateReferenceItem(
            Dictionary<string, object> row,
            string firstDisplayField,
            string secondDisplayField)
        {
            var item = new ReferenceItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = BuildDisplayName(row, firstDisplayField, secondDisplayField)
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

        private static string BuildDisplayName(Dictionary<string, object> row, string firstField, string secondField)
        {
            var first = row.GetValueOrDefault(firstField)?.ToString() ??
                        row.GetValueOrDefault(firstField.Replace(" ", "_"))?.ToString() ??
                        string.Empty;
            var second = row.GetValueOrDefault(secondField)?.ToString() ??
                         row.GetValueOrDefault(secondField.Replace(" ", "_"))?.ToString() ??
                         string.Empty;

            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return $"{first} - {second}";

            return first.Length > 0
                ? first
                : second.Length > 0
                    ? second
                    : row.GetValueOrDefault("Наименование")?.ToString() ??
                      row.GetValueOrDefault("name")?.ToString() ??
                      row.GetValueOrDefault("Id")?.ToString() ??
                      string.Empty;
        }

        private static string NormalizeReferenceLookupKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var separatorIndex = normalized.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? normalized[..separatorIndex].Trim() : normalized;
        }

        private static string? GetRowString(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return null;
        }

        public static string ResolveCashDeskAccountCode(
            string? accountCode,
            AccountAnalyticsRegistry accountAnalytics)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return string.Empty;

            var account = accountAnalytics.FindAccount(accountCode);
            return account?.Code ?? NormalizeAccountCodeText(accountCode);
        }

        private static string NormalizeAccountCodeText(string accountCode)
        {
            var text = accountCode.Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        private static void SelectComboByRecordValue(ComboBox comboBox, Dictionary<string, object> record, string fieldName)
        {
            if (!record.TryGetValue(fieldName, out var value) || !Guid.TryParse(value?.ToString(), out var id))
                return;

            comboBox.SelectedItem = comboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == id);
        }

        private static void SetAccountControlledFieldVisibility(
            FrameworkElement label,
            ComboBox comboBox,
            bool isVisible)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            comboBox.Visibility = visibility;

            if (!isVisible)
                comboBox.SelectedItem = null;
        }

        private static void SetAccountControlledPanelVisibility(
            FrameworkElement panel,
            ComboBox comboBox,
            bool isVisible)
        {
            panel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible && comboBox != null)
                comboBox.SelectedItem = null;
        }

        private async void SelectEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");

                if (employeeCatalog == null)
                {
                    MessageBox.Show("Справочник сотрудников не найден!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);

                if (employeesData == null || employeesData.Count == 0)
                {
                    MessageBox.Show("В справочнике сотрудников нет данных!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(employeesData, "Табельный номер", "ФИО");
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                {
                    var employee = dialog.SelectedItem;
                    var displayName = $"{employee.GetValueOrDefault("Табельный номер")} - {employee.GetValueOrDefault("ФИО")}";

                    EmployeeNameBox.Text = displayName;
                    _selectedEmployeeId = Guid.Parse(employee["Id"].ToString());
                    _selectedEmployeeName = employee.GetValueOrDefault("ФИО")?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе сотрудника: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void AddEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.Cursor = Cursors.Wait;

                var employeeCatalog = await GetEmployeeCatalogAsync();
                if (employeeCatalog == null)
                    return;

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                var createdId = await _metadataService.CreateDynamicRecordAsync(employeeCatalog.Id, dialog.ItemData);
                await ApplyEmployeeByIdAsync(employeeCatalog, createdId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении сотрудника: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async void EditEmployee_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedEmployeeId == Guid.Empty)
                {
                    MessageBox.Show("Сначала выберите сотрудника.", "Сотрудники",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                this.Cursor = Cursors.Wait;

                var employeeCatalog = await GetEmployeeCatalogAsync();
                if (employeeCatalog == null)
                    return;

                var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
                var employee = employeesData.FirstOrDefault(row =>
                    row.TryGetValue("Id", out var value) &&
                    Guid.TryParse(value?.ToString(), out var id) &&
                    id == _selectedEmployeeId);

                if (employee == null)
                {
                    MessageBox.Show("Выбранный сотрудник не найден в справочнике.", "Сотрудники",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService, employee)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true)
                    return;

                await _metadataService.UpdateDynamicRecordAsync(employeeCatalog.Id, _selectedEmployeeId, dialog.ItemData);
                await ApplyEmployeeByIdAsync(employeeCatalog, _selectedEmployeeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при редактировании сотрудника: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Cursor = null;
            }
        }

        private async Task<MetadataObject?> GetEmployeeCatalogAsync()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();
            var employeeCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Сотрудники (Списочный состав)");

            if (employeeCatalog == null)
            {
                MessageBox.Show("Справочник сотрудников не найден!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return employeeCatalog;
        }

        private async Task ApplyEmployeeByIdAsync(MetadataObject employeeCatalog, Guid employeeId)
        {
            var employeesData = await _metadataService.GetCatalogDataAsync(employeeCatalog.Id);
            var employee = employeesData.FirstOrDefault(row =>
                row.TryGetValue("Id", out var value) &&
                Guid.TryParse(value?.ToString(), out var id) &&
                id == employeeId);

            if (employee == null)
                return;

            var displayName = BuildEmployeeDisplayName(employee);
            EmployeeNameBox.Text = displayName;
            _selectedEmployeeId = employeeId;
            _selectedEmployeeName = employee.GetValueOrDefault("ФИО")?.ToString()
                                    ?? employee.GetValueOrDefault("full_name")?.ToString()
                                    ?? displayName;
        }

        private static string BuildEmployeeDisplayName(Dictionary<string, object> employee)
        {
            var personnelNumber = employee.GetValueOrDefault("Табельный номер")?.ToString()
                                  ?? employee.GetValueOrDefault("personnel_number")?.ToString()
                                  ?? string.Empty;
            var fullName = employee.GetValueOrDefault("ФИО")?.ToString()
                           ?? employee.GetValueOrDefault("full_name")?.ToString()
                           ?? employee.GetValueOrDefault("Наименование")?.ToString()
                           ?? employee.GetValueOrDefault("name")?.ToString()
                           ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(personnelNumber) && !string.IsNullOrWhiteSpace(fullName))
                return $"{personnelNumber} - {fullName}";

            return !string.IsNullOrWhiteSpace(fullName)
                ? fullName
                : personnelNumber;
        }
        private void CashDeskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CashDeskCombo.SelectedItem is CashDeskItem selected)
            {
                _selectedCashDeskId = selected.Id;
                _selectedCashDeskCode = selected.AccountCode;
                CashDeskAccountBox.Text = _selectedCashDeskCode;
                RefreshPostingPreview();
            }
        }
        private static bool IsReceiptOrder(string orderKind)
            => orderKind.Equals(CashOrderReceiptKind, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeOrderKind(string? value, string documentName)
        {
            var rawKind = value ?? string.Empty;
            if (rawKind.Contains("приход", StringComparison.OrdinalIgnoreCase) ||
                rawKind.Equals(CashOrderReceiptKind, StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals(CashOrderReceiptDocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return CashOrderReceiptKind;
            }

            if (rawKind.Contains("расход", StringComparison.OrdinalIgnoreCase) ||
                rawKind.Equals(CashOrderPaymentKind, StringComparison.OrdinalIgnoreCase) ||
                documentName.Equals(CashOrderPaymentDocumentType, StringComparison.OrdinalIgnoreCase))
            {
                return CashOrderPaymentKind;
            }

            return CashOrderPaymentKind;
        }

        private static string ResolveOrderKind(Dictionary<string, object> record, string documentName)
        {
            return NormalizeOrderKind(
                GetRowString(record, "Тип КО", "order_kind", "cash_order_kind", "Тип", "document_type"),
                documentName);
        }

        private static string GetOrderKindDisplay(string orderKind)
            => IsReceiptOrder(orderKind) ? "Приходный" : "Расходный";

        private (string DebitAccount, string CreditAccount) BuildPostingAccounts(string cashDeskCode, string corrAccountCode)
            => IsReceiptOrder(_orderKind)
                ? (cashDeskCode, corrAccountCode)
                : (corrAccountCode, cashDeskCode);

        private decimal TryReadAmount()
            => decimal.TryParse(AmountBox?.Text, out var parsedAmount) ? parsedAmount : 0m;

        private void OnPostingPreviewChanged(object sender, EventArgs e)
        {
            RefreshPostingPreview();
        }

        private void RefreshPostingPreview()
        {
            if (PostingsPreviewGrid == null || PostingPreviewHint == null)
                return;

            _postingPreviewRows.Clear();
            var amount = TryReadAmount();
            var (debitAccount, creditAccount) = BuildPostingAccounts(_selectedCashDeskCode, _selectedCorrAccountCode);
            var isReceipt = IsReceiptOrder(_orderKind);

            _postingPreviewRows.Add(new CashPostingPreviewRow
            {
                Mark = isReceipt ? "Приход" : "Расход",
                Debit = string.IsNullOrWhiteSpace(debitAccount) ? "не выбран" : debitAccount,
                Credit = string.IsNullOrWhiteSpace(creditAccount) ? "не выбран" : creditAccount,
                Amount = amount.ToString("N2"),
                Note = string.IsNullOrWhiteSpace(BasisBox?.Text) ? DescriptionBox?.Text ?? string.Empty : BasisBox.Text
            });

            PostingPreviewHint.Text = isReceipt
                ? "Приходный ордер: дебетуется счет кассы, кредитуется корреспондирующий счет."
                : "Расходный ордер: дебетуется корреспондирующий счет, кредитуется счет кассы.";
        }
    }

    public class CashPostingPreviewRow
    {
        public string Mark { get; set; } = string.Empty;
        public string Debit { get; set; } = string.Empty;
        public string Credit { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
    public class CashDeskItem : ReferenceItem
    {
        public string AccountCode { get; set; } = string.Empty;
        public string CashNumber { get; set; } = string.Empty;
        public string CurrencyName { get; set; } = string.Empty;
        public string DisplayNameWithAccount =>
            string.IsNullOrWhiteSpace(AccountCode)
                ? DisplayName
                : $"{DisplayName} (счет {AccountCode})";
    }
}





