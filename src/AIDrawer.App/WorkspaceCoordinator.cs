using System.Diagnostics;
using AIDrawer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal sealed class WorkspaceCoordinator : IDisposable
{
    private static readonly string UserDataRoot = Path.Combine(
        ApplicationDataPaths.AppDataRoot,
        "WebView2");

    private readonly Panel _host;
    private readonly Func<PermissionRequest, Task<PermissionDecision>> _requestPermissionAsync;
    private readonly Func<DownloadRequest, Task<DownloadDecision>> _requestDownloadAsync;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, ProviderWorkspace> _workspaces = new(StringComparer.Ordinal);
    private Task<CoreWebView2Environment>? _environmentTask;
    private CoreWebView2Environment? _observedEnvironment;
    private Process? _observedBrowserProcess;
    private readonly HashSet<string> _visibleWorkspaceIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _releasedWorkspaceIds = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _windowIsVisible = true;
    private bool _browserRecoveryInProgress;
    private bool _profileCleanupInProgress;

    internal WorkspaceCoordinator(
        Panel host,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync,
        Func<DownloadRequest, Task<DownloadDecision>> requestDownloadAsync)
    {
        _host = host;
        _requestPermissionAsync = requestPermissionAsync;
        _requestDownloadAsync = requestDownloadAsync;
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal event EventHandler<RestoreLocatorChangedEventArgs>? RestoreLocatorChanged;

    internal event EventHandler<WorkspaceLifecycleChangedEventArgs>? LifecycleChanged;

    internal event EventHandler<string>? SuccessfulOpen;

    internal event EventHandler<NavigationPromptRequestedEventArgs>? NavigationPromptRequested;

    internal static IReadOnlyList<ProviderDefinition> Providers => ProviderCatalog.AvailableProviders;

    internal ProviderWorkspace? ActiveWorkspace { get; private set; }

    internal bool TryGetActiveBrowserRecoveryUri(string expectedWorkspaceId, out Uri recoveryUri)
    {
        if (ActiveWorkspace is { } workspace
            && string.Equals(workspace.WorkspaceId, expectedWorkspaceId, StringComparison.Ordinal))
        {
            recoveryUri = workspace.BrowserRecoveryUri;
            return true;
        }

        recoveryUri = null!;
        return false;
    }

    internal bool TryPromoteCommittedRestoreLocator(string workspaceId, out Uri? restoreLocator)
    {
        if (_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            restoreLocator = workspace.PromoteCommittedRestoreLocator();
            return true;
        }

        restoreLocator = null;
        return false;
    }

    internal event EventHandler<string>? FocusRequested;

    internal async Task ApplyLayoutAsync(
        IReadOnlyList<WorkspaceTab> visibleWorkspaces,
        string? focusedId,
        bool restoreExactWorkspace,
        string? resumeWorkspaceId)
    {
        ThrowIfDisposed();
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            if (_disposed)
            {
                return;
            }
            _visibleWorkspaceIds.Clear();
            foreach (var tab in visibleWorkspaces.Where(tab => tab.Provider is not null))
            {
                _visibleWorkspaceIds.Add(tab.Id);
            }
            foreach (var workspace in _workspaces.Values)
            {
                if (!_visibleWorkspaceIds.Contains(workspace.WorkspaceId))
                {
                    workspace.SetPresentation(false, _windowIsVisible);
                }
            }
            ActiveWorkspace = null;
            for (var index = 0; index < visibleWorkspaces.Count; index++)
            {
                var tab = visibleWorkspaces[index];
                if (tab.Provider is not { } provider)
                {
                    continue;
                }
                if (!_workspaces.TryGetValue(tab.Id, out var workspace))
                {
                    workspace = new ProviderWorkspace(
                        tab.Id, provider,
                        restoreExactWorkspace ? tab.RestoreLocator : null,
                        restoreExactWorkspace && tab.ShouldExplainHomeFallback,
                        _requestPermissionAsync, _requestDownloadAsync);
                    workspace.StateChanged += Workspace_StateChanged;
                    workspace.RestoreLocatorChanged += Workspace_RestoreLocatorChanged;
                    workspace.LifecycleChanged += Workspace_LifecycleChanged;
                    workspace.SuccessfulOpen += Workspace_SuccessfulOpen;
                    workspace.ProcessFailure += Workspace_ProcessFailure;
                    workspace.NavigationPromptRequested += Workspace_NavigationPromptRequested;
                    workspace.BrowserProcessAvailable += Workspace_BrowserProcessAvailable;
                    workspace.FocusRequested += Workspace_FocusRequested;
                    _workspaces.Add(tab.Id, workspace);
                    _host.Children.Add(workspace.View);
                }
                Grid.SetColumn((FrameworkElement)workspace.View, index * 2);
                if (tab.Id == focusedId)
                {
                    ActiveWorkspace = workspace;
                }
                workspace.SetPresentation(true, _windowIsVisible);
                if (tab.Id == resumeWorkspaceId)
                {
                    _releasedWorkspaceIds.Remove(tab.Id);
                }
                if (!workspace.IsLive && !_releasedWorkspaceIds.Contains(tab.Id))
                {
                    var environment = await GetEnvironmentAsync(workspace);
                    if (environment is not null && !_disposed)
                    {
                        await workspace.EnsureCreatedAsync(environment);
                    }
                }
                if (_disposed)
                {
                    return;
                }
                workspace.SetPresentation(true, _windowIsVisible);
            }
            ActiveWorkspace?.ReplayState();
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    private void Workspace_FocusRequested(object? sender, string workspaceId)
    {
        if (_visibleWorkspaceIds.Contains(workspaceId))
        {
            FocusRequested?.Invoke(this, workspaceId);
        }
    }

    internal void SetWindowVisibility(bool isVisible)
    {
        _windowIsVisible = isVisible;
        foreach (var workspace in _workspaces.Values)
        {
            workspace.SetPresentation(_visibleWorkspaceIds.Contains(workspace.WorkspaceId), isVisible);
        }
    }

    internal void FocusWorkspace(string workspaceId)
    {
        if (_visibleWorkspaceIds.Contains(workspaceId) && _workspaces.TryGetValue(workspaceId, out var workspace))
        {
            ActiveWorkspace = workspace;
            workspace.ReplayState();
        }
    }

    internal async Task<bool> ReleaseWorkspaceAsync(string workspaceId)
    {
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        try
        {
            if (_disposed || !_workspaces.TryGetValue(workspaceId, out var workspace)
                || workspace.IsOperationProtected)
            {
                return false;
            }
            workspace.ReleasePage();
            _releasedWorkspaceIds.Add(workspaceId);
            return true;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal bool ReloadActiveWorkspace(string expectedWorkspaceId)
    {
        if (ActiveWorkspace is not { IsLive: true } workspace
            || !string.Equals(workspace.WorkspaceId, expectedWorkspaceId, StringComparison.Ordinal))
        {
            return false;
        }

        return workspace.Reload();
    }

    internal void ClearAllPersistedRestoreLocators()
    {
        foreach (var workspace in _workspaces.Values)
        {
            workspace.ClearPersistedRestoreLocator();
        }
    }

    internal async Task RemoveWorkspaceAsync(string workspaceId)
    {
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (_disposed || !_workspaces.Remove(workspaceId, out var workspace))
            {
                return;
            }

            if (ReferenceEquals(ActiveWorkspace, workspace))
            {
                ActiveWorkspace = null;
            }

            _visibleWorkspaceIds.Remove(workspaceId);
            _releasedWorkspaceIds.Remove(workspaceId);

            workspace.ProcessFailure -= Workspace_ProcessFailure;
            workspace.NavigationPromptRequested -= Workspace_NavigationPromptRequested;
            workspace.FocusRequested -= Workspace_FocusRequested;
            workspace.BrowserProcessAvailable -= Workspace_BrowserProcessAvailable;
            workspace.Dispose();
            _host.Children.Remove(workspace.View);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal async Task<bool> RestartActiveWorkspaceAsync(string expectedWorkspaceId)
    {
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        try
        {
            if (_disposed
                || ActiveWorkspace is not { } workspace
                || !string.Equals(workspace.WorkspaceId, expectedWorkspaceId, StringComparison.Ordinal))
            {
                return false;
            }

            var environment = await GetEnvironmentAsync(workspace);
            if (environment is not null && !_disposed)
            {
                _releasedWorkspaceIds.Remove(workspace.WorkspaceId);
                return await workspace.RestartAsync(environment);
            }

            return false;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal Task<ProviderProfileCleanupBatchResult?> ClearActiveProviderCacheAsync(string expectedWorkspaceId) =>
        ClearActiveProviderDataAsync(expectedWorkspaceId, CoreWebView2BrowsingDataKinds.DiskCache, clearNavigationTargets: false);

    internal Task<ProviderProfileCleanupBatchResult?> ResetActiveWorkspaceAsync(string expectedWorkspaceId) =>
        ClearActiveProviderDataAsync(expectedWorkspaceId, CoreWebView2BrowsingDataKinds.AllProfile, clearNavigationTargets: true);

    internal async Task<ProviderProfileCleanupBatchResult?> ResetAllProviderDataAsync()
    {
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (_disposed)
            {
                return null;
            }

            _profileCleanupInProgress = true;
            DetachObservedBrowserProcess();
            foreach (var workspace in _workspaces.Values)
            {
                workspace.DisposeView();
                workspace.ClearNavigationTargets();
            }

            var environment = await GetEnvironmentAsync(ActiveWorkspace);
            if (environment is null || _disposed)
            {
                return null;
            }

            var cleaner = new ProviderProfileDataCleaner(_host, environment);
            return await cleaner.ClearAsync(Providers, CoreWebView2BrowsingDataKinds.AllProfile);
        }
        finally
        {
            _profileCleanupInProgress = false;
            _selectionLock.Release();
        }
    }

    private async Task<ProviderProfileCleanupBatchResult?> ClearActiveProviderDataAsync(
        string expectedWorkspaceId,
        CoreWebView2BrowsingDataKinds dataKinds,
        bool clearNavigationTargets)
    {
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (_disposed
                || ActiveWorkspace is not { } workspace
                || !string.Equals(workspace.WorkspaceId, expectedWorkspaceId, StringComparison.Ordinal))
            {
                return null;
            }

            _profileCleanupInProgress = true;
            DetachObservedBrowserProcess();
            var affectedWorkspaces = _workspaces.Values.Where(candidate =>
                    string.Equals(candidate.Provider.Id, workspace.Provider.Id, StringComparison.Ordinal))
                .ToArray();
            foreach (var affected in affectedWorkspaces)
            {
                affected.DisposeView();
                if (clearNavigationTargets)
                {
                    affected.ClearNavigationTargets();
                }
            }

            var environment = await GetEnvironmentAsync(workspace);
            if (environment is null || _disposed)
            {
                return null;
            }

            var cleaner = new ProviderProfileDataCleaner(_host, environment);
            return await cleaner.ClearAsync([workspace.Provider], dataKinds);
        }
        finally
        {
            _profileCleanupInProgress = false;
            _selectionLock.Release();
        }
    }

    private void Workspace_StateChanged(object? sender, WorkspaceStateChangedEventArgs args) =>
        StateChanged?.Invoke(this, args);

    private void Workspace_RestoreLocatorChanged(object? sender, RestoreLocatorChangedEventArgs args) =>
        RestoreLocatorChanged?.Invoke(this, args);

    private void Workspace_LifecycleChanged(object? sender, WorkspaceLifecycleChangedEventArgs args) =>
        LifecycleChanged?.Invoke(this, args);

    private void Workspace_SuccessfulOpen(object? sender, string workspaceId) =>
        SuccessfulOpen?.Invoke(this, workspaceId);

    private void Workspace_NavigationPromptRequested(object? sender, NavigationPromptRequestedEventArgs args) =>
        NavigationPromptRequested?.Invoke(this, args);

    private async void Workspace_ProcessFailure(object? sender, WorkspaceProcessFailureEventArgs args)
    {
        switch (args.Kind)
        {
            case WorkspaceProcessFailureKind.BrowserExit:
                await RecoverBrowserProcessAsync();
                break;
            case WorkspaceProcessFailureKind.RendererExit:
                await RecoverRendererAsync(args.WorkspaceId);
                break;
            case WorkspaceProcessFailureKind.OutOfMemory:
                // Preserve other pages: they may contain drafts or ongoing work.
                break;
        }
    }

    private async Task RecoverBrowserProcessAsync()
    {
        if (_browserRecoveryInProgress)
        {
            return;
        }

        var lockTaken = false;
        try
        {
            _browserRecoveryInProgress = true;
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
            lockTaken = true;
            if (_disposed)
            {
                return;
            }

            DetachObservedBrowserProcess();
            DetachObservedEnvironment();
            _environmentTask = null;
            foreach (var workspace in _workspaces.Values.Where(workspace => workspace.IsLive).ToArray())
            {
                workspace.DisposeView();
            }

            var visible = _workspaces.Values.Where(workspace => _visibleWorkspaceIds.Contains(workspace.WorkspaceId)
                && !_releasedWorkspaceIds.Contains(workspace.WorkspaceId)).ToArray();
            var environment = await GetEnvironmentAsync(visible.FirstOrDefault());
            if (environment is not null && !_disposed)
            {
                foreach (var workspace in visible)
                {
                    await workspace.EnsureCreatedAsync(environment);
                    if (_disposed)
                    {
                        return;
                    }
                    workspace.SetPresentation(true, _windowIsVisible);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels the recovery operation.
        }
        finally
        {
            if (lockTaken)
            {
                _selectionLock.Release();
            }

            _browserRecoveryInProgress = false;
        }
    }

    private async Task RecoverRendererAsync(string workspaceId)
    {
        var lockTaken = false;
        try
        {
            await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
            lockTaken = true;
            if (_disposed
                || !_visibleWorkspaceIds.Contains(workspaceId)
                || _releasedWorkspaceIds.Contains(workspaceId)
                || !_workspaces.TryGetValue(workspaceId, out var activeWorkspace))
            {
                return;
            }

            var environment = await GetEnvironmentAsync(activeWorkspace);
            if (environment is not null && !_disposed)
            {
                await activeWorkspace.RestartAsync(environment);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels the recovery operation.
        }
        finally
        {
            if (lockTaken)
            {
                _selectionLock.Release();
            }
        }
    }

    private async Task<CoreWebView2Environment?> GetEnvironmentAsync(ProviderWorkspace? workspace)
    {
        var environmentTask = _environmentTask ??= CreateEnvironmentAsync();
        try
        {
            return await environmentTask;
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_environmentTask, environmentTask))
            {
                _environmentTask = null;
            }

            workspace?.ReportEnvironmentFailure(exception);
            return null;
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(UserDataRoot);
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            UserDataRoot,
            new CoreWebView2EnvironmentOptions());
        environment.BrowserProcessExited += Environment_BrowserProcessExited;
        _observedEnvironment = environment;
        return environment;
    }

    private void Environment_BrowserProcessExited(
        object? sender,
        CoreWebView2BrowserProcessExitedEventArgs args)
    {
        if (_disposed
            || sender is not CoreWebView2Environment exitedEnvironment
            || !ReferenceEquals(exitedEnvironment, _observedEnvironment)
            || _profileCleanupInProgress
            || _visibleWorkspaceIds.Count == 0)
        {
            return;
        }

        _host.DispatcherQueue.TryEnqueue(() =>
            RecoverBrowserProcessAfterExitFromQueueAsync(exitedEnvironment));
    }

    private async void RecoverBrowserProcessAfterExitFromQueueAsync(
        CoreWebView2Environment exitedEnvironment)
    {
        try
        {
            for (var attempt = 0; _browserRecoveryInProgress && attempt < 50; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _lifetimeCancellation.Token);
            }

            if (_disposed
                || _browserRecoveryInProgress
                || _observedEnvironment is not null
                    && !ReferenceEquals(_observedEnvironment, exitedEnvironment))
            {
                return;
            }

            await RecoverBrowserProcessAsync();
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels a queued browser-environment recovery.
        }
        catch
        {
            // The active workspace reports environment failures through the normal recovery UI.
        }
    }

    private void Workspace_BrowserProcessAvailable(object? sender, uint browserProcessId)
    {
        if (_disposed || browserProcessId > int.MaxValue)
        {
            return;
        }

        var processId = (int)browserProcessId;
        if (_observedBrowserProcess is { Id: var observedProcessId } && observedProcessId == processId)
        {
            return;
        }

        DetachObservedBrowserProcess();
        try
        {
            var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            process.Exited += ObservedBrowserProcess_Exited;
            _observedBrowserProcess = process;
            if (process.HasExited)
            {
                _host.DispatcherQueue.TryEnqueue(() =>
                    RecoverBrowserProcessAfterObservedExitFromQueueAsync(process));
            }
        }
        catch (ArgumentException)
        {
            // The browser exited between WebView creation and watcher attachment.
            _host.DispatcherQueue.TryEnqueue(RecoverBrowserProcessFromMissingWatcherAsync);
        }
        catch (InvalidOperationException)
        {
            // The browser exited between WebView creation and watcher attachment.
            _host.DispatcherQueue.TryEnqueue(RecoverBrowserProcessFromMissingWatcherAsync);
        }
    }

    private void ObservedBrowserProcess_Exited(object? sender, EventArgs args)
    {
        if (sender is Process exitedProcess)
        {
            _host.DispatcherQueue.TryEnqueue(() =>
                RecoverBrowserProcessAfterObservedExitFromQueueAsync(exitedProcess));
        }
    }

    private async void RecoverBrowserProcessAfterObservedExitFromQueueAsync(Process exitedProcess)
    {
        try
        {
            for (var attempt = 0; _browserRecoveryInProgress && attempt < 50; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _lifetimeCancellation.Token);
            }

            if (_disposed
                || _profileCleanupInProgress
                || _browserRecoveryInProgress
                || _observedBrowserProcess is not null
                    && !ReferenceEquals(_observedBrowserProcess, exitedProcess)
                || _visibleWorkspaceIds.Count == 0)
            {
                return;
            }

            await RecoverBrowserProcessAsync();
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels a queued browser-process recovery.
        }
        catch
        {
            // The active workspace reports environment failures through the normal recovery UI.
        }
    }

    private async void RecoverBrowserProcessFromMissingWatcherAsync()
    {
        try
        {
            if (_disposed || _profileCleanupInProgress || _visibleWorkspaceIds.Count == 0)
            {
                return;
            }

            await RecoverBrowserProcessAsync();
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels a queued browser-process recovery.
        }
        catch
        {
            // The active workspace reports environment failures through the normal recovery UI.
        }
    }

    private void DetachObservedBrowserProcess()
    {
        if (_observedBrowserProcess is not { } process)
        {
            return;
        }

        process.Exited -= ObservedBrowserProcess_Exited;
        process.Dispose();
        _observedBrowserProcess = null;
    }

    private void DetachObservedEnvironment()
    {
        if (_observedEnvironment is not { } environment)
        {
            return;
        }

        environment.BrowserProcessExited -= Environment_BrowserProcessExited;
        _observedEnvironment = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachObservedBrowserProcess();
        DetachObservedEnvironment();
        _lifetimeCancellation.Cancel();
        foreach (var workspace in _workspaces.Values)
        {
            workspace.FocusRequested -= Workspace_FocusRequested;
            workspace.ProcessFailure -= Workspace_ProcessFailure;
            workspace.NavigationPromptRequested -= Workspace_NavigationPromptRequested;
            workspace.BrowserProcessAvailable -= Workspace_BrowserProcessAvailable;
            workspace.Dispose();
        }

        _host.Children.Clear();
        _lifetimeCancellation.Dispose();
    }
}
