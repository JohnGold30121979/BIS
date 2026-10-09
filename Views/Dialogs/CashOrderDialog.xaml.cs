using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

        private bool _isApplyingCurrencyRate;
        private string _currencyCode = string.Empty;

        /// <summary>
        /// Идентификатор сохранённого кассового ордера. Заполняется после успешного
        /// сохранения, чтобы вызывающий раздел выделил документ в списке.
        /// </summary>
        public Guid? SavedRecordId { get; private set; }
        private readonly bool _isReadOnly;
        private Guid _selectedCorrAccountId;
        private string _selectedCorrAccountCode = string.Empty;
        // Пока диалог заполняет поле «Корр. счет» программно (загрузка записи, выбор
        // через «?»), обработчик TextChanged не должен сбрасывать выбранный счет.
        private bool _isApplyingCorrAccount;
        private Guid _selectedCashDeskId;
        private string _selectedCashDeskCode = string.Empty;
        private AccountAnalyticsRegistry _accountAnalytics = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;
        private List<CashDeskItem> _cashDesks = new();
        private MetadataObject? _cashDeskCatalog;
        private readonly ObservableCollection<CashPostingPreviewRow> _postingPreviewRows = new();
        private string _orderKind = CashOrderPaymentKind;
        // Флаг проведения исходной записи: если документ был проведен, после
        // редактирования проводки нужно пересформировать (перепровести).
        private bool _wasPosted;

        public static bool ResolveRecordPostedFlag(IReadOnlyDictionary<string, object> record)
        {
            foreach (var key in new[] { "is_posted", "Проведён", "Проведен", "posted" })
            {
                var pair = record.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (pair.Value is bool flag) return flag;
                if (bool.TryParse(pair.Value?.ToString(), out var parsed)) return parsed;
            }

            return false;
        }

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
            DialogTitle.Text = BuildDialogTitle();
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

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId, bool isReadOnly)
        {
            InitializeComponent();
            PostingsPreviewGrid.ItemsSource = _postingPreviewRows;
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            _isReadOnly = isReadOnly;
            DialogTitle.Text = isReadOnly ? "Просмотр: кассовый ордер" : "Редактирование: кассовый ордер";

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
                                "Наименование");
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
                        DialogTitle.Text = BuildDialogTitle();
                        _wasPosted = ResolveRecordPostedFlag(data.Record);
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

                        // Загружаем корреспондирующий счет. В старых записях поле могло быть пустым,
                        // хотя Дт/Кт уже сохранены, поэтому восстанавливаем значение из проводки.
                        ApplyExistingCorrespondentAccount(data.Record);
                        RefreshPostingPreview();

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

                await UpdateDialogTitleWithOpenDayAsync();
                ApplyReadOnlyState();
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
                    .Select(row => CreateReferenceItem(row, "Код организации", "Наименование"))
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
            result.Materials = await LoadReferenceItemsAsync(allCatalogs, "Справочник материалов", "Код", "Наименование");

            // Для ПКО и РКО номера счетов считаются отдельно, хоть записи хранятся в общей таблице.
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

            await UpdateDialogTitleWithOpenDayAsync();
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
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(cashDeskCatalog, _metadataService);
                if (rows.Count == 0)
                {
                    MessageBox.Show("В справочнике касс нет данных. Добавьте кассу кнопкой '+'.", "Кассы",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new ReferenceSelectionDialog(rows, "Наименование кассы", "Счет", referenceMaps)
                {
                    Title = "Выбор: Кассы"
                };

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) == true &&
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

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService);

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
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

                var dialog = new CatalogItemDialog(cashDeskCatalog, _metadataService, cashDesk);

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
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

                var selection = new AccountSelectionView(accountsData);

                if (await MdiDialogService.ShowControlInWorkspaceForResultAsync(this, "Выбор счета", selection) == true && selection.SelectedAccount != null)
                {
                    var accountCode = selection.SelectedAccount.ContainsKey("Код") ? selection.SelectedAccount["Код"].ToString() : "";
                    var accountName = selection.SelectedAccount.ContainsKey("Наименование") ? selection.SelectedAccount["Наименование"].ToString() : "";

                    // Поле редактируемое (MaxLength=8, только цифры), поэтому показываем код счета.
                    // Наименование остается доступным во всплывающей подсказке.
                    _isApplyingCorrAccount = true;
                    try
                    {
                        CorrAccountBox.Text = accountCode ?? string.Empty;
                        CorrAccountBox.ToolTip = string.IsNullOrWhiteSpace(accountName)
                            ? "Только цифры, до 8 знаков"
                            : $"{accountCode} - {accountName}";

                        if (selection.SelectedAccount.ContainsKey("Id"))
                        {
                            _selectedCorrAccountId = Guid.Parse(selection.SelectedAccount["Id"].ToString());
                        }

                        _selectedCorrAccountCode = accountCode ?? string.Empty;
                    }
                    finally
                    {
                        _isApplyingCorrAccount = false;
                    }

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
            if (_isReadOnly)
            {
                MdiDialogService.CloseWithResult(this, false);
                Close();
                return;
            }

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

                var amount = NumericInputHelper.TryParseDecimal(AmountBox.Text, CultureInfo.CurrentCulture, out var parsedAmount)
                    ? parsedAmount
                    : 0m;
                if (amount <= 0)
                {
                    MessageBox.Show("Сумма кассового ордера должна быть больше нуля.", "Проверка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    AmountBox.Focus();
                    AmountBox.SelectAll();
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

                var documentDate = (DatePicker.SelectedDate ?? DateTime.Today).Date;
                if (!await EnsureCashDayAllowsSaveAsync(_selectedCashDeskId, CashDeskCombo.Text, documentDate))
                    return;

                // Если документ уже проведен, нельзя переносить его в закрытый период:
                // проводки после перепроведения были бы потеряны.
                if (_wasPosted && _editId.HasValue)
                {
                    try
                    {
                        var periodService = new BIS.ERP.Services.AccountingPeriodService(
                            await BIS.ERP.ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync());
                        await periodService.EnsureDateCanBeModifiedAsync(documentDate);
                    }
                    catch (InvalidOperationException ex)
                    {
                        MessageBox.Show(ex.Message, "Закрытый период",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Получаем корреспондирующий счет. Код мог быть введен вручную —
                // тогда приводим его к счету плана счетов этого модуля.
                if (!await TryResolveCorrAccountAsync())
                    return;

                string corrAccountId = _selectedCorrAccountId != Guid.Empty ? _selectedCorrAccountId.ToString() : string.Empty;
                string corrAccountCode = _selectedCorrAccountCode;

                if (string.IsNullOrEmpty(corrAccountCode))
                {
                    MessageBox.Show("Укажите корреспондирующий счет: введите до 8 цифр или выберите через кнопку «?».", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var corrAccountValue = !string.IsNullOrWhiteSpace(corrAccountId)
                    ? corrAccountId
                    : corrAccountCode;

                // Определяем дебет и кредит
                var (debitAccount, creditAccount) = BuildPostingAccounts(cashDeskCode, corrAccountCode);

                if (debitAccount == creditAccount)
                {
                    MessageBox.Show("Дебет и кредит не могут быть одинаковыми!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }


                // Валюта
                // Валюта и курс — берём из панели валюты, если она показана.
                // Курс вводится/подтягивается в ExchangeRateBox, валюта — в CurrencyCombo.
                var exchangeRate = NumericInputHelper.TryParseDecimal(ExchangeRateBox?.Text, CultureInfo.CurrentCulture, out var parsedRate)
                    ? parsedRate
                    : 0m;
                var currencyId = GetSelectedReferenceId(CurrencyCombo);

                decimal amountInCurrency = 0m;
                if (CurrencyPanel.Visibility == Visibility.Visible && exchangeRate > 0)
                    amountInCurrency = Math.Round(amount / exchangeRate, 2, MidpointRounding.AwayFromZero);

                var counterpartyOrganizationId =
                    OrganizationCombo.Visibility == Visibility.Visible && OrganizationCombo.SelectedItem is ReferenceItem organization
                        ? organization.Id.ToString()
                        : string.Empty;
                var primaryOrganizationId = await _metadataService.GetPrimaryOrganizationIdAsync();

                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = documentNumber,
                    ["Дата"] = documentDate,
                    ["Тип КО"] = _orderKind,
                    ["Сумма"] = amount,
                    ["Основание"] = BasisBox.Text,
                    ["Примечание"] = DescriptionBox.Text,
                    ["Проведён"] = _wasPosted,
                    ["Проведен"] = _wasPosted,
                    ["is_posted"] = _wasPosted,
                    ["Касса"] = cashDeskId,
                    ["cash_desk_id"] = cashDeskId,
                    ["Корр. счет"] = corrAccountValue,
                    ["correspondent_account"] = corrAccountValue,
                    ["Организация"] = counterpartyOrganizationId,
                    ["organization_id"] = counterpartyOrganizationId,
                    ["Первичная организация"] = primaryOrganizationId,
                    ["primary_organization_id"] = primaryOrganizationId,
                    ["Первая организация"] = primaryOrganizationId,
                    ["Организация Б"] = counterpartyOrganizationId,
                    ["counterparty_organization_id"] = counterpartyOrganizationId,
                    ["Дебет"] = debitAccount,
                    ["debit_account"] = debitAccount,
                    ["Кредит"] = creditAccount,
                    ["credit_account"] = creditAccount,
                    ["Курс"] = exchangeRate,
                    ["exchange_rate"] = exchangeRate,
                    ["Валюта"] = currencyId != Guid.Empty ? currencyId.ToString() : string.Empty,
                    ["currency_id"] = currencyId != Guid.Empty ? currencyId.ToString() : string.Empty,
                    ["Сумма в валюте"] = amountInCurrency,
                    ["amount_currency"] = amountInCurrency
                };

                // Заполняем остальные поля (только если они видимы, иначе не сохраняем)
                SetFieldValueIfExists(itemData, "Организация", counterpartyOrganizationId);
                SetFieldValueIfExists(itemData, "Первичная организация", primaryOrganizationId);
                SetFieldValueIfExists(itemData, "Организация Б", counterpartyOrganizationId);

                SetFieldValueIfExists(itemData, "Сотрудник",
                    EmployeePanel.Visibility == Visibility.Visible && _selectedEmployeeId != Guid.Empty
                        ? _selectedEmployeeId.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Материал",
                    MaterialPanel.Visibility == Visibility.Visible && MaterialCombo.SelectedItem is ReferenceItem material
                        ? material.Id.ToString()
                        : string.Empty);

                SetFieldValueIfExists(itemData, "Контрагент", string.Empty);

                //  Сохраняем документ.
                if (_editId.HasValue)
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                    SavedRecordId = _editId.Value;
                }
                else
                {
                    SavedRecordId = await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);
                }

                // Если документ был проведен — пересоздаем проводки с новыми данными:
                // журналы и отчеты строятся по doc_postings и без этого остались бы старыми.
                if (_editId.HasValue && _wasPosted)
                {
                    try
                    {
                        await _metadataService.UnpostDocumentAsync(_document.Id, _editId.Value);
                        await _metadataService.PostDocumentAsync(_document.Id, _editId.Value);
                    }
                    catch (Exception repostEx)
                    {
                        MessageBox.Show(
                            $"Документ сохранен, но при перепроведении возникла ошибка:\n{repostEx.Message}\n\n" +
                            "Проверьте документ в журнале кассовых ордеров и проведите его повторно.",
                            "Перепроведение",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                BIS.ERP.Services.MdiDialogService.CloseWithResult(this, true);
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

        private async Task<bool> EnsureCashDayAllowsSaveAsync(Guid cashDeskId, string cashDeskName, DateTime documentDate)
        {
            const string caption = "Проверка кассового дня";
            if (cashDeskId == Guid.Empty)
            {
                MessageBox.Show("Выберите кассу.", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                CashDeskCombo.Focus();
                return false;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var lastClosedDate = await cashDayService.GetLastClosedDayDateAsync(cashDeskId);

                if (lastClosedDate.HasValue && documentDate.Date <= lastClosedDate.Value.Date)
                {
                    MessageBox.Show($"Документ от {documentDate:dd/MM/yyyy} относится к закрытому кассовому периоду по кассе \"{cashDeskName}\". Создание и изменение документов в закрытом периоде запрещено.",
                        caption,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                var currentOpenDay = await cashDayService.GetCurrentOpenDayAsync(cashDeskId);
                if (currentOpenDay != null)
                    return true;

                var answer = MessageBox.Show($"По кассе \"{cashDeskName}\" нет открытого кассового дня. Открыть день {documentDate:dd/MM/yyyy} для работы?",
                    caption,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                    return false;

                await cashDayService.OpenDayAsync(cashDeskId, documentDate.Date, CurrentUserNameForAudit());
                await UpdateDialogTitleWithOpenDayAsync();
                return true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки кассового дня: {ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        private string BuildDialogTitle()
        {
            var action = _editId.HasValue ? "Редактирование" : "Добавление";
            return $"{action}: {GetOrderKindDisplay(_orderKind)} КО";
        }

        private async Task UpdateDialogTitleWithOpenDayAsync()
        {
            if (DialogTitle == null)
                return;

            var title = BuildDialogTitle();
            if (_selectedCashDeskId == Guid.Empty)
            {
                DialogTitle.Text = $"{title} | открытый день: касса не выбрана";
                return;
            }

            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var cashDayService = new CashDayClosureService(context);
                var openDay = await cashDayService.GetCurrentOpenDayAsync(_selectedCashDeskId);
                var openDayText = openDay == null ? "не открыт" : openDay.CloseDate.ToString("dd/MM/yyyy");
                DialogTitle.Text = $"{title} | открытый день: {openDayText}";
            }
            catch
            {
                DialogTitle.Text = $"{title} | открытый день: не определен";
            }
        }
        private static string CurrentUserNameForAudit() =>
            string.IsNullOrWhiteSpace(Environment.UserName) ? "user" : Environment.UserName;

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
            BIS.ERP.Services.MdiDialogService.CloseWithResult(this, false);
            Close();
        }

        /// <summary>
        /// Применяет режим «только чтение»: блокирует все поля ввода (внутри InputScrollViewer)
        /// и превращает кнопку «Сохранить» в «Закрыть». Просмотр проводок остаётся доступным.
        /// </summary>
        private void ApplyReadOnlyState()
        {
            if (!_isReadOnly || InputScrollViewer == null)
                return;

            SetInputControlsEnabled(InputScrollViewer, false);

            if (SaveButton != null)
            {
                SaveButton.Content = "Закрыть";
                SaveButton.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(149, 165, 166));
            }
        }

        private static void SetInputControlsEnabled(DependencyObject parent, bool enabled)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);

                if (child is not ScrollViewer && child is System.Windows.Controls.Control control)
                {
                    control.IsEnabled = enabled;
                }

                SetInputControlsEnabled(child, enabled);
            }
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

        //------------------------------------------------------------------------
        // --- «Корр. счет»: ручной ввод кода (только цифры, до 8 знаков) и «?» ---

        private void OnCorrAccountPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsAccountCodeDigits(e.Text);
        }

        private void OnCorrAccountPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Пробел не приходит через PreviewTextInput — отсекаем отдельно.
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        private void OnCorrAccountPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText, true))
            {
                e.CancelCommand();
                return;
            }

            var text = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
            if (!IsAccountCodeDigits(text))
                e.CancelCommand();
        }

        /// <summary>
        /// Ручной ввод сбрасывает ранее выбранный счет из диалога, чтобы при сохранении
        /// не записался устаревший идентификатор. Код при этом берется из поля.
        /// </summary>
        private void OnCorrAccountTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingCorrAccount)
                return;

            _selectedCorrAccountId = Guid.Empty;
            _selectedCorrAccountCode = CorrAccountBox?.Text?.Trim() ?? string.Empty;
            if (CorrAccountBox != null)
                CorrAccountBox.ToolTip = "Только цифры, до 8 знаков";

            UpdateAccountControlledFieldsVisibility();
            RefreshPostingPreview();
        }

        /// <summary>
        /// Приводит код, введённый в поле вручную, к счету плана счетов этого модуля.
        /// Возвращает false (с сообщением пользователю), если код не найден.
        /// </summary>
        private async Task<bool> TryResolveCorrAccountAsync()
        {
            if (_selectedCorrAccountId != Guid.Empty)
                return true;

            var code = ExtractAccountCodeDigits(CorrAccountBox?.Text);
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show(
                    "Укажите корреспондирующий счет. Введите до 8 цифр или выберите через кнопку «?».",
                    "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                CorrAccountBox?.Focus();
                return false;
            }

            var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                _document.Id,
                _document.ObjectType);

            var match = accountsData?.FirstOrDefault(row =>
                row.TryGetValue("Код", out var codeValue) &&
                string.Equals(codeValue?.ToString()?.Trim(), code, StringComparison.Ordinal));

            if (match == null || !Guid.TryParse(match["Id"]?.ToString(), out var accountId))
            {
                MessageBox.Show(
                    $"Счет с кодом «{code}» не найден в плане счетов для этого модуля.",
                    "Проверка", MessageBoxButton.OK, MessageBoxImage.Warning);
                CorrAccountBox?.Focus();
                CorrAccountBox?.SelectAll();
                return false;
            }

            var account = _accountAnalytics.FindAccount(match["Id"]);
            ApplyCorrAccount(accountId, account?.Code ?? code, account);

            UpdateAccountControlledFieldsVisibility();
            RefreshPostingPreview();
            return true;
        }

        /// <summary>Заполняет поле и состояние счета, не затрагивая ручной ввод пользователя.</summary>
        private void ApplyCorrAccount(Guid accountId, string accountCode, AccountReferenceItem? account)
        {
            _isApplyingCorrAccount = true;
            try
            {
                CorrAccountBox.Text = accountCode;
                CorrAccountBox.ToolTip = account == null || string.IsNullOrWhiteSpace(account.DisplayName)
                    ? "Только цифры, до 8 знаков"
                    : account.DisplayName;
                _selectedCorrAccountId = accountId;
                _selectedCorrAccountCode = accountCode;
            }
            finally
            {
                _isApplyingCorrAccount = false;
            }
        }

        private static bool IsAccountCodeDigits(string? text)
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

        private static string ExtractAccountCodeDigits(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (character is >= '0' and <= '9')
                    builder.Append(character);
            }

            return builder.ToString();
        }
        //------------------------------------------------------------------------

        private bool ApplySelectedCorrAccount(object? accountValue)
        {
            var account = _accountAnalytics.FindAccount(accountValue);
            if (account == null)
                return false;

            ApplyCorrAccount(account.Id, account.Code, account);
            return true;
        }

        private void ApplyExistingCorrespondentAccount(Dictionary<string, object> record)
        {
            var savedAccountValue = GetRowValue(
                record,
                "Корр. счет",
                "Корр. счёт",
                "Корр. Счет",
                "correspondent_account");

            if (ApplySelectedCorrAccount(savedAccountValue))
                return;

            var inferredAccountCode = GetStoredCorrespondentAccountCode(record);
            if (ApplySelectedCorrAccount(inferredAccountCode))
                return;

            if (!string.IsNullOrWhiteSpace(inferredAccountCode))
            {
                _isApplyingCorrAccount = true;
                try
                {
                    _selectedCorrAccountCode = inferredAccountCode;
                    CorrAccountBox.Text = inferredAccountCode;
                }
                finally
                {
                    _isApplyingCorrAccount = false;
                }
            }
        }

        private string GetStoredCorrespondentAccountCode(Dictionary<string, object> record)
        {
            var debitAccount = GetRowString(record, "Дебет", "debit_account");
            var creditAccount = GetRowString(record, "Кредит", "credit_account");
            var candidate = IsReceiptOrder(_orderKind) ? creditAccount : debitAccount;
            var accountCode = NormalizeAccountCodeText(candidate ?? string.Empty);

            return !string.IsNullOrWhiteSpace(accountCode) &&
                   !string.Equals(accountCode, _selectedCashDeskCode, StringComparison.OrdinalIgnoreCase)
                ? accountCode
                : string.Empty;
        }

        private static object? GetRowValue(Dictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != null && value != DBNull.Value)
                    return value;
            }

            foreach (var key in keys)
            {
                var match = row.FirstOrDefault(pair =>
                    string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match.Key) && match.Value != null && match.Value != DBNull.Value)
                    return match.Value;
            }

            return null;
        }

        private void UpdateAccountControlledFieldsVisibility()
        {
            var settings = _selectedCorrAccountId == Guid.Empty
                ? null
                : _accountAnalytics.GetSettingsById(_selectedCorrAccountId);

            // Организация
            SetAccountControlledFieldVisibility(
                OrganizationLabel,
                OrganizationCombo,
                AccountAnalyticsRules.ShouldShowField(
                    "Организация",
                    new[] { settings },
                    _accountAnalytics.Definitions,
                    "Организации",
                    showWhenNoAccountSelected: false,
                    showUnmappedFields: false),
                OrganizationSelectButton);

            // Сотрудник — просто показываем/скрываем панель
            EmployeePanel.Visibility = AccountAnalyticsRules.ShouldShowField(
                "Сотрудник",
                new[] { settings },
                _accountAnalytics.Definitions,
                "Сотрудники (Списочный состав)",
                showWhenNoAccountSelected: false,
                showUnmappedFields: false)
                ? Visibility.Visible : Visibility.Collapsed;

            // Материал
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

            // Валюта: берётся из самого счёта («Валюта счета» в плане счетов)
            _ = UpdateCurrencyFromCorrAccountAsync();
        }

        /// <summary>
        /// Обновляет панель валюты по выбранному корреспондирующему счёту:
        /// берёт валюту, закреплённую за счётом в плане счетов, подставляет её
        /// в CurrencyCombo и подтягивает курс на дату документа.
        /// </summary>
        private async Task UpdateCurrencyFromCorrAccountAsync()
        {
            if (CurrencyPanel == null)
                return;

            // Счёт ещё не выбран — панель скрываем
            if (_selectedCorrAccountId == Guid.Empty &&
                string.IsNullOrWhiteSpace(_selectedCorrAccountCode))
            {
                HideCurrencyPanel();
                return;
            }

            // Валюта счёта из плана счетов
            var currencyId = await ResolveAccountCurrencyIdAsync(
                _selectedCorrAccountId,
                _selectedCorrAccountCode);

            // У счёта валюта не задана — панель скрываем
            if (currencyId == Guid.Empty)
            {
                HideCurrencyPanel();
                return;
            }

            // Показываем панель
            CurrencyPanel.Visibility = Visibility.Visible;

            // Находим валюту в ComboBox
            var currencyItem = CurrencyCombo.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item => item.Id == currencyId);

            // Если справочник валют ещё не подгружен — подгружаем
            if (currencyItem == null)
            {
                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var currencies = await LoadReferenceItemsAsync(
                    allCatalogs, "Справочник валют", "Код", "Наименование");

                ReferenceComboBoxSearchHelper.Attach(CurrencyCombo, currencies);

                currencyItem = currencies.FirstOrDefault(item => item.Id == currencyId);
            }

            if (currencyItem == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Валюта счёта {_selectedCorrAccountCode} не найдена в справочнике валют (Id={currencyId}).");
                return;
            }

            // Подставляем валюту без рекурсии
            if (GetSelectedReferenceId(CurrencyCombo) != currencyItem.Id)
            {
                try
                {
                    _isApplyingCurrencyRate = true;
                    CurrencyCombo.SelectedItem = currencyItem;
                }
                finally
                {
                    _isApplyingCurrencyRate = false;
                }
            }

            // Курс на дату документа
            await LoadExchangeRateFromCatalogAsync();
        }

        /// <summary>Скрывает панель валюты и очищает связанные поля.</summary>
        private void HideCurrencyPanel()
        {
            if (CurrencyPanel == null)
                return;

            CurrencyPanel.Visibility = Visibility.Collapsed;
            CurrencyCombo.SelectedItem = null;
            if (ExchangeRateBox != null)
                ExchangeRateBox.Text = "0";
            if (AmountCurrencyBox != null)
                AmountCurrencyBox.Text = "0";

            RefreshPostingPreview();
        }

        /// <summary>Id выбранного элемента в ComboBox.</summary>
        private static Guid GetSelectedReferenceId(ComboBox? comboBox)
        {
            return comboBox?.SelectedItem is ReferenceItem selected ? selected.Id : Guid.Empty;
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

            foreach (var key in keys)
            {
                var match = row.FirstOrDefault(pair =>
                    string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match.Key) && match.Value != null && match.Value != DBNull.Value)
                {
                    var text = match.Value.ToString();
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
            bool isVisible,
            FrameworkElement? selectButton = null)
        {
            var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = visibility;
            comboBox.Visibility = visibility;

            if (selectButton != null)
                selectButton.Visibility = visibility;

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

                var dialog = new ReferenceSelectionDialog(employeesData, "Табельный номер", "ФИО") { Title = "Выбор: Сотрудники" };

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) == true && dialog.SelectedItem != null)
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

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService);

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
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

                var dialog = new CatalogItemDialog(employeeCatalog, _metadataService, employee);

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) != true)
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
        private async void CashDeskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CashDeskCombo.SelectedItem is CashDeskItem selected)
            {
                _selectedCashDeskId = selected.Id;
                _selectedCashDeskCode = selected.AccountCode;
                CashDeskAccountBox.Text = _selectedCashDeskCode;
                RefreshPostingPreview();
            }
            else
            {
                _selectedCashDeskId = Guid.Empty;
                _selectedCashDeskCode = string.Empty;
                CashDeskAccountBox.Text = string.Empty;
                RefreshPostingPreview();
            }

            await UpdateDialogTitleWithOpenDayAsync();
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
            => NumericInputHelper.TryParseDecimal(AmountBox?.Text, CultureInfo.CurrentCulture, out var parsedAmount)
                ? parsedAmount
                : 0m;

        private void OnPostingPreviewChanged(object sender, EventArgs e)
        {
            RefreshPostingPreview();

            // При вводе суммы пересчитываем «Сумму в валюте» по курсу
            if (!_isApplyingCurrencyRate && CurrencyPanel?.Visibility == Visibility.Visible)
                RecalculateAmountFromCurrency();
        }

        /// <summary>
        /// Кнопка «?» рядом с организацией. Открывает справочник организаций
        /// и подставляет выбранную в OrganizationCombo.
        /// </summary>
        private async void SelectOrganization_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Cursor = Cursors.Wait;

                var allCatalogs = await _metadataService.GetCatalogsAsync();
                var organizationCatalog = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
                if (organizationCatalog == null)
                {
                    MessageBox.Show("Справочник организаций не найден.", "Организации",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var rows = await _metadataService.GetCatalogDataAsync(organizationCatalog.Id);
                if (rows.Count == 0)
                {
                    MessageBox.Show("В справочнике организаций нет данных.", "Организации",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(organizationCatalog, _metadataService);
                var dialog = new ReferenceSelectionDialog(rows, "Код организации", "Наименование", referenceMaps)
                {
                    Title = "Выбор: Организации"
                };

                if (await MdiDialogService.ShowInWorkspaceForResultAsync(this, dialog, dialog.Title) == true &&
                    dialog.SelectedItem != null &&
                    dialog.SelectedItem.TryGetValue("Id", out var idValue) &&
                    Guid.TryParse(idValue?.ToString(), out var selectedId))
                {
                    var selected = OrganizationCombo.Items
                        .OfType<ReferenceItem>()
                        .FirstOrDefault(item => item.Id == selectedId);

                    if (selected != null)
                        OrganizationCombo.SelectedItem = selected;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при выборе организации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = null;
            }
        }

        private void RefreshPostingPreview()
        {
            if (PostingsPreviewGrid == null || PostingPreviewHint == null)
                return;

            _postingPreviewRows.Clear();

            var amount = TryReadAmount();
            var (debitAccount, creditAccount) = BuildPostingAccounts(_selectedCashDeskCode, _selectedCorrAccountCode);
            var isReceipt = IsReceiptOrder(_orderKind);

            // Валюта, курс и сумма в валюте (если панель валюты показана)
            string currencyName = string.Empty;
            string exchangeRateText = string.Empty;
            string amountCurrencyText = string.Empty;

            if (CurrencyPanel?.Visibility == Visibility.Visible)
            {
                currencyName = CurrencyCombo?.SelectedItem is ReferenceItem currency
                    ? currency.DisplayName
                    : string.Empty;

                var rate = NumericInputHelper.TryParseDecimal(ExchangeRateBox?.Text, CultureInfo.CurrentCulture, out var parsedRate)
                    ? parsedRate
                    : 0m;

                var amountCurrency = NumericInputHelper.TryParseDecimal(AmountCurrencyBox?.Text, CultureInfo.CurrentCulture, out var parsedAmountCurrency)
                    ? parsedAmountCurrency
                    : 0m;

                exchangeRateText = rate > 0 ? rate.ToString("0.####", CultureInfo.CurrentCulture) : string.Empty;
                amountCurrencyText = amountCurrency > 0 ? amountCurrency.ToString("N2", CultureInfo.CurrentCulture) : string.Empty;
            }

            _postingPreviewRows.Add(new CashPostingPreviewRow
            {
                Mark = isReceipt ? "Приходный" : "Расходный",
                Debit = string.IsNullOrWhiteSpace(debitAccount) ? "не выбран" : debitAccount,
                Credit = string.IsNullOrWhiteSpace(creditAccount) ? "не выбран" : creditAccount,
                Amount = amount.ToString("N2", CultureInfo.CurrentCulture),
                ExchangeRate = exchangeRateText,
                AmountCurrency = amountCurrencyText,
                Note = string.IsNullOrWhiteSpace(BasisBox?.Text) ? DescriptionBox?.Text ?? string.Empty : BasisBox.Text
            });

            PostingPreviewHint.Text = isReceipt
                ? "Приходный заказ: дебетуется счет кассы, кредитуется корреспондирующий счет."
                : "Расходный заказ: дебетуется корреспондирующий счет, кредитуется счет кассы.";
        }

        /// <summary>
        /// Смена валюты или даты документа: курс подтягивается из справочника
        /// курсов валют, «Сумма в валюте» пересчитывается по курсу.
        /// </summary>
        private async void OnRateInputChanged(object sender, EventArgs e)
        {
            if (_isLoading || _isApplyingCurrencyRate || CurrencyCombo == null || DatePicker == null)
                return;

            await LoadExchangeRateFromCatalogAsync();
        }

        /// <summary>
        /// Ручное изменение курса или суммы в валюте: если курс введён вручную,
        /// «Сумма в валюте» пересчитывается от суммы документа по курсу.
        /// </summary>
        private void CurrencyAmountBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingCurrencyRate || AmountCurrencyBox == null ||
                ExchangeRateBox == null || AmountBox == null)
            {
                return;
            }

            RecalculateAmountFromCurrency();
        }

        /// <summary>
        /// Загружает курс валюты на дату документа из справочника курсов валют
        /// и пересчитывает «Сумму в валюте».
        /// </summary>
        private async Task LoadExchangeRateFromCatalogAsync(bool interactive = false)
        {
            if (CurrencyCombo?.SelectedItem is not ReferenceItem currency ||
                DatePicker?.SelectedDate is not DateTime documentDate)
            {
                return;
            }

            var rateDate = documentDate.Date;
            var rate = await _metadataService.GetCurrencyRateForDateAsync(currency.Id, rateDate);

            if (rate == null && interactive)
                rate = await RequestNewExchangeRateAsync(currency.Id, rateDate);

            if (rate != null)
            {
                try
                {
                    _isApplyingCurrencyRate = true;
                    ExchangeRateBox.Text = rate.Rate.ToString("0.####", CultureInfo.CurrentCulture);
                }
                finally
                {
                    _isApplyingCurrencyRate = false;
                }

                RecalculateAmountFromCurrency();
                return;
            }

            if (interactive)
            {
                MessageBox.Show(
                    $"Курс валюты на {rateDate:dd/MM/yyyy} не найден. Введите курс вручную.",
                    "Курс валюты",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Предлагает загрузить свежие курсы НБКР и повторно ищет курс на дату.
        /// Возвращает null, если курс так и не найден — пользователь вводит его вручную.
        /// </summary>
        private async Task<CurrencyRateLookupResult?> RequestNewExchangeRateAsync(Guid currencyId, DateTime rateDate)
        {
            var answer = MessageBox.Show(
                $"Курс на {rateDate:dd/MM/yyyy} не найден в справочнике курсов валют. Загрузить актуальные курсы НБКР?",
                "Курс валюты",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return null;

            try
            {
                Cursor = Cursors.Wait;
                var results = await _metadataService.ImportOfficialCurrencyRatesAsync(rateDate.AddDays(-7), rateDate);
                var refreshed = await _metadataService.GetCurrencyRateForDateAsync(currencyId, rateDate);
                if (refreshed != null)
                    return refreshed;

                MessageBox.Show(
                    $"Курсы загружены (добавлено: {results.Sum(item => item.Imported)}, " +
                    $"пропущено: {results.Sum(item => item.Skipped)}), но курс на {rateDate:dd/MM/yyyy} отсутствует. " +
                    "Введите курс вручную.",
                    "Курс валюты",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось загрузить курсы НБКР: {ex.Message}", "Курс валюты",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            finally
            {
                Cursor = null;
            }
        }

        /// <summary>
        /// Кнопка «?» рядом с курсом: запрос курса из справочника курсов валют.
        /// </summary>
        private async void RequestHeaderExchangeRate_Click(object sender, RoutedEventArgs e)
        {
            await LoadExchangeRateFromCatalogAsync(interactive: true);
        }

        /// <summary>
        /// «Сумма в валюте» = Сумма документа / Курс.
        /// </summary>
        private void RecalculateAmountFromCurrency()
        {
            if (AmountCurrencyBox == null || ExchangeRateBox == null || AmountBox == null)
                return;

            try
            {
                _isApplyingCurrencyRate = true;

                var amount = NumericInputHelper.TryParseDecimal(AmountBox.Text, CultureInfo.CurrentCulture, out var parsedAmount)
                    ? parsedAmount
                    : 0m;
                var rate = NumericInputHelper.TryParseDecimal(ExchangeRateBox.Text, CultureInfo.CurrentCulture, out var parsedRate)
                    ? parsedRate
                    : 0m;

                AmountCurrencyBox.Text = amount > 0 && rate > 0
                    ? Math.Round(amount / rate, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.CurrentCulture)
                    : "0";
            }
            finally
            {
                _isApplyingCurrencyRate = false;
            }

            // Обновляем предпросмотр проводки: курс и сумма в валюте изменились
            RefreshPostingPreview();
        }

        /// <summary>
        /// Достаёт валюту, закреплённую за счётом плана счетов (реквизит «Валюта счета»).
        /// Возвращает Guid.Empty, если у счёта валюта не задана или счёт не найден.
        /// </summary>
        private async Task<Guid> ResolveAccountCurrencyIdAsync(Guid accountId, string accountCode)
        {
            if (accountId == Guid.Empty && string.IsNullOrWhiteSpace(accountCode))
                return Guid.Empty;

            try
            {
                var accountsData = await _metadataService.GetChartOfAccountsSelectionDataForObjectAsync(
                    _document.Id,
                    _document.ObjectType);

                if (accountsData == null || accountsData.Count == 0)
                    return Guid.Empty;

                // Ищем строку счёта: сначала по Id, потом по коду (на случай ручного ввода)
                Dictionary<string, object>? accountRow = null;

                if (accountId != Guid.Empty)
                {
                    accountRow = accountsData.FirstOrDefault(row =>
                        row.TryGetValue("Id", out var idValue) &&
                        Guid.TryParse(idValue?.ToString(), out var rowId) &&
                        rowId == accountId);
                }

                if (accountRow == null && !string.IsNullOrWhiteSpace(accountCode))
                {
                    accountRow = accountsData.FirstOrDefault(row =>
                        row.TryGetValue("Код", out var codeValue) &&
                        string.Equals(codeValue?.ToString()?.Trim(), accountCode, StringComparison.Ordinal));
                }

                if (accountRow == null)
                    return Guid.Empty;

                // Валюта счёта — это ссылка на справочник валют (Guid).
                // Пробуем несколько возможных ключей.
                foreach (var key in new[]
                         {
                     "Валюта счета", "account_currency_id",
                     "Валюта", "currency_id"
                 })
                {
                    if (accountRow.TryGetValue(key, out var value) &&
                        value != null && value != DBNull.Value)
                    {
                        if (value is Guid guid && guid != Guid.Empty)
                            return guid;

                        if (Guid.TryParse(value.ToString(), out var parsed) && parsed != Guid.Empty)
                            return parsed;
                    }
                }

                return Guid.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Не удалось определить валюту счёта {accountCode}: {ex.Message}");
                return Guid.Empty;
            }
        }
    }

    public class CashPostingPreviewRow
    {
        public string Mark { get; set; } = string.Empty;
        public string Debit { get; set; } = string.Empty;
        public string Credit { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string ExchangeRate { get; set; } = string.Empty;   // ← новое
        public string AmountCurrency { get; set; } = string.Empty; // ← новое
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