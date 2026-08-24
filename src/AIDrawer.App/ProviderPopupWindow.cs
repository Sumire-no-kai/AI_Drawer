using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace AIDrawer;

/// <summary>
/// A provider-requested top-level window that deliberately shares the originating provider profile.
/// It is not a persisted workspace and never receives a restore locator.
/// </summary>
internal sealed class ProviderPopupWindow : IDisposable
{
    private readonly Window _window = new();
    private readonly WebView2 _webView = new();
    private readonly ProviderDefinition _provider;
    private readonly Action<string, string, InfoBarSeverity> _reportState;
    private readonly Action<NavigationPromptKind, Uri?> _requestNavigationPrompt;
    private readonly Action<ProviderPopupWindow> _closed;
    private bool _disposed;

    private ProviderPopupWindow(
        ProviderDefinition provider,
        ControlledPopupKind popupKind,
        Action<string, string, InfoBarSeverity> reportState,
        Action<NavigationPromptKind, Uri?> requestNavigationPrompt,
        Action<ProviderPopupWindow> closed)
    {
        _provider = provider;
        _reportState = reportState;
        _requestNavigationPrompt = requestNavigationPrompt;
        _closed = closed;
        _window.Title = popupKind == ControlledPopupKind.Authentication
            ? $"{provider.DisplayName} sign-in"
            : provider.DisplayName;
        _window.Content = _webView;
        _window.AppWindow.Resize(new SizeInt32(560, 720));
        _window.AppWindow.Closing += Window_Closing;
    }

    internal CoreWebView2 CoreWebView => _webView.CoreWebView2
        ?? throw new InvalidOperationException("The provider popup has not been initialized.");

    internal static async Task<ProviderPopupWindow?> CreateAsync(
        CoreWebView2Environment environment,
        ProviderDefinition provider,
        ControlledPopupKind popupKind,
        Action<string, string, InfoBarSeverity> reportState,
        Action<NavigationPromptKind, Uri?> requestNavigationPrompt,
        Action<ProviderPopupWindow> closed)
    {
        var popup = new ProviderPopupWindow(provider, popupKind, reportState, requestNavigationPrompt, closed);
        try
        {
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.ProfileName = provider.ProfileName;
            await popup._webView.EnsureCoreWebView2Async(environment, controllerOptions);
            popup.Configure(popup.CoreWebView);
            popup._window.Activate();
            return popup;
        }
        catch
        {
            popup.Dispose();
            return null;
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
            if (_disposed)
            {
                args.Cancel = true;
                return;
            }

            if (string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            switch (_provider.ClassifyTopLevelNavigation(args.Uri))
            {
                case NavigationDisposition.BlockPurchase:
                    args.Cancel = true;
                    ReportPurchaseBlocked();
                    return;

                case NavigationDisposition.OpenExternal:
                    args.Cancel = true;
                    RequestExternalNavigation(args.Uri);
                    return;

                case NavigationDisposition.BlockUnsupported:
                    args.Cancel = true;
                    _reportState(
                        "Unsupported link blocked",
                        "AI Drawer only embeds reviewed HTTPS provider origins and opens safe HTTPS external links in the system browser.",
                        InfoBarSeverity.Warning);
                    return;
            }
        };

        core.FrameNavigationStarting += (sender, args) =>
        {
            if (_disposed)
            {
                args.Cancel = true;
                return;
            }

            if (string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            switch (_provider.ClassifyTopLevelNavigation(args.Uri))
            {
                case NavigationDisposition.BlockPurchase:
                    args.Cancel = true;
                    ReportPurchaseBlocked();
                    return;

                case NavigationDisposition.OpenExternal:
                    args.Cancel = true;
                    ReportExternalFrameNavigationBlocked();
                    return;

                case NavigationDisposition.BlockUnsupported:
                    args.Cancel = true;
                    _reportState(
                        "Unsupported embedded navigation blocked",
                        "AI Drawer only embeds reviewed HTTPS provider and authentication origins.",
                        InfoBarSeverity.Warning);
                    return;
            }
        };

        core.ServerCertificateErrorDetected += (_, args) =>
        {
            args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            if (_disposed)
            {
                return;
            }

            _reportState(
                "Secure connection blocked",
                $"Windows could not verify the certificate for {_provider.DisplayName}. AI Drawer blocked this connection.",
                InfoBarSeverity.Error);
        };

        core.NewWindowRequested += (sender, args) =>
        {
            if (_disposed)
            {
                args.Handled = true;
                return;
            }

            switch (_provider.ClassifyPopup(args.Uri))
            {
                case PopupDisposition.BlockPurchase:
                    args.Handled = true;
                    ReportPurchaseBlocked();
                    return;

                case PopupDisposition.OpenExternal:
                    args.Handled = true;
                    RequestExternalNavigation(args.Uri);
                    return;

                case PopupDisposition.OpenControlledProviderWindow:
                case PopupDisposition.OpenControlledAuthenticationWindow:
                    args.Handled = true;
                    _reportState(
                        "Additional provider popup blocked",
                        "AI Drawer keeps one controlled provider popup at a time. Return to the provider page or use the system browser for unrelated links.",
                        InfoBarSeverity.Warning);
                    return;

                default:
                    args.Handled = true;
                    _reportState(
                        "Unsupported popup blocked",
                        "AI Drawer only opens safe HTTPS external links in the system browser.",
                        InfoBarSeverity.Warning);
                    return;
            }
        };

        core.PermissionRequested += (_, args) =>
        {
            args.Handled = true;
            args.State = CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
        };
    }

    private void RequestExternalNavigation(string? rawUri)
    {
        var uri = _provider.CreateSafeExternalUri(rawUri);
        if (uri is null)
        {
            _reportState(
                "Unsupported link blocked",
                "AI Drawer only opens reviewed HTTPS provider origins. This link was not opened.",
                InfoBarSeverity.Warning);
            return;
        }

        _reportState(
            "External link needs confirmation",
            "AI Drawer did not open this unreviewed origin. You can choose whether to open a sanitized link in your browser.",
            InfoBarSeverity.Informational);
        _requestNavigationPrompt(NavigationPromptKind.ExternalLink, uri);
    }

    private void ReportExternalFrameNavigationBlocked() => _reportState(
        "External embedded navigation blocked",
        "AI Drawer only embeds reviewed provider and authentication origins. This external frame was not opened.",
        InfoBarSeverity.Warning);

    private void ReportPurchaseBlocked()
    {
        _reportState(
            "Subscription and purchase blocked",
            "AI Drawer does not provide or process subscriptions, billing, cancellations, refunds, or payment information.",
            InfoBarSeverity.Warning);
        _requestNavigationPrompt(NavigationPromptKind.PurchaseBlocked, null);
    }

    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.AppWindow.Closing -= Window_Closing;
        try
        {
            _webView.Close();
        }
        catch
        {
            // The popup renderer can already be gone during close.
        }

        _closed(this);
    }
}

internal enum ControlledPopupKind
{
    ProviderApplication,
    Authentication
}
