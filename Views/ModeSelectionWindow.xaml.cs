using BIS.ERP.Models;
using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BIS.ERP.Views
{
    public partial class ModeSelectionWindow : Window, INotifyPropertyChanged
    {
        private User _currentUser;
        private bool _isConfigMode;

        public User CurrentUser
        {
            get => _currentUser;
            set
            {
                _currentUser = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UserFullName));
                OnPropertyChanged(nameof(UserLogin));
                OnPropertyChanged(nameof(UserRole));
                OnPropertyChanged(nameof(IsAdminVisibility));
            }
        }

        public bool IsConfigMode
        {
            get => _isConfigMode;
            private set
            {
                _isConfigMode = value;
                OnPropertyChanged();
            }
        }

        public string UserFullName => CurrentUser?.FullName ?? "";
        public string UserLogin => CurrentUser?.Login ?? "";

        // Явно указываем Models.UserRole.Admin
        public string UserRole => CurrentUser?.Role == Models.UserRole.Admin ? "Администратор" : "Пользователь";

        // Явно указываем Models.UserRole.Admin
        public Visibility IsAdminVisibility => CurrentUser?.Role == Models.UserRole.Admin ? Visibility.Visible : Visibility.Collapsed;

        public ModeSelectionWindow(User user)
        {
            InitializeComponent();
            CurrentUser = user;
            DataContext = this;
        }

        private void OnWorkModeClick(object sender, RoutedEventArgs e)
        {
            IsConfigMode = false;
            DialogResult = true;
            Close();
        }

        private void OnConfigModeClick(object sender, RoutedEventArgs e)
        {
            IsConfigMode = true;
            DialogResult = true;
            Close();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}