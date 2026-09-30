using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;

namespace BIS.ERP.Views
{
    public partial class InfoBasesView : UserControl
    {
        private readonly InfoBaseManager _manager;
        private List<InfoBase> _infoBases = new();

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
                _infoBases = bases;

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
                    await LoadInfoBasesAsync();
                    // Прокручиваем список к карточке только что созданной базы.
                    // Модальное подтверждение не показываем: результат виден в списке и не отвлекает от работы.
                    BringInfoBaseIntoView(dialog.InfoBaseName);
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

        /// <summary>
        /// Прокручивает список к карточке только что созданной информационной базы,
        /// чтобы результат создания был сразу виден пользователю.
        /// </summary>
        private void BringInfoBaseIntoView(string? infoBaseName)
        {
            if (string.IsNullOrWhiteSpace(infoBaseName))
                return;

            var target = _infoBases.FirstOrDefault(infoBase =>
                string.Equals(infoBase.Name, infoBaseName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return;

            InfoBasesList.UpdateLayout();
            if (InfoBasesList.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement container)
                container.BringIntoView();
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

        private async void OnModulesClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var infoBase = button?.Tag as InfoBase;

            if (infoBase == null)
                return;

            var dialog = new InfoBaseModulesDialog(infoBase)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadInfoBasesAsync();
                // Состояние модулей видно в карточке базы: модальное подтверждение только отвлекает.
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
                        // Удалённая база исчезает из списка: модальное подтверждение не нужно.
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
