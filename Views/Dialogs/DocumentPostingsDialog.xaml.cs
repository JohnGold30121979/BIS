using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BIS.ERP.Models;

namespace BIS.ERP.Views.Dialogs
{
    public partial class DocumentPostingsDialog : Window
    {
        public DocumentPostingsDialog(string documentType, string documentNumber, IEnumerable<PostingViewModel> postings)
        {
            InitializeComponent();
            TitleText.Text = $"Все проводки: {documentType} № {documentNumber}";
            var items = postings.ToList();
            PostingsGrid.ItemsSource = items;
            SummaryText.Text = items.Count == 0
                ? "Проводки не найдены. Документ ещё не проведён или проводки отсутствуют."
                : $"Всего проводок: {items.Count}; общая сумма по дебету: {items.Sum(item => item.Amount):N2} сом";
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
