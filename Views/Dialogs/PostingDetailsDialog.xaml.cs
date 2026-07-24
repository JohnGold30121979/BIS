using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class PostingDetailsDialog : Window
    {
        public PostingDetailsDialog(PostingViewModel posting)
        {
            InitializeComponent();

            var typeLabel = string.IsNullOrWhiteSpace(posting.DocumentType) ? "Проводка" : posting.DocumentType;
            TitleText.Text = $"{typeLabel} N{posting.DocumentNumber}";

            Title = $"Детали проводки N{posting.DocumentNumber}";

            BuildDetails(posting);
        }

        private void BuildDetails(PostingViewModel posting)
        {
            var rows = new List<(string Label, string?)>
            {
                ("Документ", posting.DocumentNumber),
                ("Дата", posting.Date.ToString("dd.MM.yyyy HH:mm")),
                ("Модуль", string.IsNullOrWhiteSpace(posting.ModuleName) ? "нет данных" : posting.ModuleName),
                ("Дебет", FormatAccount(posting.DebitAccount, posting.DebitAccountName)),
                ("Кредит", FormatAccount(posting.CreditAccount, posting.CreditAccountName)),
                ("Операция", posting.Direction),
                ("Сумма (сом)", posting.Amount.ToString("N2")),
                ("Сумма (валюта)", posting.AmountCurrency > 0 ? posting.AmountCurrency.ToString("N2") : null),
                ("Валюта", posting.Currency),
                ("Организация", posting.Organization),
                ("Сотрудник", posting.Employee),
                ("Создана", posting.CreatedAt?.ToString("dd.MM.yyyy HH:mm")),
                ("Статус", posting.IsActive ? "Активна" : "Отключена"),
                ("Примечание", posting.Note)
            };

            var grid = new Grid
            {
                Margin = new Thickness(0),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(150) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };

            var rowIndex = 0;
            foreach (var (label, value) in rows)
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var labelBlock = new TextBlock
                {
                    Text = label + ":",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F8C8D")),
                    Margin = new Thickness(0, 4, 10, 4),
                    VerticalAlignment = VerticalAlignment.Top,
                    FontSize = 14
                };
                Grid.SetRow(labelBlock, rowIndex);
                Grid.SetColumn(labelBlock, 0);
                grid.Children.Add(labelBlock);

                var valueBlock = new TextBlock
                {
                    Text = value,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50")),
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14
                };
                Grid.SetRow(valueBlock, rowIndex);
                Grid.SetColumn(valueBlock, 1);
                grid.Children.Add(valueBlock);

                rowIndex++;

                // Разделитель на отдельной строке между строками с данными
                if (rowIndex < rows.Count)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var separator = new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECF0F1")),
                        Margin = new Thickness(0, 0, 0, 0)
                    };
                    Grid.SetRow(separator, rowIndex);
                    Grid.SetColumn(separator, 0);
                    Grid.SetColumnSpan(separator, 2);
                    grid.Children.Add(separator);

                    rowIndex++;
                }
            }

            DetailsPanel.Children.Add(grid);
        }

        private static string FormatAccount(string accountCode, string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountCode))
                return accountName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(accountName))
                return accountCode;

            return $"{accountCode} - {accountName}";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}