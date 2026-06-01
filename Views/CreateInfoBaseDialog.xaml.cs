using System;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class CreateInfoBaseDialog : Window
    {
        public string InfoBaseName => NameBox.Text;
        public string InfoBaseType => (TypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Finance";
        public string Host => HostBox.Text;
        public int Port => int.TryParse(PortBox.Text, out var port) ? port : 5432;
        public string Username => UsernameBox.Text;
        public string Password => PasswordBox.Password;

        public bool IsSuccess { get; private set; } = false;

        public CreateInfoBaseDialog()
        {
            InitializeComponent();

            // Загружаем настройки из AppSettings
            var settings = AppSettings.Instance;
            HostBox.Text = settings.Host;
            PortBox.Text = settings.Port.ToString();
            UsernameBox.Text = settings.Username;
            PasswordBox.Password = settings.Password;

            GenerateDatabaseName();
        }

        private void GenerateDatabaseName()
        {
            // Генерируем имя БД только из латинских букв, цифр и подчеркиваний
            var baseName = NameBox.Text.Trim()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_");

            // Транслитерация кириллицы в латиницу
            baseName = Transliterate(baseName);

            // Убираем все недопустимые символы
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^a-zA-Z0-9_]", "");

            if (string.IsNullOrEmpty(baseName))
                baseName = "new_base";

            // Добавляем префикс и суффикс с датой
            DatabaseBox.Text = $"bis_{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
            CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        // Транслитерация кириллицы в латиницу
        private string Transliterate(string text)
        {
            var translitMap = new System.Collections.Generic.Dictionary<char, string>
    {
        {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"}, {'е', "e"},
        {'ё', "yo"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"}, {'й', "y"}, {'к', "k"},
        {'л', "l"}, {'м', "m"}, {'н', "n"}, {'о', "o"}, {'п', "p"}, {'р', "r"},
        {'с', "s"}, {'т', "t"}, {'у', "u"}, {'ф', "f"}, {'х', "h"}, {'ц', "ts"},
        {'ч', "ch"}, {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""},
        {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
        {'А', "A"}, {'Б', "B"}, {'В', "V"}, {'Г', "G"}, {'Д', "D"}, {'Е', "E"},
        {'Ё', "Yo"}, {'Ж', "Zh"}, {'З', "Z"}, {'И', "I"}, {'Й', "Y"}, {'К', "K"},
        {'Л', "L"}, {'М', "M"}, {'Н', "N"}, {'О', "O"}, {'П', "P"}, {'Р', "R"},
        {'С', "S"}, {'Т', "T"}, {'У', "U"}, {'Ф', "F"}, {'Х', "H"}, {'Ц', "Ts"},
        {'Ч', "Ch"}, {'Ш', "Sh"}, {'Щ', "Sch"}, {'Ъ', ""}, {'Ы', "Y"}, {'Ь', ""},
        {'Э', "E"}, {'Ю', "Yu"}, {'Я', "Ya"}
    };

            var result = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (translitMap.ContainsKey(c))
                    result.Append(translitMap[c]);
                else if (char.IsLetterOrDigit(c) || c == '_')
                    result.Append(c);
                else
                    result.Append('_');
            }
            return result.ToString();
        }

        private void OnNameChanged(object sender, TextChangedEventArgs e)
        {
            GenerateDatabaseName();
            ErrorText.Visibility = Visibility.Collapsed;
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

        private async void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InfoBaseName))
            {
                ShowError("Введите название базы");
                return;
            }

            CreateButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                var manager = new InfoBaseManager();

                var canConnect = await manager.TestConnectionAsync(
                    Host, Port, "postgres", Username, Password);

                if (!canConnect)
                {
                    ShowError("Не удалось подключиться к серверу PostgreSQL. Проверьте параметры подключения.");
                    CreateButton.IsEnabled = true;
                    return;
                }

                // Создаем базу с описанием
                var newBase = await manager.CreateInfoBaseAsync(
                    InfoBaseName,
                    InfoBaseType,
                    Host,
                    Port,
                    Username,
                    Password);

                // Обновляем описание для отображения
                if (newBase != null)
                {
                    newBase.Description = InfoBaseName;
                    IsSuccess = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowError("Не удалось создать базу данных");
                }
            }
            catch (Exception ex)
            {
                ShowError($"Ошибка создания: {ex.Message}");
                CreateButton.IsEnabled = true;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}