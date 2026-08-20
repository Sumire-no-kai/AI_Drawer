using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace AIDrawer;

internal sealed class GeminiWorkspace : IDisposable
{
    private static readonly IReadOnlySet<string> PurchaseHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "one.google.com",
        "pay.google.com",
        "payments.google.com"
    };

    private static readonly IReadOnlyList<string> PurchasePathFragments = ["upgrade", "advanced", "subscription"];
    private readonly Panel _host;
    private CoreWebView2Environment? _environment;
    private WebView2? _webView;
    private bool _disposed;

    internal GeminiWorkspace(Panel host)
    {
        _host = host;
    }

    internal event EventHandler<WorkspaceStateChangedEventArgs>? StateChanged;

    internal Task StartAsync() => InitializeAsync();

    internal async Task RestartAsync()
    {
        RaiseState("Restarting Gemini", "Recreating the workspace with the same local profile.", InfoBarSeverity.Informational);
        await InitializeAsync();
    }

    internal void Reload()
    {
        if (_webView?.CoreWebView2 is null)
        {
            _ = RestartAsync();
            return;
        }

        RaiseState("Reloading Gemini", "Your provider session stays in its local AI Drawer profile.", InfoBarSeverity.Informational);
        _webView.Reload();
    }

    private async Task InitializeAsync()
    {
        ThrowIfDisposed();
        CloseWebView();
        RaiseState("Loading Gemini", "Preparing your private AI Drawer workspace.", InfoBarSeverity.Informational);

        try
        {
            Directory.CreateDirectory(ProfilePath);
            _environment ??= await CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                ProfilePath,
                new CoreWebView2EnvironmentOptions());

            _webView = new WebView2();
            _host.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async(_environment);
            Configure(_webView.CoreWebView2);
            _webView.CoreWebView2.Navigate("https://gemini.google.com/");
        }
        catch (Exception exception)
        {
            CloseWebView();
            RaiseState(
                "Gemini could not start",
                $"The embedded browser could not be initialized ({exception.GetType().Name}). You can retry without losing any existing profile data.",
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
            if (!IsCurrent(core))
            {
                return;
            }

            if (!IsKnownPurchaseUri(args.Uri))
            {
                return;
            }

            args.Cancel = true;
            RaiseState(
                "Subscription opens on Gemini's website",
                "AI Drawer does not provide or process subscriptions. Purchases stay with the provider.",
                InfoBarSeverity.Warning);
        };

        core.NewWindowRequested += (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            if (!IsKnownPurchaseUri(args.Uri))
            {
                return;
            }

            args.Handled = true;
            RaiseState(
                "Subscription opens on Gemini's website",
                "AI Drawer does not provide or process subscriptions. Purchases stay with the provider.",
                InfoBarSeverity.Warning);
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            if (args.IsSuccess)
            {
                RaiseState("Gemini", "Your Gemini session is managed by Google in this local AI Drawer profile.", InfoBarSeverity.Success);
                return;
            }

            RaiseState(
                "Gemini did not finish loading",
                "Check your connection, then reload or restart this workspace.",
                InfoBarSeverity.Warning,
                requiresRecovery: true);
        };

        core.ProcessFailed += (_, args) =>
        {
            if (!IsCurrent(core))
            {
                return;
            }

            RaiseState(
                "Gemini stopped responding",
                "You can keep waiting, reload, or restart this workspace. Your local profile is preserved.",
                InfoBarSeverity.Error,
                requiresRecovery: true);
        };
    }

    private static bool IsKnownPurchaseUri(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (PurchaseHosts.Contains(uri.IdnHost))
        {
            return true;
        }

        return string.Equals(uri.IdnHost, "gemini.google.com", StringComparison.OrdinalIgnoreCase)
            && PurchasePathFragments.Any(fragment => uri.AbsolutePath.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string ProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI Drawer",
        "WebView2",
        "gemini");

    private bool IsCurrent(CoreWebView2 core) => ReferenceEquals(_webView?.CoreWebView2, core);

    private void RaiseState(string title, string message, InfoBarSeverity severity, bool requiresRecovery = false) =>
        StateChanged?.Invoke(this, new WorkspaceStateChangedEventArgs(title, message, severity, requiresRecovery));

    private void CloseWebView()
    {
        _webView?.Close();
        _webView = null;
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
        _environment = null;
    }
}

internal sealed class WorkspaceStateChangedEventArgs(
    string title,
    string message,
    InfoBarSeverity severity,
    bool requiresRecovery = false) : EventArgs
{
    internal string Title { get; } = title;
    internal string Message { get; } = message;
    internal InfoBarSeverity Severity { get; } = severity;
    internal bool RequiresRecovery { get; } = requiresRecovery;
}
