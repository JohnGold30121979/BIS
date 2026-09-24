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
    private static readonly Dictionary<object, HostedDialogSession> HostedDialogs = new();

    private static readonly Dictionary<object, HostedControlSession> HostedControlSessions = new();

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

        var openerDocumentKey = workspace.SelectedDocument?.Key;
        var completionSource = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HostedDialogSession(
            actualKey,
            completionSource,
            () => workspace.CloseDocumentByKey(actualKey),
            workspace,
            openerDocumentKey);

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

    /// <summary>
    /// Открывает UserControl (например, AccountSelectionView) как документ MDI
    /// и возвращает результат подтверждения: true — «Выбрать», false — «Отмена»
    /// либо закрытие вкладки пользователем. Контент сам инициирует закрытие через
    /// MdiDialogService.CloseWithResult(this, ...). Если MDI недоступен, контрол
    /// размещается в обычном модальном окне.
    /// </summary>
    public static Task<bool?> ShowControlInWorkspaceForResultAsync(
        Window? owner,
        string title,
        UserControl content,
        bool activate = true)
    {
        var workspace = ResolveWorkspace(owner);
        if (workspace == null)
        {
            // fallback — обычное модальное окно
            var fallback = new Window
            {
                Title = title,
                Content = content,
                Width = 860,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            SetSafeOwner(fallback, owner);
            return Task.FromResult(fallback.ShowDialog());
        }

        var actualKey = $"{content.GetType().FullName}:{Guid.NewGuid():N}";
        var completionSource = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new HostedDialogSession(
            actualKey,
            completionSource,
            () => workspace.CloseDocumentByKey(actualKey));

        // Регистрируем сам контрол: он закроет документ через CloseWithResult(this, ...)
        HostedDialogs[content] = session;

        workspace.OpenDocument(actualKey, title, content, activate);

        // Закрытие вкладки из MDI (кнопка «x») тоже должно завершить ожидание
        void OnDocumentsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                // Вкладки очищены разом (например, «Закрыть все») — завершаем ожидание как отмену
                workspace.Documents.CollectionChanged -= OnDocumentsChanged;
                CompleteHostedDialog(content, false, closeDocument: false);
                return;
            }

            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove || e.OldItems == null)
                return;

            foreach (var item in e.OldItems)
            {
                if (item is MdiDocumentItem document && ReferenceEquals(document.Content, content))
                {
                    workspace.Documents.CollectionChanged -= OnDocumentsChanged;
                    CompleteHostedDialog(content, false, closeDocument: false);
                    return;
                }
            }
        }

        workspace.Documents.CollectionChanged += OnDocumentsChanged;

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

    public static void CloseWithResult(object source, bool? result)
    {
        if (CompleteHostedDialog(source, result))
            return;

        // source — Window (классические диалоги) либо UserControl внутри окна
        // (fallback-режим для контролов). Закрываем окно с результатом.
        var window = source as Window ??
            (source is DependencyObject dependency ? Window.GetWindow(dependency) : null);
        if (window == null)
            return;

        try
        {
            window.DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            window.Close();
        }
    }

    private static bool CompleteHostedDialog(object source, bool? result, bool closeDocument = true)
    {
        if (!HostedDialogs.TryGetValue(source, out var session))
            return false;

        if (session.IsCompleted)
            return true;

        session.IsCompleted = true;
        HostedDialogs.Remove(source);
        session.CompletionSource.TrySetResult(result);

        if (closeDocument)
            session.CloseDocument();

        session.RestoreOpenerFocus();

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
            Action closeDocument,
            MdiWorkspaceControl? workspace = null,
            string? openerDocumentKey = null)
        {
            Key = key;
            CompletionSource = completionSource;
            CloseDocument = closeDocument;
            Workspace = workspace;
            OpenerDocumentKey = openerDocumentKey;
        }

        public string Key { get; }

        public TaskCompletionSource<bool?> CompletionSource { get; }

        public Action CloseDocument { get; }

        public MdiWorkspaceControl? Workspace { get; }

        public string? OpenerDocumentKey { get; }

        public bool IsCompleted { get; set; }

        public void RestoreOpenerFocus()
        {
            try
            {
                if (Workspace == null || string.IsNullOrWhiteSpace(OpenerDocumentKey))
                    return;
                if (string.Equals(OpenerDocumentKey, Key, StringComparison.OrdinalIgnoreCase))
                    return;
                Workspace.ActivateDocumentByKey(OpenerDocumentKey);
            }
            catch
            {
            }
        }
    }

    private sealed class HostedControlSession
    {
        public HostedControlSession(MdiWorkspaceControl workspace, string? openerDocumentKey)
        {
            Workspace = workspace;
            OpenerDocumentKey = openerDocumentKey;
        }

        public MdiWorkspaceControl Workspace { get; }

        public string? OpenerDocumentKey { get; }
    }
}