using BIS.ERP.Models;
using System;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class AppUpdateBuildSettingsDialog : Window
    {
        public AppUpdateManifest Manifest { get; private set; }

        public AppUpdateBuildSettingsDialog(AppUpdateManifest manifest, bool generatedAutomatically)
        {
            InitializeComponent();
            Manifest = manifest;

            UpdateIdBox.Text = manifest.UpdateId;
            VersionBox.Text = manifest.Version;
            NameBox.Text = manifest.Name;
            AuthorBox.Text = string.IsNullOrWhiteSpace(manifest.Author)
                ? Environment.UserName
                : manifest.Author;
            MinAppVersionBox.Text = manifest.MinAppVersion;
            RestartExecutableBox.Text = string.IsNullOrWhiteSpace(manifest.RestartExecutable)
                ? "BIS.ERP.exe"
                : manifest.RestartExecutable;
            DescriptionBox.Text = manifest.Description;
            HintText.Text = generatedAutomatically
                ? "Манифест будет создан автоматически."
                : "В папке найден manifest.json. Можно оставить или изменить его параметры.";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UpdateIdBox.Text))
            {
                MessageBox.Show("Укажите UpdateId.", "Обновление программы", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateIdBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(VersionBox.Text))
            {
                MessageBox.Show("Укажите версию программы.", "Обновление программы", MessageBoxButton.OK, MessageBoxImage.Warning);
                VersionBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Укажите наименование обновления.", "Обновление программы", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(RestartExecutableBox.Text))
            {
                MessageBox.Show("Укажите исполняемый файл для запуска.", "Обновление программы", MessageBoxButton.OK, MessageBoxImage.Warning);
                RestartExecutableBox.Focus();
                return;
            }

            Manifest.UpdateId = UpdateIdBox.Text.Trim();
            Manifest.Version = VersionBox.Text.Trim();
            Manifest.Name = NameBox.Text.Trim();
            Manifest.Author = AuthorBox.Text.Trim();
            Manifest.MinAppVersion = MinAppVersionBox.Text.Trim();
            Manifest.RestartExecutable = RestartExecutableBox.Text.Trim();
            Manifest.Description = DescriptionBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
