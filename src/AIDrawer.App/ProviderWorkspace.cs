using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.System;

namespace AIDrawer;

internal sealed class ProviderWorkspace : IDisposable
{
    private readonly Grid _host = new();
    private readonly Func<PermissionRequest, Task<PermissionDecision>> _requestPermissionAsync;
    private readonly HashSet<ulong> _pendingNavigations = [];
    private readonly HashSet<long> _pendingPermissionRequests = [];
    private readonly Dictionary<CoreWebView2DownloadOperation, TypedEventHandler<CoreWebView2DownloadOperation, object>> _activeDownloads = [];
    private WebView2? _webView;
    private Uri? _restoreLocator;
    private Uri? _inMemoryNavigationTarget;
    private WorkspaceStateChangedEventArgs? _lastState;
    private long _nextPermissionRequestId;
    private int _viewGeneration;
    private bool _hasCompletedInitialNavigation;
    private bool _hasCountedCurrentView;
    private bool _shouldExplainHomeFallback;
    private bool _isCreatingWebView;
    private bool _disposed;

    internal ProviderWorkspace(
        string workspaceId,
        ProviderDefinition provider,
        Uri? restoreLocator,
        bool keepActive,
        bool shouldExplainHomeFallback,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync)
    {
        WorkspaceId = workspaceId;
        Provider = provider;
        _restoreLocator = provider.CreateRestoreLocator(restoreLocator?.AbsoluteUri);
        _inMemoryNavigationTarget = _restoreLocator;
        KeepActive = keepActive;
        _shouldExplainHomeFallback = shouldExplainHomeFallback && _restoreLocator is null;
        _requestPermissionAsync = requestPermissionAsync;
        _host.Visibility = Visibility.Collapsed;
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal event EventHandler<RestoreLocatorChangedEventArgs>? RestoreLocatorChanged;

    internal event EventHandler<WorkspaceLifecycleChangedEventArgs>? LifecycleChanged;

    internal event EventHandler<string>? SuccessfulOpen;

    internal ProviderDefinition Provider { get; }

    internal string WorkspaceId { get; }

    internal UIElement View => _host;

    internal bool IsLive => _webView?.CoreWebView2 is not null;

    internal bool IsOperationProtected => _isCreatingWebView
        || _pendingNavigations.Count > 0
        || _pendingPermissionRequests.Count > 0
        || _activeDownloads.Count > 0;

    internal Uri? PromoteCommittedRestoreLocator()
    {
        _restoreLocator = Provider.CreateRestoreLocator(_inMemoryNavigationTarget?.AbsoluteUri);
        return _restoreLocator;
    }

    internal bool KeepActive { get; private set; }

    internal DateTimeOffset ProtectedUntil { get; private set; }

    internal DateTimeOffset LastActivated { get; private set; }

    internal async Task<bool> ActivateAsync(CoreWebView2Environment environment, bool windowIsVisible)
    {
        ThrowIfDisposed();
        _host.Visibility = Visibility.Visible;
        LastActivated = DateTimeOffset.UtcNow;

        if (_webView?.CoreWebView2 is null)
        {
            var created = await CreateWebViewAsync(
                environment,
                _inMemoryNavigationTarget ?? _restoreLocator ?? Provider.HomeUri);
            if (!created)
            {
                if (!_disposed)
                {
                    RaiseLifecycle(WorkspaceLifecyclePhase.Disposed);
                }

                return false;
            }
        }

        SetMemoryTarget(windowIsVisible
            ? CoreWebView2MemoryUsageTargetLevel.Normal
            : CoreWebView2MemoryUsageTargetLevel.Low);
        RaiseLifecycle(WorkspaceLifecyclePhase.Active);
        ReplayLastState();
        return true;
    }

    internal void Deactivate(TimeSpan gracePeriod)
    {
        _host.Visibility = Visibility.Collapsed;
        if (!IsLive)
        {
            RaiseLifecycle(WorkspaceLifecyclePhase.Disposed);
            return;
        }

        ProtectedUntil = DateTimeOffset.UtcNow.Add(gracePeriod);
        SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Low);
        RaiseLifecycle(WorkspaceLifecyclePhase.Recent);
    }

