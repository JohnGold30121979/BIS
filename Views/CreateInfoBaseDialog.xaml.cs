using System;
using System.Windows;
using System.Windows.Controls;
using Npgsql;
using BIS.ERP.Services;

namespace BIS.ERP.Views;

public partial class CreateInfoBaseDialog : Window
{
    public string InfoBaseName => NameBox.Text;
    public string InfoBaseType => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Finance";
    public string Host => HostBox.Text;
    public int Port => int.TryParse(PortBox.Text, out var port) ? port : 5432;
    public string Username => UsernameBox.Text;
    public string Password => PasswordBox.Password;

    public CreateInfoBaseDialog()
    {
        InitializeComponent();

        // Генерируем имя БД при изменении названия
        NameBox.TextChanged += (s, e) => GenerateDatabaseName();
    }

    private void GenerateDatabaseName()
    {
        var baseName = NameBox.Text.Trim().ToLower()
            .Replace(" ", "_")
            .Replace("-", "_")
            .Replace(".", "_");

        if (string.IsNullOrEmpty(baseName))
            baseName = "new_base";

        DatabaseBox.Text = $"bis_{baseName}_{DateTime.Now:yyyyMMdd}";

        // Включаем кнопку создания только если есть название
        CreateBtn.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        TestConnectionBtn.IsEnabled = false;
        TestResultText.Visibility = Visibility.Collapsed;

        try
        {
            var manager = new InfoBaseManager();
            var success = await manager.TestConnectionAsync(
                Host, Port, "postgres", Username, Password);

            if (success)
            {
                TestResultText.Text = "✓ Подключение успешно!";
                TestResultText.Foreground = System.Windows.Media.Brushes.Green;
                TestResultText.Visibility = Visibility.Visible;
            }
            else
            {
                TestResultText.Text = "✗ Ошибка подключения. Проверьте параметры.";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
                TestResultText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"✗ Ошибка: {ex.Message}";
            TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            TestResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            TestConnectionBtn.IsEnabled = true;
        }
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        GenerateDatabaseName();
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InfoBaseName))
        {
            ErrorText.Text = "Введите название базы";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        CreateBtn.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            // Проверяем подключение перед созданием
            var manager = new InfoBaseManager();
            var canConnect = await manager.TestConnectionAsync(Host, Port, "postgres", Username, Password);

            if (!canConnect)
            {
                ErrorText.Text = "Не удалось подключиться к серверу PostgreSQL. Проверьте параметры подключения.";
                ErrorText.Visibility = Visibility.Visible;
                CreateBtn.IsEnabled = true;
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Ошибка: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            CreateBtn.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}