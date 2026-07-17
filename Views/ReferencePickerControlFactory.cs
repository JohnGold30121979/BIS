using BIS.ERP.Models;
using BIS.ERP.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BIS.ERP.Views
{
    public sealed class ReferencePickerControl : UserControl
    {
        internal ReferencePickerControl(ComboBox comboBox)
        {
            ComboBox = comboBox;
        }

        public ComboBox ComboBox { get; }

        public ReferenceItem? SelectedReferenceItem
        {
            get => ComboBox.SelectedItem as ReferenceItem;
            set => ComboBox.SelectedItem = value;
        }

        public void ClearSelection()
        {
            ComboBox.SelectedItem = null;
            ComboBox.Text = string.Empty;
        }
    }

    public static class ReferencePickerControlFactory
    {
        private static readonly DependencyProperty InlineButtonsAddedProperty =
            DependencyProperty.RegisterAttached(
                "InlineButtonsAdded",
                typeof(bool),
                typeof(ReferencePickerControlFactory),
                new PropertyMetadata(false));

        public static async Task<ReferencePickerControl> CreateAsync(
            MetadataService metadataService,
            MetadataField field,
            MetadataObject referenceCatalog,
            object? currentValue,
            Window owner,
            Action? selectionChanged = null)
        {
            var comboBox = CreateComboBox(field.Name);
            var picker = new ReferencePickerControl(comboBox)
            {
                MinWidth = 220
            };

            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(comboBox, 0);
            panel.Children.Add(comboBox);

            var selectButton = CreateActionButton("?", "Выбрать из справочника");
            var addButton = CreateActionButton("+", "Добавить запись в справочник");
            var editButton = CreateActionButton("Изм.", "Изменить выбранную запись");

            Grid.SetColumn(selectButton, 1);
            Grid.SetColumn(addButton, 2);
            Grid.SetColumn(editButton, 3);
            panel.Children.Add(selectButton);
            panel.Children.Add(addButton);
            panel.Children.Add(editButton);

            picker.Content = panel;

            async Task RefreshAsync(Guid? selectedId = null)
            {
                var items = await LoadReferenceItemsAsync(metadataService, referenceCatalog, field);
                ReferenceComboBoxSearchHelper.Attach(comboBox, items);
                SelectReferenceValue(comboBox, selectedId?.ToString() ?? currentValue?.ToString());
            }

            comboBox.SelectionChanged += (_, _) => selectionChanged?.Invoke();

            selectButton.Click += async (_, _) =>
            {
                var selectedId = await SelectReferenceAsync(metadataService, referenceCatalog, owner);
                if (selectedId.HasValue)
                    SelectReferenceValue(comboBox, selectedId.Value.ToString());
            };

            addButton.Click += async (_, _) =>
            {
                var createdId = await AddReferenceAsync(metadataService, referenceCatalog, owner);
                if (createdId.HasValue)
                {
                    currentValue = createdId.Value.ToString();
                    await RefreshAsync(createdId.Value);
                }
            };

            editButton.Click += async (_, _) =>
            {
                if (comboBox.SelectedItem is not ReferenceItem selected)
                {
                    MessageBox.Show("Сначала выберите запись справочника.", referenceCatalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var edited = await EditReferenceAsync(metadataService, referenceCatalog, selected.Id, owner);
                if (edited)
                {
                    currentValue = selected.Id.ToString();
                    await RefreshAsync(selected.Id);
                }
            };

            await RefreshAsync();
            return picker;
        }

        public static void AttachEditor(
            ComboBox comboBox,
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            Window owner,
            Action<List<ReferenceItem>>? itemsReloaded = null,
            string? firstDisplayField = null,
            string? secondDisplayField = null)
        {
            comboBox.ToolTip = "Введите текст для быстрого поиска. ПКМ: выбрать, добавить или изменить запись справочника.";

            var contextMenu = comboBox.ContextMenu ?? new ContextMenu();
            contextMenu.Items.Add(CreateMenuItem("Выбрать...", async () =>
            {
                var selectedId = await SelectReferenceAsync(metadataService, referenceCatalog, owner, firstDisplayField, secondDisplayField);
                if (selectedId.HasValue)
                    SelectReferenceValue(comboBox, selectedId.Value.ToString());
            }));
            contextMenu.Items.Add(CreateMenuItem("Добавить...", async () =>
            {
                var createdId = await AddReferenceAsync(metadataService, referenceCatalog, owner);
                if (createdId.HasValue)
                    await ReloadExistingComboBoxAsync(
                        comboBox,
                        metadataService,
                        referenceCatalog,
                        createdId.Value,
                        itemsReloaded,
                        firstDisplayField,
                        secondDisplayField);
            }));
            contextMenu.Items.Add(CreateMenuItem("Изменить выбранную...", async () =>
            {
                if (comboBox.SelectedItem is not ReferenceItem selected)
                {
                    MessageBox.Show("Сначала выберите запись справочника.", referenceCatalog.Name,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (await EditReferenceAsync(metadataService, referenceCatalog, selected.Id, owner))
                {
                    await ReloadExistingComboBoxAsync(
                        comboBox,
                        metadataService,
                        referenceCatalog,
                        selected.Id,
                        itemsReloaded,
                        firstDisplayField,
                        secondDisplayField);
                }
            }));
            contextMenu.Items.Add(CreateMenuItem("Обновить список", async () =>
            {
                var selectedId = (comboBox.SelectedItem as ReferenceItem)?.Id;
                await ReloadExistingComboBoxAsync(
                    comboBox,
                    metadataService,
                    referenceCatalog,
                    selectedId,
                    itemsReloaded,
                    firstDisplayField,
                    secondDisplayField);
            }));

            comboBox.ContextMenu = contextMenu;
            TryAddInlineButtons(
                comboBox,
                async () =>
                {
                    var selectedId = await SelectReferenceAsync(metadataService, referenceCatalog, owner, firstDisplayField, secondDisplayField);
                    if (selectedId.HasValue)
                        SelectReferenceValue(comboBox, selectedId.Value.ToString());
                },
                async () =>
                {
                    var createdId = await AddReferenceAsync(metadataService, referenceCatalog, owner);
                    if (createdId.HasValue)
                        await ReloadExistingComboBoxAsync(
                            comboBox,
                            metadataService,
                            referenceCatalog,
                            createdId.Value,
                            itemsReloaded,
                            firstDisplayField,
                            secondDisplayField);
                },
                async () =>
                {
                    if (comboBox.SelectedItem is not ReferenceItem selected)
                    {
                        MessageBox.Show("Сначала выберите запись справочника.", referenceCatalog.Name,
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (await EditReferenceAsync(metadataService, referenceCatalog, selected.Id, owner))
                    {
                        await ReloadExistingComboBoxAsync(
                            comboBox,
                            metadataService,
                            referenceCatalog,
                            selected.Id,
                            itemsReloaded,
                            firstDisplayField,
                            secondDisplayField);
                    }
                });
        }

        public static async Task<List<ReferenceItem>> LoadReferenceItemsAsync(
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            MetadataField? field = null,
            string? firstDisplayField = null,
            string? secondDisplayField = null)
        {
            var rows = await metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            return rows
                .Where(row => row.TryGetValue("Id", out var id) && Guid.TryParse(id?.ToString(), out _))
                .Select(row => CreateReferenceItem(row, field, firstDisplayField, secondDisplayField))
                .ToList();
        }

        private static ComboBox CreateComboBox(string fieldName)
        {
            return new ComboBox
            {
                Height = 30,
                Name = GetSafeName(fieldName),
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                MinWidth = 200
            };
        }

        private static Button CreateActionButton(string content, string tooltip)
        {
            return new Button
            {
                Content = content,
                Width = content.Length > 1 ? 46 : 34,
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0),
                ToolTip = tooltip
            };
        }

        private static void TryAddInlineButtons(
            ComboBox comboBox,
            Func<Task> selectAction,
            Func<Task> addAction,
            Func<Task> editAction)
        {
            if (comboBox.GetValue(InlineButtonsAddedProperty) is true)
                return;

            if (comboBox.Parent is not Panel parent)
                return;

            var childIndex = parent.Children.IndexOf(comboBox);
            if (childIndex < 0)
                return;

            var row = Grid.GetRow(comboBox);
            var column = Grid.GetColumn(comboBox);
            var rowSpan = Grid.GetRowSpan(comboBox);
            var columnSpan = Grid.GetColumnSpan(comboBox);
            var dock = DockPanel.GetDock(comboBox);
            var margin = comboBox.Margin;

            parent.Children.Remove(comboBox);

            var wrapper = new Grid { Margin = margin };
            wrapper.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(UIElement.Visibility))
            {
                Source = comboBox,
                Mode = BindingMode.OneWay
            });

            Grid.SetRow(wrapper, row);
            Grid.SetColumn(wrapper, column);
            Grid.SetRowSpan(wrapper, rowSpan);
            Grid.SetColumnSpan(wrapper, columnSpan);
            DockPanel.SetDock(wrapper, dock);

            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            comboBox.Margin = new Thickness(0);
            Grid.SetRow(comboBox, 0);
            Grid.SetColumn(comboBox, 0);
            Grid.SetRowSpan(comboBox, 1);
            Grid.SetColumnSpan(comboBox, 1);
            wrapper.Children.Add(comboBox);

            var selectButton = CreateActionButton("?", "Выбрать из справочника");
            var addButton = CreateActionButton("+", "Добавить запись в справочник");
            var editButton = CreateActionButton("Изм.", "Изменить выбранную запись");

            selectButton.Click += async (_, _) => await selectAction();
            addButton.Click += async (_, _) => await addAction();
            editButton.Click += async (_, _) => await editAction();

            Grid.SetColumn(selectButton, 1);
            Grid.SetColumn(addButton, 2);
            Grid.SetColumn(editButton, 3);
            wrapper.Children.Add(selectButton);
            wrapper.Children.Add(addButton);
            wrapper.Children.Add(editButton);

            parent.Children.Insert(childIndex, wrapper);
            comboBox.SetValue(InlineButtonsAddedProperty, true);
        }

        private static MenuItem CreateMenuItem(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            return item;
        }

        private static async Task ReloadExistingComboBoxAsync(
            ComboBox comboBox,
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            Guid? selectedId,
            Action<List<ReferenceItem>>? itemsReloaded,
            string? firstDisplayField,
            string? secondDisplayField)
        {
            var items = await LoadReferenceItemsAsync(
                metadataService,
                referenceCatalog,
                firstDisplayField: firstDisplayField,
                secondDisplayField: secondDisplayField);
            itemsReloaded?.Invoke(items);
            ReferenceComboBoxSearchHelper.Attach(comboBox, items);

            if (selectedId.HasValue)
                SelectReferenceValue(comboBox, selectedId.Value.ToString());
        }

        private static async Task<Guid?> SelectReferenceAsync(
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            Window owner,
            string? firstDisplayField = null,
            string? secondDisplayField = null)
        {
            var rows = await metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            var dialog = new ReferenceSelectionDialog(
                rows,
                firstDisplayField ?? FindFirstDisplayField(referenceCatalog),
                secondDisplayField ?? FindSecondDisplayField(referenceCatalog))
            {
                Owner = owner,
                Title = $"Выбор: {referenceCatalog.Name}"
            };

            if (dialog.ShowDialog() == true &&
                dialog.SelectedItem != null &&
                dialog.SelectedItem.TryGetValue("Id", out var idValue) &&
                Guid.TryParse(idValue?.ToString(), out var id))
            {
                return id;
            }

            return null;
        }

        private static async Task<Guid?> AddReferenceAsync(
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            Window owner)
        {
            var dialog = new CatalogItemDialog(referenceCatalog, metadataService)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true)
                return null;

            return await metadataService.CreateDynamicRecordAsync(referenceCatalog.Id, dialog.ItemData);
        }

        private static async Task<bool> EditReferenceAsync(
            MetadataService metadataService,
            MetadataObject referenceCatalog,
            Guid recordId,
            Window owner)
        {
            var rows = await metadataService.GetCatalogDataAsync(referenceCatalog.Id);
            var row = rows.FirstOrDefault(item =>
                item.TryGetValue("Id", out var idValue) &&
                Guid.TryParse(idValue?.ToString(), out var id) &&
                id == recordId);
            if (row == null)
            {
                MessageBox.Show("Выбранная запись справочника не найдена.", referenceCatalog.Name,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var dialog = new CatalogItemDialog(referenceCatalog, metadataService, row)
            {
                Owner = owner
            };

            if (dialog.ShowDialog() != true)
                return false;

            await metadataService.UpdateDynamicRecordAsync(referenceCatalog.Id, recordId, dialog.ItemData);
            return true;
        }

        private static ReferenceItem CreateReferenceItem(
            Dictionary<string, object> row,
            MetadataField? field,
            string? firstDisplayField,
            string? secondDisplayField)
        {
            var item = new ReferenceItem
            {
                Id = Guid.Parse(row["Id"].ToString()!),
                DisplayName = BuildDisplayName(row, field, firstDisplayField, secondDisplayField)
            };

            foreach (var key in BuildLookupKeys(row, item.DisplayName))
                item.LookupKeys.Add(key);

            return item;
        }

        private static string BuildDisplayName(
            Dictionary<string, object> row,
            MetadataField? field,
            string? firstDisplayField,
            string? secondDisplayField)
        {
            if (field != null)
            {
                var displayValue = ReferenceDisplayHelper.BuildDisplayValue(row, field);
                if (!string.IsNullOrWhiteSpace(displayValue))
                    return displayValue;
            }

            var first = GetValue(row, firstDisplayField);
            var second = GetValue(row, secondDisplayField);
            if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second))
                return $"{first} - {second}";
            if (!string.IsNullOrWhiteSpace(first))
                return first;
            if (!string.IsNullOrWhiteSpace(second))
                return second;

            foreach (var name in new[]
                     {
                         "Код организации", "organization_code", "Код", "code", "Счет", "account_number",
                         "Табельный номер", "personnel_number", "Наименование", "Наименование материала",
                         "Наименование банка", "ФИО", "full_name", "name"
                     })
            {
                var value = GetValue(row, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return row.GetValueOrDefault("Id")?.ToString() ?? string.Empty;
        }

        private static IEnumerable<string> BuildLookupKeys(Dictionary<string, object> row, string displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                yield return displayName;

            foreach (var value in row.Values)
            {
                var text = value?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                yield return text;

                var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
                if (separatorIndex > 0)
                    yield return text[..separatorIndex].Trim();
            }
        }

        private static void SelectReferenceValue(ComboBox comboBox, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var selected = comboBox.Items
                .OfType<ReferenceItem>()
                .FirstOrDefault(item =>
                    item.Id.ToString().Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    item.LookupKeys.Contains(value.Trim()));

            if (selected != null)
                comboBox.SelectedItem = selected;
        }

        private static string? FindFirstDisplayField(MetadataObject catalog)
        {
            return FindField(catalog,
                "Код организации", "organization_code", "Код", "code", "Табельный номер",
                "personnel_number", "Счет", "account_number");
        }

        private static string? FindSecondDisplayField(MetadataObject catalog)
        {
            return FindField(catalog,
                "Наименование", "name", "ФИО", "full_name", "Наименование материала",
                "Наименование банка", "Наименование кассы");
        }

        private static string? FindField(MetadataObject catalog, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var field = catalog.Fields.FirstOrDefault(item =>
                    item.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                    item.DbColumnName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                    return field.Name;
            }

            return null;
        }

        private static string GetValue(Dictionary<string, object> row, string? fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return string.Empty;

            if (row.TryGetValue(fieldName, out var value))
                return value?.ToString() ?? string.Empty;

            var normalizedName = fieldName.Replace(" ", "_");
            if (row.TryGetValue(normalizedName, out value))
                return value?.ToString() ?? string.Empty;

            var match = row.FirstOrDefault(item =>
                item.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                item.Key.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            return match.Equals(default(KeyValuePair<string, object>))
                ? string.Empty
                : match.Value?.ToString() ?? string.Empty;
        }

        private static string GetSafeName(string fieldName)
        {
            var safeName = new string(fieldName.Select(ch =>
                char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            return string.IsNullOrWhiteSpace(safeName) ? "ReferencePicker" : safeName;
        }
    }
}
