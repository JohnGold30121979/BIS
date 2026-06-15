using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CashOrderDialog : Window
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private readonly Guid? _editId;
        private Guid _selectedCorrAccountId;

        public CashOrderDialog(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = null;
            DialogTitle.Text = $"Добавление: {document.Name}";
            NumberBox.Text = Guid.NewGuid().ToString().Substring(0, 8);
            DatePicker.SelectedDate = DateTime.Today;
            Loaded += async (s, e) => await LoadReferenceData();
        }

        public CashOrderDialog(MetadataObject document, MetadataService metadataService, Guid editId)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _editId = editId;
            DialogTitle.Text = $"Редактирование: {document.Name}";
            Loaded += async (s, e) => await LoadData(editId);
        }

        private async Task LoadReferenceData()
        {
            var allCatalogs = await _metadataService.GetCatalogsAsync();

            // Загружаем кассы
            var cashDesks = allCatalogs.FirstOrDefault(c => c.Name == "Кассы");
            if (cashDesks != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(cashDesks.Id);
                CashDeskCombo.ItemsSource = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                });
            }

            // Загружаем организации
            var orgs = allCatalogs.FirstOrDefault(c => c.Name == "Организации");
            if (orgs != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(orgs.Id);
                OrganizationCombo.ItemsSource = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                });
            }

            // Загружаем контрагентов
            var contractors = allCatalogs.FirstOrDefault(c => c.Name == "Контрагенты");
            if (contractors != null)
            {
                var data = await _metadataService.GetCatalogDataAsync(contractors.Id);
                ContractorCombo.ItemsSource = data.Select(d => new ReferenceItem
                {
                    Id = Guid.Parse(d["Id"].ToString()),
                    DisplayName = d["Наименование"].ToString()
                });
            }
        }

        private async Task LoadData(Guid id)
        {
            await LoadReferenceData();
            var data = await _metadataService.GetCatalogDataAsync(_document.Id);
            var record = data.FirstOrDefault(r => r["Id"].ToString() == id.ToString());
            if (record != null)
            {
                NumberBox.Text = record.ContainsKey("Номер") ? record["Номер"].ToString() : "";
                if (record.ContainsKey("Дата") && record["Дата"] is DateTime dt)
                    DatePicker.SelectedDate = dt;
                if (record.ContainsKey("Сумма"))
                    AmountBox.Text = record["Сумма"].ToString();
                if (record.ContainsKey("Основание"))
                    BasisBox.Text = record["Основание"].ToString();
                if (record.ContainsKey("Примечание"))
                    DescriptionBox.Text = record["Примечание"].ToString();
            }
        }

        private async void SelectAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Получаем все справочники
                var allCatalogs = await _metadataService.GetCatalogsAsync();

                // Ищем справочник "План счетов" (название может начинаться с "План счетов")
                var chartCatalog = allCatalogs.FirstOrDefault(c => c.Name.StartsWith("План счетов"));

                if (chartCatalog == null)
                {
                    MessageBox.Show("План счетов не найден! Сначала создайте справочник 'План счетов'.",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Получаем данные счетов
                var accountsData = await _metadataService.GetCatalogDataAsync(chartCatalog.Id);

                if (accountsData == null || accountsData.Count == 0)
                {
                    MessageBox.Show("В плане счетов нет данных! Добавьте счета в справочник 'План счетов'.",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Открываем диалог выбора счета
                var dialog = new AccountSelectionDialog(accountsData);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true && dialog.SelectedAccount != null)
                {
                    // Получаем код и наименование счета
                    var accountCode = dialog.SelectedAccount.ContainsKey("Код") ? dialog.SelectedAccount["Код"].ToString() : "";
                    var accountName = dialog.SelectedAccount.ContainsKey("Наименование") ? dialog.SelectedAccount["Наименование"].ToString() : "";

                    // Отображаем в текстовом поле
                    CorrAccountBox.Text = $"{accountCode} - {accountName}";

                    // Сохраняем ID счета
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
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var itemData = new Dictionary<string, object>
                {
                    ["Номер"] = NumberBox.Text,
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
                {
                    await _metadataService.UpdateDynamicRecordAsync(_document.Id, _editId.Value, itemData);
                }
                else
                {
                    await _metadataService.CreateDynamicRecordAsync(_document.Id, itemData);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}");
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}