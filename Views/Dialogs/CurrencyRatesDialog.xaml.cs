using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views.Dialogs
{
    /// <summary>
    /// Просмотр курсов выбранной валюты из справочника и запрос новых курсов НБКР.
    ///
    /// Раньше кнопка «?» в ПКО/РКО срабатывала только когда курса на дату не было:
    /// при наличии курса она молча заполняла поле, и посмотреть историю было нельзя.
    /// </summary>
    public partial class CurrencyRatesDialog : Window
    {
        private readonly MetadataService _metadataService;
        private readonly Guid _currencyId;
        private readonly DateTime _documentDate;
        private List<RateRow> _rows = new();

        public decimal? SelectedRate { get; private set; }
        public DateTime? SelectedRateDate { get; private set; }

        public CurrencyRatesDialog(
            MetadataService metadataService,
            Guid currencyId,
            string currencyName,
            DateTime documentDate)
        {
            InitializeComponent();

            _metadataService = metadataService;
            _currencyId = currencyId;
            _documentDate = documentDate.Date;

            HeaderText.Text = $"Курсы валюты: {currencyName} (документ на {_documentDate:dd.MM.yyyy})";
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async System.Threading.Tasks.Task ReloadAsync()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Загрузка курсов...";

                var rates = await _metadataService.GetCurrencyRatesAsync(
                    _currencyId,
                    _documentDate.AddMonths(-6),
                    _documentDate,
                    limit: 500);

                _rows = rates
                    .Select((rate, index) => new RateRow(rate.RateDate, rate.Rate, rate.Source, index == 0))
                    .ToList();

                RatesGrid.ItemsSource = _rows;

                StatusText.Text = _rows.Count == 0
                    ? "Курсов за последние 6 месяцев нет. Нажмите «Запросить новый курс»."
                    : $"Найдено курсов: {_rows.Count}. Двойной клик по строке — применить курс.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не удалось загрузить курсы: {ex.Message}";
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private async void OnRequestNewRateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                StatusText.Text = "Запрос курсов НБКР...";

                var results = await _metadataService.ImportOfficialCurrencyRatesAsync(
                    _documentDate.AddDays(-7),
                    _documentDate);

                var imported = results.Sum(item => item.Imported);
                var skipped = results.Sum(item => item.Skipped);
                StatusText.Text = $"Курсы запрошены: добавлено {imported}, пропущено {skipped}.";

                await ReloadAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не удалось запросить курсы: {ex.Message}";
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void OnApplyClick(object sender, RoutedEventArgs e) => ApplySelected();

        /// <summary>
        /// Первая строка списка — последний курс валюты. Подсвечиваем её
        /// светло-зелёным и делаем жирной, чтобы её было видно сразу.
        /// </summary>
        private void OnRatesGridLoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.ClearValue(Control.BackgroundProperty);
            e.Row.ClearValue(Control.FontWeightProperty);

            if (e.Row.Item is RateRow { IsLatest: true })
            {
                e.Row.Background = new SolidColorBrush(Color.FromRgb(232, 248, 233));
                e.Row.FontWeight = FontWeights.Bold;
            }
        }

        private void OnRatesGridDoubleClick(object sender, MouseButtonEventArgs e) => ApplySelected();

        private void ApplySelected()
        {
            if (RatesGrid.SelectedItem is not RateRow row)
            {
                StatusText.Text = "Выберите строку с курсом.";
                return;
            }

            SelectedRate = row.Rate;
            SelectedRateDate = row.Date;
            DialogResult = true;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private sealed record RateRow(DateTime Date, decimal Rate, string Source, bool IsLatest)
        {
            public string DateText => Date.ToString("dd.MM.yyyy");
            public string RateText => Rate.ToString("0.####");
        }
    }
}
