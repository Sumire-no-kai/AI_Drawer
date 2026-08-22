using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.System;

namespace AIDock.CompatibilityLab;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _metricsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private WebView2? _webView;
    private CoreWebView2Environment? _environment;
    private ProviderDefinition? _currentProvider;
    private string? _profilePath;
    private DateTimeOffset _previousSampleTime;
    private TimeSpan _previousProcessorTime;

    public MainPage()
    {
        InitializeComponent();

        ProviderBox.ItemsSource = ProviderCatalog.InitialCandidates;
        ProviderBox.SelectedItem = ProviderCatalog.InitialCandidates.Single(provider => provider.Id == "gemini");

        ProfileRootBox.Text = Directory.Exists(@"D:\")
            ? @"D:\AI Dock TestData"
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AI Dock",
                "CompatibilityLab");

        UpdateSelectedProviderUi();
        _metricsTimer.Tick += MetricsTimer_Tick;
        Unloaded += MainPage_Unloaded;
    }

    private ProviderDefinition? SelectedProvider => ProviderBox.SelectedItem as ProviderDefinition;

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webView is not null)
        {
            EndTest();
            return;
        }

        var provider = SelectedProvider;
        if (provider is null)
        {
            return;
        }

        StartButton.IsEnabled = false;

        try
        {
            if (string.IsNullOrWhiteSpace(ProfileRootBox.Text))
            {
                throw new InvalidOperationException("Profile root is required.");
            }

            var root = Path.GetFullPath(ProfileRootBox.Text.Trim());
            var mode = (ProfileModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _currentProvider = provider;
            _profilePath = BuildProfilePath(root, provider, mode);

            Directory.CreateDirectory(_profilePath);
            ProfilePathText.Text = $"Profile: {_profilePath}";
            SetConfigurationEnabled(false);

            await InitializeWebViewAsync(provider, _profilePath);
            StartButton.Content = "End test";
            StartButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            CloseWebView();
            _environment = null;
            _currentProvider = null;
            _profilePath = null;
            ProfilePathText.Text = "Profile: not started";
            SetConfigurationEnabled(true);
            UpdateSelectedProviderUi();
            Log($"start-failed {exception.GetType().Name}");
            StartButton.IsEnabled = true;
        }
    }

    private static string BuildProfilePath(string root, ProviderDefinition provider, string? mode) =>
        mode == "fresh"
            ? Path.Combine(root, provider.Id, $"fresh-{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}")
            : Path.Combine(root, provider.Id, "persistent");

    private async Task InitializeWebViewAsync(ProviderDefinition provider, string profilePath)
    {
        CloseWebView();
        Log($"webview-initializing provider={provider.Id}");

        _environment ??= await CoreWebView2Environment.CreateWithOptionsAsync(
            null,
            profilePath,
            new CoreWebView2EnvironmentOptions());

        _webView = new WebView2();
        WebViewHost.Children.Clear();
        WebViewHost.Children.Add(_webView);

        await _webView.EnsureCoreWebView2Async(_environment);
        ConfigureWebView(_webView.CoreWebView2, provider);

        ReloadButton.IsEnabled = true;
        RestartButton.IsEnabled = true;
        _previousSampleTime = DateTimeOffset.UtcNow;
        _previousProcessorTime = GetTotalProcessorTime();
        _metricsTimer.Start();

        Log($"webview-ready provider={provider.Id} runtime={_environment.BrowserVersionString}");
        _webView.CoreWebView2.Navigate(provider.HomeUri.AbsoluteUri);
    }

    private void ConfigureWebView(CoreWebView2 core, ProviderDefinition provider)
    {
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.NavigationStarting += (_, args) =>
        {
            if (provider.IsKnownPurchaseUri(args.Uri))
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
            if (provider.IsKnownPurchaseUri(args.Uri))
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

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => _webView?.Reload();

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profilePath is null || _currentProvider is null)
        {
            return;
        }

        RestartButton.IsEnabled = false;
        try
        {
            await InitializeWebViewAsync(_currentProvider, _profilePath);
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

    private async void BrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = SelectedProvider;
        if (provider is not null)
        {
            await Launcher.LaunchUriAsync(provider.HomeUri);
        }
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_webView is null)
        {
            UpdateSelectedProviderUi();
        }
    }

    private void UpdateSelectedProviderUi()
    {
        var provider = SelectedProvider;
        if (provider is null)
        {
            return;
        }

        ProviderStatusText.Text = $"Provider: {provider.DisplayName} — {provider.CompatibilityStatus}";
        PurchasePolicyText.Text = provider.PurchasePolicySummary;
        EmptyStateText.Text = $"Choose a profile mode, then start {provider.DisplayName}.";
        StartButton.Content = $"Start {provider.DisplayName}";
    }

    private void SetConfigurationEnabled(bool isEnabled)
    {
        ProviderBox.IsEnabled = isEnabled;
        ProfileRootBox.IsEnabled = isEnabled;
        ProfileModeBox.IsEnabled = isEnabled;
    }

    private void EndTest()
    {
        CloseWebView();
        _environment = null;
        _currentProvider = null;
        _profilePath = null;
        ProfilePathText.Text = "Profile: not started";
        SetConfigurationEnabled(true);
        UpdateSelectedProviderUi();
        Log("webview-closed");
    }

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
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // A WebView process can exit or become inaccessible between the snapshot and inspection.
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
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // A WebView process can exit or become inaccessible between the snapshot and inspection.
            }
        }

        return total;
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _metricsTimer.Stop();
        CloseWebView();
        _environment = null;
    }

    private void CloseWebView()
    {
        _metricsTimer.Stop();
        _webView?.Close();
        _webView = null;
        ReloadButton.IsEnabled = false;
        RestartButton.IsEnabled = false;
        if (WebViewHost is not null)
        {
            WebViewHost.Children.Clear();
            WebViewHost.Children.Add(EmptyStateText);
        }
    }

    private void Log(string message)
    {
        EventLogBox.Text += $"[{DateTimeOffset.Now:HH:mm:ss}] {message}\r\n";
        EventLogBox.Select(EventLogBox.Text.Length, 0);
    }
}
