using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIS.ERP.Views.Controls;

namespace BIS.ERP.Services;

public static class MdiDialogService
{
    private static readonly Dictionary<Window, HostedDialogSession> HostedDialogs = new();

    public static bool TryShowInWorkspace(
        Window? owner,
        Window dialog,
        string? title = null,
        string? key = null,
        bool activate = true,
        bool fillWorkspace = false)
    {
        var workspace = ResolveWorkspace(owner);
        if (workspace == null)
            return false;

        var actualTitle = string.IsNullOrWhiteSpace(title)
            ? (string.IsNullOrWhiteSpace(dialog.Title) ? dialog.GetType().Name : dialog.Title)
            : title;
        var actualKey = string.IsNullOrWhiteSpace(key)
            ? $"{dialog.GetType().FullName}:{actualTitle}:{Guid.NewGuid():N}"
            : key;

        workspace.OpenWindow(actualKey, actualTitle, dialog, activate, fillWorkspace: fillWorkspace);
        return true;
    }

    public static void ShowInWorkspaceOrDialog(Window? owner, Window dialog, string? title = null, string? key = null)
    {
        if (TryShowInWorkspace(owner, dialog, title, key))
            return;

        SetSafeOwner(dialog, owner);
        dialog.ShowDialog();
    }

    public static void ShowInWorkspaceOrDialog(
        Window? owner,
        Window dialog,
        string? title,
        string? key,
        bool fillWorkspace)
    {
        if (TryShowInWorkspace(owner, dialog, title, key, activate: true, fillWorkspace: fillWorkspace))
            return;

        SetSafeOwner(dialog, owner);
        dialog.ShowDialog();
    }

    public static Task<bool?> ShowInWorkspaceForResultAsync(
        Window? owner,
        Window dialog,
        string? title = null,
        string? key = null,
        bool activate = true)
    {
        var workspace = ResolveWorkspace(owner);
        if (workspace == null)
        {
            SetSafeOwner(dialog, owner);
            return Task.FromResult(dialog.ShowDialog());
        }

        var actualTitle = string.IsNullOrWhiteSpace(title)
            ? (string.IsNullOrWhiteSpace(dialog.Title) ? dialog.GetType().Name : dialog.Title)
            : title;
        var actualKey = string.IsNullOrWhiteSpace(key)
            ? $"{dialog.GetType().FullName}:{actualTitle}:{Guid.NewGuid():N}"
            : key;

        var completionSource = new TaskCompletionSource<bool?>();
        var session = new HostedDialogSession(
            actualKey,
            completionSource,
            () => workspace.CloseDocumentByKey(actualKey));

        HostedDialogs[dialog] = session;
        workspace.OpenWindow(
            actualKey,
            actualTitle,
            dialog,
            activate,
            () => CompleteHostedDialog(dialog, false, closeDocument: false),
            fillWorkspace: true);

        return completionSource.Task;
    }

    public static bool TryOpenDocumentInWorkspace(
        Window? owner,
        string key,
        string title,
        UserControl content,
        bool activate = true)
    {
        var workspace = ResolveWorkspace(owner);
        if (workspace == null)
            return false;

        workspace.OpenDocument(key, title, content, activate);
        return true;
    }

    public static void CloseWithResult(Window dialog, bool? result)
    {
        if (CompleteHostedDialog(dialog, result))
            return;

        try
        {
            dialog.DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            dialog.Close();
        }
    }

    private static bool CompleteHostedDialog(Window dialog, bool? result, bool closeDocument = true)
    {
        if (!HostedDialogs.TryGetValue(dialog, out var session))
            return false;

        if (session.IsCompleted)
            return true;

        session.IsCompleted = true;
        HostedDialogs.Remove(dialog);
        session.CompletionSource.TrySetResult(result);

        if (closeDocument)
            session.CloseDocument();

        return true;
    }

    private static void SetSafeOwner(Window dialog, Window? owner)
    {
        if (owner == null || !owner.IsVisible)
            return;

        dialog.Owner = owner;
    }
    private static MdiWorkspaceControl? ResolveWorkspace(Window? owner)
    {
        return FindWorkspace(owner)
            ?? FindActiveWorkspace()
            ?? FindWorkspace(Application.Current?.MainWindow)
            ?? FindWorkspaceInOpenWindows();
    }

    private static MdiWorkspaceControl? FindActiveWorkspace()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        foreach (Window window in app.Windows)
        {
            if (!window.IsActive)
                continue;

            var workspace = FindWorkspace(window);
            if (workspace != null)
                return workspace;
        }

        return null;
    }

    private static MdiWorkspaceControl? FindWorkspaceInOpenWindows()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        foreach (Window window in app.Windows)
        {
            var workspace = FindWorkspace(window);
            if (workspace != null)
                return workspace;
        }

        return null;
    }

    private static MdiWorkspaceControl? FindWorkspace(DependencyObject? root)
    {
        if (root == null)
            return null;

        if (root is MdiWorkspaceControl workspace)
            return workspace;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var result = FindWorkspace(VisualTreeHelper.GetChild(root, index));
            if (result != null)
                return result;
        }

        return null;
    }

    private sealed class HostedDialogSession
    {
        public HostedDialogSession(
            string key,
            TaskCompletionSource<bool?> completionSource,
            Action closeDocument)
        {
            Key = key;
            CompletionSource = completionSource;
            CloseDocument = closeDocument;
        }

        public string Key { get; }

        public TaskCompletionSource<bool?> CompletionSource { get; }

        public Action CloseDocument { get; }

        public bool IsCompleted { get; set; }
    }
}