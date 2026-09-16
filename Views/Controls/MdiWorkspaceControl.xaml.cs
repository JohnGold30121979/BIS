using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BIS.ERP.Views.Controls;

public partial class MdiWorkspaceControl : UserControl, INotifyPropertyChanged
{
    private const string MdiTabDragFormat = "BIS.ERP.MdiDocumentItem";
    private MdiDocumentItem? _selectedDocument;
    private MdiDocumentItem? _draggedDocument;
    private Point _dragStartPoint;
    private Window? _hostWindow;

    public MdiWorkspaceControl()
    {
        InitializeComponent();
        Documents.CollectionChanged += (_, _) => UpdateEmptyState();
        Loaded += OnWorkspaceLoaded;
        Unloaded += OnWorkspaceUnloaded;
    }

    public ObservableCollection<MdiDocumentItem> Documents { get; } = new();

    public MdiDocumentItem? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (ReferenceEquals(_selectedDocument, value))
                return;

            _selectedDocument = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentContent));
        }
    }

    public UserControl? CurrentContent => SelectedDocument?.Content;

    public MdiDocumentItem OpenDocument(string key, string title, UserControl content, bool activate = true)
    {
        if (string.IsNullOrWhiteSpace(key))
            key = content.GetType().FullName ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(title))
            title = content.GetType().Name;

        var existing = Documents.FirstOrDefault(document =>
            string.Equals(document.Key, key, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            if (activate)
                SelectedDocument = existing;
            return existing;
        }

        var item = new MdiDocumentItem(key, title, content);
        Documents.Add(item);
        if (activate)
            SelectedDocument = item;

        return item;
    }

    public MdiDocumentItem OpenWindow(
        string key,
        string title,
        Window window,
        bool activate = true,
        Action? documentClosed = null,
        bool fillWorkspace = false)
    {
        if (string.IsNullOrWhiteSpace(key))
            key = $"{window.GetType().FullName}:{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(title))
            title = string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title;

        var existing = Documents.FirstOrDefault(document =>
            string.Equals(document.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (activate)
                SelectedDocument = existing;
            return existing;
        }

        var actualKey = key;
        var host = new MdiDialogHostControl(window, () => CloseDocumentByKey(actualKey), documentClosed, fillWorkspace);
        return OpenDocument(actualKey, title, host, activate);
    }

    public void CloseDocumentByKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var document = Documents.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (document != null)
            CloseDocument(document);
    }

    public void CloseCurrentDocument()
    {
        if (SelectedDocument == null)
            return;

        CloseDocument(SelectedDocument);
    }

    public void CloseDocument(MdiDocumentItem document)
    {
        if (document.Content is MdiDialogHostControl hostedDialog)
            hostedDialog.NotifyDocumentClosed();

        var index = Documents.IndexOf(document);
        if (index < 0)
            return;

        Documents.RemoveAt(index);
        if (ReferenceEquals(SelectedDocument, document))
            SelectedDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
    }

    public void CloseAllDocuments()
    {
        // Закрываем вкладки по одной: для каждой корректно срабатывает
        // уведомление хостируемого диалога (Window и UserControl),
        // а коллекция получает отдельное событие Remove на каждую вкладку
        foreach (var document in Documents.ToList())
            CloseDocument(document);
    }

    private void OnCloseCurrentClick(object sender, RoutedEventArgs e) => CloseCurrentDocument();

    private void OnCloseAllClick(object sender, RoutedEventArgs e) => CloseAllDocuments();

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MdiDocumentItem document)
            CloseDocument(document);
    }

    private void OnWorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow != null)
            _hostWindow.KeyDown += OnHostWindowKeyDown;
    }

    private void OnWorkspaceUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow == null)
            return;

        _hostWindow.KeyDown -= OnHostWindowKeyDown;
        _hostWindow = null;
    }

    // ESC: закрываем только активную вкладку. При повторных нажатиях
    // окна закрываются последовательно, по одному за каждое нажатие.
    private void OnHostWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || SelectedDocument == null)
            return;

        CloseDocument(SelectedDocument);
        e.Handled = true;
    }

    private void DocumentsTabControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(DocumentsTabControl);
        _draggedDocument = null;

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;

        var tabItem = FindAncestor<TabItem>(e.OriginalSource as DependencyObject);
        _draggedDocument = tabItem?.DataContext as MdiDocumentItem;
    }

    private void DocumentsTabControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedDocument == null)
            return;

        var currentPoint = e.GetPosition(DocumentsTabControl);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(DocumentsTabControl, new DataObject(MdiTabDragFormat, _draggedDocument), DragDropEffects.Move);
        _draggedDocument = null;
    }

    private void DocumentsTabControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(MdiTabDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DocumentsTabControl_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(MdiTabDragFormat))
            return;

        var draggedDocument = e.Data.GetData(MdiTabDragFormat) as MdiDocumentItem;
        var targetDocument = FindAncestor<TabItem>(e.OriginalSource as DependencyObject)?.DataContext as MdiDocumentItem;
        if (draggedDocument == null || targetDocument == null || ReferenceEquals(draggedDocument, targetDocument))
            return;

        var oldIndex = Documents.IndexOf(draggedDocument);
        var newIndex = Documents.IndexOf(targetDocument);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            return;

        Documents.Move(oldIndex, newIndex);
        SelectedDocument = draggedDocument;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateEmptyState()
    {
        var hasDocuments = Documents.Count > 0;
        EmptyState.Visibility = hasDocuments ? Visibility.Collapsed : Visibility.Visible;
        DocumentsTabControl.Visibility = hasDocuments ? Visibility.Visible : Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class MdiDocumentItem
{
    public MdiDocumentItem(string key, string title, UserControl content)
    {
        Key = key;
        Title = title;
        Content = content;
    }

    public string Key { get; }

    public string Title { get; }

    public UserControl Content { get; }
}