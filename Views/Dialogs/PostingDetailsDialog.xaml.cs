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

            var dateText = posting.Date.ToString("dd/MM/yyyy HH:mm");
            var moduleText = string.IsNullOrWhiteSpace(posting.ModuleName) ? "нет данных" : posting.ModuleName;
            SubtitleText.Text = $"Дата: {dateText}    •    Модуль: {moduleText}";

            ConfigureStatusBadge(posting.IsActive);
            BuildDetails(posting);
        }

        private void ConfigureStatusBadge(bool isActive)
        {
            if (isActive)
            {
                StatusBadgeText.Text = "● Активна";
                StatusBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E7E34"));
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F7EE"));
            }
            else
            {
                StatusBadgeText.Text = "● Отключена";
                StatusBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A5C1F"));
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBF3E3"));
            }
        }

        private void BuildDetails(PostingViewModel p)
        {
            // Левая колонка: документ + бухгалтерия
            LeftPanel.Children.Add(BuildValueSection("Документ", new List<(string Label, string?)>
            {
                ("Тип документа", Fallback(p.DocumentType)),
                ("Номер документа", Fallback(p.DocumentNumber)),
                ("Дата", p.Date.ToString("dd/MM/yyyy HH:mm")),
                ("Модуль", string.IsNullOrWhiteSpace(p.ModuleName) ? null : p.ModuleName)
            }));

            LeftPanel.Children.Add(BuildValueSection("Бухгалтерия", new List<(string Label, string?)>
            {
                ("Дебет", FormatAccount(p.DebitAccount, p.DebitAccountName)),
                ("Кредит", FormatAccount(p.CreditAccount, p.CreditAccountName)),
                ("Корр. счёт", p.CorrespondentAccount),
                ("Операция", Fallback(p.Direction))
            }));

            // Правая колонка: суммы + участники + системная информация
            RightPanel.Children.Add(BuildValueSection("Суммы", new List<(string Label, string?)>
            {
                ("Сумма (сом)", p.Amount.ToString("N2")),
                ("Сумма (валюта)", p.AmountCurrency > 0 ? p.AmountCurrency.ToString("N2") : null),
                ("Валюта", Fallback(p.Currency))
            }));

            RightPanel.Children.Add(BuildValueSection("Участники", new List<(string Label, string?)>
            {
                ("Организация", Fallback(p.Organization)),
                ("Сотрудник", Fallback(p.Employee)),
                ("Площадка", Fallback(p.Site)),
                ("Ответственный", Fallback(p.ResponsiblePerson))
            }));

            RightPanel.Children.Add(BuildValueSection("Системная информация", new List<(string Label, string?)>
            {
                ("Создана", p.CreatedAt?.ToString("dd/MM/yyyy HH:mm")),
                ("ID проводки", p.Id.ToString()),
                ("ID документа", p.DocumentId?.ToString())
            }));

            // Примечание на всю ширину
            if (!string.IsNullOrWhiteSpace(p.Note))
            {
                NotePanel.Children.Add(BuildValueSection("Примечание", new List<(string Label, string?)>
                {
                    ("", p.Note)
                }));
            }
        }

        private static Border BuildValueSection(string title, List<(string Label, string?)> rows)
        {
            var content = new StackPanel();

            if (!string.IsNullOrWhiteSpace(title))
            {
                content.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34495E")),
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(180) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };

            var rowIndex = 0;
            foreach (var (label, value) in rows)
            {
                if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(value))
                    continue;

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                if (!string.IsNullOrWhiteSpace(label))
                {
                    var labelBlock = new TextBlock
                    {
                        Text = label + ":",
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")),
                        Margin = new Thickness(0, 4, 10, 4),
                        VerticalAlignment = VerticalAlignment.Top,
                        FontSize = 13
                    };
                    Grid.SetRow(labelBlock, rowIndex);
                    Grid.SetColumn(labelBlock, 0);
                    grid.Children.Add(labelBlock);
                }

                var valueBlock = new TextBlock
                {
                    Text = Fallback(value),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#243B53")),
                    Margin = new Thickness(0, 4, 0, 4),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13
                };
                Grid.SetRow(valueBlock, rowIndex);
                Grid.SetColumn(valueBlock, string.IsNullOrWhiteSpace(label) ? 0 : 1);
                grid.Children.Add(valueBlock);

                rowIndex++;

                if (rowIndex < rows.Count)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var separator = new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"))
                    };
                    Grid.SetRow(separator, rowIndex);
                    Grid.SetColumn(separator, 0);
                    Grid.SetColumnSpan(separator, 2);
                    grid.Children.Add(separator);

                    rowIndex++;
                }
            }

            content.Children.Add(grid);

            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D7E3EF")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = content
            };
        }

        private static string FormatAccount(string accountCode, string accountName)
        {
            var code = string.IsNullOrWhiteSpace(accountCode) ? string.Empty : accountCode.Trim();
            var name = string.IsNullOrWhiteSpace(accountName) || accountName.Trim().Equals(code, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : accountName.Trim();

            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(name))
                return string.Empty;

            return string.IsNullOrEmpty(name) ? code : $"{code} — {name}";
        }

        private static string Fallback(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value;

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}