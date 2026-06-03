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
        private ReportService _reportService;
        private List<MetadataObject> _catalogs;
        private List<Report> _reports;
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
                _reportService = new ReportService(context);

                _catalogs = await _metadataService.GetCatalogsAsync();
                _reports = await _reportService.GetReportsAsync();

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

                    // Контекстное меню для справочника
                    var contextMenu = new ContextMenu();
                    var deleteMenuItem = new MenuItem { Header = "🗑️ Удалить справочник", Foreground = Brushes.Red };
                    deleteMenuItem.Click += async (s, e) => await DeleteCatalog(catalog);
                    contextMenu.Items.Add(deleteMenuItem);
                    catalogItem.ContextMenu = contextMenu;

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

            // Отчеты
            var reportsItem = new TreeViewItem
            {
                Header = "📊 Отчеты",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };

            if (_reports != null && _reports.Any())
            {
                // В разделе отчетов при создании reportItem
                foreach (var report in _reports.OrderBy(r => r.Name))
                {
                    var reportItem = new TreeViewItem
                    {
                        Header = $"{report.Icon} {report.Name}",
                        Tag = report,
                        Foreground = Brushes.White
                    };

                    // Контекстное меню для отчета
                    var contextMenu = new ContextMenu();
                    var editMenuItem = new MenuItem { Header = "✏️ Редактировать" };
                    editMenuItem.Click += (s, e) => ShowReportEditor(report);
                    contextMenu.Items.Add(editMenuItem);

                    var deleteMenuItem = new MenuItem { Header = "🗑️ Удалить", Foreground = Brushes.Red };
                    deleteMenuItem.Click += async (s, e) => await DeleteReport(report);
                    contextMenu.Items.Add(deleteMenuItem);

                    reportItem.ContextMenu = contextMenu;
                    reportItem.Selected += (s, e) => ShowReportEditor(report);

                    reportsItem.Items.Add(reportItem);
                }
            }
            else
            {
                var emptyItem = new TreeViewItem
                {
                    Header = "   (нет отчетов)",
                    Foreground = Brushes.Gray,
                    IsEnabled = false
                };
                reportsItem.Items.Add(emptyItem);
            }

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
                Width = 110,
                Height = 35,
                Margin = new Thickness(5, 0, 5, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowCatalogEditor(catalog);
            Grid.SetColumn(editButton, 2);

            var deleteButton = new Button
            {
                Content = "🗑️ Удалить",
                Width = 90,
                Height = 35,
                Margin = new Thickness(0, 0, 5, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (s, e) => await DeleteCatalog(catalog);
            Grid.SetColumn(deleteButton, 3);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            grid.Children.Add(deleteButton);
            card.Child = grid;

            return card;
        }

        private void ShowCatalogEditor(MetadataObject catalog)
        {
            EditorTitle.Text = $"✏️ Редактирование: {catalog.Name}";
            EditorDescription.Text = "Основная информация и поля справочника";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel();

            var nameLabel = new TextBlock { Text = "Наименование:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            mainPanel.Children.Add(nameLabel);

            var nameBox = new TextBox { Text = catalog.Name, Height = 30, Margin = new Thickness(0, 0, 0, 15) };
            mainPanel.Children.Add(nameBox);

            var descLabel = new TextBlock { Text = "Описание:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) };
            mainPanel.Children.Add(descLabel);

            var descBox = new TextBox { Text = catalog.Description, Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
            mainPanel.Children.Add(descBox);

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
                fieldName.TextChanged += (s, e) =>
                {
                    field.Name = fieldName.Text;
                    field.DbColumnName = fieldName.Text.ToLower().Replace(" ", "_");
                };
                fieldGrid.Children.Add(fieldName);
                Grid.SetColumn(fieldName, 0);

                var fieldType = new ComboBox { Height = 25, Margin = new Thickness(5) };
                var types = new[] { "String", "Int", "Decimal", "DateTime", "Bool" };
                foreach (var t in types)
                {
                    fieldType.Items.Add(new ComboBoxItem { Content = GetTypeDisplayName(t), Tag = t });
                }
                fieldType.SelectedIndex = Math.Max(0, Array.IndexOf(types, field.FieldType));
                fieldType.SelectionChanged += (s, e) =>
                {
                    var selected = fieldType.SelectedItem as ComboBoxItem;
                    field.FieldType = selected?.Tag?.ToString() ?? "String";
                };
                fieldGrid.Children.Add(fieldType);
                Grid.SetColumn(fieldType, 1);

                var removeFieldButton = new Button
                {
                    Content = "❌",
                    Width = 25,
                    Height = 25,
                    Margin = new Thickness(5),
                    Background = Brushes.Transparent
                };
                removeFieldButton.Click += (s, e) =>
                {
                    var result = MessageBox.Show($"Удалить поле '{field.Name}'?", "Подтверждение",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        catalog.Fields.Remove(field);
                        fieldsList.Children.Remove(fieldPanel);
                    }
                };
                fieldGrid.Children.Add(removeFieldButton);
                Grid.SetColumn(removeFieldButton, 2);

                fieldPanel.Child = fieldGrid;
                fieldsList.Children.Add(fieldPanel);
            }
            mainPanel.Children.Add(fieldsList);

            var addFieldButton = new Button
            {
                Content = "+ Добавить поле",
                Height = 35,
                Margin = new Thickness(0, 10, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addFieldButton.Click += (s, e) =>
            {
                var newField = new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Новое поле",
                    DbColumnName = "new_field",
                    FieldType = "String",
                    Order = catalog.Fields.Count + 1,
                    MetadataObjectId = catalog.Id
                };
                catalog.Fields.Add(newField);
                ShowCatalogEditor(catalog);
            };
            mainPanel.Children.Add(addFieldButton);

            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };

            var saveButton = new Button
            {
                Content = "💾 Сохранить изменения",
                Height = 40,
                Width = 140,
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            saveButton.Click += async (s, e) =>
            {
                catalog.Name = nameBox.Text;
                catalog.Description = descBox.Text;
                await SaveCatalog(catalog);
            };

            var deleteButton = new Button
            {
                Content = "🗑️ Удалить справочник",
                Height = 40,
                Width = 140,
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (s, e) => await DeleteCatalog(catalog);

            buttonsPanel.Children.Add(saveButton);
            buttonsPanel.Children.Add(deleteButton);
            mainPanel.Children.Add(buttonsPanel);

            PropertiesPanel.Children.Add(mainPanel);
        }

        private string GetTypeDisplayName(string type)
        {
            return type switch
            {
                "String" => "Строка",
                "Int" => "Число",
                "Decimal" => "Дробное",
                "DateTime" => "Дата",
                "Bool" => "Логический",
                _ => type
            };
        }

        private async Task DeleteCatalog(MetadataObject catalog)
        {
            var result = MessageBox.Show(
                $"Удалить справочник '{catalog.Name}'?\n\n" +
                "ВНИМАНИЕ! Это действие удалит:\n" +
                "✓ Все данные справочника\n" +
                "✓ Структуру таблицы\n" +
                "✓ Метаданные справочника\n\n" +
                "Восстановление будет невозможно!",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _metadataService.DeleteCatalogAsync(catalog.Id);
                    MessageBox.Show($"Справочник '{catalog.Name}' успешно удален!",
                        "Удаление выполнено",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    await LoadMetadata();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
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
                    var fieldsList = dialog.Fields.ToList();

                    await metadataService.CreateCatalogAsync(
                        dialog.CatalogName,
                        dialog.CatalogDescription,
                        dialog.CatalogIcon,
                        fieldsList
                    );

                    await LoadMetadata();
                    MessageBox.Show($"Справочник \"{dialog.CatalogName}\" успешно создан!",
                        "Успех",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}\n\n{ex.InnerException?.Message}",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private async void OnCreateReportClick(object sender, RoutedEventArgs e)
        {
            var designer = new ReportDesignerWindow();
            designer.Owner = this;

            if (designer.ShowDialog() == true)
            {
                await LoadMetadata();
                MessageBox.Show("Отчет успешно создан!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowReportEditor(Report report)
        {
            var designer = new ReportDesignerWindow(report);
            designer.Owner = this;
            designer.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (designer.ShowDialog() == true)
            {
                _ = LoadMetadata();
            }
        }

        private void OnCreateDocumentClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("В разработке", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadMetadata();
            MessageBox.Show("Метаданные обновлены!", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("BIS ERP Конфигуратор v1.0", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private Border CreateReportCard(Report report)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = report,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Добавляем колонку для кнопки удаления

            var icon = new TextBlock
            {
                Text = report.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = report.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = report.Description,
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {report.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 110,
                Height = 35,
                Margin = new Thickness(5, 0, 5, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowReportEditor(report);
            Grid.SetColumn(editButton, 2);

            // Кнопка удаления отчета
            var deleteButton = new Button
            {
                Content = "🗑️ Удалить",
                Width = 90,
                Height = 35,
                Margin = new Thickness(0, 0, 5, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (s, e) => await DeleteReport(report);
            Grid.SetColumn(deleteButton, 3);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            grid.Children.Add(deleteButton);
            card.Child = grid;

            return card;
        }

        private void OnEditClick(object sender, RoutedEventArgs e)
        {
            // Получаем выбранный элемент в дереве
            var selectedItem = MetadataTree.SelectedItem as TreeViewItem;

            if (selectedItem?.Tag is MetadataObject catalog)
            {
                ShowCatalogEditor(catalog);
            }
            else if (selectedItem?.Tag is Report report)
            {
                ShowReportEditor(report);
            }
            else
            {
                MessageBox.Show("Выберите объект для редактирования",
                    "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            // Получаем выбранный элемент в дереве
            var selectedItem = MetadataTree.SelectedItem as TreeViewItem;

            if (selectedItem?.Tag is MetadataObject catalog)
            {
                await DeleteCatalog(catalog);
            }
            else if (selectedItem?.Tag is Report report)
            {
                await DeleteReport(report);
            }
            else
            {
                MessageBox.Show("Выберите объект для удаления",
                    "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private async void OnImportFrxClick(object sender, RoutedEventArgs e)
        {
            var dialog = new FrxImportWindow();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                await LoadMetadata();
                MessageBox.Show("Отчеты импортированы! Проверьте раздел 'Отчеты'.",
                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void OnSwitchModeClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Выйти в окно выбора режима?\nНесохраненные данные будут потеряны.",
                "Смена режима", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Закрываем конфигуратор и открываем окно выбора режима
                var modeWindow = new InfoBaseSelectionWindow();
                modeWindow.Show();
                this.Close();
            }
        }

        private void OnImportDbfClick(object sender, RoutedEventArgs e)
        {
            var dialog = new DbfImportWindow();
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
        }

        private async Task DeleteReport(Report report)
        {
            var result = MessageBox.Show(
                $"Удалить отчет '{report.Name}'?\n\n" +
                "ВНИМАНИЕ! Это действие удалит отчет без возможности восстановления!",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    await _reportService.DeleteReportAsync(report.Id);

                    MessageBox.Show($"Отчет '{report.Name}' успешно удален!",
                        "Удаление выполнено",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await LoadMetadata();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }
    }
}