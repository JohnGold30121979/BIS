using System.Windows;
using BIS.ERP.Models;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class ModeSelectionWindow : Window
    {
        private readonly ModeSelectionViewModel _viewModel;

        public bool IsConfigMode => _viewModel.IsConfigMode;

        public ModeSelectionWindow(User user)
        {
            InitializeComponent();

            _viewModel = new ModeSelectionViewModel(user);
            _viewModel.ModeSelected += (_, _) =>
            {
                DialogResult = true;
                Close();
            };
            _viewModel.CloseRequested += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            DataContext = _viewModel;
        }
    }
}
