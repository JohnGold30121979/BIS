using BIS.ERP.Models;
using BIS.ERP.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class InfoBaseModulesDialog : Window
    {
        private readonly InfoBase _infoBase;
        private readonly InfoBaseManager _manager = new();
        private readonly ObservableCollection<InfoBaseModuleAvailability> _modules = new();

        public InfoBaseModulesDialog(InfoBase infoBase)
        {
            InitializeComponent();
            _infoBase = infoBase;
            InfoBaseNameText.Text = $"Информационная база: {infoBase.Name}";
            ModulesGrid.ItemsSource = _modules;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            LoadingProgress.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
            StatusText.Text = "Загрузка модулей...";

            try
            {
                var modules = await _manager.GetModuleAvailabilityAsync(_infoBase.Id);
                _modules.Clear();
                foreach (var module in modules)
                    _modules.Add(module);

                StatusText.Text = $"Модулей в конфигурации: {_modules.Count}.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка загрузки модулей.";
                MessageBox.Show(ex.Message, "Модули информационной базы", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                LoadingProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnFinanceOnlyClick(object sender, RoutedEventArgs e)
        {
            foreach (var module in _modules)
                module.IsActive = module.Code.Equals(ModuleMetadataService.FinanceCode, StringComparison.OrdinalIgnoreCase);

            ModulesGrid.Items.Refresh();
            StatusText.Text = "Оставлен только модуль «Финансы».";
        }

        private void OnEnableAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var module in _modules)
                module.IsActive = true;

            ModulesGrid.Items.Refresh();
            StatusText.Text = "Все модули отмечены как доступные.";
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            LoadingProgress.Visibility = Visibility.Visible;
            StatusText.Text = "Сохранение...";

            try
            {
                await _manager.SaveModuleAvailabilityAsync(_infoBase.Id, _modules);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Модули информационной базы", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SaveButton.IsEnabled = true;
                LoadingProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
