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
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, ProviderWorkspace> _workspaces = new(StringComparer.Ordinal);
    private Task<CoreWebView2Environment>? _environmentTask;
    private WorkspaceLifecyclePolicy _lifecyclePolicy;
    private readonly DispatcherTimer _lifecycleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _disposed;
    private bool _windowIsVisible = true;
    private bool _browserRecoveryInProgress;
    private string? _capacityBlockedWorkspaceId;

    internal WorkspaceCoordinator(
        Panel host,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync,
        MemoryMode memoryMode)
    {
        _host = host;
        _lifecyclePolicy = CreateLifecyclePolicy(memoryMode);
        _requestPermissionAsync = requestPermissionAsync;
        _lifecycleTimer.Tick += LifecycleTimer_Tick;
        _lifecycleTimer.Start();
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal event EventHandler<RestoreLocatorChangedEventArgs>? RestoreLocatorChanged;

    internal event EventHandler<WorkspaceLifecycleChangedEventArgs>? LifecycleChanged;

    internal event EventHandler<string>? SuccessfulOpen;

    internal event EventHandler<NavigationPromptRequestedEventArgs>? NavigationPromptRequested;

    internal static IReadOnlyList<ProviderDefinition> Providers => ProviderCatalog.AvailableProviders;

    internal ProviderWorkspace? ActiveWorkspace { get; private set; }

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

    internal async Task<bool> ActivateAsync(
        string workspaceId,
        ProviderDefinition provider,
        Uri? restoreLocator,
        bool keepActive,
        bool shouldExplainHomeFallback)
    {
        ThrowIfDisposed();
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
            if (_disposed)
            {
                return false;
            }

            if (!_workspaces.TryGetValue(workspaceId, out var nextWorkspace))
            {
                nextWorkspace = new ProviderWorkspace(
                    workspaceId,
                    provider,
                    restoreLocator,
                    keepActive,
                    shouldExplainHomeFallback,
                    _requestPermissionAsync);
                nextWorkspace.StateChanged += Workspace_StateChanged;
                nextWorkspace.RestoreLocatorChanged += Workspace_RestoreLocatorChanged;
                nextWorkspace.LifecycleChanged += Workspace_LifecycleChanged;
                nextWorkspace.SuccessfulOpen += Workspace_SuccessfulOpen;
                nextWorkspace.ProcessFailure += Workspace_ProcessFailure;
                nextWorkspace.NavigationPromptRequested += Workspace_NavigationPromptRequested;
                nextWorkspace.OperationCompleted += Workspace_OperationCompleted;
                _workspaces.Add(workspaceId, nextWorkspace);
                _host.Children.Add(nextWorkspace.View);
            }
            else if (!string.Equals(nextWorkspace.Provider.Id, provider.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A workspace cannot change providers after it has been opened.");
            }
            else
            {
                nextWorkspace.SetKeepActive(keepActive);
            }

            if (!ReferenceEquals(ActiveWorkspace, nextWorkspace))
            {
                _capacityBlockedWorkspaceId = null;
                ActiveWorkspace?.Deactivate(_lifecyclePolicy.GracePeriod);
                ActiveWorkspace = nextWorkspace;
            }

            return await ActivateWorkspaceUnderLockAsync(nextWorkspace);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal void SetWindowVisibility(bool isVisible)
    {
        _windowIsVisible = isVisible;
        ActiveWorkspace?.SetWindowVisibility(isVisible);
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

    internal async Task DeactivateActiveWorkspaceAsync()
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
            if (_disposed)
            {
                return;
            }

            ActiveWorkspace?.Deactivate(_lifecyclePolicy.GracePeriod);
            ActiveWorkspace = null;
            _capacityBlockedWorkspaceId = null;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal void SetActiveWorkspaceKeepActive(bool keepActive) =>
        ActiveWorkspace?.SetKeepActive(keepActive);

    internal void SetMemoryMode(MemoryMode memoryMode)
    {
        _lifecyclePolicy = CreateLifecyclePolicy(memoryMode);
        LifecycleTimer_Tick(this, EventArgs.Empty);
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

            if (string.Equals(_capacityBlockedWorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                _capacityBlockedWorkspaceId = null;
            }

            workspace.ProcessFailure -= Workspace_ProcessFailure;
            workspace.NavigationPromptRequested -= Workspace_NavigationPromptRequested;
            workspace.OperationCompleted -= Workspace_OperationCompleted;
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
                if (!EnsureCapacityFor(workspace))
                {
                    workspace.ReportCapacityBlocked();
                    return false;
                }

                return await workspace.RestartAsync(environment, _windowIsVisible);
            }

            return false;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal async Task<bool> ResetActiveWorkspaceAsync(string expectedWorkspaceId)
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
                var affectedWorkspaces = _workspaces.Values.Where(candidate =>
                        string.Equals(candidate.Provider.Id, workspace.Provider.Id, StringComparison.Ordinal))
                    .ToArray();
                foreach (var affected in affectedWorkspaces)
                {
                    affected.DisposeView();
                    affected.ClearNavigationTargets();
                }

                if (!EnsureCapacityFor(workspace))
                {
                    workspace.ReportCapacityBlocked();
                    return false;
                }

                if (!await workspace.ResetWebsiteDataAsync(environment))
                {
                    return false;
                }

                return true;
            }

            return false;
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    private bool EnsureCapacityFor(ProviderWorkspace nextWorkspace)
    {
        if (nextWorkspace.IsLive)
        {
            return true;
        }

        var liveWorkspaces = _workspaces.Values.Where(workspace => workspace.IsLive).ToList();
        if (liveWorkspaces.Count < _lifecyclePolicy.HardLiveLimit)
        {
            return true;
        }

        foreach (var workspaceId in _lifecyclePolicy.SelectForDisposal(
                     CreateLiveStates(liveWorkspaces),
                     DateTimeOffset.UtcNow,
                     enforceHardLimit: true))
        {
            if (_workspaces.TryGetValue(workspaceId, out var workspace))
            {
                workspace.DisposeView();
            }
        }

        return _workspaces.Values.Count(workspace => workspace.IsLive) < _lifecyclePolicy.HardLiveLimit;
    }

    private async Task<bool> ActivateWorkspaceUnderLockAsync(ProviderWorkspace workspace)
    {
        if (!EnsureCapacityFor(workspace))
        {
            _capacityBlockedWorkspaceId = workspace.WorkspaceId;
            workspace.ReportCapacityBlocked();
            return false;
        }

        var environment = await GetEnvironmentAsync(workspace);
        if (environment is null || _disposed)
        {
            return false;
        }

        var activated = await workspace.ActivateAsync(environment, _windowIsVisible);
        if (activated && string.Equals(_capacityBlockedWorkspaceId, workspace.WorkspaceId, StringComparison.Ordinal))
        {
            _capacityBlockedWorkspaceId = null;
        }

        return activated;
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

    private void Workspace_OperationCompleted(object? sender, EventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _host.DispatcherQueue.TryEnqueue(RetryCapacityBlockedWorkspaceFromQueueAsync);
    }

    private async void RetryCapacityBlockedWorkspaceFromQueueAsync()
    {
        try
        {
            await RetryCapacityBlockedWorkspaceAsync();
        }
        catch (OperationCanceledException)
        {
            // Shutdown cancels a deferred workspace activation.
        }
        catch
        {
            // A deferred activation must not surface an unhandled event exception.
        }
    }

    private async Task RetryCapacityBlockedWorkspaceAsync()
    {
        await _selectionLock.WaitAsync(_lifetimeCancellation.Token);
        try
        {
            if (_disposed
                || _capacityBlockedWorkspaceId is not { } workspaceId
                || ActiveWorkspace is not { } activeWorkspace
                || !string.Equals(activeWorkspace.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                return;
            }

            activeWorkspace.ReportCapacityRetrying();
            await ActivateWorkspaceUnderLockAsync(activeWorkspace);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

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
                ReleaseInactiveWorkspacesForMemoryPressure(args.WorkspaceId);
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

            _environmentTask = null;
            foreach (var workspace in _workspaces.Values.Where(workspace => workspace.IsLive).ToArray())
            {
                workspace.DisposeView();
            }

            if (ActiveWorkspace is not { } activeWorkspace)
            {
                return;
            }

            var environment = await GetEnvironmentAsync(activeWorkspace);
            if (environment is not null && !_disposed)
            {
                await activeWorkspace.ActivateAsync(environment, _windowIsVisible);
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
                || ActiveWorkspace is not { } activeWorkspace
                || !string.Equals(activeWorkspace.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                return;
            }

            var environment = await GetEnvironmentAsync(activeWorkspace);
            if (environment is not null && !_disposed && EnsureCapacityFor(activeWorkspace))
            {
                await activeWorkspace.RestartAsync(environment, _windowIsVisible);
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

    private void ReleaseInactiveWorkspacesForMemoryPressure(string failedWorkspaceId)
    {
        foreach (var workspace in _workspaces.Values.Where(workspace =>
                     !string.Equals(workspace.WorkspaceId, failedWorkspaceId, StringComparison.Ordinal)
                     && !ReferenceEquals(workspace, ActiveWorkspace)
                     && workspace.IsLive).ToArray())
        {
            workspace.DisposeView();
        }
    }

    private void LifecycleTimer_Tick(object? sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        var liveWorkspaces = _workspaces.Values.Where(workspace => workspace.IsLive).ToList();
        foreach (var workspaceId in _lifecyclePolicy.SelectForDisposal(
                     CreateLiveStates(liveWorkspaces),
                     DateTimeOffset.UtcNow,
                     enforceHardLimit: false))
        {
            if (_workspaces.TryGetValue(workspaceId, out var workspace))
            {
                workspace.DisposeView();
            }
        }
    }

    private LiveWorkspaceState[] CreateLiveStates(
        IReadOnlyCollection<ProviderWorkspace> workspaces) => workspaces
        .Select(workspace => new LiveWorkspaceState(
            workspace.WorkspaceId,
            ReferenceEquals(workspace, ActiveWorkspace),
            workspace.KeepActive,
            workspace.ProtectedUntil,
            workspace.LastActivated,
            workspace.IsOperationProtected))
        .ToArray();

    private static WorkspaceLifecyclePolicy CreateLifecyclePolicy(MemoryMode memoryMode) => memoryMode switch
    {
        MemoryMode.LowMemory => new WorkspaceLifecyclePolicy(1, 2, TimeSpan.FromMinutes(1)),
        MemoryMode.FastSwitching => new WorkspaceLifecyclePolicy(3, 4, TimeSpan.FromMinutes(15)),
        _ => new WorkspaceLifecyclePolicy(2, 3, TimeSpan.FromMinutes(5))
    };

    private async Task<CoreWebView2Environment?> GetEnvironmentAsync(ProviderWorkspace workspace)
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

            workspace.ReportEnvironmentFailure(exception);
            return null;
        }
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(UserDataRoot);
        return await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            UserDataRoot,
            new CoreWebView2EnvironmentOptions());
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
        _lifecycleTimer.Stop();
        _lifetimeCancellation.Cancel();
        foreach (var workspace in _workspaces.Values)
        {
            workspace.ProcessFailure -= Workspace_ProcessFailure;
            workspace.NavigationPromptRequested -= Workspace_NavigationPromptRequested;
            workspace.Dispose();
        }

        _host.Children.Clear();
        _lifetimeCancellation.Dispose();
    }
}
