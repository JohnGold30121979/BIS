using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Views
{
    public partial class ConfiguratorWindow : Window
    {
        private MetadataService _metadataService;
        private List<MetadataObject> _catalogs;
        private MetadataObject _selectedCatalog;
        private bool _isLoading = false;

        public ConfiguratorWindow()
        {
            InitializeComponent();
            this.Loaded += async (s, e) => await LoadMetadata();
        }

        private async Task LoadMetadata()
        {
            if (_isLoading) return;
            _isLoading = true;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                _metadataService = new MetadataService(context);

                _catalogs = await _metadataService.GetCatalogsAsync();

                BuildMetadataTree();

                if (_catalogs != null && _catalogs.Any())
                {
                    ShowCatalogsList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки метаданных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isLoading = false;
            }
        }

        private void BuildMetadataTree()
        {
            MetadataTree.Items.Clear();

            // Корневой элемент
            var rootItem = new TreeViewItem
            {
                Header = "📁 Метаданные",
                IsExpanded = true,
                Foreground = Brushes.White
            };

            // Справочники
            var catalogsItem = new TreeViewItem
            {
                Header = "📚 Справочники",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };

            if (_catalogs != null && _catalogs.Any())
            {
                foreach (var catalog in _catalogs.OrderBy(c => c.Name))
                {
                    var catalogItem = new TreeViewItem
                    {
                        Header = $"{catalog.Icon} {catalog.Name}",
                        Tag = catalog,
                        Foreground = Brushes.White
                    };
                    catalogItem.Selected += (s, e) =>
                    {
                        _selectedCatalog = catalog;
                        ShowCatalogEditor(catalog);
                    };
                    catalogsItem.Items.Add(catalogItem);
                }
            }
            else
            {
                var emptyItem = new TreeViewItem
                {
                    Header = "   (нет справочников)",
                    Foreground = Brushes.Gray,
                    IsEnabled = false
                };
                catalogsItem.Items.Add(emptyItem);
            }

            catalogsItem.Selected += (s, e) => ShowCatalogsList();

            // Документы (заглушка)
            var documentsItem = new TreeViewItem
            {
                Header = "📄 Документы",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };
            var emptyDocItem = new TreeViewItem
            {
                Header = "   (в разработке)",
                Foreground = Brushes.Gray,
                IsEnabled = false
            };
            documentsItem.Items.Add(emptyDocItem);

            // Отчеты (заглушка)
            var reportsItem = new TreeViewItem
            {
                Header = "📊 Отчеты",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };
            var emptyReportItem = new TreeViewItem
            {
                Header = "   (в разработке)",
                Foreground = Brushes.Gray,
                IsEnabled = false
            };
            reportsItem.Items.Add(emptyReportItem);

            rootItem.Items.Add(catalogsItem);
            rootItem.Items.Add(documentsItem);
            rootItem.Items.Add(reportsItem);

            MetadataTree.Items.Add(rootItem);
        }

        private void ShowCatalogsList()
        {
            EditorTitle.Text = "📚 Справочники";
            EditorDescription.Text = "Список всех справочников в системе";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();

            var createButton = new Button
            {
                Content = "➕ Создать справочник",
                Height = 40,
                Margin = new Thickness(0, 0, 0, 20),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            createButton.Click += (s, e) => OnCreateCatalogClick(s, e);
            stackPanel.Children.Add(createButton);

            if (_catalogs != null)
            {
                foreach (var catalog in _catalogs.OrderBy(c => c.Name))
                {
                    var card = CreateCatalogCard(catalog);
                    stackPanel.Children.Add(card);
                }
            }

            if (_catalogs == null || !_catalogs.Any())
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Нет созданных справочников. Нажмите 'Создать справочник' для добавления.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 50, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            PropertiesPanel.Children.Add(stackPanel);
        }

        private Border CreateCatalogCard(MetadataObject catalog)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = catalog,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = catalog.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = catalog.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = catalog.TableName,
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {catalog.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 100,
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowCatalogEditor(catalog);
            Grid.SetColumn(editButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            card.Child = grid;

            return card;
        }

        private void ShowCatalogEditor(MetadataObject catalog)
        {
            EditorTitle.Text = $"✏️ Редактирование: {catalog.Name}";
            EditorDescription.Text = "Основная информация и поля справочника";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel();

            // Основная информация
            var nameLabel = new TextBlock { Text = "Наименование:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            mainPanel.Children.Add(nameLabel);

            var nameBox = new TextBox { Text = catalog.Name, Height = 30, Margin = new Thickness(0, 0, 0, 15) };
            mainPanel.Children.Add(nameBox);

            var descLabel = new TextBlock { Text = "Описание:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            mainPanel.Children.Add(descLabel);

            var descBox = new TextBox { Text = catalog.Description, Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            mainPanel.Children.Add(descBox);

            // Поля справочника
            var fieldsLabel = new TextBlock { Text = "Поля справочника:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 10), FontSize = 14 };
            mainPanel.Children.Add(fieldsLabel);

            var fieldsList = new StackPanel();
            foreach (var field in catalog.Fields.OrderBy(f => f.Order))
            {
                var fieldPanel = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFrom("#F8F9FA"),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 5)
                };
                var fieldGrid = new Grid();
                fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var fieldName = new TextBox { Text = field.Name, Height = 25, Margin = new Thickness(5) };
                fieldGrid.Children.Add(fieldName);
                Grid.SetColumn(fieldName, 0);

                var fieldType = new TextBlock { Text = field.FieldType, Height = 25, Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center };
                fieldGrid.Children.Add(fieldType);
                Grid.SetColumn(fieldType, 1);

                fieldPanel.Child = fieldGrid;
                fieldsList.Children.Add(fieldPanel);
            }
            mainPanel.Children.Add(fieldsList);

            // Кнопка сохранения
            var saveButton = new Button
            {
                Content = "💾 Сохранить изменения",
                Height = 40,
                Margin = new Thickness(0, 20, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            saveButton.Click += async (s, e) =>
            {
                catalog.Name = nameBox.Text;
                catalog.Description = descBox.Text;
                await SaveCatalog(catalog);
            };
            mainPanel.Children.Add(saveButton);

            PropertiesPanel.Children.Add(mainPanel);
        }

        private async Task SaveCatalog(MetadataObject catalog)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await _metadataService.SaveCatalogAsync(catalog);
                MessageBox.Show("Справочник сохранен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadMetadata();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // В методе OnCreateCatalogClick
        private async void OnCreateCatalogClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateCatalogDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                    var metadataService = new MetadataService(context);

                    // Теперь типы совпадают - не нужно преобразование!
                    var fieldsList = dialog.Fields.ToList();

                    await metadataService.CreateCatalogAsync(
                        dialog.CatalogName,
                        dialog.CatalogDescription,
                        dialog.CatalogIcon,
                        fieldsList
                    );

                    await LoadMetadata();
                    MessageBox.Show($"Справочник '{dialog.CatalogName}' создан!", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void OnCreateDocumentClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("В разработке", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCreateReportClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("В разработке", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadMetadata();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("BIS ERP Конфигуратор v1.0", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}