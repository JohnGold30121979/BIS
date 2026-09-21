using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Controls;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Выбор счёта из плана счетов.
    /// UserControl (аналогично CatalogDataView): размещается в MDI как документ
    /// через MdiDialogService.ShowControlInWorkspaceForResultAsync, при отсутствии
    /// MDI — в отдельном модальном окне.
    /// Добавление/редактирование счёта выполняется штатным механизмом каталога
    /// «План счетов» (CatalogItemDialog + MetadataService) — тем же, что и в
    /// разделе «План счетов» → Добавить/Редактировать; отдельных редакторов нет.
    /// </summary>
    public partial class AccountSelectionView : UserControl
    {
        private const string ChartOfAccountsCatalogName = "План счетов";

        public Dictionary<string, object> SelectedAccount { get; private set; }
        private List<Dictionary<string, object>> _accounts;

        public AccountSelectionView(List<Dictionary<string, object>> accounts)
        {
            InitializeComponent();
            _accounts = accounts ?? new List<Dictionary<string, object>>();

            // Используем контрол для поиска и отображения
            AccountsView.SetData(_accounts);
            EditButton.IsEnabled = false;
            AccountsView.SelectionChanged += AccountsView_SelectionChanged;
            AccountsView.RowDoubleClick += AccountsView_RowDoubleClick;
        }

        private void AccountsView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            EditButton.IsEnabled = AccountsView.GetSelectedRow() != null;
        }

        // «Добавить» — тот же механизм, что «План счетов» → Добавить:
        // CatalogItemDialog по каталогу «План счетов» + CreateDynamicRecordAsync.
        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            // Метаданные должны работать с базой ТЕКУЩЕЙ информационной базы:
            // справочник «План счетов» существует именно там, а не в мастер-БД.
            var metadata = await CreateCurrentInfoBaseMetadataServiceAsync();
            if (metadata == null)
                return;

            var catalog = await TryGetChartOfAccountsCatalogAsync(metadata);
            if (catalog == null)
            {
                MessageBox.Show($"Справочник «{ChartOfAccountsCatalogName}» не найден.", ChartOfAccountsCatalogName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new CatalogItemDialog(catalog, metadata);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this), dialog, $"Добавление: {catalog.Name}") != true)
                    return;

                await metadata.CreateDynamicRecordAsync(catalog.Id, dialog.ItemData);
                await ReloadAccountsFromCatalogAsync(metadata, catalog);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка добавления счёта: {ex.Message}", ChartOfAccountsCatalogName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // «Изменить счёт» — тот же механизм, что «План счетов» → Редактировать:
        // CatalogItemDialog со свежими данными записи + UpdateDynamicRecordAsync.
        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var drv = AccountsView.GetSelectedRow();
            if (drv == null)
                return;

            var selectedId = drv["Id"]?.ToString();
            var original = FindAccountById(selectedId);
            if (original == null)
                return;

            // Тот же принцип, что и в «Добавить»: сервис метаданных —
            // от текущей информационной базы, а не от мастер-БД.
            var metadata = await CreateCurrentInfoBaseMetadataServiceAsync();
            if (metadata == null)
                return;

            var catalog = await TryGetChartOfAccountsCatalogAsync(metadata);
            if (catalog == null)
            {
                MessageBox.Show($"Справочник «{ChartOfAccountsCatalogName}» не найден.", ChartOfAccountsCatalogName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recordId = TryParseRecordId(original);
            if (recordId == null)
            {
                MessageBox.Show("У выбранной записи не найден идентификатор.", ChartOfAccountsCatalogName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Всегда берём свежие «сырые» данные из БД, чтобы поля-ссылки
                // не оказались подменены отображаемыми значениями при сохранении
                // (тот же подход, что в ReferenceSelectionDialog.OnEditClick).
                var rawRows = await metadata.GetCatalogDataAsync(catalog.Id);
                var existingData = rawRows?.FirstOrDefault(row =>
                    string.Equals(row.GetValueOrDefault("Id")?.ToString(), selectedId, StringComparison.OrdinalIgnoreCase));
                if (existingData == null)
                {
                    MessageBox.Show("Выбранная запись справочника не найдена.", catalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dialog = new CatalogItemDialog(catalog, metadata, existingData);
                if (await MdiDialogService.ShowInWorkspaceForResultAsync(
                        Window.GetWindow(this), dialog, $"Редактирование: {catalog.Name}") != true)
                    return;

                await metadata.UpdateDynamicRecordAsync(catalog.Id, recordId.Value, dialog.ItemData);
                await ReloadAccountsFromCatalogAsync(metadata, catalog);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка обновления счёта: {ex.Message}", ChartOfAccountsCatalogName,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Ранее здесь использовался DI-сервис из App.Services, который в App.OnStartup
        // создаётся от мастер-БД (AppSettings.GetMasterConnectionString). Из-за этого
        // GetCatalogsAsync искал каталог «План счетов» не в той базе, и появлялось
        // сообщение «Справочник «План счетов» не найден». Теперь сервис создаётся
        // от контекста текущей информационной базы — так же, как во всех остальных
        // разделах (MainWorkWindow, AccountingSetupView, диалоги документов).
        private static async Task<MetadataService?> CreateCurrentInfoBaseMetadataServiceAsync()
        {
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                return new MetadataService(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть текущую информационную базу: {ex.Message}",
                    ChartOfAccountsCatalogName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        private static async Task<MetadataObject?> TryGetChartOfAccountsCatalogAsync(MetadataService metadata)
        {
            var catalogs = await metadata.GetCatalogsAsync();
            return catalogs?.FirstOrDefault(item =>
                string.Equals(item.ObjectType, "Catalog", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Name, ChartOfAccountsCatalogName, StringComparison.OrdinalIgnoreCase));
        }

        // Перезагрузка из БД: список счетов всегда соответствует справочнику
        // «План счетов», как и в самом разделе «План счетов».
        private async Task ReloadAccountsFromCatalogAsync(MetadataService metadata, MetadataObject catalog)
        {
            var rows = await metadata.GetCatalogDataAsync(catalog.Id) ?? new List<Dictionary<string, object>>();

            var previousId = AccountsView.GetSelectedId();
            _accounts = rows;
            AccountsView.SetData(_accounts);

            if (!string.IsNullOrEmpty(previousId))
                AccountsView.SelectRowById(previousId);
        }

        private Dictionary<string, object>? FindAccountById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            return _accounts.FirstOrDefault(account =>
                string.Equals(account.GetValueOrDefault("Id")?.ToString(), id, StringComparison.OrdinalIgnoreCase));
        }

        private static Guid? TryParseRecordId(Dictionary<string, object> row)
        {
            return row.TryGetValue("Id", out var value) && Guid.TryParse(value?.ToString(), out var id)
                ? id
                : (Guid?)null;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            MdiDialogService.CloseWithResult(this, false);
        }

        private void AccountsView_RowDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            SelectCurrentAccount();
        }

        private void SelectCurrentAccount()
        {
            var drv = AccountsView.GetSelectedRow();
            if (drv == null)
                return;

            var id = drv["Id"]?.ToString();
            var acc = _accounts.FirstOrDefault(a => string.Equals(a.GetValueOrDefault("Id")?.ToString(), id, StringComparison.OrdinalIgnoreCase));
            if (acc == null)
                return;

            SelectedAccount = acc;
            MdiDialogService.CloseWithResult(this, true);
        }
    }
}
