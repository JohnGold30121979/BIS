using System.Windows;
using System.ComponentModel;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class InfoBaseSelectionWindow : Window
    {
        private readonly InfoBaseSelectionViewModel _viewModel;
        private bool _closeForMainWindow;

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
            _viewModel.CloseRequested += (_, _) =>
            {
                _closeForMainWindow = true;
                Close();
            };
            _viewModel.ExitRequested += (_, _) => ApplicationExitService.ConfirmAndShutdown(this);

            DataContext = _viewModel;
            Loaded += OnLoaded;
            Closing += OnWindowClosing;

            InfoBasesList.MouseDoubleClick += async (_, _) =>
            {
                if (_viewModel.StartWorkModeCommand.CanExecute(null))
                    await _viewModel.StartWorkModeCommand.ExecuteAsync(null);
            };
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (ApplicationExitService.IsShuttingDown || _closeForMainWindow)
                return;

            e.Cancel = true;
            ApplicationExitService.ConfirmAndShutdown(this);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var configuration = await new SystemConfigurationService().GetAsync();
            SystemNameText.Text = configuration.SystemName;
            SystemIconText.Text = configuration.Icon;
            Title = $"Запуск {configuration.SystemName}";
            await _viewModel.LoadAsync();
        }
    }
}
