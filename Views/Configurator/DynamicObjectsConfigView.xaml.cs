using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Configurator.Views
{
    public partial class DynamicObjectsConfigView : UserControl
    {
        private readonly MetadataService _metadataService;
        private ObservableCollection<MetadataObject> _objects = new();
        private MetadataObject _selectedObject;

        public DynamicObjectsConfigView(MetadataService metadataService)
        {
            InitializeComponent();
            _metadataService = metadataService;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadObjectsAsync();
        }

        private async Task LoadObjectsAsync()
        {
            try
            {
                var catalogs = await _metadataService.GetCatalogsAsync();
                var documents = await _metadataService.GetDocumentsAsync();

                _objects.Clear();
                foreach (var cat in catalogs) _objects.Add(cat);
                foreach (var doc in documents) _objects.Add(doc);

                ObjectsList.ItemsSource = _objects;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        public void LoadObject(MetadataObject obj)
        {
            _selectedObject = obj;
            txtName.Text = obj.Name;
            txtIcon.Text = obj.Icon;
            txtDescription.Text = obj.Description;
            chkPostings.IsChecked = obj.UsePostings;
            chkBalances.IsChecked = obj.UseBalances;
            TitleText.Text = $"Редактирование: {obj.Name}";

            // Заполняем поля
            var fields = obj.Fields?.Select(f => new
            {
                f.Name,
                f.DbColumnName,
                f.FieldType,
                f.IsRequired,
                f.Order
            }).ToList() ?? new();
            FieldsGrid.ItemsSource = fields;
        }

        private void OnObjectSelected(object sender, SelectionChangedEventArgs e)
        {
            _selectedObject = ObjectsList.SelectedItem as MetadataObject;
            if (_selectedObject != null)
            {
                txtName.Text = _selectedObject.Name;
                txtIcon.Text = _selectedObject.Icon;
                txtDescription.Text = _selectedObject.Description;
                chkPostings.IsChecked = _selectedObject.UsePostings;
                chkBalances.IsChecked = _selectedObject.UseBalances;
                TitleText.Text = $"Редактирование: {_selectedObject.Name}";

                // Заполняем поля
                var fields = _selectedObject.Fields?.Select(f => new
                {
                    f.Name,
                    f.DbColumnName,
                    f.FieldType,
                    f.IsRequired,
                    f.Order
                }).ToList() ?? new();
                FieldsGrid.ItemsSource = fields;
            }
        }

        private void OnCreateObjectClick(object sender, RoutedEventArgs e)
        {
            txtName.Text = "";
            txtIcon.Text = "📄";
            txtDescription.Text = "";
            chkPostings.IsChecked = false;
            chkBalances.IsChecked = false;
            FieldsGrid.ItemsSource = null;
            _selectedObject = null;
            TitleText.Text = "Создание нового объекта";
        }

        private void OnAddFieldClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Добавление полей в разработке", "Инфо");
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedObject == null)
                {
                    // Создание нового объекта
                    var selectedType = cmbType.SelectedItem as ComboBoxItem;
                    var objectType = selectedType?.Tag?.ToString() ?? "Catalog";

                    var newObject = new MetadataObject
                    {
                        Id = Guid.NewGuid(),
                        Name = txtName.Text,
                        ObjectType = objectType,
                        Icon = txtIcon.Text,
                        Description = txtDescription.Text,
                        TableName = $"{objectType}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                        UsePostings = chkPostings.IsChecked ?? false,
                        UseBalances = chkBalances.IsChecked ?? false,
                        UseMovements = false,
                        IsSystem = false,
                        Order = 1
                    };

                    await _metadataService.CreateMetadataObjectAsync(newObject);
                    await _metadataService.CreateDynamicTableAsync(newObject);
                    MessageBox.Show("Объект создан!", "Успех");
                }
                else
                {
                    // Обновление существующего
                    _selectedObject.Name = txtName.Text;
                    _selectedObject.Icon = txtIcon.Text;
                    _selectedObject.Description = txtDescription.Text;
                    _selectedObject.UsePostings = chkPostings.IsChecked ?? false;
                    _selectedObject.UseBalances = chkBalances.IsChecked ?? false;

                    // Исправлено: передаем _selectedObject, а не obj
                    await _metadataService.UpdateMetadataObjectAsync(_selectedObject);
                    MessageBox.Show("Объект сохранен!", "Успех");
                }

                await LoadObjectsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }

        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_selectedObject == null) return;

            var result = MessageBox.Show($"Удалить '{_selectedObject.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _metadataService.DeleteMetadataObjectAsync(_selectedObject.Id);
                await LoadObjectsAsync();
                _selectedObject = null;
                TitleText.Text = "Выберите объект";
                txtName.Text = "";
            }
        }
    }
}