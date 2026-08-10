using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;

namespace BIS.ERP.Views.Dialogs
{
    public partial class DocumentPostingsDialog : Window
    {
        public DocumentPostingsDialog(
            string documentType,
            string documentNumber,
            IEnumerable<PostingViewModel> postings,
            IEnumerable<KeyValuePair<string, string>>? turnoverSummary = null)
        {
            InitializeComponent();
            TitleText.Text = $"Все проводки: {documentType} № {documentNumber}";
            var items = postings.Select(NormalizePostingContent).ToList();
            PostingsGrid.ItemsSource = items;
            SummaryText.Text = items.Count == 0
                ? "Проводки не найдены. Документ ещё не проведён или проводки отсутствуют."
                : $"Всего проводок: {items.Count}; общая сумма по дебету: {items.Sum(item => item.Amount):N2} сом";
            ApplyTurnoverSummary(turnoverSummary);
        }


        private static PostingViewModel NormalizePostingContent(PostingViewModel posting)
        {
            if (IsWeakPostingNote(posting.Note))
            {
                posting.Note = BuildFallbackContent(posting);
            }

            return posting;
        }

        private static string BuildFallbackContent(PostingViewModel posting)
        {
            if (!string.IsNullOrWhiteSpace(posting.Direction))
                return posting.Direction;

            if (!string.IsNullOrWhiteSpace(posting.DocumentType) || !string.IsNullOrWhiteSpace(posting.DocumentNumber))
                return $"{posting.DocumentType} № {posting.DocumentNumber}".Trim();

            return $"Дт {posting.DebitAccount} / Кт {posting.CreditAccount}";
        }

        private static bool IsWeakPostingNote(string? note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return true;

            var trimmed = note.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex < 0)
                return false;

            var prefix = trimmed[..colonIndex].Trim();
            return prefix.StartsWith("Строка", StringComparison.OrdinalIgnoreCase) ||
                   prefix.StartsWith("Row", StringComparison.OrdinalIgnoreCase);
        }
        private void ApplyTurnoverSummary(IEnumerable<KeyValuePair<string, string>>? turnoverSummary)
        {
            TurnoverSummaryPanel.Children.Clear();
            var fields = turnoverSummary?
                .Where(field => !string.IsNullOrWhiteSpace(field.Key))
                .ToList() ?? new List<KeyValuePair<string, string>>();

            if (fields.Count == 0)
            {
                TurnoverSummaryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var field in fields)
            {
                TurnoverSummaryPanel.Children.Add(new TextBlock
                {
                    Text = $"{field.Key}: ",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                TurnoverSummaryPanel.Children.Add(new TextBlock
                {
                    Text = field.Value,
                    MinWidth = 78,
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            TurnoverSummaryPanel.Visibility = Visibility.Visible;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
