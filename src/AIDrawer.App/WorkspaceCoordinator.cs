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
    private readonly SemaphoreSlim _selectionLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, ProviderWorkspace> _workspaces;
    private readonly Task<CoreWebView2Environment> _environmentTask;
    private bool _disposed;
    private bool _windowIsVisible = true;

    internal WorkspaceCoordinator(
        Panel host,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync)
    {
        _host = host;
        _environmentTask = CreateEnvironmentAsync();
        _workspaces = ProviderCatalog.AvailableProviders.ToDictionary(
            provider => provider.Id,
            provider => new ProviderWorkspace(provider, requestPermissionAsync),
            StringComparer.OrdinalIgnoreCase);

        foreach (var workspace in _workspaces.Values)
        {
            workspace.StateChanged += Workspace_StateChanged;
            _host.Children.Add(workspace.View);
        }
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal IReadOnlyList<ProviderDefinition> Providers => ProviderCatalog.AvailableProviders;

    internal ProviderWorkspace? ActiveWorkspace { get; private set; }

    internal async Task SelectAsync(string providerId)
    {
        ThrowIfDisposed();
        if (!_workspaces.TryGetValue(providerId, out var nextWorkspace))
        {
            throw new ArgumentOutOfRangeException(nameof(providerId));
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

            ActiveWorkspace?.Deactivate();
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
        while (liveWorkspaces.Count >= 2)
        {
            var workspaceToDispose = liveWorkspaces
                .Where(workspace => !ReferenceEquals(workspace, nextWorkspace))
                .OrderBy(workspace => workspace.LastActivated)
                .First();
            workspaceToDispose.DisposeView();
            liveWorkspaces.Remove(workspaceToDispose);
        }
    }

    private void Workspace_StateChanged(object? sender, WorkspaceStateChangedEventArgs args) =>
        StateChanged?.Invoke(this, args);

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
        _lifetimeCancellation.Cancel();
        foreach (var workspace in _workspaces.Values)
        {
            workspace.Dispose();
        }

        _host.Children.Clear();
        _lifetimeCancellation.Dispose();
    }
}
