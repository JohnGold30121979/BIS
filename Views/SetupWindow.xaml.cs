using System;
using System.Windows;
using Npgsql;
using BIS.ERP.Services;
using System.IO;
using System.Text.Json;

namespace BIS.ERP.Views
{
    public partial class SetupWindow : Window
    {
        public SetupWindow()
        {
            InitializeComponent();

            // Загружаем сохраненные настройки
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

                // Создаем объект настроек
                var settings = new AppSettings
                {
                    Host = host,
                    Port = port,
                    DatabaseName = "bis_master",
                    Username = username,
                    Password = password
                };

                // Сохраняем в файл напрямую
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                // Обновляем статический экземпляр
                var instance = AppSettings.Instance;
                instance.Host = host;
                instance.Port = port;
                instance.Username = username;
                instance.Password = password;

                MessageBox.Show($"Настройки сохранены!\nФайл: {configPath}\nПароль: '{password}'",
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