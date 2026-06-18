using BIS.ERP.Configurator.Views;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views
{
    public partial class ConfiguratorWindow : Window
    {
        private MetadataService _metadataService;
        private ReportService _reportService;
        private List<MetadataObject> _catalogs;
        private List<MetadataObject> _documents;
        private List<Report> _reports;
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

                var allMetadata = await _metadataService.GetAllMetadataObjectsAsync();
                _catalogs = allMetadata.Where(m => m.ObjectType == "Catalog").OrderBy(m => m.Order).ToList();
                _documents = allMetadata.Where(m => m.ObjectType == "Document").OrderBy(m => m.Order).ToList();
                _reports = await _reportService.GetReportsAsync();

                BuildMetadataTree();
                ShowCatalogsList();
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
            catalogsItem.Selected += (s, e) => ShowCatalogsList();

            if (_catalogs != null && _catalogs.Any())
            {
                foreach (var catalog in _catalogs)
                {
                    var catalogItem = new TreeViewItem
                    {
                        Header = $"{catalog.Icon} {catalog.Name}",
                        Tag = catalog,
                        Foreground = Brushes.White
                    };
                    catalogItem.Selected += (s, e) => ShowCatalogEditor(catalog);
                    catalogsItem.Items.Add(catalogItem);
                }
            }

            // Документы
            var documentsItem = new TreeViewItem
            {
                Header = "📄 Документы",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };
            documentsItem.Selected += (s, e) => ShowDocumentsList();

            if (_documents != null && _documents.Any())
            {
                foreach (var doc in _documents)
                {
                    var docItem = new TreeViewItem
                    {
                        Header = $"{doc.Icon} {doc.Name}",
                        Tag = doc,
                        Foreground = Brushes.White
                    };
                    docItem.Selected += (s, e) => ShowDocumentEditor(doc);
                    documentsItem.Items.Add(docItem);
                }
            }

            // Отчеты
            var reportsItem = new TreeViewItem
            {
                Header = "📊 Отчеты",
                IsExpanded = true,
                Foreground = (Brush)new BrushConverter().ConvertFrom("#BDC3C7")
            };

            if (_reports != null && _reports.Any())
            {
                foreach (var report in _reports)
                {
                    var reportItem = new TreeViewItem
                    {
                        Header = $"{report.Icon} {report.Name}",
                        Tag = report,
                        Foreground = Brushes.White
                    };
                    reportItem.Selected += (s, e) => ShowReportEditor(report);
                    reportsItem.Items.Add(reportItem);
                }
            }

            rootItem.Items.Add(catalogsItem);
            rootItem.Items.Add(documentsItem);
            rootItem.Items.Add(reportsItem);
            MetadataTree.Items.Add(rootItem);
        }

        // ДИНАМИЧЕСКИЕ МЕТОДЫ СОЗДАНИЯ
        private async void OnCreateDynamicCatalogClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateDynamicObjectDialog("Catalog");
            dialog.Owner = this;
            dialog.Title = "Создание справочника";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var newObject = new MetadataObject
                    {
                        Name = dialog.ObjectName,
                        ObjectType = "Catalog",
                        Description = dialog.Description,
                        Icon = dialog.Icon,
                        TableName = $"catalog_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                        Order = (_catalogs?.Count ?? 0) + 1,
                        IsSystem = false,
                        UsePostings = false,
                        UseBalances = false,
                        UseMovements = false,
                        Fields = new List<MetadataField>()
                    };

                    await _metadataService.CreateMetadataObjectAsync(newObject);
                    await _metadataService.CreateDynamicTableAsync(newObject);
                    await LoadMetadata();

                    MessageBox.Show($"Справочник '{dialog.ObjectName}' успешно создан!",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private async void OnCreateDynamicDocumentClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateDynamicObjectDialog("Document");
            dialog.Owner = this;
            dialog.Title = "Создание документа";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var newObject = new MetadataObject
                    {
                        Name = dialog.ObjectName,
                        ObjectType = "Document",
                        Description = dialog.Description,
                        Icon = dialog.Icon,
                        TableName = $"doc_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                        Order = (_documents?.Count ?? 0) + 1,
                        IsSystem = false,
                        UsePostings = dialog.UsePostings,
                        UseBalances = dialog.UseBalances,
                        UseMovements = false,
                        Fields = new List<MetadataField>()
                    };

                    await _metadataService.CreateMetadataObjectAsync(newObject);
                    await _metadataService.CreateDynamicTableAsync(newObject);
                    await LoadMetadata();

                    // ОТКРЫВАЕМ РЕДАКТОР ДЛЯ СОЗДАННОГО ОБЪЕКТА
                    var editorView = new DynamicObjectsConfigView(_metadataService);
                    // Нужно передать выбранный объект в редактор
                    editorView.LoadObject(newObject);

                    // Открываем в новой вкладке или окне
                    var tabItem = new TabItem { Header = newObject.Name, Content = editorView };
                    // Добавляем в TabControl (если есть) или открываем окно

                    MessageBox.Show($"Документ '{dialog.ObjectName}' успешно создан! Теперь добавьте поля.",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }



        // ОТОБРАЖЕНИЕ СПИСКОВ
        private void ShowCatalogsList()
        {
            EditorTitle.Text = "📚 Справочники";
            EditorDescription.Text = "Список справочников в системе";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();

            if (_catalogs != null && _catalogs.Any())
            {
                foreach (var catalog in _catalogs)
                {
                    var card = CreateCatalogCard(catalog);
                    stackPanel.Children.Add(card);
                }
            }
            else
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Нет созданных справочников.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 50, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            PropertiesPanel.Children.Add(stackPanel);
        }

        // Добавьте отладку в метод ShowDocumentsList
        private async Task ShowDocumentsList()
        {
            EditorTitle.Text = "📄 Документы";
            EditorDescription.Text = "Управление документами";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();

            // Кнопка импорта DBF
            var importButton = new Button
            {
                Content = "📁 Импорт из DBF",
                Height = 40,
                Margin = new Thickness(0, 0, 0, 20),
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            importButton.Click += (s, e) => OnImportDbfClick(s, e);
            stackPanel.Children.Add(importButton);

            // Разделитель
            stackPanel.Children.Add(new TextBlock
            {
                Text = "--- Динамические документы (метаданные) ---",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 10, 0, 5),
                FontWeight = FontWeights.Bold
            });

            // Показываем динамические документы (метаданные)
            if (_documents != null && _documents.Any())
            {
                foreach (var doc in _documents)
                {
                    var card = CreateDynamicMetadataCard(doc);
                    stackPanel.Children.Add(card);
                }
            }
            else
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Нет динамических документов. Нажмите 'Создать документ' в меню для добавления.",
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 5, 0, 5),
                    FontSize = 11
                });
            }

            // Разделитель
            stackPanel.Children.Add(new TextBlock
            {
                Text = "--- Импортированные DBF документы (данные) ---",
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 15, 0, 5),
                FontWeight = FontWeights.Bold
            });

            // Показываем импортированные DBF документы
            try
            {
                var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
                var documentService = new DocumentService(context);
                var documents = await documentService.GetDocumentsAsync();

                foreach (var doc in documents.OrderByDescending(d => d.Date))
                {
                    var card = CreateDynamicDocumentCard(doc);
                    stackPanel.Children.Add(card);
                }

                if (!documents.Any())
                {
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = "Нет импортированных DBF документов. Нажмите 'Импорт из DBF' для загрузки.",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(0, 50, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
            }
            catch (Exception ex)
            {
                stackPanel.Children.Add(new TextBlock
                {
                    Text = $"Ошибка загрузки DBF документов: {ex.Message}",
                    Foreground = Brushes.Red,
                    Margin = new Thickness(10)
                });
            }

            PropertiesPanel.Children.Add(stackPanel);
        }

        // Карточка для динамического документа (метаданных)
        private Border CreateDynamicMetadataCard(MetadataObject doc)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = doc,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = doc.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = doc.Name,
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Таблица: {doc.TableName}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {doc.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 80,
                Height = 30,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowDocumentEditor(doc);
            Grid.SetColumn(editButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            card.Child = grid;

            return card;
        }

        // Создание карточки для DynamicDocument
        private Border CreateDynamicDocumentCard(DynamicDocument doc)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = "📄",
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = $"№{doc.Number} от {doc.Date:dd.MM.yyyy HH:mm:ss}",
                FontSize = 14,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Тип: {doc.DocumentType}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Строк: {doc.TotalRows} | Файл: {doc.SourceFile}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var detailButton = new Button
            {
                Content = "📋 Детали",
                Width = 80,
                Height = 30,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            detailButton.Click += async (s, e) => await ShowDynamicDocumentDetails(doc);
            Grid.SetColumn(detailButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(detailButton);
            card.Child = grid;

            return card;
        }

        private async Task ShowOperationsList()
        {
            EditorTitle.Text = "📋 Операции";
            EditorDescription.Text = "Список всех операций";
            PropertiesPanel.Children.Clear();

            var stackPanel = new StackPanel();

            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var documentService = new DocumentService(context);
            var documents = await documentService.GetDocumentsAsync();

            // Таблица с документами
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 400,
                Margin = new Thickness(0, 0, 0, 10)
            };

            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Номер", Binding = new System.Windows.Data.Binding("Number"), Width = 120 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Дата", Binding = new System.Windows.Data.Binding("Date"), Width = 120 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Сумма", Binding = new System.Windows.Data.Binding("TotalAmount"), Width = 100 });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Статус", Binding = new System.Windows.Data.Binding("IsPosted"), Width = 80 });

            dataGrid.ItemsSource = documents;
            stackPanel.Children.Add(dataGrid);

            // Кнопка обновления
            var refreshButton = new Button
            {
                Content = "🔄 Обновить",
                Width = 100,
                Height = 35,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            refreshButton.Click += async (s, e) => await ShowOperationsList();
            stackPanel.Children.Add(refreshButton);

            PropertiesPanel.Children.Add(stackPanel);
        }

        // Показ деталей документа
        private async Task ShowDynamicDocumentDetails(DynamicDocument doc)
        {
            var context = await ServiceLocator.InfoBaseManager.GetCurrentDbContextAsync();
            var documentService = new DocumentService(context);

            // Получаем первую строку для отображения полей
            var firstRow = doc.Rows?.FirstOrDefault();
            Dictionary<string, object> rowData = new Dictionary<string, object>();

            if (firstRow != null)
            {
                rowData = documentService.GetRowData(firstRow);
            }

            var details = $"📄 Документ №{doc.Number}\n" +
                          $"📅 Дата: {doc.Date:dd.MM.yyyy HH:mm:ss}\n" +
                          $"📂 Тип: {doc.DocumentType}\n" +
                          $"📁 Файл: {doc.SourceFile}\n" +
                          $"📊 Строк: {doc.TotalRows}\n\n" +
                          $"📋 Поля в документе:\n";

            foreach (var field in rowData.Keys.OrderBy(k => k))
            {
                var value = rowData[field]?.ToString();
                if (value?.Length > 50) value = value.Substring(0, 50) + "...";
                details += $"  • {field}: {value}\n";
            }

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                Width = 500,
                Content = new TextBlock
                {
                    Text = details,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Margin = new Thickness(10)
                }
            };

            var window = new Window
            {
                Title = $"Детали документа №{doc.Number}",
                Content = scrollViewer,
                Width = 550,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };
            window.ShowDialog();
        }
        private Border CreateCatalogCard(MetadataObject obj)
        {
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 10),
                Tag = obj,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = obj.Icon,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(icon, 0);

            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = obj.Name,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = obj.TableName,
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            infoStack.Children.Add(new TextBlock
            {
                Text = $"Полей: {obj.Fields?.Count ?? 0}",
                FontSize = 11,
                Foreground = Brushes.Gray
            });
            Grid.SetColumn(infoStack, 1);

            var editButton = new Button
            {
                Content = "✏️ Редактировать",
                Width = 110,
                Height = 35,
                Background = (Brush)new BrushConverter().ConvertFrom("#3498DB"),
                Foreground = Brushes.White
            };
            editButton.Click += (s, e) => ShowCatalogEditor(obj);
            Grid.SetColumn(editButton, 2);

            grid.Children.Add(icon);
            grid.Children.Add(infoStack);
            grid.Children.Add(editButton);
            card.Child = grid;

            return card;
        }

        private void ShowCatalogEditor(MetadataObject obj)
        {
            EditorTitle.Text = $"✏️ Редактирование: {obj.Name}";
            EditorDescription.Text = $"Таблица: {obj.TableName} | Тип: {(obj.ObjectType == "Catalog" ? "Справочник" : "Документ")}";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel();
            var tabControl = new TabControl { Margin = new Thickness(0, 0, 0, 10) };

            // ==================== ВКЛАДКА "ОСНОВНЫЕ" ====================
            var basicTab = new TabItem { Header = "📋 Основные" };
            var basicPanel = new StackPanel { Margin = new Thickness(10) };

            basicPanel.Children.Add(new TextBlock { Text = "Наименование:", FontWeight = FontWeights.Bold });
            var nameBox = new TextBox { Text = obj.Name, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(nameBox);

            basicPanel.Children.Add(new TextBlock { Text = "Описание:", FontWeight = FontWeights.Bold });
            var descBox = new TextBox { Text = obj.Description, Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(descBox);

            basicPanel.Children.Add(new TextBlock { Text = "Иконка:", FontWeight = FontWeights.Bold });
            var iconBox = new TextBox { Text = obj.Icon, Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(iconBox);

            basicPanel.Children.Add(new TextBlock { Text = "Порядок:", FontWeight = FontWeights.Bold });
            var orderBox = new TextBox { Text = obj.Order.ToString(), Margin = new Thickness(0, 5, 0, 15) };
            basicPanel.Children.Add(orderBox);

            var optionsPanel = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) };
            var usePostingsCheck = new CheckBox { Content = "Использует проводки", IsChecked = obj.UsePostings, Margin = new Thickness(0, 0, 15, 0) };
            var useBalancesCheck = new CheckBox { Content = "Использует балансы", IsChecked = obj.UseBalances, Margin = new Thickness(0, 0, 15, 0) };
            var useMovementsCheck = new CheckBox { Content = "Использует движения", IsChecked = obj.UseMovements };
            optionsPanel.Children.Add(usePostingsCheck);
            optionsPanel.Children.Add(useBalancesCheck);
            optionsPanel.Children.Add(useMovementsCheck);
            basicPanel.Children.Add(optionsPanel);

            basicTab.Content = basicPanel;
            tabControl.Items.Add(basicTab);

            // ==================== ВКЛАДКА "ПОЛЯ" ====================
            var fieldsTab = new TabItem { Header = "📊 Поля" };
            var fieldsPanel = new StackPanel { Margin = new Thickness(10) };

            var fieldsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 300,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Имя поля", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Колонка БД", Binding = new System.Windows.Data.Binding("DbColumnName"), Width = 150 });
            fieldsGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Тип",
                SelectedItemBinding = new System.Windows.Data.Binding("FieldType"),
                ItemsSource = new List<string> { "String", "Int", "Decimal", "DateTime", "Bool", "Reference"},
                Width = 100
            });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Длина", Binding = new System.Windows.Data.Binding("Length"), Width = 80 });
            fieldsGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Обязательное", Binding = new System.Windows.Data.Binding("IsRequired"), Width = 100 });
            fieldsGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Уникальное", Binding = new System.Windows.Data.Binding("IsUnique"), Width = 100 });
            fieldsGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("Order"), Width = 80 });

            // ========== КОЛОНКА "СПРАВОЧНИК" - ТЕПЕРЬ С ВЫПАДАЮЩИМ СПИСКОМ ==========
            var referenceColumn = new DataGridComboBoxColumn
            {
                Header = "Справочник",
                SelectedValueBinding = new System.Windows.Data.Binding("ReferenceCatalog"),
                Width = 150
            };

            // ========== НОВЫЕ ПОЛЯ ==========
            // DisplayPattern - шаблон отображения
            var displayPatternColumn = new DataGridTextColumn
            {
                Header = "Шаблон отображения",
                Binding = new System.Windows.Data.Binding("DisplayPattern"),
                Width = 150
            };
            fieldsGrid.Columns.Add(displayPatternColumn);

            // DisplayFields - поля для подстановки
            var displayFieldsColumn = new DataGridTextColumn
            {
                Header = "Поля для подстановки",
                Binding = new System.Windows.Data.Binding("DisplayFields"),
                Width = 150
            };
            fieldsGrid.Columns.Add(displayFieldsColumn);

            // Загружаем список доступных справочников
            var catalogNames = _catalogs?.Select(c => c.Name).ToList() ?? new List<string>();
            referenceColumn.ItemsSource = catalogNames;
            fieldsGrid.Columns.Add(referenceColumn);

            var fieldsList = new System.Collections.ObjectModel.ObservableCollection<MetadataField>();
            if (obj.Fields != null)
            {
                // Удаляем дубликаты по имени
                var uniqueFields = obj.Fields
                    .GroupBy(f => f.Name)
                    .Select(g => g.First())
                    .ToList();

                foreach (var field in uniqueFields.OrderBy(f => f.Order))
                {
                    fieldsList.Add(field);
                }
            }
            fieldsGrid.ItemsSource = fieldsList;

            fieldsPanel.Children.Add(fieldsGrid);

            var addFieldButton = new Button
            {
                Content = "+ Добавить поле",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addFieldButton.Click += (s, args) =>
            {
                fieldsList.Add(new MetadataField
                {
                    Id = Guid.NewGuid(),
                    Name = "Новое_поле",
                    DbColumnName = "new_field",
                    FieldType = "String",
                    Length = 100,
                    Order = fieldsList.Count + 1,
                    MetadataObjectId = obj.Id,
                    ReferenceCatalog = null,
                    DisplayPattern = null,     
                    DisplayFields = null 
                });
            };
            fieldsPanel.Children.Add(addFieldButton);

            fieldsTab.Content = fieldsPanel;
            tabControl.Items.Add(fieldsTab);

            // ==================== ВКЛАДКА "РАСЧЕТЫ" ====================
            var calculationsTab = new TabItem { Header = "🧮 Расчеты" };
            var calcPanel = new StackPanel { Margin = new Thickness(10) };

            var calcGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 200,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Наименование", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            calcGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Тип расчета",
                SelectedItemBinding = new System.Windows.Data.Binding("CalculationType"),
                ItemsSource = new List<string> { "Depreciation", "Sum", "Average", "Formula" },
                Width = 150
            });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Целевое поле", Binding = new System.Windows.Data.Binding("TargetField"), Width = 150 });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Формула", Binding = new System.Windows.Data.Binding("Formula"), Width = 200 });
            calcGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Авто", Binding = new System.Windows.Data.Binding("IsAuto"), Width = 80 });
            calcGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("ExecutionOrder"), Width = 80 });

            var calcList = new System.Collections.ObjectModel.ObservableCollection<MetadataCalculation>();
            if (obj.Calculations != null)
            {
                foreach (var calc in obj.Calculations)
                {
                    calcList.Add(calc);
                }
            }
            calcGrid.ItemsSource = calcList;

            calcPanel.Children.Add(calcGrid);

            var addCalcButton = new Button
            {
                Content = "+ Добавить расчет",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addCalcButton.Click += (s, args) =>
            {
                calcList.Add(new MetadataCalculation
                {
                    Id = Guid.NewGuid(),
                    Name = "Новый_расчет",
                    CalculationType = "Formula",
                    IsAuto = true,
                    ExecutionOrder = calcList.Count + 1,
                    MetadataObjectId = obj.Id
                });
            };
            calcPanel.Children.Add(addCalcButton);

            calculationsTab.Content = calcPanel;
            tabControl.Items.Add(calculationsTab);

            // ==================== ВКЛАДКА "ПРОВОДКИ" ====================
            var postingsTab = new TabItem { Header = "📝 Проводки" };
            var postingsPanel = new StackPanel { Margin = new Thickness(10) };

            var postingGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                Height = 250,
                Margin = new Thickness(0, 0, 0, 10),
                CanUserAddRows = true,
                CanUserDeleteRows = true
            };

            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Наименование", Binding = new System.Windows.Data.Binding("Name"), Width = 150 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Дебет", Binding = new System.Windows.Data.Binding("DebitAccountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Кредит", Binding = new System.Windows.Data.Binding("CreditAccountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Сумма", Binding = new System.Windows.Data.Binding("AmountExpression"), Width = 120 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Условие", Binding = new System.Windows.Data.Binding("Condition"), Width = 150 });
            postingGrid.Columns.Add(new DataGridTextColumn { Header = "Порядок", Binding = new System.Windows.Data.Binding("Order"), Width = 80 });

            var postingList = new System.Collections.ObjectModel.ObservableCollection<MetadataPostingRule>();
            if (obj.PostingRules != null)
            {
                foreach (var rule in obj.PostingRules)
                {
                    postingList.Add(rule);
                }
            }
            postingGrid.ItemsSource = postingList;

            postingsPanel.Children.Add(postingGrid);

            var addRuleButton = new Button
            {
                Content = "+ Добавить проводку",
                Height = 30,
                Width = 120,
                Margin = new Thickness(0, 5, 0, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            addRuleButton.Click += (s, args) =>
            {
                postingList.Add(new MetadataPostingRule
                {
                    Id = Guid.NewGuid(),
                    Name = "Новая_проводка",
                    DebitAccountExpression = "{AccountDebet}",
                    CreditAccountExpression = "{AccountCredit}",
                    AmountExpression = "{Amount}",
                    Order = postingList.Count + 1,
                    MetadataObjectId = obj.Id
                });
            };
            postingsPanel.Children.Add(addRuleButton);

            // Подсказка по выражениям
            var hintText = new TextBlock
            {
                Text = "💡 Доступные выражения: {FieldName} - подстановка значения поля\nПример: {Amount} * 0.2 - НДС 20%",
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            postingsPanel.Children.Add(hintText);

            postingsTab.Content = postingsPanel;
            tabControl.Items.Add(postingsTab);

            mainPanel.Children.Add(tabControl);

            // Кнопки сохранения и удаления
            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };

            var saveButton = new Button
            {
                Content = "💾 Сохранить",
                Height = 35,
                Width = 120,
                Margin = new Thickness(0, 0, 10, 0),
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            saveButton.Click += async (s, e) =>
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    obj.Name = nameBox.Text;
                    obj.Description = descBox.Text;
                    obj.Icon = iconBox.Text;
                    obj.Order = int.TryParse(orderBox.Text, out var order) ? order : obj.Order;
                    obj.UsePostings = usePostingsCheck.IsChecked ?? false;
                    obj.UseBalances = useBalancesCheck.IsChecked ?? false;
                    obj.UseMovements = useMovementsCheck.IsChecked ?? false;
                    obj.Fields = fieldsList.ToList();
                    obj.Calculations = calcList.ToList();
                    obj.PostingRules = postingList.ToList();

                    await _metadataService.UpdateMetadataObjectAsync(obj);
                    await LoadMetadata();

                    MessageBox.Show("Сохранено!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            };

            var deleteButton = new Button
            {
                Content = "🗑️ Удалить",
                Height = 35,
                Width = 120,
                Background = (Brush)new BrushConverter().ConvertFrom("#E74C3C"),
                Foreground = Brushes.White
            };
            deleteButton.Click += async (s, e) => await DeleteDynamicObject(obj);

            buttonsPanel.Children.Add(saveButton);
            buttonsPanel.Children.Add(deleteButton);
            mainPanel.Children.Add(buttonsPanel);

            PropertiesPanel.Children.Add(mainPanel);
        }

        private void ShowDocumentEditor(MetadataObject obj)
        {
            ShowCatalogEditor(obj); // Используем тот же редактор, так как функционал одинаковый
        }      

        private void ShowReportEditor(Report report)
        {
            EditorTitle.Text = $"✏️ Редактирование отчета: {report.Name}";
            EditorDescription.Text = "";
            PropertiesPanel.Children.Clear();

            var mainPanel = new StackPanel();

            mainPanel.Children.Add(new TextBlock { Text = "Наименование:", FontWeight = FontWeights.Bold });
            var nameBox = new TextBox { Text = report.Name, Margin = new Thickness(0, 5, 0, 15) };
            mainPanel.Children.Add(nameBox);

            mainPanel.Children.Add(new TextBlock { Text = "Описание:", FontWeight = FontWeights.Bold });
            var descBox = new TextBox { Text = report.Description, Height = 60, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 15) };
            mainPanel.Children.Add(descBox);

            var saveButton = new Button
            {
                Content = "💾 Сохранить",
                Height = 35,
                Width = 120,
                Background = (Brush)new BrushConverter().ConvertFrom("#27AE60"),
                Foreground = Brushes.White
            };
            saveButton.Click += async (s, e) =>
            {
                report.Name = nameBox.Text;
                report.Description = descBox.Text;
                await _reportService.UpdateReportAsync(report);
                await LoadMetadata();
                MessageBox.Show("Сохранено!", "Успех");
            };

            mainPanel.Children.Add(saveButton);
            PropertiesPanel.Children.Add(mainPanel);
        }

        // ОСТАЛЬНЫЕ МЕТОДЫ
        private async void OnCreateCatalogClick(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateCatalogDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnCreateReportClick(object sender, RoutedEventArgs e)
        {
            var designer = new ReportDesignerWindow();
            designer.Owner = this;
            if (designer.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnImportFrxClick(object sender, RoutedEventArgs e)
        {
            var dialog = new FrxImportWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                await LoadMetadata();
            }
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadMetadata();
        }

        private void OnSwitchModeClick(object sender, RoutedEventArgs e)
        {
            var modeWindow = new InfoBaseSelectionWindow();
            modeWindow.Show();
            this.Close();
        }

        private void OnImportDbfClick(object sender, RoutedEventArgs e)
        {
            var dialog = new DbfImportWindow();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("BIS ERP Конфигуратор v1.0", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private async Task DeleteDynamicObject(MetadataObject obj)
        {
            var result = MessageBox.Show(
                $"Удалить объект '{obj.Name}'?\n\nВНИМАНИЕ! Все данные будут потеряны!",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    await _metadataService.DeleteMetadataObjectAsync(obj.Id);
                    await LoadMetadata();

                    MessageBox.Show("Объект удален!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }
    }
}
