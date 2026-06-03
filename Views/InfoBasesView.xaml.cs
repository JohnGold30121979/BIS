using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class InfoBasesView : UserControl
    {
        private readonly InfoBaseManager _manager;

        public InfoBasesView()
        {
            InitializeComponent();
            _manager = new InfoBaseManager();
            this.Loaded += async (s, e) => await LoadInfoBasesAsync();
        }

        private async Task LoadInfoBasesAsync()
        {
            LoadingProgress.Visibility = Visibility.Visible;

            try
            {
                var bases = await _manager.GetInfoBasesAsync();

                if (bases.Count == 0)
                {
                    EmptyText.Visibility = Visibility.Visible;
                    InfoBasesList.Visibility = Visibility.Collapsed;
                }
                else
                {
                    EmptyText.Visibility = Visibility.Collapsed;
                    InfoBasesList.Visibility = Visibility.Visible;
                    InfoBasesList.ItemsSource = bases;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void OnCreateClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateInfoBaseDialog();
            dialog.Owner = Window.GetWindow(this);
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (dialog.ShowDialog() == true)
            {
                LoadingProgress.Visibility = Visibility.Visible;

                try
                {
                    // Убираем InfoBaseType - теперь тип всегда "Universal"
                    await _manager.CreateInfoBaseAsync(
                        dialog.InfoBaseName,
                        "Universal",
                        dialog.Host,
                        dialog.Port,
                        dialog.Username,
                        dialog.Password);

                    await LoadInfoBasesAsync();
                    MessageBox.Show("Информационная база успешно создана!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    LoadingProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void OnSelectClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var infoBase = button?.Tag as InfoBase;

            if (infoBase != null)
            {
                LoadingProgress.Visibility = Visibility.Visible;

                try
                {
                    await _manager.SetCurrentInfoBaseAsync(infoBase.Id);
                    await LoadInfoBasesAsync();
                    MessageBox.Show($"Выбрана база: {infoBase.Name}", "Информация",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка выбора: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    LoadingProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var infoBase = button?.Tag as InfoBase;

            if (infoBase != null)
            {
                var result = MessageBox.Show($"Удалить базу '{infoBase.Name}'?\nВсе данные будут потеряны!",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    LoadingProgress.Visibility = Visibility.Visible;

                    try
                    {
                        await _manager.DeleteInfoBaseAsync(infoBase.Id);
                        await LoadInfoBasesAsync();
                        MessageBox.Show("База удалена", "Успех",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        LoadingProgress.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }
    }
}