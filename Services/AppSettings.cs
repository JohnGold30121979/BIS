// Services/AppSettings.cs
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
        public string Password { get; set; } = "qwerty123";

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

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public bool TestConnection()
        {
            try
            {
                using var connection = new Npgsql.NpgsqlConnection(GetMasterConnectionString());
                connection.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}