    internal void SetKeepActive(bool keepActive) => KeepActive = keepActive;

    internal void SetWindowVisibility(bool isVisible)
    {
        SetMemoryTarget(isVisible
            ? CoreWebView2MemoryUsageTargetLevel.Normal
            : CoreWebView2MemoryUsageTargetLevel.Low);
    }

    internal bool Reload()
    {
        if (_webView?.CoreWebView2 is null)
        {
            return false;
        }

        try
        {
            _webView.Reload();
            return true;
        }
        catch (Exception exception)
        {
            RaiseState(
                $"{Provider.DisplayName} could not reload",
                $"The current embedded browser view is unavailable ({exception.GetType().Name}). AI Drawer will try to recreate it.",
                InfoBarSeverity.Warning,
                requiresRecovery: true);
            return false;
        }
    }

    internal async Task<bool> RestartAsync(CoreWebView2Environment environment, bool windowIsVisible)
    {
        ThrowIfDisposed();
        RaiseState(
            $"Restarting {Provider.DisplayName}",
            "Recreating this provider workspace with the same local profile.",
            InfoBarSeverity.Informational,
            activity: WorkspaceActivity.Opening);
        CloseWebView();
        return await ActivateAsync(environment, windowIsVisible);
    }

    internal async Task<bool> ResetWebsiteDataAsync(CoreWebView2Environment environment)
    {
        ThrowIfDisposed();
        CloseWebView();
        _host.Visibility = Visibility.Collapsed;

        try
        {
            if (!await CreateWebViewAsync(environment, navigationTarget: null))
            {
                return false;
            }

            var profile = _webView?.CoreWebView2?.Profile
                ?? throw new InvalidOperationException("The provider profile could not be opened.");
            await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            RaiseState(
                $"{Provider.DisplayName} website data reset",
                "All local browsing data was removed from this provider profile, including local sign-in, cookies, cache, site storage, remembered permissions, and browsing or download history. Your provider account and provider-hosted conversations were not deleted.",
                InfoBarSeverity.Success);
            return true;
        }
        catch (Exception exception)
        {
            RaiseState(
                $"{Provider.DisplayName} data reset did not finish",
                $"Some local website data may remain ({exception.GetType().Name}).",
                InfoBarSeverity.Error,
                requiresRecovery: true);
            return false;
        }
        finally
        {
            CloseWebView();
        }
    }

    internal void DisposeView()
    {
        _shouldExplainHomeFallback |= _hasCompletedInitialNavigation
            && _inMemoryNavigationTarget is null;
        CloseWebView();
        _host.Visibility = Visibility.Collapsed;
        RaiseLifecycle(WorkspaceLifecyclePhase.Disposed);
    }

    internal void ClearPersistedRestoreLocator()
    {
        _shouldExplainHomeFallback = false;
        _restoreLocator = null;
    }

    internal void ClearNavigationTargets()
    {
        _inMemoryNavigationTarget = null;
        ClearPersistedRestoreLocator();
    }

    internal void ReportCapacityBlocked()
    {
        RaiseState(
            "Workspace needs a safe memory slot",
            "Other workspaces are completing navigation, a permission request, or a download. Try again after that protected operation finishes.",
            InfoBarSeverity.Warning,
            requiresRecovery: true);
        if (!IsLive)
        {
            RaiseLifecycle(WorkspaceLifecyclePhase.Disposed);
        }
    }

    internal void ReportEnvironmentFailure(Exception exception)
    {
        RaiseState(
            $"{Provider.DisplayName} could not start",
            $"The embedded browser environment could not be created ({exception.GetType().Name}). You can retry after WebView2 is available.",
            InfoBarSeverity.Error,
            requiresRecovery: true);
        if (!IsLive)
        {
            RaiseLifecycle(WorkspaceLifecyclePhase.Disposed);
        }
    }

