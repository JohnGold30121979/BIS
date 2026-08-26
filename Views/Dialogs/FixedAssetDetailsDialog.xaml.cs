using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public enum FixedAssetCardMode
    {
        Create,
        Edit,
        Details
    }

    public partial class FixedAssetDetailsDialog : Window
    {
        private readonly MetadataObject _catalog;
        private readonly MetadataService _metadataService;
        private readonly FixedAssetCardMode _mode;
        private readonly Dictionary<string, object> _existingData;
        private readonly DateTime _asOfDate;
        private Dictionary<string, MetadataObject> _catalogsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object?> _pickerValues = new(StringComparer.OrdinalIgnoreCase);
        private bool _isUpdatingCalculatedFields;

        private enum DepreciationCalculationSource
        {
            Auto,
            BaseAmount,
            UsefulLife,
            Rate,
            Monthly,
            Mileage
        }

        public Dictionary<string, object> ItemData { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FixedAssetDetailsDialog(
            MetadataObject catalog,
            MetadataService metadataService,
            FixedAssetCardMode mode,
            IReadOnlyDictionary<string, object>? existingData = null,
            Guid? recordId = null,
            DateTime? asOfDate = null)
        {
            InitializeComponent();
            AttachDepreciationCalculationHandlers();

            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            _mode = mode;
            _asOfDate = asOfDate ?? DateTime.Today;
            _existingData = existingData?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            if (recordId.HasValue)
                _existingData["Id"] = recordId.Value;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки карточки ОС: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializeAsync()
        {
            var catalogs = await _metadataService.GetCatalogsAsync();
            _catalogsByName = catalogs
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            HeaderTitleText.Text = _mode switch
            {
                FixedAssetCardMode.Create => "Добавление: Основное средство",
                FixedAssetCardMode.Edit => "Редактирование: Основное средство",
                _ => "Детально: Основное средство"
            };
            HeaderSubtitleText.Text = $"Справочник ОС на {_asOfDate:dd.MM.yyyy}";
            Title = HeaderTitleText.Text;

            PopulateScalarFields();
            await EnsureGeneratedCodeAsync();
            await PopulateReferenceFieldsAsync();
            ConfigureReferenceActions();
            ApplyMode();
        }

        private void PopulateScalarFields()
        {
            CodeBox.Text = TextValue("code", "Код");
            NameBox.Text = TextValue("name", "Наименование");
            InventoryNumberBox.Text = TextValue("inventory_number", "Инвентарный номер");
            ManufactureYearBox.Text = TextValue("manufacture_year", "Год выпуска оборудования", "Год выпуска");
            UseCodeAsInventoryNumberCheckBox.IsChecked = BoolValue("use_code_as_inventory_number", "Основной код для инв. №");

            AcquisitionDatePicker.SelectedDate = DateValue("acquisition_date", "Дата приобретения", "Поступления ОС");
            AcquisitionDocumentBox.Text = TextValue("acquisition_document_number", "Документ поступления ОС");
            CommissioningDatePicker.SelectedDate = DateValue("commissioning_date", "Дата ввода в эксплуатацию");
            CommissioningDocumentBox.Text = TextValue("commissioning_document_number", "Документ ввода в эксплуатацию");
            DepreciationStartDatePicker.SelectedDate = DateValue("depreciation_start_date", "Дата начала амортизации", "Дата нач. износа");

            InitialCostBox.Text = DecimalText("initial_cost", "Начальная стоимость");
            SalvageValueBox.Text = DecimalText("salvage_value", "Ликвидационная стоимость");
            AccumulatedDepreciationBox.Text = DecimalText("accumulated_depreciation", "Накопленный износ");
            CarryingAmountBox.Text = DecimalText("carrying_amount", "Перерасчетная стоимость", "Остаточная стоимость");
            UsefulLifeMonthsBox.Text = TextValue("useful_life_months", "Срок полезного использования");
            DepreciationCodeBox.Text = TextValue("depreciation_code", "Шифр");
            if (string.IsNullOrWhiteSpace(DepreciationCodeBox.Text))
                DepreciationCodeBox.Text = "0";
            DepreciationRateBox.Text = DecimalText("depreciation_rate", "Норма амортизации, %");
            MonthlyDepreciationBox.Text = DecimalText("monthly_depreciation", "Месячная амортизация");

            UseMileageDepreciationCheckBox.IsChecked = BoolValue("use_mileage_depreciation", "Амортизация по пробегу");
            MonthlyMileageBox.Text = DecimalText("monthly_mileage", "Месячный пробег");
            MileageResourceBox.Text = DecimalText("mileage_resource", "Ресурс пробега");
            DescriptionBox.Text = TextValue("description", "Описание");

            var assetClass = TextValue("asset_class", "Класс ОС");
            MobileClassRadio.IsChecked = assetClass == "2" || assetClass.Equals("Подвижной", StringComparison.OrdinalIgnoreCase);
            StationaryClassRadio.IsChecked = MobileClassRadio.IsChecked != true;
            RecalculateDepreciationFields();
        }

        private async Task PopulateReferenceFieldsAsync()
        {
            await SetReferenceDisplayAsync(AssetTypeDisplayBox, "asset_type_id", "Вид ОС", "Виды ОС");
            await SetReferenceDisplayAsync(AssetGroupDisplayBox, "asset_group", "Группа ОС", "Группы ОС");
            await SetReferenceDisplayAsync(AssetSubgroupDisplayBox, "asset_subgroup_id", "Категория ОС", "Подгруппа ОС", "Подгруппы ОС");
            await SetReferenceDisplayAsync(AssetAccountDisplayBox, "asset_account", "Материальный счет", "Счет учета", "План счетов");
            await SetReferenceDisplayAsync(DepreciationAccountDisplayBox, "depreciation_account", "Счет износа", "Счет амортизации", "План счетов");
            await SetReferenceDisplayAsync(ExpenseAccountDisplayBox, "expense_account", "Счет списания затрат по амортизации", "Затратный счет", "План счетов");
            await SetReferenceDisplayAsync(SiteDisplayBox, "site_id", "Участок", "Участки");
            await SetReferenceDisplayAsync(ResponsiblePersonDisplayBox, "responsible_person_id", "МОЛ");
            await SetReferenceDisplayAsync(TaxGroupDisplayBox, "tax_group", "Группа для налог декл", "Налоговая группа", "Налоговые группы ОС");
            await SetReferenceDisplayAsync(DepreciationMethodDisplayBox, "depreciation_method", "Метод амортизации", "Методы амортизации ОС");
        }

        private void ConfigureReferenceActions()
        {
            ConfigureReferenceActions(AssetTypeDisplayBox, "asset_type_id", "Вид ОС", "Виды ОС");
            ConfigureReferenceActions(AssetGroupDisplayBox, "asset_group", "Группа ОС", "Группы ОС");
            ConfigureReferenceActions(AssetSubgroupDisplayBox, "asset_subgroup_id", "Категория ОС", "Подгруппа ОС", "Подгруппы ОС");
            ConfigureReferenceActions(AssetAccountDisplayBox, "asset_account", "Материальный счет", "Счет учета", "План счетов");
            ConfigureReferenceActions(DepreciationAccountDisplayBox, "depreciation_account", "Счет износа", "Счет амортизации", "План счетов");
            ConfigureReferenceActions(ExpenseAccountDisplayBox, "expense_account", "Счет списания затрат по амортизации", "Затратный счет", "План счетов");
            ConfigureReferenceActions(SiteDisplayBox, "site_id", "Участок", "Участки");
            ConfigureReferenceActions(ResponsiblePersonDisplayBox, "responsible_person_id", "МОЛ");
            ConfigureReferenceActions(TaxGroupDisplayBox, "tax_group", "Группа для налог декл", "Налоговая группа", "Налоговые группы ОС");
            ConfigureReferenceActions(DepreciationMethodDisplayBox, "depreciation_method", "Метод амортизации", "Методы амортизации ОС");
        }

        private void ConfigureReferenceActions(TextBox displayBox, params string[] aliases)
        {
            if (displayBox.Parent is not DockPanel panel || string.Equals(panel.Tag?.ToString(), "Configured", StringComparison.Ordinal))
                return;

            var selectButton = CreatePickerActionButton("?", "Выбрать из справочника");
            var addButton = CreatePickerActionButton("+", "Добавить запись в справочник");
            var editButton = CreatePickerActionButton("Изм", "Изменить выбранную запись");

            selectButton.Click += async (_, _) => await PickReferenceAsync(displayBox, aliases);
            addButton.Click += async (_, _) => await AddReferenceAsync(displayBox, aliases);
            editButton.Click += async (_, _) => await EditReferenceAsync(displayBox, aliases);

            panel.Children.Clear();
            AddRightDockedButton(panel, editButton);
            AddRightDockedButton(panel, addButton);
            AddRightDockedButton(panel, selectButton);
            panel.Children.Add(displayBox);
            panel.Tag = "Configured";
        }

        private static Button CreatePickerActionButton(string content, string tooltip)
        {
            return new Button
            {
                Content = content,
                Width = content.Length > 1 ? 46 : 34,
                Height = 29,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(175, 197, 216)),
                ToolTip = tooltip,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static void AddRightDockedButton(DockPanel panel, Button button)
        {
            DockPanel.SetDock(button, Dock.Right);
            panel.Children.Add(button);
        }
        private void ApplyMode()
        {
            ApplyCodeReadOnly();
            ApplyCalculatedFieldsReadOnly();

            if (_mode != FixedAssetCardMode.Details)
            {
                StatusText.Text = _mode == FixedAssetCardMode.Create
                    ? "Заполните карточку основного средства."
                    : "Измените карточку основного средства.";
                return;
            }

            SetReadOnly(this);
            SaveButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "Закрыть";
            StatusText.Text = "Режим просмотра. Для изменения используйте кнопку \"Редактировать\" в справочнике.";
        }

        private void AttachDepreciationCalculationHandlers()
        {
            InitialCostBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.BaseAmount);
            SalvageValueBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.BaseAmount);
            CarryingAmountBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.BaseAmount);
            UsefulLifeMonthsBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.UsefulLife);
            DepreciationRateBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Rate);
            MonthlyDepreciationBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Monthly);
            UseMileageDepreciationCheckBox.Checked += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Mileage);
            UseMileageDepreciationCheckBox.Unchecked += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.UsefulLife);
            StationaryClassRadio.Checked += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.UsefulLife);
            MobileClassRadio.Checked += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Mileage);
            MonthlyMileageBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Mileage);
            MileageResourceBox.TextChanged += (_, _) => RecalculateDepreciationFields(DepreciationCalculationSource.Mileage);
        }

        private void ApplyCalculatedFieldsReadOnly()
        {
            DepreciationRateBox.IsReadOnly = false;
            MonthlyDepreciationBox.IsReadOnly = false;
            DepreciationRateBox.Background = Brushes.White;
            MonthlyDepreciationBox.Background = Brushes.White;
            DepreciationRateBox.ToolTip = "Можно ввести вручную. R пересчитает норму по сроку полезного использования.";
            MonthlyDepreciationBox.ToolTip = "Можно ввести вручную. R пересчитает месячный износ по сроку или норме.";
        }

        private async Task EnsureGeneratedCodeAsync()
        {
            ApplyCodeReadOnly();

            if (_mode != FixedAssetCardMode.Create || !string.IsNullOrWhiteSpace(CodeBox.Text))
                return;

            CodeBox.Text = await GenerateNextFixedAssetCodeAsync();
        }

        private void ApplyCodeReadOnly()
        {
            CodeBox.IsReadOnly = true;
            CodeBox.Background = new SolidColorBrush(Color.FromRgb(238, 243, 247));
            CodeBox.ToolTip = "Код формируется автоматически и не редактируется вручную.";
        }

        private async Task<string> GenerateNextFixedAssetCodeAsync()
        {
            var codeField = FindField("code", "Код");
            var rows = await _metadataService.GetCatalogDataAsync(_catalog.Id);
            var existingCodes = rows
                .Select(row => GetFixedAssetCodeValue(row, codeField))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var numericCodes = existingCodes
                .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0)
                .Where(number => number > 0)
                .ToList();

            var nextNumber = numericCodes.Count == 0 ? 1 : numericCodes.Max() + 1;
            var usedCodes = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nextCode = nextNumber.ToString(CultureInfo.InvariantCulture);

            while (usedCodes.Contains(nextCode))
            {
                nextNumber++;
                nextCode = nextNumber.ToString(CultureInfo.InvariantCulture);
            }

            return nextCode;
        }

        private static string GetFixedAssetCodeValue(IReadOnlyDictionary<string, object> row, MetadataField? codeField)
        {
            var keys = new[]
                {
                    codeField?.Name,
                    codeField?.DbColumnName,
                    "Код",
                    "code",
                    "Code"
                }
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();

            var value = GetRowValue(row, keys);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        private void RecalculateDepreciationFields()
        {
            RecalculateDepreciationFields(DepreciationCalculationSource.Auto);
        }

        private void RecalculateDepreciationFields(DepreciationCalculationSource source)
        {
            if (_isUpdatingCalculatedFields)
                return;

            _isUpdatingCalculatedFields = true;
            try
            {
                var initialCost = ParseDecimalInput(InitialCostBox.Text);
                var carryingAmount = ParseDecimalInput(CarryingAmountBox.Text);
                var calculationCost = initialCost > 0 ? initialCost : carryingAmount;
                var salvageValue = ParseDecimalInput(SalvageValueBox.Text);
                var depreciableAmount = GetDepreciableAmount(calculationCost, salvageValue);

                if (depreciableAmount <= 0)
                {
                    DepreciationRateBox.Text = string.Empty;
                    MonthlyDepreciationBox.Text = string.Empty;
                    return;
                }

                if (source == DepreciationCalculationSource.Rate && RecalculateFromRate(depreciableAmount))
                    return;

                if (source == DepreciationCalculationSource.Monthly && RecalculateFromMonthly(depreciableAmount))
                    return;

                if (source == DepreciationCalculationSource.Mileage && RecalculateFromMileage(depreciableAmount))
                    return;

                if (source == DepreciationCalculationSource.BaseAmount && RecalculateFromExistingRateOrMonthly(depreciableAmount))
                    return;

                var usefulLifeYears = ParseIntInput(UsefulLifeMonthsBox.Text);
                if (usefulLifeYears > 0)
                {
                    DepreciationRateBox.Text = FormatMoney(100m / usefulLifeYears);
                    MonthlyDepreciationBox.Text = FormatMoney(depreciableAmount / (usefulLifeYears * 12m));
                    return;
                }

                RecalculateFromMileage(depreciableAmount);
            }
            finally
            {
                _isUpdatingCalculatedFields = false;
            }
        }

        private decimal GetDepreciableAmount(decimal calculationCost, decimal salvageValue)
        {
            var protectedResidualValue = calculationCost > 0
                ? Math.Min(Math.Max(0m, salvageValue), calculationCost)
                : 0m;
            return Math.Max(0m, calculationCost - protectedResidualValue);
        }

        private bool RecalculateFromExistingRateOrMonthly(decimal depreciableAmount)
        {
            var rate = ParseDecimalInput(DepreciationRateBox.Text);
            if (rate > 0)
                return RecalculateFromRate(depreciableAmount);

            var monthly = ParseDecimalInput(MonthlyDepreciationBox.Text);
            return monthly > 0 && RecalculateFromMonthly(depreciableAmount);
        }

        private bool RecalculateFromRate(decimal depreciableAmount)
        {
            var rate = ParseDecimalInput(DepreciationRateBox.Text);
            if (rate <= 0 || depreciableAmount <= 0)
                return false;

            MonthlyDepreciationBox.Text = FormatMoney(depreciableAmount * rate / 100m / 12m);
            SetUsefulLifeFromYears(100m / rate);
            return true;
        }

        private bool RecalculateFromMonthly(decimal depreciableAmount)
        {
            var monthly = ParseDecimalInput(MonthlyDepreciationBox.Text);
            if (monthly <= 0 || depreciableAmount <= 0)
                return false;

            DepreciationRateBox.Text = FormatMoney(monthly * 12m / depreciableAmount * 100m);
            SetUsefulLifeFromYears(depreciableAmount / (monthly * 12m));
            return true;
        }

        private bool RecalculateFromMileage(decimal depreciableAmount)
        {
            var useMileageDepreciation = UseMileageDepreciationCheckBox.IsChecked == true ||
                                         MobileClassRadio.IsChecked == true;
            var monthlyMileage = ParseDecimalInput(MonthlyMileageBox.Text);
            var mileageResource = ParseDecimalInput(MileageResourceBox.Text);
            if (!useMileageDepreciation || monthlyMileage <= 0 || mileageResource <= 0 || depreciableAmount <= 0)
                return false;

            var monthly = depreciableAmount * monthlyMileage / mileageResource;
            MonthlyDepreciationBox.Text = FormatMoney(monthly);
            DepreciationRateBox.Text = FormatMoney(monthly * 12m / depreciableAmount * 100m);
            return true;
        }

        private void SetUsefulLifeFromYears(decimal years)
        {
            if (years <= 0)
                return;

            var rounded = Math.Round(years, 0, MidpointRounding.AwayFromZero);
            if (rounded <= 0 || rounded > int.MaxValue)
                return;

            UsefulLifeMonthsBox.Text = ((int)rounded).ToString(CultureInfo.CurrentCulture);
        }

        private void ResetUsefulLife_Click(object sender, RoutedEventArgs e)
        {
            var source = ParseDecimalInput(DepreciationRateBox.Text) > 0
                ? DepreciationCalculationSource.Rate
                : DepreciationCalculationSource.Monthly;
            RecalculateDepreciationFields(source);
        }

        private void ResetDepreciationRate_Click(object sender, RoutedEventArgs e)
        {
            var source = ParseIntInput(UsefulLifeMonthsBox.Text) > 0
                ? DepreciationCalculationSource.UsefulLife
                : DepreciationCalculationSource.Monthly;
            RecalculateDepreciationFields(source);
        }

        private void ResetMonthlyDepreciation_Click(object sender, RoutedEventArgs e)
        {
            var source = ParseIntInput(UsefulLifeMonthsBox.Text) > 0
                ? DepreciationCalculationSource.UsefulLife
                : DepreciationCalculationSource.Rate;
            RecalculateDepreciationFields(source);
        }

        private void SetReadOnly(DependencyObject root)
        {
            if (root is TextBox textBox)
            {
                textBox.IsReadOnly = true;
                textBox.Background = new SolidColorBrush(Color.FromRgb(238, 243, 247));
            }
            else if (root is DatePicker datePicker)
            {
                datePicker.IsEnabled = false;
            }
            else if (root is CheckBox checkBox)
            {
                checkBox.IsEnabled = false;
            }
            else if (root is RadioButton radioButton)
            {
                radioButton.IsEnabled = false;
            }
            else if (root is Button button && button != CancelButton)
            {
                button.Visibility = Visibility.Collapsed;
            }

            var childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childrenCount; index++)
                SetReadOnly(VisualTreeHelper.GetChild(root, index));
        }

        private async void AssetTypePick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(AssetTypeDisplayBox, "asset_type_id", "Вид ОС", "Виды ОС");

        private async void AssetGroupPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(AssetGroupDisplayBox, "asset_group", "Группа ОС", "Группы ОС");

        private async void AssetSubgroupPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(AssetSubgroupDisplayBox, "asset_subgroup_id", "Категория ОС", "Подгруппа ОС", "Подгруппы ОС");

        private async void AssetAccountPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(AssetAccountDisplayBox, "asset_account", "Материальный счет", "Счет учета", "План счетов");

        private async void DepreciationAccountPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(DepreciationAccountDisplayBox, "depreciation_account", "Счет износа", "Счет амортизации", "План счетов");

        private async void ExpenseAccountPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(ExpenseAccountDisplayBox, "expense_account", "Счет списания затрат по амортизации", "Затратный счет", "План счетов");

        private async void SitePick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(SiteDisplayBox, "site_id", "Участок", "Участки");

        private async void ResponsiblePersonPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(ResponsiblePersonDisplayBox, "responsible_person_id", "МОЛ");

        private async void TaxGroupPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(TaxGroupDisplayBox, "tax_group", "Группа для налог декл", "Налоговая группа", "Налоговые группы ОС");

        private async void DepreciationMethodPick_Click(object sender, RoutedEventArgs e) =>
            await PickReferenceAsync(DepreciationMethodDisplayBox, "depreciation_method", "Метод амортизации", "Методы амортизации ОС");

        private async Task PickReferenceAsync(TextBox displayBox, params string[] aliases)
        {
            var (field, referenceCatalog) = ResolveReferenceContext(aliases);
            if (referenceCatalog == null)
            {
                MessageBox.Show("Для этого поля не найден справочник выбора.", "Основные средства", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = await _metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            var maps = await ReferenceDisplayHelper.LoadMapsAsync(referenceCatalog, _metadataService);
            var firstField = FindBestDisplayField(referenceCatalog, "Код", "code", "Счет", "account_number", "Табельный номер", "personnel_number", "Код участка", "site_code");
            var secondField = FindBestDisplayField(referenceCatalog, "Наименование", "name", "ФИО", "full_name", "Название", "Наименование участка", "site_name", "Описание", "description");

            var dialog = new ReferenceSelectionDialog(rows, firstField, secondField, maps)
            {
                Owner = this,
                Title = $"Выбор: {referenceCatalog.Name}"
            };

            if (dialog.ShowDialog() != true || dialog.SelectedItem == null)
                return;

            var selected = dialog.SelectedItem;
            var selectedId = GetRowValue(selected, "Id");
            if (field != null)
                StorePickerValue(field, selectedId);

            displayBox.Text = BuildReferenceText(selected, referenceCatalog, field);
            ApplyReferenceSideEffects(field, selected);
        }

        private async Task AddReferenceAsync(TextBox displayBox, params string[] aliases)
        {
            var (field, referenceCatalog) = ResolveReferenceContext(aliases);
            if (referenceCatalog == null)
            {
                MessageBox.Show("Для этого поля не найден справочник выбора.", "Основные средства", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new CatalogItemDialog(referenceCatalog, _metadataService)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            var createdId = await _metadataService.CreateDynamicRecordAsync(referenceCatalog.Id, dialog.ItemData);
            if (field != null)
                StorePickerValue(field, createdId);

            await SetSelectedReferenceAsync(displayBox, referenceCatalog, field, createdId);
        }

        private async Task EditReferenceAsync(TextBox displayBox, params string[] aliases)
        {
            var (field, referenceCatalog) = ResolveReferenceContext(aliases);
            if (referenceCatalog == null)
            {
                MessageBox.Show("Для этого поля не найден справочник выбора.", "Основные средства", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedId = GetSelectedReferenceId(field);
            if (!selectedId.HasValue)
            {
                MessageBox.Show("Сначала выберите запись справочника.", referenceCatalog.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rows = await _metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            var selected = rows.FirstOrDefault(row => SameValue(GetRowValue(row, "Id"), selectedId.Value.ToString()));
            if (selected == null)
            {
                MessageBox.Show("Выбранная запись справочника не найдена.", referenceCatalog.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new CatalogItemDialog(referenceCatalog, _metadataService, selected)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
                return;

            await _metadataService.UpdateDynamicRecordAsync(referenceCatalog.Id, selectedId.Value, dialog.ItemData);
            await SetSelectedReferenceAsync(displayBox, referenceCatalog, field, selectedId.Value);
        }

        private (MetadataField? Field, MetadataObject? Catalog) ResolveReferenceContext(params string[] aliases)
        {
            var field = FindField(aliases);
            var referenceName = field?.ReferenceCatalog;
            if (string.IsNullOrWhiteSpace(referenceName))
                referenceName = aliases.LastOrDefault(alias => _catalogsByName.ContainsKey(alias));

            return !string.IsNullOrWhiteSpace(referenceName) && _catalogsByName.TryGetValue(referenceName, out var referenceCatalog)
                ? (field, referenceCatalog)
                : (field, null);
        }

        private async Task SetSelectedReferenceAsync(TextBox displayBox, MetadataObject referenceCatalog, MetadataField? field, object? rawValue)
        {
            var rows = await _metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            var row = FindReferenceRow(rows, rawValue);
            displayBox.Text = row == null
                ? Convert.ToString(rawValue, CultureInfo.CurrentCulture) ?? string.Empty
                : BuildReferenceText(row, referenceCatalog, field);

            if (field != null)
                StorePickerValue(field, rawValue);
            if (row != null)
                ApplyReferenceSideEffects(field, row);
        }

        private Guid? GetSelectedReferenceId(MetadataField? field)
        {
            object? raw = null;
            if (field != null)
            {
                if (!string.IsNullOrWhiteSpace(field.DbColumnName) && _pickerValues.TryGetValue(field.DbColumnName, out var byColumn))
                    raw = byColumn;
                else if (!string.IsNullOrWhiteSpace(field.Name) && _pickerValues.TryGetValue(field.Name, out var byName))
                    raw = byName;
                else
                    raw = RawValue(field.DbColumnName, field.Name);
            }

            return Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var id)
                ? id
                : null;
        }

        private static Dictionary<string, object>? FindReferenceRow(IEnumerable<Dictionary<string, object>> rows, object? rawValue)
        {
            if (IsEmpty(rawValue))
                return null;

            var rawText = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
            return rows.FirstOrDefault(item => SameValue(GetRowValue(item, "Id", "id"), rawText))
                   ?? rows.FirstOrDefault(item => SameValue(GetRowValue(item, "Код", "code", "Счет", "account_number", "Код участка", "site_code"), rawText));
        }

        private void ApplyReferenceSideEffects(MetadataField? field, Dictionary<string, object> row)
        {
            var fieldKey = NormalizeIdentifier(field?.DbColumnName ?? field?.Name);
            if (fieldKey is not ("asset_type_id" or "asset_group" or "asset_subgroup_id"))
                return;

            var usefulLife = GetRowValue(row, "Срок использования, лет", "useful_life_months", "Срок полезного использования");
            if (!IsEmpty(usefulLife))
                UsefulLifeMonthsBox.Text = Convert.ToString(usefulLife, CultureInfo.CurrentCulture) ?? string.Empty;

            RecalculateDepreciationFields();
        }

        private async Task SetReferenceDisplayAsync(TextBox displayBox, params string[] aliases)
        {
            var field = FindField(aliases);
            if (field == null)
            {
                displayBox.Text = TextValue(aliases);
                return;
            }

            var raw = RawValue(aliases.Concat(new[] { field.Name, field.DbColumnName }).ToArray());
            StorePickerValue(field, NormalizeDbValue(raw));

            if (IsEmpty(raw))
            {
                displayBox.Text = string.Empty;
                return;
            }

            var referenceName = field.ReferenceCatalog;
            if (string.IsNullOrWhiteSpace(referenceName))
                referenceName = aliases.LastOrDefault(alias => _catalogsByName.ContainsKey(alias));

            if (string.IsNullOrWhiteSpace(referenceName))
            {
                displayBox.Text = Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
                return;
            }

            if (!_catalogsByName.TryGetValue(referenceName, out var referenceCatalog))
            {
                displayBox.Text = Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
                return;
            }

            try
            {
                var rows = await _metadataService.GetCatalogDataAsync(referenceCatalog.Id);
                var row = FindReferenceRow(rows, raw);

                displayBox.Text = row == null
                    ? Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty
                    : BuildReferenceText(row, referenceCatalog, field);
                if (row != null)
                    ApplyReferenceSideEffects(field, row);
            }
            catch
            {
                displayBox.Text = Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
            }
        }

        private void StorePickerValue(MetadataField field, object? value)
        {
            var normalized = NormalizeDbValue(value);
            if (!string.IsNullOrWhiteSpace(field.Name))
                _pickerValues[field.Name] = normalized;
            if (!string.IsNullOrWhiteSpace(field.DbColumnName))
                _pickerValues[field.DbColumnName] = normalized;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BuildItemData();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Основные средства", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BuildItemData()
        {
            ItemData.Clear();
            RecalculateDepreciationFields();

            PutRequiredText("code", CodeBox.Text, "Код");
            PutRequiredText("inventory_number", InventoryNumberBox.Text, "Инвентарный номер");
            PutRequiredText("name", NameBox.Text, "Наименование");
            PutInt("manufacture_year", ManufactureYearBox.Text);
            PutBool("use_code_as_inventory_number", UseCodeAsInventoryNumberCheckBox.IsChecked == true);

            PutReference("asset_type_id");
            PutReference("asset_group");
            PutReference("asset_subgroup_id");
            PutReference("asset_account");
            PutReference("depreciation_account");
            PutReference("expense_account");
            PutReference("site_id");
            PutReference("responsible_person_id");
            PutReference("tax_group");
            PutReference("depreciation_method");

            PutDate("acquisition_date", AcquisitionDatePicker.SelectedDate);
            PutText("acquisition_document_number", AcquisitionDocumentBox.Text);
            PutDate("commissioning_date", CommissioningDatePicker.SelectedDate);
            PutText("commissioning_document_number", CommissioningDocumentBox.Text);
            PutDate("depreciation_start_date", DepreciationStartDatePicker.SelectedDate);

            PutDecimal("initial_cost", InitialCostBox.Text);
            PutDecimal("salvage_value", SalvageValueBox.Text);
            PutDecimal("accumulated_depreciation", AccumulatedDepreciationBox.Text);
            PutDecimal("carrying_amount", CarryingAmountBox.Text);
            PutInt("useful_life_months", UsefulLifeMonthsBox.Text);
            PutText("depreciation_code", DepreciationCodeBox.Text);
            PutDecimal("depreciation_rate", DepreciationRateBox.Text);
            PutDecimal("monthly_depreciation", MonthlyDepreciationBox.Text);
            PutBool("use_mileage_depreciation", UseMileageDepreciationCheckBox.IsChecked == true);
            PutDecimal("monthly_mileage", MonthlyMileageBox.Text);
            PutDecimal("mileage_resource", MileageResourceBox.Text);
            PutText("description", DescriptionBox.Text);
            PutIntValue("asset_class", MobileClassRadio.IsChecked == true ? 2 : 1);

            if (_mode == FixedAssetCardMode.Create)
                PutBool("is_active", true);
            else if (FindField("is_active") != null)
                PutBool("is_active", BoolValue("is_active", "Активен") ?? true);
        }

        private void PutRequiredText(string dbColumnName, string value, string displayName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Поле '{displayName}' обязательно для заполнения.");

            PutText(dbColumnName, value);
        }

        private void PutText(string dbColumnName, string value)
        {
            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = value?.Trim() ?? string.Empty;
        }

        private void PutBool(string dbColumnName, bool value)
        {
            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = value;
        }

        private void PutDate(string dbColumnName, DateTime? value)
        {
            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = value;
        }

        private void PutInt(string dbColumnName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                PutNullable(dbColumnName, null);
                return;
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
                throw new InvalidOperationException($"Поле '{DisplayName(dbColumnName)}' должно быть целым числом.");

            PutIntValue(dbColumnName, parsed);
        }

        private void PutIntValue(string dbColumnName, int value)
        {
            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = value;
        }

        private void PutDecimal(string dbColumnName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                PutNullable(dbColumnName, null);
                return;
            }

            var normalized = value.Trim().Replace(" ", string.Empty);
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) &&
                !decimal.TryParse(normalized.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
            {
                throw new InvalidOperationException($"Поле '{DisplayName(dbColumnName)}' должно быть числом.");
            }

            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = parsed;
        }

        private void PutNullable(string dbColumnName, object? value)
        {
            var field = FindField(dbColumnName);
            if (field != null)
                ItemData[field.Name] = value ?? DBNull.Value;
        }

        private void PutReference(string dbColumnName)
        {
            var field = FindField(dbColumnName);
            if (field == null)
                return;

            var raw = _pickerValues.TryGetValue(field.DbColumnName, out var byColumn)
                ? byColumn
                : _pickerValues.TryGetValue(field.Name, out var byName)
                    ? byName
                    : NormalizeDbValue(RawValue(field.DbColumnName, field.Name));

            ItemData[field.Name] = raw ?? DBNull.Value;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == FixedAssetCardMode.Details)
            {
                Close();
                return;
            }

            DialogResult = false;
        }

        private void DevelopmentButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Раздел в разработке.", "Основные средства", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private MetadataField? FindField(params string?[] identifiers)
        {
            var aliases = identifiers
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                .Select(NormalizeIdentifier)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _catalog.Fields.FirstOrDefault(field =>
                aliases.Contains(NormalizeIdentifier(field.DbColumnName)) ||
                aliases.Contains(NormalizeIdentifier(field.Name)));
        }

        private string DisplayName(string dbColumnName) => FindField(dbColumnName)?.Name ?? dbColumnName;

        private object? RawValue(params string?[] identifiers)
        {
            foreach (var identifier in identifiers.Where(identifier => !string.IsNullOrWhiteSpace(identifier)))
            {
                if (TryGetValue(_existingData, identifier!, out var direct))
                    return direct;
            }

            var field = FindField(identifiers);
            if (field == null)
                return null;

            if (TryGetValue(_existingData, field.Name, out var byName))
                return byName;
            if (!string.IsNullOrWhiteSpace(field.DbColumnName) && TryGetValue(_existingData, field.DbColumnName, out var byColumn))
                return byColumn;

            return null;
        }

        private string TextValue(params string?[] identifiers)
        {
            var raw = RawValue(identifiers);
            if (IsEmpty(raw))
                return string.Empty;

            return Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        private string DecimalText(params string?[] identifiers)
        {
            var raw = RawValue(identifiers);
            if (IsEmpty(raw))
                return string.Empty;

            if (decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantDecimal))
                return invariantDecimal.ToString("0.##", CultureInfo.CurrentCulture);

            if (decimal.TryParse(Convert.ToString(raw, CultureInfo.CurrentCulture), NumberStyles.Number, CultureInfo.CurrentCulture, out var localDecimal))
                return localDecimal.ToString("0.##", CultureInfo.CurrentCulture);

            return Convert.ToString(raw, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        private static decimal ParseDecimalInput(string? value)
        {
            var normalized = value?.Trim().Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return 0m;

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.CurrentCulture, out var localDecimal))
                return localDecimal;

            if (decimal.TryParse(normalized.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantDecimal))
                return invariantDecimal;

            return 0m;
        }

        private static int ParseIntInput(string? value)
        {
            return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var result)
                ? result
                : 0;
        }

        private static string FormatMoney(decimal value) =>
            Math.Round(value, 2).ToString("0.00", CultureInfo.CurrentCulture);

        private DateTime? DateValue(params string?[] identifiers)
        {
            var raw = RawValue(identifiers);
            if (IsEmpty(raw))
                return null;

            if (raw is DateTime date)
                return date;
            if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
                return parsed;

            return null;
        }

        private bool? BoolValue(params string?[] identifiers)
        {
            var raw = RawValue(identifiers);
            if (IsEmpty(raw))
                return null;

            if (raw is bool flag)
                return flag;
            if (bool.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed))
                return parsed;

            var text = Convert.ToString(raw, CultureInfo.CurrentCulture)?.Trim();
            return text switch
            {
                "Да" or "+" or "1" => true,
                "Нет" or "-" or "0" => false,
                _ => null
            };
        }

        private static bool TryGetValue(IReadOnlyDictionary<string, object> data, string key, out object? value)
        {
            if (data.TryGetValue(key, out var direct))
            {
                value = direct;
                return true;
            }

            foreach (var pair in data)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object? NormalizeDbValue(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (value is Guid guid)
                return guid;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (Guid.TryParse(text, out var parsedGuid))
                return parsedGuid;

            return text;
        }

        private static object? GetRowValue(IReadOnlyDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && value != DBNull.Value)
                    return value;

                var pair = row.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != DBNull.Value)
                    return pair.Value;
            }

            return null;
        }

        private static bool SameValue(object? left, string? rightText)
        {
            if (left == null || string.IsNullOrWhiteSpace(rightText))
                return false;

            return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), rightText, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildReferenceText(Dictionary<string, object> row, MetadataObject referenceCatalog, MetadataField? field)
        {
            if (field != null)
            {
                var resolved = ReferenceDisplayHelper.BuildDisplayValue(row, field);
                if (!string.IsNullOrWhiteSpace(resolved) && !LooksLikeRawId(resolved))
                    return resolved;
            }

            var code = Convert.ToString(GetRowValue(row, "Код", "code", "Счет", "account_number", "Табельный номер", "personnel_number", "Код участка", "site_code"), CultureInfo.CurrentCulture);
            var name = Convert.ToString(GetRowValue(row, "Наименование", "name", "ФИО", "full_name", "Название", "Наименование участка", "site_name", "Описание", "description"), CultureInfo.CurrentCulture);

            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
                return $"{code} - {name}";
            if (!string.IsNullOrWhiteSpace(name))
                return name!;
            if (!string.IsNullOrWhiteSpace(code))
                return code!;

            return Convert.ToString(GetRowValue(row, "Id"), CultureInfo.CurrentCulture) ?? referenceCatalog.Name;
        }

        private static bool LooksLikeRawId(string value) => Guid.TryParse(value.Trim(), out _);

        private static string? FindBestDisplayField(MetadataObject catalog, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var field = catalog.Fields.FirstOrDefault(item =>
                    string.Equals(item.Name, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DbColumnName, candidate, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                    return field.Name;
            }

            return catalog.Fields.OrderBy(item => item.Order).FirstOrDefault()?.Name;
        }

        private static bool IsEmpty(object? value)
        {
            return value == null || value == DBNull.Value || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static string NormalizeIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim()
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace("№", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }
}