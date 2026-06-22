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
            Loaded += OnLoaded;

            InfoBasesList.MouseDoubleClick += async (_, _) =>
            {
                if (_viewModel.StartWorkModeCommand.CanExecute(null))
                    await _viewModel.StartWorkModeCommand.ExecuteAsync(null);
            };
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
