using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.System;

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
    private readonly Action<ProviderPopupWindow> _closed;
    private bool _disposed;

    private ProviderPopupWindow(
        ProviderDefinition provider,
        Action<string, string, InfoBarSeverity> reportState,
        Action<ProviderPopupWindow> closed)
    {
        _provider = provider;
        _reportState = reportState;
        _closed = closed;
        _window.Title = $"{provider.DisplayName} sign-in";
        _window.Content = _webView;
        _window.AppWindow.Resize(new SizeInt32(560, 720));
        _window.AppWindow.Closing += Window_Closing;
    }

    internal CoreWebView2 CoreWebView => _webView.CoreWebView2
        ?? throw new InvalidOperationException("The provider popup has not been initialized.");

    internal static async Task<ProviderPopupWindow?> CreateAsync(
        CoreWebView2Environment environment,
        ProviderDefinition provider,
        Action<string, string, InfoBarSeverity> reportState,
        Action<ProviderPopupWindow> closed)
    {
        var popup = new ProviderPopupWindow(provider, reportState, closed);
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
            switch (_provider.ClassifyTopLevelNavigation(args.Uri))
            {
                case NavigationDisposition.BlockPurchase:
                    args.Cancel = true;
                    _reportState(
                        "Subscription opens on the provider's website",
                        "AI Drawer does not provide or process subscriptions. Purchases stay with the provider.",
                        InfoBarSeverity.Warning);
                    return;

                case NavigationDisposition.OpenExternal:
                    args.Cancel = true;
                    _ = OpenExternalUriAsync(args.Uri);
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

        core.ServerCertificateErrorDetected += (_, args) =>
        {
            args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            _reportState(
                "Secure connection blocked",
                $"Windows could not verify the certificate for {_provider.DisplayName}. AI Drawer blocked this connection.",
                InfoBarSeverity.Error);
        };

        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            _reportState(
                "Additional provider popup blocked",
                "AI Drawer keeps one controlled provider popup at a time. Return to the provider page or use the system browser for unrelated links.",
                InfoBarSeverity.Warning);
        };

        core.PermissionRequested += (_, args) =>
        {
            args.Handled = true;
            args.State = CoreWebView2PermissionState.Deny;
            args.SavesInProfile = false;
        };
    }

    private async Task OpenExternalUriAsync(string? rawUri)
    {
        var uri = _provider.CreateSafeExternalUri(rawUri);
        if (uri is null)
        {
            _reportState("Unsupported link blocked", "AI Drawer only opens safe HTTPS links in the system browser.", InfoBarSeverity.Warning);
            return;
        }

        _reportState(
            "Opening link in your browser",
            "This popup link is outside the selected provider. Query parameters were not forwarded.",
            InfoBarSeverity.Informational);
        try
        {
            await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            _reportState("Link could not be opened", "Windows could not open this HTTPS link.", InfoBarSeverity.Warning);
        }
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
