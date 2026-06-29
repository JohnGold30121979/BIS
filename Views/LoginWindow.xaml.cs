using System.Windows;
using System.Windows.Input;
using BIS.ERP.Services;
using BIS.ERP.ViewModels;

namespace BIS.ERP.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();

            var viewModel = new LoginViewModel(ServiceLocator.AuthService, new WindowDialogService(this));
            viewModel.LoginSucceeded += (_, _) =>
            {
                DialogResult = true;
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
            Loaded += (_, _) => LoginBox.Focus();
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);
            AddHandler(Keyboard.KeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown), true);
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
