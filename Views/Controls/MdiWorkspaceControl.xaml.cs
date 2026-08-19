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

    public MdiWorkspaceControl()
    {
        InitializeComponent();
        Documents.CollectionChanged += (_, _) => UpdateEmptyState();
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

    public void OpenDocument(string key, string title, UserControl content, bool activate = true)
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
            return;
        }

        var item = new MdiDocumentItem(key, title, content);
        Documents.Add(item);
        if (activate)
            SelectedDocument = item;
    }

    public void CloseCurrentDocument()
    {
        if (SelectedDocument == null)
            return;

        CloseDocument(SelectedDocument);
    }

    public void CloseDocument(MdiDocumentItem document)
    {
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
        Documents.Clear();
        SelectedDocument = null;
    }

    private void OnCloseCurrentClick(object sender, RoutedEventArgs e) => CloseCurrentDocument();

    private void OnCloseAllClick(object sender, RoutedEventArgs e) => CloseAllDocuments();

    private void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MdiDocumentItem document)
            CloseDocument(document);
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
