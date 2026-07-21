using System.Windows;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow(string selectedInfoBaseText = "", string selectedModeText = "")
        {
            InitializeComponent();

            var viewModel = new LoginViewModel(ServiceLocator.AuthService, selectedInfoBaseText, selectedModeText);
            viewModel.LoginSucceeded += (_, _) =>
            {
                DialogResult = true;
                Close();
            };
            viewModel.BackRequested += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            viewModel.CloseRequested += (_, _) =>
            {
                try
                {
                    DialogResult = false;
                }
                catch (InvalidOperationException)
                {
                    // LoginWindow can be shown either as a dialog or as a regular window.
                }

                Application.Current.Shutdown();
            };

            DataContext = viewModel;
            Loaded += async (_, _) =>
            {
                await ApplyInfoBaseLogoAsync();
                LoginBox.Focus();
            };
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);
            AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);
        }

        private async System.Threading.Tasks.Task ApplyInfoBaseLogoAsync()
        {
            var currentInfoBase = await ServiceLocator.InfoBaseManager.GetCurrentInfoBaseAsync();
            if (currentInfoBase == null)
                return;

            LogoDisplayHelper.Apply(
                InfoBaseLogoImage,
                InfoBaseIconText,
                currentInfoBase.LogoImage,
                currentInfoBase.DisplayIcon);
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnterKey(e.Key))
                return;

            if (LoginBox.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                PasswordBox.Focus();
                return;
            }

            if (PasswordBox.IsKeyboardFocusWithin || LoginButton.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                ExecuteLoginCommand();
            }
        }

        private void OnLoginPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnterKey(e.Key))
                return;
            e.Handled = true;
            PasswordBox.Focus();
        }

        private void OnPasswordPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsEnterKey(e.Key))
                return;
            e.Handled = true;
            ExecuteLoginCommand();
        }

        private void ExecuteLoginCommand()
        {
            var command = (DataContext as LoginViewModel)?.LoginCommand;
            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }

        private static bool IsEnterKey(Key key) => key is Key.Enter or Key.Return;
    }
}

