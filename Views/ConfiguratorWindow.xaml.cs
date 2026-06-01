using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class ConfiguratorWindow : Window
    {
        public ConfiguratorWindow()
        {
            InitializeComponent();

            // Подписываемся на событие выбора в TreeView
            MetadataTree.SelectedItemChanged += OnMetadataSelected;
        }

        private void OnMetadataSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Header != null)
            {
                string header = selectedItem.Header.ToString().Trim();

                // Обновляем заголовок
                EditorTitle.Text = header;
                EditorDescription.Text = $"Редактирование объекта: {header}";

                // Очищаем панель свойств
                PropertiesPanel.Children.Clear();

                // Создаем панель редактирования в зависимости от выбранного элемента
                if (header == "Справочники" || header == "Документы" || header == "Отчеты")
                {
                    // Для корневых папок показываем информацию
                    ShowFolderInfo(header);
                }
                else if (header.Contains("Сотрудники") || header.Contains("Материалы") ||
                         header.Contains("Основные средства") || header.Contains("Контрагенты") ||
                         header.Contains("Подразделения"))
                {
                    // Для справочников показываем редактор
                    ShowCatalogEditor(header);
                }
                else
                {
                    // Для остальных объектов показываем общий редактор
                    ShowGenericEditor(header);
                }
            }
        }

        private void ShowFolderInfo(string folderName)
        {
            var stackPanel = new StackPanel
            {
                Margin = new Thickness(20)
            };

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"📁 {folderName}",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 0, 20)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"Это группа объектов \"{folderName}\".\n\n" +
                       "Здесь будут отображаться все объекты этого типа.\n" +
                       "Для просмотра свойств объекта выберите конкретный справочник, документ или отчет.",
                FontSize = 13,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void ShowCatalogEditor(string catalogName)
        {
            var stackPanel = new StackPanel();

            // Заголовок
            stackPanel.Children.Add(new TextBlock
            {
                Text = $"📚 Редактор справочника: {catalogName}",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // Поле для кода
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Код",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 10, 0, 5)
            });
            var codeBox = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 15) };
            stackPanel.Children.Add(codeBox);

            // Поле для наименования
            stackPanel.Children.Add(new TextBlock
            {
                Text = "Наименование",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 10, 0, 5)
            });
            var nameBox = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 15) };
            stackPanel.Children.Add(nameBox);

            // Кнопка сохранения
            var saveButton = new Button
            {
                Content = "💾 Сохранить",
                Height = 35,
                Width = 120,
                Background = Brushes.Green,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 20, 0, 0),
                Cursor = Cursors.Hand
            };
            saveButton.Click += (s, e) =>
            {
                MessageBox.Show($"Справочник \"{catalogName}\"\nКод: {codeBox.Text}\nНаименование: {nameBox.Text}",
                    "Сохранено", MessageBoxButton.OK, MessageBoxImage.Information);
                codeBox.Clear();
                nameBox.Clear();
            };
            stackPanel.Children.Add(saveButton);

            // Информация
            stackPanel.Children.Add(new TextBlock
            {
                Text = "\n💡 В следующих версиях: добавление полей, настройка форм, импорт/экспорт данных",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 20, 0, 0)
            });

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void ShowGenericEditor(string objectName)
        {
            var stackPanel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(new TextBlock
            {
                Text = "⚙️",
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 15)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"Редактор для объекта: {objectName}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "Настройка полей, форм и прав доступа будет доступна в следующей версии",
                FontSize = 12,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            PropertiesPanel.Children.Add(stackPanel);
        }

        private void OnAddMetadataClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateMetadataObjectDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                MessageBox.Show($"Создан объект: {dialog.ObjectName}\nТип: {dialog.ObjectType}",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

                // TODO: Добавить созданный объект в дерево
            }
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Обновление метаданных...\n\n" +
                "В следующей версии: загрузка актуальной структуры из базы данных",
                "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}