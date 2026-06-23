using BIS.ERP.Services;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AboutSystemDialog : Window
    {
        public AboutSystemDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var configuration = await new SystemConfigurationService().GetAsync();
            Title = $"О системе {configuration.SystemName}";
            IconText.Text = configuration.Icon;
            SystemNameText.Text = configuration.SystemName;
            DescriptionText.Text = EmptyFallback(configuration.Description);
            CompanyDetailsText.Text = EmptyFallback(configuration.CompanyDetails);
            EmailText.Text = EmptyFallback(configuration.Email);
            PhoneText.Text = EmptyFallback(configuration.Phone);
        }

        private static string EmptyFallback(string value) =>
            string.IsNullOrWhiteSpace(value) ? "Не указано" : value;
    }
}
