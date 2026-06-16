using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class PostingSearchDialog : Window
    {
        public Dictionary<string, object> SelectedPosting { get; private set; }
        private List<Dictionary<string, object>> _postings;

        public PostingSearchDialog(List<Dictionary<string, object>> postings)
        {
            InitializeComponent();
            _postings = postings;
            SearchResultsGrid.ItemsSource = postings;
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            var searchText = SearchBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(searchText))
            {
                SearchResultsGrid.ItemsSource = _postings;
                StatusText.Text = "Готово";
                return;
            }

            var results = _postings.Where(p =>
                (p.ContainsKey("Номер документа") && p["Номер документа"]?.ToString().ToLower().Contains(searchText) == true) ||
                (p.ContainsKey("Дебет") && p["Дебет"]?.ToString().ToLower().Contains(searchText) == true) ||
                (p.ContainsKey("Кредит") && p["Кредит"]?.ToString().ToLower().Contains(searchText) == true) ||
                (p.ContainsKey("Сумма в сом") && p["Сумма в сом"]?.ToString().ToLower().Contains(searchText) == true) ||
                (p.ContainsKey("Примечание") && p["Примечание"]?.ToString().ToLower().Contains(searchText) == true) ||
                (p.ContainsKey("Организация") && p["Организация"]?.ToString().ToLower().Contains(searchText) == true)
            ).ToList();

            SearchResultsGrid.ItemsSource = results;
            StatusText.Text = $"Найдено: {results.Count}";
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            SelectedPosting = SearchResultsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedPosting != null)
            {
                DialogResult = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SearchResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SelectedPosting = SearchResultsGrid.SelectedItem as Dictionary<string, object>;
            if (SelectedPosting != null)
            {
                DialogResult = true;
                Close();
            }
        }
    }
}