    private async Task<bool> CreateWebViewAsync(CoreWebView2Environment environment, Uri? navigationTarget)
    {
        CloseWebView();
        RaiseState(
            $"Loading {Provider.DisplayName}",
            "Preparing a local AI Drawer workspace.",
            InfoBarSeverity.Informational,
            activity: WorkspaceActivity.Opening);

        try
        {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = Provider.ProfileName;

            var view = new WebView2();
            var generation = ++_viewGeneration;
            _isCreatingWebView = true;
            _webView = view;
            _host.Children.Add(view);
            await view.EnsureCoreWebView2Async(environment, controllerOptions);
            if (_disposed
                || generation != _viewGeneration
                || !ReferenceEquals(_webView, view)
                || view.CoreWebView2 is null)
            {
                return false;
            }

            Configure(view.CoreWebView2);

            if (navigationTarget is not null)
            {
                view.CoreWebView2.Navigate(navigationTarget.AbsoluteUri);
            }

            return true;
        }
        catch (Exception exception)
        {
            CloseWebView();
            if (!_disposed)
            {
                RaiseState(
                    $"{Provider.DisplayName} could not start",
                    $"The embedded browser could not be initialized ({exception.GetType().Name}). You can retry without removing existing profile data.",
                    InfoBarSeverity.Error,
                    requiresRecovery: true);
            }

            return false;
        }
        finally
        {
            _isCreatingWebView = false;
        }
    }

    private void Configure(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.NavigationStarting += (sender, args) =>
        {
            if (!IsCurrent(core) || string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Provider.IsKnownPurchaseUri(args.Uri))
            {
                args.Cancel = true;
                RaisePurchaseState();
                return;
            }

            if (!Provider.IsAllowedEmbeddedUri(args.Uri))
            {
                args.Cancel = true;
                _ = OpenExternalUriAsync(core, args.Uri);
                return;
            }

            _pendingNavigations.Add(args.NavigationId);

            RaiseState(
                _hasCompletedInitialNavigation ? $"Updating {Provider.DisplayName}" : $"Opening {Provider.DisplayName}",
                "Keeping the AI Drawer navigation available while the provider page changes.",
                InfoBarSeverity.Informational,
                activity: _hasCompletedInitialNavigation ? WorkspaceActivity.Navigating : WorkspaceActivity.Opening);
        };

        core.FrameNavigationStarting += (_, args) =>
        {
            if (IsCurrent(core) && Provider.IsKnownPurchaseUri(args.Uri))
            {
                args.Cancel = true;
                RaisePurchaseState();
            }
        };

        core.NewWindowRequested += (sender, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            args.Handled = true;
            if (Provider.IsKnownPurchaseUri(args.Uri))
            {
                RaisePurchaseState();
                return;
            }

            if (Provider.IsAllowedEmbeddedUri(args.Uri))
            {
                core.Navigate(args.Uri);
                return;
            }

            _ = OpenExternalUriAsync(core, args.Uri);
        };

        core.PermissionRequested += async (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            using var deferral = args.GetDeferral();
            args.Handled = true;
            args.State = CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
            var requestId = ++_nextPermissionRequestId;
            _pendingPermissionRequests.Add(requestId);

            try
            {
                var decision = await _requestPermissionAsync(new PermissionRequest(
                    WorkspaceId,
                    Provider.DisplayName,
                    args.PermissionKind,
                    CreateOriginLabel(args.Uri)));
                if (IsCurrent(core))
                {
                    args.State = decision.Allowed
                        ? CoreWebView2PermissionState.Allow
                        : CoreWebView2PermissionState.Deny;
                    args.SavesInProfile = decision.Remember;
                }
            }
            catch
            {
                // Deny remains the safe fallback when a native prompt cannot be shown.
            }
            finally
            {
                _pendingPermissionRequests.Remove(requestId);
            }
        };

        core.DownloadStarting += (_, args) =>
        {
            if (IsCurrent(core))
            {
                TrackDownload(args.DownloadOperation);
            }
        };

        core.SourceChanged += (_, args) =>
        {
            if (IsCurrent(core) && !args.IsNewDocument)
            {
                CaptureNavigationTargets(core.Source);
            }
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            _pendingNavigations.Remove(args.NavigationId);

            if (args.IsSuccess)
            {
                _hasCompletedInitialNavigation = true;
                CaptureNavigationTargets(core.Source);
                if (!_hasCountedCurrentView)
                {
                    _hasCountedCurrentView = true;
                    SuccessfulOpen?.Invoke(this, WorkspaceId);
                }

                if (_shouldExplainHomeFallback)
                {
                    _shouldExplainHomeFallback = false;
                    RaiseState(
                        "Workspace opened at provider home",
                        $"AI Drawer kept this {Provider.DisplayName} workspace, but no reviewed conversation locator was stored. The workspace may have been left at home, or exact restart restoration may not be available for this provider path.",
                        InfoBarSeverity.Informational);
                    return;
                }

                RaiseState(
                    Provider.DisplayName,
                    $"{Provider.CompatibilityStatus}. Provider sign-in and conversations are managed by the provider in this local profile.",
                    InfoBarSeverity.Success);
                return;
            }

            RaiseState(
                $"{Provider.DisplayName} did not finish loading",
                "Check your connection, then reload or restart this workspace.",
                InfoBarSeverity.Warning,
                requiresRecovery: true);
        };

        core.ProcessFailed += (_, _) =>
        {
            if (IsCurrent(core))
            {
                _pendingNavigations.Clear();
                RaiseState(
                    $"{Provider.DisplayName} stopped responding",
                    "You can reload or restart this workspace. Its local profile is preserved.",
                    InfoBarSeverity.Error,
                    requiresRecovery: true);
            }
        };
    }

