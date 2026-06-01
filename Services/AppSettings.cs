using Npgsql;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace BIS.ERP.Services
{
    public class AppSettings
    {
        private static AppSettings _instance;
        private static readonly object _lock = new object();
        private const string SettingsFile = "appsettings.json";

        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string DatabaseName { get; set; } = "bis_master";
        public string Username { get; set; } = "postgres";
        public string Password { get; set; } = "";

        public static AppSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance = Load();
                    }
                }
                return _instance;
            }
        }

        public string GetMasterConnectionString()
        {
            return $"Host={Host};Port={Port};Database={DatabaseName};Username={Username};Password={Password}";
        }

        // Подключение к системной базе postgres (всегда существует)
        public string GetPostgresConnectionString()
        {
            return $"Host={Host};Port={Port};Database=postgres;Username={Username};Password={Password}";
        }

        public static AppSettings Load()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFile);

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
                MessageBox.Show($"Ошибка загрузки настроек: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFile);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                System.Diagnostics.Debug.WriteLine($"Settings saved to: {configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
                MessageBox.Show($"Ошибка сохранения настроек: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Проверяем подключение к серверу PostgreSQL через системную базу postgres
        public bool TestConnection()
        {
            try
            {
                using var connection = new NpgsqlConnection(GetPostgresConnectionString());
                connection.Open();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Connection test failed: {ex.Message}");
                return false;
            }
        }
    }
}