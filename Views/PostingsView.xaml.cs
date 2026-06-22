using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class PostingsView : UserControl
    {
        private readonly MetadataObject _document;
        private readonly MetadataService _metadataService;
        private ObservableCollection<Dictionary<string, object>> _postings;

        public PostingsView(MetadataObject document, MetadataService metadataService)
        {
            InitializeComponent();
            _document = document;
            _metadataService = metadataService;
            _postings = new ObservableCollection<Dictionary<string, object>>();
            PostingsGrid.ItemsSource = _postings;

            // Привязываем горячие клавиши
            this.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private async Task LoadData()
        {
            try
            {
                StatusText.Text = "Загрузка...";
                var data = await _metadataService.GetCatalogDataAsync(_document.Id);
                var accountAnalytics = await AccountAnalyticsRegistry.LoadAsync(_metadataService);
                var referenceMaps = await ReferenceDisplayHelper.LoadMapsAsync(_document, _metadataService);
                var displayData = ReferenceDisplayHelper.ResolveRows(data, referenceMaps);

                _postings.Clear();

                // Для отладки - выводим ключи
                if (displayData.Any())
                {
                    var firstRow = displayData.First();
                    System.Diagnostics.Debug.WriteLine("=== Ключи в данных ===");
                    foreach (var key in firstRow.Keys)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {key}");
                    }
                }

                foreach (var row in displayData.OrderByDescending(r => r.GetValueOrDefault("Дата")))
                {
                    _postings.Add(row);
                }

                UpdateAnalyticColumns(data, accountAnalytics);

                StatusText.Text = $"📊 Загружено проводок: {_postings.Count}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateAnalyticColumns(
            List<Dictionary<string, object>> rawRows,
            AccountAnalyticsRegistry accountAnalytics)
        {
            var accountFields = new[] { "Дебет", "Кредит" };
            var showCurrency = AccountAnalyticsRules.ShouldShowFieldForRows(
                "Валюта", rawRows, accountFields, accountAnalytics, "Справочник валют");

            AmountCurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            CurrencyColumn.Visibility = showCurrency ? Visibility.Visible : Visibility.Collapsed;
            OrganizationColumn.Visibility = GetAnalyticColumnVisibility(
                "Организация", "Организации", rawRows, accountFields, accountAnalytics);
            EmployeeColumn.Visibility = GetAnalyticColumnVisibility(
                "Сотрудник", "Сотрудники (Списочный состав)", rawRows, accountFields, accountAnalytics);
            MaterialColumn.Visibility = GetAnalyticColumnVisibility(
                "Материал", "Справочник материалов", rawRows, accountFields, accountAnalytics);
        }

        private static Visibility GetAnalyticColumnVisibility(
            string fieldName,
            string referenceCatalog,
            List<Dictionary<string, object>> rows,
            IEnumerable<string> accountFields,
            AccountAnalyticsRegistry registry)
        {
            return AccountAnalyticsRules.ShouldShowFieldForRows(
                    fieldName, rows, accountFields, registry, referenceCatalog)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void PostingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PostingsGrid.SelectedItem != null;
            EditButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }

        private async void OnAddClick(object sender, RoutedEventArgs e)
        {
            var dialog = new PostingEditDialog(_document, _metadataService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                await LoadData();
            }
        }

        private async void OnEditClick(object sender, RoutedEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as Dictionary<string, object>;
            if (selected == null) return;

            if (selected.ContainsKey("Id") && selected["Id"] != null)
            {
                var id = Guid.Parse(selected["Id"].ToString());
                var dialog = new PostingEditDialog(_document, _metadataService, id);
                dialog.Owner = Window.GetWindow(this);

                if (dialog.ShowDialog() == true)
                {
                    await LoadData();
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var selected = PostingsGrid.SelectedItem as Dictionary<string, object>;
            if (selected == null) return;

            var result = MessageBox.Show("Удалить проводку?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (selected.ContainsKey("Id") && selected["Id"] != null)
                {
                    var id = Guid.Parse(selected["Id"].ToString());
                    await _metadataService.DeleteDynamicRecordAsync(_document.Id, id);
                    await LoadData();
                }
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadData();
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            // Открыть окно поиска
            var searchDialog = new PostingSearchDialog(_postings.ToList());
            searchDialog.Owner = Window.GetWindow(this);
            if (searchDialog.ShowDialog() == true && searchDialog.SelectedPosting != null)
            {
                PostingsGrid.SelectedItem = searchDialog.SelectedPosting;
                PostingsGrid.ScrollIntoView(searchDialog.SelectedPosting);
            }
        }
    }
}
