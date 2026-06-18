using System;
using System.Windows;
using Npgsql;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();

            var settings = AppSettings.Instance;
            HostBox.Text = settings.Host;
            PortBox.Text = settings.Port.ToString();
            UsernameBox.Text = settings.Username;
            PasswordBox.Password = settings.Password;
        }

        private async void OnTestClick(object sender, RoutedEventArgs e)
        {
            TestButton.IsEnabled = false;
            TestResultText.Visibility = Visibility.Collapsed;

            try
            {
                var host = HostBox.Text;
                var port = int.Parse(PortBox.Text);
                var username = UsernameBox.Text;
                var password = PasswordBox.Password;

                using var connection = new NpgsqlConnection($"Host={host};Port={port};Username={username};Password={password}");
                await connection.OpenAsync();

                TestResultText.Text = "✓ Подключение успешно!";
                TestResultText.Foreground = System.Windows.Media.Brushes.Green;
                TestResultText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                TestResultText.Text = $"✗ Ошибка: {ex.Message}";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
                TestResultText.Visibility = Visibility.Visible;
            }
            finally
            {
                TestButton.IsEnabled = true;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var host = HostBox.Text;
                var port = int.Parse(PortBox.Text);
                var username = UsernameBox.Text;
                var password = PasswordBox.Password;

                var settings = AppSettings.Instance;
                settings.Host = host;
                settings.Port = port;
                settings.DatabaseName = "bis_master";
                settings.Username = username;
                settings.Password = password;
                settings.Save();

                MessageBox.Show("Настройки сохранены.",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