    private async Task OpenExternalUriAsync(CoreWebView2 sourceCore, string? rawUri)
    {
        var externalUri = Provider.CreateSafeExternalUri(rawUri);
        if (externalUri is null)
        {
            if (IsCurrent(sourceCore))
            {
                RaiseState(
                    "Unsupported link blocked",
                    "AI Drawer only opens safe HTTPS links in the system browser.",
                    InfoBarSeverity.Warning);
            }

            return;
        }

        if (!IsCurrent(sourceCore))
        {
            return;
        }

        RaiseState(
            "Opening link in your browser",
            "This link is outside the selected provider workspace. Query parameters were not forwarded.",
            InfoBarSeverity.Informational);
        try
        {
            var launched = await Launcher.LaunchUriAsync(externalUri);
            if (IsCurrent(sourceCore) && !launched)
            {
                RaiseState(
                    "Link could not be opened",
                    "Windows did not find an application that could open this HTTPS link.",
                    InfoBarSeverity.Warning);
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(sourceCore))
            {
                RaiseState(
                    "Link could not be opened",
                    $"Windows could not open this HTTPS link ({exception.GetType().Name}).",
                    InfoBarSeverity.Warning);
            }
        }
    }

    private void RaisePurchaseState() => RaiseState(
        "Subscription opens on the provider's website",
        "AI Drawer does not provide or process subscriptions. Purchases stay with the provider.",
        InfoBarSeverity.Warning);

