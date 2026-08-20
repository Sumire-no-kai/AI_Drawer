using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace AIDock.CompatibilityLab;

public sealed partial class MainPage : Page
{
    private static readonly Uri GeminiUri = new("https://gemini.google.com/");

    private readonly DispatcherTimer _metricsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WebView2? _webView;
    private CoreWebView2Environment? _environment;
    private string? _profilePath;
    private DateTimeOffset _previousSampleTime;
    private TimeSpan _previousProcessorTime;

    public MainPage()
    {
        InitializeComponent();

        ProfileRootBox.Text = Directory.Exists(@"D:\")
            ? @"D:\AI Dock TestData\Gemini"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AI Dock",
                "CompatibilityLab",
                "Gemini");

        _metricsTimer.Tick += MetricsTimer_Tick;
        Unloaded += MainPage_Unloaded;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;

        try
        {
            var root = Path.GetFullPath(ProfileRootBox.Text.Trim());
            var mode = (ProfileModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _profilePath = mode == "fresh"
                ? Path.Combine(root, $"fresh-{DateTimeOffset.Now:yyyyMMdd-HHmmss}")
                : Path.Combine(root, "persistent");

            Directory.CreateDirectory(_profilePath);
            ProfilePathText.Text = $"Profile: {_profilePath}";
            ProfileRootBox.IsEnabled = false;
            ProfileModeBox.IsEnabled = false;

            await InitializeWebViewAsync(_profilePath);
        }
        catch (Exception exception)
        {
            Log($"start-failed {exception.GetType().Name}");
            StartButton.IsEnabled = true;
        }
    }

    private async Task InitializeWebViewAsync(string profilePath)
    {
        CloseWebView();
        Log("webview-initializing");

        _environment ??= await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            profilePath,
            new CoreWebView2EnvironmentOptions());

        _webView = new WebView2();
        WebViewHost.Children.Clear();
        WebViewHost.Children.Add(_webView);

        await _webView.EnsureCoreWebView2Async(_environment);
        ConfigureWebView(_webView.CoreWebView2);

        ReloadButton.IsEnabled = true;
        RestartButton.IsEnabled = true;
        _previousSampleTime = DateTimeOffset.UtcNow;
        _previousProcessorTime = GetTotalProcessorTime();
        _metricsTimer.Start();

        Log($"webview-ready runtime={_environment.BrowserVersionString}");
        _webView.CoreWebView2.Navigate(GeminiUri.AbsoluteUri);
    }

    private void ConfigureWebView(CoreWebView2 core)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.NavigationStarting += (_, args) =>
        {
            if (IsKnownPurchaseUri(args.Uri))
            {
                args.Cancel = true;
                Log($"purchase-navigation-blocked {SafeEventText.Origin(args.Uri)}");
                return;
            }

            Log($"navigation-start {SafeEventText.Origin(args.Uri)}");
        };

        core.NavigationCompleted += (_, args) =>
            Log(args.IsSuccess
                ? "navigation-complete success"
                : $"navigation-complete failure={args.WebErrorStatus}");

        core.NewWindowRequested += (_, args) =>
        {
            if (IsKnownPurchaseUri(args.Uri))
            {
                args.Handled = true;
                Log($"purchase-popup-blocked {SafeEventText.Origin(args.Uri)}");
                return;
            }

            Log($"popup-request {SafeEventText.Origin(args.Uri)}");
        };

        core.PermissionRequested += (_, args) =>
            Log($"permission-request kind={args.PermissionKind} origin={SafeEventText.Origin(args.Uri)}");

        core.DownloadStarting += (_, _) => Log("download-starting");
        core.ProcessFailed += (_, args) =>
            Log($"process-failed kind={args.ProcessFailedKind} reason={args.Reason}");
    }

    private static bool IsKnownPurchaseUri(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.IdnHost;
        if (host is "pay.google.com" or "payments.google.com" or "one.google.com")
        {
            return true;
        }

        if (host != "gemini.google.com")
        {
            return false;
        }

        return uri.AbsolutePath.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("advanced", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("subscription", StringComparison.OrdinalIgnoreCase);
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => _webView?.Reload();

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profilePath is null)
        {
            return;
        }

        RestartButton.IsEnabled = false;
        try
        {
            await InitializeWebViewAsync(_profilePath);
        }
        catch (Exception exception)
        {
            Log($"restart-failed {exception.GetType().Name}");
        }
        finally
        {
            RestartButton.IsEnabled = _webView?.CoreWebView2 is not null;
        }
    }

    private async void BrowserButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(GeminiUri);

    private void ClearLogButton_Click(object sender, RoutedEventArgs e) => EventLogBox.Text = string.Empty;

    private void MetricsTimer_Tick(object? sender, object e)
    {
        if (_webView?.CoreWebView2 is null)
        {
            return;
        }

        var processInfos = _environment?.GetProcessInfos() ?? [];
        var memoryBytes = 0L;
        foreach (var info in processInfos)
        {
            try
            {
                using var process = Process.GetProcessById(info.ProcessId);
                memoryBytes += process.PrivateMemorySize64;
            }
            catch (ArgumentException)
            {
                // A WebView process can exit between the snapshot and inspection.
            }
        }

        var now = DateTimeOffset.UtcNow;
        var processorTime = GetTotalProcessorTime();
        var elapsed = now - _previousSampleTime;
        var cpu = elapsed.TotalMilliseconds <= 0
            ? 0
            : (processorTime - _previousProcessorTime).TotalMilliseconds
                / elapsed.TotalMilliseconds
                / Environment.ProcessorCount
                * 100;

        ResourceText.Text = $"WebView processes: {processInfos.Count}\n"
            + $"Private memory: {memoryBytes / 1024d / 1024d:F1} MB\n"
            + $"CPU: {Math.Max(0, cpu):F1}%";

        _previousSampleTime = now;
        _previousProcessorTime = processorTime;
    }

    private TimeSpan GetTotalProcessorTime()
    {
        if (_webView?.CoreWebView2 is null)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        foreach (var info in _environment?.GetProcessInfos() ?? [])
        {
            try
            {
                using var process = Process.GetProcessById(info.ProcessId);
                total += process.TotalProcessorTime;
            }
            catch (ArgumentException)
            {
                // A WebView process can exit between the snapshot and inspection.
            }
        }

        return total;
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _metricsTimer.Stop();
        CloseWebView();
    }

    private void CloseWebView()
    {
        _metricsTimer.Stop();
        _webView?.Close();
        _webView = null;
        WebViewHost?.Children.Clear();
    }

    private void Log(string message)
    {
        EventLogBox.Text += $"[{DateTimeOffset.Now:HH:mm:ss}] {message}\r\n";
        EventLogBox.Select(EventLogBox.Text.Length, 0);
    }
}
