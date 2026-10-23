using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    /// <summary>
    /// Окно «Закрытие периодов»: сбор информации за период, итоговое закрытие баланса
    /// и закрытие/открытие рабочих модулей. Ранее эти элементы жили в
    /// <see cref="AccountingReportsView"/> и мешали работе с отчётами.
    /// </summary>
    public partial class PeriodClosingView : UserControl
    {
        private readonly AccountingPeriodService _periodService;
        private AccountingPeriod? _currentPeriod;
        private List<AccountingPeriodModuleStatus> _currentModuleStates = new();
        private bool _isLoading;

        public PeriodClosingView(AppDbContext context)
        {
            InitializeComponent();

            _periodService = new AccountingPeriodService(ServiceLocator.DbContextFactory);

            var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
            _isLoading = true;
            StartDatePicker.SelectedDate = yearStart;
            EndDatePicker.SelectedDate = DateTime.Today;
            _isLoading = false;

            Loaded += async (_, _) => await RefreshAsync();
        }

        private async void OnPeriodDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading)
                return;

            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                var (start, end) = GetPeriod();
                await UpdatePeriodStateAsync(start, end);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private async void OnCollectPeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.CollectAsync(start, end);
                await UpdatePeriodStateAsync(start, end);
                StatusText.Text = "Информация за период собрана и контрольные остатки сохранены";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Сбор периода", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnTogglePeriodClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var (start, end) = GetPeriod();
                _currentPeriod = await _periodService.FindAsync(start, end);
                if (_currentPeriod == null)
                    throw new InvalidOperationException("Сначала выполните сбор информации за период.");

                if (_currentPeriod.IsLocked)
                {
                    if (MessageBox.Show("Открыть период для изменения документов?", "Учетный период",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    await _periodService.ReopenAsync(_currentPeriod.Id);
                }
                else
                {
                    if (MessageBox.Show(
                            "Выполнить итоговое закрытие баланса? После этого документы с датами выбранного периода нельзя будет изменять.",
                            "Итоговое закрытие баланса", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    await _periodService.CloseAsync(_currentPeriod.Id);
                }

                await UpdatePeriodStateAsync(start, end);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Учетный период", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task UpdatePeriodStateAsync(DateTime start, DateTime end)
        {
            _currentPeriod = await _periodService.FindAsync(start, end);
            PeriodStatusText.Text = _currentPeriod == null
                ? "Период не собран"
                : _currentPeriod.IsLocked
                    ? "Статус: итоговый баланс закрыт"
                    : $"Статус: {LocalizationService.DisplayValue(_currentPeriod.Status)}";
            PeriodStateButton.Content = _currentPeriod?.IsLocked == true ? "Открыть баланс" : "Закрыть баланс";
            await LoadPeriodModuleStatesAsync();
        }

        private async Task LoadPeriodModuleStatesAsync()
        {
            _currentModuleStates.Clear();
            PeriodModuleCombo.ItemsSource = null;

            if (_currentPeriod == null)
            {
                PeriodModuleCombo.IsEnabled = false;
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = "Модули периода недоступны";
                return;
            }

            _currentModuleStates = await _periodService.GetModuleStatusesAsync(_currentPeriod.Id);
            PeriodModuleCombo.ItemsSource = _currentModuleStates;
            if (_currentModuleStates.Count == 0)
            {
                PeriodModuleCombo.IsEnabled = false;
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = _currentPeriod.IsLocked
                    ? "Рабочих модулей нет; итоговый баланс закрыт."
                    : "Рабочих модулей для закрытия нет. Можно выполнить итоговое закрытие баланса.";
                return;
            }

            PeriodModuleCombo.IsEnabled = _currentPeriod.IsLocked == false;
            PeriodModuleCombo.SelectedItem = _currentModuleStates.FirstOrDefault(item => !item.IsClosed) ??
                                             _currentModuleStates.FirstOrDefault();
            UpdateSelectedModuleState();
        }

        private void OnPeriodModuleSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedModuleState();
        }

        private void UpdateSelectedModuleState()
        {
            if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
            {
                CloseModuleButton.IsEnabled = false;
                ReopenModuleButton.IsEnabled = false;
                PeriodModuleStatusText.Text = "Модули периода недоступны";
                return;
            }

            CloseModuleButton.IsEnabled = !_currentPeriod.IsLocked && !module.IsClosed && module.ParticipatesInPeriodClose;
            ReopenModuleButton.IsEnabled = !_currentPeriod.IsLocked && module.IsClosed;

            if (!module.ParticipatesInPeriodClose)
            {
                PeriodModuleStatusText.Text = $"{module.ModuleName}: не участвует в закрытии периода";
                return;
            }

            var dependencyText = module.RequirePreviousModulesClosed
                ? "закрывается по очереди"
                : "может закрываться отдельно";
            PeriodModuleStatusText.Text = $"{module.ModuleName}: {module.StateCaption}, {dependencyText}";
        }

        private async void OnCloseModuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
                    return;

                await _periodService.CloseModuleAsync(_currentPeriod.Id, module.ModuleId);
                await UpdatePeriodStateAsync(_currentPeriod.StartDate, _currentPeriod.EndDate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Закрытие модуля", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OnReopenModuleClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentPeriod == null || PeriodModuleCombo.SelectedItem is not AccountingPeriodModuleStatus module)
                    return;

                await _periodService.ReopenModuleAsync(_currentPeriod.Id, module.ModuleId);
                await UpdatePeriodStateAsync(_currentPeriod.StartDate, _currentPeriod.EndDate);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Открытие модуля", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private (DateTime Start, DateTime End) GetPeriod()
        {
            var start = StartDatePicker.SelectedDate ?? new DateTime(DateTime.Today.Year, 1, 1);
            var end = EndDatePicker.SelectedDate ?? DateTime.Today;
            if (end < start)
                throw new InvalidOperationException("Дата окончания периода не может быть раньше даты начала");
            return (start, end);
        }
    }
}
