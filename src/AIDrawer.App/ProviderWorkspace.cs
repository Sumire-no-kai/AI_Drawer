using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace AIDrawer;

internal sealed class ProviderWorkspace : IDisposable
{
    private readonly Grid _host = new();
    private readonly Func<PermissionRequest, Task<PermissionDecision>> _requestPermissionAsync;
    private WebView2? _webView;
    private bool _hasCompletedInitialNavigation;
    private bool _disposed;

    internal ProviderWorkspace(
        string workspaceId,
        ProviderDefinition provider,
        Func<PermissionRequest, Task<PermissionDecision>> requestPermissionAsync)
    {
        WorkspaceId = workspaceId;
        Provider = provider;
        _requestPermissionAsync = requestPermissionAsync;
        _host.Visibility = Visibility.Collapsed;
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal ProviderDefinition Provider { get; }

    internal string WorkspaceId { get; }

    internal UIElement View => _host;

    internal bool IsLive => _webView?.CoreWebView2 is not null;

    internal DateTimeOffset LastActivated { get; private set; }

    internal async Task ActivateAsync(CoreWebView2Environment environment, bool windowIsVisible)
    {
        ThrowIfDisposed();
        _host.Visibility = Visibility.Visible;
        LastActivated = DateTimeOffset.UtcNow;

        if (_webView?.CoreWebView2 is null)
        {
            await CreateWebViewAsync(environment, navigateToHome: true);
        }

        SetMemoryTarget(windowIsVisible
            ? CoreWebView2MemoryUsageTargetLevel.Normal
            : CoreWebView2MemoryUsageTargetLevel.Low);
    }

    internal void Deactivate()
    {
        _host.Visibility = Visibility.Collapsed;
        SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel.Low);
    }

    internal void SetWindowVisibility(bool isVisible, bool isActive)
    {
        if (isActive)
        {
            SetMemoryTarget(isVisible
                ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low);
        }
    }

    internal void Reload()
    {
        if (_webView?.CoreWebView2 is null)
        {
            return;
        }

        _webView.Reload();
    }

    internal async Task RestartAsync(CoreWebView2Environment environment, bool windowIsVisible)
    {
        ThrowIfDisposed();
        RaiseState(
            $"Restarting {Provider.DisplayName}",
            "Recreating this provider workspace with the same local profile.",
            InfoBarSeverity.Informational,
            activity: WorkspaceActivity.Opening);
        CloseWebView();
        await ActivateAsync(environment, windowIsVisible);
    }

    internal async Task ResetWebsiteDataAsync(CoreWebView2Environment environment)
    {
        ThrowIfDisposed();
        CloseWebView();
        _host.Visibility = Visibility.Collapsed;

        try
        {
            await CreateWebViewAsync(environment, navigateToHome: false);
            var profile = _webView?.CoreWebView2?.Profile
                ?? throw new InvalidOperationException("The provider profile could not be opened.");
            CloseWebView();
            await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            RaiseState(
                $"{Provider.DisplayName} website data reset",
                "Local cookies, cache, site storage, and remembered permissions were removed. Your provider account and provider-hosted conversations were not changed.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            CloseWebView();
            RaiseState(
                $"{Provider.DisplayName} data reset did not finish",
                $"Some local website data may remain ({exception.GetType().Name}).",
                InfoBarSeverity.Error,
                requiresRecovery: true);
        }
    }

    internal void DisposeView()
    {
        CloseWebView();
        _host.Visibility = Visibility.Collapsed;
    }

    internal void ReportEnvironmentFailure(Exception exception) => RaiseState(
        $"{Provider.DisplayName} could not start",
        $"The embedded browser environment could not be created ({exception.GetType().Name}). You can retry after WebView2 is available.",
        InfoBarSeverity.Error,
        requiresRecovery: true);

    private async Task CreateWebViewAsync(CoreWebView2Environment environment, bool navigateToHome)
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

            _webView = new WebView2();
            _host.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async(environment, controllerOptions);
            Configure(_webView.CoreWebView2);

            if (navigateToHome)
            {
                _webView.CoreWebView2.Navigate(Provider.HomeUri.AbsoluteUri);
            }
        }
        catch (Exception exception)
        {
            CloseWebView();
            RaiseState(
                $"{Provider.DisplayName} could not start",
                $"The embedded browser could not be initialized ({exception.GetType().Name}). You can retry without removing existing profile data.",
                InfoBarSeverity.Error,
                requiresRecovery: true);
        }
    }

    private void Configure(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.NavigationStarting += (_, args) =>
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
                OpenExternalUri(args.Uri);
                return;
            }

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

        core.NewWindowRequested += (_, args) =>
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

            OpenExternalUri(args.Uri);
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

            try
            {
                var decision = await _requestPermissionAsync(new PermissionRequest(
                    Provider.DisplayName,
                    args.PermissionKind));
                args.State = decision.Allowed
                    ? CoreWebView2PermissionState.Allow
                    : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = decision.Remember;
            }
            catch
            {
                // Deny remains the safe fallback when a native prompt cannot be shown.
            }
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            if (args.IsSuccess)
            {
                _hasCompletedInitialNavigation = true;
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
                RaiseState(
                    $"{Provider.DisplayName} stopped responding",
                    "You can reload or restart this workspace. Its local profile is preserved.",
                    InfoBarSeverity.Error,
                    requiresRecovery: true);
            }
        };
    }

    private void OpenExternalUri(string? rawUri)
    {
        var externalUri = Provider.CreateSafeExternalUri(rawUri);
        if (externalUri is null)
        {
            RaiseState(
                "Unsupported link blocked",
                "AI Drawer only opens safe HTTPS links in the system browser.",
                InfoBarSeverity.Warning);
            return;
        }

        RaiseState(
            "Opening link in your browser",
            "This link is outside the selected provider workspace. Query parameters were not forwarded.",
            InfoBarSeverity.Informational);
        _ = Launcher.LaunchUriAsync(externalUri);
    }

    private void RaisePurchaseState() => RaiseState(
        "Subscription opens on the provider's website",
        "AI Drawer does not provide or process subscriptions. Purchases stay with the provider.",
        InfoBarSeverity.Warning);

    private void SetMemoryTarget(CoreWebView2MemoryUsageTargetLevel level)
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.MemoryUsageTargetLevel = level;
        }
    }

    private bool IsCurrent(CoreWebView2 core) => ReferenceEquals(_webView?.CoreWebView2, core);

    private void RaiseState(
        string title,
        string message,
        InfoBarSeverity severity,
        bool requiresRecovery = false,
        WorkspaceActivity activity = WorkspaceActivity.None) =>
        StateChanged?.Invoke(this, new WorkspaceStateChangedEventArgs(
            WorkspaceId,
            title,
            message,
            severity,
            requiresRecovery,
            activity));

    private void CloseWebView()
    {
        _webView?.Close();
        _webView = null;
        _hasCompletedInitialNavigation = false;
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
    string ProviderName,
    CoreWebView2PermissionKind PermissionKind);

internal sealed record PermissionDecision(bool Allowed, bool Remember);

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
