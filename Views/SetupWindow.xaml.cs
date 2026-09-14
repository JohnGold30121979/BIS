using System.Windows;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();

            var viewModel = new SetupViewModel(AppSettings.Instance, new WindowDialogService(this));
            viewModel.Saved += (_, _) =>
            {
                DialogResult = true;
                Close();
            };

            DataContext = viewModel;
        }

        // Прокрутка колесиком мыши, когда фокус на вложенных элементах (TextBox/ComboBox),
        // которые сами перехватывают событие колесика.
        private void OnScrollPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta == 0 || RootScroll == null)
                return;

            var offset = RootScroll.VerticalOffset - e.Delta;
            RootScroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(offset, RootScroll.ScrollableHeight)));
            e.Handled = true;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}


