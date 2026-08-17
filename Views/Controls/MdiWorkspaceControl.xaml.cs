using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace BIS.ERP.Views.Controls;

public partial class MdiWorkspaceControl : UserControl, INotifyPropertyChanged
{
    private MdiDocumentItem? _selectedDocument;

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
