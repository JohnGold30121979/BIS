using System.Windows;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class InfoBaseSelectionWindow : Window
    {
        private readonly InfoBaseSelectionViewModel _viewModel;

        public InfoBaseSelectionWindow()
        {
            InitializeComponent();

            _viewModel = new InfoBaseSelectionViewModel(
                ServiceLocator.InfoBaseManager,
                ServiceLocator.AuthService,
                new WindowDialogService(this));

            _viewModel.OpenMainWindowRequested += (_, isConfigMode) =>
            {
                if (isConfigMode)
                {
                    new ConfiguratorWindow().Show();
                }
                else
                {
                    new MainWorkWindow(ServiceLocator.AuthService).Show();
                }
            };
            _viewModel.CloseRequested += (_, _) => Close();
            _viewModel.ExitRequested += (_, _) => Application.Current.Shutdown();

            DataContext = _viewModel;
            Loaded += async (_, _) => await _viewModel.LoadAsync();

            InfoBasesList.MouseDoubleClick += async (_, _) =>
            {
                if (_viewModel.StartWorkModeCommand.CanExecute(null))
                    await _viewModel.StartWorkModeCommand.ExecuteAsync(null);
            };
        }
    }
}
