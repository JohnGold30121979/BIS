using System.Windows;
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
    }
}
