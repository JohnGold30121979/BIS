using System.Windows.Controls;

namespace BIS.ERP.Services;

public class NavigationService
{
    private readonly ContentControl _contentControl;
    private UserControl? _currentView;

    public NavigationService(ContentControl contentControl)
    {
        _contentControl = contentControl;
    }

    public void NavigateTo<T>() where T : UserControl, new()
    {
        _currentView = new T();
        _contentControl.Content = _currentView;
    }

    public void NavigateTo(UserControl view)
    {
        _currentView = view;
        _contentControl.Content = view;
    }

    public T? GetCurrentView<T>() where T : UserControl
    {
        return _currentView as T;
    }
}