    private void SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel level)
    {
        if (_webView?.CoreWebView2 is not null)
        {
            try
            {
                _webView.CoreWebView2.MemoryUsageTargetLevel = level;
            }
            catch
            {
                // This is a best-effort resource hint and must not break workspace switching.
            }
        }
    }

    private bool IsCurrent(CoreWebView2 core) => ReferenceEquals(_webView?.CoreWebView2, core);

    private void CaptureNavigationTargets(string? rawUri)
    {
        if (!Provider.IsProviderAppUri(rawUri))
        {
            return;
        }

        _inMemoryNavigationTarget = Provider.CreateSafeInMemoryUri(rawUri);
        UpdateRestoreLocator(Provider.CreateRestoreLocator(rawUri));
    }

    private void UpdateRestoreLocator(Uri? restoreLocator)
    {
        if (Equals(restoreLocator, _restoreLocator))
        {
            return;
        }

        _restoreLocator = restoreLocator;
        RestoreLocatorChanged?.Invoke(this, new RestoreLocatorChangedEventArgs(WorkspaceId, restoreLocator));
    }

    private void RaiseLifecycle(WorkspaceLifecyclePhase phase) =>
        LifecycleChanged?.Invoke(this, new WorkspaceLifecycleChangedEventArgs(WorkspaceId, phase));

    private void RaiseState(
        string title,
        string message,
        InfoBarSeverity severity,
        bool requiresRecovery = false,
        WorkspaceActivity activity = WorkspaceActivity.None)
    {
        if (_disposed)
        {
            return;
        }

        _lastState = new WorkspaceStateChangedEventArgs(
            WorkspaceId,
            title,
            message,
            severity,
            requiresRecovery,
            activity);
        StateChanged?.Invoke(this, _lastState);
    }

    private void ReplayLastState()
    {
        if (!_disposed && _lastState is not null)
        {
            StateChanged?.Invoke(this, _lastState);
        }
    }

    private void TrackDownload(CoreWebView2DownloadOperation operation)
    {
        if (operation.State != CoreWebView2DownloadState.InProgress
            || _activeDownloads.ContainsKey(operation))
        {
            return;
        }

        TypedEventHandler<CoreWebView2DownloadOperation, object>? stateChanged = null;
        stateChanged = (_, _) =>
        {
            if (operation.State != CoreWebView2DownloadState.InProgress)
            {
                StopTrackingDownload(operation);
            }
        };
        _activeDownloads.Add(operation, stateChanged);
        operation.StateChanged += stateChanged;
        if (operation.State != CoreWebView2DownloadState.InProgress)
        {
            StopTrackingDownload(operation);
        }
    }

    private void StopTrackingDownload(CoreWebView2DownloadOperation operation)
    {
        if (_activeDownloads.Remove(operation, out var stateChanged))
        {
            try
            {
                operation.StateChanged -= stateChanged;
            }
            catch
            {
                // The download may already have released its COM event source.
            }
        }
    }

    private static string CreateOriginLabel(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return "unknown origin";
        }

        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.IdnHost}{port}";
    }

    private void CloseWebView()
    {
        _viewGeneration++;
        foreach (var (download, stateChanged) in _activeDownloads)
        {
            try
            {
                download.StateChanged -= stateChanged;
            }
            catch
            {
                // Cleanup remains best effort after a browser-process failure.
            }
        }

        _activeDownloads.Clear();
        _pendingNavigations.Clear();
        _pendingPermissionRequests.Clear();
        var view = _webView;
        _webView = null;
        try
        {
            view?.Close();
        }
        catch
        {
            // The underlying browser process may already be unavailable.
        }
        _hasCompletedInitialNavigation = false;
        _hasCountedCurrentView = false;
        _host.Children.Clear();
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
        CloseWebView();
    }
}

internal sealed record PermissionRequest(
    string WorkspaceId,
    string ProviderName,
    CoreWebView2PermissionKind PermissionKind,
    string Origin);

internal sealed record PermissionDecision(bool Allowed, bool Remember);

internal sealed record RestoreLocatorChangedEventArgs(string WorkspaceId, Uri? RestoreLocator);

internal sealed record WorkspaceLifecycleChangedEventArgs(string WorkspaceId, WorkspaceLifecyclePhase Phase);

internal enum WorkspaceLifecyclePhase
{
    Active,
    Recent,
    Disposed
}

internal enum WorkspaceActivity
{
    None,
    Opening,
    Navigating
}

internal sealed class WorkspaceStateChangedEventArgs(
    string workspaceId,
    string title,
    string message,
    InfoBarSeverity severity,
    bool requiresRecovery = false,
    WorkspaceActivity activity = WorkspaceActivity.None) : EventArgs
{
    internal string WorkspaceId { get; } = workspaceId;
    internal string Title { get; } = title;
    internal string Message { get; } = message;
    internal InfoBarSeverity Severity { get; } = severity;
    internal bool RequiresRecovery { get; } = requiresRecovery;
    internal WorkspaceActivity Activity { get; } = activity;
}
