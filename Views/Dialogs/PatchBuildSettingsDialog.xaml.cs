using BIS.ERP.Models;
using System;
using System.Linq;
using System.Windows;

namespace BIS.ERP.Views.Dialogs
{
    public partial class PatchBuildSettingsDialog : Window
    {
        public BisPatchManifest Manifest { get; private set; }

        public PatchBuildSettingsDialog(BisPatchManifest manifest, bool generatedAutomatically)
        {
            InitializeComponent();
            Manifest = manifest;

            PatchIdBox.Text = manifest.PatchId;
            VersionBox.Text = manifest.Version;
            NameBox.Text = manifest.Name;
            AuthorBox.Text = string.IsNullOrWhiteSpace(manifest.Author)
                ? Environment.UserName
                : manifest.Author;
            DependenciesBox.Text = string.Join(", ", manifest.Dependencies);
            DescriptionBox.Text = manifest.Description;
            HintText.Text = generatedAutomatically
                ? "Манифест будет создан автоматически."
                : "В папке найден manifest.json. Можно оставить или изменить его параметры.";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PatchIdBox.Text))
            {
                MessageBox.Show("Укажите PatchId.", "Патчи", MessageBoxButton.OK, MessageBoxImage.Warning);
                PatchIdBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(VersionBox.Text))
            {
                MessageBox.Show("Укажите версию патча.", "Патчи", MessageBoxButton.OK, MessageBoxImage.Warning);
                VersionBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Укажите наименование патча.", "Патчи", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus();
                return;
            }

            Manifest.PatchId = PatchIdBox.Text.Trim();
            Manifest.Version = VersionBox.Text.Trim();
            Manifest.Name = NameBox.Text.Trim();
            Manifest.Author = AuthorBox.Text.Trim();
            Manifest.Description = DescriptionBox.Text.Trim();
            Manifest.Dependencies = DependenciesBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
