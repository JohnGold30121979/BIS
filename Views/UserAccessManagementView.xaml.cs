using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views
{
    public partial class UserAccessManagementView : UserControl
    {
        private readonly UserAccessService _accessService;
        private readonly List<NavigationItem> _navigationItems;
        private UserAccessRow? _selectedUser;

        public ObservableCollection<AccessTreeNode> Capabilities { get; } = new();

        public UserAccessManagementView(AppDbContext context, IEnumerable<NavigationItem> navigationItems)
        {
            InitializeComponent();
            DataContext = this;
            _accessService = new UserAccessService(context);
            _navigationItems = navigationItems.Where(item => item.Id != "AdminSection").ToList();
            Loaded += async (_, _) => await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            var users = await _accessService.GetUsersAsync();
            UsersGrid.ItemsSource = users.Select(user => new UserAccessRow(user)).ToList();
            UsersGrid.SelectedIndex = users.Count > 0 ? 0 : -1;
        }

        private async void OnUserSelected(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UsersGrid.SelectedItem as UserAccessRow;
            Capabilities.Clear();
            if (_selectedUser == null)
            {
                SaveButton.IsEnabled = false;
                return;
            }

            var isAdmin = _selectedUser.User.Role == UserRole.Admin;
            var allowedKeys = isAdmin
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : await _accessService.GetAllowedKeysAsync(_selectedUser.User.Id);

            foreach (var item in _navigationItems)
            {
                var node = BuildNode(item, null, allowedKeys, isAdmin);
                if (node != null)
                    Capabilities.Add(node);
            }

            SelectedUserText.Text = $"Доступ для: {_selectedUser.FullName} ({_selectedUser.Login})";
            AccessTree.IsEnabled = !isAdmin;
            SelectAllButton.IsEnabled = !isAdmin;
            ClearButton.IsEnabled = !isAdmin;
            SaveButton.IsEnabled = !isAdmin;
            StatusText.Text = isAdmin ? "Администратор всегда имеет полный доступ." : "Изменения ещё не сохранены.";
        }

        private static AccessTreeNode? BuildNode(
            NavigationItem item,
            AccessTreeNode? parent,
            IReadOnlySet<string> allowedKeys,
            bool isAdmin)
        {
            if (item.Id is "HelpSection" or "UserProfile" or "SwitchMode" or "Logout" or "AboutSystem")
                return null;

            var node = new AccessTreeNode(item.Id, item.Name, parent);
            foreach (var child in item.Children)
            {
                var childNode = BuildNode(child, node, allowedKeys, isAdmin);
                if (childNode != null)
                    node.Children.Add(childNode);
            }

            if (node.Children.Count == 0)
                node.SetCheckedSilently(isAdmin || allowedKeys.Contains(item.Id));
            else
                node.RefreshFromChildren();
            return node;
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var node in Capabilities)
                node.IsChecked = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            foreach (var node in Capabilities)
                node.IsChecked = false;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
                return;
            try
            {
                var keys = Capabilities.SelectMany(node => node.CheckedLeafKeys()).ToList();
                await _accessService.SavePermissionsAsync(_selectedUser.User.Id, keys);
                StatusText.Text = $"Права сохранены: {DateTime.Now:HH:mm:ss}. Применятся при следующем входе пользователя.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Права доступа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public sealed class UserAccessRow
    {
        public UserAccessRow(User user) => User = user;
        public User User { get; }
        public string Login => User.Login;
        public string FullName => string.IsNullOrWhiteSpace(User.FullName) ? User.Login : User.FullName;
        public string RoleDisplay => User.Role == UserRole.Admin ? "Администратор" : "Пользователь";
    }

    public sealed class AccessTreeNode : INotifyPropertyChanged
    {
        private bool? _isChecked;
        private bool _isInternalUpdate;

        public AccessTreeNode(string key, string name, AccessTreeNode? parent)
        {
            Key = key;
            Name = name;
            Parent = parent;
        }

        public string Key { get; }
        public string Name { get; }
        public AccessTreeNode? Parent { get; }
        public ObservableCollection<AccessTreeNode> Children { get; } = new();
        public bool IsLeaf => Children.Count == 0;

        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
                if (!_isInternalUpdate && value.HasValue)
                    foreach (var child in Children)
                        child.IsChecked = value;
                Parent?.RefreshFromChildren();
            }
        }

        public void SetCheckedSilently(bool value)
        {
            _isInternalUpdate = true;
            IsChecked = value;
            _isInternalUpdate = false;
        }

        public void RefreshFromChildren()
        {
            if (Children.Count == 0)
                return;
            _isInternalUpdate = true;
            IsChecked = Children.All(child => child.IsChecked == true)
                ? true
                : Children.All(child => child.IsChecked == false)
                    ? false
                    : null;
            _isInternalUpdate = false;
        }

        public IEnumerable<string> CheckedLeafKeys()
        {
            if (IsLeaf)
            {
                if (IsChecked == true)
                    yield return Key;
                yield break;
            }

            foreach (var key in Children.SelectMany(child => child.CheckedLeafKeys()))
                yield return key;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
