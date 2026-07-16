using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using BIS.ERP.Views.Dialogs;
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
        private readonly User? _currentUser;
        private UserAccessRow? _selectedUser;

        public ObservableCollection<AccessTreeNode> Capabilities { get; } = new();

        public UserAccessManagementView(AppDbContext context, IEnumerable<NavigationItem> navigationItems, User? currentUser = null)
        {
            InitializeComponent();
            DataContext = this;
            _accessService = new UserAccessService(context);
            _navigationItems = navigationItems.Where(item => item.Id != "AdminSection").ToList();
            _currentUser = currentUser;
            AddUserButton.IsEnabled = UserAccessService.CanManageUsers(_currentUser);
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
                DeleteUserButton.IsEnabled = false;
                ToggleActiveButton.IsEnabled = false;
                return;
            }

            var isAdmin = _selectedUser.User.Role == UserRole.Admin;
            var canManageTarget = _currentUser != null &&
                                  UserAccessService.CanManageTargetRole(_currentUser.Role, _selectedUser.User.Role);
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
            AccessTree.IsEnabled = !isAdmin && canManageTarget;
            SelectAllButton.IsEnabled = !isAdmin && canManageTarget;
            ClearButton.IsEnabled = !isAdmin && canManageTarget;
            SaveButton.IsEnabled = !isAdmin && canManageTarget;
            DeleteUserButton.IsEnabled = CanDeleteSelectedUser();
            ToggleActiveButton.IsEnabled = CanToggleSelectedUser();
            ToggleActiveButton.Content = _selectedUser.User.IsActive ? "Отключить" : "Включить";
            StatusText.Text = isAdmin
                ? "Администратор всегда имеет полный доступ и не имеет признака активности."
                : canManageTarget
                    ? "Изменения ещё не сохранены."
                    : "Недостаточно прав для изменения этого пользователя.";
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
                if (_currentUser == null ||
                    !UserAccessService.CanManageTargetRole(_currentUser.Role, _selectedUser.User.Role))
                    throw new InvalidOperationException("Недостаточно прав для изменения доступа этого пользователя.");
                var keys = Capabilities.SelectMany(node => node.CheckedLeafKeys()).ToList();
                await _accessService.SavePermissionsAsync(_selectedUser.User.Id, keys);
                StatusText.Text = $"Права сохранены: {DateTime.Now:HH:mm:ss}. Применятся при следующем входе пользователя.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Права доступа", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnAddUserClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var roles = UserAccessService.GetCreatableRoles(_currentUser);
                if (roles.Count == 0)
                    throw new InvalidOperationException("Недостаточно прав для создания пользователей.");

                var dialog = new UserEditDialog(roles) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() != true)
                    return;

                await _accessService.CreateUserAsync(
                    _currentUser,
                    dialog.UserLogin,
                    dialog.Password,
                    dialog.FullName,
                    dialog.Email,
                    dialog.SelectedRole,
                    dialog.IsUserActive);
                await LoadUsersAsync();
                StatusText.Text = "Пользователь добавлен.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Добавление пользователя", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDeleteUserClick(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
                return;

            var result = MessageBox.Show(
                $"Удалить пользователя '{_selectedUser.Login}'?",
                "Удаление пользователя",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                await _accessService.DeleteUserAsync(_currentUser, _selectedUser.User.Id);
                await LoadUsersAsync();
                StatusText.Text = "Пользователь удален.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Удаление пользователя", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
                return;

            try
            {
                await _accessService.SetUserActiveAsync(
                    _currentUser,
                    _selectedUser.User.Id,
                    !_selectedUser.User.IsActive);
                await LoadUsersAsync();
                StatusText.Text = "Активность пользователя изменена.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Активность пользователя", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanDeleteSelectedUser()
        {
            if (_selectedUser == null || _currentUser == null)
                return false;
            if (_selectedUser.User.Id == _currentUser.Id)
                return false;
            if (_selectedUser.User.Role == UserRole.Admin)
                return false;
            if (_selectedUser.Login.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                _selectedUser.Login.Equals("user", StringComparison.OrdinalIgnoreCase))
                return false;
            return UserAccessService.CanManageTargetRole(_currentUser.Role, _selectedUser.User.Role);
        }

        private bool CanToggleSelectedUser()
        {
            if (_selectedUser == null || _currentUser == null)
                return false;
            if (_selectedUser.User.Role == UserRole.Admin)
                return false;
            return UserAccessService.CanManageTargetRole(_currentUser.Role, _selectedUser.User.Role);
        }
    }

    public sealed class UserAccessRow
    {
        public UserAccessRow(User user) => User = user;
        public User User { get; }
        public string Login => User.Login;
        public string FullName => string.IsNullOrWhiteSpace(User.FullName) ? User.Login : User.FullName;
        public string RoleDisplay => UserAccessService.GetRoleDisplayName(User.Role);
        public string ActiveDisplay => User.Role == UserRole.Admin
            ? "Всегда"
            : User.IsActive ? "Да" : "Нет";
        public string LastLoginDisplay => User.LastLoginDate?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "-";
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
