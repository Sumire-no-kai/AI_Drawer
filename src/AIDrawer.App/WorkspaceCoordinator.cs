using AIDrawer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal sealed class WorkspaceCoordinator : IDisposable
{
    private static readonly string UserDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI Drawer",
        "WebView2");

    private readonly Panel _host;
    private readonly Func<PermissionRequest, Task<PermissionDecision>> _requestPermissionAsync;
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, ProviderWorkspace> _workspaces = new(StringComparer.Ordinal);
    private readonly Task<CoreWebView2Environment> _environmentTask;
    private WorkspaceLifecyclePolicy _lifecyclePolicy;
    private readonly DispatcherTimer _lifecycleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _disposed;
    private bool _windowIsVisible = true;

    internal WorkspaceCoordinator(
        Panel host,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync,
        MemoryMode memoryMode)
    {
        _host = host;
        _lifecyclePolicy = CreateLifecyclePolicy(memoryMode);
        _environmentTask = CreateEnvironmentAsync();
        _requestPermissionAsync = requestPermissionAsync;
        _lifecycleTimer.Tick += LifecycleTimer_Tick;
        _lifecycleTimer.Start();
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal event EventHandler<RestoreLocatorChangedEventArgs>? RestoreLocatorChanged;

    internal event EventHandler<WorkspaceLifecycleChangedEventArgs>? LifecycleChanged;

    internal event EventHandler<string>? SuccessfulOpen;

    internal IReadOnlyList<ProviderDefinition> Providers => ProviderCatalog.AvailableProviders;

    internal ProviderWorkspace? ActiveWorkspace { get; private set; }

    internal async Task ActivateAsync(
        string workspaceId,
        ProviderDefinition provider,
        Uri? restoreLocator,
        bool keepActive,
        bool shouldExplainHomeFallback)
    {
        ThrowIfDisposed();
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

            var environment = await GetEnvironmentAsync(nextWorkspace);
            if (environment is null || _disposed)
            {
                return;
            }

            if (ReferenceEquals(ActiveWorkspace, nextWorkspace))
            {
                if (nextWorkspace.IsLive)
                {
                    nextWorkspace.SetWindowVisibility(_windowIsVisible, isActive: true);
                }
                else
                {
                    await nextWorkspace.ActivateAsync(environment, _windowIsVisible);
                }

                return;
            }

            ActiveWorkspace?.Deactivate(_lifecyclePolicy.GracePeriod);
            EnsureCapacityFor(nextWorkspace);
            ActiveWorkspace = nextWorkspace;
            await nextWorkspace.ActivateAsync(environment, _windowIsVisible);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal void SetWindowVisibility(bool isVisible)
    {
        _windowIsVisible = isVisible;
        ActiveWorkspace?.SetWindowVisibility(isVisible, isActive: true);
    }

    internal void ReloadActiveWorkspace() => ActiveWorkspace?.Reload();

    internal void DeactivateActiveWorkspace()
    {
        ActiveWorkspace?.Deactivate(_lifecyclePolicy.GracePeriod);
        ActiveWorkspace = null;
    }

    internal void SetActiveWorkspaceKeepActive(bool keepActive) =>
        ActiveWorkspace?.SetKeepActive(keepActive);

    internal void SetMemoryMode(MemoryMode memoryMode)
    {
        _lifecyclePolicy = CreateLifecyclePolicy(memoryMode);
        LifecycleTimer_Tick(this, EventArgs.Empty);
    }

    internal void ClearAllRestoreLocators()
    {
        foreach (var workspace in _workspaces.Values)
        {
            workspace.ClearRestoreLocator();
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

            workspace.Dispose();
            _host.Children.Remove(workspace.View);
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal async Task RestartActiveWorkspaceAsync()
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
            if (_disposed || ActiveWorkspace is not { } workspace)
            {
                return;
            }

            var environment = await GetEnvironmentAsync(workspace);
            if (environment is not null && !_disposed)
            {
                await workspace.RestartAsync(environment, _windowIsVisible);
            }
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    internal async Task ResetActiveWorkspaceAsync()
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
            if (_disposed || ActiveWorkspace is not { } workspace)
            {
                return;
            }

            var environment = await GetEnvironmentAsync(workspace);
            if (environment is not null && !_disposed)
            {
                var providerId = workspace.Provider.Id;
                foreach (var affected in _workspaces.Values.Where(candidate =>
                             string.Equals(candidate.Provider.Id, providerId, StringComparison.Ordinal)))
                {
                    affected.ClearRestoreLocator();
                    if (!ReferenceEquals(affected, workspace))
                    {
                        affected.DisposeView();
                    }
                }

                await workspace.ResetWebsiteDataAsync(environment);
            }
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    private void EnsureCapacityFor(ProviderWorkspace nextWorkspace)
    {
        if (nextWorkspace.IsLive)
        {
            return;
        }

        var liveWorkspaces = _workspaces.Values.Where(workspace => workspace.IsLive).ToList();
        if (liveWorkspaces.Count < _lifecyclePolicy.HardLiveLimit)
        {
            return;
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
    }

    private void Workspace_StateChanged(object? sender, WorkspaceStateChangedEventArgs args) =>
        StateChanged?.Invoke(this, args);

    private void Workspace_RestoreLocatorChanged(object? sender, RestoreLocatorChangedEventArgs args) =>
        RestoreLocatorChanged?.Invoke(this, args);

    private void Workspace_LifecycleChanged(object? sender, WorkspaceLifecycleChangedEventArgs args) =>
        LifecycleChanged?.Invoke(this, args);

    private void Workspace_SuccessfulOpen(object? sender, string workspaceId) =>
        SuccessfulOpen?.Invoke(this, workspaceId);

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

    private IReadOnlyCollection<LiveWorkspaceState> CreateLiveStates(
        IReadOnlyCollection<ProviderWorkspace> workspaces) => workspaces
        .Select(workspace => new LiveWorkspaceState(
            workspace.WorkspaceId,
            ReferenceEquals(workspace, ActiveWorkspace),
            workspace.KeepActive,
            workspace.ProtectedUntil,
            workspace.LastActivated))
        .ToArray();

    private static WorkspaceLifecyclePolicy CreateLifecyclePolicy(MemoryMode memoryMode) => memoryMode switch
    {
        MemoryMode.LowMemory => new WorkspaceLifecyclePolicy(1, 2, TimeSpan.FromMinutes(1)),
        MemoryMode.FastSwitching => new WorkspaceLifecyclePolicy(3, 4, TimeSpan.FromMinutes(15)),
        _ => new WorkspaceLifecyclePolicy(2, 3, TimeSpan.FromMinutes(5))
    };

    private async Task<CoreWebView2Environment?> GetEnvironmentAsync(ProviderWorkspace workspace)
    {
        try
        {
            return await _environmentTask;
        }
        catch (Exception exception)
        {
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
            workspace.Dispose();
        }

        _host.Children.Clear();
        _lifetimeCancellation.Dispose();
    }
}
