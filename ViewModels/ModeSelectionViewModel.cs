using System;
using CommunityToolkit.Mvvm.Input;
using BIS.ERP.Models;

namespace BIS.ERP.ViewModels
{
    public partial class ModeSelectionViewModel : ViewModelBase
    {
        public User CurrentUser { get; }
        public bool IsConfigMode { get; private set; }
        public string UserFullName => CurrentUser.FullName;
        public string UserLogin => CurrentUser.Login;
        public string UserRoleText => CurrentUser.Role == UserRole.Admin ? "Администратор" : "Пользователь";
        public bool IsAdminVisible => CurrentUser.Role == UserRole.Admin;

        public event EventHandler? ModeSelected;
        public event EventHandler? CloseRequested;

        public ModeSelectionViewModel(User currentUser)
        {
            CurrentUser = currentUser;
        }

        [RelayCommand]
        private void SelectWorkMode()
        {
            IsConfigMode = false;
            ModeSelected?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void SelectConfigMode()
        {
            IsConfigMode = true;
            ModeSelected?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Close()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
