using System.Windows.Controls;
using BIS.ERP.Views.Controls;

namespace BIS.ERP.Services;

public class AppNavigationService
{
    private readonly ContentControl? _contentControl;
    private readonly MdiWorkspaceControl? _workspaceControl;
    private UserControl? _currentView;

    public AppNavigationService(ContentControl contentControl)
    {
        _contentControl = contentControl;
    }

    public AppNavigationService(MdiWorkspaceControl workspaceControl)
    {
        _workspaceControl = workspaceControl;
    }

    public void NavigateTo<T>() where T : UserControl, new()
    {
        NavigateTo(new T());
    }

    public void NavigateTo(UserControl view, string? title = null, string? key = null)
    {
        _currentView = view;
        if (_workspaceControl != null)
        {
            _workspaceControl.OpenDocument(
                string.IsNullOrWhiteSpace(key) ? view.GetType().FullName ?? view.GetHashCode().ToString() : key,
                string.IsNullOrWhiteSpace(title) ? view.GetType().Name : title,
                view);
            return;
        }

        if (_contentControl != null)
            _contentControl.Content = view;
    }

    public T? GetCurrentView<T>() where T : UserControl
    {
        return (_workspaceControl?.CurrentContent ?? _currentView) as T;
    }
}
